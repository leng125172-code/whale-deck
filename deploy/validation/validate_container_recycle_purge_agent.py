#!/usr/bin/env python3
"""Fresh installed purge RPC with paced independent namespace/HDD checks.

Only a newly owned fixture may be captured/purged; failed proof retains it.
No permanent business/API/UI or automatic retention acceptance is implied.
"""
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
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import snapshot
from validate_container_recycle_restore_agent import baseline as previous_baseline
from validate_container_recycle_agent import owned,load,preflight
from validate_container_recursive_acl_agent import Probe as BaseProbe
from validate_container_recycle_restore_browser import idle,old_rows
from validate_container_upload_recovery_agent import trusted
import container_recycle_purge_rpc_proof as proof
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io
from container_recycle_purge_workflow import CANCEL,NORMAL,transcript_proof

SOURCES=('validate_container_recycle_purge_agent.py','verify_container_recycle_purge_agent.py','container_recycle_purge_rpc_proof.py','container_recycle_purge_agent_io.py','container_recycle_purge_workflow.py','test_container_recycle_purge_rpc_proof.py','test_container_recycle_purge_probe_scope.py','test_container_recycle_purge_workflow.py','test_container_recycle_purge_agent_scope.py')
PROBE_SOURCES=('deploy/validation/TerminalProbe/Program.cs','deploy/validation/TerminalProbe/ContainerRecyclePurgeProbe.cs','src/WhaleDeck.Contracts/ContainerRecyclePurgeBinding.cs','src/WhaleDeck.Contracts/ContainerRecyclePurgeWire.cs','src/WhaleDeck.Contracts/Protos/agent.proto')
HOSTS=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py','whaledeck-container-recycle-restores.py','whaledeck-container-recycle-transport.py','whaledeck-container-recycle-restore-transport.py','whaledeck-container-recycle-purge-plan.py','whaledeck-container-recycle-purge-fences.py','whaledeck-container-recycle-purges.py','whaledeck-container-recycle-purge-transport.py','whaledeck-privileged')


class Probe(BaseProbe):
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'rpc.stderr').open('xb');self.transcript=evidence/'rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-recycle-purges'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:self.send(header);proof.ready_proof(self.receive(),header)
        except BaseException:self.close(False);raise

    def receive(self):
        deadline=time.monotonic()+135
        while b'\n' not in self.buffer:
            remaining=deadline-time.monotonic();assert remaining>0 and select.select([self.process.stdout],[],[],remaining)[0],'PURGE_PROBE_DEADLINE'
            raw=os.read(self.process.stdout.fileno(),65536);assert raw,'PURGE_PROBE_EOF';self.buffer+=raw;assert len(self.buffer)<=131072
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=65536;value=proof.unique_json(line.decode())
        with self.transcript.open('ab') as out:out.write(line+b'\n')
        return value


def baseline(revision):return {**previous_baseline(revision),'purgeRoot':snapshot(purge_io.STATE)}


def preserved(before,after,source_id,operations):
    proof.identity(source_id);assert len(operations)==2 and len({source_id,*operations})==3
    for op in operations:proof.identity(op)
    for k in before.keys()-{'recycleRoot','recycleHdd','purgeRoot'}:typed_equal(before[k],after[k])
    allowed=dict(recycleRoot={source_id+s for s in ('.json','.lock','.purge.json')},recycleHdd={source_id+s for s in ('.json','.data')},purgeRoot={op+s for op in operations for s in ('.json','.lock')})
    for key,extra in allowed.items():
        assert all(after[key].get(k)==v for k,v in before[key].items());assert set(after[key])-set(before[key])<=extra


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_purge_agent.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=120)


