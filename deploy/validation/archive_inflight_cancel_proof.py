"""Pure archive cancellation/cut joins. Synthetic inputs are not live acceptance."""
from acl_history_browser_proof import typed_equal
from recycle_archive_browser_proof import execution,stored_object,job_summary,managed_plan
from container_recycle_archive_rpc_proof import fields,RECORD


def terminal_proof(value,fixture,purge_row,browser=None):
    r=value['row'];assert r['actor_subject']==fixture['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==value['executionJob']['state']=='Canceled' and r['result_code']=='CONTAINER_RECYCLE_ARCHIVE_CANCELED'
    assert all(r[k] for k in ('cancel_requested_at_utc','confirmed_at_utc','root_started_at_utc','reservation_json','apply_started_at_utc','completed_at_utc'))
    assert r['execution_job_id']==execution(r['id']) and r['root_state']=='Aborted'
    assert r['archive_copied'] is r['owned_fence'] is False
    assert r['source_fenced'] is False and (r['binding_current'] is False or r['binding_current'] is None)
    assert r['publish_barrier'] is False and r['lease_token'] is r['lease_expires_at_utc'] is None and value['pending']==0
    assert value['accepted']==value['confirmed']==1 and value['canceled']==0 and value['planJob']['state']=='Succeeded'
    job_summary(stored_object(value['planJob']['result_json']),r,purge_row,execution_stage=False)
    job_summary(stored_object(value['executionJob']['result_json']),r,purge_row,execution_stage=True)
    if browser is not None:
        assert browser['actualUi'] and browser['normalDualLogout'] and browser['reloginOriginalReferencesDenied']==9
        assert len(browser['archives'])==2 and browser['archives'][1]['id']==r['id'] and browser['archives'][1]['state']=='Canceled'
    return dict(originalExecutionCanceled=True,rootAborted=True,archiveCopied=False,durableApplyMarkerIsNotCopy=True)


def cut_proof(record,sql,proof,request):
    fields(record,RECORD)
    for key in ('operationId','owner','plan'):typed_equal(record[key],request[key])
    assert record['state']=='Reserved' and all(record[k] is None for k in ('privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity')) and record['parentAfterStage']==''
    r=sql['row'];assert r['execution_job_id']==execution(r['id']) and r['execution_job_id'].replace('-','')==request['operationId']
    assert r['owner_hash']==request['owner'];typed_equal(managed_plan(stored_object(r['plan_json'])),request['plan'])
    assert r['state']=='Applying' and r['publish_barrier'] is True and r['receipt_json'] is None
    assert all(r[k] for k in ('root_started_at_utc','reservation_json','apply_started_at_utc'))
    for key in ('fiveKernelFdProofs','currentHddFdAttested','sourceClaimMatches','targetAbsent','stageAbsent','copyingNotEntered','hddDataReread','hddMetadataJoined'):assert proof[key] is True
    p=request['plan'];assert proof['rootPhase']=='Reserved' and proof['hddSha256']==p['archiveSha256'] and proof['hddMetadataDigest']==p['historyMetadataDigest'] and proof['storageHash']==p['storageHash']
    typed_equal(proof['hddIdentity'],p['archiveIdentity']);typed_equal(proof['archiveFdIdentity'],p['archiveIdentity'][:2]);typed_equal(proof['storeIdentity'],p['storeIdentity'])
    identities=[proof[k] for k in ('containerRootIdentity','archiveLockIdentity','purgeLockIdentity','sourceLockIdentity','archiveFdIdentity')]
    assert all(type(i) is list and len(i)==2 and all(type(n) is int and n>=0 for n in i) and i[1]>0 for i in identities)
    assert len({tuple(i) for i in identities})==5
    return dict(rootReservedBeforeCopy=True,fiveKernelFdProofs=True,currentHddMetadataJoined=True)


def exit_proof(value,action,identifier):
    assert action['id']==identifier and action['executionJobId']==execution(identifier)
    assert value['observedWithOriginalPidfd'] is value['measurementBeginsBeforeAllowingUiAction'] is True
    assert type(value['elapsedSeconds']) in (int,float) and 0<=value['elapsedSeconds']<=20
    assert value['beganMonotonicNs']<=value['actionMonotonicNs']==int(action['monotonicNs'])<=value['exitObservedMonotonicNs']
    return dict(originalPidfdExitAfterUiAction=True)
