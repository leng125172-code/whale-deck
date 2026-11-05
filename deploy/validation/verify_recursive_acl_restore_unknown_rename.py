#!/usr/bin/env python3
"""Separate read-only Unknown verification before and after exact renamed cleanup."""
import json,os,re,struct,subprocess,sys
from pathlib import Path
import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from verify_recursive_acl_restore_partial_cancel import acquisition,SOURCES
from recursive_acl_history_paging_proof import summary
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows
from validate_container_file_publishing import common
from validate_recursive_acl_restore_paging import restore_database

def verify(name,live=False):
    assert type(live) is bool
    assert re.fullmatch('recursive-acl-restore-unknown-rename-[a-f0-9]{32}',name)
    from recursive_acl_restore_job_results_proof import SOURCES as JOB_SOURCES
    from recursive_acl_restore_unknown_workflow import SOURCES as UNKNOWN_SOURCES,normal_logout,authority_proof
    seeds=(*SOURCES,*JOB_SOURCES,*UNKNOWN_SOURCES)
    repo,evidence,saved,b,row,browser,reader,acl,storage,data,metadata,actual=acquisition(name,seeds)
    assert b['restoreUnknownRename'] is b['restoreJobResults'] is True
    assert b['restorePartialWorkflow'] is b['restorePartialCancel'] is b['restorePartialLogout'] is b['restorePrewriteCancel'] is b['restoreInflightLogout'] is b['restoreReadyLogout'] is False
    from recursive_acl_restore_paging_proof import page_proof,ready_proof as ready_business
    from recursive_acl_restore_job_results_proof import ready_job_proof
    from recursive_acl_restore_unknown_business_proof import require_unknown,require_acknowledged
    from recursive_acl_restore_partial_record_proof import read_unknown_first,completed_first_facts
    from recursive_acl_restore_unknown_record_proof import unknown_renamed_first_proof,prefix_archives_unchanged
    from recursive_acl_restore_unknown_physical import inventory as renamed_inventory,OLD,NEW
    from recursive_acl_restore_agent_io import installed,read_bundle
    from recursive_acl_restore_rpc_proof import child_id
    from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest,expected_execution
    from acl_history_browser_proof import typed_equal
    signal=saved('ready-result.json');initial=saved('ready.sql.json');page_proof(signal,b,row,metadata,0);page_proof(signal['selected'],b,row,metadata,0,selection=True);ready_business(signal,b,row,metadata,initial);ready_job_proof(signal,initial)
    typed_equal(browser,saved('final-auth-result.json'));ui=saved('unknown-result.json');recovery=saved('recovery-result.json');typed_equal(browser['unknown'],ui);typed_equal(browser['recovery'],recovery)
    typed_equal(ui['readySignal'],signal);typed_equal(ui['pending'],saved('pending-result.json'))
    assert browser['revision']==b['revision'] and browser['actualUi'] is True and ui['id']==signal['ready']['id'] and ui['subject']==signal['subject']
    unknown=saved('unknown.sql.json');current=sql_snapshot(signal['ready']['id']);typed_equal(current,saved('acknowledged.sql.json'))
    request,receipt=require_unknown(unknown,signal,initial,b,summary(row,metadata),ui['result'],ui['job'])
    req,ack_receipt=require_acknowledged(unknown,current,recovery['subject'],recovery['result'],recovery['job']);typed_equal(req,request)
    pending=saved('pending.sql.json');assert pending['row']['state']=='Pending' and pending['row']['root_started_at_utc'] is None and pending['row']['publish_barrier'] is False
    assert pending['row']['execution_job_id']==request['operationId'][:8]+'-'+request['operationId'][8:12]+'-'+request['operationId'][12:16]+'-'+request['operationId'][16:20]+'-'+request['operationId'][20:]==expected_execution(signal['ready']['id'])
    io=installed();source_bundle,frame=read_bundle(io,row['request'],False,evidence/'bound-source');typed_equal(frame,saved('source-frame.json'));source=dict(**row,metadata=source_bundle['metadata'])
    cut_physical=saved('physical-at-cut.json');physical=saved('physical-after-rename.json');typed_equal(physical,saved('unknown-final-physical.json'))
    sub=child_id(request['operationId'],0)
    cut_bundle=dict(root=saved('cut-records/root.json'),metadata=saved('cut-records/hdd.json'),children=[dict(root=saved('cut-records/'+sub+'.root.json'),hdd=saved('cut-records/'+sub+'.hdd.json'))])
    assert cut_bundle['root']['state']=='Applying';completed_first_facts(request,cut_bundle,source,frame,row['after'],cut_physical,attribute_digest())
    records=read_unknown_first(io,request,evidence/'unknown-records')
    proof=unknown_renamed_first_proof(request,receipt,cut_bundle,records,source,frame,row['after'],cut_physical,physical,attribute_digest(),OLD,NEW)
    typed_equal(proof,saved('unknown-record-proof.json'))
    unknown_renamed_first_proof(req,ack_receipt,cut_bundle,records,source,frame,row['after'],cut_physical,physical,attribute_digest(),OLD,NEW)
    from recursive_acl_restore_unknown_record_proof import renamed_leaf
    typed_equal(renamed_leaf(cut_physical,physical,OLD,NEW),saved('rename-proof.json'))
    prefix_archives_unchanged(evidence/'cut-records',evidence/'unknown-records',request['operationId'])
    bound=saved('bound-helper.json');p=bound['proof'];assert re.fullmatch(r'whaledeck-container-recursive-acl-restore-[a-f0-9]{32}\.service',bound['unit'])
    assert p['twoKernelFdProofs'] is p['exactCgroupAndInvocation'] is p['pidfdStartTimePinned'] is p['firstRestoreCompleted'] is True and p['mainPid']==bound['pid'] and re.fullmatch('[a-f0-9]{32}',p['invocationId']) and type(p['processStartTicks']) is int and p['processStartTicks']>0
    assert p['startedThrough']==p['finishedThrough']==0 and p['otherEntriesUnchanged']==25
    cut=saved('cut.sql.json');assert cut['row']['state']=='Applying' and cut['row']['publish_barrier'] is True and cut['row']['root_started_at_utc']
    for key in ('id','actor_subject','owner_hash','container_id','instance_id','relative_path','plan_json','plan_hash','execution_job_id'):typed_equal(cut['row'][key],current['row'][key])
    timing=saved('product-helper-exit.json');assert 0<timing['seconds']<15 and timing['validatorKilledHelper'] is False
    assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==0 and ui['uploads']==0 and ui['anonymous']==[401]*9
    action=saved('action-result.json');assert action==dict(id=current['row']['id'],subject=signal['subject'],cancellations=0)
    dismissed=saved('recovery-dismissed-result.json');assert dismissed['id']==current['row']['id'] and dismissed['subject']==recovery['subject']!=signal['subject'] and dismissed['acknowledgements']==0 and dismissed['ownedLookup']==404
    assert recovery['acknowledgements']==1 and recovery['anonymous']==[401]*9
    ordinary=browser['ordinary'];assert len({ordinary['subject'],signal['subject'],recovery['subject']})==3 and ordinary['denied']==[403]*9 and ordinary['posts']==[403]*3 and ordinary['anonymous']==[401]*9 and ordinary['uiRequests']==0
    authority_proof(ui,recovery,ordinary,dismissed)
    for value in (ui['logout'],recovery['logout'],ordinary['logout']):normal_logout(value)
    from validate_metric_continuity import query
    assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=ordinary['subject'])==0
    prove_pages(signal['pages'],b,{row['request']['operationId']:summary(row,metadata)});audits=audit_rows(signal['subject']);typed_equal(audits,saved('history-audits.json'));prove_audits(audits,signal['subject'],b,signal['pages'],[signal['selected'],signal])
    assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
    before=saved('worker-before.json');after=saved('worker-after.json');assert before['Id']==after['Id'] and before['Image']==after['Image'] and before['State']['StartedAt']!=after['State']['StartedAt'] and after['State']['Running'] and after['State']['Health']['Status']=='healthy'
    expected={'selection.png','paging-0.png','inflight.png','unknown.png','recovery-unknown.png','recovery-acknowledged.png','ordinary-denied.png','job-planning-counts.png','job-planning-metadata.png','job-fault-unknown-execution-counts.png','job-fault-unknown-execution-metadata.png','job-fault-acknowledged-execution-counts.png','job-fault-acknowledged-execution-metadata.png'}
    assert {p.name for p in evidence.glob('*.png')}==expected
    for path in evidence.glob('*.png'):
        raw=path.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n' and len(raw)>1000 and struct.unpack('>II',raw[16:24])==(320,600)
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        response=api.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20);assert response.status_code==200 and not response.json().get('pagination',{}).get('next')
        assert not any(str(u['username']).startswith('whaledeck-upload-qa-') for u in response.json()['results'])
    finally:api.close()
    source_path=Path(b['source']);assert source_path.parent==common.APPS and source_path.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source_path.name)
    if live:
        assert source_path.resolve(strict=True)==source_path and Path(b['root'])==source_path/'recursive-tree';typed_equal(renamed_inventory(Path(b['root']),reader,data,acl),physical)
        from validate_container_file_publishing import verify_container
        inspected=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(inspected,b['containerId'],b['containerName'],source_path)
    else:
        assert not os.path.lexists(source_path);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        assert saved('live-independent.json')['physicalTreeReread'] is True;typed_equal(saved('final-preservation.json'),actual)
        accepted=saved('verified.json');assert accepted['objects']==26 and accepted['restoreExecutions']==1 and accepted['unknownRename'] is accepted['unknownRecovery'] is accepted['restoreUnknownRename'] is True and accepted['restorePartialWorkflow'] is accepted['restorePartialCancel'] is accepted['restorePartialLogout'] is False and accepted['canceled']==0 and accepted['sourceAndContainerAbsent'] is True
    return dict(revision=b['revision'],independentReadOnly=True,physicalTreeReread=live,sourceBytesRereadAfterCleanup=False,counts=proof['aggregateCounts'],knownActualChanges=proof['knownActualChanges'],temporaryIdentitiesAbsent=True,sourceAndContainerAbsent=not live,unknownRename=True,unknownRecovery=True,acknowledgedByDifferentAdministrator=True,productUnknownAfterResumeSeconds=timing['seconds'],oldProtected=len(b['protectedFiles']),finalProtected=len(actual))

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live']
        result=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as stream:json.dump(result,stream)
        print(json.dumps(dict(actualRestoreUnknownIndependent='passed',**result)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(actualRestoreUnknownIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None

