"""Independent bounded wire/Root/Trash-HDD proof; no file acquisition or mutation.

Data bytes, namespace identity and cleanup must be checked by the owning gate.
"""
import base64
import json
import re
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_rpc_proof import fields,number,hash_value,digest,FRAME,instance
from recursive_acl_history_rpc_proof import integer
from verify_container_recursive_acl import file_version

PLAN={'schemaVersion','command','targetProof','parentIdentity','parentVersion','ownerUid','ownerGid','fingerprint','sourceIdentity','sourceChangedAtUnixNs','sha256','attributes'}
RECEIPT={'operationId','containerId','instanceId','fingerprint','targetProof','state','bindingCurrent','replayed','historyMayExist','historyVerified','historyDigest','historyAvailable'}
ROOT={'schemaVersion','operationId','owner','plan','state','privateIdentity','historyMayExist','historyVerified','historyDigest','actualIdentity'}
PATH='work/recycle-rpc.bin'
HEADER={'revision','probeRevision','containerId','instanceId','coreId'}
PLAN_REFUSALS=17
OPERATION_REFUSALS=14


def capability_refusal(exit_code,stdout,error):
    assert type(exit_code) is int and exit_code==1 and stdout==''
    fields(error,('terminalProbe','phase','code','failureType','rpcStatus','fileCode'))
    assert error['terminalProbe']=='failed' and error['phase']=='capabilities'
    assert error['code']=='CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE' and error['failureType']=='InvalidOperationException'
    assert error['rpcStatus'] is error['fileCode'] is None
    return dict(capabilityRejectedBeforeKey=True,operationFramesSent=0)


def ready_proof(value,header):
    fields(header,HEADER);fields(value,('kind','revision','probeRevision','protocolMinor'))
    for key in ('revision','probeRevision'):assert re.fullmatch('[a-f0-9]{40}',header[key]) and value[key]==header[key]
    assert value['kind']=='ready';integer(value['protocolMinor'],56,2**32-1)
    for key in ('containerId','coreId'):assert type(header[key]) is str and re.fullmatch('[a-f0-9]{64}',header[key])
    assert header['containerId']!=header['coreId'];instance(header['instanceId'])


def proto_plan(value):
    fields(value,PLAN);c=value['command'];fields(c,('location','expectedVersion'));location=c['location'];fields(location,('containerId','instanceId','path'))
    assert type(location['containerId']) is str and re.fullmatch('[a-f0-9]{64}',location['containerId']);instance(location['instanceId']);assert location['path']==PATH
    hash_value(c['expectedVersion']);source=value['sourceIdentity'];fields(source,FRAME[:8]);parent=value['parentIdentity'];fields(parent,('device','inode'))
    for key in ('mode','ownerUid','ownerGid'):integer(source[key],0,65535 if key=='mode' else 4294967294)
    frame=[number(source['device']),number(source['inode'],1),source['mode'],source['ownerUid'],source['ownerGid'],number(source['linkCount'],1,1),number(source['sizeBytes'],0,1073741824),number(source['modifiedAtUnixNs'],-2**63,2**63-1),number(value['sourceChangedAtUnixNs'],-2**63,2**63-1)]
    assert stat.S_ISREG(frame[2]) and not frame[2]&0o7000
    attrs=value['attributes'];assert type(attrs) is list and len(attrs)<=32;output=[];size=0
    for entry in attrs:
        fields(entry,('name','value'));name=entry['name'];text=entry['value'];assert type(name) is str and name.startswith('user.') and '\0' not in name and type(text) is str
        encoded=name.encode('utf-8');assert len(encoded)<=255
        raw=base64.b64decode(text,validate=True);assert base64.b64encode(raw).decode('ascii')==text
        size+=len(encoded)+len(raw);assert size<=4096;output.append([name,text])
    assert [v[0] for v in output]==sorted({v[0] for v in output},key=lambda n:n.encode('utf-8'))
    integer(value['schemaVersion'],1,1)
    for key in ('ownerUid','ownerGid'):integer(value[key],0,4294967294)
    for key in ('targetProof','parentVersion','fingerprint','sha256'):hash_value(value[key])
    plan=dict(schemaVersion=1,command={**location,'expectedVersion':c['expectedVersion']},targetProof=value['targetProof'],parentIdentity=[number(parent['device']),number(parent['inode'],1)],
        parentVersion=value['parentVersion'],uid=value['ownerUid'],gid=value['ownerGid'],fingerprint=value['fingerprint'],sourceIdentity=frame,sha256=value['sha256'],attributes=output)
    assert file_version(plan['command'],PATH,frame)==plan['command']['expectedVersion']
    assert digest(b'whaledeck-container-recycle-plan-v1\n',{k:v for k,v in plan.items() if k!='fingerprint'})==plan['fingerprint']
    return plan


