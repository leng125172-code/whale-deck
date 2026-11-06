"""Strict parent-paced three-operation transcript; no physical acceptance."""
from acl_history_browser_proof import typed_equal
import container_recycle_purge_workflow as purge
from container_recycle_archive_rpc_proof import ready_proof,plan_frame,receipt_frame,fields,integer,identity,PLAN_REFUSALS,OPERATION_REFUSALS

CANCEL=tuple(('archive-'+action,state) for action,state in purge.CANCEL)
NORMAL=tuple(('archive-'+action,state) for action,state in purge.NORMAL)


def prefix_proof(frames,header):
    """Live prefix only: explicitly not a closed/finished probe transcript."""
    assert type(frames) is list and len(frames)==34;ready_proof(frames[0],header)
    old=purge.transcript_proof(frames[:19]+[dict(kind='done')],header)
    first=plan_frame(frames[19],header,True);typed_equal(first['command']['purge']['operationId'],old['purgeOperationId'])
    reject=frames[20];fields(reject,('kind','operationId','refusals'));assert reject['kind']=='archive-rejected';integer(reject['refusals'],OPERATION_REFUSALS,OPERATION_REFUSALS);cancel=reject['operationId'];identity(cancel)
    assert cancel not in (old['sourceOperationId'],old['purgeOperationId'],old['cancelOperationId'])
    for at,(action,state) in enumerate(CANCEL,21):
        assert frames[at]['action']==action;assert receipt_frame(frames[at],dict(operationId=cancel,owner='D'*64,plan=first))['state']==state
    second=plan_frame(frames[26],header,False);typed_equal(second['command'],first['command']);normal=frames[27]['operationId'];identity(normal)
    assert len({normal,cancel,old['sourceOperationId'],old['purgeOperationId'],old['cancelOperationId']})==5
    for at,(action,state) in enumerate(NORMAL,27):
        assert frames[at]['action']==action;receipt=receipt_frame(frames[at],dict(operationId=normal,owner='D'*64,plan=second));assert receipt['state']==state
        if state=='Completed':assert receipt['bindingCurrent'] is True
    source_ref=second['command']['purge']['plan']['command']['source'];assert frames[33]['action']=='source-inspect';assert purge.source_receipt_frame(frames[33],source_ref)['state']=='Purged'
    return dict(**old,archiveCancelOperationId=cancel,archiveOperationId=normal,archiveReceiptFrames=11,archivePlanRefusals=PLAN_REFUSALS,archiveOperationRefusals=OPERATION_REFUSALS,sourceReceiptFramesAfterArchive=4,closed=False,afterCleanupBindingObserved=False)


def transcript_proof(frames,header):
    assert type(frames) is list and len(frames)==36;summary=prefix_proof(frames[:34],header);typed_equal(frames[-1],dict(kind='done'))
    p=plan_frame(frames[26],header,False);frame=frames[34];assert frame['action']=='archive-inspect';r=receipt_frame(frame,dict(operationId=summary['archiveOperationId'],owner='D'*64,plan=p));assert r['state']=='Completed' and r['archiveCopied'] is True and r['bindingCurrent'] is False
    summary.update(archiveReceiptFrames=12,afterCleanupBindingObserved=True)
    summary['closed']=True;return summary
