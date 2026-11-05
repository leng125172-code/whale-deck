"""Original four-object restore rounds: Ready historical Jobs and Completed facts.

Only read-only SQL/HTTP/DOM evidence validation; native file/HDD proofs remain
mandatory in the existing restore bridge, not replaced by task summaries.
"""
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_business_proof import managed_plan
from recursive_acl_restore_job_results_proof import projection,job_proof,api_job,COUNTS

SOURCES=('validate_recursive_acl_restore_success_jobs.py','verify_recursive_acl_restore_success_jobs.py','recursive_acl_restore_success_jobs_proof.py','recursive_acl_restore_success_jobs_failure.py','test_recursive_acl_restore_success_jobs_proof.py','installed_ui_restore_job_results.mjs')
LABELS=('restore-access','restore-noop','restore-default','restore-clear','restore-cancel')
CHANGES=(4,0,2,2,4)

def images():
    return {f'job-{name}-{stage}-{part}.png' for name in LABELS for stage in (('planning',) if name=='restore-cancel' else ('planning','execution')) for part in ('counts','metadata')}

def ready_proof(signal,data,name):
    assert name in LABELS and signal['name']==name and signal['id']==data['row']['id']
    row=data['row'];plan=managed_plan(row['plan_json']);s=projection(row,plan,execution=False)
    assert row['state']=='Ready' and row['execution_job_id'] is data['executionJob'] is None
    assert s['receiptState'] is None and all(s[k] is None for k in COUNTS)
    assert [s[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[4,2,2,CHANGES[LABELS.index(name)]]
    assert s['sourceChangedCount']==(4,4,2,2,4)[LABELS.index(name)] and s['aclType']==('default' if name in ('restore-default','restore-clear') else 'access')
    typed_equal(plan['restoreSource'],signal['source'])
    job_proof(signal['planning'],data['planJob'],s,prefix=f'job-{name}-planning')
    return s

def terminal_proof(signal,ready_signal,initial,data):
    name=signal['name'];ready=ready_proof(ready_signal,initial,name);packet=signal['jobResults']
    typed_equal(packet['planning'],ready_signal['planning']);typed_equal(data['planJob'],initial['planJob'])
    api_job(packet['historicalPlanning'],data['planJob'],ready)
    row=data['row'];assert row['id']==initial['row']['id']==signal['result']['id']
    typed_equal(row['plan_json'],initial['row']['plan_json'])
    if name=='restore-cancel':
        assert row['state']=='Canceled' and row['execution_job_id'] is data['executionJob'] is packet['execution'] is None
        actual=None
    else:
        assert row['state']=='Succeeded' and row['cancel_requested_at_utc'] is None
        actual=projection(row,managed_plan(row['plan_json']),execution=True)
        assert actual['receiptState']=='Completed' and actual['completedCount']==4 and actual['changedCount']==CHANGES[LABELS.index(name)]
        assert [actual[k] for k in ('abortedCount','notStartedCount','unknownCount')]==[0,0,0]
        assert all(actual[k] is True for k in ('bindingCurrent','historyMayExist','historyVerified','historyAvailable')) and actual['publishBarrier'] is False
        job_proof(packet['execution'],data['executionJob'],actual,prefix=f'job-{name}-execution')
    return dict(name=name,fields=26,planningSnapshotPreserved=True,planningUnprovedCounts=True,sourceChangedCount=ready['sourceChangedCount'],plannedChanges=ready['changeCount'],actualCounts=None if actual is None else [actual[k] for k in COUNTS],executionCreated=actual is not None)

def authority_proof(value,ordinary,completed):
    assert len(completed)==5
    ids=[j for r in completed for j in (r['planningJobId'],r['executionJobId']) if j is not None]
    assert len(ids)==len(set(ids))==9
    typed_equal(value['ids'],ids);typed_equal(value['adminAnonymous'],[401]*10)
    typed_equal(ordinary['jobsDenied'],[403]*10);typed_equal(ordinary['jobsAnonymous'],[401]*10)
    assert type(ordinary['jobsUiRequests']) is int and ordinary['jobsUiRequests']==0
