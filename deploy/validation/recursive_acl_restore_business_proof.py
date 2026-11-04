"""Pure C# intent/receipt -> native recursive restore evidence binding.

No SQL, RPC, process control, filesystem access or credential/HMAC key reads.
Acquisition and live bytes belong to the actual browser gate. Format checks on
business MAC columns are not an independent verification of their secret MAC.
"""
import hashlib
import json
import re
import uuid
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_rpc_proof import proto_plan, fields, COUNTS
from recursive_acl_restore_record_proof import RECEIPT, receipt_proof, records_proof

PLAN_FIELDS = ('SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','Fingerprint','RootIdentity','RootChangedAtUnixNs','ManifestDigest','EntryCount','FileCount','DirectoryCount','ChangeCount','RestoreSource','RestoreSourceFrame','RestoreTargetsDigest')
GUID = re.compile('[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}')
HASH = re.compile('[A-F0-9]{64}')


def unique(pairs):
    value={}
    for key,item in pairs:
        assert key not in value,'DUPLICATE_BUSINESS_PROOF_JSON_FIELD'
        value[key]=item
    return value


def private_json(raw):
    assert type(raw) is str and 0<len(raw.encode('utf-8'))<=65536,'BUSINESS_PROOF_JSON_LIMIT'
    def invalid(_):raise AssertionError('BUSINESS_PROOF_NONFINITE_NUMBER')
    return json.loads(raw,object_pairs_hook=unique,parse_constant=invalid)


def camel(value):
    if type(value) is dict:
        result={}
        for key,item in value.items():
            assert type(key) is str and key
            name=key[0].lower()+key[1:];assert name not in result,'AMBIGUOUS_BUSINESS_PROOF_FIELD'
            result[name]=camel(item)
        return result
    if type(value) is list:return [camel(item) for item in value]
    return value


def managed_plan(value):
    if type(value) is str:value=private_json(value)
    fields(value,PLAN_FIELDS)
    raw=camel(value)
    numbers=[raw[k] for k in ('schemaVersion','ownerUid','ownerGid','rootChangedAtUnixNs',*COUNTS)]
    numbers += list(raw['parentIdentity'].values())+list(raw['rootIdentity'].values())+list(raw['restoreSourceFrame'].values())
    numbers += [raw['restoreSource'][k] for k in ('createdAtUnixMs',*COUNTS)]
    assert all(type(v) is int for v in numbers),'MANAGED_PLAN_REQUIRES_INTEGER_NUMBERS'
    result=proto_plan(raw);assert result['command']['captureId']!='0'*32
    return result


def identifier(value):
    assert type(value) is str and GUID.fullmatch(value) and uuid.UUID(value).int!=0,'BUSINESS_PROOF_ID_INVALID'
    return uuid.UUID(value).hex


def managed_receipt(value, *, api=False):
    if type(value) is str:value=private_json(value)
    assert type(api) is bool
    wanted=(RECEIPT-{'operationId'})|{'id'}
    fields(value,wanted if api else {key[0].upper()+key[1:] for key in wanted})
    out=dict(value) if api else camel(value)
    out['operationId']=identifier(out.pop('id'))
    for field in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):assert type(out[field]) is int and 0<=out[field]<=10000
    for field in ('bindingCurrent','replayed','historyMayExist','historyVerified','historyAvailable'):assert type(out[field]) is bool
    return out


def metadata(row):
    digest=lambda value:hashlib.sha256(value.encode()).hexdigest().upper()
    return dict(intentId=row['id'],containerId=row['container_id'],instanceHash=digest(row['instance_id']),pathHash=digest(row['relative_path']))


def audit_proof(audits,row,canceled):
    expected={'plan-accepted':('Accepted',row['id']),'plan-result':('Ready',row['id'])}
    expected.update({'cancel-requested':('Accepted',row['id'])} if canceled else {'confirmed':('Accepted',row['execution_job_id']),'result':('Succeeded',row['execution_job_id'])})
    assert type(audits) is list and len(audits)==len(expected)
    seen=set();ids=set()
    for item in audits:
        prefix='containers.files.recursive-acl-restore.'
        assert item['actor_subject']==row['actor_subject'] and item['target_type']=='container' and item['target_id']==row['container_id']
        assert type(item['action']) is str and item['action'].startswith(prefix)
        action=item['action'].removeprefix(prefix);assert action in expected and action not in seen;seen.add(action)
        assert (item['result'],item['job_id'])==expected[action] and item['id'] not in ids;ids.add(item['id'])
        assert type(item['trace_id']) is str and 0<len(item['trace_id'])<=128
        typed_equal(private_json(item['detail_json']),metadata(row))
    assert seen==expected.keys()


