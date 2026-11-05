#!/usr/bin/env python3
"""Fresh installed signed recycle RPC, originals, physical boundaries and exact cleanup."""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import subprocess
import sys
import time
import uuid
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_recursive_acl_agent import Probe as BaseProbe,host_identity
from validate_container_recursive_acl_restore_agent import core_health
from validate_container_recursive_acl_faults import preservation_snapshot
from recursive_acl_restore_agent_io import installed,snapshot,hdd_snapshot as restore_hdd_snapshot
from validate_recursive_acl_restore_paging import restore_database,database
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from container_recycle_rpc_proof import ready_proof,plan_frame,receipt_frame,OPERATION_REFUSALS
from container_recycle_agent_io import STATE,PRIVATE,physical,file_snapshot,physical_proof,read_operation,hdd_snapshot
from validate_container_upload_recovery_agent import trusted

SOURCES=('validate_container_recycle_agent.py','verify_container_recycle_agent.py','container_recycle_agent_io.py','container_recycle_rpc_proof.py','test_container_recycle_rpc_proof.py','test_container_recycle_agent_scope.py')
PROBE_SOURCES=('deploy/validation/TerminalProbe/Program.cs','deploy/validation/TerminalProbe/ContainerRecycleProbe.cs')
HOSTS=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py','whaledeck-container-recycle-transport.py','whaledeck-privileged')


class Probe(BaseProbe):
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'rpc.stderr').open('xb');self.transcript=evidence/'rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-recycling'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:self.send(header);ready_proof(self.receive(),header)
        except BaseException:self.close(False);raise
    def receive(self):
        deadline=time.monotonic()+135
        while b'\n' not in self.buffer:
            remaining=deadline-time.monotonic();assert remaining>0 and select.select([self.process.stdout],[],[],remaining)[0],'RECYCLE_PROBE_DEADLINE'
            raw=os.read(self.process.stdout.fileno(),65536);assert raw,'RECYCLE_PROBE_EOF';self.buffer+=raw;assert len(self.buffer)<=131072
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=65536
        value=json.loads(line)
        with self.transcript.open('ab') as out:out.write(line+b'\n')
        return value


def baseline(revision):
    io=installed();core=owned.common.core_snapshot()
    return json.loads(json.dumps(dict(protected=preservation_snapshot(io.storage),sourceRoot=snapshot(io.sourceState),restoreRoot=snapshot(io.restoreState),restoreHdd=restore_hdd_snapshot(io,True),directoryRoot=snapshot(Path('/var/lib/whaledeck-container-directories')),
        core=core,health=core_health(core),hosts=host_identity(revision),restoreDatabase=restore_database(),sourceDatabase=database(),recycleRoot=snapshot(STATE),recycleHdd=hdd_snapshot(io.storage))))


def preserved(before,after,operations):
    for key in before.keys()-{'recycleRoot','recycleHdd'}:typed_equal(after[key],before[key])
    for key in ('recycleRoot','recycleHdd'):
        assert all(after[key].get(k)==v for k,v in before[key].items())
        allowed={op+suffix for op in operations for suffix in (('.json','.lock') if key=='recycleRoot' else ('.json','.data'))}
        if key=='recycleHdd':allowed.add('history.lock')
        assert set(after[key])-set(before[key])<=allowed


