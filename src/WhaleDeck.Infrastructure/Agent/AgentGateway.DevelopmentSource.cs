using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IWorkspaceSourceGateway, IWorkspaceSourceDownloadGateway, IWorkspaceSourceDiffGateway
{
    public async Task<WorkspaceSourceDiffPageView> ReadDiffAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, int offset, int limit, CancellationToken token)
    {
        const string method = DevelopmentSourceBinding.ReadDiff;
        var request = new WorkspaceSourceDiffRequest
        {
            Source = SourceRequest(import, workspace, actor, method, import.Plan),
            Offset = (uint)offset, Limit = (uint)limit
        };
        if (!DevelopmentSourceDiffBinding.IsValid(request) || import.PreviousImportId is not { } previous ||
            import.Plan?.Update is not { } update)
            throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
        try
        {
            var client = await SourceClientAsync(true, token, diff: true);
            var headers = SourceHeaders(method, DevelopmentSourceDiffBinding.Hash(request), DevelopmentSourceBinding.Payload);
            var page = await client.ReadDiffAsync(request, headers, DateTime.UtcNow.AddSeconds(112), token).ResponseAsync;
            if (!DevelopmentSourceDiffBinding.Matches(request, page) ||
                page.VisibleEntries + page.RedactedEntries != update.AddedEntries + update.RemovedEntries + update.ChangedEntries)
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            return new(workspace.Id, import.ImportId, previous, page.PlanSha256, offset,
                page.HasNextOffset ? (int)page.NextOffset : null, (int)page.VisibleEntries, (int)page.RedactedEntries,
                page.Entries.Select(entry => new WorkspaceSourceDiffEntryView(entry.Path,
                    entry.Change.ToString().ToLowerInvariant(), Node(entry.Before), Node(entry.After))).ToArray());
        }
        catch (RpcException error) { token.ThrowIfCancellationRequested(); throw SourceFailure(error); }

        static WorkspaceSourceDiffNodeView? Node(WorkspaceSourceDiffNode? node) => node?.Kind switch
        {
            WorkspaceSourceDiffKind.File => new("File", (long)node.SizeBytes, node.Executable),
            WorkspaceSourceDiffKind.Directory => new("Directory", null, null),
            _ => null
        };
    }

    public async Task<ReadOnlyMemory<byte>> ExportPreviousAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken token)
    {
        const string method = DevelopmentSourceBinding.ExportPrevious;
        var request = SourceRequest(import, workspace, actor, method, import.Plan);
        try
        {
            var client = await SourceClientAsync(true, token, previous: true);
            using var call = client.ExportPrevious(request, SourceHeaders(method, request), DateTime.UtcNow.AddSeconds(112), token);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ulong count = 0;
            var pending = ReadOnlyMemory<byte>.Empty;
            var ended = false;
            while (await call.ResponseStream.MoveNext(token))
            {
                var frame = call.ResponseStream.Current;
                if (ended || frame.Offset != count)
                    throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
                if (frame.PayloadCase == WorkspaceSourceExportFrame.PayloadOneofCase.Content &&
                    frame.Content.Length is > 0 and <= DevelopmentSourceBinding.ChunkBytes &&
                    (ulong)frame.Content.Length <= DevelopmentSourceBinding.MaximumPreviousArchiveBytes - count)
                {
                    if (!pending.IsEmpty) await write(pending, token);
                    pending = frame.Content.Memory;
                    count += (ulong)pending.Length;
                    digest.AppendData(pending.Span);
                }
                else if (frame.PayloadCase == WorkspaceSourceExportFrame.PayloadOneofCase.ArchiveSha256 &&
                    count >= 10240 && count % 512 == 0 &&
                    frame.ArchiveSha256 == Convert.ToHexStringLower(digest.GetHashAndReset()))
                    ended = true;
                else throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            }
            // A valid footer must be followed by a successful RPC status.
            return ended ? pending : throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
        }
        catch (RpcException error) { token.ThrowIfCancellationRequested(); throw SourceFailure(error); }
    }

    public async Task<WorkspaceSourceObservation> StageSourceAsync(WorkspaceSourceClaim claim,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, CancellationToken token)
    {
        var update = claim.Import.PreviousImportId is not null;
        var method = update ? DevelopmentSourceBinding.StageUpdate : DevelopmentSourceBinding.Stage;
        var request = SourceRequest(claim, method);
        try
        {
            var client = await SourceClientAsync(update, token);
            var headers = SourceHeaders(method, request);
            var deadline = DateTime.UtcNow.AddSeconds(112);
            using var call = update ? client.StageUpdate(headers, deadline, token) : client.Stage(headers, deadline, token);
            await call.RequestStream.WriteAsync(new() { Header = request }, token);
            await foreach (var chunk in chunks.WithCancellation(token))
            {
                for (var offset = 0; offset < chunk.Length; offset += DevelopmentSourceBinding.ChunkBytes)
                {
                    var count = Math.Min(DevelopmentSourceBinding.ChunkBytes, chunk.Length - offset);
                    await call.RequestStream.WriteAsync(new() { Content = ByteString.CopyFrom(chunk.Span.Slice(offset, count)) }, token);
                }
            }
            await call.RequestStream.CompleteAsync();
            return SourceObservation(request, await call.ResponseAsync, method);
        }
        catch (RpcException error) { token.ThrowIfCancellationRequested(); throw SourceFailure(error); }
    }

    public Task<WorkspaceSourceObservation> InspectSourceAsync(WorkspaceSourceClaim claim, CancellationToken token) =>
        ObserveSourceAsync(claim, null, claim.Import.PreviousImportId is null ? DevelopmentSourceBinding.Inspect : DevelopmentSourceBinding.InspectUpdate, token);

    public Task<WorkspaceSourceObservation> PublishSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token) =>
        ObserveSourceAsync(claim, plan, claim.Import.PreviousImportId is null ? DevelopmentSourceBinding.Publish : DevelopmentSourceBinding.PublishUpdate, token);

    public Task<WorkspaceSourceObservation> DiscardSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token) =>
        ObserveSourceAsync(claim, plan, claim.Import.PreviousImportId is null ? DevelopmentSourceBinding.Discard : DevelopmentSourceBinding.DiscardUpdate, token);

    public async Task AbortSourceAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        var update = claim.Import.PreviousImportId is not null;
        var method = update ? DevelopmentSourceBinding.AbortUpdate : DevelopmentSourceBinding.Abort;
        var request = SourceRequest(claim, method);
        try
        {
            var client = await SourceClientAsync(update, token);
            var headers = SourceHeaders(method, request);
            var deadline = DateTime.UtcNow.AddSeconds(22);
            using var call = update ? client.AbortUpdateAsync(request, headers, deadline, token) : client.AbortAsync(request, headers, deadline, token);
            await call.ResponseAsync;
        }
        catch (RpcException error) { token.ThrowIfCancellationRequested(); throw SourceFailure(error); }
    }

    private async Task<WorkspaceSourceObservation> ObserveSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan? plan, string method,
        CancellationToken token)
    {
        var request = SourceRequest(claim, method, plan);
        try
        {
            var client = await SourceClientAsync(DevelopmentSourceBinding.IsUpdate(method), token);
            var headers = SourceHeaders(method, request);
            var deadline = DateTime.UtcNow.AddSeconds(method == DevelopmentSourceBinding.PublishUpdate ? 112 : 22);
            using var call = method switch
            {
                DevelopmentSourceBinding.Inspect => client.InspectAsync(request, headers, deadline, token),
                DevelopmentSourceBinding.Publish => client.PublishAsync(request, headers, deadline, token),
                DevelopmentSourceBinding.Discard => client.DiscardAsync(request, headers, deadline, token),
                DevelopmentSourceBinding.InspectUpdate => client.InspectUpdateAsync(request, headers, deadline, token),
                DevelopmentSourceBinding.PublishUpdate => client.PublishUpdateAsync(request, headers, deadline, token),
                _ => client.DiscardUpdateAsync(request, headers, deadline, token)
            };
            var result = SourceObservation(request, await call.ResponseAsync, method);
            if (plan is not null && result.Plan != plan) throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            return result;
        }
        catch (RpcException error) { token.ThrowIfCancellationRequested(); throw SourceFailure(error); }
    }

    private async Task<DevelopmentSourceService.DevelopmentSourceServiceClient> SourceClientAsync(bool update, CancellationToken token,
        bool previous = false, bool diff = false)
    {
        var caps = await _agent.GetCapabilitiesAsync(new Google.Protobuf.WellKnownTypes.Empty(),
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        if (!DevelopmentSourceBinding.IsSupported(caps) || caps.ProtocolMinor < DevelopmentSourceBinding.AbortProtocolMinor ||
            update && !DevelopmentSourceBinding.SupportsUpdate(caps) ||
            previous && !DevelopmentSourceBinding.SupportsPrevious(caps) ||
            diff && !DevelopmentSourceBinding.SupportsDiff(caps))
            throw new WorkspaceSourceException(WorkspaceSourceFailure.Unsupported);
        return new(_channel);
    }

    private static WorkspaceSourceRequest SourceRequest(WorkspaceSourceClaim claim, string method, WorkspaceSourcePlan? plan = null) =>
        SourceRequest(claim.Import, claim.Workspace, claim.Actor, method, plan);

    private static WorkspaceSourceRequest SourceRequest(WorkspaceSourceView import, WorkspaceDefinition workspace,
        TerminalActor actor, string method, WorkspaceSourcePlan? plan)
    {
        if (import.WorkspaceId != workspace.Id || workspace.OwnerSubject != actor.Subject ||
            workspace.State != WorkspaceStorageAllocation.Allocated || import.Archive.ArchiveBytes <= 0)
            throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
        var request = new WorkspaceSourceRequest
        {
            Storage = SourceStorage(workspace, actor),
            ImportId = import.ImportId.ToString("N"), ArchiveFormat = import.Archive.Format,
            ArchiveBytes = (uint)import.Archive.ArchiveBytes, ArchiveSha256 = import.Archive.ArchiveSha256,
            PlanSha256 = plan?.PlanSha256 ?? "", PreviousImportId = import.PreviousImportId?.ToString("N") ?? "",
            ConflictResolution = method is not (DevelopmentSourceBinding.PublishUpdate or DevelopmentSourceBinding.ExportPrevious)
                ? WorkspaceSourceConflictResolution.Unspecified :
                import.ConflictResolution switch
                {
                    WorkspaceSources.CleanOnly => WorkspaceSourceConflictResolution.CleanOnly,
                    WorkspaceSources.RetainAndReplace => WorkspaceSourceConflictResolution.RetainAndReplace,
                    _ => WorkspaceSourceConflictResolution.Unspecified
                }
        };
        if (!DevelopmentSourceBinding.IsValid(request, method))
            throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
        return request;
    }

    private Metadata SourceHeaders(string method, WorkspaceSourceRequest request)
    {
        var hash = DevelopmentSourceBinding.Hash(request);
        var headers = SourceHeaders(method, hash, DevelopmentSourceBinding.Payload);
        if (DevelopmentSourceBinding.IsStage(method)) headers.Add(DevelopmentSourceBinding.Header, hash);
        return headers;
    }

    private Metadata SourceHeaders(string method, string hash, Func<string, string, string, string, string> bind)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new WorkspaceSourceException(WorkspaceSourceFailure.Unsupported);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var payload = bind(expires, nonce, method, hash);
        var signature = Convert.ToHexString(HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(payload)));
        return new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{signature}" } };
    }

    private static WorkspaceAllocationRequest SourceStorage(WorkspaceDefinition workspace, TerminalActor actor) => new()
    {
        WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
        EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
        ActorSubject = actor.Subject, TraceId = actor.TraceId
    };

    private static WorkspaceSourceObservation SourceObservation(WorkspaceSourceRequest request, WorkspaceSourceReceipt receipt, string method) =>
        DevelopmentSourceBinding.Matches(request, receipt, method)
            ? new(receipt.State, new(receipt.PlanSha256, receipt.ContentSha256, (int)receipt.FileCount, (long)receipt.TotalBytes,
                receipt.Update is { } update ? new(update.PreviousContentSha256, update.WorkContentSha256, update.Dirty,
                    (int)update.AddedEntries, (int)update.RemovedEntries, (int)update.ChangedEntries) : null))
            : throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);

    private static WorkspaceSourceException SourceFailure(RpcException error) => new(error.StatusCode switch
    {
        StatusCode.NotFound => WorkspaceSourceFailure.Missing,
        StatusCode.InvalidArgument => WorkspaceSourceFailure.Rejected,
        StatusCode.ResourceExhausted when error.Status.Detail == "WORKSPACE_SOURCE_CAPACITY" => WorkspaceSourceFailure.Rejected,
        StatusCode.Aborted => WorkspaceSourceFailure.Conflict,
        StatusCode.DataLoss => WorkspaceSourceFailure.Integrity,
        StatusCode.Unimplemented or StatusCode.PermissionDenied => WorkspaceSourceFailure.Unsupported,
        _ => WorkspaceSourceFailure.Retryable
    }, error.Status.Detail);
}
