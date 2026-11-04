"""Exact 26-object physical/capture/HTTP proof; no RPC, identities or writes.

The caller may explicitly archive raw immutable records before its read-only
rounds. Verification never constructs substitute HDD metadata.
"""
import base64
import hashlib
import json
import os
import stat

from acl_history_browser_proof import typed_equal
from recursive_acl_history_browser_proof import location,request_proof
from verify_container_recursive_acl import digest,unsigned,file_version,acl_mode
from validate_container_recursive_acl import file_acl,directory_acl
from validate_container_recursive_acl_agent import STATE
from validate_container_recursive_ownership_browser import read_metadata
from validate_container_upload_recovery_agent import trusted

FILES=tuple(f'item-{n:02d}.bin' for n in range(24))+('末项.bin',)
ORDER=(*FILES,'')
PATHS={p:'File' for p in FILES}|{'':'Directory'}

def content(identifier):
    assert len(identifier)==64 and all(v in '0123456789abcdef' for v in identifier)
    return hashlib.shake_256(('recursive-history-paging:'+identifier).encode()).digest(65536+7)

def inventory(root,reader,data,acl):
    assert not root.is_symlink() and stat.S_ISDIR(root.lstat().st_mode)
    assert {p.name for p in root.iterdir()}==set(FILES)
    output={}
    for relative,kind in PATHS.items():
        path=root/relative;info=path.lstat();assert not path.is_symlink()
        assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and path.read_bytes()==data if kind=='File' else stat.S_ISDIR(info.st_mode)
        assert os.getxattr(path,'user.whaledeck-recursive')==b'preserve ordinary attribute'
        assert set(os.listxattr(path))<={'user.whaledeck-recursive',*acl.XATTRS.values()}
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC)
        try:
            assert list(reader.fingerprint(os.fstat(fd)))==list(reader.fingerprint(info))
            output[relative]=dict(identity=list(reader.fingerprint(info)),access=acl.read_acl(fd,'access',info.st_mode),default=acl.read_acl(fd,'default',info.st_mode))
        finally:os.close(fd)
    return output

def remove_proved(root,reader,data,acl,expected):
    assert inventory(root,reader,data,acl)==expected
    for relative in FILES:
        path=root/relative;assert list(reader.fingerprint(path.lstat()))==expected[relative]['identity'];path.unlink()
    assert not list(root.iterdir()) and list(reader.fingerprint(root.lstat()))[:5]==expected['']['identity'][:5]
    root.rmdir()

def summary(row,metadata):
    plan=row['request']['plan'];c=plan['command']
    return dict(captureId=row['request']['operationId'],historyDigest=metadata['historyDigest'],createdAtUnixMs=metadata['createdAtUnixMs'],
        sourceInstanceId=c['instanceId'],changedAclType='access',manifestDigest=plan['manifestDigest'],entryCount=26,fileCount=25,directoryCount=1,changeCount=26)

