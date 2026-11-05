"""Fresh-resource fixed target rename/Unknown/second-admin acknowledgement orchestration.

No entrypoint: the owning fixture must freeze sources, provision identities and
run independent verifiers before cleanup. Failure always retains that fixture.
"""
from concurrent.futures import ThreadPoolExecutor
from threading import Event
import json,time
import signal as kernel_signal
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_prewrite import worker_receipt,stop_browser,drain_original,wait_named
from recursive_acl_restore_ready_logout import wait_signal
from recursive_acl_restore_paging_proof import page_proof,ready_proof
from recursive_acl_restore_job_results_proof import ready_job_proof
from recursive_acl_restore_unknown_business_proof import require_unknown,require_acknowledged,phase
from recursive_acl_restore_partial_record_proof import read_unknown_first
from recursive_acl_restore_unknown_record_proof import unknown_renamed_first_proof,prefix_archives_unchanged
from recursive_acl_restore_unknown_physical import inventory as renamed_inventory,rename_owned,OLD,NEW
from recursive_acl_restore_agent_io import installed,read_bundle
from recursive_acl_history_paging_proof import summary,verify_capture,inventory
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows,acknowledge
from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest,expected_execution
from validate_container_directory_restart import Worker,preflight
from validate_container_upload_recovery_agent import trusted
from bound_container_recursive_acl_restore_cut import freeze_after_first_restore,OBSERVER_WAIT_SECONDS

PACKETS=('unknown','recovery-dismissed','recovery','final-auth')
SOURCES=('recursive_acl_restore_unknown_workflow.py','recursive_acl_restore_unknown_physical.py','installed_ui_restore_unknown_resolution.mjs','validate_recursive_acl_restore_unknown_rename.py','validate_recursive_acl_restore_unknown_rename.mjs','verify_recursive_acl_restore_unknown_rename.py','test_recursive_acl_restore_unknown_workflow.py')
def wait_packet(process,evidence,name):
    assert name in PACKETS
    path=evidence/(name+'-result.json');deadline=time.monotonic()+150
    while not path.exists():
        assert process.poll() is None and time.monotonic()<deadline,'UNKNOWN_BROWSER_EXIT_OR_DEADLINE';time.sleep(.1)
    trusted(path);assert path.stat().st_size<=262144
    return json.loads(path.read_bytes())

def normal_logout(value):
    assert value['normalUiLogout'] is value['providerNavigationObserved'] is value['finalLoginPage'] is True
    assert value['postStatus'] in (302,303) and value['bffStatus']==401 and value['identityStatus'] in (401,403)

def authority_proof(unknown,recovery,ordinary,dismissed):
    assert all(type(v['subject']) is str and v['subject'] for v in (unknown,recovery,ordinary))
    assert len({unknown['subject'],recovery['subject'],ordinary['subject']})==3
    assert unknown['id']==recovery['id']==dismissed['id'] and recovery['subject']==dismissed['subject']
    for key,value in (('registrations',1),('confirmations',1),('cancellations',0),('uploads',0)):
        assert type(unknown[key]) is int and unknown[key]==value
    assert type(recovery['acknowledgements']) is int and recovery['acknowledgements']==1
    assert type(dismissed['acknowledgements']) is int and dismissed['acknowledgements']==0
    assert type(dismissed['ownedLookup']) is int and dismissed['ownedLookup']==404
    for values,wanted in ((unknown['anonymous'],[401]*9),(recovery['anonymous'],[401]*9),
            (ordinary['denied'],[403]*9),(ordinary['posts'],[403]*3),(ordinary['anonymous'],[401]*9)):
        typed_equal(values,wanted)
    assert type(ordinary['uiRequests']) is int and ordinary['uiRequests']==0
    for value in (unknown['logout'],recovery['logout'],ordinary['logout']):normal_logout(value)

