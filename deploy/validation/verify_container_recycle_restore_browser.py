#!/usr/bin/env python3
"""Separate read-only process: permanent SQL, archived originals and live HDD.

No product writes, identity changes, native Apply/Discard, or cleanup.
"""
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_restore_browser import SOURCES,sql,old_rows
from validate_container_recycle_restore_agent import baseline,preserved
from validate_container_recycle_browser import sql as source_sql
from validate_container_file_publishing import verify_container,common
from container_recycle_restore_rpc_proof import record_proof
from container_recycle_rpc_proof import record_proof as source_record_proof
from recursive_acl_restore_agent_io import read_at
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-restore-browser-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    s=private_json(e/'source.json');f=private_json(e/'fixture.json');b=private_json(e/'browser.json')
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    assert f['revision']==b['revision']==s['revision'] and f['containerId']==b['containerId'] and f['subject']==b['subject']
    assert b['actualUi'] and b['normalDualLogout'] and b['reloginOriginalReferencesDenied']==5 and len(b['completed'])==2 and len(set(b['keys']))==2
    source=private_json(e/'source-proof.json');cancel=private_json(e/'cancel-proof.json');success=private_json(e/'success-proof.json')
    typed_equal(source_sql(b['source']['id']),source['sql'])
    for n,key in [('root.json','root'),('hdd.json','history')]:
        _,value,_=read_at(None,str(e/'source-originals'/n));typed_equal(value,source['records'][key])
    source_record_proof(source['request'],source['receipt'],source['records']['root'],source['records']['history'],data_sha256=source['records']['data']['sha256'],data_size=source['records']['data']['sizeBytes'])
    typed_equal(source['physical']['retained']['identity'][:8],f['original']['identity'][:8]);assert source['physical']['source'] is None
    for index,(phase,proof) in enumerate((('cancel',cancel),('success',success))):
        dto=b['completed'][index];assert dto['name']==phase
        marker=private_json(e/(phase+'-completed.json'));typed_equal(marker['result'],{k:v for k,v in dto.items() if k!='name'})
        assert private_json(e/(phase+'-accepted.json'))['id']==dto['id'];typed_equal(sql(dto['id']),proof['sql'])
        row=proof['sql']['row'];assert row['source_intent_id']==b['source']['id'] and row['state']==dto['state'] and row['actor_subject']==f['subject'] and row['completed_at_utc']
        assert (row['container_id'],row['instance_id'],row['relative_path'])==(f['containerId'],f['instanceId'],'work/recycle-rpc.bin')
        assert not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None and proof['sql']['pending']==0
        plan=managed_plan(json.loads(row['plan_json']));typed_equal(plan['command']['source'],source['request'])
        job_summary(stored_object(proof['sql']['planJob']['result_json']),row,execution_stage=False)
        if phase=='cancel':
            assert row['state']=='Canceled' and row['execution_job_id'] is row['root_started_at_utc'] is row['receipt_json'] is None
            assert proof['sql']['confirmed']==0 and proof['sql']['canceled']==1 and not row['history_verified']
            typed_equal(proof['physical'],source['physical'])
            for suffix in ('.json','.lock'):assert not (restore_io.STATE/(uuid.UUID(execution(row['id'])).hex+suffix)).exists()
        else:
            assert row['state']=='Succeeded' and row['execution_job_id']==execution(row['id']) and row['root_state']=='Completed'
            assert row['binding_current'] is row['source_restored'] is row['restored_by_this_operation'] is row['history_verified'] is True
            assert row['source_retained'] is False and proof['sql']['confirmed']==1
            typed_equal(proof['request']['plan'],plan);typed_equal(managed_receipt(dto['receipt'],proof['request']),proof['receipt'])
            job_summary(stored_object(proof['sql']['executionJob']['result_json']),row,execution_stage=True)
    assert private_json(e/'source-accepted.json')['id']==b['source']['id']
    for key,name in [('root','restore-root.json'),('claim','source-claim.json'),('sourceRoot','source-root.json'),('history','source-hdd.json')]:
        _,value,_=read_at(None,str(e/'restore-originals'/name));typed_equal(value,success['records'][key])
    record_proof(success['request'],success['receipt'],success['records']['root'],success['records']['claim'],success['records']['sourceRoot'],success['records']['history'],data_sha256=success['records']['data']['sha256'],data_size=success['records']['data']['sizeBytes'])
    typed_equal(restore_io.read_operation(success['request'],success['receipt'],e/'restore-originals'),success['records'])
    for http,job in zip(b['jobProofs'],(cancel['sql']['planJob'],success['sql']['executionJob']),strict=True):
        assert http['id']==job['id'] and http['state']==job['state'];typed_equal(json.loads(http['resultJson']),stored_object(job['result_json']))
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder)
        assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
        value=source_io.physical(folder,source['request']['operationId']);typed_equal(value,success['physical']);restore_io.physical_proof(value,f['original'],success['records'],success['receipt'])
    else:
        assert private_json(e/'independent-live.json')['physicalReread'] is True and private_json(e/'verified.json')['fixtureRemoved'] is True
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(private_json(e/'after.json'),baseline(s['revision']))
    ops=[uuid.UUID(execution(v['id'])).hex for v in b['completed']]
    preserved(private_json(e/'before.json'),baseline(s['revision']),source['request']['operationId'],ops)
    typed_equal(private_json(e/'prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,physicalReread=live,hddDataReread=True,actualIdentity=True,actualUi=True,readyCanceledWithoutExecution=True,completedIndependentRestore=True,historiesRetained=True)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live)
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(actualRecycleRestoreIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(actualRecycleRestoreIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
