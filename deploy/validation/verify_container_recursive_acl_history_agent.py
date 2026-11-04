#!/usr/bin/env python3
"""Independent installed history proof. Live phase reads pinned bytes BEFORE cleanup.

Post-cleanup phase verifies archives and source absence, never claims live rereads.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from acl_source_apply_missing_proof import source_closure
from validate_container_file_staging import load
from validate_container_recursive_acl_agent import host_identity,fixture_bytes,validation_seeds
from validate_container_recursive_acl_faults import preservation_snapshot
from validate_container_recursive_acl_history_agent import metadata_rows,PROBE_SOURCES
from recursive_acl_history_rpc_proof import verify_transcript
from verify_container_recursive_acl_agent import private_json,verify_fixture_recipe
from validate_container_recursive_acl import PATHS,inventory
from validate_container_recursive_acl_browser import no_active_work
import validate_container_file_publishing as owned


def verify_pinned_source(b,final,reader,files,docker,acl):
    """Use a newly opened container root and verify every fd/byte/ACL/9-field identity."""
    content=fixture_bytes(b['containerId']);assert len(content)==b['bytesPerFile'] and hashlib.sha256(content).hexdigest().upper()==b['sha256']
    bridge=load('/usr/local/libexec/whaledeck-container-acl-transport.py','recursive_history_independent_mapping')
    location={k:b[k] for k in ('containerId','instanceId')}
    with reader.ContainerRoot(location,files,docker) as session:
        bridge.namespace_current(reader,session);root=files.open_at(session.root,'work/recursive-tree',os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,cross_mount=True)
        try:
            for relative,kind in PATHS.items():
                fd=files.open_at(root,relative,os.O_RDONLY|os.O_NOFOLLOW,cross_mount=True) if relative else os.dup(root)
                try:
                    bridge.require_unmapped(fd);info=os.fstat(fd);expected=final[relative]
                    assert list(reader.fingerprint(info))==expected['identity']
                    assert acl.read_acl(fd,'access',info.st_mode)==expected['access'] and acl.read_acl(fd,'default',info.st_mode)==expected['default']
                    assert set(os.listxattr(fd))<={'user.whaledeck-recursive',*acl.XATTRS.values()} and os.getxattr(fd,'user.whaledeck-recursive')==b'preserve ordinary attribute'
                    if kind=='File':
                        assert stat.S_ISREG(info.st_mode) and info.st_nlink==1
                        offset=0
                        while True:
                            data=os.read(fd,65536)
                            if not data:break
                            assert data==content[offset:offset+len(data)];offset+=len(data)
                        assert offset==len(content)
                    else:
                        assert stat.S_ISDIR(info.st_mode) and set(os.listdir(fd))=={n.rsplit('/',1)[-1] for n in PATHS if n and n.rpartition('/')[0]==relative}
                    assert list(reader.fingerprint(os.fstat(fd)))==expected['identity']
                finally:os.close(fd)
            bridge.namespace_current(reader,session);session.current()
        finally:os.close(root)
    root_path=Path(b['source'])/'recursive-tree';assert inventory(root_path,reader,content,acl)==final


def verify_history(evidence,b,rows,storage,*,live):
    repo=evidence.parents[2]
    assert b['recursiveHistory'] is True and b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=validation_seeds(True))
    for name,sha in b['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest()==sha
    assert set(b['historyProbeSourceSha256'])==set(PROBE_SOURCES)
    for name,sha in b['historyProbeSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['probeRevision']+':'+name]);assert (repo/name).read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    assert host_identity(b['revision'])==b['hostIdentity'];no_active_work()
    assert owned.common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    result=private_json(evidence/'history-verified.json');assert result['installedRecursiveAclHistoryRpc']=='passed' and result['sourceRootAndHddUnchanged'] is True
    assert result['humanBffOrUi'] is result['restorationExecuted'] is False
    metadata=metadata_rows(storage,rows)
    for capture,value in metadata.items():assert private_json(evidence/(capture+'.hdd.json'))==value
    pages=private_json(evidence/'history-pages.json');snapshots=private_json(evidence/'history-snapshots.json')
    header={k:b[k] for k in ('revision','probeRevision','containerId','instanceId')};location={k:b[k] for k in ('containerId','instanceId')};location['path']='work/recursive-tree'
    transcript=[json.loads(v) for v in (evidence/'history-rpc.jsonl').read_bytes().splitlines()]
    proof=verify_transcript(pages,snapshots,rows,metadata,location,header,transcript)
    for key,value in proof.items():assert result[key]==value
    assert not (evidence/'history-rpc.stderr').read_bytes()
    expected=private_json(evidence/'history-read-preservation.json');assert preservation_snapshot(storage)==expected
    if live:
        verify_fixture_recipe(b)
        for name,sha in b['sourceHashes'].items():
            assert re.fullmatch(r'whaledeck-[a-z-]+(?:\.py)?',name)
            path=Path('/usr/local/libexec')/name;info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==info.st_gid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o750
            raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+name]);assert path.read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
        reader=load('/usr/local/libexec/whaledeck-container-files.py','recursive_history_live_reader');files=reader.trusted_module('whaledeck-files.py','recursive_history_live_files')
        docker=load('/usr/local/libexec/whaledeck-container-terminal.py','recursive_history_live_docker');acl=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_history_live_acl')
        observed=docker.api('GET','/containers/'+b['containerId']+'/json');owned.verify_container(observed,b['containerId'],b['name'],Path(b['source']))
        target=docker.target(observed,b['containerId'],docker.registry());assert target['instanceId']==b['instanceId']
        final=private_json(evidence/'physical-final.json');verify_pinned_source(b,final,reader,files,docker,acl)
        assert preservation_snapshot(storage)==expected
    else:
        original=private_json(evidence/'history-independent-live.json');assert original['independentLivePinnedSource'] is True and original['sourceBytesReadBeforeCleanup'] is True
    proof.update(independentInstalledHistory=True,independentLivePinnedSource=live,sourceBytesReadBeforeCleanup=live,sourceBytesRereadAfterCleanup=False,humanBffOrUi=False,restorationExecuted=False)
    (evidence/('history-independent-live.json' if live else 'history-independent-final.json')).open('x').write(json.dumps(proof));return proof


def main():
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    if len(sys.argv)==2:
        from verify_container_recursive_acl_agent import main as verify_original
        verify_original(history=True);return
    assert len(sys.argv)==3 and sys.argv[1]=='--live'
    name=sys.argv[2];assert re.fullmatch('container-recursive-acl-agent-[a-f0-9]{32}',name)
    evidence=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name;info=evidence.lstat();assert stat.S_ISDIR(info.st_mode) and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o700 and not evidence.is_symlink()
    b=private_json(evidence/'baseline.json');rows=private_json(evidence/'results.json')
    files=load('/usr/local/libexec/whaledeck-files.py','recursive_history_live_storage_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_history_live_storage').FileBackups(files)
    result=verify_history(evidence,b,rows,storage,live=True);print(json.dumps(result))


if __name__=='__main__':main()
