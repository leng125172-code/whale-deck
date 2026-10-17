using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileMutationGateway
{
    private const string MutationsMethod = "/whaledeck.agent.v1.FileOperationService/";
    private async Task<FileOperationService.FileOperationServiceClient> Mutations(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("files.mutation-tombstones"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_MUTATION_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<FileMutationPlan> PlanMutationAsync(TerminalActor actor, FileMutationCommand command, CancellationToken cancellationToken)
    {
        var client = await Mutations(cancellationToken);
        var plan = await client.PlanAsync(new() { Command = MutationCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
            Headers(MutationsMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        var result = new FileMutationPlan(command, plan.SourceIdentity is not { } source ? null : new(source.Device, source.Inode, source.Mode, source.OwnerUid, source.OwnerGid, source.LinkCount, source.SizeBytes, source.ModifiedAtUnixNs),
            Parent(plan.SourceParent), Parent(plan.TargetParent), plan.SizeBytes, plan.Sha256, plan.Fingerprint);
        if (!plan.Command.Equals(MutationCommand(command))) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        FileMutationService.Validate(result); return result;
    }
    public async Task<FileMutationReceipt?> InspectMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken)
    {
        var client = await Mutations(cancellationToken);
        try { return MutationReceipt(await client.InspectAsync(new() { OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash },
            Headers(MutationsMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken), operation); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<FileMutationReceipt> ApplyMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken) => MutationAction(operation, false, cancellationToken);
    public Task<FileMutationReceipt> DiscardMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken) => MutationAction(operation, true, cancellationToken);
    private async Task<FileMutationReceipt> MutationAction(FileMutationOperation operation, bool discard, CancellationToken cancellationToken, TerminalActor? recoveryActor = null)
    {
        var client = await Mutations(cancellationToken); var plan = FileMutationService.ReadPlan(operation);
        var request = new FileOperationApplyRequest { OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() { Command = MutationCommand(plan.Command),
                SourceIdentity = plan.SourceIdentity is not { } source ? null : new() { Device = source.Device, Inode = source.Inode, Mode = source.Mode, OwnerUid = source.OwnerUid,
                    OwnerGid = source.OwnerGid, LinkCount = source.LinkCount, SizeBytes = source.SizeBytes, ModifiedAtUnixNs = source.ModifiedAtUnixNs },
                SourceParent = Parent(plan.SourceParent), TargetParent = Parent(plan.TargetParent), SizeBytes = plan.SizeBytes, Sha256 = plan.Sha256, Fingerprint = plan.Fingerprint } };
        using var call = discard ? client.DiscardAsync(request, Headers(MutationsMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : client.ApplyAsync(request, Headers(MutationsMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return MutationReceipt(await call, operation);
    }
    private static FileParentIdentity? Parent(FileDirectoryIdentity? value) => value is null ? null : new(value.Device, value.Inode);
    private static FileDirectoryIdentity? Parent(FileParentIdentity? value) => value is null ? null : new() { Device = value.Device, Inode = value.Inode };
    private static FileOperationKind MutationKind(string value) => value switch { "mkdir" => FileOperationKind.CreateDirectory, "copy" => FileOperationKind.CopyFile,
        "move" => FileOperationKind.MoveFile, "chmod" => FileOperationKind.SetPermissions, _ => throw new ArgumentException("FILES_KIND_INVALID") };
    private static FileOperationCommand MutationCommand(FileMutationCommand command)
    {
        FileMutationService.Validate(command);
        return new() { Kind = MutationKind(command.Kind), Source = command.Source is null ? null : new() { RootId = command.Source.RootId, Path = command.Source.Path },
            Target = command.Target is null ? null : new() { RootId = command.Target.RootId, Path = command.Target.Path }, ExpectedVersion = command.ExpectedVersion, Mode = command.Mode };
    }
    private static FileMutationReceipt MutationReceipt(FileOperationReceipt value, FileMutationOperation operation)
    {
        var plan = FileMutationService.ReadPlan(operation);
        if (value.OperationId != operation.Id.ToString("N") || value.Kind != MutationKind(plan.Command.Kind)) throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        var result = new FileMutationReceipt(plan.Command.Kind, value.State, value.Fingerprint, value.Version, value.SizeBytes, value.Sha256, value.SourceRetained, value.BindingCurrent);
        FileMutationService.ValidateReceipt(plan, result); return result;
    }
}