def database_proof(data,result,baseline,subject):
    """Bind normal Completed or cancel-before-execution, never resolve uncertainty."""
    fields(data,('row','planJob','executionJob','pending','accepted','confirmed','events','audits'))
    row=data['row'];operation=identifier(row['id']);identifier(row['idempotency_key'])
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']=='work/recursive-tree' and result['id']==result['planningJobId']==row['id'] and result['state']==row['state']
    assert row['completed_at_utc'] and row['publish_barrier'] is False and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert type(row['version']) is int and row['version']==result['version'] and result['confirmation'] is None
    for key in ('login_binding','owner_hash','request_hash','plan_hash'):assert type(row[key]) is str and HASH.fullmatch(row[key])
    plan=managed_plan(row['plan_json']);command=plan['command'];source=plan['restoreSource']
    expected_command=dict(location={k:command[k] for k in ('containerId','instanceId','path')},**{k:command[k] for k in ('captureId','expectedHistoryDigest','expectedVersion')})
    typed_equal(result['command'],expected_command)
    typed_equal(private_json(row['command_json']),{key[0].upper()+key[1:]:value if key!='location' else {k[0].upper()+k[1:]:v for k,v in value.items()} for key,value in expected_command.items()})
    typed_equal(result['restoreSource'],source)
    for key in COUNTS:assert type(result[key]) is int and result[key]==plan[key]
    assert [result[k] for k in ('rootUid','rootGid','rootMode','executorUid','executorGid')]==[*plan['rootIdentity'][3:5],plan['rootIdentity'][2],plan['uid'],plan['gid']]
    assert result['manifestDigest']==plan['manifestDigest'] and all(type(data[k]) is int and data[k]>=0 for k in ('pending','accepted','confirmed','events')) and data['pending']==0 and data['accepted']==1
    plan_job=data['planJob'];assert plan_job['id']==row['id'] and plan_job['actor_subject']==subject and plan_job['state']=='Succeeded' and plan_job['job_type']=='containers.files.recursive-acl-restore-plan'
    assert plan_job['idempotency_key']==row['idempotency_key'];typed_equal(private_json(plan_job['request_json']),metadata(row))
    canceled=row['state']=='Canceled';assert row['state'] in ('Canceled','Succeeded')
    if canceled:
        assert row['execution_job_id'] is row['execution_hash'] is row['root_started_at_utc'] is data['executionJob'] is result['executionJobId'] is None
        assert row['cancel_requested_at_utc'] and data['confirmed']==0 and result['receipt'] is None
        assert row['receipt_json'] is row['receipt_hash'] is row['root_state'] is row['history_digest'] is None
        assert row['history_verified'] is row['history_may_exist'] is result['historyVerified'] is result['historyMayExist'] is False and result['historyDigest'] is None
        request=None;receipt=None
    else:
        execution=identifier(row['execution_job_id']);assert execution!=operation and execution!=source['captureId'] and operation!=source['captureId']
        job=data['executionJob'];assert job['id']==row['execution_job_id']==result['executionJobId'] and job['actor_subject']==subject and job['state']=='Succeeded' and job['job_type']=='containers.files.recursive-acl-restore'
        assert job['idempotency_key']==job['id'];typed_equal(private_json(job['request_json']),metadata(row))
        assert data['confirmed']==1 and data['events']>=4 and row['root_started_at_utc']
        for key in ('execution_hash','receipt_hash'):assert type(row[key]) is str and HASH.fullmatch(row[key])
        request=dict(operationId=execution,owner=row['owner_hash'],plan=plan)
        receipt=managed_receipt(row['receipt_json']);typed_equal(receipt,managed_receipt(result['receipt'],api=True))
        assert receipt['state']==row['root_state']=='Completed'
        assert receipt['operationId']==execution and receipt['containerId']==row['container_id'] and receipt['instanceId']==row['instance_id']
        assert receipt['fingerprint']==plan['fingerprint'] and receipt['targetProof']==plan['targetProof'] and receipt['aclType']==source['changedAclType']
        assert [receipt[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')]==[plan['entryCount'],plan['changeCount'],0,0,0]
        assert all(receipt[k] is True for k in ('bindingCurrent','historyAvailable','historyVerified','historyMayExist'))
        assert all(type(receipt[k]) is str and HASH.fullmatch(receipt[k]) for k in ('historyDigest','outcomeDigest'))
        for field in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount','outcomeDigest','historyMayExist','historyVerified','historyDigest','historyAvailable','bindingCurrent'):
            column=re.sub(r'(?<!^)(?=[A-Z])','_',field).lower();typed_equal(receipt[field],row[column])
        for field in ('historyMayExist','historyVerified','historyDigest'):typed_equal(result[field],receipt[field])
    audit_proof(data['audits'],row,canceled)
    return dict(plan=plan,request=request,receipt=receipt,canceled=canceled)


def records_bound_to_business(proof,bundle,source,frame,before,after,attribute_digest):
    assert proof['canceled'] is False
    item=dict(request=proof['request'],result=proof['receipt'],before=before,after=after)
    children=records_proof(item,bundle,restore=True,source=source,frame=frame,attribute_digest=attribute_digest)
    receipt_proof(proof['receipt'],proof['request'],bundle['root'],restore=True,binding=True,replayed=False)
    return children