def exercise(process,evidence,b,row,metadata,root,reader,content,acl,storage,fixture):
    import validate_recursive_acl_restore_paging as gate
    assert b['restoreUnknownRename'] is True and not b['restorePartialWorkflow']
    renamed=False
    fixture['retain_unsettled']=True;worker=held=None;identifier=None
    save=lambda name,value:(evidence/name).write_text(json.dumps(value))
    current=lambda:renamed_inventory(root,reader,content,acl) if renamed else inventory(root,reader,content,acl)
    try:
        gate.PHASE='unknown-ready';signal=wait_signal(process,evidence,'ready');identifier=signal['ready']['id']
        page_proof(signal,b,row,metadata,0);page_proof(signal['selected'],b,row,metadata,0,selection=True)
        initial=sql_snapshot(identifier);ready_proof(signal,b,row,metadata,initial);ready_job_proof(signal,initial);gate.no_restore_records(identifier);save('ready.sql.json',initial)
        prove_pages(signal['pages'],b,{row['request']['operationId']:summary(row,metadata)})
        audits=audit_rows(signal['subject']);prove_audits(audits,signal['subject'],b,signal['pages'],[signal['selected'],signal]);save('history-audits.json',audits)
        io=installed();bundle,frame=read_bundle(io,row['request'],False,evidence/'bound-source',create=True);source=dict(**row,metadata=bundle['metadata']);save('source-frame.json',frame)
        typed_equal(current(),row['after']);verify_capture(evidence,b,row,storage);gate.unchanged(b,storage)
        worker=Worker();save('worker-before.json',worker_receipt(worker.original));save('worker-preflight.json',preflight([identifier]));worker.stop()
        acknowledge(evidence,'ready',dict(readySqlPhysicalSourceVerified=True,workerStopped=True))
        pending=wait_named(process,evidence,'pending');data=sql_snapshot(identifier);r=data['row']
        assert r['state']=='Pending' and r['root_started_at_utc'] is None and r['publish_barrier'] is False and r['execution_job_id']==pending['executionJobId']==expected_execution(identifier);save('pending.sql.json',data)
        gate.PHASE='unknown-first-completed-cut';events=[];armed=Event()
        def observe(value):
            assert len(events)<64;events.append(value);save('boundary-observations.json',events)
        with ThreadPoolExecutor(max_workers=1) as pool:
            watch=pool.submit(freeze_after_first_restore,r,b['source'],int(b['instanceId'].split(':')[0]),row['after'],current,source,frame,attribute_digest(),evidence/'cut-records',observe,armed)
            assert armed.wait(timeout=5),'UNKNOWN_OBSERVER_NOT_ARMED';worker.start(wait_ready=False);held=watch.result(timeout=OBSERVER_WAIT_SECONDS+3)
        assert held is not None,'FIRST_COMPLETED_CUT_MISSED_NO_REPLAY'
        cut=sql_snapshot(identifier);assert cut['row']['state']=='Applying' and cut['row']['publish_barrier'] and cut['row']['root_started_at_utc']
        physical=current();save('physical-at-cut.json',physical);save('cut.sql.json',cut);save('bound-helper.json',dict(pid=held.pid,unit=held.cgroup,proof=held.proof))
        gate.PHASE='unknown-original-applying-view'
        acknowledge(evidence,'action',dict(boundFirstCompleted=True))
        action=wait_named(process,evidence,'action')
        typed_equal(action,dict(id=identifier,subject=signal['subject'],cancellations=0))
        gate.PHASE='owned-fixed-leaf-rename'
        physical_after,rename=rename_owned(root,reader,content,acl,physical);renamed=True
        save('rename-proof.json',rename);save('physical-after-rename.json',physical_after)
        began=time.monotonic()
        with ThreadPoolExecutor(max_workers=1) as pool:
            exit_watch=pool.submit(held.wait_exit_seconds,began,15)
            kernel_signal.pidfd_send_signal(held.descriptor,kernel_signal.SIGCONT)
            elapsed=exit_watch.result(timeout=16)
        assert elapsed is not None and elapsed<15
        save('product-helper-exit.json',dict(seconds=elapsed,validatorKilledHelper=False));held.release();held=None
        deadline=time.monotonic()+145
        while True:
            unknown=sql_snapshot(identifier)
            if unknown['row']['state']=='Unknown' and unknown['row']['lease_token'] is None:break
            assert unknown['row']['state'] in ('Pending','Applying','Unknown') and time.monotonic()<deadline;time.sleep(.5)
        request,receipt=phase(unknown);records=read_unknown_first(io,request,evidence/'unknown-records',create=True)
        from recursive_acl_restore_rpc_proof import child_id
        sub=child_id(request['operationId'],0)
        prefix_archives_unchanged(evidence/'cut-records',evidence/'unknown-records',request['operationId'])
        cut_bundle=dict(root=json.loads((evidence/'cut-records/root.json').read_text()),metadata=json.loads((evidence/'cut-records/hdd.json').read_text()),children=[dict(root=json.loads((evidence/'cut-records'/(sub+'.root.json')).read_text()),hdd=json.loads((evidence/'cut-records'/(sub+'.hdd.json')).read_text()))])
        proof=unknown_renamed_first_proof(request,receipt,cut_bundle,records,source,frame,row['after'],physical,current(),attribute_digest(),OLD,NEW)
        typed_equal(current(),physical_after)
        save('unknown.sql.json',unknown);save('unknown-record-proof.json',proof);acknowledge(evidence,'settled',dict(id=identifier,state='Unknown'))
        ui=wait_packet(process,evidence,'unknown');typed_equal(ui['readySignal'],signal);typed_equal(ui['pending'],pending)
        assert ui['id']==identifier and ui['subject']==signal['subject']
        require_unknown(unknown,signal,initial,b,summary(row,metadata),ui['result'],ui['job']);normal_logout(ui['logout']);assert ui['anonymous']==[401]*9
        assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==0 and ui['uploads']==0
        typed_equal(sql_snapshot(identifier),unknown);typed_equal(current(),physical_after);acknowledge(evidence,'unknown',dict(unknownPhysicalVerified=True))
        dismissed=wait_packet(process,evidence,'recovery-dismissed');assert dismissed['id']==identifier and dismissed['subject']!=signal['subject'] and dismissed['acknowledgements']==0 and dismissed['ownedLookup']==404
        typed_equal(sql_snapshot(identifier),unknown);read_unknown_first(io,request,evidence/'unknown-records');typed_equal(current(),physical_after)
        acknowledge(evidence,'recovery-allow',dict(originalFactsPreserved=True))
        recovery=wait_packet(process,evidence,'recovery');after=sql_snapshot(identifier)
        assert recovery['subject']==dismissed['subject'] and recovery['acknowledgements']==1
        req,ack_receipt=require_acknowledged(unknown,after,recovery['subject'],recovery['result'],recovery['job']);typed_equal(req,request)
        normal_logout(recovery['logout']);assert recovery['anonymous']==[401]*9
        unknown_renamed_first_proof(req,ack_receipt,cut_bundle,read_unknown_first(io,req,evidence/'unknown-records'),source,frame,row['after'],physical,current(),attribute_digest(),OLD,NEW);typed_equal(current(),physical_after);save('acknowledged.sql.json',after)
        acknowledge(evidence,'recovery-done',dict(ackAndPhysicalPreserved=True))
        final=wait_packet(process,evidence,'final-auth');typed_equal(final['unknown'],ui);typed_equal(final['recovery'],recovery)
        assert final['revision']==b['revision'] and final['actualUi'] is True
        ordinary=final['ordinary'];assert len({ordinary['subject'],recovery['subject'],signal['subject']})==3
        assert ordinary['denied']==[403]*9 and ordinary['posts']==[403]*3 and ordinary['anonymous']==[401]*9 and ordinary['uiRequests']==0
        normal_logout(ordinary['logout'])
        authority_proof(ui,recovery,ordinary,dismissed)
        from validate_metric_continuity import query
        assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=ordinary['subject'])==0
        typed_equal(sql_snapshot(identifier),after);typed_equal(current(),physical_after);verify_capture(evidence,b,row,storage)
        save('unknown-final-physical.json',physical_after);acknowledge(evidence,'final-auth',dict(unknownAndAckVerified=True));assert process.wait(timeout=90)==0
        assert gate.restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
        # The owning gate clears retention only after independent live proof
        # and exact removal of the renamed physical tree, never at UI completion.
    except BaseException as error:
        import traceback
        from pathlib import Path
        frames=traceback.extract_tb(error.__traceback__);last=frames[-1] if frames else None
        save('unknown-failure-context.json',dict(phase=gate.PHASE,type=type(error).__name__,frame=Path(last.filename).name if last else None,line=last.lineno if last else None))
        stop_browser(process)
        if held is not None:held.release();held=None
        if worker is not None:
            try:
                worker.start(wait_ready=False)
                if identifier is not None:drain_original(identifier,evidence)
            except BaseException as drain_error:save('unknown-drain-error.json',dict(type=type(drain_error).__name__,targetRetained=True))
        # Even a drained failure keeps the source for exact reconciliation.
        raise
    finally:
        if held is not None:held.release()
        if worker is not None:worker.start();save('worker-after.json',worker_receipt(worker.current()))