def main(revision,binary_name,probe_revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision) and re.fullmatch('[a-f0-9]{40}',probe_revision)
    os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a]);assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    probe_sources={}
    for n in PROBE_SOURCES:
        raw=git('show',probe_revision+':'+n);assert raw==(repo/n).read_bytes();probe_sources[n]=hashlib.sha256(raw).hexdigest()
    binary=Path(binary_name);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    preflight();idle();before=baseline(revision);rows=old_rows('0'*64)
    e=repo/'deploy/artifacts'/('container-recycle-purge-agent-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('baseline.json',before);save('business-before.json',rows);save('source.json',dict(revision=revision,probeRevision=probe_revision,validationRevision=sha,validationSourceSha256=closure,probeSourceSha256=probe_sources,binary=str(binary),binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest()))
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    capability=e/'capability';capability.mkdir(mode=0o700);header=dict(revision=revision,probeRevision=probe_revision,containerId='a'*64,instanceId='777:2026-11-06T01:02:03Z',coreId=before['core']['/whaledeck-api'][0]);steps=[];probe=None
    print(json.dumps(dict(installedPurge='started',evidence=e.name)),flush=True)
    try:
        ready=Probe(binary,capability,header);ready.close(True)
        with owned.fixture() as f:
            f['retain_unsettled']=True;folder=f['folder'];leaf=folder/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('installed-purge:'+f['identifier']).encode()).digest(1024**2+7));os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'owned native purge bytes');os.utime(leaf,ns=(1700000000123456789,1700000000123456789))
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header={**header,'containerId':f['identifier'],'instanceId':str(c['State']['Pid'])+':'+c['State']['StartedAt']};original=source_io.file_snapshot(leaf);save('fixture.json',dict(header=header,folder=str(folder),name=c['Name'].lstrip('/'),original=original))
            reader=load('/usr/local/libexec/whaledeck-container-files.py','installed_purge_reader');version=reader.version(header,'work/recycle-rpc.bin',leaf.stat());probe=Probe(binary,e,header)
            wire=probe.call(dict(action='source-plan',version=version));proof.fields(wire,('kind','plan'));assert wire['kind']=='source-plan';old=proof.source.proto_plan(proof.unique_json(wire['plan']));save('source-plan-frame.json',wire)
            typed_equal(old['sourceIdentity'],original['identity']);typed_equal(old['attributes'],original['attributes']);assert old['sha256']==original['sha256']
            source_id,cancel,normal=(uuid.uuid4().hex for _ in range(3));assert len({source_id,cancel,normal})==3;ref=dict(operationId=source_id,owner='B'*64,plan=old)
            assert not (source_io.STATE/(source_id+'.json')).exists() and all(not (purge_io.STATE/(op+'.json')).exists() for op in (cancel,normal))
            def capture_source(action):
                frame=probe.call(dict(action=action,operationId=source_id,plan=wire['plan']));receipt=proof.source_receipt_frame(frame,ref);assert receipt['state']=='Trashed'
                records=source_io.read_operation(ref,receipt,e/('records-'+str(len(steps))),create=True);actual=source_io.physical(folder,source_id);source_io.physical_proof(actual,original,records['root'],receipt);steps.append(dict(phase='source',action=action,request=ref,frame=frame,receipt=receipt,records=records,physical=actual))
            capture_source('source-apply');capture_source('source-inspect');save('steps-initial.json',steps);source_before=steps[-1]['records']['root'];history_before=steps[-1]['records']['history'];save('source-before.json',source_before);save('history-before.json',history_before)
            retained=source_io.physical(folder,source_id);roots=snapshot(purge_io.STATE);planned=probe.call(dict(action='plan',sourceOperationId=source_id,boundaries=True));plan=proof.plan_frame(planned,header,boundaries=True);save('plan-frame.json',planned);typed_equal(plan['command']['source'],ref);typed_equal(source_io.physical(folder,source_id),retained);typed_equal(snapshot(purge_io.STATE),roots)
            refusal=probe.call(dict(action='reject',operationId=cancel,plan=planned['plan']));typed_equal(refusal,dict(kind='rejected',operationId=cancel,refusals=proof.OPERATION_REFUSALS));save('refusals.json',refusal);assert not (purge_io.STATE/(cancel+'.json')).exists();typed_equal(source_io.physical(folder,source_id),retained)
            def capture(op,action,state,current_plan,current_wire):
                q=dict(operationId=op,owner='C'*64,plan=current_plan);frame=probe.call(dict(action=action,operationId=op,plan=current_wire['plan']));receipt=proof.receipt_frame(frame,q);assert receipt['state']==state
                records=purge_io.read_operation(q,receipt,source_before,history_before,e/('records-'+str(len(steps))),create=True);actual=purge_io.physical(folder,q);purge_io.physical_proof(actual,original,q,records,receipt);steps.append(dict(phase='purge',action=action,request=q,frame=frame,receipt=receipt,records=records,physical=actual))
            for action,state in CANCEL:capture(cancel,action,state,plan,planned)
            fresh=probe.call(dict(action='plan',sourceOperationId=source_id,boundaries=False));fresh_plan=proof.plan_frame(fresh,header,boundaries=False);save('fresh-plan-frame.json',fresh)
            for action,state in NORMAL:capture(normal,action,state,fresh_plan,fresh)
            frame=probe.call(dict(action='source-inspect',operationId=source_id,plan=wire['plan']));receipt=proof.source_receipt_frame(frame,ref);assert receipt['state']=='Purged' and receipt['historyDigest']==fresh_plan['command']['historyDigest'];save('source-purged-frame.json',frame)
            closing=probe;probe=None;closing.close(True);save('steps.json',steps);frames=[proof.unique_json(l) for l in (e/'rpc.jsonl').read_text().splitlines()];summary=transcript_proof(frames,header);save('summary.json',summary)
            typed_equal(old_rows('0'*64),rows);preserved(before,baseline(revision),source_id,(cancel,normal));independent(repo,e,True)
            current=purge_io.physical(folder,steps[-1]['request']);typed_equal(current,steps[-1]['physical']);assert current['source'] is current['retained'] is current['pending'] is None
            private=folder/source_io.PRIVATE;assert not private.is_symlink() and [private.stat().st_dev,private.stat().st_ino]==fresh_plan['privateIdentity'] and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,source_id,(cancel,normal));typed_equal(old_rows('0'*64),rows);idle();save('after.json',after);save('verified.json',dict(revision=revision,signedRpc=True,sourceOperations=1,purgeOperations=2,newReceiptFrames=11,sourceReceiptFrames=3,sourceAndContainerAbsent=not folder.exists(),historyRetained=True,humanUi=False));independent(repo,e,False)
        print(json.dumps(dict(installedPurge='passed',evidence=e.name,newReceiptFrames=11,sourceReceiptFrames=3,humanUi=False)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(f.filename).name,line=f.lineno,function=f.name) for f in traceback.extract_tb(error.__traceback__)]));raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':
    try:assert len(sys.argv)==4;main(*sys.argv[1:])
    except BaseException as error:print(json.dumps(dict(installedPurge='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
