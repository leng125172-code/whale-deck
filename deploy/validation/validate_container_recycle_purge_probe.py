#!/usr/bin/env python3
"""Prepare the native purge probe against the installed older Agent, read only.

No install, fixture creation, signing-key read, operation RPC or purge execution.
Positive installed RPC/HDD and independent physical gates remain separate work.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_restore_browser import idle,old_rows
from validate_container_upload_recovery_agent import trusted
from container_recycle_purge_rpc_proof import capability_refusal,unique_json

SOURCES=('validate_container_recycle_purge_probe.py','container_recycle_purge_rpc_proof.py','container_recycle_purge_agent_io.py','container_recycle_purge_workflow.py','test_container_recycle_purge_workflow.py','test_container_recycle_purge_rpc_proof.py','test_container_recycle_purge_probe_scope.py')


def main(revision,binary_name,probe_revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision) and re.fullmatch('[a-f0-9]{40}',probe_revision)
    os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip() and git('rev-parse','HEAD').decode().strip()==probe_revision
    closure=source_closure(repo,probe_revision,seeds=SOURCES);paths={str(Path('deploy/validation')/n):h for n,h in closure.items()}
    tracked=git('ls-files','deploy/host/*.py','deploy/validation/TerminalProbe/*','src/WhaleDeck.Contracts/*','src/WhaleDeck.Contracts/Protos/*','Directory.*','global.json','NuGet.config','.images.env').decode().splitlines()
    tracked+=['deploy/host/whaledeck-privileged','deploy/host/install.sh','deploy/validation/publish-terminal-probe.sh']
    for n in tracked:paths[n]=hashlib.sha256(git('show',probe_revision+':'+n)).hexdigest()
    binary=Path(binary_name);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    idle();before=baseline(revision);rows=old_rows('0'*64);state=Path('/var/lib/whaledeck-container-recycle-purges');assert not os.path.lexists(state)
    e=repo/'deploy/artifacts'/('container-recycle-purge-probe-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as f:json.dump(v,f)
    save('before.json',before);save('business-before.json',rows);print(json.dumps(dict(purgeProbe='started',evidence=e.name)),flush=True)
    try:
        frozen=e/'source';frozen.mkdir(mode=0o700);normalized=[]
        for n,h in paths.items():
            raw=git('show',probe_revision+':'+n);actual=(repo/n).read_bytes();assert hashlib.sha256(raw).hexdigest()==h and (raw==actual or raw==actual.replace(b'\r\n',b'\n')),n
            if raw!=actual:normalized.append(n)
            p=frozen/n;p.parent.mkdir(parents=True,exist_ok=True,mode=0o700);p.write_bytes(raw)
        save('source.json',dict(productRevision=revision,probeRevision=probe_revision,sourceSha256=paths,normalizedWorktreePaths=normalized,frozenBeforeProbe=True,binary=str(binary)))
        tests={}
        for pattern in ('test_container_file_recycl*.py','test_container_recycle*.py','test_scope_baseline.py'):
            result=subprocess.run([sys.executable,'-W','error::ResourceWarning','-m','unittest','discover','-s','deploy/validation','-p',pattern],cwd=repo,text=True,capture_output=True,timeout=150)
            stem=pattern.replace('*','_');(e/(stem+'.stdout')).write_text(result.stdout);(e/(stem+'.stderr')).write_text(result.stderr)
            count=re.search(r'Ran (\d+) tests in [^\n]+\n\nOK\s*$',result.stderr);assert result.returncode==0 and count and 'skipped' not in result.stderr;tests[pattern]=int(count[1])
        header=dict(revision=revision,probeRevision=probe_revision,containerId='a'*64,instanceId='777:2026-11-06T01:02:03Z',coreId=before['core']['/whaledeck-api'][0]);assert header['containerId']!=header['coreId'];save('header.json',header)
        result=subprocess.run([str(binary),'container-recycle-purges'],input=json.dumps(header)+'\n',text=True,capture_output=True,timeout=20)
        (e/'probe.stdout').write_text(result.stdout);(e/'probe.stderr').write_text(result.stderr);save('old-host-refusal.json',capability_refusal(result.returncode,result.stdout,unique_json(result.stderr)))
        idle();after=baseline(revision);typed_equal(before,after);typed_equal(old_rows('0'*64),rows);assert not os.path.lexists(state);save('after.json',after)
        save('binary.json',dict(binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(),assemblySha256=hashlib.sha256((binary.parent/'TerminalProbe.dll').read_bytes()).hexdigest(),contractsSha256=hashlib.sha256((binary.parent/'WhaleDeck.Contracts.dll').read_bytes()).hexdigest(),publishSha256=hashlib.sha256((binary.parent/'SHA256SUMS').read_bytes()).hexdigest()))
        save('verified.json',dict(tests=tests,productionAndBusinessUnchanged=True,oldHostReadOnlyRefusal=True,purgeNamespaceNotInstalled=True,positiveSignedRpcTested=False,physicalHddMutation=False,permanentBusiness=False,humanUi=False))
        print(json.dumps(dict(purgeProbe='passed',evidence=e.name,tests=tests,positiveSignedRpcTested=False)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(f.filename).name,line=f.lineno,function=f.name) for f in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==4;main(*sys.argv[1:])
    except BaseException as error:print(json.dumps(dict(purgeProbe='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
