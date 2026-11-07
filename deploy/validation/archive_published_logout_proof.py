"""Actual BFF logout ordering and published Unknown/ACK joins; no mutation."""
from datetime import datetime,timedelta
from archive_published_restart_unknown_proof import prove,row_proof,audits,require_ack
from archive_inflight_logout_proof import exit_proof
from validate_metric_continuity import query


def logout_exit_proof(observation,action,cut_row):
    exit_proof(observation,action,cut_row['id'])
    assert action['executionJobId']==cut_row['execution_job_id'] and cut_row['state']=='Applying' and cut_row['publish_barrier'] is True and cut_row['cancel_requested_at_utc'] is None
    began=datetime.fromisoformat(observation['beganAtUtc']);at=datetime.fromisoformat(action['observedAtUtc']);ended=datetime.fromisoformat(observation['exitObservedAtUtc']);lease=datetime.fromisoformat(cut_row['lease_expires_at_utc'])
    assert began<=at<=ended<lease and began+timedelta(seconds=40)<lease
    assert datetime.fromisoformat(cut_row['execution_expires_at_utc'])>began+timedelta(minutes=10)


def result_audit(row):
    return query("SELECT json_build_object('count',count(*),'atUtc',min(occurred_at_utc),'actors',coalesce(json_agg(actor_subject),'[]'::json)) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.result' AND result='Unknown'",job=row['execution_job_id'])


def result_order(row,action,settled,audit):
    assert audit['count']==1 and audit['actors']==[row['actor_subject']]
    assert settled['id']==action['id']==row['id'] and settled['executionJobId']==action['executionJobId']==row['execution_job_id'] and settled['state']=='Unknown'
    assert datetime.fromisoformat(action['observedAtUtc'])<=datetime.fromisoformat(audit['atUtc'])<=datetime.fromisoformat(settled['observedAtUtc'])


def browser_proof(b,f):
    assert b['actualUi'] and b['subject']==f['subject'] and b['recoverySubject']==f['recoverySubject']!=f['subject']
    assert type(b['ownerLogoutPosts']) is int and b['ownerLogoutPosts']==1 and b['originalSessionReadStatus']==401 and b['hintsCleared'] is b['cancelledDecisionPreserved'] is True
    assert len(b['oldReferenceStatuses'])==9 and all(type(n) is int and n==404 for n in b['oldReferenceStatuses'])
    assert b['managementWrites']==b['acknowledgements']==1 and len(b['jobProofs'])==len(b['logoutReceipts'])==2
    for r in b['logoutReceipts']:assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']
    suffix='/files/recycle-archives/'+b['archives'][1]['id']
    assert sum(p.endswith(suffix+'/cancel') for p in b['mutations'])==0 and sum(p.endswith(suffix+'/confirm') for p in b['mutations'])==1
    assert b['archives'][1]['state']=='Unknown' and b['archives'][1]['archiveCopied'] is None and b['final']['state']=='Acknowledged' and b['final']['archiveCopied'] is None
