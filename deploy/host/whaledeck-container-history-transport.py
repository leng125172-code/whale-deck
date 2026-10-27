#!/usr/bin/env python3
"""Fixed read-only history transport; all paths stay in private stdin.

No restore/publication command. Source end is emitted after verified_source's
exit checks, never just after the last chunk or a consumer cancellation.
"""
import base64
import errno
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

MODULES=frozenset(('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py',
    'whaledeck-container-upload.py','whaledeck-file-backups.py','whaledeck-container-history.py'))


class Rejected(Exception):pass


def load(filename):
    if filename not in MODULES:raise Rejected('CONTAINER_HISTORY_MODULE_UNSAFE')
    path=Path(__file__).with_name(filename);info=path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_nlink!=1 or info.st_mode&0o022 or info.st_size>131072:
        raise Rejected('CONTAINER_HISTORY_MODULE_UNSAFE')
    name='history_transport_'+filename.replace('-','_').replace('.','_')
    spec=importlib.util.spec_from_file_location(name,path);module=importlib.util.module_from_spec(spec);sys.modules[name]=module
    try:spec.loader.exec_module(module)
    except BaseException:sys.modules.pop(name,None);raise
    return module


def execute(action,request,reader,files,docker,engine):
    if action not in ('list','source') or type(request) is not dict:raise Rejected('CONTAINER_HISTORY_REQUEST_INVALID')
    required={'containerId','instanceId','path'}|({'after','take','expectedVersion'} if action=='list' else {'captureId','expectedCaptureHash'})
    if set(request)!=required:raise Rejected('CONTAINER_HISTORY_REQUEST_INVALID')
    reader.target_binding(request['containerId'],request['instanceId']);reader.canonical_path(request['path'],empty=action=='list')
    if reader.protected(request['path'],files):raise Rejected('CONTAINER_HISTORY_PROTECTED')
    if action=='source':files.identity(request['captureId']);engine.hash(request['expectedCaptureHash'])
    else:
        if type(request['take']) is not int or not 1<=request['take']<=50 or type(request['after']) is not str or type(request['expectedVersion']) is not str:
            raise Rejected('CONTAINER_HISTORY_REQUEST_INVALID')
        if request['after']:files.identity(request['after'])
        if request['expectedVersion']:engine.hash(request['expectedVersion'])
        if request['after'] and not request['expectedVersion']:raise Rejected('CONTAINER_HISTORY_VERSION_REQUIRED')
    with reader.ContainerRoot(request,files,docker) as session:
        if action=='list':files.emit(engine.listing(request,session));return
        with engine.verified_source(request,session) as (value,descriptor):
            entry=engine.public(value,'Present');proof=engine.u.target_proof(session)
            files.emit({'type':'metadata','entry':entry,'targetProof':proof})
            offset=0
            while chunk:=os.read(descriptor,reader.CHUNK):
                reader.receiver_current(None);session.current()
                files.emit({'type':'chunk','offset':offset,'data':base64.b64encode(chunk).decode()});offset+=len(chunk)
        # The context above revalidates metadata/FD/HDD/namespace after reads.
        files.emit({'type':'end','sizeBytes':offset,'sha256':value['sha256'],
            'captureHash':entry['captureHash'],'targetProof':proof})


def main():
    if os.geteuid()!=0 or len(sys.argv)!=2 or sys.argv[1] not in ('list','source'):raise Rejected('CONTAINER_HISTORY_REQUEST_INVALID')
    invocation=os.environ.get('INVOCATION_ID','')
    if not re.fullmatch('[a-f0-9]{32}',invocation):raise Rejected('CONTAINER_HISTORY_INVOCATION_INVALID')
    files=load('whaledeck-files.py');reader=load('whaledeck-container-files.py');docker=load('whaledeck-container-terminal.py')
    uploads=load('whaledeck-container-upload.py');backups=load('whaledeck-file-backups.py');history=load('whaledeck-container-history.py')
    engine=history.ContainerHistory(uploads.ContainerUploads(reader,files,Path('/var/lib/whaledeck-container-uploads')),
        backups.FileBackups(files,namespace='container-files-v1'))
    with reader.ReadLease():
        files.emit({'type':'driver','invocationId':invocation})
        def expired(_signal,_frame):raise Rejected('CONTAINER_HISTORY_TIMEOUT')
        signal.signal(signal.SIGALRM,expired);signal.alarm(10)
        source=files.InputLines(sys.stdin.fileno());request=files.line(source);source.finish()
        signal.alarm(300 if sys.argv[1]=='source' else 20)
        try:execute(sys.argv[1],request,reader,files,docker,engine)
        finally:signal.alarm(0)


def error_code(error):
    value=str(error)
    if re.fullmatch(r'(?:CONTAINER_HISTORY|CONTAINER_FILES|CONTAINER_UPLOAD|FILES|CONTAINER)_[A-Z_]{1,80}',value):return value
    if isinstance(error,OSError):
        if error.errno==errno.ENOENT:return 'CONTAINER_HISTORY_NOT_FOUND'
        if error.errno in (errno.ELOOP,errno.EXDEV,errno.ENOTDIR,errno.EACCES,errno.EPERM):return 'CONTAINER_HISTORY_PROTECTED'
    if isinstance(error,(ValueError,KeyError,TypeError)):return 'CONTAINER_HISTORY_REQUEST_INVALID'
    return 'CONTAINER_HISTORY_UNAVAILABLE'


if __name__=='__main__':
    try:main()
    except BaseException as error:
        try:print(json.dumps({'type':'error','code':error_code(error)}),flush=True)
        except (OSError,ValueError):pass
        raise SystemExit(1) from None
