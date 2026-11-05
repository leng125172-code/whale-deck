"""Read-only binding of permanent restore intents to native plans and task facts.

Pure projections do not establish a physical namespace. The owning gate must
reopen the installed journals/HDD and separately prove the original inode.
"""
import hashlib
import uuid
from acl_history_browser_proof import typed_equal
from container_recycle_restore_rpc_proof import fields, native_plan_proof, proto_receipt
from validate_container_recycle_browser import native_plan as source_plan

SOURCE_FIELDS = ('SchemaVersion', 'Command', 'TargetProof', 'ParentIdentity', 'ParentVersion', 'OwnerUid', 'OwnerGid', 'Fingerprint', 'SourceIdentity', 'SourceChangedAtUnixNs', 'Sha256', 'Attributes')
IDENTITY = ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs')
SUMMARY = ('schemaVersion', 'intentId', 'jobId', 'sourceIntentId', 'sourceOperationId', 'stage', 'state', 'code', 'receiptState', 'sourceState', 'bindingCurrent', 'sourceRetained', 'sourceRestored', 'restoredByThisOperation', 'historyVerified', 'historyAvailable', 'publishBarrier')


def guid(value):
    assert type(value) is str and str(uuid.UUID(value)) == value and uuid.UUID(value).int
    return uuid.UUID(value)


def execution(identifier, *, source=False):
    value = guid(identifier)
    prefix = 'whaledeck-container-recycle-' + ('' if source else 'restore-') + 'execution-id-v1:'
    return str(uuid.UUID(bytes_le=hashlib.sha256((prefix + value.hex).encode()).digest()[:16]))


def managed_source(value):
    fields(value, SOURCE_FIELDS); fields(value['Command'], ('Location', 'ExpectedVersion'))
    fields(value['Command']['Location'], ('ContainerId', 'InstanceId', 'Path'))
    fields(value['ParentIdentity'], ('Device', 'Inode')); fields(value['SourceIdentity'], IDENTITY)
    assert type(value['Attributes']) is list
    for attribute in value['Attributes']: fields(attribute, ('Name', 'Value'))
    return source_plan(value)


def managed_plan(value):
    fields(value, ('SchemaVersion', 'Command', 'TargetProof', 'ParentIdentity', 'ParentVersion', 'OwnerUid', 'OwnerGid', 'PrivateIdentity', 'RetainedIdentity', 'RetainedChangedAtUnixNs', 'Fingerprint'))
    command = value['Command']; fields(command, ('Source', 'HistoryDigest'))
    source = command['Source']; fields(source, ('OperationId', 'Owner', 'Plan'))
    for key in ('ParentIdentity', 'PrivateIdentity'): fields(value[key], ('Device', 'Inode'))
    fields(value['RetainedIdentity'], IDENTITY)
    result = dict(schemaVersion=value['SchemaVersion'], command=dict(source=dict(operationId=guid(source['OperationId']).hex, owner=source['Owner'], plan=managed_source(source['Plan'])), historyDigest=command['HistoryDigest']),
        targetProof=value['TargetProof'], parentIdentity=[value['ParentIdentity'][k] for k in ('Device', 'Inode')], parentVersion=value['ParentVersion'], uid=value['OwnerUid'], gid=value['OwnerGid'],
        privateIdentity=[value['PrivateIdentity'][k] for k in ('Device', 'Inode')], retainedIdentity=[value['RetainedIdentity'][k] for k in IDENTITY]+[value['RetainedChangedAtUnixNs']], fingerprint=value['Fingerprint'])
    native_plan_proof(result); return result


def managed_receipt(value, request):
    fields(value, ('id', 'sourceOperationId', 'fingerprint', 'targetProof', 'state', 'bindingCurrent', 'replayed', 'historyVerified', 'historyDigest', 'historyAvailable', 'sourceState', 'sourceRetained', 'sourceRestored', 'restoredByThisOperation'))
    result = {k: v for k, v in value.items() if k != 'id' and v is not None}
    result['operationId'] = guid(value['id']).hex
    result['sourceOperationId'] = guid(value['sourceOperationId']).hex
    return proto_receipt(result, request)


def job_summary(value, row, *, execution_stage):
    """Planning Ready facts remain historical when a later Ready intent is canceled."""
    fields(value, SUMMARY)
    original = row['source_intent_id']; source_op = execution(original, source=True)
    expected = dict(schemaVersion=1, intentId=row['id'], jobId=execution(row['id']) if execution_stage else row['id'], sourceIntentId=original, sourceOperationId=source_op,
        stage='execution' if execution_stage else 'planning', state=row['state'] if execution_stage else 'Ready', code=row['result_code'] if execution_stage else 'CONTAINER_RECYCLE_RESTORE_READY',
        receiptState=row['root_state'] if execution_stage else None, sourceState=row['source_state'] if execution_stage else None,
        bindingCurrent=row['binding_current'] if execution_stage else None, sourceRetained=row['source_retained'] if execution_stage else None,
        sourceRestored=row['source_restored'] if execution_stage else None, restoredByThisOperation=row['restored_by_this_operation'] if execution_stage else None,
        historyVerified=row['history_verified'] if execution_stage else False, historyAvailable=row['history_available'] if execution_stage else None, publishBarrier=False)
    typed_equal(value, expected); return expected
