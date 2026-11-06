"""Pure archive service restart joins. Synthetic inputs are not live acceptance."""
from recycle_archive_browser_proof import execution,stored_object,job_summary


def terminal_proof(value,fixture,purge_row,browser=None):
    r=value['row'];assert r['actor_subject']==fixture['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==value['executionJob']['state']=='Failed' and r['result_code']=='CONTAINER_RECYCLE_ARCHIVE_INTERRUPTED'
    assert all(r[k] for k in ('confirmed_at_utc','root_started_at_utc','reservation_json','apply_started_at_utc','completed_at_utc'))
    assert r['cancel_requested_at_utc'] is value['executionJob']['cancel_requested_at_utc'] is None
    assert r['execution_job_id']==execution(r['id']) and r['root_state']=='Aborted'
    assert r['archive_copied'] is r['owned_fence'] is False
    assert r['source_fenced'] is False and (r['binding_current'] is False or r['binding_current'] is None)
    assert r['publish_barrier'] is False and r['lease_token'] is r['lease_expires_at_utc'] is None and value['pending']==0
    assert value['accepted']==value['confirmed']==1 and value['canceled']==0 and value['planJob']['state']=='Succeeded'
    job_summary(stored_object(value['planJob']['result_json']),r,purge_row,execution_stage=False)
    job_summary(stored_object(value['executionJob']['result_json']),r,purge_row,execution_stage=True)
    if browser is not None:
        assert browser['subject']==r['actor_subject'] and browser['archives'][1]['id']==r['id']
        browser_proof(browser,fixture)
    return dict(originalExecutionInterrupted=True,explicitCancelAbsent=True,rootAborted=True,archiveCopied=False,durableApplyMarkerIsNotCopy=True)


def explicit_cancel_absent(row,count):
    assert row['cancel_requested_at_utc'] is None and type(count) is int and count==0


def browser_proof(b,f):
    assert b['actualUi'] and b['kind']==f['kind'] and b['kind'] in ('worker-crash','agent-restart')
    assert b['originalOwnerStillAdministrator'] is True and b['ownerLogoutPostsBeforeRestartOutcome']==0
    assert b['normalDualLogout'] and b['normalDualLogouts']==2 and b['reloginOriginalReferencesDenied']==9
    assert len(b['archives'])==2 and b['archives'][1]['state']=='Failed'
    suffix='/files/recycle-archives/'+b['archives'][1]['id']
    assert sum(p.endswith(suffix+'/cancel') for p in b['mutations'])==0 and sum(p.endswith(suffix+'/confirm') for p in b['mutations'])==1
    for r in b['logoutReceipts']:
        assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']
    assert len(b['logoutReceipts'])==2
