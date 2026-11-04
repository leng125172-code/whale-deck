"""Frozen candidate bundle using real container, publisher, backup and ACL engines."""
from contextlib import contextmanager
import hashlib,os,stat,subprocess,tempfile
from pathlib import Path
from types import SimpleNamespace

from validate_container_file_staging import load
from validate_container_recursive_acl import NAMES as ENGINE_NAMES

NAMES=(*ENGINE_NAMES,'whaledeck-container-backups.py','whaledeck-container-acl-history.py','whaledeck-container-recursive-acl-history.py')


@contextmanager
def engines(repo,revision,state):
    blobs={name:subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]) for name in NAMES}
    assert all((repo/'deploy/host'/name).read_bytes()==raw for name,raw in blobs.items())
    with tempfile.TemporaryDirectory(prefix='wd-recursive-history-') as temporary:
        bundle=Path(temporary)
        for name,raw in blobs.items():(bundle/name).write_bytes(raw);(bundle/name).chmod(0o750)
        reader=load(bundle/NAMES[1],'recursive_history_native_reader');files=reader.trusted_module(NAMES[0],'recursive_history_native_files');docker=reader.trusted_module(NAMES[2],'recursive_history_native_docker')
        uploads=load(bundle/NAMES[3],'recursive_history_native_upload').ContainerUploads(reader,files,state)
        backups=load(bundle/NAMES[5],'recursive_history_native_backups');storage=backups.FileBackups(files)
        content_history=load(bundle/NAMES[11],'recursive_history_native_content').ContainerBackups(uploads,backups.FileBackups(files,namespace='container-files-v1'))
        publisher=load(bundle/NAMES[4],'recursive_history_native_publisher').ContainerPublisher(uploads,content_history)
        directory=load(bundle/NAMES[6],'recursive_history_native_directory').ContainerDirectories;permission=load(bundle/NAMES[7],'recursive_history_native_permission');acl=load(bundle/NAMES[8],'recursive_history_native_acl')
        recursive=load(bundle/NAMES[9],'recursive_history_native_recursive');profile=load(bundle/NAMES[10],'recursive_history_native_profile')
        history=profile.recursive_acl_history_type(recursive)(storage);engine=profile.recursive_acl_type(acl,permission,directory,recursive)(uploads,publisher,state,history)
        common=load(bundle/NAMES[12],'recursive_history_native_common');catalog=load(bundle/NAMES[13],'recursive_history_native_catalog').recursive_acl_history_reader_type(common)(engine)
        yield SimpleNamespace(reader=reader,files=files,docker=docker,storage=storage,acl=acl,engine=engine,catalog=catalog,hashes={n:hashlib.sha256(v).hexdigest() for n,v in blobs.items()})


def source_request(b,case,**extra):
    return dict(containerId=b['containerId'],instanceId=b['instanceId'],path='work/recursive-tree',captureId=case['request']['operationId'],expectedHistoryDigest=case['receipt']['historyDigest'],offset=0,take=50,**extra)


def verify_page(page,b,case,offset):
    plan=case['request']['plan'];assert page['containerId']==b['containerId'] and page['instanceId']==b['instanceId'] and page['path']=='work/recursive-tree'
    assert page['captureId']==case['request']['operationId'] and page['historyDigest']==case['receipt']['historyDigest'] and page['manifestDigest']==plan['manifestDigest'] and page['sourceInstanceId']==plan['command']['instanceId']
    assert page['changedAclType']==plan['command']['aclType'] and [page[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]
    order=('sub/中文.bin','one.bin','sub','');assert page['offset']==offset and page['entries']
    for index,entry in enumerate(page['entries'],offset):
        relative=order[index];old=case['before'][relative]
        assert entry==dict(relativePath=relative,kind='File' if relative.endswith('.bin') else 'Directory',originalMode=old['identity'][2]&0o777,accessAcl=old['access'],defaultAcl=old['default'])
    next_offset=offset+len(page['entries']);assert page['complete'] is (next_offset==4) and page['nextOffset']==(None if next_offset==4 else next_offset)
    return next_offset


def root_snapshot(root):
    result={}
    for path in root.rglob('*'):
        if path.is_dir():continue
        info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600 and info.st_size<=16*1024**2
        frame=lambda v:[v.st_dev,v.st_ino,v.st_mode,v.st_uid,v.st_gid,v.st_nlink,v.st_size,v.st_mtime_ns,v.st_ctime_ns]
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC)
        try:
            digest=hashlib.sha256();size=0
            while True:
                part=os.read(fd,65536)
                if not part:break
                digest.update(part);size+=len(part)
            assert size==info.st_size and frame(os.fstat(fd))==frame(info)==frame(path.lstat())
        finally:os.close(fd)
        result[str(path.relative_to(root))]=dict(identity=frame(info),sha256=digest.hexdigest())
    return result
