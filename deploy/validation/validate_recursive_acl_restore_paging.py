#!/usr/bin/env python3
"""New signed capture, real RESTORE preview next/back and cancel before execution."""
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_container_recursive_acl_agent import Probe,host_identity
from validate_container_recursive_acl_browser import no_active_work
from validate_container_recursive_acl_faults import preservation_snapshot
from recursive_acl_history_probe import NAMES,root_snapshot
from validate_container_recursive_acl_history_browser import audit_rows,acknowledge
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from recursive_acl_history_paging_proof import PATHS,FILES,content,inventory,remove_proved,verify_capture,summary,prove_page
from validate_container_recursive_acl import directory_acl
from verify_container_recursive_acl_agent import proto_plan
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure

PHASE='startup'
LABELS=('first','last','back','last-again')
SOURCES=('validate_recursive_acl_restore_paging.py','validate_recursive_acl_restore_paging.mjs','recursive_acl_restore_paging_proof.py','verify_recursive_acl_restore_paging.py','test_recursive_acl_restore_paging_proof.py')
PROBE_SOURCES=('deploy/validation/TerminalProbe/Program.cs','deploy/validation/TerminalProbe/ContainerRecursiveAclProbe.cs')

def database():
    return query("SELECT json_build_object('intents',(SELECT count(*) FROM container_recursive_acl_intents),'executions',(SELECT count(*) FROM container_recursive_acl_intents WHERE execution_job_id IS NOT NULL),'jobs',(SELECT count(*) FROM operation_jobs WHERE job_type IN('containers.files.recursive-acl','containers.files.recursive-acl-plan')));")

def wait_case(process,evidence,name):
    assert name in tuple('paging-'+v for v in LABELS)+('paging-auth',)
    deadline=time.monotonic()+150;path=evidence/(name+'-result.json')
    while not path.exists():
        assert process.poll() is None,'ACTUAL_PAGING_BROWSER_EXITED'
        assert time.monotonic()<deadline,'PAGING_BROWSER_DEADLINE'
        time.sleep(.1)
    trusted(path);assert path.stat().st_size<=256*1024
    return json.loads(path.read_bytes())

from recursive_acl_restore_agent_io import installed,snapshot,hdd_snapshot,namespace,absent
from validate_container_recursive_acl_restore_browser import sql_snapshot,expected_execution
from recursive_acl_restore_paging_proof import page_proof,ready_proof,canceled_proof


def restore_database():
    return query("SELECT json_build_object('intents',(SELECT count(*) FROM container_recursive_acl_restore_intents),'executions',(SELECT count(*) FROM container_recursive_acl_restore_intents WHERE execution_job_id IS NOT NULL),'active',(SELECT count(*) FROM container_recursive_acl_restore_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));")


def unchanged(b,storage):
    actual=preservation_snapshot(storage)
    assert all(actual.get(k)==v for k,v in b['protectedFiles'].items()) and len(actual)==len(b['protectedFiles'])+54
    io=installed()
    assert snapshot(io.restoreState)==b['restoreRootBefore'] and hdd_snapshot(io,True)==b['restoreHddBefore']
    current=snapshot(io.sourceState);assert all(current.get(k)==v for k,v in b['sourceRootBefore'].items())
    assert root_snapshot(Path('/var/lib/whaledeck-container-directories'))==b['directoryRoot']
    assert owned.common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()} and host_identity(b['revision'])==b['hostIdentity'] and database()==b['database']
    no_active_work();return actual


