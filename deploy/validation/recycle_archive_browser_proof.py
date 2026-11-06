"""Independent SQL/public/native archive joins. No product imports, RPC or I/O.

Passing two synthetic dictionaries never proves a live file, HDD or identity;
the owning installed gate must acquire and independently re-read those objects.
"""
import copy
import hashlib
import re
import uuid
from acl_history_browser_proof import typed_equal
from recycle_purge_browser_proof import managed_plan as purge_plan, IDENTITY, guid, stored_object
from container_recycle_purge_rpc_proof import fields, integer
import container_recycle_archive_rpc_proof as native

PLAN = ('SchemaVersion', 'Command', 'TargetProof', 'ParentIdentity', 'ParentVersion', 'OwnerUid', 'OwnerGid',
        'PrivateIdentity', 'DeletedIdentity', 'DeletedChangedAtUnixNs', 'SourceRecordDigest', 'PurgeRecordDigest',
        'PurgeMarkerDigest', 'HistoryMetadataDigest', 'StorageHash', 'StoreIdentity', 'ArchiveIdentity',
        'ArchiveChangedAtUnixNs', 'ArchiveBytes', 'ArchiveSha256', 'Fingerprint')
RECEIPT = ('id', 'sourceOperationId', 'purgeOperationId', 'fingerprint', 'state', 'sourceFenced', 'ownedFence',
           'archiveCopied', 'bindingCurrent', 'publishedIdentity', 'publishedChangedAtUnixNs')
SUMMARY = ('schemaVersion', 'intentId', 'jobId', 'sourceIntentId', 'purgeOperationId', 'originalIntentId',
           'sourceOperationId', 'stage', 'state', 'code', 'receiptState', 'reservationStarted', 'reservationRecorded',
           'applyStarted', 'archiveCopied', 'sourceFenced', 'ownedFence', 'bindingCurrent', 'publishBarrier')
PUBLIC_IDENTITY = tuple(k[0].lower() + k[1:] for k in IDENTITY)


def execution(identifier, family='archive'):
    value = guid(identifier); assert family in ('archive', 'purge', 'recycle')
    prefix = 'whaledeck-container-recycle-' + ('' if family == 'recycle' else family + '-') + 'execution-id-v1:'
    return str(uuid.UUID(bytes_le=hashlib.sha256((prefix + value.hex).encode()).digest()[:16]))


def managed_plan(value):
    fields(value, PLAN); c = value['Command']; fields(c, ('Purge', 'HistoryDigest', 'Mode'))
    ref = c['Purge']; fields(ref, ('OperationId', 'Owner', 'Plan'))
    result = {k[0].lower() + k[1:]: v for k, v in value.items()
              if k not in ('Command', 'OwnerUid', 'OwnerGid', 'DeletedChangedAtUnixNs', 'ArchiveChangedAtUnixNs')}
    result['command'] = dict(purge=dict(operationId=guid(ref['OperationId']).hex, owner=ref['Owner'], plan=purge_plan(ref['Plan'])),
                             historyDigest=c['HistoryDigest'], mode=c['Mode'])
    result['uid'], result['gid'] = value['OwnerUid'], value['OwnerGid']
    for key in ('ParentIdentity', 'PrivateIdentity', 'StoreIdentity'):
        item = value[key]
        if key == 'PrivateIdentity' and item is None: result['privateIdentity'] = None; continue
        fields(item, ('Device', 'Inode')); result[key[0].lower() + key[1:]] = [item['Device'], item['Inode']]
    for key, changed in (('DeletedIdentity', 'DeletedChangedAtUnixNs'), ('ArchiveIdentity', 'ArchiveChangedAtUnixNs')):
        fields(value[key], IDENTITY)
        result[key[0].lower() + key[1:]] = [value[key][k] for k in IDENTITY] + [value[changed]]
    native.native_plan_proof(result); return result


def decimal(value, low, high):
    assert type(value) is str and re.fullmatch(r'(?:0|-?[1-9][0-9]{0,19})', value)
    number = int(value); assert low <= number <= high and str(number) == value; return number


def public_receipt(value, request):
    fields(value, RECEIPT); native.request_proof(request)
    result = {k: copy.deepcopy(v) for k, v in value.items() if k not in ('id', 'publishedIdentity', 'publishedChangedAtUnixNs') and v is not None}
    result['operationId'] = guid(value['id']).hex
    for key in ('sourceOperationId', 'purgeOperationId'): result[key] = guid(value[key]).hex
    if value['state'] == 'Completed':
        item = value['publishedIdentity']; fields(item, PUBLIC_IDENTITY); published = {}
        for key in PUBLIC_IDENTITY:
            if key in ('device', 'inode', 'linkCount'): published[key] = decimal(item[key], 0 if key == 'device' else 1, 2**64-1)
            elif key == 'modifiedAtUnixNs': published[key] = decimal(item[key], -2**63, 2**63-1)
            else: integer(item[key], 0, 1073741824 if key == 'sizeBytes' else 2**32-1); published[key] = item[key]
        result['publishedIdentity'] = published
        result['publishedChangedAtUnixNs'] = decimal(value['publishedChangedAtUnixNs'], 0, 2**63-1)
    else:
        assert value['publishedIdentity'] is value['publishedChangedAtUnixNs'] is None
    return native.proto_receipt(result, request)


