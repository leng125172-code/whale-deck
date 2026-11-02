#!/usr/bin/env python3
"""Actual owned journal-loss RPC gate; original bytes stay in private evidence.

--verify is a separate read-only process (only writes its report). No SQL job,
human UI, product checkpoint, source rollback or broad cleanup is involved.
"""
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

from validate_container_recursive_permission_agent import Probe,STATE,no_active_work,host_identity
from validate_container_recursive_permissions import inventory,remove_proved,PATHS,NAMES
from verify_container_recursive_permissions import digest,unsigned,private_json
from verify_container_recursive_permission_agent import proto_plan
from validate_container_file_staging import load
import validate_container_file_publishing as owned

PHASE='startup'


def storage_type():
    reader=load('/usr/local/libexec/whaledeck-container-files.py','orphan_reader')
    files=reader.trusted_module('whaledeck-files.py','orphan_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','orphan_hdd').FileBackups(files)
    return reader,storage


def history(storage,operation):
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-permissions-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            saved=storage.read_json(fd,operation+'.json',16*1024**2)
            assert saved['historyDigest']==digest(b'whaledeck-container-recursive-permission-history-v1\n',unsigned(saved,'historyDigest'))
            entries=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,entries,read_only=True);children={}
                for n,entry in enumerate(saved['entries']):
                    sub=hashlib.sha256(b'whaledeck-recursive-permission-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    data=storage.read_json(entries,sub+'.json')
                    assert data['parentOperationId']==operation and data['manifestDigest']==saved['plan']['manifestDigest'] and data['plan']==entry['plan']
                    assert data['historyDigest']==digest(b'whaledeck-container-recursive-permission-entry-history-v1\n',unsigned(data,'historyDigest'))
                    children[sub]=data
            finally:os.close(entries)
            return {'aggregate':saved,'children':children}
        finally:os.close(fd)


def verify(evidence):
    assert evidence.resolve(strict=True)==evidence and evidence.stat().st_uid==0 and stat.S_IMODE(evidence.stat().st_mode)==0o700
    baseline=private_json(evidence/'baseline.json');proof=private_json(evidence/'verified.json');cut=private_json(evidence/'cut.json')
    prior=private_json(evidence/'original.root.json');operation=baseline['operationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
    current=private_json(STATE/(operation+'.json'));assert current==private_json(evidence/'current.root.json')
    assert hashlib.sha256((STATE/(operation+'.json')).read_bytes()).hexdigest()==proof['sealedRootSha256']
    assert prior['state']=='Completed' and prior['completedCount']==4 and prior['changedCount']==2 and prior['historyVerified']
    assert prior['operationId']==current['operationId']==operation and prior['plan']==current['plan'] and prior['owner']==current['owner']=='B'*64
    assert current['state']=='Unknown' and current['unknownCount']==4 and current['completedCount']==current['changedCount']==current['notStartedCount']==0
    assert current['historyMayExist'] and not current['historyVerified']
    assert current['plan']==proto_plan(baseline['protoPlan']) and current['plan']['rootIdentity']==baseline['original']['']
    assert current['plan']['fingerprint']==digest(b'whaledeck-container-recursive-permission-plan-v1\n',unsigned(current['plan'],'fingerprint'))
    assert cut['originalRootSha256']==hashlib.sha256((evidence/'original.root.json').read_bytes()).hexdigest()
    assert cut['beforeLoss']==cut['afterFence']==cut['afterLateApply'] and cut['sourceBytesAndAttributesVerified']
    for relative,kind in PATHS.items():
        before=baseline['original'][relative];after=cut['beforeLoss'][relative]
        assert before[:2]==after[:2] and before[3:8]==after[3:8]
        assert after[2]&0o777==(0o700 if kind=='Directory' else 0o600)
    reader,storage=storage_type();actual=history(storage,operation);assert actual==private_json(evidence/'hdd.json')
    assert actual['aggregate']['plan']==prior['plan'] and actual['aggregate']['historyDigest']==prior['historyDigest']
    assert actual['aggregate']['plan']['manifestDigest']==digest(b'whaledeck-container-recursive-permission-manifest-v1\n',actual['aggregate']['entries'])
    assert len(actual['children'])==4
    for n,entry in enumerate(actual['aggregate']['entries']):
        sub=hashlib.sha256(b'whaledeck-recursive-permission-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
        child=private_json(STATE/(operation+'.entries')/(sub+'.json'))
        assert child['state']=='Completed' and child['plan']==entry['plan'] and child['actualIdentity']==cut['beforeLoss'][entry['relativePath']]
        assert child['historyDigest']==actual['children'][sub]['historyDigest']
    source=Path(baseline['source']);assert source.parent==owned.common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and not os.path.lexists(source)
    absent=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15);assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    assert owned.common.core_snapshot()=={k:tuple(v) for k,v in baseline['coreBefore'].items()}
    assert host_identity(baseline['hostRevision'])==baseline['hostIdentity'];no_active_work()
    assert proof['lateApplyState']=='Unknown' and proof['actualJournalLoss'] and proof['originalRetained'] and not proof['humanBusinessOrUi']
    report={'independentOrphanProof':'passed','operationState':'Unknown','originalCompletedReceiptRetained':True,'actualHddAndChildHistoryVerified':4,
        'exactFixtureAbsent':True,'coreUnchanged':11,'sourceBytesRereadAfterCleanup':False,'humanBusinessOrUi':False}
    (evidence/'independent-final.json').write_text(json.dumps(report));print(json.dumps(report),flush=True)


def main():
    global PHASE
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2]
    if len(sys.argv)==3 and sys.argv[1]=='--verify':
        assert re.fullmatch('container-recursive-orphan-[a-f0-9]{32}',sys.argv[2]);verify(repo/'deploy/artifacts'/sys.argv[2]);return
    assert len(sys.argv)==4
    host_revision,validation_revision=sys.argv[1:3]
    assert all(re.fullmatch('[a-f0-9]{40}',value) for value in (host_revision,validation_revision))
    assert Path(__file__).read_bytes()==subprocess.check_output(['git','-C',str(repo),'show',validation_revision+':deploy/validation/'+Path(__file__).name],timeout=10)
    binary=Path(sys.argv[3]);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    assert binary.stat().st_uid==binary.parent.stat().st_uid==0 and stat.S_IMODE(binary.parent.stat().st_mode)==0o700
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True,timeout=20)
    for name in (*NAMES,'whaledeck-container-recursive-permission-transport.py','whaledeck-privileged'):
        assert (Path('/usr/local/libexec')/name).read_bytes()==subprocess.check_output(['git','-C',str(repo),'show',host_revision+':deploy/host/'+name],timeout=10)
    no_active_work();core=owned.common.core_snapshot();assert len(core)==11;hosts=host_identity(host_revision)
    reader,storage=storage_type();evidence=repo/'deploy/artifacts'/('container-recursive-orphan-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    probe=None
    try:
        with owned.fixture() as fixture:
            PHASE='fixture';root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
            content=hashlib.shake_256(('recursive-orphan:'+fixture['identifier']).encode()).digest(2*1024**2+7)
            for relative,kind in PATHS.items():
                path=root/relative
                if kind=='File':path.write_bytes(content);os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                os.chown(path,2100,3100);path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
                os.utime(path,ns=(1700000000123456789,1700000000123456789))
            inspected=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
            location={'containerId':fixture['identifier'],'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt']}
            initial=inventory(root,reader,content);operation=uuid.uuid4().hex
            probe=Probe(binary,evidence,{'revision':host_revision,**location,'coreId':core['/whaledeck-api'][0]})
            response=probe.call({'action':'plan','fileMode':'0600','directoryMode':'0700','version':reader.version(location,'work/recursive-tree',root.stat()),'boundaries':False})
            plan=json.loads(response['plan']);reference={'operationId':operation,'plan':plan}
            baseline={'hostRevision':host_revision,'validationRevision':validation_revision,**location,'source':str(fixture['folder']),'operationId':operation,
                'original':initial,'protoPlan':plan,'coreBefore':core,'hostIdentity':hosts,'bytesPerFile':len(content),'sha256':hashlib.sha256(content).hexdigest().upper()}
            (evidence/'baseline.json').write_text(json.dumps(baseline))
            PHASE='actual-apply';result=json.loads(probe.call({'action':'apply',**reference})['result']);assert result['state']=='Completed' and result['changedCount']==2
            current=inventory(root,reader,content);hdd=history(storage,operation);(evidence/'hdd.json').write_text(json.dumps(hdd))
            path=STATE/(operation+'.json');raw=path.read_bytes();record=json.loads(raw)
            assert record['state']=='Completed' and record['plan']==proto_plan(plan) and record['owner']=='B'*64 and record['operationId']==operation and record['historyDigest']==hdd['aggregate']['historyDigest']
            PHASE='exact-owned-journal-loss'
            lock=os.open(STATE/(operation+'.lock'),os.O_RDWR|os.O_NOFOLLOW|os.O_CLOEXEC)
            try:
                info=os.fstat(lock);assert info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o600 and info.st_nlink==1 and info.st_size==0
                fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o600 and info.st_nlink==1 and path.read_bytes()==raw
                backup=evidence/'original.root.json'
                with backup.open('xb') as stream:stream.write(raw);stream.flush();os.fsync(stream.fileno())
                assert backup.read_bytes()==raw and path.read_bytes()==raw
                durable=os.open(evidence,os.O_RDONLY|os.O_DIRECTORY)
                try:os.fsync(durable)
                finally:os.close(durable)
                path.unlink()  # exact owned UUID JSON only; original bytes retained and synced
                for directory in (STATE,evidence):
                    fd=os.open(directory,os.O_RDONLY|os.O_DIRECTORY)
                    try:os.fsync(fd)
                    finally:os.close(fd)
            finally:os.close(lock)
            assert not path.exists() and inventory(root,reader,content)==current
            PHASE='discard-fence';fenced=json.loads(probe.call({'action':'discard',**reference})['result'])
            assert fenced['state']=='Unknown' and fenced['unknownCount']==4 and fenced['historyMayExist'] and not fenced['bindingCurrent']
            sealed=path.read_bytes();after_fence=inventory(root,reader,content);assert after_fence==current
            PHASE='late-apply';late=json.loads(probe.call({'action':'apply',**reference})['result'])
            assert late['state']=='Unknown' and late['replayed'] and path.read_bytes()==sealed
            after_late=inventory(root,reader,content);assert after_late==current and history(storage,operation)==hdd
            (evidence/'current.root.json').write_bytes(sealed)
            (evidence/'cut.json').write_text(json.dumps({'originalRootSha256':hashlib.sha256(raw).hexdigest(),'beforeLoss':current,'afterFence':after_fence,'afterLateApply':after_late,'sourceBytesAndAttributesVerified':True}))
            closing=probe;probe=None;closing.close(True)
            PHASE='proved-cleanup';remove_proved(root,reader,content,current)
        assert not fixture['folder'].exists() and owned.common.core_snapshot()==core and host_identity(host_revision)==hosts;no_active_work()
        proof={'hostRevision':host_revision,'validationRevision':validation_revision,'actualJournalLoss':True,'originalRetained':True,
            'newState':'Unknown','lateApplyState':'Unknown','sealedRootSha256':hashlib.sha256(sealed).hexdigest(),'hddUnchanged':True,
            'fullBytesAndNineFieldIdentityUnchangedAfterLoss':True,'exactCleanup':True,'humanBusinessOrUi':False}
        (evidence/'verified.json').write_text(json.dumps(proof));print(json.dumps({'orphanRpc':'passed','evidence':evidence.name,**proof}),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__}));raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':main()
