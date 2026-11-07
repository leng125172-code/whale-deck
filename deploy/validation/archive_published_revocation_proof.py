"""Published Unknown/ACK after actual owned identity revocation; no mutation."""
from datetime import datetime,timedelta
from acl_history_browser_proof import typed_equal
from archive_published_restart_unknown_proof import prove,row_proof,require_ack,audits
from archive_revocation_proof import exit_proof
from validate_container_recycle_revocation import authority_proof
from validate_metric_continuity import query

KINDS=('group-removal','account-disable')


def result_audit(row):
    return query("SELECT json_build_object('count',count(*),'atUtc',min(occurred_at_utc),'actors',coalesce(json_agg(actor_subject),'[]'::json)) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.result' AND result='Unknown'",job=row['execution_job_id'])


def result_order(authority,row,settled,audit):
    assert audit['count']==1 and audit['actors']==[row['actor_subject']]
    assert settled['id']==row['id'] and settled['executionJobId']==row['execution_job_id'] and settled['state']=='Unknown'
    assert datetime.fromisoformat(authority['confirmedAtUtc'])<=datetime.fromisoformat(audit['atUtc'])<=datetime.fromisoformat(settled['observedAtUtc'])


def authority_exit_proof(authority,fixture,cut_row,observation):
    authority_proof(authority,fixture);exit_proof(observation)
    assert authority['executionExpiresAtUtc']==cut_row['execution_expires_at_utc'] and authority['leaseExpiresAtCutUtc']==cut_row['lease_expires_at_utc']
    confirmed=datetime.fromisoformat(authority['confirmedAtUtc']);lease=datetime.fromisoformat(authority['leaseExpiresAtCutUtc']);ended=datetime.fromisoformat(observation['exitObservedAtUtc'])
    assert confirmed+timedelta(seconds=25)<lease and confirmed<=ended<lease
    assert cut_row['state']=='Applying' and cut_row['publish_barrier'] and cut_row['cancel_requested_at_utc'] is None
    return dict(realOwnedAuthorityRevoked=True,originalPidfdExitedAfterConfirmation=True,neitherExecutionNorLeaseExpired=True)


def browser_proof(b,f):
    assert b['actualUi'] and b['kind']==f['kind'] and f['kind'] in KINDS and b['subject']==f['subject'] and b['recoverySubject']==f['recoverySubject']!=f['subject']
    assert b['ownerLogoutPosts']==0 and b['privatePanelsAbsent'] is b['hintsCleared'] is b['cancelledDecisionPreserved'] is True
    assert len(b['oldReferenceStatuses'])==9 and all(type(n) is int and n==404 for n in b['oldReferenceStatuses'])
    assert b['managementWrites']==b['acknowledgements']==1 and len(b['jobProofs'])==2
    suffix='/files/recycle-archives/'+b['archives'][1]['id']
    assert sum(p.endswith(suffix+'/cancel') for p in b['mutations'])==0 and sum(p.endswith(suffix+'/confirm') for p in b['mutations'])==1
    if f['kind']=='group-removal':assert b['originalIntentStatus']==403 and b['currentIdentityStatus']==200 and b['currentSubject']==f['subject'] and b['currentlyAdministrator'] is False and b['renderedRevocationPage']=='forbidden'
    else:assert b['originalIntentStatus'] in (401,403) and b['currentIdentityStatus'] in (401,403) and b['renderedRevocationPage']=='login'
    assert b['archives'][1]['state']=='Unknown' and b['archives'][1]['archiveCopied'] is None and b['final']['state']=='Acknowledged' and b['final']['archiveCopied'] is None
    r=b['recoveryLogout'];assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']
