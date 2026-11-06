"""Independent purge wire/journal/source/HDD join, with no IO or product import.

Actual pre-purge source/history snapshots are required, never reconstructed
observations. Namespace absence must be acquired separately by the owning gate.
"""
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_rpc_proof import fields,number,hash_value,digest,FRAME
from recursive_acl_history_rpc_proof import integer
from container_recycle_restore_rpc_proof import unique_json,identity
import container_recycle_rpc_proof as source

NATIVE_PLAN={'schemaVersion','command','targetProof','parentIdentity','parentVersion','uid','gid','privateIdentity','retainedIdentity','sourceRecordDigest','historyMetadataDigest','historyCreatedAtUnixMs','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','fingerprint'}
PLAN=(NATIVE_PLAN-{'uid','gid'})|{'ownerUid','ownerGid','retainedChangedAtUnixNs','archiveChangedAtUnixNs'}
RECORD={'schemaVersion','operationId','owner','plan','state','pendingIdentity','unlinkConfirmed'}
RECEIPT={'operationId','sourceOperationId','fingerprint','state','sourceFenced','ownedFence'}
PLAN_REFUSALS=18
OPERATION_REFUSALS=26


def capability_refusal(exit_code,stdout,error):
    assert type(exit_code) is int and exit_code==1 and stdout==''
    fields(error,('terminalProbe','phase','code','failureType','rpcStatus','fileCode','capabilityObservation'))
    typed_equal({k:v for k,v in error.items() if k!='capabilityObservation'},dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None))
    c=error['capabilityObservation'];fields(c,('protocolMajor','protocolMinor','sourceCapability','restoreCapability','purgeCapability'))
    integer(c['protocolMajor'],1,1);integer(c['protocolMinor'],57,57)
    assert c['sourceCapability'] is c['restoreCapability'] is True and c['purgeCapability'] is False
    return dict(capabilityRejectedBeforeKey=True,operationFramesSent=0,observedProtocolMajor=1,observedProtocolMinor=57)


def ready_proof(value,header):
    source.ready_proof(value,header);integer(value['protocolMinor'],58,2**32-1)


def directory(value):
    assert type(value) is list and len(value)==2
    integer(value[0],0,2**64-1);integer(value[1],1,2**64-1)


