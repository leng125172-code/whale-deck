"""Strict successful probe transcript grammar, not physical acceptance.

The installed owning gate must additionally acquire every private record and
check both the namespace and actual HDD, before permitting another mutation.
"""
from acl_history_browser_proof import typed_equal
from container_recycle_purge_rpc_proof import fields,integer,identity,unique_json,ready_proof,plan_frame,receipt_frame,source_receipt_frame,PLAN_REFUSALS,OPERATION_REFUSALS,source

CANCEL=(('reserve','Reserved'),('inspect','Reserved'),('discard','Aborted'),('apply','Aborted'),('inspect','Aborted'))
NORMAL=(('reserve','Reserved'),('inspect','Reserved'),('apply','Completed'),('inspect','Completed'),('apply','Completed'),('discard','Completed'))


def transcript_proof(frames,header):
    assert type(frames) is list and len(frames)==20;ready_proof(frames[0],header)
    fields(frames[1],('kind','plan'));assert frames[1]['kind']=='source-plan';old=source.proto_plan(unique_json(frames[1]['plan']))
    for k in ('containerId','instanceId'):typed_equal(old['command'][k],header[k])
    ref=dict(operationId=frames[2]['operationId'],owner='B'*64,plan=old);identity(ref['operationId'])
    for at,action in ((2,'source-apply'),(3,'source-inspect')):
        assert frames[at]['action']==action;assert source_receipt_frame(frames[at],ref)['state']=='Trashed'
    first=plan_frame(frames[4],header,boundaries=True);typed_equal(first['command']['source'],ref)
    reject=frames[5];fields(reject,('kind','operationId','refusals'));assert reject['kind']=='rejected';integer(reject['refusals'],OPERATION_REFUSALS,OPERATION_REFUSALS);identity(reject['operationId'])
    cancel=reject['operationId'];assert cancel!=ref['operationId']
    for at,(action,state) in enumerate(CANCEL,6):
        assert frames[at]['action']==action;q=dict(operationId=cancel,owner='C'*64,plan=first);assert receipt_frame(frames[at],q)['state']==state
    second=plan_frame(frames[11],header,boundaries=False);typed_equal(second['command']['source'],ref)
    normal=frames[12]['operationId'];identity(normal);assert normal not in (ref['operationId'],cancel)
    for at,(action,state) in enumerate(NORMAL,12):
        assert frames[at]['action']==action;q=dict(operationId=normal,owner='C'*64,plan=second);assert receipt_frame(frames[at],q)['state']==state
    assert frames[18]['action']=='source-inspect';assert source_receipt_frame(frames[18],ref)['state']=='Purged';typed_equal(frames[19],dict(kind='done'))
    return dict(sourceOperationId=ref['operationId'],cancelOperationId=cancel,purgeOperationId=normal,receiptFrames=11,sourceReceiptFrames=3,planRefusals=PLAN_REFUSALS,operationRefusals=OPERATION_REFUSALS,physicalNamespaceChecked=False)
