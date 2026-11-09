"""Fixed installed-path read-only retirement acquisition for owned RPC QA.

No creation, journal repair, mutation executor, RPC, cleanup or retry. Caller
must start Python with -B before importing, freeze the collector, and verify
the owned fixture/core scope separately. The library assertion is only a sanity check.
Two matching acquisitions are a bounded observation, not future immutability.
"""
from contextlib import contextmanager
import fcntl
import base64
import hashlib
import os
from pathlib import Path
import re
import stat
import sys
import time
from types import SimpleNamespace
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import frame
from container_recycle_restore_agent_io import private_directory
from validate_container_file_staging import load
import container_recycle_archive_retirement_rpc_proof as proof

STATE=Path('/var/lib/whaledeck-container-recycle-archive-retirements')
SOURCE=Path('/var/lib/whaledeck-container-recycling')
PURGE=Path('/var/lib/whaledeck-container-recycle-purges')
ARCHIVE=Path('/var/lib/whaledeck-container-recycle-archives')
CLAIMS=Path('/var/lib/whaledeck-container-recycle-restores/claims')
MODULES=frozenset(('whaledeck-container-files.py','whaledeck-files.py','whaledeck-container-terminal.py','whaledeck-file-backups.py'))
MAX_ENTRIES=20000
MAX_BYTES=2*1024**3
MAX_REGISTRY_BYTES=32*1024**2


def _module(name):
    assert name in MODULES
    path=Path('/usr/local/libexec')/name;i=path.lstat();parent=path.parent.lstat()
    assert path.resolve(strict=True)==path and stat.S_ISREG(i.st_mode) and i.st_uid==0 and i.st_nlink==1 and not i.st_mode&0o022 and i.st_size<=131072
    assert stat.S_ISDIR(parent.st_mode) and parent.st_uid==0 and not parent.st_mode&0o022
    return load(str(path),'retirement_readonly_'+name.replace('-','_').replace('.','_'))


def installed_io():
    files=_module('whaledeck-files.py');reader=_module('whaledeck-container-files.py')
    return SimpleNamespace(files=files,reader=reader,docker=_module('whaledeck-container-terminal.py'),storage=_module('whaledeck-file-backups.py').FileBackups(files))


@contextmanager
def _private(path):
    fd=private_directory(path)
    try:
        before=frame(os.fstat(fd));yield fd
        assert frame(os.fstat(fd))==before==frame(path.lstat())
    finally:os.close(fd)


def _json(parent,name):
    fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=parent)
    try:
        i=os.fstat(fd);before=frame(i)
        assert stat.S_ISREG(i.st_mode) and (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink)==(0,0,0o600,1) and 0<i.st_size<=65536
        raw=bytearray()
        while len(raw)<=i.st_size:
            part=os.read(fd,min(65536,i.st_size+1-len(raw)))
            if not part:break
            raw.extend(part)
        assert len(raw)==i.st_size and before==frame(os.fstat(fd))==frame(os.stat(name,dir_fd=parent,follow_symlinks=False))
        return proof.unique_json(bytes(raw).decode('utf-8')),dict(identity=before,sha256=hashlib.sha256(raw).hexdigest())
    finally:os.close(fd)


def _file(parent,name,deadline,maximum,*,empty_attrs=False):
    fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=parent)
    try:
        i=os.fstat(fd);before=frame(i);assert stat.S_ISREG(i.st_mode) and i.st_nlink==1 and 0<=i.st_size<=maximum
        names=sorted(os.listxattr(fd));assert len(names)<=32 and (not empty_attrs or not names);attrs=[];size=0
        for key in names:
            assert key.startswith('user.');raw=os.getxattr(fd,key);size+=len(key.encode('utf-8'))+len(raw);assert len(key.encode('utf-8'))<=255 and size<=4096
            attrs.append([key,base64.b64encode(raw).decode('ascii')])
        digest=hashlib.sha256();count=0
        while True:
            assert time.monotonic()<=deadline;part=os.read(fd,65536)
            if not part:break
            count+=len(part);assert count<=i.st_size;digest.update(part)
        assert count==i.st_size and before==frame(os.fstat(fd))==frame(os.stat(name,dir_fd=parent,follow_symlinks=False))
        return dict(identity=before,attributes=attrs,sha256=digest.hexdigest().upper(),sizeBytes=count)
    finally:os.close(fd)


def _names(fd,limit):
    names=[]
    with os.scandir(fd) as entries:
        for entry in entries:
            names.append(entry.name);assert len(names)<=limit
    return names


