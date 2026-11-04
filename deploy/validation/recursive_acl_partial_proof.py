"""Permanent ACL Partial/ack proof, separate from live browser coordination."""
import json
import os
import re
import uuid
import datetime as dt

from bound_container_recursive_acl_cut import record_for,child_id,STATE
from validate_container_recursive_acl_browser import managed_plan,expected_targets,read_metadata,archive
from validate_container_recursive_acl import PATHS,preserved
from verify_container_recursive_acl import file_version,acl_mode
from verify_container_recursive_permissions import digest,unsigned
from validate_container_upload_recovery_agent import trusted
from validate_container_recursive_permission_faults import require_logout_authority
from validate_container_recursive_permission_faults import require_authority_change
from validate_container_recursive_acl_faults import verify_revoked_browser
from recursive_acl_partial_unknown_proof import UNKNOWN_KINDS

COUNTS=('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')


def require_case(kind,partial,owner,authority=None):
    revoked=kind in ('group-removal','account-disable')
    assert kind in ('cancel','logout','group-removal','account-disable','worker-crash','agent-restart','target-removed','instance-changed') and owner['subject']==partial['row']['actor_subject'] and owner['normalDualLogout'] is (not revoked)
    assert owner['registrations']==owner['confirmations']==1 and owner['cancellations']==int(kind=='cancel') and owner['acknowledgements']==owner['uploads']==0
    assert bool(partial['row']['cancel_requested_at_utc']) is (kind=='cancel') and bool(partial['executionJob']['cancel_requested_at_utc']) is (kind=='cancel')
    if kind=='logout' or kind in UNKNOWN_KINDS:
        assert owner['result'] is None and owner['hintsCleared'] is True
        require_logout_authority(authority,owner['subject'])
        expiry=dt.datetime.fromisoformat(authority['permissionExpiresAtUtc']);ended=dt.datetime.fromisoformat(authority['confirmedAtUtc'])
        assert expiry.utcoffset() is not None and ended.utcoffset() is not None and expiry>ended+dt.timedelta(hours=1)
    if revoked:
        assert owner['result'] is None
        require_authority_change(authority,dict(subject=owner['subject'],permissionExpiresAtUtc=authority['permissionExpiresAtUtc']),kind)
        verify_revoked_browser(owner,kind,owner['subject'])


def require_phase(data,ack=False,unknown=False):
    assert type(unknown) is bool
    phase='Unknown' if unknown else 'Partial'
    row=data['row'];assert row['state']==('Acknowledged' if ack else phase) and row['root_state']==phase
    assert row['root_started_at_utc'] and row['lease_token'] is row['lease_expires_at_utc'] is None and row['history_verified'] and row['history_may_exist']
    assert row['publish_barrier'] is (not ack) and bool(row['completed_at_utc']) is ack
    assert data['planJob']['state']=='Succeeded' and data['executionJob']['state']==('Failed' if ack else 'Running')
    assert bool(data['executionJob']['completed_at_utc']) is ack and data['accepted']==data['confirmed']==1
    code='CONTAINER_RECURSIVE_ACL_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECURSIVE_ACL_RESULT_UNKNOWN'
    assert row['result_code']==(code if unknown else 'CONTAINER_RECURSIVE_ACL_PARTIAL_ACKNOWLEDGED' if ack else 'CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT')
    assert (data['pending']==0) if ack else data['pending']>0
    if unknown:
        assert all(type(row[k]) is int and row[k]==0 for k in ('completed_count','changed_count','aborted_count','not_started_count')) and type(row['unknown_count']) is int and row['unknown_count']==4
    else:
        assert type(row['changed_count']) is int and 0<row['changed_count']==row['completed_count']<4 and row['unknown_count']==0
        assert row['aborted_count']+row['not_started_count']+row['completed_count']==4
    assert bool(row['acknowledged_by']) is ack and bool(row['acknowledgement_json']) is ack


