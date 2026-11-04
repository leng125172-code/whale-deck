"""Installed pre-first-write cancellation orchestration; exact owned identities only."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import subprocess
import time
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_paging_proof import page_proof,ready_proof
from recursive_acl_restore_ready_logout import wait_signal
from recursive_acl_restore_prewrite_proof import read_early,early_abort_proof,sql_proof
from recursive_acl_restore_agent_io import installed,read_bundle,read_at,namespace
from recursive_acl_restore_rpc_proof import source_proof,manifest_proof
from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest,expected_execution
from recursive_acl_history_paging_proof import summary,verify_capture,inventory
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows,acknowledge
from validate_container_directory_restart import Worker,preflight
from bound_container_recursive_acl_restore_cut import freeze_before_first_restore

SOURCES=('validate_recursive_acl_restore_prewrite.py','validate_recursive_acl_restore_prewrite.mjs','recursive_acl_restore_prewrite.py','recursive_acl_restore_prewrite_proof.py','bound_container_recursive_acl_restore_cut.py','verify_recursive_acl_restore_prewrite.py','test_recursive_acl_restore_prewrite_proof.py')

def wait_named(process,evidence,name):
    assert name in ('pending','action','final-auth')
    path=evidence/(name+'-result.json');deadline=time.monotonic()+150
    while not path.exists():
        assert process.poll() is None and time.monotonic()<deadline,'PREWRITE_BROWSER_EXIT_OR_DEADLINE';time.sleep(.1)
    from validate_container_upload_recovery_agent import trusted
    trusted(path);assert path.stat().st_size<=262144
    return json.loads(path.read_bytes())


def worker_receipt(value):
    # Never archive Config.Env: the real Worker contains database/service secrets.
    return dict(Id=value['Id'],Name=value['Name'],Image=value['Image'],State=dict(Running=value['State']['Running'],StartedAt=value['State']['StartedAt'],Health=dict(Status=value['State']['Health']['Status'])))


def stop_browser(process):
    # Only the QA-owned Node child; this never signals the privileged helper.
    if process.poll() is None:
        process.terminate()
        try:process.wait(timeout=15)
        except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)


def quiescent(row,record,lock_free):
    if row['execution_job_id'] is None:return row.get('completed_at_utc') is not None
    return (row['state'] in ('Succeeded','Superseded','Canceled','Failed','Partial','Unknown','Acknowledged')
        and row['lease_token'] is None and record is not None and record['state'] in ('Completed','Aborted','Partial','Unknown') and lock_free is True)


def drain_original(identifier,evidence):
    import fcntl,uuid
    from recursive_acl_restore_agent_io import RESTORE_STATE,read_at
    began=time.monotonic();deadline=began+350;samples=[]
    while True:
        data=sql_snapshot(identifier);r=data['row'];record=None;free=False
        if r['execution_job_id'] is not None:
            op=uuid.UUID(r['execution_job_id']).hex
            try:
                _,record,_=read_at(None,str(RESTORE_STATE/(op+'.json')))
                from recursive_acl_restore_business_proof import managed_plan
                from bound_container_acl_cut import operation_lock
                if record['operationId']!=op or record['owner']!=r['owner_hash'] or record['plan']!=managed_plan(r['plan_json']):record=None
                lock_identity=operation_lock(RESTORE_STATE/(op+'.lock'))
                descriptor=os.open(RESTORE_STATE/(op+'.lock'),os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC)
                try:
                    assert (os.fstat(descriptor).st_dev,os.fstat(descriptor).st_ino)==lock_identity
                    try:fcntl.flock(descriptor,fcntl.LOCK_EX|fcntl.LOCK_NB);free=True;fcntl.flock(descriptor,fcntl.LOCK_UN)
                    except BlockingIOError:pass
                finally:os.close(descriptor)
            except FileNotFoundError:pass
        samples.append(dict(elapsedMs=round((time.monotonic()-began)*1000),state=r['state'],rootState=record['state'] if record else None,operationLockFree=free))
        (evidence/'failure-drain.json').write_text(json.dumps(samples))
        if quiescent(r,record,free):
            (evidence/'failure-drained.sql.json').write_text(json.dumps(data));return True
        if time.monotonic()>=deadline or len(samples)>=800:return False
        time.sleep(.5)


def exercise(process,evidence,b,row,m,root,reader,content,acl,storage,fixture):
    import validate_recursive_acl_restore_paging as gate
    gate.PHASE='confirmed-prewrite-ready';signal=wait_signal(process,evidence,'ready');page_proof(signal,b,row,m,0);page_proof(signal['selected'],b,row,m,0,selection=True)
    initial=sql_snapshot(signal['ready']['id']);ready_proof(signal,b,row,m,initial);gate.no_restore_records(signal['ready']['id']);(evidence/'ready.sql.json').write_text(json.dumps(initial))
    prove_pages(signal['pages'],b,{row['request']['operationId']:summary(row,m)});audits=audit_rows(signal['subject']);prove_audits(audits,signal['subject'],b,signal['pages'],[signal['selected'],signal]);(evidence/'history-audits.json').write_text(json.dumps(audits))
    io=installed();bundle,frame=read_bundle(io,row['request'],False,evidence/'bound-source',create=True);source=dict(**row,metadata=bundle['metadata']);(evidence/'source-frame.json').write_text(json.dumps(frame))
    before=row['after'];current=lambda:inventory(root,reader,content,acl)
    assert current()==before;verify_capture(evidence,b,row,storage);gate.unchanged(b,storage)
    worker=Worker();held=None
    (evidence/'worker-before.json').write_text(json.dumps(worker_receipt(worker.original)))
    try:
        (evidence/'worker-preflight.json').write_text(json.dumps(preflight([signal['ready']['id']])));worker.stop()
        acknowledge(evidence,'ready',dict(readySqlPhysicalSourceVerified=True,workerStopped=True))
        pending=wait_named(process,evidence,'pending');pending_sql=sql_snapshot(signal['ready']['id']);r=pending_sql['row']
        assert r['state']=='Pending' and r['root_started_at_utc'] is None and r['publish_barrier'] is False and r['execution_job_id']==pending['executionJobId']==expected_execution(r['id'])
        (evidence/'pending.sql.json').write_text(json.dumps(pending_sql))
        gate.PHASE='bound-pre-first-restore'
        with ThreadPoolExecutor(max_workers=1) as pool:
            events=[]
            def observe(value):
                assert len(events)<64;events.append(value);(evidence/'boundary-observations.json').write_text(json.dumps(events))
            watch=pool.submit(freeze_before_first_restore,r,b['source'],int(b['instanceId'].split(':')[0]),before,current,observe)
            worker.start(wait_ready=False);held=watch.result(timeout=28)
        assert held is not None,'REAL_RESTORE_PREWRITE_CUT_MISSED_NOT_COUNTED'
        cut=sql_snapshot(r['id']);assert cut['row']['state']=='Applying' and cut['row']['publish_barrier'] and cut['row']['root_started_at_utc']
        request=dict(operationId=r['execution_job_id'].replace('-',''),owner=r['owner_hash'],plan=held.record['plan'])
        source_proof(request['plan'],source,frame,before)
        with namespace(io,True) as fd:
            raw,meta,_=read_at(fd,request['operationId']+'.json');(evidence/'cut-hdd.json').write_bytes(raw)
        manifest_proof(request['plan'],meta,source,before,attribute_digest(),operation_id=request['operationId'],owner=request['owner'])
        assert meta['historyDigest']==held.record['historyDigest'] and current()==before
        (evidence/'cut-root.json').write_text(json.dumps(held.record));(evidence/'cut.sql.json').write_text(json.dumps(cut));(evidence/'bound-helper.json').write_text(json.dumps(dict(pid=held.pid,unit=held.cgroup,proof=held.proof)))
        gate.PHASE='actual-prewrite-cancel';began=time.monotonic()
        with ThreadPoolExecutor(max_workers=1) as pool:
            exit_watch=pool.submit(held.wait_exit_seconds,began,15);acknowledge(evidence,'action',dict(boundPrewrite=True))
            action=wait_named(process,evidence,'action');elapsed=exit_watch.result(timeout=16)
        assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_BOUND_RESTORE'
        (evidence/'product-helper-exit.json').write_text(json.dumps(dict(seconds=elapsed,validatorKilledHelper=False)))
        held.release();held=None;deadline=time.monotonic()+145
        while True:
            final=sql_snapshot(r['id'])
            if final['row']['completed_at_utc']:break
            assert time.monotonic()<deadline;time.sleep(.5)
        assert final['row']['state']=='Canceled' and action['id']==r['id'] and action['subject']==signal['subject'] and current()==before
        (evidence/'terminal.sql.json').write_text(json.dumps(final))
        acknowledge(evidence,'settled',dict(id=r['id'],state='Canceled'))
        gate.PHASE='terminal-ui-and-identities';ui=wait_named(process,evidence,'final-auth')
        req,receipt=sql_proof(final,ui,initial,b,summary(row,m));records=read_early(io,req,evidence/'early-records',create=True)
        proof=early_abort_proof(req,receipt,records,source,frame,before,current(),attribute_digest())
        assert (evidence/'cut-hdd.json').read_bytes()==(evidence/'early-records/hdd.json').read_bytes()
        assert ui['registrations']==ui['confirmations']==ui['cancellations']==1 and ui['uploads']==0 and ui['dismissedWithoutWrite'] is True
        assert ui['readySignal']==signal and ui['normalDualLogout'] and ui['anonymous']==[401]*6
        assert ui['logout']['normalUiLogout'] and ui['logout']['providerNavigationObserved'] and ui['logout']['bffStatus']==401 and ui['logout']['identityStatus'] in (401,403)
        assert ui['ordinary']['subject']!=signal['subject'] and ui['ordinary']['denied']==[403]*6 and ui['ordinary']['posts']==[403]*2 and ui['ordinary']['anonymous']==[401]*6 and ui['ordinary']['uiRequests']==0
        assert ui['ordinary']['logout']['normalUiLogout'] and ui['ordinary']['logout']['providerNavigationObserved'] and ui['ordinary']['logout']['bffStatus']==401
        from validate_metric_continuity import query
        assert audit_rows(ui['ordinary']['subject'])==[] and query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=ui['ordinary']['subject'])==0
        (evidence/'early-proof.json').write_text(json.dumps(proof));assert sql_snapshot(r['id'])==final and current()==before
        acknowledge(evidence,'final-auth',dict(prewriteNoChangeAndHistoryVerified=True));assert process.wait(timeout=90)==0
    except BaseException:
        # Drain before identity cleanup; retain the target on an unproved drain.
        # The outer user context still revokes temporary identities on timeout.
        fixture['retain_unsettled']=True
        stop_browser(process)
        if held is not None:held.release();held=None
        try:
            worker.start(wait_ready=False)
            if drain_original(signal['ready']['id'],evidence):fixture['retain_unsettled']=False
        except BaseException as drain_error:
            (evidence/'failure-drain-error.json').write_text(json.dumps(dict(type=type(drain_error).__name__,containerRetained=True)))
        raise
    finally:
        if held is not None:held.release()
        worker.start();(evidence/'worker-after.json').write_text(json.dumps(worker_receipt(worker.current())))
    gate.unchanged(b,storage);assert current()==before;verify_capture(evidence,b,row,storage)
    assert gate.restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
