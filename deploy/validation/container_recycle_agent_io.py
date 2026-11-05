"""Read-only installed recycle originals and fixed owned-file physical proofs."""
import base64
import contextlib
import hashlib
import os
from pathlib import Path
import re
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at,archive_raw,frame,snapshot,installed
from container_recycle_rpc_proof import record_proof

STATE=Path('/var/lib/whaledeck-container-recycling')
NAMESPACE='container-recycling-v1'
PRIVATE='.whaledeck-container-recycling-v1'


@contextlib.contextmanager
def namespace(storage):
    with storage.target(create_namespace=False,read_only=True) as (config,base):
        fd=storage.f.open_at(base,NAMESPACE,os.O_RDONLY|os.O_DIRECTORY)
        try:
            i=os.fstat(fd);assert (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))==(0,0,0o700)
            storage.attest(config,fd,read_only=True);yield fd;storage.attest(config,fd,read_only=True)
        finally:os.close(fd)


def hdd_snapshot(storage):
    try:
        with namespace(storage) as fd:
            root=Path(os.readlink('/proc/self/fd/'+str(fd)));result={}
            for name in os.listdir(fd):
                assert re.fullmatch(r'[a-f0-9]{32}\.(json|data|partial)|history\.lock',name)
                value=file_snapshot(root/name);assert value is not None
                assert value['identity'][3:6]==[0,0,1] and stat.S_IMODE(value['identity'][2])==0o600
                if name.endswith('.json'):read_at(fd,name)
                result[name]=value
            return result
    except FileNotFoundError:return {}


def file_snapshot(path):
    try:fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC)
    except FileNotFoundError:return None
    try:
        i=os.fstat(fd);before=frame(i);assert stat.S_ISREG(i.st_mode) and i.st_nlink==1 and 0<=i.st_size<=1073741824
        attrs=[]
        for name in sorted(os.listxattr(fd)):
            assert name.startswith('user.');attrs.append([name,base64.b64encode(os.getxattr(fd,name)).decode()])
        h=hashlib.sha256();size=0
        while part:=os.read(fd,65536):size+=len(part);assert size<=i.st_size;h.update(part)
        assert size==i.st_size and before==frame(os.fstat(fd))==frame(path.lstat())
        return dict(identity=before,attributes=attrs,sha256=h.hexdigest().upper(),sizeBytes=size)
    finally:os.close(fd)


def physical(folder,operation):
    assert re.fullmatch('[a-f0-9]{32}',operation)
    assert folder.resolve(strict=True)==folder and not folder.is_symlink()
    private=folder/PRIVATE;identity=None
    if os.path.lexists(private):
        i=private.lstat();assert stat.S_ISDIR(i.st_mode) and (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))==(0,0,0o700)
        identity=[i.st_dev,i.st_ino];assert set(p.name for p in private.iterdir())<= {operation+'.trash'}
    return dict(source=file_snapshot(folder/'recycle-rpc.bin'),retained=file_snapshot(private/(operation+'.trash')),privateIdentity=identity)


def physical_proof(value,original,root,receipt):
    assert receipt['state'] in ('Aborted','Trashed','Restored')
    if receipt['state']=='Aborted':
        typed_equal(value['source'],original);assert value['retained'] is None and value['privateIdentity'] is None
    else:
        assert receipt['bindingCurrent'] is True
        present=value['retained'] if receipt['state']=='Trashed' else value['source']
        assert (value['source'] if receipt['state']=='Trashed' else value['retained']) is None
        typed_equal(present['identity'],root['actualIdentity']);typed_equal(present['identity'][:8],original['identity'][:8]);assert present['identity'][8]>=original['identity'][8]
        for k in ('attributes','sha256','sizeBytes'):typed_equal(present[k],original[k])
        typed_equal(value['privateIdentity'],root['privateIdentity'])


def read_operation(request,receipt,folder,*,create=False):
    op=request['operationId'];assert re.fullmatch('[a-f0-9]{32}',op);io=installed()
    if create:folder.mkdir(mode=0o700)
    raw,root,_=read_at(None,str(STATE/(op+'.json')));archive_raw(folder,'root.json',raw,create)
    history=None;data=None
    if root['historyVerified']:
        with namespace(io.storage) as fd:
            raw,history,_=read_at(fd,op+'.json');archive_raw(folder,'hdd.json',raw,create)
            path=Path(os.readlink('/proc/self/fd/'+str(fd)))/(op+'.data');data=file_snapshot(path)
            assert data is not None and data['identity'][3:6]==[0,0,1] and data['identity'][2]&0o777==0o600
    else:
        try:
            with namespace(io.storage) as fd:
                for suffix in ('.json','.data','.partial'):
                    try:os.stat(op+suffix,dir_fd=fd,follow_symlinks=False)
                    except FileNotFoundError:continue
                    raise AssertionError('UNEXPECTED_RECYCLE_HISTORY')
        except FileNotFoundError:pass
    record_proof(request,receipt,root,history,data_sha256=None if data is None else data['sha256'],data_size=None if data is None else data['sizeBytes'])
    return dict(root=root,history=history,data=data)