def require_ack(before,after,subject,unknown=False):
    require_phase(before,unknown=unknown);require_phase(after,True,unknown=unknown);old,new=before['row'],after['row']
    assert subject!=old['actor_subject'] and new['acknowledged_by']==subject and new['version']>old['version']
    stable=('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','command_json','request_hash','plan_json','plan_hash','execution_hash','execution_job_id','confirmed_at_utc','execution_expires_at_utc','root_started_at_utc',
        'root_state','completed_count','changed_count','aborted_count','not_started_count','unknown_count','outcome_digest','history_digest','history_verified','history_may_exist','cancel_requested_at_utc')
    for key in stable:assert old[key]==new[key],key
    a,b=json.loads(old['receipt_json']),json.loads(new['receipt_json'])
    facts=lambda r:{k:v for k,v in r.items() if k not in ('Replayed','BindingCurrent','HistoryAvailable')}
    assert facts(a)==facts(b) and json.loads(new['acknowledgement_json'])==b
    assert all(re.fullmatch('[A-F0-9]{64}',r['receipt_hash']) for r in (old,new))
    assert after['executionJob']['phase']=='Acknowledged'


def entry_checkpoint(record,index,present,unknown):
    """A started marker precedes child journal creation; absence is Unknown evidence."""
    assert type(present) is type(unknown) is bool
    if index<=record['startedThrough']:
        if present:return True
        assert unknown and index>record['finishedThrough'],'STARTED_CHILD_RECORD_MISSING'
    else:assert not present,'UNSTARTED_CHILD_RECORD_PRESENT'
    return False


