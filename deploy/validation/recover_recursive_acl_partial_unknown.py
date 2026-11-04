#!/usr/bin/env python3
"""Resume only the recorded failed Unknown case; never repeat removal or execution."""
from pathlib import Path
import hashlib,json,os,re,stat,subprocess,sys

from validate_container_recursive_acl_partial import SOURCES,browser,stop,wait_signal,write_signal,source_closure,snapshot,load,inventory,remove_proved,owned,preflight,no_active_work,host_identity,preservation_snapshot,temporary_user
from recursive_acl_partial_proof import verify_records,require_ack
from recursive_acl_partial_unknown_proof import verify_unknown_target
from verify_container_recursive_permissions import private_json
from validate_metric_continuity import query

CASE='container-recursive-acl-partial-519ae8c7ca29447ab57712780dad1f38'
INVOCATION='3aaacc73344446f183bde7bea9d1d3a6'
PHASE='startup'


def target(name):
    assert name==CASE,'ONLY_RECORDED_UNKNOWN_FAILURE_CAN_RESUME'
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def clean_source(b,reader,content,physical,acl):
    folder=Path(b['source']);assert folder.parent==owned.common.APPS and folder.name==b['containerName'] and folder.resolve(strict=True)==folder
    assert folder.lstat().st_uid==0 and stat.S_IMODE(folder.lstat().st_mode)==0o755
    assert {p.name for p in folder.iterdir()}=={'original.bin','recursive-tree'}
    original=folder/'original.bin';info=original.lstat();identity=owned.common.fingerprint(original);expected=hashlib.shake_256(('owned-container-original:'+folder.name).encode()).digest(owned.common.SIZE)
    assert stat.S_ISREG(info.st_mode) and stat.S_IMODE(info.st_mode)==0o640 and info.st_uid==info.st_gid==1000 and info.st_nlink==1 and original.read_bytes()==expected
    remove_proved(folder/'recursive-tree',reader,content,physical,acl)
    assert {p.name for p in folder.iterdir()}=={'original.bin'} and owned.common.fingerprint(original)==identity and original.read_bytes()==expected
    original.unlink();folder.rmdir()


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2 and re.fullmatch('[a-f0-9]{40}',sys.argv[1]);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();p=target(CASE);b=private_json(p/'baseline.json');scope=private_json(p/'scope.json');ready=private_json(p/'ready.json')
    assert scope['revision']==b['revision']==sys.argv[1] and b['kind']==scope['kind']=='target-removed'
    assert scope['validationSourceSha256']==source_closure(repo,scope['validationSourceRevision'],seeds=SOURCES)
    hashes=source_closure(repo,revision,seeds=('recover_recursive_acl_partial_unknown.py',*SOURCES));assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    for n,sha in scope['sourceHashes'].items():assert hashlib.sha256((Path('/usr/local/libexec')/n).read_bytes()).hexdigest()==sha
    assert not any((p/n).exists() for n in ('verified.json','resumed-recovery.json','recovery.stdout','recovery.stderr','recovery-browser.json'))
    unit='wd-qa-recursive-acl-partial-unknown-recheck.service'
    raw=subprocess.check_output(['systemctl','show',unit,'-p','InvocationID','-p','MainPID','-p','Result','-p','ExecMainCode','-p','ExecMainStatus','-p','ExecMainStartTimestamp','-p','ExecMainExitTimestamp'],text=True);exit_info=dict(v.split('=',1) for v in raw.splitlines())
    assert exit_info['InvocationID']==INVOCATION and exit_info['MainPID']=='0' and exit_info['Result']=='exit-code' and exit_info['ExecMainCode']==exit_info['ExecMainStatus']=='1'
    (p/'systemd-exit.json').open('x').write(json.dumps(dict(unit=unit,**exit_info)));(p/'systemd.journal').open('xb').write(subprocess.check_output(['journalctl','_SYSTEMD_INVOCATION_ID='+INVOCATION,'--no-pager','-o','cat']))
    original=private_json(p/'partial.sql.json');identifier=original['row']['id'];assert original==snapshot(identifier) and identifier==ready['id'];preflight([identifier,original['row']['execution_job_id']])
    assert original['row']['state']=='Unknown' and original['row']['publish_barrier'] and original['row']['completed_at_utc'] is None
    assert host_identity(sys.argv[1])==scope['hostIdentity'];missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    reader=load('/usr/local/libexec/whaledeck-container-files.py','unknown_resume_reader');files=reader.trusted_module('whaledeck-files.py','unknown_resume_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','unknown_resume_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','unknown_resume_hdd').FileBackups(files)
    content=hashlib.shake_256(('recursive-acl-partial:'+b['containerId']).encode()).digest(b['bytesPerFile']);current=lambda:inventory(Path(b['source'])/'recursive-tree',reader,content,acl)
    physical=current();assert physical==private_json(p/'cut-physical.json')==private_json(p/'physical-partial.json');facts=verify_records(original,b,b['original'],physical,storage,p,'partial',True)
    (p/'resume-scope.json').open('x').write(json.dumps(dict(validationSourceRevision=revision,validationSourceSha256=hashes,originalGatePassed=False,noRemovalOrExecutionReplayed=True)))
    public={k[0].lower()+k[1:]:v for k,v in json.loads(original['row']['receipt_json']).items()};b.update(id=identifier,originalSubject=ready['subject'],receipt=public)
    (p/'recovery-fixture.json').open('x').write(json.dumps(b));process=None
    try:
        with temporary_user(True) as credentials:
            PHASE='different-current-admin-visible-recovery';process=browser(repo,sys.argv[1],'recovery',b,credentials);canceled=wait_signal(p,'recovery-canceled.json',process)
            assert canceled['subject']!=ready['subject'] and canceled['acknowledgements']==0 and snapshot(identifier)==original and current()==physical
            verify_records(original,b,b['original'],physical,storage,p,'partial',True);write_signal(p,'acknowledge.json',dict(originalProtectionAndHistoryVerified=True))
            assert process.wait(timeout=100)==0,'RESUMED_RECOVERY_PAGE_OR_LOGOUT_FAILED';ui=private_json(p/'recovery-browser.json');ack=snapshot(identifier);require_ack(original,ack,ui['subject'],unknown=True)
            assert ui['actualUi'] and ui['normalDualLogout'] and ui['acknowledgements']==1 and ui['registrations']==ui['confirmations']==ui['cancellations']==ui['uploads']==0
            assert current()==physical;after=verify_records(ack,b,b['original'],physical,storage,p,'ack');assert facts['counts']==after['counts'] and facts['outcomes']==after['outcomes']
            (p/'physical-final.json').open('x').write(json.dumps(current()));verify_unknown_target(b,ack['row'],p,private_json)
            assert (p/'partial.root.json').read_bytes()==(p/'ack.root.json').read_bytes() and (p/'partial.hdd.json').read_bytes()==(p/'ack.hdd.json').read_bytes()
            audits=query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recursive-acl.unknown-acknowledged';",id=ack['row']['execution_job_id']);assert audits==dict(n=1,actors=[ui['subject']],results=['Unknown'])
            clean_source(b,reader,content,physical,acl)
    finally:stop(process)
    no_active_work();preflight();assert host_identity(sys.argv[1])==scope['hostIdentity'];now=preservation_snapshot(storage);assert all(now.get(k)==v for k,v in scope['preservedFiles'].items())
    result=dict(originalGatePassed=False,resumedRecoveryPassed=True,unknownCounts=facts['counts'],priorPhysicalChangeCount=1,differentAdminNormalDualLogout=True,noRemovalOrExecutionReplayed=True,originalRootAndHddRetained=True,exactSourceAndContainerAbsent=True,oldProtectedFilesUnchanged=len(scope['preservedFiles']))
    (p/'resumed-recovery.json').open('x').write(json.dumps(result));print(json.dumps(result),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__) if '/deploy/validation/' in f.filename]
        print(json.dumps(dict(resumedUnknownRecovery='failed',phase=PHASE,failureType=type(error).__name__,locations=frames)),flush=True);raise SystemExit(1) from None