def verify_capture(evidence,b,row,storage,archive=False):
    typed_equal(row['before'],b['original'])
    request=row['request'];operation=request['operationId'];plan=request['plan'];result=row['result'];before=row['before'];after=row['after']
    assert request['owner']=='B'*64 and before.keys()==after.keys()==PATHS.keys()
    assert [plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[26,25,1,26]
    command=plan['command'];assert command==dict(containerId=b['containerId'],instanceId=b['instanceId'],path='work/recursive-tree',expectedVersion=file_version(b,'work/recursive-tree',before['']['identity']),aclType='access',fileEntries=file_acl(),directoryEntries=directory_acl())
    assert plan['rootIdentity']==before['']['identity'] and (plan['uid'],plan['gid'])==(1000,1000)
    assert digest(b'whaledeck-container-recursive-acl-plan-v1\n',unsigned(plan,'fingerprint'))==plan['fingerprint']
    def raw_record(name,raw):
        target=evidence/'records'/name
        if archive:
            target.parent.mkdir(mode=0o700,exist_ok=True)
            with target.open('xb') as stream:stream.write(raw)
            target.chmod(0o600)
        else:assert target.read_bytes()==raw,'ORIGINAL_CAPTURE_CHANGED'
        return json.loads(raw)
    root=STATE/(operation+'.json');trusted(root);record=raw_record('aggregate.root.json',root.read_bytes())
    assert record['schemaVersion']==1 and type(record['schemaVersion']) is int and all(record[k]==request[k] for k in ('operationId','owner','plan'))
    assert record['state']==result['state']=='Completed' and result['operationId']==operation and result['aclType']=='access'
    assert result['containerId']==b['containerId'] and result['instanceId']==b['instanceId'] and result['replayed'] is False
    assert result['fingerprint']==plan['fingerprint'] and result['targetProof']==plan['targetProof']
    for flag in ('bindingCurrent','historyAvailable','historyVerified','historyMayExist'):assert result[flag] is True
    assert record['historyVerified'] is record['historyMayExist'] is True
    assert [record[k] for k in ('completedCount','changedCount','abortedCount','unknownCount','notStartedCount')]==[26,26,0,0,0]
    for key in ('completedCount','changedCount','abortedCount','unknownCount','notStartedCount'):typed_equal(result[key],record[key])
    attrs=digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]])
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json');metadata=raw_record('aggregate.hdd.json',raw)
            assert metadata['schemaVersion']==1 and type(metadata['schemaVersion']) is int and metadata['resourceType']=='ContainerRecursiveAclMetadata' and metadata['storageId']=='database-platform.backup.hdd'
            assert type(metadata['createdAtUnixMs']) is int and metadata['createdAtUnixMs']>=0 and all(metadata[k]==request[k] for k in ('operationId','owner','plan'))
            assert digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(metadata,'historyDigest'))==metadata['historyDigest']==record['historyDigest']==result['historyDigest']
            items=metadata['entries'];assert [v['relativePath'] for v in items]==list(ORDER)
            assert digest(b'whaledeck-container-recursive-acl-manifest-v1\n',items)==plan['manifestDigest']
            outcomes=[];child=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,child,read_only=True)
                for n,item in enumerate(items):
                    relative=item['relativePath'];old=before[relative];actual=after[relative];p=item['plan'];identity=old['identity'];current=actual['identity'];kind=PATHS[relative]
                    identifier=hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    path=STATE/(operation+'.entries')/(identifier+'.json');trusted(path);cr=raw_record(identifier+'.root.json',path.read_bytes())
                    ch=raw_record(identifier+'.hdd.json',read_metadata(storage.f,storage,configuration,child,identifier+'.json',16384))
                    target=file_acl() if kind=='File' else directory_acl()
                    node='work/recursive-tree'+('/'+relative if relative else '')
                    assert p['sourceIdentity']==identity and p['accessAcl']==old['access'] and p['defaultAcl']==old['default'] and p['attributesDigest']==attrs
                    assert p['command']==dict(containerId=b['containerId'],instanceId=b['instanceId'],path=node,expectedVersion=file_version(b,node,identity),aclType='access',entries=target)
                    assert p['kind']==kind and p['noChange'] is False and p['targetMode']==acl_mode(target)
                    assert digest(b'whaledeck-container-acl-plan-v1\n',unsigned(p,'fingerprint'))==p['fingerprint']
                    assert cr['schemaVersion']==ch['schemaVersion']==1 and type(cr['schemaVersion']) is type(ch['schemaVersion']) is int
                    assert cr['operationId']==ch['operationId']==identifier and cr['owner']==ch['owner']==request['owner'] and cr['plan']==ch['plan']==p
                    assert cr['state']=='Completed' and cr['aclConfirmed'] is cr['historyVerified'] is True and cr['actualIdentity']==current
                    wanted=list(identity);wanted[2]=wanted[2]&~0o777|p['targetMode'];assert current[:8]==wanted[:8]
                    assert actual['access']==target and actual['default']==old['default']
                    assert ch['resourceType']=='ContainerRecursiveAclEntryMetadata' and ch['storageId']=='database-platform.backup.hdd' and ch['parentOperationId']==operation and ch['manifestDigest']==plan['manifestDigest']
                    assert type(ch['createdAtUnixMs']) is int and ch['createdAtUnixMs']>=0
                    assert digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(ch,'historyDigest'))==ch['historyDigest']==cr['historyDigest']
                    published=file_version(b,node,current);assert cr['version']==published
                    outcomes.append(dict(id=identifier,state='Completed',version=published,changed=True))
            finally:os.close(child)
        finally:os.close(fd)
    assert digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)==record['outcomeDigest']==result['outcomeDigest']
    return metadata

def prove_page(signal,b,row,metadata,index):
    assert type(index) is int and 0<=index<4
    offset=(0,25,0,25)[index];end=min(offset+25,26);complete=end==26;s=summary(row,metadata)
    entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in metadata['entries'][offset:end]]
    typed_equal(signal['snapshot'],dict(location=location(b),summary=s,offset=offset,entries=entries,complete=complete,nextOffset=None if complete else end))
    request_proof(signal['request'],b,s['captureId'],s,offset)
    assert signal['index']==index and signal['screenshot']==f'paging-{index}.png' and signal['subject']
    return s
