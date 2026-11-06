#!/usr/bin/env python3
"""Independent read-only current SQL/Root/HDD and live owned archive-copy proof.

No native RPC, browser/identity mutation, SQL update or fixture cleanup.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_file_publishing import verify_container, common
from validate_container_recycle_archive_browser import SOURCES, HOSTS, baseline, preserved, old_rows, archive_sql
from validate_container_recycle_purge_browser import sql as purge_sql, hdd_observation
from validate_container_recycle_browser import sql as source_sql
from recycle_purge_browser_proof import retained_history, stored_object
from recycle_archive_browser_proof import execution, managed_plan, receipt_join, job_summary
import container_recycle_archive_agent_io as archive_io
import container_recycle_purge_agent_io as purge_io


def verify(name, live=False):
    assert os.geteuid()==0 and type(live) is bool and re.fullmatch('container-recycle-archive-browser-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    s=private_json(e/'source.json');f=private_json(e/'fixture.json');b=private_json(e/'browser.json')
    assert b['actualUi'] and b['normalDualLogout'] and b['reloginOriginalReferencesDenied']==9 and f['subject']==b['subject']
    closure=source_closure(repo,s['validationRevision'],seeds=SOURCES);typed_equal(closure,s['validationSourceSha256'])
    for n,h in closure.items():
        assert hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h
        assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:
        expected=subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),'show',s['revision']+':deploy/host/'+n])
        assert (Path('/usr/local/libexec')/n).read_bytes()==expected
    source=private_json(e/'source-proof.json');purge=private_json(e/'success-proof.json')
    canceled=private_json(e/'archive-cancel-proof.json');ready=private_json(e/'archive-ready-proof.json');final=private_json(e/'archive-final-proof.json');success=private_json(e/'archive-success-proof.json')
    typed_equal(source_sql(source['sql']['row']['id']),source['sql']);typed_equal(purge_sql(purge['sql']['row']['id']),purge['sql'])
    typed_equal(ready,final);assert ready['noExecutionRoot'] and ready['sql']['row']['state']=='Ready'
    assert ready['sql']['row']['execution_job_id'] is ready['sql']['row']['root_started_at_utc'] is ready['sql']['row']['reservation_json'] is ready['sql']['row']['apply_started_at_utc'] is None
    current_purge=purge_io.read_operation(purge['request'],purge['receipt'],source['records']['root'],source['records']['history'],e/'purge-originals')
    typed_equal(current_purge,purge['records'])
    for dto,phase,proof in zip(b['archives'],('archive-cancel','archive-success'),(canceled,success),strict=True):
        marker=private_json(e/(phase+'-completed.json'));typed_equal(marker['result'],{k:v for k,v in dto.items() if k!='name'})
        assert private_json(e/(phase+'-accepted.json'))['id']==dto['id']
        data=archive_sql(dto['id']);typed_equal(data,proof['sql']);row=data['row']
        assert row['actor_subject']==f['subject'] and row['source_intent_id']==purge['sql']['row']['id']
        assert (row['container_id'],row['instance_id'],row['relative_path'])==(f['containerId'],f['instanceId'],'work/recycle-rpc.bin')
        assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None and data['pending']==0
        plan=managed_plan(json.loads(row['plan_json']));typed_equal(plan['command']['purge'],purge['request'])
        job_summary(stored_object(data['planJob']['result_json']),row,purge['sql']['row'],execution_stage=False)
        if phase=='archive-cancel':
            assert row['state']=='Canceled' and row['execution_job_id'] is row['root_started_at_utc'] is row['receipt_json'] is row['reservation_json'] is row['apply_started_at_utc'] is None
            assert row['archive_copied'] is row['source_fenced'] is row['owned_fence'] is row['binding_current'] is None
            assert data['confirmed']==0 and data['canceled']==1 and data['executionJob'] is None
            for suffix in ('.json','.lock'):assert not (archive_io.STATE/(uuid.UUID(execution(row['id'])).hex+suffix)).exists()
        else:
            assert row['state']=='Succeeded' and row['execution_job_id']==execution(row['id']) and row['root_state']=='Completed'
            assert row['archive_copied'] is row['binding_current'] is True and row['root_started_at_utc'] and row['reservation_json'] and row['apply_started_at_utc']
            assert data['confirmed']==1 and data['canceled']==0
            typed_equal(proof['request']['plan'],plan);typed_equal(receipt_join(row,dto['receipt'],proof['request']),proof['receipt'])
            job_summary(stored_object(data['executionJob']['result_json']),row,purge['sql']['row'],execution_stage=True)
    current=archive_io.read_operation(success['request'],success['receipt'],purge['records'],e/'archive-originals');typed_equal(current,success['records'])
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True);typed_equal(hdd,success['hdd'])
    for http,job in zip(b['archiveJobProofs'],(canceled['sql']['planJob'],success['sql']['executionJob']),strict=True):
        assert http['id']==job['id'] and http['state']==job['state'];typed_equal(json.loads(http['resultJson']),stored_object(job['result_json']))
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder)
        assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
        physical=archive_io.physical(folder,success['request']);typed_equal(physical,success['physical'])
        archive_io.physical_proof(physical,f['original'],success['request'],current,success['receipt'])
    else:
        assert private_json(e/'independent-live.json')['newInodePhysicallyReread'] is True and private_json(e/'verified.json')['fixtureRemoved'] is True
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(private_json(e/'after.json'),baseline(s['revision']));typed_equal(private_json(e/'hdd-after-cleanup.json'),hdd)
    purges=[uuid.UUID(v['executionJobId'] or execution(v['id'],'purge')).hex for v in b['completed']]
    archives=[uuid.UUID(execution(v['id'])).hex for v in b['archives']]
    preserved(private_json(e/'before.json'),baseline(s['revision']),source['request']['operationId'],purges,archives)
    typed_equal(private_json(e/'prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,actualUi=True,actualIdentity=True,newInodePhysicallyReread=live,hddDataReread=True,readyCanceledWithoutExecution=True,
                sourcePurgeHistoryImmutable=True,archiveCopied=True,hddRereadAfterCleanup=not live)


if __name__=='__main__':
    try:
        assert len(sys.argv) in (2,3) and (len(sys.argv)==2 or sys.argv[2]=='--live');live=len(sys.argv)==3
        result=verify(sys.argv[1],live);root=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]
        with (root/('independent-live.json' if live else 'independent-final.json')).open('x') as out:json.dump(result,out)
        print(json.dumps(result))
    except BaseException as error:print(json.dumps(dict(independent=False,errorType=type(error).__name__)),flush=True);raise SystemExit(1) from None
