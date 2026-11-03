#!/usr/bin/env python3
"""Installed signed RPC; every operation/replay independently checks real tree/HDD.

No human BFF/business/UI. Failed proof preserves exact source; no broad cleanup.
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

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted
from validate_container_directory_restart import preflight
from validate_metric_continuity import query
from validate_container_recursive_acl import inventory,remove_proved,preserved,PATHS,NAMES
from validate_container_acl_browser import no_active_work
from acl_source_apply_missing_proof import source_closure

PHASE='startup'
STATE=Path('/var/lib/whaledeck-container-recursive-acl')


def host_identity(revision):
    result={}
    for unit,app in [('whaledeck-agent.service','WhaleDeck.Agent'),('whaledeck-maintenance.service','WhaleDeck.MaintenanceHost')]:
        pid=subprocess.check_output(['systemctl','show',unit,'-p','MainPID','--value'],text=True,timeout=10).strip()
        assert pid.isdigit() and int(pid)>0
        executable=Path('/proc')/pid/'exe';assert executable.readlink().name==app
        info=json.loads(subprocess.check_output([str(executable),'--build-info'],text=True,timeout=10))
        assert info['sourceRevision']==revision
        result[unit]={'pid':int(pid),'sourceRevision':info['sourceRevision']}
    return result


class Probe:
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'rpc.stderr').open('wb');self.transcript=evidence/'rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-recursive-acl'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:
            self.send(header);ready=self.receive();assert ready['kind']=='ready' and ready['revision']==header['revision'] and ready['probeRevision']==header['probeRevision'] and ready['protocolMinor']>=53
        except BaseException:self.close(False);raise
    def send(self,value):
        data=json.dumps(value,ensure_ascii=False,separators=(',',':')).encode()+b'\n';assert len(data)<=65536
        self.process.stdin.write(data);self.process.stdin.flush()
    def receive(self):
        deadline=time.monotonic()+45
        while b'\n' not in self.buffer:
            assert time.monotonic()<deadline,'PROBE_RESPONSE_DEADLINE'
            ready=select.select([self.process.stdout],[],[],max(0,deadline-time.monotonic()))[0]
            assert ready,'PROBE_RESPONSE_DEADLINE'
            raw=os.read(self.process.stdout.fileno(),65536);assert raw,'PROBE_EOF';self.buffer+=raw
            assert len(self.buffer)<=131072,'PROBE_OUTPUT_LIMIT'
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=65536
        value=json.loads(line)
        with self.transcript.open('ab') as stream:stream.write(line+b'\n')
        return value
    def call(self,value):self.send(value);return self.receive()
    def close(self,success):
        try:
            if success:assert self.call({'action':'quit'})=={'kind':'done'}
            self.process.stdin.close()
            try:self.process.wait(timeout=8)
            except subprocess.TimeoutExpired:self.process.kill();self.process.wait(timeout=5)
            if success:assert self.process.returncode==0 and not self.buffer and not self.process.stdout.read(1)
        finally:self.process.stdout.close();self.error.close()


def archive_state(evidence,operation,storage):
    path=STATE/(operation+'.json');trusted(path);record=json.loads(path.read_bytes())
    target=evidence/'root-state';target.mkdir(mode=0o700,exist_ok=True);(target/path.name).write_bytes(path.read_bytes())
    if record['historyVerified']:
        with storage.target(create_namespace=False,read_only=True) as (configuration,base):
            fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,fd,read_only=True);saved=storage.read_json(fd,operation+'.json',16*1024**2)
                (evidence/(operation+'.hdd.json')).write_text(json.dumps(saved))
            finally:os.close(fd)
        batch=STATE/(operation+'.entries');trusted(batch,True);copy=target/batch.name;copy.mkdir(mode=0o700)
        for item in batch.iterdir():
            if item.name.endswith('.json'):trusted(item);(copy/item.name).write_bytes(item.read_bytes())
    return hashlib.sha256(path.read_bytes()).hexdigest()


CASES=(('default','default','named',2),('access','access','named',4),('noop','access','named',0),('mask','access','mask',2),
       ('root','access','root',4),('default-noop','default','named',0),('clear-default','default','clear',2),('cancel','access','mask',4))


def fixture_bytes(identifier):
    assert re.fullmatch('[a-f0-9]{64}',identifier)
    return hashlib.shake_256(('recursive-acl-installed:'+identifier).encode()).digest(2*1024**2+7)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==4;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2];binary=Path(sys.argv[2])
    validation_revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    probe_revision=sys.argv[3];assert re.fullmatch('[a-f0-9]{40}',probe_revision)
    validation_hashes=source_closure(repo,validation_revision,seeds=('validate_container_recursive_acl_agent.py','verify_container_recursive_acl_agent.py','test_recursive_acl_agent_probe_scope.py'))
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in validation_hashes.items())
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_INSTALLED_GATE_FIRST'
    probe_path='deploy/validation/TerminalProbe/ContainerRecursiveAclProbe.cs'
    probe_source=subprocess.check_output(['git','-C',str(repo),'show',probe_revision+':'+probe_path])
    assert (repo/probe_path).read_bytes()==probe_source
    assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True,timeout=30)
    hashes={}
    for name in (*NAMES,'whaledeck-container-backups.py','whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path)
        frozen=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name],timeout=10)
        assert path.read_bytes()==frozen;hashes[name]=hashlib.sha256(frozen).hexdigest()
    trusted(STATE,True);assert stat.S_IMODE(STATE.stat().st_mode)==0o700
    before=owned.common.core_snapshot();assert len(before)==11;checks=no_active_work();hosts=host_identity(revision)
    evidence=repo/'deploy/artifacts'/('container-recursive-acl-agent-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(installedRecursiveAclRpc='started',evidence=str(evidence))),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','recursive_installed_reader')
    files=reader.trusted_module('whaledeck-files.py','recursive_installed_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_installed_hdd').FileBackups(files)
    module=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_installed_acl')
    rows=[];probe=None
    try:
        with owned.fixture() as fixture:
            PHASE='prepare-owned-tree';root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
            content=fixture_bytes(fixture['identifier'])
            for relative,kind in PATHS.items():
                path=root/relative
                if kind=='File':path.write_bytes(content)
                os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                os.chown(path,2100+len(relative),3100+len(relative));path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
                os.utime(path,ns=(1700000000123456789,1700000000123456789))
            inspected=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
            location={'containerId':fixture['identifier'],'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt']}
            baseline={'revision':revision,'probeRevision':probe_revision,'validationSourceRevision':validation_revision,'validationSourceSha256':validation_hashes,'probeSourceSha256':hashlib.sha256(probe_source).hexdigest(),**location,'source':str(fixture['folder']),'name':inspected['Name'].lstrip('/'),'root':str(root),
                'original':inventory(root,reader,content,module),'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),
                'coreBefore':before,'hostIdentity':hosts,'sourceHashes':hashes,'preflight':checks,'candidateOnly':False,'statePath':str(STATE)}
            with storage.target(create_namespace=False,read_only=True) as (_,fd):baseline['hddDevice']=os.fstat(fd).st_dev;assert root.stat().st_dev==baseline['hddDevice']
            (evidence/'baseline.json').write_text(json.dumps(baseline))
            header={'revision':revision,'probeRevision':probe_revision,**location,'coreId':before['/whaledeck-api'][0]};probe=Probe(binary,evidence,header)
            for label,kind,profile,changes in CASES:
                PHASE=label;old=inventory(root,reader,content,module)
                response=probe.call({'action':'plan','aclType':kind,'profile':profile,
                    'version':reader.version(location,'work/recursive-tree',root.stat()),'boundaries':label=='access'})
                assert response['kind']=='plan' and response['boundaryRefusals']==(18 if label=='access' else 0)
                from verify_container_recursive_acl_agent import proto_plan
                wire_plan=json.loads(response['plan']);plan=proto_plan(wire_plan);operation=uuid.uuid4().hex;reference={'operationId':operation,'plan':wire_plan}
                command=plan['command'];assert command['containerId']==location['containerId'] and command['instanceId']==location['instanceId'] and command['path']=='work/recursive-tree'
                assert command['aclType']==kind and plan['rootIdentity']==old['']['identity'] and plan['uid']==plan['gid']==1000
                assert plan['entryCount']==4 and plan['fileCount']==plan['directoryCount']==2 and plan['changeCount']==changes
                assert inventory(root,reader,content,module)==old and not (STATE/(operation+'.json')).exists()
                if label=='access':
                    denied=probe.call({'action':'reject',**reference});assert denied==dict(kind='rejected',operationId=operation,refusals=8)
                    assert not (STATE/(operation+'.json')).exists() and inventory(root,reader,content,module)==old
                result=probe.call({'action':'discard' if label=='cancel' else 'apply',**reference})
                assert result['kind']=='receipt';receipt=json.loads(result['result']);expected='Aborted' if label=='cancel' else 'Completed'
                assert receipt['state']==expected and receipt['aclType']==kind
                after=inventory(root,reader,content,module);path=STATE/(operation+'.json');raw=path.read_bytes();record=json.loads(raw)
                assert record['state']==expected and record['plan']==plan and record['plan']['command']==command
                preserved(old,after,kind)
                if label in ('noop','default-noop','cancel'):assert after==old
                if expected=='Completed':
                    assert receipt['bindingCurrent'] and receipt['historyAvailable'] and receipt['completedCount']==4 and receipt['changedCount']==changes
                    for relative,node in after.items():
                        if kind=='access':assert node['access']==command['fileEntries' if PATHS[relative]=='File' else 'directoryEntries']
                        elif PATHS[relative]=='Directory':assert node['default']==command['directoryEntries']
                else:assert not record['historyMayExist'] and not record['historyVerified'] and record['notStartedCount']==4
                for action in ('inspect','apply','discard'):
                    replay=probe.call({'action':action,**reference});assert replay['kind']=='receipt'
                    replay=json.loads(replay['result']);assert replay['state']==expected and replay['outcomeDigest']==receipt['outcomeDigest'] and replay['replayed']==(action!='discard')
                    assert replay['aclType']==kind and path.read_bytes()==raw and inventory(root,reader,content,module)==after
                root_sha=archive_state(evidence,operation,storage)
                rows.append({'label':label,'request':{k:record[k] for k in ('operationId','owner','plan')},'result':receipt,'before':old,'after':after,'rootSha256':root_sha})
                (evidence/'results.json').write_text(json.dumps(rows))
            closing=probe;probe=None;closing.close(True)
            final=inventory(root,reader,content,module);(evidence/'physical-final.json').write_text(json.dumps(final))
            PHASE='proved-cleanup';remove_proved(root,reader,content,final,module)
        assert not fixture['folder'].exists() and owned.common.core_snapshot()==before and host_identity(revision)==hosts;no_active_work()
        units=subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-recursive-acl-*'],text=True,timeout=10)
        assert not units.strip()
        verified={'revision':revision,'operations':8,'completed':7,'aborted':1,'aggregateHddMetadata':7,'childHddMetadata':28,
            'installedSignedUdsAndFixedHelper':True,'humanBffOrUi':False,'fullBytesAndOriginalInodesOwnersMtimeAttributesAndOtherAcl':True,
            'rootAndHddRetained':True,'exactSourceAndContainerCleaned':True,'coreUnchanged':True,'activeTransientUnits':0,
            'planBoundaryRefusals':18,'operationBoundaryRefusals':8}
        (evidence/'verified.json').write_text(json.dumps(verified))
        print(json.dumps({'installedRecursiveAclRpc':'passed','evidence':evidence.name,**verified}),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__,'completedOperations':len(rows)}))
        print(json.dumps({'installedRecursiveAclRpc':'failed','phase':PHASE,'evidence':evidence.name,'type':type(error).__name__}),flush=True);raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':main()
