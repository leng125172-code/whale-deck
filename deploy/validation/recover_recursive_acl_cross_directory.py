#!/usr/bin/env python3
"""Resolve one failed cross-directory QA case; no execution or registration replay."""
from pathlib import Path
import hashlib,json,os,subprocess,sys,time,uuid

from validate_container_recursive_acl_partial import SOURCES,browser,stop,wait_signal,write_signal,source_closure,snapshot,load,inventory,preflight,no_active_work,host_identity,preservation_snapshot,temporary_user
from recursive_acl_partial_proof import verify_records,require_ack
from recursive_acl_partial_unknown_proof import verify_unknown_target
from recursive_acl_cross_directory_proof import directory_snapshot,same_waiting,history_snapshot,ROOT
from recover_recursive_acl_partial_unknown import clean_source
from verify_container_recursive_permissions import private_json
from validate_container_upload_bff import login,logout,request
from validate_databases import Api

CASE='container-recursive-acl-partial-d6a8627144bd402ea4ccb7443dc6ee8e'
INVOCATION='bdda06ea0eac4b5ca7fd6ec31e2afb76'
PHASE='startup'


def target(name):
    assert name==CASE,'ONLY_RECORDED_CROSS_DIRECTORY_FAILURE_CAN_RESOLVE'
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,source,seeds=('recover_recursive_acl_cross_directory.py',*SOURCES));assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    old=target(CASE);b=private_json(old/'baseline.json');scope=private_json(old/'scope.json');original=private_json(old/'partial.sql.json');ready=private_json(old/'ready.json');registered=private_json(old/'waiting-registered.json');waiting=private_json(old/'waiting-baseline.json');initial=directory_snapshot(registered['id'])
    assert scope['revision']==b['revision']==sys.argv[1] and scope['crossDirectory'] and b['kind']=='target-removed' and snapshot(original['row']['id'])==original and original['row']['state']=='Unknown' and original['row']['publish_barrier']
    assert initial['row']['state']=='Pending' and initial['row']['root_started_at_utc'] is initial['row']['completed_at_utc'] is None and not initial['row']['publish_barrier']
    assert host_identity(sys.argv[1])==scope['hostIdentity'];preflight([original['row']['id'],original['row']['execution_job_id'],registered['id']])
    assert not Path(waiting['source']).exists()
    for cid in (b['containerId'],waiting['containerId']):
        missing=subprocess.run(['docker','inspect',cid],capture_output=True);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    for n,sha in scope['sourceHashes'].items():assert hashlib.sha256((Path('/usr/local/libexec')/n).read_bytes()).hexdigest()==sha
    unit='wd-qa-recursive-acl-cross-directory-recheck-51fe911.service';raw=subprocess.check_output(['systemctl','show',unit,'-p','InvocationID','-p','MainPID','-p','Result','-p','ExecMainCode','-p','ExecMainStatus','-p','ExecMainStartTimestamp','-p','ExecMainExitTimestamp'],text=True);exit_info=dict(v.split('=',1) for v in raw.splitlines());assert exit_info['InvocationID']==INVOCATION and exit_info['MainPID']=='0' and exit_info['Result']=='exit-code' and exit_info['ExecMainCode']==exit_info['ExecMainStatus']=='1'
    (old/'systemd-exit.json').open('x').write(json.dumps(dict(unit=unit,**exit_info)));(old/'systemd.journal').open('xb').write(subprocess.check_output(['journalctl','_SYSTEMD_INVOCATION_ID='+INVOCATION,'--no-pager','-o','cat']))
    p=repo/'deploy/artifacts'/('container-recursive-acl-partial-'+uuid.uuid4().hex);p.mkdir(mode=0o700);print(json.dumps(dict(resolutionEvidence=p.name)),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','cross_resolution_reader');files=reader.trusted_module('whaledeck-files.py','cross_resolution_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','cross_resolution_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','cross_resolution_hdd').FileBackups(files)
    content=hashlib.shake_256(('recursive-acl-partial:'+b['containerId']).encode()).digest(b['bytesPerFile']);current=lambda:inventory(Path(b['source'])/'recursive-tree',reader,content,acl);physical=current();assert physical==private_json(old/'physical-partial.json')==private_json(old/'cut-physical.json')
    facts=verify_records(original,b,b['original'],physical,storage,p,'partial');(p/'resolution-scope.json').write_text(json.dumps(dict(originalArtifact=CASE,originalGatePassed=False,validationSourceRevision=source,validationSourceSha256=hashes,waitingBefore=initial,oldDirectoryHistory=history_snapshot())))
    public={k[0].lower()+k[1:]:v for k,v in json.loads(original['row']['receipt_json']).items()};f={**b,'id':original['row']['id'],'originalSubject':ready['subject'],'receipt':public,'evidence':str(p),'crossDirectory':False,'waitingDirectory':None};process=None
    try:
        with temporary_user(True) as credentials:
            PHASE='visible-current-admin-resolution';process=browser(repo,sys.argv[1],'recovery',f,credentials);canceled=wait_signal(p,'recovery-canceled.json',process);assert canceled['subject']!=ready['subject'] and canceled['acknowledgements']==0 and snapshot(original['row']['id'])==original and current()==physical
            write_signal(p,'acknowledge.json',dict(originalProtectionAndHistoryVerified=True));assert process.wait(timeout=100)==0,'FAILURE_RESOLUTION_UI_OR_LOGOUT_FAILED';ui=private_json(p/'recovery-browser.json');ack=snapshot(original['row']['id']);require_ack(original,ack,ui['subject'],unknown=True)
            assert ui['normalDualLogout'] and current()==physical;after=verify_records(ack,b,b['original'],physical,storage,p,'ack');assert facts['outcomes']==after['outcomes'] and (p/'partial.root.json').read_bytes()==(p/'ack.root.json').read_bytes() and (p/'partial.hdd.json').read_bytes()==(p/'ack.hdd.json').read_bytes()
            PHASE='explicit-http-cancel-orphaned-waiting-job';base='http://127.0.0.1:8080';session,profile=login(base,*credentials)
            try:
                assert profile['subject']==ui['subject'];api=Api(base,session);before_cancel=directory_snapshot(registered['id']);same_waiting(initial['row'],before_cancel['row']);assert before_cancel['row']['state']=='Pending' and before_cancel['row']['root_started_at_utc'] is None
                request(session,'POST',base+'/api/v1/jobs/'+registered['id']+'/cancel',202,headers=api.headers)
                until=time.monotonic()+100
                while True:
                    final=directory_snapshot(registered['id'])
                    if final['row']['state']=='Canceled':break
                    assert final['row']['state']=='Pending' and time.monotonic()<until,'ORPHANED_WAITING_JOB_NOT_CANCELED';time.sleep(.5)
                same_waiting(initial['row'],final['row']);assert final['row']['root_started_at_utc'] is None and final['row']['completed_at_utc'] and not final['row']['publish_barrier'] and final['pending']==0 and final['job']['state']=='Canceled' and final['job']['cancel_requested_at_utc']
                raw=(ROOT/(uuid.UUID(registered['id']).hex+'.json')).read_bytes();record=json.loads(raw);assert record['state']=='Aborted' and record['operationId']==uuid.UUID(registered['id']).hex and record['owner']==final['row']['owner_hash'] and record['pendingIdentity'] is None
                (p/'waiting-canceled.sql.json').write_text(json.dumps(final));(p/'waiting-aborted.root.json').write_bytes(raw);logout(base,session)
            finally:session.close()
            (old/'physical-final.json').open('x').write(json.dumps(current()));verify_unknown_target(b,ack['row'],old,private_json);clean_source(b,reader,content,physical,acl)
    finally:stop(process)
    no_active_work();preflight();assert host_identity(sys.argv[1])==scope['hostIdentity'];now=preservation_snapshot(storage);assert all(now.get(k)==v for k,v in scope['preservedFiles'].items())
    result=dict(originalGatePassed=False,failureResolutionPassed=True,unknownExplicitDifferentAdminUiAck=True,orphanedWaitingCanceledThroughAdministratorHttp=True,waitingOriginalBindingUnchanged=True,waitingRootAbortedWithoutStart=True,noRegistrationOrExecutionReplayed=True,originalHistoryRetained=True,exactSourcesAndContainersAbsent=True,crossFenceAcceptance=False)
    (p/'resolution.json').write_text(json.dumps(result));(old/'failure-resolution-reference.json').open('x').write(json.dumps(dict(artifact=p.name,originalGatePassed=False)));print(json.dumps(result),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__) if '/deploy/validation/' in f.filename]
        print(json.dumps(dict(crossFailureResolution='failed',phase=PHASE,failureType=type(error).__name__,locations=frames)),flush=True);raise SystemExit(1) from None
