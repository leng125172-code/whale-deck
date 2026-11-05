#!/usr/bin/env python3
"""Fresh real Authentik/Vue/Job -> SQL/Root/HDD gate. No old operation replay.

Failed/unsettled fixtures remain. Only byte/inode-proved private test data may
be removed after independent live reread; all permanent histories remain.
"""
import hashlib,json,os,re,subprocess,sys,uuid,time
from pathlib import Path
import requests
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_upload_bff import temporary_user,login,logout,request
from validate_databases import Api
from validate_metric_continuity import query
from validate_container_recycle_agent import baseline,preserved
from container_recycle_agent_io import STATE,PRIVATE,file_snapshot,physical,physical_proof,read_operation
from container_recycle_rpc_proof import native_plan_proof,proto_receipt
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure

SOURCES=('validate_container_recycle_browser.py','validate_container_recycle_browser.mjs','verify_container_recycle_browser.py','test_container_recycle_browser_scope.py')

def sql(identifier):
    assert str(uuid.UUID(identifier))==identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN (c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recycle.confirmed'))
      FROM container_recycle_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''',id=identifier)


def native_plan(m):
    c=m['Command'];location=c['Location'];i=m['SourceIdentity'];p=dict(schemaVersion=m['SchemaVersion'],command={k[0].lower()+k[1:]:v for k,v in location.items()}|dict(expectedVersion=c['ExpectedVersion']),targetProof=m['TargetProof'],parentIdentity=[m['ParentIdentity']['Device'],m['ParentIdentity']['Inode']],parentVersion=m['ParentVersion'],uid=m['OwnerUid'],gid=m['OwnerGid'],fingerprint=m['Fingerprint'],sourceIdentity=[i[k] for k in ('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs')]+[m['SourceChangedAtUnixNs']],sha256=m['Sha256'],attributes=[[a['Name'],a['Value']] for a in m['Attributes']]);native_plan_proof(p);return p


def verify_case(value,fixture,e,create=False):
    data=sql(value['id']);r=data['row'];assert r['actor_subject']==fixture['subject'] and r['container_id']==fixture['header']['containerId'] and r['instance_id']==fixture['header']['instanceId'] and r['relative_path']=='work/recycle-rpc.bin'
    assert r['state']==value['state'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is None and r['lease_expires_at_utc'] is None and data['pending']==0 and data['accepted']==1 and data['planJob']['state']=='Succeeded'
    assert value['confirmation'] is None
    if value['name']=='cancel':
        assert r['state']=='Canceled' and r['execution_job_id'] is None and r['root_started_at_utc'] is None and r['receipt_json'] is None and not r['history_may_exist'] and not r['history_verified'] and data['confirmed']==0 and data['executionJob'] is None
        typed_equal(file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),fixture['original']);return dict(sql=data,source=file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'))
    op=uuid.UUID(r['execution_job_id']).hex;plan=native_plan(json.loads(r['plan_json']));q=dict(operationId=op,owner=r['owner_hash'],plan=plan)
    receipt={k:v for k,v in value['receipt'].items() if k!='id'};receipt['operationId']=op;receipt=proto_receipt(receipt,q)
    assert r['state']==data['executionJob']['state']=='Succeeded' and r['root_started_at_utc'] and r['root_state']=='Trashed' and r['execution_job_id']==value['executionJobId'] and r['source_retained'] is True and r['source_restored'] is False
    assert r['history_verified'] and r['history_digest']==receipt['historyDigest'] and data['confirmed']==1
    stored=json.loads(r['receipt_json']);typed_equal({k[0].lower()+k[1:]:v for k,v in stored.items()},value['receipt'])
    job=json.loads(data['executionJob']['result_json']) if isinstance(data['executionJob']['result_json'],str) else data['executionJob']['result_json'];assert len(job)==14 and job['intentId']==value['id'] and job['jobId']==value['executionJobId'] and job['receiptState']=='Trashed' and job['sourceRetained'] is True and job['sourceRestored'] is False and not job['publishBarrier']
    records=read_operation(q,receipt,e/'originals',create=create);actual=physical(Path(fixture['folder']),op);physical_proof(actual,fixture['original'],records['root'],receipt)
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=actual)


def denied(base,fixture,value):
    endpoint=base+'/api/v1/containers/'+fixture['header']['containerId']+'/files/recycles';recovery=base+'/api/v1/files/container-recycles/recovery';paths=[endpoint+'/'+value['id'],endpoint+'/by-key/'+str(uuid.uuid4()),recovery,recovery+'/'+value['id']]
    anonymous=requests.Session();anonymous.trust_env=False
    try:
        for p in paths:request(anonymous,'GET',p,401)
    finally:anonymous.close()
    with temporary_user(False) as (username,password):
        session,_=login(base,username,password,False)
        try:
            api=Api(base,session)
            for p in paths:request(session,'GET',p,403)
            request(session,'POST',endpoint,403,headers={**api.headers,'Idempotency-Key':str(uuid.uuid4())},json=dict(instanceId=fixture['header']['instanceId'],path='work/recycle-rpc.bin',expectedVersion=fixture['sourceVersion']))
            for action in ('confirm','cancel'):
                request(session,'POST',endpoint+'/'+value['id']+'/'+action,403,headers={**api.headers,'Idempotency-Key':value['id']},json=dict(planHash='not-authority',confirmed=True,version=value['version']) if action=='confirm' else None)
            request(session,'POST',recovery+'/'+value['id']+'/acknowledge',403,headers={**api.headers,'Idempotency-Key':value['id']},json=dict(version=value['version'],confirmed=True));logout(base,session)
        finally:session.close()


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip();sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    preflight();before=baseline(revision);assert query("SELECT json_build_object('n',count(*)) FROM container_recycle_intents WHERE completed_at_utc IS NULL OR publish_barrier;")['n']==0
    e=repo/'deploy/artifacts'/('container-recycle-browser-'+uuid.uuid4().hex);e.mkdir(mode=0o700);save=lambda n,v:(e/n).write_text(json.dumps(v));save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualRecycleBrowser='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';content=hashlib.shake_256(('real-recycle-ui:'+f['identifier']).encode()).digest(1024**2+7);leaf.write_bytes(content);leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'actual original attribute');original=file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','real_recycle_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture.json',fixture)
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password},text=True)
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+440
                        while process.poll() is None:
                            signal=e/'cancel-completed.json'
                            if signal.exists() and not (e/'cancel-proof-accepted.json').exists():
                                canceled=json.loads(signal.read_text());fixture['subject']=canceled['subject'];proof=verify_case(canceled['result'],fixture,e);save('cancel-live-proof.json',proof);save('cancel-proof-accepted.json',dict(id=canceled['result']['id']))
                            assert time.monotonic()<deadline,'BROWSER_GATE_DEADLINE';time.sleep(.2)
                        assert process.returncode==0,'ACTUAL_RECYCLE_UI_FAILED'
                    finally:
                        if process.poll() is None:process.terminate();process.wait(timeout=10)
                assert (e/'browser.stdout').stat().st_size<=16384 and (e/'browser.stderr').stat().st_size<=16384
                b=json.loads((e/'browser.json').read_text());fixture['subject']=b['subject'];save('fixture.json',fixture);assert len(b['completed'])==2 and b['normalDualLogout']
                # Canceled source proof must be captured before the later successful removal.
                # Browser supplies ordered DTOs; prove its cancellation through permanent SQL only here.
                canceled=b['completed'][0];cancel=sql(canceled['id']);assert canceled['name']=='cancel' and cancel['row']['state']=='Canceled' and cancel['row']['execution_job_id'] is None and cancel['row']['receipt_json'] is None and cancel['pending']==0
                save('cancel-sql.json',cancel);success=verify_case(b['completed'][1],fixture,e,create=True);save('live-proof.json',success);denied('http://127.0.0.1:8080',fixture,b['completed'][1])
                with (e/'independent-live.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_browser.py'),e.name,'--live'],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
            op=success['request']['operationId'];private=f['folder']/PRIVATE;retained=private/(op+'.trash');typed_equal(file_snapshot(retained),success['physical']['retained']);assert not os.path.lexists(leaf);retained.unlink();info=private.lstat();assert [info.st_dev,info.st_ino]==success['records']['root']['privateIdentity'] and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,[op]);save('after.json',after);assert not Path(fixture['folder']).exists();preflight();assert query("SELECT json_build_object('n',count(*)) FROM container_recycle_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL;")['n']==0
        save('verified.json',dict(actualUi=True,actualIdentity=True,cases=2,canceledBeforeExecution=1,trashed=1,restoreBusiness=False,ordinaryDenied=8,anonymousDenied=4,normalDualLogout=True,fixtureRemoved=True,historiesRetained=True))
        with (e/'independent-final.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_browser.py'),e.name],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
        print(json.dumps(dict(actualRecycleBrowser='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];save('failure.json',dict(type=type(error).__name__,file=Path(t.filename).name,line=t.lineno));raise

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualRecycleBrowser='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
