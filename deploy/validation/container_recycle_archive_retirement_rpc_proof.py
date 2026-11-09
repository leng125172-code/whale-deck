"""Independent RPC/Root byte-record consistency, never deletion authority.

No product imports, clock, acquisition, mutation or scheduler. Fixed RPC QA
scope uses work/recycle-rpc.bin. A separate trusted physical acquisition and
before/after HDD acceptance are mandatory even for a Completed certificate.
"""
import math
import re
import hashlib
from acl_history_browser_proof import typed_equal
from archive_retirement_preexpiry_proof import canonical,digest,raw_record_proof,private_file
from container_recycle_purge_rpc_proof import fields,integer,number,hash_value,directory,identity,unique_json as base_json
import container_recycle_archive_rpc_proof as archive

MAX_TIME=253402300799999
NATIVE_PLAN={'schemaVersion','command','archiveRecordDigest','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','claimDigest','historyMetadataDigest','historyCreatedAtUnixMs','notBeforeUnixMs','retentionElapsed','bindingCurrent','retirementEligible','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','publishedIdentity','fingerprint'}
PLAN=NATIVE_PLAN|{'archiveChangedAtUnixNs','publishedChangedAtUnixNs'}
RECEIPT={'operationId','sourceOperationId','archiveOperationId','fingerprint','state','sourceFenced','ownedFence','notBeforeUnixMs'}
RECORD={'schemaVersion','operationId','owner','plan','state'}
ORIGINALS={'archive','restoreClaim','source','purge','purgeMarker','history','storeIdentity','storageHash'}
ROOT_ORIGINALS=ORIGINALS-{'storeIdentity','storageHash'}


def unique_json(text):
    value=base_json(text);pending=[(value,1)]
    while pending:
        item,depth=pending.pop()
        if type(item) is float:assert math.isfinite(item)
        if isinstance(item,(dict,list)):
            assert depth<=24
            pending.extend((v,depth+1) for v in (item.values() if isinstance(item,dict) else item))
    return value


def source_ref(p):return p['command']['archive']['plan']['command']['purge']['plan']['command']['source']


