"""Constructed lease/fault/baseline proofs; no product service changes here."""
import ast,copy,hashlib,unittest
from datetime import datetime,timedelta,timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_archive_restart_proof as proof
import validate_container_recycle_archive_restart as gate
from archive_restart_terminal_proof import terminal_proof,browser_proof
from test_container_recycle_archive_inflight_logout import sample as logout_sample


def sample():
    v,f,p,b=logout_sample();f['kind']='worker-crash';v['row'].update(state='Failed',result_code='CONTAINER_RECYCLE_ARCHIVE_INTERRUPTED');v['executionJob'].update(state='Failed')
    v['executionJob']['result_json'].update(state='Failed',code=v['row']['result_code']);b.update(kind=f['kind'],originalOwnerStillAdministrator=True,ownerLogoutPostsBeforeRestartOutcome=0);b['archives'][1]['state']='Failed'
    return v,f,p,b


class ArchiveRestartTests(unittest.TestCase):
    def row(self):
        r={k:k for k in proof.IMMUTABLE};r.update(state='Applying',publish_barrier=True,completed_at_utc=None,cancel_requested_at_utc=None,lease_token='old-token',lease_expires_at_utc='2026-11-07T10:01:00+00:00');return r

    def test_immutable_plan_source_reservation_and_original_lease_are_bound(self):
        old=self.row();data=dict(row=copy.deepcopy(old),executionJob=dict(state='Running',cancel_requested_at_utc=None));proof.lease_held(old,data)
        for k,bad in (('source_proof_hash','other'),('plan_hash','other'),('reservation_json',None),('reservation_hash',None),('apply_started_at_utc',None),('lease_token','new'),('lease_expires_at_utc','later'),('state','Reserving'),('publish_barrier',False)):
            changed=copy.deepcopy(data);changed['row'][k]=bad
            with self.subTest(field=k),self.assertRaises(AssertionError):proof.lease_held(old,changed)

    def test_all_fifteen_file_tables_block_before_any_service_command(self):
        self.assertEqual(len(proof.TABLES),15);self.assertIn('container_recycle_archive_intents',proof.TABLES)
        for table in proof.TABLES:
            counts=dict.fromkeys(proof.TABLES,0);counts[table]=1
            with self.subTest(table=table),patch.object(proof,'preflight',return_value={}),patch.object(proof,'query',return_value=counts),patch.object(proof,'command') as cmd,self.assertRaisesRegex(AssertionError,'OTHER_FILE_WORK'):
                proof.restart_allowed(SimpleNamespace(),dict(id='intent',execution_job_id='execution'))
            cmd.assert_not_called()

    def test_only_fixed_archive_helper_and_owned_qa_transient_are_permitted(self):
        helper='whaledeck-container-recycle-archives-'+'a'*32+'.service';owner='whaledeck-qa-archive-restart-'+'b'*12+'.service'
        proof.require_transients({'old':dict(MainPID='0',ControlPID='0',processes=[])},helper,owner)
        for v in (dict(MainPID='4',ControlPID='0',processes=[]),dict(MainPID='0',ControlPID='4',processes=[]),dict(MainPID='0',ControlPID='0',processes=['4'])):
            with self.assertRaises(AssertionError):proof.require_transients({'other':v},helper,owner)
        with self.assertRaises(AssertionError):proof.require_transients({},'whaledeck-agent.service',owner)

    def baseline(self,kind):
        before=dict(core={'/whaledeck-worker':['worker','image','running','old'],'/other':['other','image','running','same']},hosts={'whaledeck-agent.service':dict(pid=10,sourceRevision='a'*40),'whaledeck-maintenance.service':dict(pid=11,sourceRevision='a'*40)},health={'worker':'healthy'},protected={'old':'bytes'},recycleRoot={},recycleHdd={},recycleRestoreRoot={},purgeRoot={},archiveRoot={})
        after=copy.deepcopy(before);after['recycleRoot']['a'*32+'.purge.json']=1;after['purgeRoot']['b'*32+'.json']=2;after['archiveRoot']['d'*32+'.json']=3
        if kind=='worker-crash':after['core']['/whaledeck-worker'][3]='new'
        else:after['hosts']['whaledeck-agent.service']['pid']=12
        service=dict(kind=kind,otherActiveWork=dict(transientScopeVerified=True,checks=dict(otherJobs=0),counts=dict.fromkeys(proof.TABLES,0)),hostsBefore=before['hosts'],hostsAfter=copy.deepcopy(after['hosts']),sameWorkerContainerAndImage=True,workerId='worker',imageId='image',signal='SIGKILL',beforeStartedAt='old',afterStartedAt='new')
        return before,after,service

    def check_preserved(self,a,b,s,kind):proof.preserved_restart(a,b,'a'*32,['b'*32,'c'*32],['d'*32,'e'*32],s,kind)

    def test_worker_may_change_only_start_time_of_exact_container_and_image(self):
        a,b,s=self.baseline('worker-crash');self.check_preserved(a,b,s,'worker-crash')
        for key,bad in (('core',{'/other':['other','image','running','new']}),('protected',{'old':'changed'}),('archiveRoot',{'f'*32+'.json':4})):
            changed=copy.deepcopy(b);changed[key].update(bad)
            with self.subTest(key=key),self.assertRaises(AssertionError):self.check_preserved(a,changed,s,'worker-crash')
        b['core']['/whaledeck-worker'][0]='recreated'
        with self.assertRaises(AssertionError):self.check_preserved(a,b,s,'worker-crash')

    def test_agent_restart_cannot_hide_maintenance_or_other_resource_changes(self):
        a,b,s=self.baseline('agent-restart');self.check_preserved(a,b,s,'agent-restart')
        b['hosts']['whaledeck-maintenance.service']['pid']=99;s['hostsAfter']=copy.deepcopy(b['hosts'])
        with self.assertRaises(AssertionError):self.check_preserved(a,b,s,'agent-restart')

    def lease(self):
        start=datetime(2026,11,7,10,tzinfo=timezone.utc);expiry=start+timedelta(seconds=60);raw=b'original archive reserved';r=self.row();r['lease_expires_at_utc']=expiry.isoformat();final={**r,'completed_at_utc':(expiry+timedelta(seconds=1)).isoformat()}
        samples=[dict(atUtc=(start+timedelta(seconds=n)).isoformat(),state='Applying',barrier=True,leaseToken=r['lease_token'],leaseExpiresAtUtc=r['lease_expires_at_utc'],sourceUnchanged=True,rootSha256=hashlib.sha256(raw).hexdigest()) for n in range(0,60,2)]
        p=dict(waitBeganAtUtc=start.isoformat(),waitEndedAtUtc=expiry.isoformat(),monotonicSeconds=60,originalLeaseToken=r['lease_token'],originalLeaseExpiresAtUtc=r['lease_expires_at_utc'],clocksOrSqlModified=False,samples=samples);return r,final,p,raw

    def test_natural_lease_requires_real_time_dense_windows_and_old_token(self):
        r,f,p,raw=self.lease();proof.verify_lease(r,f,p,raw)
        for k,bad in (('monotonicSeconds',1),('clocksOrSqlModified',True),('originalLeaseToken','new'),('samples',p['samples'][::5])):
            with self.subTest(k=k),self.assertRaises(AssertionError):proof.verify_lease(r,f,{**p,k:bad},raw)
        with self.assertRaises(AssertionError):proof.verify_lease(r,{**f,'completed_at_utc':p['waitBeganAtUtc']},p,raw)

    def test_prelease_root_and_claim_cannot_enter_any_copy_phase(self):
        r=self.row();r.update(execution_job_id='a'*32,owner_hash='owner');source=dict(operationId='b'*32);p=dict(fingerprint='fp',command=dict(purge=dict(plan=dict(command=dict(source=source)))))
        root=dict(operationId='a'*32,owner='owner',plan=p,state='Reserved',privateIdentity=None,pendingIdentity=None,preparedIdentity=None,publishedIdentity=None,parentAfterStage='')
        claim=dict(schemaVersion=1,source=source,restoreOperationId='a'*32,restoreOwner='owner',restoreFingerprint='fp');cut=dict(plan=p);proof.prelease_originals(r,root,claim,cut);proof.prelease_originals(r,{**root,'state':'Aborted'},None,cut)
        for k,bad in (('state','Applying'),('state','Unknown'),('privateIdentity',[1,2]),('pendingIdentity',[1,3]),('preparedIdentity',[1]*9),('publishedIdentity',[1]*9)):
            with self.subTest(k=k),self.assertRaises(AssertionError):proof.prelease_originals(r,{**root,k:bad},claim,cut)
        with self.assertRaises(AssertionError):proof.prelease_originals(r,root,{**claim,'restoreOperationId':'c'*32},cut)

    def test_terminal_is_interrupted_failed_not_user_canceled_or_unknown(self):
        v,f,p,b=sample();self.assertTrue(terminal_proof(v,f,p,b)['originalExecutionInterrupted'])
        for state,code in (('Canceled','CONTAINER_RECYCLE_ARCHIVE_CANCELED'),('Unknown','CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN')):
            changed=copy.deepcopy(v);changed['row'].update(state=state,result_code=code);changed['executionJob']['state']=state
            with self.subTest(state=state),self.assertRaises(AssertionError):terminal_proof(changed,f,p,b)

    def test_owner_remains_admin_without_early_logout_or_target_cancel(self):
        _,f,_,b=sample();browser_proof(b,f)
        for k,bad in (('originalOwnerStillAdministrator',False),('ownerLogoutPostsBeforeRestartOutcome',1)):
            with self.subTest(k=k),self.assertRaises(AssertionError):browser_proof({**b,k:bad},f)

    def test_missed_cut_and_unsupported_service_never_authorize_fault(self):
        for kind in ('reboot','logout','maintenance-restart'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()
        code=Path(gate.__file__).read_text();self.assertLess(code.index('cut_proof(held.record'),code.index('restart(kind,held,worker,revision,e)'));self.assertIn('worker.start()',code);self.assertIn("restartable_core=('whaledeck-worker',) if kind=='worker-crash' else ()",code)

    def test_independent_reread_precedes_empty_fixture_cleanup_and_has_no_writes(self):
        root=Path(__file__).parent;parent=(root/'validate_container_recycle_archive_restart.py').read_text();self.assertLess(parent.index('independent(repo,e,True)'),parent.index('private.rmdir()'))
        verifier=(root/'verify_container_recycle_archive_restart.py').read_text();calls=[ast.unparse(n.func) for n in ast.walk(ast.parse(verifier)) if isinstance(n,ast.Call)]
        for bad in ('cleanup_new_copy','requests.post','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(bad,calls)
        self.assertIn('verify_restart',calls);self.assertIn('natural_lease',parent)

    def test_independent_restart_verifier_requires_zero_audit_for_both_ids(self):
        from archive_restart_terminal_proof import explicit_cancel_absent
        v,_,_,_=sample();explicit_cancel_absent(v['row'],0)
        for n in (1,True,None,'0'):
            with self.subTest(n=n),self.assertRaises(AssertionError):explicit_cancel_absent(v['row'],n)
        s=(Path(__file__).parent/'verify_container_recycle_archive_restart.py').read_text()
        self.assertIn("job_id IN (:'intent'::uuid,:'job'::uuid)",s);self.assertIn('explicit_cancel_absent(row,count)',s);self.assertNotIn("proof['cancelAuditCount']==1",s)


if __name__=='__main__':unittest.main()
