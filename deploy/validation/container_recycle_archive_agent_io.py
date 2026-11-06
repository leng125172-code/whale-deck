"""Read-only installed archive acquisition. No mutation kernel, RPC or cleanup.

Fixed registry paths; owning QA supplies an exact fixture parent, requests and
actual post-purge baselines. Test-only constants may be patched, never CLI paths.
"""
import os
from pathlib import Path
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at,archive_raw,installed
from container_recycle_restore_agent_io import private_directory
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io
import container_recycle_archive_rpc_proof as proof

STATE=Path('/var/lib/whaledeck-container-recycle-archives')
CLAIMS=Path('/var/lib/whaledeck-container-recycle-restores/claims')
STAGE_PREFIX='.whaledeck-container-archive-'


def read_operation(q,receipt,purged_before,folder,*,create=False):
    proof.request_proof(q);p=q['plan'];purge=p['command']['purge'];ref=purge['plan']['command']['source'];op=q['operationId'];source_op=ref['operationId'];saved={}
    if create:folder.mkdir(mode=0o700)
    fd=private_directory(folder);os.close(fd)
    for state,name,key,raw_name in ((STATE,op+'.json','root','archive-root.json'),(CLAIMS,source_op+'.json','claim','source-claim.json'),(source_io.STATE,source_op+'.json','sourceRoot','source-root.json'),(source_io.STATE,source_op+'.purge.json','marker','purge-marker.json'),(purge_io.STATE,purge['operationId']+'.json','purgeRoot','purge-root.json')):
        directory=private_directory(state)
        try:
            try:raw,saved[key],_=read_at(directory,name)
            except FileNotFoundError:
                if key!='claim':raise
                raw,saved[key]=b'null',None
            assert len(raw)<=65536;archive_raw(folder,raw_name,raw,create)
        finally:os.close(directory)
    io=installed()
    with io.storage.target(create_namespace=False,read_only=True) as (config,base):
        directory=io.files.open_at(base,source_io.NAMESPACE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            info=os.fstat(directory);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);io.storage.attest(config,directory,read_only=True)
            saved['storeIdentity']=[info.st_dev,info.st_ino];saved['storageHash']=proof.digest(b'',config)
            raw,saved['history'],_=read_at(directory,source_op+'.json');archive_raw(folder,'source-hdd.json',raw,create)
            saved['data']=source_io.file_snapshot(Path('/proc/self/fd/'+str(directory))/(source_op+'.data'));assert saved['data'] is not None
            io.storage.attest(config,directory,read_only=True);assert [os.fstat(directory).st_dev,os.fstat(directory).st_ino]==saved['storeIdentity']
        finally:os.close(directory)
    proof.record_proof(q,receipt,saved['root'],saved['claim'],saved['sourceRoot'],saved['purgeRoot'],saved['marker'],saved['history'],saved['data'],source_before=purged_before['sourceRoot'],purge_before=purged_before['root'],marker_before=purged_before['marker'],history_before=purged_before['history'],data_before=purged_before['data'],store_identity=saved['storeIdentity'],storage_hash=saved['storageHash'])
    return saved


def physical(folder,q):
    proof.request_proof(q);assert folder.is_absolute() and folder.resolve(strict=True)==folder
    p=q['plan'];purge=p['command']['purge'];source_op=purge['plan']['command']['source']['operationId'];assert purge['plan']['command']['source']['plan']['command']['path']=='work/recycle-rpc.bin'
    parent=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
    try:
        before=os.fstat(parent);typed_equal([before.st_dev,before.st_ino],p['parentIdentity']);assert os.path.samestat(before,folder.lstat())
        result=dict(parentIdentity=p['parentIdentity'],source=source_io.file_snapshot(Path('/proc/self/fd/'+str(parent))/'recycle-rpc.bin'),oldPrivateIdentity=None,retained=None,pending=None,stageIdentity=None,staged=None)
        for name,kind in ((source_io.PRIVATE,'old'),(STAGE_PREFIX+q['operationId'],'stage')):
            try:fd=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
            except FileNotFoundError:continue
            try:
                info=os.fstat(fd);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);root=Path('/proc/self/fd/'+str(fd))
                if kind=='old':
                    result['oldPrivateIdentity']=[info.st_dev,info.st_ino];assert set(os.listdir(fd))<={source_op+'.trash',purge['operationId']+'.purging'}
                    result['retained']=source_io.file_snapshot(root/(source_op+'.trash'));result['pending']=source_io.file_snapshot(root/(purge['operationId']+'.purging'))
                else:
                    result['stageIdentity']=[info.st_dev,info.st_ino];assert set(os.listdir(fd))<={'copy.partial'};result['staged']=source_io.file_snapshot(root/'copy.partial')
                assert os.path.samestat(info,os.stat(name,dir_fd=parent,follow_symlinks=False))
            finally:os.close(fd)
        assert os.path.samestat(before,folder.lstat());return result
    finally:os.close(parent)


def physical_proof(value,original,q,records,receipt):
    assert receipt['state'] in ('Reserved','Aborted','Completed'),'UNKNOWN_REQUIRES_SEPARATE_FAULT_PROOF'
    typed_equal(value['parentIdentity'],q['plan']['parentIdentity']);typed_equal(value['oldPrivateIdentity'],q['plan']['privateIdentity']);assert value['retained'] is value['pending'] is value['staged'] is None
    if receipt['state']=='Completed':
        assert receipt['archiveCopied'] is True and receipt['bindingCurrent'] is True and value['source'] is not None and value['stageIdentity'] is not None
        target=value['source'];typed_equal(target['identity'],records['root']['publishedIdentity']);typed_equal(target['identity'],receipt['publishedIdentity']);typed_equal(value['stageIdentity'],records['root']['privateIdentity'])
        assert target['identity'][:2]!=original['identity'][:2] and target['identity'][:2]!=q['plan']['archiveIdentity'][:2];typed_equal(target['identity'][2:8],original['identity'][2:8])
        for key in ('sha256','attributes','sizeBytes'):typed_equal(target[key],original[key])
    else:
        assert receipt['archiveCopied'] is False and value['source'] is value['stageIdentity'] is None
    return dict(physicalNamespaceChecked=True,newInodeProven=receipt['state']=='Completed',oldPurgeLifecyclePreserved=True)
