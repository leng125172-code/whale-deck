#!/usr/bin/env python3
"""Only the named failed QA run: preserve facts, real current-admin ACK, exact cleanup."""
import hashlib,json,os,sys,subprocess,time,uuid
from pathlib import Path
from contextlib import ExitStack
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import installed,read_at,read_bundle,namespace,archive_raw,snapshot,hdd_snapshot
from recursive_acl_restore_rpc_proof import source_proof,manifest_proof,child_id,digest
from recursive_acl_restore_record_proof import records_proof,receipt_proof
from recursive_acl_restore_business_proof import managed_plan,managed_receipt
from recursive_acl_history_paging_proof import content,inventory,remove_proved
from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest
from validate_container_upload_bff import temporary_user
from validate_container_recursive_acl_history_browser import acknowledge
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted
from acl_source_apply_missing_proof import source_closure

NAME='recursive-acl-restore-prewrite-cancel-bd15b60dd0914c08a8728c1758cf9259'
SOURCES=('recover_recursive_acl_restore_prewrite.py','recover_recursive_acl_restore_prewrite.mjs','test_recover_recursive_acl_restore_prewrite.py')

def target(name):
    assert name==NAME
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def inspect(p,*,create=False):
    io=installed();b=json.loads((p/'baseline.json').read_bytes());row=json.loads((p/'capture.json').read_bytes());old=json.loads((p/'failed-original.sql.json').read_bytes());r=old['row'];request=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=managed_plan(r['plan_json']))
    d=p/'original-record-preservation'
    if create:d.mkdir(mode=0o700)
    def save(name,raw):archive_raw(d,name,raw,create)
    bundle,frame=read_bundle(io,row['request'],False,d/'source',create=create);source=dict(**row,metadata=bundle['metadata']);assert records_proof(source,bundle,restore=False,attribute_digest=attribute_digest())==26
    raw,root,_=read_at(None,str(io.restoreState/(request['operationId']+'.json')));save('restore.root.json',raw)
    assert root['state']=='Unknown' and root['startedThrough']==22 and root['finishedThrough']==21 and root['unknownCount']==26
    receipt=managed_receipt(r['receipt_json']);receipt_proof(receipt,request,root,restore=True,binding=False,replayed=receipt['replayed'])
    with namespace(io,True) as fd:
        raw,meta,_=read_at(fd,request['operationId']+'.json');save('restore.hdd.json',raw)
        source_proof(request['plan'],source,frame,row['after']);entries=manifest_proof(request['plan'],meta,source,row['after'],attribute_digest(),operation_id=request['operationId'],owner=request['owner'])
        childfd=io.files.open_at(fd,request['operationId']+'/entries',os.O_RDONLY|os.O_DIRECTORY)
        try:
            physical=inventory(Path(b['root']),io.reader,content(b['containerId']),io.acl);typed_equal(physical,json.loads((p/'failed-physical.json').read_bytes()));completed=[];uncertain=[]
            names={f.name for f in (io.restoreState/(request['operationId']+'.entries')).glob('*.json')};assert names=={child_id(request['operationId'],i)+'.json' for i in range(23)}
            for index,e in enumerate(entries):
                rel=e['relativePath'];before=row['after'][rel];actual=physical[rel]
                if index<23:
                    sub=child_id(request['operationId'],index);raw,cr,_=read_at(None,str(io.restoreState/(request['operationId']+'.entries')/(sub+'.json')));save(sub+'.root.json',raw);raw,hdd,_=read_at(childfd,sub+'.json');save(sub+'.hdd.json',raw)
                    assert cr['operationId']==hdd['operationId']==sub and cr['owner']==hdd['owner']==request['owner'] and cr['plan']==hdd['plan']==e['plan'] and cr['historyVerified'] is True
                    assert hdd['resourceType']=='ContainerRecursiveAclRestoreEntryMetadata' and hdd['parentOperationId']==request['operationId'] and hdd['manifestDigest']==request['plan']['manifestDigest']
                    assert hdd['historyDigest']==cr['historyDigest']==digest(b'whaledeck-container-recursive-acl-restore-entry-history-v1\n',{k:v for k,v in hdd.items() if k!='historyDigest'})
                    if index<22:
                        assert cr['state']=='Completed' and cr['aclConfirmed'] is True and cr['actualIdentity']==actual['identity'] and actual['access']==e['plan']['command']['entries'] and actual['default']==before['default']
                        expected=list(before['identity']);expected[2]=expected[2]&~0o777|e['plan']['targetMode'];assert actual['identity'][:8]==expected[:8];completed.append(rel)
                    else:assert cr['state']=='Unknown' and actual==before;uncertain.append(rel)
                else:assert actual==before
        finally:os.close(childfd)
    assert len(completed)==22 and uncertain==['item-22.bin']
    current=snapshot(io.restoreState);hdd=hdd_snapshot(io,True)
    assert all(current.get(k)==v for k,v in b['restoreRootBefore'].items()) and all(hdd.get(k)==v for k,v in b['restoreHddBefore'].items())
    return dict(verifiedCompletedChildren=22,storedAggregateUnknown=26,unknownChild='item-22.bin',unchangedOtherObjects=4),physical,current,hdd