def verify_records(data,baseline,before,after,storage,directory,prefix,read_only=False):
    unknown=baseline.get('kind') in UNKNOWN_KINDS
    assert prefix in ('partial','ack');require_phase(data,prefix=='ack',unknown=unknown)
    row=data['row'];plan=managed_plan(json.loads(row['plan_json']));record=record_for(row);operation=uuid.UUID(row['execution_job_id']).hex
    assert row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId'] and plan['rootIdentity']==before['']['identity']
    assert plan['command']['path']==row['relative_path']=='work/recursive-tree' and plan['command']['aclType']=='access'
    assert (plan['command']['fileEntries'],plan['command']['directoryEntries'])==expected_targets(1) and plan['entryCount']==plan['changeCount']==4
    phase_name='Unknown' if unknown else 'Partial'
    assert record['state']==phase_name and record['historyVerified'] and record['historyMayExist'] and re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    receipt=json.loads(row['receipt_json']);assert receipt['Id']==row['execution_job_id'] and receipt['State']==phase_name and receipt['AclType']=='access'
    assert receipt['Fingerprint']==plan['fingerprint'] and receipt['TargetProof']==plan['targetProof'] and receipt['HistoryAvailable'] and receipt['HistoryVerified'] and receipt['HistoryMayExist']
    for key in COUNTS:
        column=re.sub(r'(?<!^)(?=[A-Z])','_',key).lower();assert type(record[key]) is int and record[key]==row[column]==receipt[key[0].upper()+key[1:]]
    if unknown:assert [record[k] for k in COUNTS]==[0,0,0,0,4] and record['outcomeDigest']==row['outcome_digest']==receipt['OutcomeDigest']=='' and receipt['BindingCurrent'] is False
    else:assert 0<record['changedCount']==record['completedCount']<4 and record['unknownCount']==0 and record['notStartedCount']>0
    preserved(before,after,'access');assert after['']==before['']
    archive(directory,prefix+'.sql.json',json.dumps(data,sort_keys=True).encode(),read_only)
    archive(directory,prefix+'.root.json',(STATE/(operation+'.json')).read_bytes(),read_only)
    outcomes=[];children=[];histories=0
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json');saved=json.loads(raw)
            assert saved['resourceType']=='ContainerRecursiveAclMetadata' and saved['storageId']=='database-platform.backup.hdd' and saved['operationId']==operation and saved['owner']==row['owner_hash'] and saved['plan']==plan
            assert saved['historyDigest']==record['historyDigest']==row['history_digest']==receipt['HistoryDigest']==digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(saved,'historyDigest'))
            assert plan['manifestDigest']==digest(b'whaledeck-container-recursive-acl-manifest-v1\n',saved['entries']) and [v['relativePath'] for v in saved['entries']]==['sub/中文.bin','one.bin','sub','']
            archive(directory,prefix+'.hdd.json',raw,read_only)
            child_fd=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,child_fd,read_only=True)
                for index,entry in enumerate(saved['entries']):
                    cid=child_id(operation,index);relative=entry['relativePath'];cp=entry['plan'];actual=after[relative];old=before[relative];path='work/recursive-tree'+('/'+relative if relative else '')
                    assert cp['sourceIdentity']==old['identity'] and cp['accessAcl']==old['access'] and cp['defaultAcl']==old['default'] and cp['noChange'] is False
                    assert cp['fingerprint']==digest(b'whaledeck-container-acl-plan-v1\n',unsigned(cp,'fingerprint'))
                    assert cp['command']==dict(containerId=row['container_id'],instanceId=row['instance_id'],path=path,expectedVersion=file_version(baseline,path,old['identity']),aclType='access',entries=plan['command']['fileEntries' if PATHS[relative]=='File' else 'directoryEntries'])
                    assert cp['targetMode']==acl_mode(cp['command']['entries'])
                    child_path=STATE/(operation+'.entries')/(cid+'.json');phase='NotStarted';version='';changed=False
                    if entry_checkpoint(record,index,child_path.exists(),unknown):
                        trusted(child_path);child_raw=child_path.read_bytes();child=json.loads(child_raw)
                        assert child['operationId']==cid and child['owner']==row['owner_hash'] and child['plan']==cp
                        phase=child['state'];assert phase in (('Completed','Aborted','Preparing','Prepared','Applying','Unknown') if unknown else ('Completed','Aborted'))
                        if phase=='Completed':
                            wanted=list(old['identity']);wanted[2]=wanted[2]&~0o777|cp['targetMode']
                            assert actual['identity'][:8]==wanted[:8] and actual['identity'][8]>old['identity'][8] and child['actualIdentity']==actual['identity'] and child['aclConfirmed'] and child['historyVerified']
                            assert actual['access']==cp['command']['entries'] and actual['default']==old['default']
                            version=file_version(baseline,path,actual['identity']);assert child['version']==version;changed=True
                        else:assert actual==old and child['aclConfirmed'] is (False if phase=='Aborted' else None) and child['actualIdentity'] is None and child['version']==''
                        archive(directory,prefix+'.'+cid+'.root.json',child_raw,read_only)
                        if child['historyVerified']:
                            hr=read_metadata(storage.f,storage,configuration,child_fd,cid+'.json',16384);h=json.loads(hr)
                            assert h['operationId']==cid and h['owner']==row['owner_hash'] and h['plan']==cp and h['parentOperationId']==operation and h['manifestDigest']==plan['manifestDigest'] and h['resourceType']=='ContainerRecursiveAclEntryMetadata'
                            assert h['historyDigest']==child['historyDigest']==digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(h,'historyDigest'))
                            archive(directory,prefix+'.'+cid+'.hdd.json',hr,read_only);histories+=1
                        children.append(dict(id=cid,state=phase,historyVerified=child['historyVerified']))
                    else:
                        assert not child_path.exists() and actual==old
                        if unknown and index<=record['startedThrough']:phase='Unrecorded'
                    outcomes.append(dict(id=cid,state=phase,version=version,changed=changed))
                assert {v.name for v in (STATE/(operation+'.entries')).glob('*.json')}=={v['id']+'.json' for v in children}
                assert {v for v in os.listdir(child_fd) if v.endswith('.json')}=={v['id']+'.json' for v in children if v['historyVerified']}
            finally:os.close(child_fd)
        finally:os.close(fd)
    if unknown:
        assert sum(v['changed'] for v in outcomes)==1 and outcomes[0]['state']=='Completed' and all(v['state']!='Completed' for v in outcomes[1:])
    else:
        assert digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)==record['outcomeDigest']==row['outcome_digest']==receipt['OutcomeDigest']
        assert sum(v['changed'] for v in outcomes)==record['changedCount']
    return dict(id=row['id'],state=row['state'],counts=[record[k] for k in COUNTS],childHddRecords=histories,outcomes=outcomes)
