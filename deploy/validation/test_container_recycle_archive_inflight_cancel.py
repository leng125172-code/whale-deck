"""Pure negative joins and safety contracts; not real installed cancellation."""
import ast,copy,json,time,unittest,uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from archive_inflight_cancel_proof import terminal_proof,cut_proof,exit_proof
from test_container_recycle_archive_browser_proof import rows,managed,public
from test_container_recycle_archive_rpc_proof import request


def sample():
    r,p,s=rows();r.update(actor_subject='subject',container_id='container',instance_id='instance',relative_path='work/recycle-rpc.bin',state='Canceled',result_code='CONTAINER_RECYCLE_ARCHIVE_CANCELED',root_state='Aborted',archive_copied=False,source_fenced=False,owned_fence=False,binding_current=None,cancel_requested_at_utc='now',confirmed_at_utc='now',completed_at_utc='now',lease_token=None,lease_expires_at_utc=None)
    s.update(state='Canceled',code=r['result_code'],receiptState='Aborted',archiveCopied=False,sourceFenced=False,ownedFence=False,bindingCurrent=None)
    planning={**s,'jobId':r['id'],'stage':'planning','state':'Ready','code':'CONTAINER_RECYCLE_ARCHIVE_READY','receiptState':None,'reservationStarted':False,'reservationRecorded':False,'applyStarted':False,'archiveCopied':None,'sourceFenced':None,'ownedFence':None,'bindingCurrent':None}
    v=dict(row=r,executionJob=dict(state='Canceled',result_json=s),planJob=dict(state='Succeeded',result_json=planning),pending=0,accepted=1,confirmed=1,canceled=0)
    f=dict(subject='subject',containerId='container',instanceId='instance')
    b=dict(actualUi=True,normalDualLogout=True,reloginOriginalReferencesDenied=9,archives=[{},dict(id=r['id'],state='Canceled')])
    return v,f,p,b


def cut_sample():
    v,_,_,_=sample();q=request();q['operationId']=v['row']['execution_job_id'].replace('-','')
    r=dict(schemaVersion=1,**q,state='Reserved',privateIdentity=None,pendingIdentity=None,preparedIdentity=None,publishedIdentity=None,parentAfterStage='')
    sql=copy.deepcopy(v);sql['row'].update(state='Applying',publish_barrier=True,receipt_json=None,owner_hash=q['owner'],plan_json=json.dumps(managed(q['plan'])))
    sql['row']['reservation_json']=json.dumps({k[0].upper()+k[1:]:value for k,value in public(q,'Reserved').items()})
    p=q['plan'];proof=dict(rootPhase='Reserved',fiveKernelFdProofs=True,currentHddFdAttested=True,sourceClaimMatches=True,targetAbsent=True,stageAbsent=True,copyingNotEntered=True,hddDataReread=True,hddMetadataJoined=True,hddSha256=p['archiveSha256'],hddMetadataDigest=p['historyMetadataDigest'],storageHash=p['storageHash'],hddIdentity=p['archiveIdentity'],archiveFdIdentity=p['archiveIdentity'][:2],storeIdentity=p['storeIdentity'],containerRootIdentity=[1,50001],archiveLockIdentity=[1,50002],purgeLockIdentity=[1,50003],sourceLockIdentity=[1,50004])
    return r,sql,proof,q


