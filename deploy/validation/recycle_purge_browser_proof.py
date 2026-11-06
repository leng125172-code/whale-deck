"""Independent SQL/API/native purge joins; no product import, RPC or file mutation.

These projections do not prove a live namespace. The owning browser gate must
acquire actual pre-purge journals, the original inode and attested HDD snapshots.
"""
import hashlib
import base64
import uuid
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import guid, managed_source, stored_object, IDENTITY
from container_recycle_purge_rpc_proof import fields, native_plan_proof, proto_receipt, digest
import container_recycle_rpc_proof as source

PLAN = ('SchemaVersion', 'Command', 'TargetProof', 'ParentIdentity', 'ParentVersion', 'OwnerUid', 'OwnerGid', 'PrivateIdentity',
        'RetainedIdentity', 'RetainedChangedAtUnixNs', 'SourceRecordDigest', 'HistoryMetadataDigest', 'HistoryCreatedAtUnixMs',
        'StorageHash', 'StoreIdentity', 'ArchiveIdentity', 'ArchiveChangedAtUnixNs', 'ArchiveBytes', 'ArchiveSha256', 'Fingerprint')
SUMMARY = ('schemaVersion', 'intentId', 'jobId', 'sourceIntentId', 'sourceOperationId', 'stage', 'state', 'code', 'receiptState',
           'reservationStarted', 'reservationRecorded', 'applyStarted', 'purgeApplied', 'sourceFenced', 'ownedFence', 'publishBarrier')


def execution(identifier, *, source=False):
    value = guid(identifier)
    prefix = 'whaledeck-container-recycle-' + ('' if source else 'purge-') + 'execution-id-v1:'
    return str(uuid.UUID(bytes_le=hashlib.sha256((prefix + value.hex).encode()).digest()[:16]))


def managed_plan(value):
    fields(value, PLAN)
    c = value['Command']; fields(c, ('Source', 'HistoryDigest', 'Scope'))
    s = c['Source']; fields(s, ('OperationId', 'Owner', 'Plan'))
    p = {k[0].lower()+k[1:]: value[k] for k in PLAN if k not in ('Command', 'OwnerUid', 'OwnerGid', 'RetainedChangedAtUnixNs', 'ArchiveChangedAtUnixNs')}
    p['command'] = dict(source=dict(operationId=guid(s['OperationId']).hex, owner=s['Owner'], plan=managed_source(s['Plan'])), historyDigest=c['HistoryDigest'], scope=c['Scope'])
    p['uid'] = value['OwnerUid']; p['gid'] = value['OwnerGid']
    for key in ('ParentIdentity', 'PrivateIdentity', 'StoreIdentity'):
        fields(value[key], ('Device', 'Inode'))
        p[key[0].lower()+key[1:]] = [value[key][n] for n in ('Device', 'Inode')]
    for key, changed in (('RetainedIdentity', 'RetainedChangedAtUnixNs'), ('ArchiveIdentity', 'ArchiveChangedAtUnixNs')):
        fields(value[key], IDENTITY)
        p[key[0].lower()+key[1:]] = [value[key][n] for n in IDENTITY] + [value[changed]]
    native_plan_proof(p)
    return p


def managed_receipt(value, request):
    fields(value, ('id', 'sourceOperationId', 'fingerprint', 'state', 'sourceFenced', 'ownedFence', 'purgeApplied'))
    result = {k: v for k, v in value.items() if k != 'id' and v is not None}
    result['operationId'] = guid(value['id']).hex
    result['sourceOperationId'] = guid(value['sourceOperationId']).hex
    return proto_receipt(result, request)