def main(revision,binary_name,probe_revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision) and re.fullmatch('[a-f0-9]{40}',probe_revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,sha,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h for n,h in closure.items())
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+n])
    probe_sources={}
    for n in PROBE_SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',probe_revision+':'+n]);assert (repo/n).read_bytes()==raw
        probe_sources[n]=hashlib.sha256(raw).hexdigest()
    binary=Path(binary_name);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch('terminal-probe.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    preflight();before=baseline(revision);assert before['restoreDatabase']['active']==0
    e=repo/'deploy/artifacts'/('container-recycle-agent-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    save=lambda n,v:(e/n).write_text(json.dumps(v,ensure_ascii=False))
    save('baseline.json',before);save('source.json',dict(revision=revision,probeRevision=probe_revision,validationRevision=sha,validationSourceSha256=closure,probeSourceSha256=probe_sources,binary=str(binary),binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest()))
    print(json.dumps(dict(installedRecycle='started',evidence=e.name)),flush=True);probe=None;steps=[]
    try:
        with owned.fixture() as fixture:
            fixture['retain_unsettled']=True;folder=fixture['folder'];leaf=folder/'recycle-rpc.bin';content=hashlib.shake_256(('installed-recycle:'+fixture['identifier']).encode()).digest(1024**2+7)
            leaf.write_bytes(content);os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'preserve original user attribute');os.utime(leaf,ns=(1700000000123456789,1700000000123456789))
            c=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
            header=dict(revision=revision,probeRevision=probe_revision,containerId=fixture['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt'],coreId=before['core']['/whaledeck-api'][0])
            original=file_snapshot(leaf);assert original is not None;save('fixture.json',dict(header=header,folder=str(folder),name=c['Name'].lstrip('/'),original=original))
            reader=load('/usr/local/libexec/whaledeck-container-files.py','recycle_gate_reader');version=reader.version(header,'work/recycle-rpc.bin',leaf.stat())
            probe=Probe(binary,e,header);wire=probe.call(dict(action='plan',version=version,boundaries=True));plan=plan_frame(wire,header,boundaries=True);save('plan-frame.json',wire)
            typed_equal(plan['sourceIdentity'],original['identity']);typed_equal(plan['attributes'],original['attributes']);assert plan['sha256']==original['sha256']
            cancel=uuid.uuid4().hex;normal=uuid.uuid4().hex;operations=(cancel,normal)
            for op in operations:assert not (STATE/(op+'.json')).exists()
            refusal=probe.call(dict(action='reject',operationId=cancel,plan=wire['plan']));assert refusal==dict(kind='rejected',operationId=cancel,refusals=OPERATION_REFUSALS);save('refusals.json',refusal)
            assert not (STATE/(cancel+'.json')).exists();typed_equal(file_snapshot(leaf),original)
            requests={op:dict(operationId=op,owner='B'*64,plan=plan) for op in operations};restore_job=None
            for op,actions in ((cancel,('discard','inspect','apply','discard')),(normal,('apply','inspect','apply','discard','restore','restore','inspect','apply','discard'))):
                previous=None
                for action in actions:
                    frame=probe.call(dict(action=action,operationId=op,plan=wire['plan']));receipt=receipt_frame(frame,requests[op]);assert frame['action']==action
                    expected='Aborted' if op==cancel else 'Restored' if restore_job is not None or action=='restore' else 'Trashed';assert receipt['state']==expected
                    if action=='restore':
                        if restore_job is None:restore_job=frame['jobId']
                        else:assert restore_job==frame['jobId']
                    records=read_operation(requests[op],receipt,e/('records-'+str(len(steps))),create=True);actual=physical(folder,op);physical_proof(actual,original,records['root'],receipt)
                    if previous is not None and receipt['state']==previous['receipt']['state']:
                        assert receipt['replayed'] is True;typed_equal(records,previous['records']);typed_equal(actual,previous['physical'])
                    step=dict(action=action,request=requests[op],frame=frame,receipt=receipt,records=records,physical=actual);steps.append(step);previous=step;save('steps.json',steps)
            closing=probe;probe=None;closing.close(True)
            preserved(before,baseline(revision),operations)
            with (e/'independent-live.log').open('x') as log:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_agent.py'),e.name,'--live'],stdout=log,stderr=subprocess.STDOUT,check=True,timeout=90)
            typed_equal(file_snapshot(leaf),steps[-1]['physical']['source']);leaf.unlink()
            private=folder/PRIVATE;i=private.lstat();assert not private.is_symlink() and [i.st_dev,i.st_ino]==steps[-1]['records']['root']['privateIdentity'] and not list(private.iterdir());private.rmdir();fixture['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,operations);save('after.json',after)
        save('verified.json',dict(revision=revision,signedRpc=True,operations=2,receiptFrames=len(steps),sourceAndContainerAbsent=not folder.exists(),historiesRetained=True,humanUi=False))
        with (e/'independent-final.log').open('x') as log:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_agent.py'),e.name],stdout=log,stderr=subprocess.STDOUT,check=True,timeout=90)
        print(json.dumps(dict(installedRecycle='passed',evidence=e.name,receiptFrames=len(steps),humanUi=False)),flush=True)
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];save('failure.json',dict(type=type(error).__name__,file=Path(f.filename).name,line=f.lineno));raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':
    try:assert len(sys.argv)==4;main(*sys.argv[1:])
    except BaseException as error:print(json.dumps(dict(installedRecycle='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