def receipt_join(row, public, request):
    stored = stored_object(row['receipt_json']); fields(stored, tuple(k[0].upper()+k[1:] for k in RECEIPT))
    converted = {k[0].lower()+k[1:]: v for k, v in stored.items()}
    if stored['PublishedIdentity'] is not None:
        fields(stored['PublishedIdentity'], IDENTITY)
        converted['publishedIdentity'] = {k[0].lower()+k[1:]: (str(v) if k in ('Device', 'Inode', 'LinkCount', 'ModifiedAtUnixNs') else v)
                                           for k, v in stored['PublishedIdentity'].items()}
        converted['publishedChangedAtUnixNs'] = str(stored['PublishedChangedAtUnixNs'])
    typed_equal(public, converted); return public_receipt(public, request)


def job_summary(value, row, purge_row, *, execution_stage):
    assert type(execution_stage) is bool; fields(value, SUMMARY)
    intent, purge_id, original = row['id'], purge_row['id'], purge_row['source_intent_id']
    for identifier in (intent, purge_id, original): guid(identifier)
    assert row['source_intent_id'] == purge_id and len({intent, purge_id, original}) == 3
    command = stored_object(row['command_json']); fields(command, ('Purge', 'HistoryDigest', 'Mode'))
    fields(command['Purge'], ('OperationId', 'Owner', 'Plan'))
    assert command['Mode'] == 'ArchiveCopy' and command['Purge']['OperationId'] == execution(purge_id, 'purge')
    old = purge_plan(command['Purge']['Plan'])
    assert command['HistoryDigest'] == old['command']['historyDigest']
    assert command['Purge']['Plan']['Command']['Source']['OperationId'] == execution(original, 'recycle')
    state = row['state'] if execution_stage else 'Ready'
    code = row['result_code'] if execution_stage else 'CONTAINER_RECYCLE_ARCHIVE_READY'
    if state == 'Reserving': code = 'CONTAINER_RECYCLE_ARCHIVE_' + ('RESERVED' if row['reservation_json'] is not None else 'RESERVING')
    if state == 'Applying': code = 'CONTAINER_RECYCLE_ARCHIVE_APPLYING'
    expected = dict(schemaVersion=1, intentId=intent, jobId=execution(intent) if execution_stage else intent,
                    sourceIntentId=purge_id, purgeOperationId=execution(purge_id, 'purge'), originalIntentId=original,
                    sourceOperationId=execution(original, 'recycle'), stage='execution' if execution_stage else 'planning', state=state, code=code,
                    receiptState=row['root_state'] if execution_stage else None,
                    reservationStarted=execution_stage and row['root_started_at_utc'] is not None,
                    reservationRecorded=execution_stage and row['reservation_json'] is not None,
                    applyStarted=execution_stage and row['apply_started_at_utc'] is not None,
                    archiveCopied=row['archive_copied'] if execution_stage else None,
                    sourceFenced=row['source_fenced'] if execution_stage else None,
                    ownedFence=row['owned_fence'] if execution_stage else None,
                    bindingCurrent=row['binding_current'] if execution_stage else None,
                    publishBarrier=row['publish_barrier'] if execution_stage else False)
    if execution_stage:
        assert row['execution_job_id'] == execution(intent)
        assert state in ('Reserving', 'Applying', 'Succeeded', 'Canceled', 'Failed', 'Unknown', 'Acknowledged')
        allowed = dict(Succeeded=('COMPLETED',), Canceled=('CANCELED',), Failed=('INTERRUPTED',),
                       Unknown=('RESULT_UNKNOWN', 'OUTPUT_INVALID', 'BINDING_CHANGED', 'EXECUTION_MARKER_MISSING'),
                       Acknowledged=('UNKNOWN_ACKNOWLEDGED',),
                       Reserving=('RESERVED' if expected['reservationRecorded'] else 'RESERVING',), Applying=('APPLYING',))
        assert code in tuple('CONTAINER_RECYCLE_ARCHIVE_' + suffix for suffix in allowed[state])
        assert expected['publishBarrier'] is (state in ('Reserving', 'Applying', 'Unknown'))
        for key in ('archiveCopied', 'sourceFenced', 'ownedFence', 'bindingCurrent'): assert expected[key] is None or type(expected[key]) is bool
        assert not expected['reservationRecorded'] or expected['reservationStarted']
        assert not expected['applyStarted'] or expected['reservationRecorded']
        if state in ('Reserving', 'Applying'):
            assert expected['reservationStarted'] and row['root_state'] is None
            assert expected['applyStarted'] is (state == 'Applying')
        if row['root_state'] is None:
            assert state in ('Reserving', 'Applying', 'Unknown')
            assert row['archive_copied'] is row['source_fenced'] is row['owned_fence'] is row['binding_current'] is None
        if state == 'Succeeded': assert row['root_state'] == 'Completed' and row['archive_copied'] is True and expected['applyStarted']
        if state == 'Unknown': assert row['root_state'] in (None, 'Unknown') and row['archive_copied'] is None
        if state in ('Canceled', 'Failed'): assert row['root_state'] == 'Aborted' and row['archive_copied'] is False
        if row['root_state'] == 'Completed': assert row['archive_copied'] is True and row['source_fenced'] is True
        if row['root_state'] == 'Unknown': assert row['archive_copied'] is None and row['source_fenced'] is True
        if row['root_state'] == 'Aborted': assert row['archive_copied'] is False and row['owned_fence'] is False
        if row['binding_current'] is True: assert row['root_state'] == 'Completed' and row['owned_fence'] is True
    typed_equal(value, expected); return value
