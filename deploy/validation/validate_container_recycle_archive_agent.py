#!/usr/bin/env python3
"""Fresh installed three-stage archive RPC with paced independent namespace/HDD checks.

Only a newly owned fixture may be captured/purged/copied; failed proof retains it.
No permanent business/API/UI or automatic retention acceptance is implied.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import stat
import subprocess
import sys
import time
import uuid
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import snapshot
from recursive_acl_restore_agent_io import frame
from validate_container_recycle_restore_agent import baseline as previous_baseline
from validate_container_recycle_agent import owned,load,preflight
from validate_container_recursive_acl_agent import Probe as BaseProbe
from validate_container_recycle_purge_browser import idle,old_rows
from validate_container_upload_recovery_agent import trusted
import container_recycle_purge_rpc_proof as proof
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io
from container_recycle_purge_workflow import CANCEL,NORMAL
import container_recycle_archive_workflow as archive_workflow
import container_recycle_archive_rpc_proof as archive_proof
import container_recycle_archive_agent_io as archive_io

SOURCES=('validate_container_recycle_archive_agent.py','verify_container_recycle_archive_agent.py','container_recycle_archive_agent_io.py','container_recycle_archive_rpc_proof.py','container_recycle_archive_workflow.py','test_container_recycle_archive_agent_io.py','test_container_recycle_archive_agent_scope.py','test_container_recycle_archive_rpc_proof.py','test_container_recycle_archive_workflow.py','test_container_recycle_archive_probe_scope.py')
PROBE_SOURCES=('deploy/validation/TerminalProbe/Program.cs','deploy/validation/TerminalProbe/ContainerRecycleArchiveProbe.cs','deploy/validation/TerminalProbe/ContainerRecycleArchiveProbeMutations.cs','src/WhaleDeck.Contracts/ContainerRecycleArchiveBinding.cs','src/WhaleDeck.Contracts/ContainerRecycleArchiveWire.cs','src/WhaleDeck.Contracts/Protos/agent.proto')
HOSTS=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py','whaledeck-container-recycle-restores.py','whaledeck-container-recycle-transport.py','whaledeck-container-recycle-restore-transport.py','whaledeck-container-recycle-purge-plan.py','whaledeck-container-recycle-purge-fences.py','whaledeck-container-recycle-purges.py','whaledeck-container-recycle-purge-transport.py','whaledeck-privileged','whaledeck-container-recycle-archive-plan.py','whaledeck-container-recycle-archive-fences.py','whaledeck-container-recycle-archive-copies.py','whaledeck-container-recycle-archive-transport.py')


class Probe(BaseProbe):
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'rpc.stderr').open('xb');self.transcript=evidence/'rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-recycle-archives'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:self.send(header);archive_proof.ready_proof(self.receive(),header)
        except BaseException:self.close(False);raise

    def receive(self):
        deadline=time.monotonic()+135
        while b'\n' not in self.buffer:
            remaining=deadline-time.monotonic();assert remaining>0 and select.select([self.process.stdout],[],[],remaining)[0],'PURGE_PROBE_DEADLINE'
            raw=os.read(self.process.stdout.fileno(),65536);assert raw,'PURGE_PROBE_EOF';self.buffer+=raw;assert len(self.buffer)<=131072
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=65536;value=proof.unique_json(line.decode())
        with self.transcript.open('ab') as out:out.write(line+b'\n')
        return value


def baseline(revision):return {**previous_baseline(revision),'purgeRoot':snapshot(purge_io.STATE),'archiveRoot':snapshot(archive_io.STATE)}


def preserved(before,after,source_id,purges,archives):
    assert len(purges)==len(archives)==2 and len({source_id,*purges,*archives})==5
    for op in (source_id,*purges,*archives):proof.identity(op)
    allowed=dict(recycleRoot={source_id+n for n in ('.json','.lock','.purge.json')},recycleHdd={source_id+n for n in ('.json','.data')},purgeRoot={op+n for op in purges for n in ('.json','.lock')},archiveRoot={op+n for op in archives for n in ('.json','.lock')},recycleRestoreRoot={'claims/'+source_id+'.json'})
    for key in before:
        if key not in allowed:typed_equal(before[key],after[key])
        else:
            assert all(after[key].get(n)==value for n,value in before[key].items());assert set(after[key])-set(before[key])<=allowed[key]


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_archive_agent.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=120)


def cleanup_new_copy(folder,q,expected,root):
    """Only the independently proved copy in an inert owned QA fixture.

    Bind parent and leaf FDs again. No recursion, directory creation or unknown
    cleanup; an unexpected identity aborts with the fixture retained.
    """
    archive_proof.request_proof(q);assert folder.is_absolute() and folder.resolve(strict=True)==folder
    parent=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC);leaf=None
    try:
        typed_equal([os.fstat(parent).st_dev,os.fstat(parent).st_ino],q['plan']['parentIdentity']);assert os.path.samestat(os.fstat(parent),folder.lstat())
        leaf=os.open('recycle-rpc.bin',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=parent)
        typed_equal(frame(os.fstat(leaf)),expected['identity']);typed_equal(source_io.file_snapshot(Path('/proc/self/fd/'+str(parent))/'recycle-rpc.bin'),expected)
        name=archive_io.STAGE_PREFIX+q['operationId'];stage=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
        try:
            info=os.fstat(stage);typed_equal([info.st_dev,info.st_ino],root['privateIdentity']);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700) and not os.listdir(stage) and os.path.samestat(info,os.stat(name,dir_fd=parent,follow_symlinks=False))
            typed_equal(frame(os.stat('recycle-rpc.bin',dir_fd=parent,follow_symlinks=False)),expected['identity']);os.unlink('recycle-rpc.bin',dir_fd=parent);assert os.fstat(leaf).st_nlink==0
            assert not os.listdir(stage) and os.path.samestat(info,os.stat(name,dir_fd=parent,follow_symlinks=False));os.rmdir(name,dir_fd=parent)
        finally:os.close(stage)
        os.fsync(parent)
    finally:
        if leaf is not None:os.close(leaf)
        os.close(parent)


def main(revision,binary_name,probe_revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision) and re.fullmatch('[a-f0-9]{40}',probe_revision)
    os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    os.environ.update(GIT_CONFIG_COUNT='1',GIT_CONFIG_KEY_0='safe.directory',GIT_CONFIG_VALUE_0=str(repo))
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a]);assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    probe_sources={}
    for n in PROBE_SOURCES:
        raw=git('show',probe_revision+':'+n);assert raw==(repo/n).read_bytes();probe_sources[n]=hashlib.sha256(raw).hexdigest()
    binary=Path(binary_name);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    preflight();idle();before=baseline(revision);rows=old_rows('0'*64)
    e=repo/'deploy/artifacts'/('container-recycle-archive-agent-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('baseline.json',before);save('business-before.json',rows);save('source.json',dict(revision=revision,probeRevision=probe_revision,validationRevision=sha,validationSourceSha256=closure,probeSourceSha256=probe_sources,binary=str(binary),binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest()))
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    capability=e/'capability';capability.mkdir(mode=0o700);header=dict(revision=revision,probeRevision=probe_revision,containerId='a'*64,instanceId='777:2026-11-06T01:02:03Z',coreId=before['core']['/whaledeck-api'][0]);steps=[];probe=None
    print(json.dumps(dict(installedArchive='started',evidence=e.name)),flush=True)
    try:
        ready=Probe(binary,capability,header);ready.close(True)
        with owned.fixture() as f:
            f['retain_unsettled']=True;folder=f['folder'];leaf=folder/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('installed-purge:'+f['identifier']).encode()).digest(1024**2+7));os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'owned native purge bytes');os.utime(leaf,ns=(1700000000123456789,1700000000123456789))
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header={**header,'containerId':f['identifier'],'instanceId':str(c['State']['Pid'])+':'+c['State']['StartedAt']};original=source_io.file_snapshot(leaf);save('fixture.json',dict(header=header,folder=str(folder),name=c['Name'].lstrip('/'),original=original))
            reader=load('/usr/local/libexec/whaledeck-container-files.py','installed_purge_reader');version=reader.version(header,'work/recycle-rpc.bin',leaf.stat());probe=Probe(binary,e,header)
            process_stat=Path('/proc')/str(probe.process.pid)/'stat';save('probe-process.json',dict(pid=probe.process.pid,startedTick=process_stat.read_text().rsplit(') ',1)[1].split()[19]))
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
            purged_before=steps[-1]['records'];save('purged-before.json',purged_before);archive_cancel,archive_normal=(uuid.uuid4().hex for _ in range(2));assert len({source_id,cancel,normal,archive_cancel,archive_normal})==5
            first=probe.call(dict(action='archive-plan',purgeOperationId=normal,boundaries=True));first_plan=archive_proof.plan_frame(first,header,True);save('archive-plan-frame.json',first)
            typed_equal(first_plan['command']['purge'],steps[-1]['request'])
            refused=probe.call(dict(action='archive-reject',operationId=archive_cancel,plan=first['plan']));typed_equal(refused,dict(kind='archive-rejected',operationId=archive_cancel,refusals=archive_proof.OPERATION_REFUSALS));save('archive-refusals.json',refused)
            assert not (archive_io.STATE/(archive_cancel+'.json')).exists() and not (archive_io.CLAIMS/(source_id+'.json')).exists()
            def archive_capture(op,action,state,p,wire):
                q=dict(operationId=op,owner='D'*64,plan=p);frame=probe.call(dict(action=action,operationId=op,plan=wire['plan']));receipt=archive_proof.receipt_frame(frame,q);assert receipt['state']==state
                records=archive_io.read_operation(q,receipt,purged_before,e/('records-'+str(len(steps))),create=True);physical=archive_io.physical(folder,q);archive_io.physical_proof(physical,original,q,records,receipt);steps.append(dict(phase='archive',action=action,request=q,frame=frame,receipt=receipt,records=records,physical=physical))
            for action,state in archive_workflow.CANCEL:archive_capture(archive_cancel,action,state,first_plan,first)
            second=probe.call(dict(action='archive-plan',purgeOperationId=normal,boundaries=False));second_plan=archive_proof.plan_frame(second,header,False);save('archive-fresh-plan-frame.json',second)
            for action,state in archive_workflow.NORMAL:archive_capture(archive_normal,action,state,second_plan,second)
            end_source=probe.call(dict(action='source-inspect',operationId=source_id,plan=wire['plan']));assert proof.source_receipt_frame(end_source,ref)['state']=='Purged';save('source-after-archive-frame.json',end_source)
            save('steps.json',steps);frames=[proof.unique_json(line) for line in (e/'rpc.jsonl').read_text().splitlines()];summary=archive_workflow.prefix_proof(frames,header);save('live-summary.json',summary)
            preserved(before,baseline(revision),source_id,(cancel,normal),(archive_cancel,archive_normal));typed_equal(old_rows('0'*64),rows);independent(repo,e,True)
            last=steps[-1];current=archive_io.physical(folder,last['request']);typed_equal(current,last['physical']);assert current['source'] is not None and current['staged'] is None
            cleanup_new_copy(folder,last['request'],current['source'],last['records']['root'])
            observed=probe.call(dict(action='archive-inspect',operationId=archive_normal,plan=second['plan']));receipt=archive_proof.receipt_frame(observed,last['request']);assert receipt['state']=='Completed' and receipt['archiveCopied'] is True and receipt['bindingCurrent'] is False
            records=archive_io.read_operation(last['request'],receipt,purged_before,e/'cleanup-records',create=True);typed_equal(records,last['records']);save('cleanup-inspect-frame.json',observed)
            private=folder/source_io.PRIVATE
            if second_plan['privateIdentity'] is not None:
                assert not private.is_symlink() and [private.stat().st_dev,private.stat().st_ino]==second_plan['privateIdentity'] and not list(private.iterdir());private.rmdir()
            else:assert not os.path.lexists(private)
            closing=probe;probe=None;closing.close(True);frames=[proof.unique_json(line) for line in (e/'rpc.jsonl').read_text().splitlines()];summary=archive_workflow.transcript_proof(frames,header);assert summary['afterCleanupBindingObserved'];save('summary.json',summary);f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,source_id,(cancel,normal),(archive_cancel,archive_normal));typed_equal(old_rows('0'*64),rows);idle();save('after.json',after);save('verified.json',dict(revision=revision,signedRpc=True,sourceOperations=1,purgeOperations=2,archiveOperations=2,archiveReceiptFrames=12,sourceAndContainerAbsent=not folder.exists(),historyRetained=True,humanUi=False,exactNewCopyRemoved=True,oldDeletedInodeNotReused=True));independent(repo,e,False)
        print(json.dumps(dict(installedArchive='passed',evidence=e.name,archiveReceiptFrames=12,sourceReceiptFrames=4,humanUi=False)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(f.filename).name,line=f.lineno,function=f.name) for f in traceback.extract_tb(error.__traceback__)]));raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':
    try:assert len(sys.argv)==4;main(*sys.argv[1:])
    except BaseException as error:print(json.dumps(dict(installedArchive='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