def no_restore_records(identifier):
    io=installed();operation=uuid.UUID(expected_execution(identifier)).hex
    assert not os.path.lexists(io.restoreState/(operation+'.json')) and not os.path.lexists(io.restoreState/(operation+'.entries'))
    with namespace(io,True) as fd:absent(fd,operation+'.json');absent(fd,operation)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==4;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision,binary_name,probe_revision=sys.argv[1:];assert all(re.fullmatch('[a-f0-9]{40}',v) for v in (revision,probe_revision))
    repo=Path(__file__).resolve().parents[2];binary=Path(binary_name)
    assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    trusted(binary);trusted(binary.parent,True);subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_PAGING_GATE_FIRST'
    validation_revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,validation_revision,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    probe_hashes={}
    for name in PROBE_SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',probe_revision+':'+name]);assert raw;probe_hashes[name]=hashlib.sha256(raw).hexdigest()
    host_hashes={}
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-container-recursive-acl-history-transport.py','whaledeck-privileged'):
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);installed_path=Path('/usr/local/libexec')/name;trusted(installed_path);assert installed_path.read_bytes()==raw;host_hashes[name]=hashlib.sha256(raw).hexdigest()
    published=json.loads((repo/'deploy/artifacts'/('recursive-acl-history-paging-probe-'+probe_revision[:7])/'published-probe.json').read_bytes());assert Path(published['binary'])==binary and published['probeRevision']==probe_revision[:7] and hashlib.sha256(binary.read_bytes()).hexdigest()==published['binarySha256']
    reader=load('/usr/local/libexec/whaledeck-container-files.py','paging_reader');f=reader.trusted_module('whaledeck-files.py','paging_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','paging_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','paging_storage').FileBackups(f)
    from validate_container_recursive_acl_restore_agent import HOST_NAMES
    for name in HOST_NAMES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert (Path('/usr/local/libexec')/name).read_bytes()==raw;host_hashes[name]=hashlib.sha256(raw).hexdigest()
    io=installed();restore_before=restore_database();assert restore_before['active']==0
    before=preservation_snapshot(storage);directories=root_snapshot(Path('/var/lib/whaledeck-container-directories'));core=owned.common.core_snapshot();hosts=host_identity(revision);sql=database();no_active_work()
    evidence=repo/'deploy/artifacts'/('recursive-acl-restore-paging-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(actualRecursiveRestorePaging='started',evidence=evidence.name)),flush=True)
    probe=None
    try:
        with owned.fixture() as fixture:
            PHASE='new-26-object-fixture';root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);data=content(fixture['identifier'])
            for relative in FILES:(root/relative).write_bytes(data)
            for index,(relative,kind) in enumerate(PATHS.items()):
                path=root/relative;os.chown(path,2100+index,3100+index);path.chmod(0o750 if kind=='Directory' else 0o640 if index%2 else 0o600)
                os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                if index%3==0:
                    fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
                    try:acl.write_acl(fd,'access',[dict(tag='user_obj',id=None,permissions=6 if kind=='File' else 7),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=4),dict(tag='other',id=None,permissions=0)])
                    finally:os.close(fd)
            fd=os.open(root,os.O_RDONLY|os.O_DIRECTORY)
            try:acl.write_acl(fd,'default',directory_acl())
            finally:os.close(fd)
            for relative in PATHS:os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
            inspected=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
            location=dict(containerId=fixture['identifier'],instanceId=str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'])
            b=dict(revision=revision,probeRevision=probe_revision,validationSourceRevision=validation_revision,validationSourceSha256=hashes,probeSourceSha256=probe_hashes,probeBinarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(),sourceHashes=host_hashes,**location,
                containerName=inspected['Name'].lstrip('/'),source=str(fixture['folder']),root=str(root),evidence=str(evidence),restoreDatabase=restore_before,restoreRootBefore=snapshot(io.restoreState),restoreHddBefore=hdd_snapshot(io,True),sourceRootBefore=snapshot(io.sourceState),original=inventory(root,reader,data,acl),bytesPerFile=len(data),sha256=hashlib.sha256(data).hexdigest().upper(),coreBefore=core,hostIdentity=hosts,protectedFiles=before,directoryRoot=directories,database=sql,sourceDevice=root.stat().st_dev)
            with storage.target(create_namespace=False,read_only=True) as (cfg,fd):storage.attest(cfg,fd,read_only=True);b['hddDevice']=os.fstat(fd).st_dev
            (evidence/'baseline.json').write_text(json.dumps(b))
            PHASE='one-new-signed-capture';probe=Probe(binary,evidence,dict(revision=revision,probeRevision=probe_revision,**location,coreId=core['/whaledeck-api'][0]),mode='container-recursive-acl-page-capture')
            planned=probe.call(dict(action='plan',aclType='access',profile='named',version=reader.version(location,'work/recursive-tree',root.stat()),boundaries=False))
            assert planned['kind']=='plan' and planned['boundaryRefusals']==0;wire=json.loads(planned['plan']);plan=proto_plan(wire)
            assert [plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[26,25,1,26] and inventory(root,reader,data,acl)==b['original']
            from validate_container_recursive_acl_agent import STATE
            operation=uuid.uuid4().hex;assert not os.path.lexists(STATE/(operation+'.json')) and not os.path.lexists(STATE/(operation+'.entries'))
            response=probe.call(dict(action='apply',operationId=operation,plan=wire));assert response['kind']=='receipt' and response['operationId']==operation
            result=json.loads(response['result']);row=dict(request=dict(operationId=operation,owner='B'*64,plan=plan),result=result,before=b['original'],after=inventory(root,reader,data,acl))
            (evidence/'capture.json').write_text(json.dumps(row));metadata=verify_capture(evidence,b,row,storage,archive=True)
            closing=probe;probe=None;closing.close(True);unchanged(b,storage)
            browser_fixture={k:b[k] for k in ('revision','containerId','instanceId','containerName','evidence')};browser_fixture.update(captureId=operation,historyDigest=result['historyDigest'])
            signals=[]
            with ExitStack() as users:
                admin=users.enter_context(temporary_user(True));ordinary=users.enter_context(temporary_user(False));env={**os.environ};env.pop('VALIDATION_USERNAME',None);env.pop('VALIDATION_PASSWORD',None)
                with (evidence/'browser.stdout').open('w') as out,(evidence/'browser.stderr').open('w') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_recursive_acl_restore_paging.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=env,text=True)
                    try:
                        process.stdin.write(json.dumps(dict(fixture=browser_fixture,credentials=dict(admin=dict(zip(('username','password'),admin)),ordinary=dict(zip(('username','password'),ordinary))))));process.stdin.close()
                        for index,label in enumerate(LABELS):
                            PHASE='real-restore-preview-'+label;signal=wait_case(process,evidence,'paging-'+label)
                            page_proof(signal,b,row,metadata,index);page_proof(signal['selected'],b,row,metadata,0,selection=True)
                            if signals:assert all(signal[k]==signals[0][k] for k in ('ready','registered','key','body','reference','pages','selected','duplicateRegistrationId'))
                            sql=sql_snapshot(signal['ready']['id']);ready_proof(signal,b,row,metadata,sql);no_restore_records(signal['ready']['id']);signals.append(signal)
                            (evidence/('paging-'+label+'.sql.json')).write_text(json.dumps(sql))
                            prove_pages(signal['pages'],b,{operation:summary(row,metadata)})
                            audits=audit_rows(signal['subject']);prove_audits(audits,signal['subject'],b,signal['pages'],[signal['selected'],*signals])
                            (evidence/('paging-'+label+'.audit.json')).write_text(json.dumps(audits))
                            assert inventory(root,reader,data,acl)==row['after'] and verify_capture(evidence,b,row,storage)==metadata;unchanged(b,storage)
                            acknowledge(evidence,'paging-'+label,dict(physicalSourceAndReadyVerified=True))
                        PHASE='canceled-and-normal-identities';auth=wait_case(process,evidence,'paging-auth');assert auth['signals']==signals and auth['subject']==signals[0]['subject']
                        sql=sql_snapshot(auth['result']['id']);canceled_proof(auth,b,row,metadata,sql);no_restore_records(auth['result']['id']);(evidence/'canceled.sql.json').write_text(json.dumps(sql))
                        ordinary_result=auth['ordinary'];assert ordinary_result['subject']!=auth['subject'] and ordinary_result['denied']==[403]*6 and ordinary_result['anonymous']==auth['adminAnonymous']==[401]*6 and ordinary_result['uiRequests']==0
                        for logout in (auth['adminLogout'],ordinary_result['logout']):assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
                        assert audit_rows(ordinary_result['subject'])==[];prove_audits(audit_rows(auth['subject']),auth['subject'],b,auth['pages'],[signals[0]['selected'],*signals])
                        assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=ordinary_result['subject'])==0
                        assert restore_database()==dict(intents=restore_before['intents']+1,executions=restore_before['executions'],active=0)
                        assert inventory(root,reader,data,acl)==row['after'] and verify_capture(evidence,b,row,storage)==metadata;unchanged(b,storage)
                        acknowledge(evidence,'paging-auth',dict(canceledNoWriteAndAuthorityVerified=True));assert process.wait(timeout=90)==0,'ACTUAL_RESTORE_PAGING_BROWSER_FAILED'
                    finally:
                        if process.poll() is None:
                            process.terminate()
                            try:process.wait(timeout=15)
                            except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
            PHASE='independent-live-before-cleanup'
            with (evidence/'independent-live.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_paging.py'),evidence.name,'--live'],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
            assert inventory(root,reader,data,acl)==row['after'];remove_proved(root,reader,data,acl,row['after'])
        PHASE='after-exact-cleanup';assert not fixture['folder'].exists();final=unchanged(b,storage)
        (evidence/'final-preservation.json').write_text(json.dumps(final))
        (evidence/'verified.json').write_text(json.dumps(dict(revision=revision,signedCaptures=1,objects=26,restorePreviewOffsets=[0,25,0,25],restoreExecutions=0,canceled=1,sourceAndContainerAbsent=True,historiesRetained=True,coreUnchanged=11,realRestoreSnapshotPagination=True)))
        PHASE='independent-final-after-cleanup'
        with (evidence/'independent-final.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_paging.py'),evidence.name],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
        print(json.dumps(dict(actualRecursiveRestorePaging='passed',evidence=evidence.name,objects=26,restorePreviewOffsets=[0,25,0,25],restoreExecutions=0)),flush=True)
    except BaseException as error:
        import traceback
        frames=traceback.extract_tb(error.__traceback__);last=frames[-1] if frames else None
        (evidence/'failure.json').write_text(json.dumps(dict(phase=PHASE,type=type(error).__name__,frame=Path(last.filename).name if last else None,line=last.lineno if last else None)))
        print(json.dumps(dict(actualRecursiveRestorePaging='failed',evidence=evidence.name,phase=PHASE,type=type(error).__name__)),flush=True);raise SystemExit(1) from None
    finally:
        if probe is not None:probe.close(False)

if __name__=='__main__':main()
