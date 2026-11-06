using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class ContainerRecycleArchiveGrpcService(FileTransferRunner runner) : ContainerRecycleArchiveService.ContainerRecycleArchiveServiceBase
{
    public override async Task<ContainerRecycleArchivePlan> Plan(ContainerRecycleArchivePlanRequest request,ServerCallContext context)
    {
        ContainerRecycleArchiveWire.RequireKnown(request);ContainerRecycleArchivePolicy.Command(request.Command);ContainerRecyclePolicy.Context(request.Context,planning:true);ContainerRecycleArchivePolicy.Binding(request,context);
        using var result=await runner.ContainerRecycleArchiveOnceAsync("plan",JsonSerializer.Serialize(ContainerRecycleArchiveWire.Command(request.Command)),context.CancellationToken);
        return ContainerRecycleArchivePolicy.PlanOutput(result.RootElement,request.Command);
    }
    public override Task<ContainerRecycleArchiveReceipt> Reserve(ContainerRecycleArchiveRequest request,ServerCallContext context)=>Run("reserve",request,context);
    public override Task<ContainerRecycleArchiveReceipt> Apply(ContainerRecycleArchiveRequest request,ServerCallContext context)=>Run("apply",request,context);
    public override Task<ContainerRecycleArchiveReceipt> Inspect(ContainerRecycleArchiveRequest request,ServerCallContext context)=>Run("inspect",request,context);
    public override Task<ContainerRecycleArchiveReceipt> Discard(ContainerRecycleArchiveRequest request,ServerCallContext context)=>Run("discard",request,context);
    private async Task<ContainerRecycleArchiveReceipt> Run(string action,ContainerRecycleArchiveRequest request,ServerCallContext context)
    {
        ContainerRecycleArchivePolicy.Request(request);ContainerRecycleArchivePolicy.Binding(request,context);
        using var result=await runner.ContainerRecycleArchiveOnceAsync(action,JsonSerializer.Serialize(new {operationId=request.OperationId,owner=request.Owner,plan=ContainerRecycleArchiveWire.Plan(request.Plan)}),context.CancellationToken);
        return ContainerRecycleArchivePolicy.Receipt(result.RootElement,request);
    }
}

