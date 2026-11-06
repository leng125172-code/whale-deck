using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Internal only: observe first; only the fresh lease may Reserve then Apply.</summary>
public sealed class ContainerRecycleArchiveDispatcher(IContainerRecycleArchiveRepository repository,IContainerRecycleArchiveGateway gateway,
    ContainerRecycleArchiveWorkflowService authorization,IContainerRecycleArchiveProtector protector,TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row=await repository.ClaimAsync(clock.GetUtcNow(),cancellationToken);if(row is null)return false;
        try
        {
            if(!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(row,protector))
            {
                if(row.ExecutionJobId is null)await repository.FinishPlanningAsync(row,null,"CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED",cancellationToken);
                else await repository.FinishAsync(row,"Unknown","CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED",null,cancellationToken);
                return true;
            }
            if(row.ExecutionJobId is null)
            {
                if(await Canceled(row,cancellationToken)||!await Allowed(row,cancellationToken))await repository.FinishPlanningAsync(row,null,"CONTAINER_RECYCLE_ARCHIVE_CANCELED",cancellationToken);
                else if(row.State=="Ready"||row.PlanningExpiresAtUtc<=clock.GetUtcNow())await repository.FinishPlanningAsync(row,null,"CONTAINER_RECYCLE_ARCHIVE_PLAN_EXPIRED",cancellationToken);
                else if(await repository.MarkPlanningAsync(row,cancellationToken))
                {
                    await gateway.RequireContainerRecycleArchiveAsync(cancellationToken);
                    var plan=await Guarded(row,true,token=>ReadPlan(row,token),cancellationToken);
                    if(!ContainerRecycleArchiveWorkflowValidation.SameCommand(plan.Command,ContainerRecycleArchiveWorkflowValidation.ReadCommand(row)))throw ContainerRecycleArchiveValidation.Invalid();
                    await repository.FinishPlanningAsync(row,plan,"CONTAINER_RECYCLE_ARCHIVE_READY",cancellationToken);
                }
                else await repository.DeferAsync(row,cancellationToken);
                return true;
            }
            var operation=ContainerRecycleArchiveWorkflowValidation.Reference(row);
            var observed=await Guarded(row,false,token=>gateway.InspectContainerRecycleArchiveAsync(operation,token),cancellationToken);
            if(observed is not null)
            {
                // A reclaimed Reserved execution is never permission to resume
                // copying. Cancel the original reservation and observe facts.
                if(observed.State=="Reserved")observed=await Guarded(row,false,token=>gateway.DiscardContainerRecycleArchiveAsync(operation,token),cancellationToken);
                await Finish(observed);return true;
            }
            if(await InvalidAuthority(row,cancellationToken)||row.RootStartedAtUtc is not null||row.ApplyStartedAtUtc is not null)
            {await Finish(await Guarded(row,false,token=>gateway.DiscardContainerRecycleArchiveAsync(operation,token),cancellationToken));return true;}
            await gateway.RequireContainerRecycleArchiveAsync(cancellationToken);
            if(await repository.StartedAsync(row,clock.GetUtcNow(),cancellationToken)!=ContainerFileUploadStartResult.Started)
            {await repository.DeferAsync(row,cancellationToken);return true;}
            var reserved=await Guarded(row,true,token=>gateway.ReserveContainerRecycleArchiveAsync(operation,token),cancellationToken);
            ContainerRecycleArchiveValidation.Receipt(operation,reserved);
            if(reserved.State!="Reserved"){await Finish(reserved);return true;}
            if(!await repository.ReservedAsync(row,reserved,cancellationToken))throw new FileTransferRejectedException();
            if(await InvalidAuthority(row,cancellationToken))
            {await Finish(await Guarded(row,false,token=>gateway.DiscardContainerRecycleArchiveAsync(operation,token),cancellationToken));return true;}
            await authorization.RequireCurrentSourceAsync(row,Actor(row),cancellationToken);
            if(await repository.ApplyingAsync(row,clock.GetUtcNow(),cancellationToken)!=ContainerFileUploadStartResult.Started)
            {await repository.DeferAsync(row,cancellationToken);return true;}
            await Finish(await Guarded(row,true,token=>gateway.ApplyContainerRecycleArchiveAsync(operation,token),cancellationToken));

            async Task Finish(ContainerRecycleArchiveReceiptDto receipt)
            {
                ContainerRecycleArchiveValidation.Receipt(operation,receipt);
                if(receipt.State=="Completed"&&(row.ApplyStartedAtUtc is null||row.ReservationJson is null))
                {
                    // Unexpected history cannot authorize a new copying or
                    // bypass missing durable business markers. Hold for explicit
                    // recovery; a later acknowledgement may record Root proof.
                    await repository.FinishAsync(row,"Unknown","CONTAINER_RECYCLE_ARCHIVE_EXECUTION_MARKER_MISSING",null,cancellationToken);return;
                }
                if(receipt.State=="Reserved")
                {
                    // Discard may fail to resolve a changed/missing namespace.
                    // A still-Reserved reply cannot finalize or release a fence.
                    await repository.FinishAsync(row,"Unknown","CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN",null,cancellationToken);return;
                }
                ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(row,receipt);var canceled=false;
                if(receipt.State=="Aborted")
                {
                    canceled=await Canceled(row,cancellationToken);
                    if(!canceled){try{canceled=!await Allowed(row,cancellationToken);}catch(Exception)when(!cancellationToken.IsCancellationRequested){}}
                }
                var state=receipt.State=="Completed"?"Succeeded":receipt.State=="Unknown"?"Unknown":canceled?"Canceled":"Failed";
                var code=state switch{"Succeeded"=>"CONTAINER_RECYCLE_ARCHIVE_COMPLETED","Unknown"=>"CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN","Canceled"=>"CONTAINER_RECYCLE_ARCHIVE_CANCELED",_=>"CONTAINER_RECYCLE_ARCHIVE_INTERRUPTED"};
                await repository.FinishAsync(row,state,code,receipt,cancellationToken);
            }
        }
        catch(InvalidOperationException error)when(row.ExecutionJobId is null&&error.Message=="CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED")
        {await repository.FinishPlanningAsync(row,null,error.Message,cancellationToken);}
        catch(InvalidOperationException error)when(error.Message=="CONTAINER_RECYCLE_ARCHIVE_OUTPUT_INVALID")
        {
            if(row.ExecutionJobId is null)await repository.FinishPlanningAsync(row,null,error.Message,cancellationToken);
            else await repository.FinishAsync(row,"Unknown",error.Message,null,cancellationToken);
        }
        catch(Exception)when(!cancellationToken.IsCancellationRequested)
        {
            if(row.ExecutionJobId is not null&&row.ExecutionExpiresAtUtc<=clock.GetUtcNow())await repository.FinishAsync(row,"Unknown","CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN",null,cancellationToken);
            else await repository.DeferAsync(row,cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerRecycleArchiveIntent row)=>new(row.ActorSubject,null,[],row.LoginBinding,row.TraceId,null);
    private async Task<ContainerRecycleArchivePlanDto> ReadPlan(ContainerRecycleArchiveIntent row,CancellationToken token)
    {
        var plan=await gateway.PlanContainerRecycleArchiveAsync(Actor(row),ContainerRecycleArchiveWorkflowValidation.ReadCommand(row),token);
        try{return ContainerRecycleArchiveWorkflowValidation.SnapshotPlan(plan);}catch(Exception error)when(error is ArgumentException or System.Text.Json.JsonException){throw ContainerRecycleArchiveValidation.Invalid();}
    }
    private Task<bool> Canceled(ContainerRecycleArchiveIntent row,CancellationToken token)=>repository.CancellationRequestedAsync(row.Id,token);
    private async Task<bool> Allowed(ContainerRecycleArchiveIntent row,CancellationToken token)
    {try{await authorization.AuthorizeAsync(Actor(row),token);return true;}catch(UnauthorizedAccessException){return false;}}
    private async Task<bool> InvalidAuthority(ContainerRecycleArchiveIntent row,CancellationToken token)=>row.ExecutionExpiresAtUtc<=clock.GetUtcNow()||await Canceled(row,token)||!await Allowed(row,token);
    private async Task<T> Guarded<T>(ContainerRecycleArchiveIntent row,bool active,Func<CancellationToken,Task<T>> action,CancellationToken cancellationToken)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromSeconds(135));await Guard();var call=action(lifetime.Token);
        try
        {
            while(!call.IsCompleted)
            {
                using var wait=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var delay=Task.Delay(TimeSpan.FromSeconds(6),clock,wait.Token);
                if(await Task.WhenAny(call,delay)==call){await wait.CancelAsync();break;}await delay;await Guard();
            }
            var value=await call;await Guard();return value;
        }
        finally{await lifetime.CancelAsync();try{await call;}catch(Exception){}}
        async Task Guard()
        {
            using var check=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);check.CancelAfter(TimeSpan.FromSeconds(10));
            if(!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(row,protector))throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED");
            var expiry=row.ExecutionJobId is null?row.PlanningExpiresAtUtc:row.ExecutionExpiresAtUtc;
            if(active)await authorization.RequireCurrentSourceAsync(row,Actor(row),check.Token);
            if(active&&(expiry<=clock.GetUtcNow()||await Canceled(row,check.Token)||!await Allowed(row,check.Token))||!await repository.RenewAsync(row,clock.GetUtcNow(),check.Token))throw new FileTransferRejectedException();
        }
    }
}
