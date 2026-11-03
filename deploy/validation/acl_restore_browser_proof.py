"""Restoration provenance checks, independently bound to selected Root/HDD metadata."""
import hashlib
import json
import uuid
from acl_history_browser_proof import typed_equal

SPECS=(('restore-root','base','acl-file.bin',False,False),
       ('restore-basic','access','acl-file.bin',False,False),
       ('restore-noop','access','acl-file.bin',False,True),
       ('restore-default','clear-default','acl-directory',False,False),
       ('restore-clear','default','acl-directory',False,False),
       ('restore-cancel','base','acl-file.bin',True,False))

def restore_spec(name):
    matches=[v for v in SPECS if v[0]==name];assert len(matches)==1
    label,capture,relative,cancel,noop=matches[0]
    return dict(name=label,capture=capture,relative=relative,cancel=cancel,noChange=noop)

def source_json(source):
    loc=source['location'];entry=source['entry']
    fields=('captureId','historyDigest','createdAtUnixMs','sourceInstanceId','changedAclType','kind','originalMode','accessEntryCount','defaultEntryCount')
    def entries(values):return [dict(Tag=e['tag'],Id=e['id'],Permissions=e['permissions']) for e in values]
    return dict(Location=dict(ContainerId=loc['containerId'],InstanceId=loc['instanceId'],Path=loc['path']),
        Entry={k[0].upper()+k[1:]:entry[k] for k in fields},AccessAcl=entries(source['accessAcl']),DefaultAcl=entries(source['defaultAcl']))

def unique_json(raw):
    def unique(pairs):
        result={}
        for key,value in pairs:assert key not in result;result[key]=value
        return result
    return json.loads(raw,object_pairs_hook=unique) if isinstance(raw,str) else raw

def prove_source(row,source,result):
    spec=restore_spec(result['name']);location=source['location'];entry=source['entry']
    assert location==dict(containerId=row['container_id'],instanceId=row['instance_id'],path=row['relative_path'])
    assert location['path']=='work/'+spec['relative'] and entry['changedAclType']==('access' if spec['relative']=='acl-file.bin' else 'default')
    raw=row['restore_source_json'];assert type(raw) is str and len(raw.encode())<=32768
    typed_equal(unique_json(raw),source_json(source))
    assert row['id']==result['id'] and row['id'].replace('-','')!=entry['captureId']
    expected=source['accessAcl' if entry['changedAclType']=='access' else 'defaultAcl']
    typed_equal(result['command']['entries'],expected);typed_equal(result['noChange'],spec['noChange'])
    assert result['command']['aclType']==entry['changedAclType']
    if row['execution_job_id']:
        assert row['execution_job_id']==result['executionJobId'] and uuid.UUID(row['execution_job_id']).hex!=entry['captureId']
        assert row['history_digest']!=entry['historyDigest']

def expected_metadata(row):
    digest=lambda s:hashlib.sha256(s.encode()).hexdigest().upper()
    return dict(intentId=row['id'],containerId=row['container_id'],instanceHash=digest(row['instance_id']),
        pathHash=digest(row['relative_path']),historyRestore=True,restoreSourceHash=digest(row['restore_source_json']))

def prove_audits(rows,row):
    canceled=row['execution_job_id'] is None
    expected={'plan-accepted':('Accepted',row['id']),'plan-result':('Ready',row['id'])}
    expected.update({'cancel-requested':('Accepted',row['id'])} if canceled else {'confirmed':('Accepted',row['execution_job_id']),'result':('Succeeded',row['execution_job_id'])})
    assert type(rows) is list and len(rows)==len(expected)
    seen=set()
    for audit in rows:
        assert audit['actor_subject']==row['actor_subject'] and audit['target_type']=='container' and audit['target_id']==row['container_id']
        assert audit['action'].startswith('containers.files.acl.restore.')
        action=audit['action'].removeprefix('containers.files.acl.restore.');assert action in expected and action not in seen;seen.add(action)
        assert (audit['result'],audit['job_id'])==expected[action]
        assert isinstance(audit['trace_id'],str) and 0<len(audit['trace_id'])<=64
        typed_equal(unique_json(audit['detail_json']),expected_metadata(row))
    assert seen==expected.keys()