def registry_snapshot(deadline=None):
    """Missing installation fails without creating its root/claims/lock."""
    deadline=time.monotonic()+120 if deadline is None else deadline
    frames={};records={};markers={};total=0
    with _private(STATE) as root,_private(STATE/'claims') as claims:
        assert not os.path.samestat(os.fstat(root),os.fstat(claims))
        assert os.path.samestat(os.fstat(claims),os.stat('claims',dir_fd=root,follow_symlinks=False))
        names=_names(root,MAX_ENTRIES);children=_names(claims,MAX_ENTRIES)
        assert 'claims' in names and len(names)-1+len(children)<=MAX_ENTRIES
        for name in sorted(names):
            assert time.monotonic()<=deadline
            if name=='claims':continue
            assert re.fullmatch(r'[a-f0-9]{32}\.(?:json|lock)',name)
            if name.endswith('.json'):
                info=os.stat(name,dir_fd=root,follow_symlinks=False);assert total+info.st_size<=MAX_REGISTRY_BYTES
                records[name[:-5]],frames[name]=_json(root,name);total+=frames[name]['identity'][6];assert total<=MAX_REGISTRY_BYTES
            else:
                fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=root)
                try:
                    i=os.fstat(fd);assert (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink,i.st_size)==(0,0,0o600,1,0) and stat.S_ISREG(i.st_mode)
                    assert not os.read(fd,1) and frame(os.fstat(fd))==frame(i)==frame(os.stat(name,dir_fd=root,follow_symlinks=False))
                    frames[name]=dict(identity=frame(i),sha256=hashlib.sha256(b'').hexdigest())
                finally:os.close(fd)
        for name in sorted(children):
            assert time.monotonic()<=deadline and re.fullmatch(r'[a-f0-9]{32}\.json',name)
            info=os.stat(name,dir_fd=claims,follow_symlinks=False);assert total+info.st_size<=MAX_REGISTRY_BYTES
            markers[name[:-5]],frames['claims/'+name]=_json(claims,name);total+=frames['claims/'+name]['identity'][6];assert total<=MAX_REGISTRY_BYTES
        assert sorted(children)==sorted(_names(claims,MAX_ENTRIES)) and sorted(names)==sorted(_names(root,MAX_ENTRIES))
        assert os.path.samestat(os.fstat(claims),os.stat('claims',dir_fd=root,follow_symlinks=False))
    return dict(frames=frames,records=records,markers=markers)


def _originals(q,hdd,configuration):
    a=q['plan']['command']['archive'];purge=a['plan']['command']['purge'];ref=proof.source_ref(q['plan']);source=ref['operationId'];records={};frames={}
    for key,path,name in (('archive',ARCHIVE,a['operationId']+'.json'),('source',SOURCE,source+'.json'),('purge',PURGE,purge['operationId']+'.json'),('purgeMarker',SOURCE,source+'.purge.json'),('restoreClaim',CLAIMS,source+'.json')):
        with _private(path) as fd:records[key],frames[key]=_json(fd,name)
    records['history'],frames['history']=_json(hdd,source+'.json')
    i=os.fstat(hdd);records.update(storeIdentity=[i.st_dev,i.st_ino],storageHash=proof.digest(b'',configuration))
    proof.originals_proof(q,records,frames)
    return records,frames


def _payloads(fd,deadline):
    names=_names(fd,MAX_ENTRIES);before=frame(os.fstat(fd));total=0;result={}
    for name in sorted(names):
        assert re.fullmatch(r'[a-f0-9]{32}\.(?:json|data|partial)|history\.lock',name) and time.monotonic()<=deadline
        value=_file(fd,name,deadline,min(1073741824,MAX_BYTES-total),empty_attrs=True)
        i=value['identity'];assert i[3:6]==[0,0,1] and stat.S_IMODE(i[2])==0o600
        total+=value['sizeBytes'];assert total<=MAX_BYTES
        if name.endswith('.json'):
            _,actual=_json(fd,name);typed_equal(actual['identity'],i);typed_equal(actual['sha256'].upper(),value['sha256'])
        result[name]=value
    assert sorted(names)==sorted(_names(fd,MAX_ENTRIES)) and before==frame(os.fstat(fd))
    return result


