#!/usr/bin/env python3
"""Separate read-only SQL/Root/physical/HDD expiry proof, before and after cleanup."""
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_purge_ready_logout import SOURCES,terminal_proof,restored_hdd,preserved
from validate_container_recycle_purge_browser import sql,old_rows,hdd_observation
from validate_container_recycle_purge_agent import baseline,HOSTS
from validate_container_recycle_restore_browser import sql as restore_sql
from validate_container_recycle_browser import sql as source_sql
from validate_container_file_publishing import verify_container,common
from recycle_purge_browser_proof import execution,managed_plan,job_summary,stored_object
from recycle_restore_browser_proof import execution as restore_execution,managed_receipt as restore_receipt,job_summary as restore_job_summary
from container_recycle_rpc_proof import record_proof as source_record_proof
from container_recycle_restore_rpc_proof import record_proof as restore_record_proof
from recursive_acl_restore_agent_io import read_at
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io
import container_recycle_restore_agent_io as restore_io


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-purge-ready-logout-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    src=private_json(e/'source.json');f=private_json(e/'fixture.json');b=private_json(e/'browser.json')
    assert src['validationSourceSha256']==source_closure(repo,src['validationRevision'],seeds=SOURCES)
    for n,h in src['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h and hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==subprocess.check_output(['git','-c','safe.directory='+str(repo),'show',src['revision']+':deploy/host/'+n])
    assert src['revision']==f['revision']==b['revision'] and f['subject']==b['subject'] and f['containerId']==b['containerId']
    source=private_json(e/'source-proof.json');ready=private_json(e/'ready-proof.json');expired=private_json(e/'expired-proof.json');restored=private_json(e/'restored-proof.json')
    typed_equal(source_sql(b['source']['id']),source['sql']);typed_equal(sql(b['original']['id']),expired['sql']);terminal_proof(expired['sql'],b,f)
    assert source['request']['operationId']==uuid.UUID(execution(b['source']['id'],source=True)).hex
    for key,n in (('root','root.json'),('history','hdd.json')):
        _,raw,_=read_at(None,str(e/'source-originals'/n));typed_equal(raw,source['records'][key])
    source_record_proof(source['request'],source['receipt'],source['records']['root'],source['records']['history'],data_sha256=source['records']['data']['sha256'],data_size=source['records']['data']['sizeBytes'])
    typed_equal(ready['sql']['row']['id'],b['original']['id']);assert ready['sql']['row']['state']=='Ready' and ready['noExecutionRoot']
    typed_equal(expired['sql']['planJob'],ready['sql']['planJob'])
    assert expired['sql']['row']['source_intent_id']==source['sql']['row']['id']
    for key in ('source_intent_id','source_proof_hash','command_json','request_hash','plan_json','plan_hash','ready_expires_at_utc','actor_subject','login_binding'):typed_equal(expired['sql']['row'][key],ready['sql']['row'][key])
    typed_equal(ready['plan'],managed_plan(json.loads(expired['sql']['row']['plan_json'])))
    typed_equal(ready['hdd'],source['hdd']);typed_equal(expired['source']['hdd'],source['hdd']);typed_equal(expired['source']['physical'],source['physical'])
    op=uuid.UUID(execution(b['original']['id'])).hex
    for suffix in ('.json','.lock'):assert not (purge_io.STATE/(op+suffix)).exists()
    restore_id=b['restored']['id'];typed_equal(restore_sql(restore_id),restored['sql'])
    rr=restored['sql']['row'];assert rr['state']=='Succeeded' and rr['source_intent_id']==source['sql']['row']['id'] and rr['actor_subject']==f['subject']
    assert (rr['container_id'],rr['instance_id'],rr['relative_path'])==(f['containerId'],f['instanceId'],'work/recycle-rpc.bin')
    assert rr['execution_job_id']==restore_execution(restore_id) and rr['restored_by_this_operation'] is rr['binding_current'] is True
    typed_equal(restore_receipt(b['restored']['receipt'],restored['request']),restored['receipt'])
    for key,n in (('root','restore-root.json'),('claim','source-claim.json'),('sourceRoot','source-root.json'),('history','source-hdd.json')):
        _,raw,_=read_at(None,str(e/'restore-originals'/n));typed_equal(raw,restored['records'][key])
    restore_record_proof(restored['request'],restored['receipt'],restored['records']['root'],restored['records']['claim'],restored['records']['sourceRoot'],restored['records']['history'],data_sha256=restored['records']['data']['sha256'],data_size=restored['records']['data']['sizeBytes'])
    typed_equal(restore_io.read_operation(restored['request'],restored['receipt'],e/'restore-originals'),restored['records'])
    restore_job_summary(stored_object(restored['sql']['executionJob']['result_json']),rr,execution_stage=True)
    hdd=hdd_observation(source['request']['operationId']);restored_hdd(source['hdd'],hdd)
    assert len(b['jobProofs'])==1;job=b['jobProofs'][0];assert job['id']==expired['sql']['planJob']['id'];typed_equal(stored_object(job['resultJson']),stored_object(expired['sql']['planJob']['result_json']))
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder)
        assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
        current=source_io.physical(folder,source['request']['operationId']);typed_equal(current,restored['physical']);restore_io.physical_proof(current,f['original'],restored['records'],restored['receipt'])
    else:
        assert private_json(e/'independent-live.json')['physicalReread'] and private_json(e/'verified.json')['fixtureRemoved']
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(hdd,private_json(e/'hdd-after-cleanup.json'));typed_equal(baseline(src['revision']),private_json(e/'after.json'))
    preserved(private_json(e/'before.json'),baseline(src['revision']),source['request']['operationId'],uuid.UUID(rr['execution_job_id']).hex)
    typed_equal(private_json(e/'prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,physicalReread=live,actualUi=True,actualIdentity=True,naturalExpiry=True,noPurgeExecution=True,sourceRestoredOnlyForQa=True,hddRereadAfterCleanup=not live)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(purgeReadyLogoutIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(purgeReadyLogoutIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
