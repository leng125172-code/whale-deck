#!/usr/bin/env python3
"""Freeze candidate source, execute temporary native I/O, prove production unchanged.

No installation, RPC, physical HDD, real container or Authentik acceptance.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

from acl_source_apply_missing_proof import source_closure
from validate_container_directory_restart import preflight
from validate_container_recycle_agent import baseline, preserved

SOURCES=('validate_container_recycle_restore_protocol.py','test_container_file_recycle_restore_transport.py','test_container_file_recycle_restores.py','test_container_file_recycling.py','test_container_file_recycle_transport.py')


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision)
    os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*args])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    sources={str(Path('deploy/validation')/n):h for n,h in closure.items()}
    tracked=git('ls-files','deploy/host/*.py').decode().splitlines()+git('diff-tree','--no-commit-id','--name-only','-r',sha).decode().splitlines()
    tracked+=['deploy/host/install.sh','deploy/host/validate.sh','src/WhaleDeck.Agent/Services/ContainerRecycleGrpcService.cs','tests/WhaleDeck.UnitTests/Agent/ContainerRecyclePolicyTests.cs']
    for name in tracked:sources[name]=hashlib.sha256(git('show',sha+':'+name)).hexdigest()
    for name,h in sources.items():assert hashlib.sha256((repo/name).read_bytes()).hexdigest()==h
    preflight();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restore-protocol-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    save=lambda n,v:(e/n).write_text(json.dumps(v,sort_keys=True,indent=2))
    save('source.json',dict(productRevision=revision,validationRevision=sha,sourceSha256=sources));save('before.json',before)
    print(json.dumps(dict(recycleRestoreProtocol='started',evidence=e.name)),flush=True)
    try:
        result=subprocess.run([sys.executable,'-W','error::ResourceWarning','-m','unittest','discover','-s','deploy/validation','-p','test_container_file_recycl*.py'],cwd=repo,text=True,capture_output=True,timeout=120)
        (e/'tests.stdout').write_text(result.stdout);(e/'tests.stderr').write_text(result.stderr)
        match=re.search(r'Ran (\d+) tests in [^\n]+\n\nOK\s*$',result.stderr)
        assert result.returncode==0 and match and 'skipped' not in result.stderr
        probe=repo/'deploy/validation/RecycleRestoreCapabilityProbe/bin/Release/net10.0/RecycleRestoreCapabilityProbe.dll'
        with (e/'old-host.stdout').open('x') as out,(e/'old-host.stderr').open('x') as err:
            run=subprocess.run(['/home/user/.local/bin/dotnet',str(probe),'old-host-refusal-only'],stdout=out,stderr=err,text=True,timeout=20);assert run.returncode==0
        negative=json.loads((e/'old-host.stdout').read_text());assert negative['operationFrames']==0 and negative['capabilityKeyRead'] is False and negative['actualAgentMinor']==56 and negative['newRestoreRefused']
        assert negative['probeRevision']==negative['sdkRevision']==negative['contractRevision']==sha
        save('probe-binary.json',{name:hashlib.sha256((probe.parent/name).read_bytes()).hexdigest() for name in ('RecycleRestoreCapabilityProbe.dll','WhaleDeck.Infrastructure.dll','WhaleDeck.Application.dll','WhaleDeck.Contracts.dll')})
        preflight();after=baseline(revision);preserved(before,after,());save('after.json',after)
        assert not Path('/var/lib/whaledeck-container-recycle-restores').exists()
        frozen=e/'source';frozen.mkdir(mode=0o700)
        for name,h in sources.items():
            data=git('show',sha+':'+name);assert hashlib.sha256(data).hexdigest()==h
            p=frozen/name;p.parent.mkdir(parents=True,exist_ok=True,mode=0o700);p.write_bytes(data)
        save('verified.json',dict(candidateTemporaryNativeIo=True,newSignedProtocolCandidate=True,oldHostReadOnlyRefusal=True,nativeTests=int(match[1]),productionUnchanged=True,restoreNamespaceNotInstalled=True,realContainer=False,physicalHdd=False,signedRpcNewOperations=False,authentik=False,permanentBusiness=False))
        print(json.dumps(dict(recycleRestoreProtocol='passed',evidence=e.name,nativeTests=int(match[1]))),flush=True)
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(v.filename).name,line=v.lineno,function=v.name) for v in traceback.extract_tb(error.__traceback__)]
        save('failure.json',dict(type=type(error).__name__,frames=frames));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(recycleRestoreProtocol='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