def _namespace(parent,folder,q,session,deadline):
    assert folder.is_absolute() and folder.resolve(strict=True)==folder
    before=frame(os.fstat(parent));assert os.path.samestat(os.fstat(parent),folder.lstat()) and before[:2]==q['plan']['command']['archive']['plan']['parentIdentity']
    assert before[3:5]==[0,0] and not stat.S_IMODE(before[2])&0o7022
    allowed={'recycle-rpc.bin','.whaledeck-container-recycling-v1','.whaledeck-container-archive-'+q['plan']['command']['archive']['operationId']}
    names=_names(parent,len(allowed));assert set(names)<=allowed;result={}
    for name in sorted(names):
        session.revalidate()
        if name=='recycle-rpc.bin':
            typed_equal(frame(os.stat(name,dir_fd=parent,follow_symlinks=False)),q['plan']['publishedIdentity'])
            result[name]=dict(kind='file',**_file(parent,name,deadline,16777216));continue
        child=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
        try:
            i=os.fstat(child);assert (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))==(0,0,0o700) and os.path.samestat(i,os.stat(name,dir_fd=parent,follow_symlinks=False))
            c=_names(child,2);ref=proof.source_ref(q['plan']);purge=q['plan']['command']['archive']['plan']['command']['purge']
            valid={'copy.partial'} if name.startswith('.whaledeck-container-archive-') else {ref['operationId']+'.trash',purge['operationId']+'.purging'}
            assert set(c)<=valid;entries={n:_file(child,n,deadline,16777216) for n in sorted(c)}
            assert all(entries.values()) and frame(i)==frame(os.fstat(child))==frame(os.stat(name,dir_fd=parent,follow_symlinks=False)) and sorted(c)==sorted(_names(child,2))
            result[name]=dict(kind='directory',identity=frame(i),entries=entries)
        finally:os.close(child)
    live=result.get('recycle-rpc.bin');assert live is not None
    typed_equal(live['identity'],q['plan']['publishedIdentity']);typed_equal(live['sha256'],q['plan']['archiveSha256']);typed_equal(live['attributes'],proof.source_ref(q['plan'])['plan']['attributes'])
    assert sorted(names)==sorted(_names(parent,len(allowed))) and before==frame(os.fstat(parent))==frame(folder.lstat());session.revalidate()
    return dict(parentIdentity=before[:2],parentFrame=before,entries=result)


def capture(q,folder):
    """Positive-path coherent acquisition; loss/unsafe scope fails closed."""
    assert sys.dont_write_bytecode, 'START_COLLECTOR_WITH_BYTECODE_DISABLED'
    proof.request_proof(q);assert proof.source_ref(q['plan'])['plan']['sourceIdentity'][6]<=16777216
    folder=Path(folder);assert folder.is_absolute() and folder.resolve(strict=True)==folder
    deadline=time.monotonic()+120;start=time.time_ns()//1000000
    first=registry_snapshot(deadline)  # before any installed module/provider load
    io=installed_io()
    c=proof.source_ref(q['plan'])['plan']['command'];assert c['path']=='work/recycle-rpc.bin'
    with io.reader.ContainerRoot(c,io.files,io.docker) as session,io.storage.target(create_namespace=False,read_only=True) as (configuration,base):
        hdd=io.files.open_at(base,'container-recycling-v1',os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            info=os.fstat(hdd);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);io.storage.attest(configuration,hdd,read_only=True)
            lock=os.open('history.lock',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=hdd)
            try:
                li=os.fstat(lock);assert stat.S_ISREG(li.st_mode) and (li.st_uid,li.st_gid,stat.S_IMODE(li.st_mode),li.st_nlink,li.st_size)==(0,0,0o600,1,0)
                fcntl.flock(lock,fcntl.LOCK_SH|fcntl.LOCK_NB)
                parent=io.files.open_at(session.root,'work',os.O_RDONLY|os.O_DIRECTORY,cross_mount=True)
                try:
                    def once():
                        assert time.monotonic()<=deadline;session.revalidate();io.storage.attest(configuration,hdd,read_only=True)
                        typed_equal(proof.digest(b'',io.storage.configuration()),proof.digest(b'',configuration))
                        originals,frames=_originals(q,hdd,configuration)
                        namespace=_namespace(parent,folder,q,session,deadline)
                        value=dict(registry=registry_snapshot(deadline),originals=originals,originalFrames=frames,hdd=_payloads(hdd,deadline),namespace=namespace)
                        assert frame(li)==frame(os.fstat(lock))==frame(os.stat('history.lock',dir_fd=hdd,follow_symlinks=False))
                        assert os.path.samestat(os.fstat(hdd),os.stat('container-recycling-v1',dir_fd=base,follow_symlinks=False));io.storage.attest(configuration,hdd,read_only=True);session.revalidate();return value
                    value=once();typed_equal(value['registry'],first);typed_equal(value,once())
                finally:os.close(parent)
            finally:os.close(lock)
        finally:os.close(hdd)
    end=time.time_ns()//1000000;proof.integer(start,0,proof.MAX_TIME);proof.integer(end,start,proof.MAX_TIME);assert time.monotonic()<=deadline
    value.update(request=q,clockBeforeUnixMs=start,clockAfterUnixMs=end)
    return value