class ArchiveCancelProofTests(unittest.TestCase):
    def test_apply_marker_is_distinct_from_actual_copy(self):
        v,f,p,b=sample();self.assertTrue(terminal_proof(v,f,p,b)['durableApplyMarkerIsNotCopy'])
        v['row']['binding_current']=False;v['executionJob']['result_json']['bindingCurrent']=False;terminal_proof(v,f,p,b)

    def test_ready_only_cancel_uncertain_or_success_cannot_pass(self):
        for key,bad in (('execution_job_id',None),('root_started_at_utc',None),('reservation_json',None),('apply_started_at_utc',None),('archive_copied',None),('archive_copied',True),('root_state','Unknown'),('publish_barrier',True),('source_fenced',True),('binding_current',True)):
            v,f,p,b=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_other_actor_target_source_job_or_pending_message_is_refused(self):
        for key,bad in (('actor_subject','foreign'),('relative_path','other'),('container_id','other'),('instance_id','other'),('source_intent_id',str(uuid.uuid4()))):
            v,f,p,b=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)
        v,f,p,b=sample();v['pending']=1
        with self.assertRaises(AssertionError):terminal_proof(v,f,p,b)
        v,f,p,b=sample();v['planJob']['result_json']['state']='Canceled'
        with self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_current_hdd_and_five_distinct_fd_identities_required(self):
        r,s,p,q=cut_sample();cut_proof(r,s,p,q)
        for key,bad in (('fiveKernelFdProofs',False),('hddDataReread',False),('hddMetadataJoined',False),('hddMetadataDigest','0'*64),('archiveFdIdentity',[1,50001]),('sourceLockIdentity',[1,50002])):
            with self.subTest(key=key),self.assertRaises(AssertionError):cut_proof(r,s,{**p,key:bad},q)

    def test_late_root_phase_mixed_plan_or_sql_without_durable_apply_fails(self):
        r,s,p,q=cut_sample()
        for key,bad in (('state','Applying'),('privateIdentity',[1,2]),('pendingIdentity',[1,3]),('preparedIdentity',[1]*9),('publishedIdentity',[1]*9),('parentAfterStage','A'*64),('owner','F'*64)):
            with self.subTest(key=key),self.assertRaises(AssertionError):cut_proof({**r,key:bad},s,p,q)
        for key,bad in (('state','Ready'),('publish_barrier',False),('root_started_at_utc',None),('reservation_json',None),('apply_started_at_utc',None),('receipt_json','completed')):
            broken=copy.deepcopy(s);broken['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):cut_proof(r,broken,p,q)

    def test_bounded_original_pidfd_exit_must_follow_ui_action(self):
        v,_,_,_=sample();r=v['row'];a=dict(id=r['id'],executionJobId=r['execution_job_id'],monotonicNs='200')
        e=dict(elapsedSeconds=1,observedWithOriginalPidfd=True,measurementBeginsBeforeAllowingUiAction=True,beganMonotonicNs=100,actionMonotonicNs=200,exitObservedMonotonicNs=300)
        exit_proof(e,a,r['id'])
        for key,bad in (('elapsedSeconds',21),('elapsedSeconds',True),('observedWithOriginalPidfd',False),('actionMonotonicNs',400),('beganMonotonicNs',250)):
            with self.subTest(key=key),self.assertRaises(AssertionError):exit_proof({**e,key:bad},a,r['id'])

    def test_actual_sql_reservation_must_match_original_root_and_request(self):
        r,s,p,q=cut_sample()
        for key,bad in (('Id',str(uuid.uuid4())),('Fingerprint','0'*64),('State','Aborted'),('OwnedFence',False),('ArchiveCopied',True)):
            broken=copy.deepcopy(s);raw=json.loads(broken['row']['reservation_json']);raw[key]=bad;broken['row']['reservation_json']=json.dumps(raw)
            with self.subTest(key=key),self.assertRaises(AssertionError):cut_proof(r,broken,p,q)

    def test_late_observer_success_is_joined_and_released(self):
        from validate_container_recycle_archive_inflight_cancel import settle_cut
        released=[]
        class Handle:
            def release(self):released.append(True)
        pool=ThreadPoolExecutor(max_workers=1)
        def observe():time.sleep(.03);return Handle()
        future=pool.submit(observe);self.assertFalse(future.done());settle_cut(pool,future,None)
        self.assertEqual(released,[True]);self.assertTrue(future.done())

    def test_existing_handle_is_released_even_if_observer_already_failed(self):
        from validate_container_recycle_archive_inflight_cancel import settle_cut
        released=[]
        class Handle:
            def release(self):released.append(True)
        pool=ThreadPoolExecutor(max_workers=1);future=pool.submit(lambda:None);settle_cut(pool,future,Handle());self.assertEqual(released,[True])

    def test_gate_uses_private_stdin_visible_cancel_and_no_replay(self):
        root=Path(__file__).parent;node=(root/'validate_container_recycle_archive_inflight_cancel.mjs').read_text();parent=(root/'validate_container_recycle_archive_inflight_cancel.py').read_text()
        self.assertLess(node.index("'cut-held.json'"),node.index("phase='visible-cancel-original-archive-execution'"))
        self.assertLess(node.index("'cancel-request-began.json'"),node.index("name:'确认请求取消'",node.index("phase='visible-cancel-original-archive-execution'")))
        for forbidden in ('page.route(','context.route(','route.fulfill(','storageState(','tracing.start('):self.assertNotIn(forbidden,node)
        self.assertIn('delete fixture.credentials',node);self.assertIn("'credentials':dict(username=username,password=password)",parent);self.assertNotIn("'VALIDATION_PASSWORD':password",parent)
        self.assertIn('EXACT_ARCHIVE_CUT_MISSED_NO_REPLAY',node);self.assertIn('pool.submit(observe_cut',parent);self.assertIn('held.release()',parent)

    def test_independent_live_proof_precedes_exact_empty_cleanup_and_final_reread(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_inflight_cancel.py').read_text();self.assertLess(s.index('independent(repo,e,True)'),s.index('private.rmdir()'));self.assertLess(s.index('private.rmdir()'),s.index('after_hdd=hdd_observation'));self.assertIn('independent(repo,e,False)',s)
        verifier=(root/'verify_container_recycle_archive_inflight_cancel.py').read_text();calls=[ast.unparse(n.func) for n in ast.walk(ast.parse(verifier)) if isinstance(n,ast.Call)]
        for forbidden in ('cleanup_new_copy','requests.post','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(forbidden,calls)
        for expected in ('cut_proof','exit_proof','record_proof','archive_io.read_operation','archive_io.physical','hdd_observation','source_closure'):self.assertIn(expected,calls)


if __name__=='__main__':unittest.main()
