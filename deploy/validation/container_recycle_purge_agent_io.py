"""Read-only purge acquisition through fixed registries and attested HDD FDs.

No namespace creation, product purge import, cleanup or process control. The
owning gate supplies actual pre-purge records and proves the current container.
"""
import os
from pathlib import Path
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at,archive_raw,installed
from container_recycle_restore_agent_io import private_directory
from container_recycle_purge_rpc_proof import request_proof,record_proof,digest
import container_recycle_agent_io as source_io

STATE=Path('/var/lib/whaledeck-container-recycle-purges')


def read_operation(q,receipt,source_before,history_before,folder,*,create=False):
    request_proof(q);ref=q['plan']['command']['source'];op=q['operationId'];source_op=ref['operationId']
    if create:folder.mkdir(mode=0o700)
    fd=private_directory(folder);os.close(fd);saved={}
    fd=private_directory(STATE)
    try:
        raw,saved['root'],_=read_at(fd,op+'.json');assert len(raw)<=65536;archive_raw(folder,'purge-root.json',raw,create)
    finally:os.close(fd)
    fd=private_directory(source_io.STATE)
    try:
        raw,saved['sourceRoot'],_=read_at(fd,source_op+'.json');assert len(raw)<=65536;archive_raw(folder,'source-root.json',raw,create)
        try:raw,saved['marker'],_=read_at(fd,source_op+'.purge.json')
        except FileNotFoundError:raw,saved['marker']=b'null',None
        assert len(raw)<=65536;archive_raw(folder,'purge-marker.json',raw,create)
    finally:os.close(fd)
    io=installed()
    with io.storage.target(create_namespace=False,read_only=True) as (config,base):
        fd=io.files.open_at(base,source_io.NAMESPACE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            info=os.fstat(fd);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);io.storage.attest(config,fd,read_only=True)
            saved['storeIdentity']=[info.st_dev,info.st_ino];saved['storageHash']=digest(b'',config)
            raw,saved['history'],_=read_at(fd,source_op+'.json');archive_raw(folder,'source-hdd.json',raw,create)
            saved['data']=source_io.file_snapshot(Path('/proc/self/fd/'+str(fd))/(source_op+'.data'));assert saved['data'] is not None
            io.storage.attest(config,fd,read_only=True);assert [os.fstat(fd).st_dev,os.fstat(fd).st_ino]==saved['storeIdentity']
        finally:os.close(fd)
    record_proof(q,receipt,saved['root'],saved['marker'],saved['sourceRoot'],saved['history'],source_before,history_before,saved['data'],store_identity=saved['storeIdentity'],storage_hash=saved['storageHash'])
    return saved


def physical(folder,q):
    request_proof(q);p=q['plan'];op=p['command']['source']['operationId'];assert folder.is_absolute() and folder.resolve(strict=True)==folder
    parent=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
    try:
        before=os.fstat(parent);typed_equal([before.st_dev,before.st_ino],p['parentIdentity']);assert os.path.samestat(before,folder.lstat())
        private=os.open(source_io.PRIVATE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
        try:
            info=os.fstat(private);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);typed_equal([info.st_dev,info.st_ino],p['privateIdentity'])
            assert set(os.listdir(private))<={op+'.trash',q['operationId']+'.purging'}
            root=Path('/proc/self/fd/'+str(private));result=dict(parentIdentity=p['parentIdentity'],privateIdentity=[info.st_dev,info.st_ino],
                source=source_io.file_snapshot(Path('/proc/self/fd/'+str(parent))/'recycle-rpc.bin'),retained=source_io.file_snapshot(root/(op+'.trash')),pending=source_io.file_snapshot(root/(q['operationId']+'.purging')))
            assert os.path.samestat(info,os.stat(source_io.PRIVATE,dir_fd=parent,follow_symlinks=False)) and os.path.samestat(before,folder.lstat());return result
        finally:os.close(private)
    finally:os.close(parent)


def physical_proof(value,original,q,records,receipt):
    assert receipt['state'] in ('Reserved','Aborted','Completed'),'UNCERTAIN_PURGE_REQUIRES_SEPARATE_FAULT_PROOF'
    typed_equal(value['parentIdentity'],q['plan']['parentIdentity']);typed_equal(value['privateIdentity'],q['plan']['privateIdentity'])
    assert value['source'] is value['pending'] is None
    if receipt['state']=='Completed':
        assert value['retained'] is None and records['sourceRoot']['state']=='Purged' and records['root']['unlinkConfirmed'] is True
    else:
        assert records['sourceRoot']['state']=='Trashed';retained=value['retained'];assert retained is not None;typed_equal(retained['identity'],q['plan']['retainedIdentity']);typed_equal(retained['identity'][:8],original['identity'][:8]);assert retained['identity'][8]>=original['identity'][8]
        for k in ('sha256','sizeBytes','attributes'):typed_equal(retained[k],original[k])
    return dict(physicalNamespaceChecked=True,archiveStillRequired=True,conclusiveState=receipt['state'])
