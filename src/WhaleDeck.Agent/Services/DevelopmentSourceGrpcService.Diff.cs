using System.Text;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class DevelopmentSourceGrpcService
{
    public override async Task<WorkspaceSourceDiffPage> ReadDiff(WorkspaceSourceDiffRequest request, ServerCallContext context)
    {
        if (!DevelopmentSourceDiffBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_REQUEST_INVALID");
        var unit = Guid.NewGuid().ToString("N");
        try
        {
            var output = await RunHostAsync(DevelopmentSourceDiffBinding.HostInput(request), "diff", unit,
                null, 110, context.CancellationToken);
            return ParseDiff(request, output);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.DeadlineExceeded)
        {
            await StopReadAsync("diff", unit);
            throw;
        }
    }

    internal static WorkspaceSourceDiffPage ParseDiff(WorkspaceSourceDiffRequest request, string output)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(output) > DevelopmentSourceDiffBinding.MaximumHostPageBytes) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.EnumerateObject().Count() != 9) throw new JsonException();
            var page = new WorkspaceSourceDiffPage
            {
                WorkspaceId = root.GetProperty("workspaceId").GetString()!,
                ImportId = root.GetProperty("importId").GetString()!,
                PreviousImportId = root.GetProperty("previousImportId").GetString()!,
                PlanSha256 = root.GetProperty("planSha256").GetString()!,
                Offset = root.GetProperty("offset").GetUInt32(),
                VisibleEntries = root.GetProperty("visibleEntries").GetUInt32(),
                RedactedEntries = root.GetProperty("redactedEntries").GetUInt32()
            };
            var next = root.GetProperty("nextOffset");
            if (next.ValueKind != JsonValueKind.Null) page.NextOffset = next.GetUInt32();
            foreach (var row in root.GetProperty("entries").EnumerateArray())
            {
                if (page.Entries.Count >= request.Limit || row.EnumerateObject().Count() != 4) throw new JsonException();
                page.Entries.Add(new WorkspaceSourceDiffEntry
                {
                    Path = row.GetProperty("path").GetString()!,
                    Change = row.GetProperty("change").GetString() switch
                    {
                        "added" => WorkspaceSourceDiffChange.Added,
                        "removed" => WorkspaceSourceDiffChange.Removed,
                        "changed" => WorkspaceSourceDiffChange.Changed,
                        _ => throw new JsonException()
                    },
                    Before = ParseDiffNode(row.GetProperty("before")),
                    After = ParseDiffNode(row.GetProperty("after"))
                });
            }
            return DevelopmentSourceDiffBinding.Matches(request, page) ? page : throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_SOURCE_DIFF_RECEIPT_INVALID"); }
    }

    private static WorkspaceSourceDiffNode? ParseDiffNode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Null) return null;
        var count = node.EnumerateObject().Count();
        return node.GetProperty("kind").GetString() switch
        {
            "Directory" when count == 1 => new() { Kind = WorkspaceSourceDiffKind.Directory },
            "File" when count == 3 => new()
            {
                Kind = WorkspaceSourceDiffKind.File,
                SizeBytes = node.GetProperty("sizeBytes").GetUInt64(),
                Executable = node.GetProperty("executable").GetBoolean()
            },
            _ => throw new JsonException()
        };
    }
}
