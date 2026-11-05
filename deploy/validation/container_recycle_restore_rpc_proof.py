"""Independent restore wire/journal/claim/source-HDD projection.

No product modules, IO or mutation. The owning gate must acquire trusted originals
and independently inspect the physical namespace; state labels alone are not proof.
"""
import json
import re
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_rpc_proof import fields, number, hash_value, digest, FRAME
from recursive_acl_history_rpc_proof import integer
import container_recycle_rpc_proof as source_proof

PLAN = {'schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'ownerUid', 'ownerGid', 'privateIdentity', 'retainedIdentity', 'retainedChangedAtUnixNs', 'fingerprint'}
NATIVE_PLAN = {'schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'uid', 'gid', 'privateIdentity', 'retainedIdentity', 'fingerprint'}
RECORD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'claimed', 'historyVerified', 'historyDigest', 'sourceOutcome'}
RECEIPT = {'operationId', 'sourceOperationId', 'fingerprint', 'targetProof', 'state', 'bindingCurrent', 'replayed', 'historyVerified', 'historyDigest', 'historyAvailable'}
OPTIONAL = {'sourceState', 'sourceRetained', 'sourceRestored', 'restoredByThisOperation'}
PLAN_REFUSALS = 16
OPERATION_REFUSALS = 16


def unique_json(text):
    assert type(text) is str and 0 < len(text.encode('utf-8')) <= 65536
    def unique(pairs):
        value = {}
        for key, item in pairs:
            assert key not in value
            value[key] = item
        return value
    return json.loads(text, object_pairs_hook=unique, parse_constant=lambda _: (_ for _ in ()).throw(AssertionError()))


def identity(value):
    assert type(value) is str and re.fullmatch('[a-f0-9]{32}', value) and int(value, 16) > 0


def capability_refusal(exit_code, stdout, error):
    assert type(exit_code) is int and exit_code == 1 and stdout == ''
    fields(error, ('terminalProbe', 'phase', 'code', 'failureType', 'rpcStatus', 'fileCode'))
    typed_equal(error, dict(terminalProbe='failed', phase='capabilities', code='CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE', failureType='InvalidOperationException', rpcStatus=None, fileCode=None))
    return dict(capabilityRejectedBeforeKey=True, operationFramesSent=0)


def ready_proof(value, header):
    source_proof.ready_proof(value, header)
    integer(value['protocolMinor'], 57, 2**32-1)


def native_plan_proof(plan):
    fields(plan, NATIVE_PLAN); integer(plan['schemaVersion'], 1, 1)
    c = plan['command']; fields(c, ('source', 'historyDigest')); s = c['source']
    fields(s, ('operationId', 'owner', 'plan')); identity(s['operationId']); hash_value(s['owner']); hash_value(c['historyDigest'])
    source_proof.native_plan_proof(s['plan']); original = s['plan']
    for key in ('targetProof', 'parentVersion', 'fingerprint'): hash_value(plan[key])
    for key in ('parentIdentity', 'privateIdentity'):
        value = plan[key]; assert type(value) is list and len(value) == 2
        integer(value[0], 0, 2**64-1); integer(value[1], 1, 2**64-1)
    typed_equal(plan['parentIdentity'], original['parentIdentity'])
    for key in ('uid', 'gid'):
        integer(plan[key], 0, 2**32-2); typed_equal(plan[key], original[key])
    assert plan['targetProof'] == original['targetProof']
    retained = plan['retainedIdentity']; assert type(retained) is list and len(retained) == 9
    assert all(type(v) is int for v in retained)
    typed_equal(retained[:8], original['sourceIdentity'][:8]); integer(retained[8], original['sourceIdentity'][8], 2**63-1)
    assert plan['fingerprint'] == digest(b'whaledeck-container-recycle-restore-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})


def proto_plan(value):
    fields(value, PLAN); command = value['command']; fields(command, ('source', 'historyDigest')); source = command['source']; fields(source, ('operationId', 'owner', 'plan'))
    i = value['retainedIdentity']; fields(i, FRAME[:8]); parent = value['parentIdentity']; private = value['privateIdentity']
    fields(parent, ('device', 'inode')); fields(private, ('device', 'inode'))
    for key in ('mode', 'ownerUid', 'ownerGid'): integer(i[key], 0, 65535 if key == 'mode' else 2**32-2)
    retained = [number(i[k], -2**63 if k == 'modifiedAtUnixNs' else 0, 2**63-1 if k in ('sizeBytes', 'modifiedAtUnixNs') else 2**64-1) if k not in ('mode', 'ownerUid', 'ownerGid') else i[k] for k in FRAME[:8]]
    retained.append(number(value['retainedChangedAtUnixNs'], -2**63, 2**63-1))
    result = dict(schemaVersion=value['schemaVersion'], command=dict(source=dict(operationId=source['operationId'], owner=source['owner'], plan=source_proof.proto_plan(source['plan'])), historyDigest=command['historyDigest']),
        targetProof=value['targetProof'], parentIdentity=[number(parent['device']), number(parent['inode'], 1)], parentVersion=value['parentVersion'], uid=value['ownerUid'], gid=value['ownerGid'],
        privateIdentity=[number(private['device']), number(private['inode'], 1)], retainedIdentity=retained, fingerprint=value['fingerprint'])
    native_plan_proof(result)
    return result


def request_proof(request):
    fields(request, ('operationId', 'owner', 'plan')); identity(request['operationId']); hash_value(request['owner']); native_plan_proof(request['plan'])
    assert request['operationId'] != request['plan']['command']['source']['operationId']


def proto_receipt(value, request):
    request_proof(request); assert type(value) is dict
    assert RECEIPT <= set(value) <= RECEIPT | OPTIONAL
    plan = request['plan']; source = plan['command']['source']
    for key, expected in dict(operationId=request['operationId'], sourceOperationId=source['operationId'], fingerprint=plan['fingerprint'], targetProof=plan['targetProof']).items(): typed_equal(value[key], expected)
    state = value['state']; assert state in ('Completed', 'Aborted', 'Unknown')
    for key in ('bindingCurrent', 'replayed', 'historyVerified', 'historyAvailable'): assert type(value[key]) is bool
    assert value['historyDigest'] == (plan['command']['historyDigest'] if value['historyVerified'] else '')
    assert not value['historyAvailable'] or value['historyVerified']
    assert not value['bindingCurrent'] or state == 'Completed' and value['historyVerified'] and value['historyAvailable']
    # JSON null is not protobuf optional presence. Known false must be present.
    for key in OPTIONAL & set(value): assert type(value[key]) is (str if key == 'sourceState' else bool)
    result = {**value, **{k: None for k in OPTIONAL - set(value)}}
    assert result['sourceState'] in (None, 'Trashed', 'Restored', 'Aborted', 'Unknown')
    if state == 'Completed':
        assert value['historyVerified']; typed_equal({k: result[k] for k in OPTIONAL}, dict(sourceState='Restored', sourceRetained=False, sourceRestored=True, restoredByThisOperation=True))
    elif state == 'Unknown':
        assert value['historyVerified'] and all(result[k] is None for k in ('sourceRetained', 'sourceRestored', 'restoredByThisOperation'))
    else:
        assert result['restoredByThisOperation'] is False
        typed_equal({k: result[k] for k in ('sourceState', 'sourceRetained', 'sourceRestored')}, dict(sourceState='Trashed', sourceRetained=True, sourceRestored=False) if value['historyVerified'] else dict(sourceState=None, sourceRetained=None, sourceRestored=None))
    return result


def plan_frame(frame, header, *, boundaries):
    assert type(boundaries) is bool; fields(frame, ('kind', 'plan', 'boundaryRefusals')); assert frame['kind'] == 'plan'
    integer(frame['boundaryRefusals'], PLAN_REFUSALS if boundaries else 0, PLAN_REFUSALS if boundaries else 0)
    plan = proto_plan(unique_json(frame['plan']))
    for key in ('containerId', 'instanceId'): assert plan['command']['source']['plan']['command'][key] == header[key]
    return plan


def receipt_frame(frame, request):
    fields(frame, ('kind', 'action', 'operationId', 'jobId', 'result')); assert frame['kind'] == 'receipt' and frame['action'] in ('apply', 'inspect', 'discard')
    assert frame['operationId'] == frame['jobId'] == request['operationId']
    return proto_receipt(unique_json(frame['result']), request)


def claim_proof(claim, request):
    if claim is None: return False
    fields(claim, ('schemaVersion', 'source', 'restoreOperationId', 'restoreOwner', 'restoreFingerprint')); integer(claim['schemaVersion'], 1, 1)
    typed_equal(claim['source'], request['plan']['command']['source']); identity(claim['restoreOperationId']); hash_value(claim['restoreOwner']); hash_value(claim['restoreFingerprint'])
    assert claim['restoreOperationId'] != claim['source']['operationId']
    matches = claim['restoreOperationId'] == request['operationId']
    if matches: assert claim['restoreOwner'] == request['owner'] and claim['restoreFingerprint'] == request['plan']['fingerprint']
    return matches


def record_proof(request, receipt, root, claim, source_root, history, *, data_sha256, data_size):
    request_proof(request)
    typed_equal(proto_receipt({k: v for k, v in receipt.items() if k not in OPTIONAL or v is not None}, request), receipt)
    fields(root, RECORD); integer(root['schemaVersion'], 1, 1)
    for key in ('operationId', 'owner', 'plan'): typed_equal(root[key], request[key])
    for key in ('state', 'historyVerified', 'historyDigest'): typed_equal(root[key], receipt[key])
    assert type(root['claimed']) is type(root['historyVerified']) is bool and root['claimed'] == root['historyVerified']
    typed_equal(root['sourceOutcome'], receipt['sourceState'])
    matching = claim_proof(claim, request)
    assert matching if root['state'] in ('Completed', 'Unknown') else not matching
    source = request['plan']['command']['source']; fields(source_root, source_proof.ROOT)
    assert source_root['historyVerified'] is True and source_root['historyDigest'] == request['plan']['command']['historyDigest']
    typed_equal(source_root['privateIdentity'], request['plan']['privateIdentity'])
    source_state = source_root['state']; assert source_state in ('Trashed', 'Restored', 'Unknown')
    sr = dict(operationId=source['operationId'], containerId=source['plan']['command']['containerId'], instanceId=source['plan']['command']['instanceId'], fingerprint=source['plan']['fingerprint'], targetProof=source['plan']['targetProof'], state=source_state,
        bindingCurrent=False, replayed=True, historyMayExist=source_root['historyMayExist'], historyVerified=True, historyDigest=source_root['historyDigest'], historyAvailable=True,
        sourceRetained=None if source_state == 'Unknown' else source_state == 'Trashed', sourceRestored=None if source_state == 'Unknown' else source_state == 'Restored')
    source_proof.record_proof(source, sr, source_root, history, data_sha256=data_sha256, data_size=data_size)
    if root['state'] == 'Completed' or root['state'] == 'Aborted' and root['claimed']: assert source_state == root['sourceOutcome']
    if source_state in ('Trashed', 'Restored'):
        typed_equal(source_root['actualIdentity'][:8], request['plan']['retainedIdentity'][:8]); assert source_root['actualIdentity'][8] >= request['plan']['retainedIdentity'][8]
    return dict(state=root['state'], sourceState=source_state, sourceClaimMatches=matching, historyBytesVerified=True, physicalNamespaceChecked=False)