def native_plan_proof(p):
    fields(p,NATIVE_PLAN);integer(p['schemaVersion'],1,1);c=p['command'];fields(c,('archive','retainDays','mode'));assert c['mode']=='ArchiveData';integer(c['retainDays'],1,3650)
    a=c['archive'];archive.request_proof(a);old=a['plan'];ref=source_ref(p);original=ref['plan']['sourceIdentity']
    for k in ('archiveRecordDigest','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','claimDigest','historyMetadataDigest','storageHash','archiveSha256','fingerprint'):hash_value(p[k])
    integer(p['historyCreatedAtUnixMs'],0,MAX_TIME);integer(p['notBeforeUnixMs'],0,MAX_TIME)
    assert p['historyCreatedAtUnixMs']==old['command']['purge']['plan']['historyCreatedAtUnixMs'] and p['notBeforeUnixMs']==p['historyCreatedAtUnixMs']+c['retainDays']*86400000
    for k in ('retentionElapsed','bindingCurrent','retirementEligible'):assert type(p[k]) is bool
    assert p['retirementEligible']==(p['retentionElapsed'] and p['bindingCurrent'])
    for k in ('sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256'):typed_equal(p[k],old[k])
    directory(p['storeIdentity']);i=p['publishedIdentity'];assert type(i) is list and len(i)==9
    for index,n in enumerate(i):integer(n,-2**63 if index==7 else 0,2**63-1 if index>=6 else 2**64-1)
    assert i[0]==old['parentIdentity'][0] and i[1]>0 and i[:2]!=original[:2] and i[:2]!=p['archiveIdentity'][:2] and i[5]==1
    typed_equal(i[2:8],original[2:8]);assert p['fingerprint']==digest(b'whaledeck-container-recycle-archive-retention-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})


def proto_identity(value,changed):
    fields(value,archive.purge.FRAME[:8])
    for k in ('mode','ownerUid','ownerGid'):integer(value[k],0,65535 if k=='mode' else 4294967294)
    return [number(value[k]) for k in archive.purge.FRAME[:7]]+[number(value['modifiedAtUnixNs'],-2**63,2**63-1),number(changed,0,2**63-1)]


def proto_plan(value):
    fields(value,PLAN);c=value['command'];fields(c,('archive','retainDays','mode'));a=c['archive'];fields(a,('operationId','owner','plan'))
    p={k:v for k,v in value.items() if k not in ('archiveChangedAtUnixNs','publishedChangedAtUnixNs')}
    p['command']={**c,'archive':{**a,'plan':archive.proto_plan(a['plan'])}}
    store=value['storeIdentity'];fields(store,('device','inode'));p['storeIdentity']=[number(store['device']),number(store['inode'],1)]
    for k,changed in (('archiveIdentity','archiveChangedAtUnixNs'),('publishedIdentity','publishedChangedAtUnixNs')):p[k]=proto_identity(value[k],value[changed])
    for k in ('historyCreatedAtUnixMs','notBeforeUnixMs'):p[k]=number(value[k],0,MAX_TIME)
    p['archiveBytes']=number(value['archiveBytes'],0,1073741824);native_plan_proof(p);return p


def request_proof(q):
    fields(q,('operationId','owner','plan'));identity(q['operationId']);hash_value(q['owner']);native_plan_proof(q['plan'])
    a=q['plan']['command']['archive'];assert len({q['operationId'],a['operationId'],a['plan']['command']['purge']['operationId'],source_ref(q['plan'])['operationId']})==4


def proto_request(value):
    fields(value,('operationId','owner','plan','context'));q={k:value[k] for k in ('operationId','owner')};q['plan']=proto_plan(value['plan']);request_proof(q)
    c=value['context'];fields(c,('actorSubject','traceId','jobId','idempotencyKey','capability'))
    assert c['actorSubject']=='installed-recycle-retirement-probe' and c['traceId']=='owned-retirement-validation' and c['jobId']==q['operationId'] and c['capability']==''
    identity(c['idempotencyKey']);assert q['owner']=='E'*64
    return q,dict(c)


def native_receipt_proof(value,q):
    request_proof(q);fields(value,RECEIPT|{'retirementApplied'});p=q['plan'];a=p['command']['archive']
    for k,v in dict(operationId=q['operationId'],sourceOperationId=source_ref(p)['operationId'],archiveOperationId=a['operationId'],fingerprint=p['fingerprint'],notBeforeUnixMs=p['notBeforeUnixMs']).items():typed_equal(value[k],v)
    state=value['state'];assert state in ('Reserved','Aborted','Unknown','Completed')
    for k in ('sourceFenced','ownedFence'):assert type(value[k]) is bool
    assert not value['ownedFence'] or value['sourceFenced'];applied=value['retirementApplied'];assert applied is None or type(applied) is bool
    if state=='Reserved':assert value['sourceFenced'] and value['ownedFence'] and applied is False
    elif state=='Aborted':assert not value['ownedFence'] and applied is False
    elif state=='Unknown':assert value['sourceFenced'] and applied is not True
    else:assert value['sourceFenced'] and value['ownedFence'] and applied is True


def proto_receipt(value,q):
    assert type(value) is dict and RECEIPT<=set(value)<=RECEIPT|{'retirementApplied'}
    if 'retirementApplied' in value:assert type(value['retirementApplied']) is bool
    r={**value,'notBeforeUnixMs':number(value['notBeforeUnixMs'],0,MAX_TIME),'retirementApplied':value.get('retirementApplied')};native_receipt_proof(r,q);return r


def plan_frame(frame,header,approved_archive):
    fields(frame,('kind','plan','replayRefused'));assert frame['kind']=='retirement-plan' and frame['replayRefused'] is True
    p=proto_plan(unique_json(frame['plan']));typed_equal(p['command']['archive'],approved_archive);c=source_ref(p)['plan']['command']
    for k in ('containerId','instanceId'):typed_equal(c[k],header[k])
    return p


def receipt_frame(frame,q,*,expected_context=None):
    fields(frame,('kind','action','operationId','request','result'));assert frame['kind']=='retirement-receipt' and frame['action'] in ('reserve','apply','inspect','discard');typed_equal(frame['operationId'],q['operationId'])
    projected,context=proto_request(unique_json(frame['request']));typed_equal(projected,q)
    if expected_context is not None:typed_equal(context,expected_context)
    return dict(action=frame['action'],request=projected,context=context,receipt=proto_receipt(unique_json(frame['result']),q))


def capability_refusal(exit_code,stdout,error,*,sent_lines):
    assert type(exit_code) is int and exit_code==1 and stdout=='' and type(sent_lines) is list and len(sent_lines)==1
    header=unique_json(sent_lines[0]);fields(header,('revision','probeRevision','containerId','instanceId','coreId'))
    for key in ('revision','probeRevision','containerId','coreId'):
        assert type(header[key]) is str and re.fullmatch('[a-f0-9]{'+str(40 if key in ('revision','probeRevision') else 64)+'}',header[key])
    assert header['containerId']!=header['coreId'];archive.purge.source.instance(header['instanceId'])
    fields(error,('terminalProbe','phase','code','failureType','rpcStatus','fileCode','capabilityObservation'))
    typed_equal({k:v for k,v in error.items() if k!='capabilityObservation'},dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None))
    typed_equal(error['capabilityObservation'],dict(protocolMajor=1,protocolMinor=59,retirementCapability=False))
    return dict(oldInstallationGateConsistent=True,operationFramesSent=0,physicalAcceptance=False)


