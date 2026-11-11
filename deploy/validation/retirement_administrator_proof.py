"""Read-only identity/task/completion binding proof, without login material."""
import uuid


def require_administrator_recovery(state, rows):
    recovery = state['retirementBusiness']['recovery']
    proof = recovery['administrator']
    owner, actor = state['ownerSubject'], proof['subject']
    assert str(uuid.UUID(owner)) == owner and str(uuid.UUID(actor)) == actor and owner != actor
    assert proof['ownerSubject'] == owner and proof['passed']
    for key in ('independentVisibleLogin', 'resourceOwnerPreserved',
            'recoveryCompletedByIndependentSession', 'originalOwnerSessionPreserved'):
        assert proof[key] is True
    assert proof['logout']['normalUiLogout'] and proof['logout']['bffStatus'] == 401
    assert proof['logout']['identityStatus'] in (401, 403)
    previous = recovery['attemptsBefore'][0]['taskId']
    latest = recovery['attemptsAfter'][1]['taskId']
    assert previous != latest
    assert rows == [dict(workspaceId=state['workspace']['id'], workspaceOwner=owner,
        jobActor=owner, executionActor=actor, recoveryActor=actor,
        previousTaskId=previous, currentTaskId=latest, recoveryTaskId=latest,
        completionTaskId=latest, bindingMatches=True, recoveryCount=1,
        independentActorAudit=True)]


def observe_administrator_recovery(state, sql):
    retirement = state['retirementBusiness']['retirement']['retirementId']
    assert str(uuid.UUID(retirement)) == retirement
    rows = sql("SELECT json_build_object('workspaceId',w.id,'workspaceOwner',w.owner_subject,'jobActor',j.actor_subject,'executionActor',r.execution_subject,'recoveryActor',a.actor_subject,'previousTaskId',a.previous_step_id,'currentTaskId',r.execute_step_id,'recoveryTaskId',a.id,'completionTaskId',c.execute_step_id,'bindingMatches',r.login_binding=a.login_binding,'recoveryCount',(SELECT count(*) FROM development_workspace_retirement_recoveries t WHERE t.retirement_id=r.id),'independentActorAudit',EXISTS(SELECT 1 FROM audit_events e WHERE e.job_id=r.id AND e.actor_subject=a.actor_subject AND e.action='development.workspace.retirement.confirm-recovery-delete-source')) FROM development_workspace_retirement_workflows r JOIN development_workspaces w ON w.id=r.workspace_id JOIN operation_jobs j ON j.id=r.id JOIN development_workspace_retirement_recoveries a ON a.id=r.execute_step_id JOIN development_workspace_retirement_completions c ON c.retirement_id=r.id WHERE r.id='"+retirement+"';")
    require_administrator_recovery(state, rows)
    return dict(passed=True, rows=rows, noLoginBindingOrTokenExported=True)