def physical_delta_proof(before,after,receipt):
    """Consistency of two trusted captures; no assertion of RPC/auth provenance."""
    q=before['request'];typed_equal(after['request'],q);proof.request_proof(q);source=proof.source_ref(q['plan'])['operationId'];op=q['operationId']
    proof.integer(before['clockBeforeUnixMs'],q['plan']['notBeforeUnixMs'],proof.MAX_TIME)
    proof.integer(before['clockAfterUnixMs'],before['clockBeforeUnixMs'],proof.MAX_TIME)
    proof.integer(after['clockBeforeUnixMs'],before['clockAfterUnixMs'],proof.MAX_TIME)
    proof.integer(after['clockAfterUnixMs'],after['clockBeforeUnixMs'],proof.MAX_TIME)
    first=before['registry'];last=after['registry'];root=last['records'][op];marker=last['markers'][source]
    typed_equal(first['records'][op],dict(schemaVersion=1,**q,state='Reserved'))
    typed_equal(first['markers'][source],dict(schemaVersion=1,archive=q['plan']['command']['archive'],retirementOperationId=op,retirementOwner=q['owner'],retirementFingerprint=q['plan']['fingerprint'],state='Reserved'))
    proof.raw_record_proof(first['frames'][op+'.json'],first['records'][op])
    proof.raw_record_proof(first['frames']['claims/'+source+'.json'],first['markers'][source])
    assert receipt['state']==root['state']=='Completed'
    proof.record_proof(q,receipt,root,marker,after['originals'],originals_before=before['originals'],original_frames=after['originalFrames'],frames_before=before['originalFrames'],registry=last['records'],root_frames=last['frames'],observed_at=after['clockAfterUnixMs'])
    proof.integer(root['completion']['completedAtUnixMs'],before['clockAfterUnixMs'],after['clockBeforeUnixMs'])
    typed_equal(before['namespace'],after['namespace'])
    a=before['hdd'];b=after['hdd'];data=source+'.data';meta=source+'.json'
    assert set(a)-set(b)=={data} and not set(b)-set(a) and meta in a and meta in b and source+'.partial' not in a and source+'.partial' not in b
    for name in b:typed_equal(a[name],b[name])
    payload=a[data];typed_equal(payload['identity'],q['plan']['archiveIdentity']);typed_equal(payload['sha256'],q['plan']['archiveSha256']);typed_equal(payload['sizeBytes'],q['plan']['archiveBytes']);assert payload['attributes']==[]
    typed_equal(set(first['frames']),set(last['frames']))
    for name in first['frames']:
        if name not in (op+'.json','claims/'+source+'.json'):typed_equal(first['frames'][name],last['frames'][name])
    typed_equal(set(first['records']),set(last['records']));typed_equal(set(first['markers']),set(last['markers']))
    for operation in first['records']:
        if operation!=op:typed_equal(first['records'][operation],last['records'][operation])
    for source_id in first['markers']:
        if source_id!=source:typed_equal(first['markers'][source_id],last['markers'][source_id])
    return dict(physicalDifferenceConsistent=True,selectedDataAbsent=True,originalMetadataAndLiveCopyKept=True,rpcAndAuthorizationProvenanceVerified=False)


def capture_native(raw,folder):
    """Preserve parent-provided native bytes, not evidence of their RPC origin."""
    assert type(raw) is bytes and 0<len(raw)<=65537 and raw.endswith(b'\n') and b'\r' not in raw and b'\n' not in raw[:-1]
    q=proof.unique_json(raw[:-1].decode('utf-8'));proof.request_proof(q)
    value=capture(q,folder)
    value['nativeInput']=dict(base64=base64.b64encode(raw).decode('ascii'),sha256=hashlib.sha256(raw).hexdigest(),byteCount=len(raw),rpcOriginVerified=False)
    return value