def wait_file(p,name,process):
    assert name in ('recovery-dismissed.json','recovery-result.json');deadline=time.monotonic()+150
    while not (p/name).exists():assert process.poll() is None and time.monotonic()<deadline;time.sleep(.1)
    trusted(p/name);return json.loads((p/name).read_bytes())


def main(name):
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';p=target(name);repo=p.parents[2]
    assert not (p/'recovered-original.json').exists() and not (p/'browser.json').exists()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,revision,seeds=SOURCES);assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h for n,h in closure.items())
    data=json.loads((p/'failed-original.sql.json').read_bytes());typed_equal(sql_snapshot(data['row']['id']),data);assert data['row']['state']=='Unknown' and data['row']['completed_at_utc'] is None and data['row']['publish_barrier'] is True
    proof,physical,root_before,hdd_before=inspect(p,create=True)
    with temporary_user(True) as credentials:
        packet=dict(id=data['row']['id'],containerId=data['row']['container_id'],originalSubject=data['row']['actor_subject'],evidence=str(p),credentials=dict(zip(('username','password'),credentials)))
        env={**os.environ};env.pop('VALIDATION_USERNAME',None);env.pop('VALIDATION_PASSWORD',None)
        with (p/'recovery-browser.stdout').open('x') as out,(p/'recovery-browser.stderr').open('x') as err:
            process=subprocess.Popen(['node',str(repo/'deploy/validation/recover_recursive_acl_restore_prewrite.mjs')],stdin=subprocess.PIPE,stdout=out,stderr=err,env=env,text=True)
            try:
                process.stdin.write(json.dumps(packet));process.stdin.close();dismissed=wait_file(p,'recovery-dismissed.json',process);assert dismissed['id']==data['row']['id'] and dismissed['subject']!=data['row']['actor_subject'] and dismissed['acknowledgements']==0
                typed_equal(sql_snapshot(data['row']['id']),data);again=inspect(p);assert again==(proof,physical,root_before,hdd_before)
                acknowledge(p,'recovery-allow',dict(originalFactsPreserved=True))
                ui=wait_file(p,'recovery-result.json',process);assert ui['acknowledgements']==1 and ui['id']==data['row']['id'] and ui['subject']==dismissed['subject'] and ui['result']['state']=='Acknowledged' and ui['anonymous']==401 and ui['logout']['normalUiLogout'] and ui['logout']['providerNavigationObserved']
                final=sql_snapshot(data['row']['id']);r=final['row'];assert r['state']=='Acknowledged' and r['completed_at_utc'] and r['publish_barrier'] is False and r['acknowledged_by']==ui['subject'] and final['executionJob']['state']=='Failed'
                for key in ('actor_subject','login_binding','owner_hash','command_json','request_hash','plan_json','plan_hash','execution_job_id','completed_count','changed_count','aborted_count','not_started_count','unknown_count','history_may_exist','history_verified','history_digest','root_state','outcome_digest'):typed_equal(r[key],data['row'][key])
                again=inspect(p);assert again==(proof,physical,root_before,hdd_before);(p/'recovered-ack.sql.json').write_text(json.dumps(final));acknowledge(p,'recovery-done',dict(ackAndPhysicalPreserved=True));assert process.wait(timeout=60)==0
            finally:
                if process.poll() is None:process.terminate();process.wait(timeout=15)
    b=json.loads((p/'baseline.json').read_bytes());io=installed();folder=Path(b['source']);assert folder.parent==common.APPS and folder.name==b['containerName'] and folder.resolve(strict=True)==folder
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    remove_proved(Path(b['root']),io.reader,content(b['containerId']),io.acl,physical)
    original=folder/'original.bin';assert {f.name for f in folder.iterdir()}=={'original.bin'} and not original.is_symlink() and original.stat().st_uid==original.stat().st_gid==1000 and original.stat().st_nlink==1
    assert original.read_bytes()==hashlib.shake_256(('owned-container-original:'+b['containerName']).encode()).digest(common.SIZE);original.unlink();folder.rmdir()
    result=dict(**proof,originalGatePassed=False,currentAdminAck=True,originalRestorationNotReplayed=True,normalOriginalLogoutNotAccepted=True,exactOwnedSourceCleaned=True,recoveryRevision=revision,recoverySourceSha256=closure)
    (p/'recovered-original.json').write_text(json.dumps(result));print(json.dumps({k:v for k,v in result.items() if k!='recoverySourceSha256'}))

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(originalRecovery='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