def proto_receipt(value,request):
    assert value.get('state') in ('Trashed','Restored','Aborted','Unknown')
    optional={'sourceRetained','sourceRestored'};fields(value,RECEIPT if value['state']=='Unknown' else RECEIPT|optional)
    c=request['plan']['command'];assert value['operationId']==request['operationId'] and value['containerId']==c['containerId'] and value['instanceId']==c['instanceId']
    assert value['fingerprint']==request['plan']['fingerprint'] and value['targetProof']==request['plan']['targetProof']
    for key in ('bindingCurrent','replayed','historyMayExist','historyVerified','historyAvailable'):assert type(value[key]) is bool
    if value['historyVerified']:assert value['historyMayExist'];hash_value(value['historyDigest'])
    else:assert value['historyDigest']=='' and value['historyAvailable'] is False
    if value['bindingCurrent']:assert value['state'] in ('Trashed','Restored') and value['historyAvailable'] and value['historyVerified']
    if value['state'] in ('Trashed','Restored'):assert value['historyVerified']
    result=dict(value)
    if value['state']=='Unknown':result.update(sourceRetained=None,sourceRestored=None)
    else:
        assert type(value['sourceRetained']) is type(value['sourceRestored']) is bool
        assert value['sourceRetained'] is (value['state']=='Trashed') and value['sourceRestored'] is (value['state']=='Restored')
    return result


def plan_frame(frame,header,*,boundaries):
    assert type(boundaries) is bool;fields(frame,('kind','plan','boundaryRefusals'))
    assert frame['kind']=='plan';integer(frame['boundaryRefusals'],PLAN_REFUSALS if boundaries else 0,PLAN_REFUSALS if boundaries else 0)
    import json
    plan=proto_plan(json.loads(frame['plan']))
    for key in ('containerId','instanceId'):assert plan['command'][key]==header[key]
    return plan


def receipt_frame(frame,request):
    fields(frame,('kind','action','operationId','jobId','result'));assert frame['kind']=='receipt' and frame['operationId']==request['operationId']
    assert type(frame['jobId']) is str and re.fullmatch('[a-f0-9]{32}',frame['jobId']) and int(frame['jobId'],16)>0
    assert frame['action'] in ('apply','inspect','discard','restore')
    assert (frame['jobId']!=request['operationId']) if frame['action']=='restore' else frame['jobId']==request['operationId']
    assert type(frame['result']) is str and len(frame['result'].encode())<=65536
    def unique(pairs):
        result={}
        for k,v in pairs:assert k not in result;result[k]=v
        return result
    return proto_receipt(json.loads(frame['result'],object_pairs_hook=unique),request)


def record_proof(request,receipt,root,history=None,*,data_sha256=None,data_size=None):
    fields(request,('operationId','owner','plan'));assert re.fullmatch('[a-f0-9]{32}',request['operationId']) and int(request['operationId'],16)>0;hash_value(request['owner'])
    wire={k:v for k,v in receipt.items() if k not in ('sourceRetained','sourceRestored') or v is not None}
    typed_equal(proto_receipt(wire,request),receipt)
    plan=request['plan'];hash_value(plan['fingerprint'])
    assert plan['fingerprint']==digest(b'whaledeck-container-recycle-plan-v1\n',{k:v for k,v in plan.items() if k!='fingerprint'})
    assert file_version(plan['command'],plan['command']['path'],plan['sourceIdentity'])==plan['command']['expectedVersion']
    fields(root,ROOT);integer(root['schemaVersion'],1,1)
    for key in ('operationId','owner','plan'):typed_equal(root[key],request[key])
    assert root['state'] in ('Trashed','Restored','Aborted','Unknown')
    for key in ('state','historyMayExist','historyVerified','historyDigest'):typed_equal(root[key],receipt[key])
    assert type(root['historyMayExist']) is type(root['historyVerified']) is bool
    actual=root['actualIdentity'];private=root['privateIdentity']
    if private is not None:
        assert type(private) is list and len(private)==2
        integer(private[0],0,2**64-1);integer(private[1],1,2**64-1)
    if root['state'] in ('Trashed','Restored'):
        assert root['historyVerified'] and private is not None and type(actual) is list and len(actual)==9
        assert all(type(v) is int for v in actual);typed_equal(actual[:8],request['plan']['sourceIdentity'][:8]);assert actual[8]>=request['plan']['sourceIdentity'][8]
    else:assert actual is None
    if root['historyVerified']:
        fields(history,('schemaVersion','resourceType','storageId','operationId','owner','plan','sizeBytes','sha256','captureKind','state','captureState','createdAtUnixMs','differentSourceDevice','historyDigest'))
        integer(history['schemaVersion'],1,1);integer(history['createdAtUnixMs'],0,253402300799999);assert type(history['differentSourceDevice']) is bool
        for key in ('operationId','owner','plan'):typed_equal(history[key],request[key])
        assert history['resourceType']=='ContainerFileTrash' and history['storageId']=='database-platform.backup.hdd' and history['captureKind']=='Trash' and history['state']=='Verified'
        assert history['captureState'] in ('Pending','Trashed','Restored','Canceled')
        if root['state']!='Unknown':assert history['captureState']=={'Trashed':'Trashed','Restored':'Restored','Aborted':'Canceled'}[root['state']]
        integer(history['sizeBytes'],0,1073741824)
        assert type(data_size) is int and data_size==history['sizeBytes']==request['plan']['sourceIdentity'][6]
        assert data_sha256==history['sha256']==request['plan']['sha256'];hash_value(data_sha256)
        assert history['historyDigest']==root['historyDigest']==digest(b'whaledeck-container-recycle-history-v1\n',{k:v for k,v in history.items() if k not in ('state','captureState','historyDigest')})
    else:assert root['historyDigest']==''
    return dict(state=root['state'],historyVerified=root['historyVerified'],actualIdentityPresent=actual is not None,physicalNamespaceChecked=False)
