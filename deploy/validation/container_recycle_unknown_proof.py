"""QA-only owned source displacement and read-only Unknown/ACK proofs."""
import ctypes
import json
import os
from pathlib import Path
import re
import select
import stat
import uuid

from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot, physical, read_operation
from container_recycle_rpc_proof import proto_receipt
from validate_container_recycle_browser import sql, native_plan
from validate_metric_continuity import query
from container_recycle_restart_proof import same_operation

APPS = Path('/data/WhaleDeck/apps')
DISPLACED = 'recycle-displaced.bin'


def rename_noreplace(fd, source, target):
    libc = ctypes.CDLL(None, use_errno=True)
    libc.renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    libc.renameat2.restype = ctypes.c_int
    if libc.renameat2(fd, os.fsencode(source), fd, os.fsencode(target), 1):
        raise OSError(ctypes.get_errno(), 'OWNED_SOURCE_RENAME_REFUSED')


def displaced_proof(before, after):
    assert before['identity'][:8] == after['identity'][:8] and after['identity'][8] >= before['identity'][8]
    for k in ('sha256', 'attributes', 'sizeBytes'): typed_equal(after[k], before[k])


def displace(f, fixture, held):
    assert os.geteuid() == 0 and fixture['containerId'] == f['identifier'] and re.fullmatch('[a-f0-9]{64}', f['identifier'])
    folder = Path(fixture['folder']); assert folder == f['folder'] and folder.parent == APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', folder.name)
    assert folder.resolve(strict=True) == folder and not folder.is_symlink() and held.proof['twoKernelFdProofs'] and held.proof['sourceNotRemoved']
    assert held.record['plan']['parentIdentity'] == [folder.stat().st_dev, folder.stat().st_ino]
    typed_equal(file_snapshot(folder/'recycle-rpc.bin'), fixture['original'])
    assert not os.path.lexists(folder/DISPLACED) and not select.select([held.descriptor], [], [], 0)[0]
    fd = os.open(folder, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        i = os.fstat(fd); assert [i.st_dev, i.st_ino] == held.record['plan']['parentIdentity'] and i.st_uid == 0 and stat.S_IMODE(i.st_mode) == 0o755
        rename_noreplace(fd, 'recycle-rpc.bin', DISPLACED)
    finally: os.close(fd)
    after = file_snapshot(folder/DISPLACED); displaced_proof(fixture['original'], after)
    assert file_snapshot(folder/'recycle-rpc.bin') is None and not select.select([held.descriptor], [], [], 0)[0]
    return dict(sourceAbsent=True, displaced=after, onlyExactOwnedLeafMoved=True, originalHelperStillAlive=True)


def acknowledgement_audit(execution):
    assert str(uuid.UUID(execution)) == execution
    return query("SELECT json_build_object('count',count(*),'actors',coalesce(json_agg(actor_subject),'[]'::json)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle.unknown-acknowledged';", id=execution)


def prove(value, fixture, e, create=False):
    data = sql(value['id']); r = data['row']; ack = value['state'] == 'Acknowledged'
    assert r['state'] == value['state'] in ('Unknown', 'Acknowledged')
    assert r['actor_subject'] == fixture['subject'] and r['container_id'] == fixture['containerId'] and r['instance_id'] == fixture['instanceId'] and r['relative_path'] == 'work/recycle-rpc.bin'
    assert r['root_started_at_utc'] and r['execution_job_id'] == value['executionJobId'] and r['cancel_requested_at_utc'] is None
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and data['pending'] == 0 and data['accepted'] == data['confirmed'] == 1 and data['planJob']['state'] == 'Succeeded'
    assert r['root_state'] == 'Unknown' and r['source_retained'] is r['source_restored'] is None and r['history_verified'] and r['history_may_exist']
    assert r['publish_barrier'] is (not ack) and bool(r['completed_at_utc']) is ack and bool(r['acknowledged_by']) is ack and bool(r['acknowledgement_json']) is ack
    job = data['executionJob']; assert job['state'] == ('Failed' if ack else 'Running') and job['phase'] == ('Acknowledged' if ack else 'AwaitingContainerRecycleReceipt')
    assert bool(job['completed_at_utc']) is ack and job['cancel_requested_at_utc'] is None
    assert r['result_code'] == ('CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECYCLE_RESULT_UNKNOWN')
    q = dict(operationId=uuid.UUID(r['execution_job_id']).hex, owner=r['owner_hash'], plan=native_plan(json.loads(r['plan_json'])))
    receipt = {k:v for k,v in value['receipt'].items() if k != 'id' and not (k in ('sourceRetained','sourceRestored') and v is None)}
    receipt['operationId'] = q['operationId']; receipt = proto_receipt(receipt, q)
    assert receipt['state'] == 'Unknown' and receipt['sourceRetained'] is receipt['sourceRestored'] is None and receipt['historyDigest'] == r['history_digest']
    records = read_operation(q, receipt, e/'originals', create=create)
    current = physical(Path(fixture['folder']), q['operationId']); assert current['source'] is current['retained'] is None
    moved = file_snapshot(Path(fixture['folder'])/DISPLACED); typed_equal(moved, fixture['displaced']); displaced_proof(fixture['original'], moved)
    result = job['result_json']; result = json.loads(result) if isinstance(result, str) else result
    assert len(result) == 14 and result['state'] == value['state'] and result['receiptState'] == 'Unknown' and result['sourceRetained'] is result['sourceRestored'] is None and result['publishBarrier'] is (not ack)
    audit = acknowledgement_audit(r['execution_job_id']); assert audit['count'] == (1 if ack else 0)
    if ack: assert audit['actors'] == [r['acknowledged_by']] and r['acknowledged_by'] != r['actor_subject']
    return dict(sql=data, request=q, receipt=receipt, records=records, physical=current, displaced=moved, acknowledgementAudit=audit)


def require_ack(before, after, actor):
    old, new = before['sql']['row'], after['sql']['row']; same_operation(old, new)
    assert old['state'] == 'Unknown' and old['publish_barrier'] and new['state'] == 'Acknowledged' and not new['publish_barrier']
    assert actor != old['actor_subject'] and new['acknowledged_by'] == actor and new['version'] > old['version']
    for k in ('root_state','source_retained','source_restored','history_digest','history_may_exist','history_verified','cancel_requested_at_utc'): typed_equal(new[k], old[k])
    facts = lambda r: {k:v for k,v in r.items() if k not in ('replayed','bindingCurrent','historyAvailable')}
    typed_equal(facts(before['receipt']), facts(after['receipt']))
    assert json.loads(new['acknowledgement_json']) == json.loads(new['receipt_json'])
    for k in ('records','physical','displaced'): typed_equal(after[k], before[k])
