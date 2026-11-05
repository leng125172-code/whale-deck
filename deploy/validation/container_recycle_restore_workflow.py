"""Closed installed-probe transcript grammar, independent of the restore engine."""
from acl_history_browser_proof import typed_equal
import container_recycle_rpc_proof as source
import container_recycle_restore_rpc_proof as restore

RESTORE_ACTIONS = (('cancel', 'discard', 'Aborted'), ('cancel', 'inspect', 'Aborted'), ('cancel', 'apply', 'Aborted'), ('cancel', 'discard', 'Aborted'),
    ('normal', 'apply', 'Completed'), ('normal', 'inspect', 'Completed'), ('normal', 'apply', 'Completed'), ('normal', 'discard', 'Completed'),
    ('cancel', 'inspect', 'Aborted'), ('cancel', 'apply', 'Aborted'), ('cancel', 'discard', 'Aborted'))


def source_plan(frame, header):
    restore.fields(frame, ('kind', 'plan')); assert frame['kind'] == 'source-plan'
    plan = source.proto_plan(restore.unique_json(frame['plan']))
    for key in ('containerId', 'instanceId'): assert plan['command'][key] == header[key]
    return plan


def source_receipt(frame, request):
    restore.fields(frame, ('kind', 'action', 'operationId', 'jobId', 'result')); assert frame['kind'] == 'source-receipt'
    assert frame['action'] in ('source-apply', 'source-inspect')
    return source.receipt_frame({**frame, 'kind': 'receipt', 'action': frame['action'].removeprefix('source-')}, request)


def transcript_proof(frames, steps, header):
    assert type(frames) is list and type(steps) is list and len(frames) == 19 and len(steps) == 14
    restore.ready_proof(frames[0], header); original = source_plan(frames[1], header)
    first = steps[0]['request']; source_id = first['operationId']; restore.identity(source_id)
    typed_equal(first, dict(operationId=source_id, owner='B'*64, plan=original))
    plan = restore.plan_frame(frames[4], header, boundaries=True)
    typed_equal(plan['command']['source'], first)
    cancel = steps[2]['request']['operationId']; normal = steps[6]['request']['operationId']
    for value in (cancel, normal): restore.identity(value)
    assert len({source_id, cancel, normal}) == 3
    typed_equal(frames[5], dict(kind='rejected', operationId=cancel, refusals=restore.OPERATION_REFUSALS))
    assert frames[-1] == dict(kind='done')
    expected = [('source', source_id, 'source-apply', 'Trashed'), ('source', source_id, 'source-inspect', 'Trashed')]
    expected += [('restore', cancel if key == 'cancel' else normal, action, state) for key, action, state in RESTORE_ACTIONS]
    expected += [('source', source_id, 'source-inspect', 'Restored')]
    wires = frames[2:4] + frames[6:18]
    previous = None
    for step, wire, (phase, op, action, state) in zip(steps, wires, expected, strict=True):
        restore.fields(step, ('phase', 'action', 'request', 'frame', 'receipt', 'records', 'physical'))
        assert step['phase'] == phase and step['action'] == wire['action'] == action
        typed_equal(step['frame'], wire)
        request = first if phase == 'source' else dict(operationId=op, owner='C'*64, plan=plan)
        typed_equal(step['request'], request)
        receipt = source_receipt(wire, request) if phase == 'source' else restore.receipt_frame(wire, request)
        typed_equal(receipt, step['receipt']); assert receipt['state'] == state and receipt['bindingCurrent'] is (state != 'Aborted')
        if phase == 'restore':
            assert receipt['historyVerified'] is (state == 'Completed') and receipt['restoredByThisOperation'] is (state == 'Completed')
            assert receipt['sourceOperationId'] == source_id
        if previous is not None and previous['phase'] == phase and previous['request']['operationId'] == op and previous['receipt']['state'] == state:
            assert receipt['replayed'] is True
            typed_equal(step['records'], previous['records']); typed_equal(step['physical'], previous['physical'])
        previous = step
    assert plan['command']['historyDigest'] == steps[0]['receipt']['historyDigest'] == steps[-1]['receipt']['historyDigest']
    return dict(sourceOperationId=source_id, cancelOperationId=cancel, restoreOperationId=normal, newReceiptFrames=11, sourceReceiptFrames=3)