def native_plan_proof(p):
    fields(p,NATIVE_PLAN);integer(p['schemaVersion'],1,1)
    c=p['command'];fields(c,('source','historyDigest','scope'));assert c['scope']=='Originals';fields(c['source'],('operationId','owner','plan'));identity(c['source']['operationId']);hash_value(c['source']['owner']);hash_value(c['historyDigest']);old=c['source']['plan'];source.native_plan_proof(old)
    for k in ('targetProof','parentVersion','sourceRecordDigest','historyMetadataDigest','storageHash','archiveSha256','fingerprint'):hash_value(p[k])
    for k in ('parentIdentity','privateIdentity','storeIdentity'):directory(p[k])
    for k in ('uid','gid'):integer(p[k],0,4294967294);typed_equal(p[k],old[k])
    typed_equal(p['parentIdentity'],old['parentIdentity']);typed_equal(p['targetProof'],old['targetProof'])
    retained=p['retainedIdentity'];archive=p['archiveIdentity']
    for i in (retained,archive):
        assert type(i) is list and len(i)==9 and all(type(n) is int for n in i)
        integer(i[0],0,2**64-1);integer(i[1],1,2**64-1)
        for n in i[7:]:integer(n,-2**63,2**63-1)
    typed_equal(retained[:8],old['sourceIdentity'][:8]);assert retained[8]>=old['sourceIdentity'][8]
    typed_equal(archive[2:6],[stat.S_IFREG|0o600,0,0,1]);integer(archive[8],0,2**63-1)
    integer(p['archiveBytes'],0,1073741824);assert archive[6]==p['archiveBytes']==old['sourceIdentity'][6] and p['archiveSha256']==old['sha256']
    integer(p['historyCreatedAtUnixMs'],0,253402300799999)
    assert p['fingerprint']==digest(b'whaledeck-container-recycle-purge-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})


def proto_plan(value):
    fields(value,PLAN);c=value['command'];fields(c,('source','historyDigest','scope'));s=c['source'];fields(s,('operationId','owner','plan'))
    p={k:v for k,v in value.items() if k not in ('ownerUid','ownerGid','retainedChangedAtUnixNs','archiveChangedAtUnixNs')}
    p['command']={**c,'source':{**s,'plan':source.proto_plan(s['plan'])}};p['uid']=value['ownerUid'];p['gid']=value['ownerGid']
    for k in ('parentIdentity','privateIdentity','storeIdentity'):
        fields(value[k],('device','inode'));p[k]=[number(value[k]['device']),number(value[k]['inode'],1)]
    for k,changed in (('retainedIdentity','retainedChangedAtUnixNs'),('archiveIdentity','archiveChangedAtUnixNs')):
        fields(value[k],FRAME[:8]);i=value[k]
        for key in ('mode','ownerUid','ownerGid'):integer(i[key],0,65535 if key=='mode' else 4294967294)
        p[k]=[number(i[n]) for n in FRAME[:7]]+[number(i['modifiedAtUnixNs'],-2**63,2**63-1),number(value[changed],-2**63,2**63-1)]
    p['historyCreatedAtUnixMs']=number(value['historyCreatedAtUnixMs'],0,253402300799999);p['archiveBytes']=number(value['archiveBytes'],0,1073741824)
    native_plan_proof(p);return p


def request_proof(q):
    fields(q,('operationId','owner','plan'));identity(q['operationId']);hash_value(q['owner']);native_plan_proof(q['plan']);assert q['operationId']!=q['plan']['command']['source']['operationId']


def proto_receipt(value,q):
    request_proof(q);assert type(value) is dict and RECEIPT<=set(value)<=RECEIPT|{'purgeApplied'}
    for k,v in dict(operationId=q['operationId'],sourceOperationId=q['plan']['command']['source']['operationId'],fingerprint=q['plan']['fingerprint']).items():typed_equal(value[k],v)
    state=value['state'];assert state in ('Reserved','Completed','Aborted','Unknown')
    for k in ('sourceFenced','ownedFence'):assert type(value[k]) is bool
    assert not value['ownedFence'] or value['sourceFenced']
    if state=='Unknown':assert value['sourceFenced'] and 'purgeApplied' not in value
    else:
        assert type(value.get('purgeApplied')) is bool and value['purgeApplied'] is (state=='Completed')
        if state in ('Reserved','Completed'):assert value['sourceFenced'] and value['ownedFence']
        else:assert not value['ownedFence']
    return {**value,'purgeApplied':value.get('purgeApplied')}


def plan_frame(frame,header,*,boundaries):
    assert type(boundaries) is bool;fields(frame,('kind','plan','boundaryRefusals'));assert frame['kind']=='plan';integer(frame['boundaryRefusals'],PLAN_REFUSALS if boundaries else 0,PLAN_REFUSALS if boundaries else 0)
    p=proto_plan(unique_json(frame['plan']))
    for k in ('containerId','instanceId'):assert p['command']['source']['plan']['command'][k]==header[k]
    return p


def receipt_frame(frame,q):
    fields(frame,('kind','action','operationId','jobId','result'));assert frame['kind']=='receipt' and frame['action'] in ('reserve','apply','inspect','discard');assert frame['operationId']==frame['jobId']==q['operationId'];return proto_receipt(unique_json(frame['result']),q)


def source_receipt_frame(frame,ref):
    fields(frame,('kind','action','operationId','jobId','result'));assert frame['kind']=='source-receipt' and frame['action'] in ('source-apply','source-inspect') and frame['operationId']==frame['jobId']==ref['operationId']
    value=unique_json(frame['result'])
    if value.get('state')!='Purged':return source.proto_receipt(value,ref)
    fields(value,source.RECEIPT|{'sourceRetained','sourceRestored'})
    c=ref['plan']['command']
    for k,v in dict(operationId=ref['operationId'],containerId=c['containerId'],instanceId=c['instanceId'],fingerprint=ref['plan']['fingerprint'],targetProof=ref['plan']['targetProof']).items():typed_equal(value[k],v)
    for k in ('bindingCurrent','replayed','historyMayExist','historyVerified','historyAvailable','sourceRetained','sourceRestored'):assert type(value[k]) is bool
    assert value['historyMayExist'] and value['historyVerified'] and value['historyAvailable'];hash_value(value['historyDigest']);assert value['bindingCurrent'] is value['sourceRetained'] is value['sourceRestored'] is False
    return value


def record_proof(q,receipt,root,marker,source_root,history,source_before,history_before,data,*,store_identity,storage_hash):
    request_proof(q);p=q['plan'];ref=p['command']['source']
    typed_equal(proto_receipt({k:v for k,v in receipt.items() if k!='purgeApplied' or v is not None},q),receipt)
    # These must be actual saved pre-purge records supplied by the acquisition gate.
    src_receipt=dict(operationId=ref['operationId'],containerId=ref['plan']['command']['containerId'],instanceId=ref['plan']['command']['instanceId'],fingerprint=ref['plan']['fingerprint'],targetProof=ref['plan']['targetProof'],state='Trashed',bindingCurrent=True,replayed=True,historyMayExist=True,historyVerified=True,historyDigest=p['command']['historyDigest'],historyAvailable=True,sourceRetained=True,sourceRestored=False)
    source.record_proof(ref,src_receipt,source_before,history_before,data_sha256=data['sha256'],data_size=data['sizeBytes'])
    assert p['sourceRecordDigest']==digest(b'whaledeck-container-recycle-purge-source-v1\n',source_before)
    assert p['historyMetadataDigest']==digest(b'whaledeck-container-recycle-purge-history-v1\n',history_before)
    typed_equal(p['historyCreatedAtUnixMs'],history_before['createdAtUnixMs']);typed_equal(p['privateIdentity'],source_before['privateIdentity']);typed_equal(p['retainedIdentity'],source_before['actualIdentity'])
    typed_equal(data['identity'],p['archiveIdentity']);typed_equal(data['sizeBytes'],p['archiveBytes']);typed_equal(data['sha256'],p['archiveSha256']);typed_equal(store_identity,p['storeIdentity']);typed_equal(storage_hash,p['storageHash'])
    fields(root,RECORD);integer(root['schemaVersion'],1,1)
    for k in ('operationId','owner','plan'):typed_equal(root[k],q[k])
    assert type(root['unlinkConfirmed']) is bool;typed_equal(root['state'],receipt['state'])
    pending=root['pendingIdentity']
    if pending is not None:
        assert type(pending) is list and len(pending)==9 and all(type(n) is int for n in pending);typed_equal(pending[:8],p['retainedIdentity'][:8]);integer(pending[8],p['retainedIdentity'][8],2**63-1)
    if root['unlinkConfirmed']:assert pending is not None and root['state'] in ('Completed','Unknown')
    matches=False
    if marker is not None:
        fields(marker,('schemaVersion','source','operationId','owner','fingerprint','state'));integer(marker['schemaVersion'],1,1);typed_equal(marker['source'],ref);identity(marker['operationId']);assert marker['operationId']!=ref['operationId'];hash_value(marker['owner']);hash_value(marker['fingerprint']);assert marker['state'] in ('Reserved','Released','Unknown','Deleting','Purged')
        matches=marker['operationId']==q['operationId']
        if matches:typed_equal(marker['owner'],q['owner']);typed_equal(marker['fingerprint'],p['fingerprint'])
    typed_equal(receipt['sourceFenced'],bool(marker and marker['state']!='Released'));typed_equal(receipt['ownedFence'],bool(matches and marker['state']!='Released'))
    if root['state'] in ('Reserved','Aborted'):
        assert pending is None and not root['unlinkConfirmed'];typed_equal(source_root,source_before);typed_equal(history,history_before)
        if root['state']=='Reserved':assert matches and marker['state']=='Reserved'
        elif matches:assert marker['state']=='Released'
    elif root['state']=='Completed':
        assert matches and marker['state']=='Purged' and pending is not None and root['unlinkConfirmed'];typed_equal(source_root,{**source_before,'state':'Purged','actualIdentity':None});typed_equal(history,{**history_before,'captureState':'Purged'})
    else:
        assert receipt['sourceFenced'];assert source_root['state'] in ('Trashed','Purging','Purged','Unknown');typed_equal(source_root,{**source_before,'state':source_root['state'],'actualIdentity':p['retainedIdentity'] if source_root['state']=='Trashed' else None});assert history['captureState'] in ('Trashed','Purged');typed_equal(history,{**history_before,'captureState':history['captureState']})
    return dict(state=root['state'],archiveBytesVerified=True,sourceFenceMatches=matches,physicalNamespaceChecked=False)