def completion_proof(value,q,observed_at):
    fields(value,('executionPlan','unlinkConfirmed','unlinkedIdentity','completedAtUnixMs','proofDigest'));p=value['executionPlan'];native_plan_proof(p)
    assert p['retentionElapsed'] is p['bindingCurrent'] is p['retirementEligible'] is True and value['unlinkConfirmed'] is True
    stable=lambda v:{k:x for k,x in v.items() if k not in ('fingerprint','retentionElapsed','retirementEligible')}
    typed_equal(stable(p),stable(q['plan']));typed_equal(value['unlinkedIdentity'],p['archiveIdentity']);integer(value['completedAtUnixMs'],p['notBeforeUnixMs'],observed_at)
    assert value['proofDigest']==digest(b'whaledeck-container-recycle-archive-retirement-completion-v1\n',{k:v for k,v in value.items() if k!='proofDigest'})


def record_proof(q,receipt,root,marker,originals,*,originals_before,original_frames,frames_before,registry,root_frames,observed_at):
    """Join acquired raw private records, not physical .data removal/namespace."""
    native_receipt_proof(receipt,q);integer(observed_at,0,MAX_TIME);fields(originals,ORIGINALS);typed_equal(originals,originals_before);fields(original_frames,ROOT_ORIGINALS);typed_equal(original_frames,frames_before)
    for key in ROOT_ORIGINALS:raw_record_proof(original_frames[key],originals[key])
    p=q['plan'];a=p['command']['archive'];ap=a['plan'];purge=ap['command']['purge'];ref=source_ref(p);source_id=ref['operationId']
    for record,shape in ((originals['archive'],archive.RECORD),(originals['purge'],archive.purge.RECORD),(originals['source'],archive.purge.source.ROOT)):
        fields(record,shape);integer(record['schemaVersion'],1,1)
    for record,request in ((originals['archive'],a),(originals['purge'],purge),(originals['source'],ref)):
        for k in ('operationId','owner','plan'):typed_equal(record[k],request[k])
    assert originals['archive']['state']=='Completed' and originals['purge']['state']=='Completed' and originals['purge']['unlinkConfirmed'] is True
    assert originals['source']['state']=='Purged' and originals['source']['actualIdentity'] is None and originals['source']['historyMayExist'] is True and originals['source']['historyVerified'] is True and originals['source']['historyDigest']==ap['command']['historyDigest']
    typed_equal(originals['purgeMarker'],dict(schemaVersion=1,source=ref,operationId=purge['operationId'],owner=purge['owner'],fingerprint=purge['plan']['fingerprint'],state='Purged'))
    assert originals['history']['captureState']=='Purged' and originals['history']['createdAtUnixMs']==p['historyCreatedAtUnixMs']
    typed_equal(originals['restoreClaim'],dict(schemaVersion=1,source=ref,restoreOperationId=a['operationId'],restoreOwner=a['owner'],restoreFingerprint=ap['fingerprint']))
    for key,domain,target in (('archiveRecordDigest',b'archive-retention-copy-v1\n','archive'),('claimDigest',b'archive-retention-claim-v1\n','restoreClaim'),('sourceRecordDigest',b'archive-source-v1\n','source'),('purgeRecordDigest',b'archive-purge-v1\n','purge'),('purgeMarkerDigest',b'archive-marker-v1\n','purgeMarker'),('historyMetadataDigest',b'archive-history-v1\n','history')):typed_equal(p[key],digest(domain,originals[target]))
    copied=originals['archive']
    for name in ('privateIdentity','pendingIdentity'):directory(copied[name])
    hash_value(copied['parentAfterStage']);prepared=copied['preparedIdentity'];published=copied['publishedIdentity']
    assert type(prepared) is list and type(published) is list and len(prepared)==len(published)==9
    typed_equal(copied['privateIdentity'][0],ap['parentIdentity'][0]);typed_equal(copied['pendingIdentity'],published[:2]);typed_equal(prepared[:8],published[:8]);integer(prepared[8],0,published[8])
    typed_equal(originals['archive']['publishedIdentity'],p['publishedIdentity']);typed_equal(originals['storeIdentity'],p['storeIdentity']);typed_equal(originals['storageHash'],p['storageHash'])
    # All registry records must be represented by acquired raw frames. This
    # qualification module intentionally supports only the fixed RPC QA scope.
    assert type(root_frames) is dict and len(root_frames)<=20000
    for name,frame in root_frames.items():
        assert type(name) is str and re.fullmatch(r'(?:[a-f0-9]{32}\.(?:json|lock)|claims/[a-f0-9]{32}\.json)',name)
        info=private_file(frame)
        if name.endswith('.lock'):assert info[6]==0 and frame['sha256']==hashlib.sha256(b'').hexdigest()
        else:assert 0<info[6]<=65536
    assert type(registry) is dict and set(registry)=={name[:-5] for name in root_frames if '/' not in name and name.endswith('.json')}
    active=[]
    for op,record in registry.items():
        identity(op);version=record['schemaVersion'];integer(version,1,2);fields(record,RECORD if version==1 else RECORD|{'completion'})
        request={k:record[k] for k in ('operationId','owner','plan')};request_proof(request);typed_equal(record['operationId'],op)
        assert record['state'] in (('Preparing','Reserved','Aborted','Unknown') if version==1 else ('Removing','Removed','Completed','Unknown'))
        if version==2 and record['completion'] is not None:completion_proof(record['completion'],request,observed_at)
        if version==2 and record['state'] in ('Removed','Completed'):assert record['completion'] is not None
        if version==2 and record['state']=='Removing':assert record['completion'] is None
        raw_record_proof(root_frames[op+'.json'],record)
        if source_ref(record['plan'])['operationId']==source_id and record['state'] in ('Preparing','Reserved','Removing','Removed','Unknown','Completed'):active.append(op)
    typed_equal(registry[q['operationId']],root)
    for k in ('operationId','owner','plan'):typed_equal(root[k],q[k])
    typed_equal(root['state'],receipt['state']);owned=False
    if marker is not None:
        fields(marker,('schemaVersion','archive','retirementOperationId','retirementOwner','retirementFingerprint','state'));integer(marker['schemaVersion'],1,1);archive.request_proof(marker['archive']);identity(marker['retirementOperationId']);hash_value(marker['retirementOwner']);hash_value(marker['retirementFingerprint']);assert marker['state'] in ('Reserved','Unknown','Released','Retiring','Retired')
        assert marker['archive']['plan']['command']['purge']['plan']['command']['source']['operationId']==source_id
        raw_record_proof(root_frames['claims/'+source_id+'.json'],marker)
        owned=marker['retirementOperationId']==q['operationId'] and marker['retirementOwner']==q['owner'] and marker['retirementFingerprint']==p['fingerprint'] and canonical(marker['archive'])==canonical(a) and marker['state']!='Released'
    else:assert 'claims/'+source_id+'.json' not in root_frames
    typed_equal(receipt['sourceFenced'],bool(active or marker and marker['state']!='Released'));typed_equal(receipt['ownedFence'],owned)
    if root['schemaVersion']==1 and root['state']=='Unknown':assert marker is None or marker['state'] not in ('Retiring','Retired')
    if root['state']=='Completed':assert root['schemaVersion']==2 and marker is not None and marker['state']=='Retired' and owned
    elif root['state'] in ('Reserved','Aborted'):assert root['schemaVersion']==1
    if root['state']=='Reserved':assert marker['state']=='Reserved' and owned
    applied=False if root['schemaVersion']==1 else True if root['state']=='Completed' else None;typed_equal(receipt['retirementApplied'],applied)
    return dict(rootRecordsJoined=True,originalRecordsPreserved=True,completionCertificateBound=root['schemaVersion']==2 and root.get('completion') is not None,physicalDataDeletionProved=False,physicalNamespaceAccepted=False)
