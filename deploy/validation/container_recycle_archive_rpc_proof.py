"""Independent archive wire/immutable-source/journal join. No product imports.

The owning gate must acquire live target/stage identities and before/after HDD
bytes separately; this module never claims physical namespace acceptance.
"""
import stat
from acl_history_browser_proof import typed_equal
from container_recycle_purge_rpc_proof import fields,integer,number,hash_value,digest,directory,identity,unique_json
import container_recycle_purge_rpc_proof as purge

NATIVE_PLAN={'schemaVersion','command','targetProof','parentIdentity','parentVersion','uid','gid','privateIdentity','deletedIdentity','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','fingerprint'}
PLAN=(NATIVE_PLAN-{'uid','gid'})|{'ownerUid','ownerGid','deletedChangedAtUnixNs','archiveChangedAtUnixNs'}
RECEIPT={'operationId','sourceOperationId','purgeOperationId','fingerprint','state','sourceFenced','ownedFence'}
RECORD={'schemaVersion','operationId','owner','plan','state','privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity','parentAfterStage'}
PLAN_REFUSALS=22
OPERATION_REFUSALS=31


def capability_refusal(exit_code,stdout,error):
    assert type(exit_code) is int and exit_code==1 and stdout==''
    fields(error,('terminalProbe','phase','code','failureType','rpcStatus','fileCode','capabilityObservation'))
    typed_equal({k:v for k,v in error.items() if k!='capabilityObservation'},dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None))
    c=error['capabilityObservation'];fields(c,('protocolMajor','protocolMinor','sourceCapability','restoreCapability','purgeCapability','archiveCapability'))
    integer(c['protocolMajor'],1,1);integer(c['protocolMinor'],58,58);assert c['sourceCapability'] is c['restoreCapability'] is c['purgeCapability'] is True and c['archiveCapability'] is False
    return dict(capabilityRejectedBeforeKey=True,operationFramesSent=0,observedProtocolMajor=1,observedProtocolMinor=58)


def ready_proof(value,header):purge.ready_proof(value,header);integer(value['protocolMinor'],59,2**32-1)


