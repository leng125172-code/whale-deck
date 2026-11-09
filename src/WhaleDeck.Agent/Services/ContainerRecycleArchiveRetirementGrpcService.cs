using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

// Not mapped in Program. Native installation and Gateway/resource authorization
// must be accepted before this adapter can become a reachable product endpoint.
public sealed class ContainerRecycleArchiveRetirementGrpcService(FileTransferRunner runner)
    : ContainerRecycleArchiveRetirementService.ContainerRecycleArchiveRetirementServiceBase
{
    public override async Task<ContainerRecycleArchiveRetirementPlan> Plan(ContainerRecycleArchiveRetirementPlanRequest request, ServerCallContext context)
    {
        ContainerRecycleArchiveRetirementPolicy.PlanRequest(request);
        ContainerRecycleArchiveRetirementPolicy.Binding(request, context);
        using var result = await runner.ContainerRecycleArchiveRetirementOnceAsync("plan", JsonSerializer.Serialize(ContainerRecycleArchiveRetirementWire.Command(request.Command)), context.CancellationToken);
        return ContainerRecycleArchiveRetirementPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerRecycleArchiveRetirementReceipt> Reserve(ContainerRecycleArchiveRetirementRequest request, ServerCallContext context) => Run("reserve", request, context);
    public override Task<ContainerRecycleArchiveRetirementReceipt> Apply(ContainerRecycleArchiveRetirementRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerRecycleArchiveRetirementReceipt> Inspect(ContainerRecycleArchiveRetirementRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerRecycleArchiveRetirementReceipt> Discard(ContainerRecycleArchiveRetirementRequest request, ServerCallContext context) => Run("discard", request, context);

    private async Task<ContainerRecycleArchiveRetirementReceipt> Run(string action, ContainerRecycleArchiveRetirementRequest request, ServerCallContext context)
    {
        ContainerRecycleArchiveRetirementPolicy.Request(request);
        ContainerRecycleArchiveRetirementPolicy.Binding(request, context);
        using var result = await runner.ContainerRecycleArchiveRetirementOnceAsync(action, JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan = ContainerRecycleArchiveRetirementWire.Plan(request.Plan) }), context.CancellationToken);
        return ContainerRecycleArchiveRetirementPolicy.Receipt(result.RootElement, request);
    }
}