def job_summary(value, row, *, execution_stage):
    """A later cancel/execute cannot rewrite the planning Job's historical Ready."""
    assert type(execution_stage) is bool
    fields(value, SUMMARY)
    intent, source = row['id'], row['source_intent_id']; guid(intent); guid(source)
    assert intent != source and execution(intent) != execution(source, source=True)
    state = row['state'] if execution_stage else 'Ready'
    assert state in ('Ready', 'Reserving', 'Applying', 'Succeeded', 'Canceled', 'Failed', 'Unknown', 'Acknowledged')
    code = row['result_code'] if execution_stage else 'CONTAINER_RECYCLE_PURGE_READY'
    if state == 'Reserving': code = 'CONTAINER_RECYCLE_PURGE_' + ('RESERVED' if row['reservation_json'] is not None else 'RESERVING')
    if state == 'Applying': code = 'CONTAINER_RECYCLE_PURGE_APPLYING'
    expected = dict(schemaVersion=1, intentId=intent, jobId=execution(intent) if execution_stage else intent,
        sourceIntentId=source, sourceOperationId=execution(source, source=True), stage='execution' if execution_stage else 'planning', state=state, code=code,
        receiptState=row['root_state'] if execution_stage else None,
        reservationStarted=execution_stage and row['root_started_at_utc'] is not None,
        reservationRecorded=execution_stage and row['reservation_json'] is not None,
        applyStarted=execution_stage and row['apply_started_at_utc'] is not None,
        purgeApplied=row['purge_applied'] if execution_stage else None,
        sourceFenced=row['source_fenced'] if execution_stage else None,
        ownedFence=row['owned_fence'] if execution_stage else None,
        publishBarrier=row['publish_barrier'] if execution_stage else False)
    if execution_stage:
        assert row['execution_job_id'] == execution(intent)
        assert expected['publishBarrier'] is (state in ('Reserving', 'Applying', 'Unknown'))
        allowed = dict(Succeeded=('COMPLETED',), Canceled=('CANCELED',), Failed=('INTERRUPTED',),
            Unknown=('RESULT_UNKNOWN', 'OUTPUT_INVALID', 'BINDING_CHANGED', 'EXECUTION_MARKER_MISSING'), Acknowledged=('UNKNOWN_ACKNOWLEDGED',),
            Reserving=('RESERVED' if expected['reservationRecorded'] else 'RESERVING',), Applying=('APPLYING',))
        assert code in tuple('CONTAINER_RECYCLE_PURGE_'+suffix for suffix in allowed[state])
        for key in ('root_started_at_utc', 'apply_started_at_utc'): assert row[key] is None or type(row[key]) is str and 0 < len(row[key]) <= 64
        for key in ('purgeApplied', 'sourceFenced', 'ownedFence'): assert expected[key] is None or type(expected[key]) is bool
        assert not expected['reservationRecorded'] or expected['reservationStarted']
        assert not expected['applyStarted'] or expected['reservationRecorded']
        if state == 'Reserving': assert expected['reservationStarted'] and not expected['applyStarted'] and expected['receiptState'] is None
        if state == 'Applying': assert expected['applyStarted'] and expected['receiptState'] is None
        r = expected['receiptState']
        if r is None:
            assert state in ('Reserving', 'Applying', 'Unknown')
            assert expected['purgeApplied'] is expected['sourceFenced'] is expected['ownedFence'] is None
        elif r == 'Completed':
            assert state in ('Succeeded', 'Acknowledged')
            assert expected['purgeApplied'] is expected['sourceFenced'] is expected['ownedFence'] is True
            if state == 'Succeeded': assert expected['applyStarted']
        elif r == 'Aborted':
            assert state in ('Canceled', 'Failed', 'Acknowledged')
            assert expected['purgeApplied'] is expected['ownedFence'] is False and type(expected['sourceFenced']) is bool
        else:
            assert r == 'Unknown' and state in ('Unknown', 'Acknowledged')
            assert expected['purgeApplied'] is None and expected['sourceFenced'] is True and type(expected['ownedFence']) is bool
    typed_equal(value, expected)
    return value


def retained_history(before, after, *, purged=False):
    """Only a proved purge may change captureState; archive bytes/identity stay fixed."""
    assert type(purged) is bool
    for v in (before, after):
        fields(v, ('history', 'data', 'storeIdentity', 'storageHash'))
        h, data = v['history'], v['data']
        fields(h, ('schemaVersion', 'resourceType', 'storageId', 'operationId', 'owner', 'plan', 'sizeBytes', 'sha256', 'captureKind', 'state', 'captureState', 'createdAtUnixMs', 'differentSourceDevice', 'historyDigest'))
        fields(data, ('identity', 'attributes', 'sha256', 'sizeBytes'))
        source.native_plan_proof(h['plan']); source.hash_value(h['owner'])
        assert type(h['operationId']) is str and len(h['operationId']) == 32 and uuid.UUID(h['operationId']).hex == h['operationId'] and uuid.UUID(h['operationId']).int
        assert type(h['schemaVersion']) is int and h['schemaVersion'] == 1
        assert h['resourceType'] == 'ContainerFileTrash' and h['storageId'] == 'database-platform.backup.hdd'
        assert h['captureKind'] == 'Trash' and h['state'] == 'Verified' and h['captureState'] in (('Trashed', 'Purged') if purged else ('Trashed',))
        assert type(h['createdAtUnixMs']) is int and 0 <= h['createdAtUnixMs'] <= 253402300799999
        assert type(h['differentSourceDevice']) is bool
        assert type(h['sizeBytes']) is type(data['sizeBytes']) is int and 0 <= h['sizeBytes'] <= 1073741824
        assert data['sizeBytes'] == h['sizeBytes'] == h['plan']['sourceIdentity'][6]
        assert data['sha256'] == h['sha256'] == h['plan']['sha256']; source.hash_value(data['sha256'])
        assert h['historyDigest'] == digest(b'whaledeck-container-recycle-history-v1\n', {k: val for k, val in h.items() if k not in ('state', 'captureState', 'historyDigest')})
        assert type(data['identity']) is list and len(data['identity']) == 9 and all(type(n) is int for n in data['identity'])
        assert 0 <= data['identity'][0] <= 2**64-1 and 1 <= data['identity'][1] <= 2**64-1
        assert data['identity'][2:6] == [33152, 0, 0, 1] and data['identity'][6] == data['sizeBytes']
        assert -2**63 <= data['identity'][7] <= 2**63-1 and 0 <= data['identity'][8] <= 2**63-1
        assert type(data['attributes']) is list
        names = []
        for attribute in data['attributes']:
            assert type(attribute) is list and len(attribute) == 2
            name, value = attribute
            assert type(name) is str and name.startswith('user.') and len(name.encode()) <= 255
            assert type(value) is str and base64.b64encode(base64.b64decode(value, validate=True)).decode() == value
            names.append(name)
        assert names == sorted(set(names))
        assert type(v['storeIdentity']) is list and len(v['storeIdentity']) == 2
        assert all(type(n) is int and 0 <= n <= 2**64-1 for n in v['storeIdentity']) and v['storeIdentity'][1] > 0
        source.hash_value(v['storageHash'])
    if purged:
        assert after['history']['captureState'] == 'Purged'
        typed_equal(after, {**before, 'history': {**before['history'], 'captureState': 'Purged'}})
    else:
        typed_equal(before, after)
    return dict(hddHistoryAndFullDataSnapshotEqual=True)
