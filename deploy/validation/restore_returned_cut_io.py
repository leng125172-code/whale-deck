"""Read current applying/restored originals and HDD without inventing an RPC reply."""
import os,stat
from pathlib import Path
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
from recursive_acl_restore_agent_io import read_at,archive_raw
from container_recycle_restore_rpc_proof import request_proof,fields,RECORD,claim_proof
from container_recycle_rpc_proof import record_proof as source_record_proof
from acl_history_browser_proof import typed_equal


def record_projection(request,root,claim,source,history,data):
    request_proof(request);fields(root,RECORD)
    assert type(root['schemaVersion']) is int and root['schemaVersion']==1
    for k in ('operationId','owner','plan'):typed_equal(root[k],request[k])
    assert root['state']=='Applying' and root['claimed'] is root['historyVerified'] is True and root['sourceOutcome'] is None
    assert root['historyDigest']==request['plan']['command']['historyDigest'] and claim_proof(claim,request)
    ref=request['plan']['command']['source'];p=ref['plan'];c=p['command']
    # A projection for structural source-history validation, explicitly not an observed wire receipt.
    observation=dict(operationId=ref['operationId'],containerId=c['containerId'],instanceId=c['instanceId'],fingerprint=p['fingerprint'],targetProof=p['targetProof'],
        state='Restored',bindingCurrent=False,replayed=True,historyMayExist=True,historyVerified=True,historyDigest=root['historyDigest'],historyAvailable=True,sourceRetained=False,sourceRestored=True)
    source_record_proof(ref,observation,source,history,data_sha256=data['sha256'],data_size=data['sizeBytes'])
    typed_equal(source['privateIdentity'],request['plan']['privateIdentity']);typed_equal(source['actualIdentity'][:8],request['plan']['retainedIdentity'][:8]);assert source['actualIdentity'][8]>=request['plan']['retainedIdentity'][8]
    return dict(sourceHistoryStructurallyVerified=True,hddDataReread=True,restoreTerminalJournalSaved=False,rpcReceiptObserved=False)


def read_pending(request,folder,create=False):
    request_proof(request);ref=request['plan']['command']['source']
    if create:folder.mkdir(mode=0o700)
    fd=restore_io.private_directory(folder);os.close(fd);result={}
    paths=[('root',restore_io.STATE,request['operationId']+'.json','restore-root.json'),('claim',restore_io.STATE/'claims',ref['operationId']+'.json','source-claim.json'),('sourceRoot',source_io.STATE,ref['operationId']+'.json','source-root.json')]
    for key,path,name,archive in paths:
        fd=restore_io.private_directory(path)
        try:raw,value,_=read_at(fd,name);archive_raw(folder,archive,raw,create);result[key]=value
        finally:os.close(fd)
    io=restore_io.installed()
    with source_io.namespace(io.storage) as fd:
        raw,value,_=read_at(fd,ref['operationId']+'.json');archive_raw(folder,'source-hdd.json',raw,create);result['history']=value
        data=source_io.file_snapshot(Path(os.readlink('/proc/self/fd/'+str(fd)))/(ref['operationId']+'.data'))
        assert data is not None and data['identity'][3:6]==[0,0,1] and stat.S_IMODE(data['identity'][2])==0o600;result['data']=data
    result['verification']=record_projection(request,result['root'],result['claim'],result['sourceRoot'],result['history'],result['data'])
    return result