def native_plan_proof(p):
    fields(p,NATIVE_PLAN);integer(p['schemaVersion'],1,1);c=p['command'];fields(c,('purge','historyDigest','mode'));assert c['mode']=='ArchiveCopy'
    ref=c['purge'];fields(ref,('operationId','owner','plan'));identity(ref['operationId']);hash_value(ref['owner']);old=ref['plan'];purge.native_plan_proof(old)
    assert ref['operationId']!=old['command']['source']['operationId'];typed_equal(c['historyDigest'],old['command']['historyDigest'])
    for key in ('targetProof','parentVersion','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','archiveSha256','fingerprint'):hash_value(p[key])
    for key in ('parentIdentity','storeIdentity'):directory(p[key])
    if p['privateIdentity'] is not None:directory(p['privateIdentity']);typed_equal(p['privateIdentity'],old['privateIdentity'])
    for key in ('uid','gid'):integer(p[key],0,4294967294);typed_equal(p[key],old[key])
    for key in ('targetProof','parentIdentity','storeIdentity','storageHash','archiveIdentity','archiveBytes','archiveSha256'):typed_equal(p[key],old[key])
    typed_equal(p['deletedIdentity'],old['retainedIdentity'])
    assert p['fingerprint']==digest(b'whaledeck-container-recycle-archive-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})


def proto_identity(value,changed):
    fields(value,('device','inode','mode','ownerUid','ownerGid','linkCount','sizeBytes','modifiedAtUnixNs'))
    return [number(value[k]) for k in ('device','inode','mode','ownerUid','ownerGid','linkCount','sizeBytes')]+[number(value['modifiedAtUnixNs'],-2**63,2**63-1),number(changed,-2**63,2**63-1)]


def proto_plan(value):
    # Protobuf message absence and explicit JSON null both mean native null.
    assert type(value) is dict and set(value) in (PLAN,PLAN-{'privateIdentity'})
    c=value['command'];fields(c,('purge','historyDigest','mode'));r=c['purge'];fields(r,('operationId','owner','plan'))
    p={k:v for k,v in value.items() if k not in ('ownerUid','ownerGid','deletedChangedAtUnixNs','archiveChangedAtUnixNs')}
    p['command']={**c,'purge':{**r,'plan':purge.proto_plan(r['plan'])}};p['uid']=value['ownerUid'];p['gid']=value['ownerGid']
    for key in ('parentIdentity','storeIdentity'):
        fields(value[key],('device','inode'));p[key]=[number(value[key]['device']),number(value[key]['inode'],1)]
    private=value.get('privateIdentity');p['privateIdentity']=None
    if private is not None:fields(private,('device','inode'));p['privateIdentity']=[number(private['device']),number(private['inode'],1)]
    p['deletedIdentity']=proto_identity(value['deletedIdentity'],value['deletedChangedAtUnixNs']);p['archiveIdentity']=proto_identity(value['archiveIdentity'],value['archiveChangedAtUnixNs']);p['archiveBytes']=number(value['archiveBytes']);native_plan_proof(p);return p


def request_proof(q):
    fields(q,('operationId','owner','plan'));identity(q['operationId']);hash_value(q['owner']);native_plan_proof(q['plan']);ref=q['plan']['command']['purge'];assert q['operationId'] not in (ref['operationId'],ref['plan']['command']['source']['operationId'])


def proto_receipt(value,q):
    request_proof(q);assert type(value) is dict and RECEIPT<=set(value)<=RECEIPT|{'archiveCopied','bindingCurrent','publishedIdentity','publishedChangedAtUnixNs'}
    p=q['plan'];ref=p['command']['purge'];typed_equal(value['operationId'],q['operationId']);typed_equal(value['sourceOperationId'],ref['plan']['command']['source']['operationId']);typed_equal(value['purgeOperationId'],ref['operationId']);typed_equal(value['fingerprint'],p['fingerprint'])
    state=value['state'];assert state in ('Reserved','Aborted','Unknown','Completed');assert type(value['sourceFenced']) is type(value['ownedFence']) is bool and (not value['ownedFence'] or value['sourceFenced'])
    copied=value.get('archiveCopied');binding=value.get('bindingCurrent');assert copied is None or type(copied) is bool;assert binding is None or type(binding) is bool
    if state=='Unknown':assert 'archiveCopied' not in value and value['sourceFenced']
    elif state=='Completed':assert copied is True and value['sourceFenced']
    else:assert copied is False
    if state=='Reserved':assert value['ownedFence'] and value['sourceFenced']
    if state=='Aborted':assert not value['ownedFence']
    if binding is True:assert state=='Completed' and value['ownedFence']
    published=value.get('publishedIdentity');changed=value.get('publishedChangedAtUnixNs')
    if state=='Completed':
        assert published is not None and changed is not None;published=proto_identity(published,changed);original=ref['plan']['command']['source']['plan']['sourceIdentity']
        assert published[:2]!=original[:2] and published[:2]!=p['archiveIdentity'][:2] and published[0]==p['parentIdentity'][0] and published[1]>0;typed_equal(published[2:8],original[2:8]);integer(published[8],0,2**63-1)
    else:assert published is None and changed is None
    return {**{k:value[k] for k in RECEIPT},'archiveCopied':copied,'bindingCurrent':binding,'publishedIdentity':published}


def plan_frame(frame,header,boundaries):
    fields(frame,('kind','plan','boundaryRefusals'));assert frame['kind']=='archive-plan';integer(frame['boundaryRefusals'],PLAN_REFUSALS if boundaries else 0,PLAN_REFUSALS if boundaries else 0)
    p=proto_plan(unique_json(frame['plan']));command=p['command']['purge']['plan']['command']['source']['plan']['command']
    for key in ('containerId','instanceId'):typed_equal(command[key],header[key])
    assert command['path']=='work/recycle-rpc.bin';return p


def receipt_frame(frame,q):
    fields(frame,('kind','action','operationId','jobId','result'));assert frame['kind']=='archive-receipt';typed_equal(frame['operationId'],q['operationId']);typed_equal(frame['jobId'],q['operationId']);assert frame['action'] in ('archive-reserve','archive-apply','archive-inspect','archive-discard');return proto_receipt(unique_json(frame['result']),q)


def record_proof(q,receipt,root,claim,source_root,purge_root,marker,history,data,*,source_before,purge_before,marker_before,history_before,data_before,store_identity,storage_hash):
    request_proof(q);p=q['plan'];ref=p['command']['purge'];source=ref['plan']['command']['source'];fields(root,RECORD);integer(root['schemaVersion'],1,1)
    fields(receipt,RECEIPT|{'archiveCopied','bindingCurrent','publishedIdentity'});wire={k:receipt[k] for k in RECEIPT}
    for key in ('archiveCopied','bindingCurrent'):
        if receipt[key] is not None:wire[key]=receipt[key]
    published=receipt['publishedIdentity']
    if published is not None:
        assert type(published) is list and len(published)==9 and all(type(n) is int for n in published)
        wire['publishedIdentity']=dict(zip(purge.FRAME[:8],published[:8],strict=True));wire['publishedChangedAtUnixNs']=published[8]
    typed_equal(proto_receipt(wire,q),receipt);typed_equal(store_identity,p['storeIdentity']);typed_equal(storage_hash,p['storageHash'])
    for key in ('operationId','owner','plan'):typed_equal(root[key],q[key])
    typed_equal(root['state'],receipt['state']);assert root['state'] in ('Reserved','Aborted','Unknown','Completed')
    for actual,before in ((source_root,source_before),(purge_root,purge_before),(marker,marker_before),(history,history_before),(data,data_before)):typed_equal(actual,before)
    for actual,expected in ((source_root,source),(purge_root,ref)):
        for key in ('operationId','owner','plan'):typed_equal(actual[key],expected[key])
    typed_equal(marker['source'],source);typed_equal(marker['operationId'],ref['operationId']);typed_equal(marker['owner'],ref['owner']);typed_equal(marker['fingerprint'],ref['plan']['fingerprint'])
    typed_equal(data['identity'],p['archiveIdentity']);typed_equal(data['sizeBytes'],p['archiveBytes']);typed_equal(data['sha256'],p['archiveSha256'])
    assert source_root['state']=='Purged' and source_root['actualIdentity'] is None and purge_root['state']=='Completed' and purge_root['unlinkConfirmed'] is True and marker['state']=='Purged' and history['captureState']=='Purged'
    for key,domain,value in (('sourceRecordDigest',b'archive-source-v1\n',source_root),('purgeRecordDigest',b'archive-purge-v1\n',purge_root),('purgeMarkerDigest',b'archive-marker-v1\n',marker),('historyMetadataDigest',b'archive-history-v1\n',history)):typed_equal(p[key],digest(domain,value))
    matches=False
    if claim is not None:
        fields(claim,('schemaVersion','source','restoreOperationId','restoreOwner','restoreFingerprint'));integer(claim['schemaVersion'],1,1);typed_equal(claim['source'],source);identity(claim['restoreOperationId']);hash_value(claim['restoreOwner']);hash_value(claim['restoreFingerprint'])
        matches=claim['restoreOperationId']==q['operationId'] and claim['restoreOwner']==q['owner'] and claim['restoreFingerprint']==p['fingerprint']
    typed_equal(receipt['sourceFenced'],claim is not None);typed_equal(receipt['ownedFence'],matches)
    for key in ('privateIdentity','pendingIdentity'):
        if root[key] is not None:directory(root[key]);typed_equal(root[key][0],p['parentIdentity'][0])
    for key in ('preparedIdentity','publishedIdentity'):
        i=root[key]
        if i is not None:
            assert type(i) is list and len(i)==9 and all(type(n) is int for n in i);typed_equal(i[:2],root['pendingIdentity']);assert i[:2]!=p['deletedIdentity'][:2] and i[:2]!=p['archiveIdentity'][:2];typed_equal(i[2:8],p['deletedIdentity'][2:8]);integer(i[8],0,2**63-1)
    if root['state'] in ('Reserved','Aborted'):assert all(root[k] is None for k in ('privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity')) and root['parentAfterStage']==''
    if root['state']=='Completed':
        assert all(root[k] is not None for k in ('privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity'));hash_value(root['parentAfterStage']);typed_equal(root['publishedIdentity'],receipt['publishedIdentity']);assert root['publishedIdentity'][8]>=root['preparedIdentity'][8]
    return dict(immutableSourcesJoined=True,sourceClaimMatches=matches,physicalNamespaceChecked=False)
