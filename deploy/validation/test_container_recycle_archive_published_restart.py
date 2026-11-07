"""Constructed restart/Unknown/ACK witnesses; no product service controls."""
import copy,hashlib,json,unittest
from pathlib import Path
from datetime import datetime,timedelta,timezone
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_archive_published_restart_proof as control
import archive_published_restart_unknown_proof as proof
import validate_container_recycle_archive_published_restart as gate
from test_container_recycle_archive_prepared_unknown import sample as cancel_sample
import test_container_recycle_archive_restart as earlier


def sample(ack=False):
    v,f,p=cancel_sample(ack);v['row']['cancel_requested_at_utc']=None;v['executionJob']['cancel_requested_at_utc']=None;v['pending']=0 if ack else 1;return v,f,p


class PublishedRestartTests(unittest.TestCase):
    def test_unknown_and_ack_are_null_published_facts_without_cancel(self):
        for ack in (False,True):
            v,f,p=sample(ack);proof.row_proof(v,f,p,ack)
        for target in ('row','executionJob'):
            v,f,p=sample();v[target]['cancel_requested_at_utc']='explicit'
            with self.subTest(target=target),self.assertRaises(AssertionError):proof.row_proof(v,f,p,False)

    def test_cancelled_outbox_or_aborted_fact_cannot_be_counted_as_restart(self):
        for key,bad in (('root_state','Aborted'),('archive_copied',False),('owned_fence',False),('publish_barrier',False)):
            v,f,p=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.row_proof(v,f,p,False)
        v,f,p=sample();v['pending']=2
        with self.assertRaises(AssertionError):proof.row_proof(v,f,p,False)

    def test_all_fifteen_guards_block_before_service_commands(self):
        self.assertEqual(len(control.TABLES),15);self.assertIn('container_recycle_archive_intents',control.TABLES)
        for table in control.TABLES:
            counts=dict.fromkeys(control.TABLES,0);counts[table]=1
            with self.subTest(table=table),patch.object(control,'preflight',return_value={}),patch.object(control,'query',return_value=counts),patch.object(control,'command') as cmd,self.assertRaises(AssertionError):control.restart_allowed(SimpleNamespace(),dict(id='intent',execution_job_id='execution'))
            cmd.assert_not_called()

    def test_prelease_keeps_publishing_or_unknown_published_target_and_own_claim(self):
        r=dict(execution_job_id='a'*32,owner_hash='owner');source={'operationId':'b'*32};p=dict(fingerprint='fp',command=dict(purge=dict(plan=dict(command=dict(source=source)))))
        cut=dict(operationId='a'*32,owner='owner',plan=p,state='Publishing',privateIdentity=[1,2],pendingIdentity=[1,3],preparedIdentity=[1,3,4,5,6,1,7,8,9],publishedIdentity=None,parentAfterStage='version');claim=dict(schemaVersion=1,source=source,restoreOperationId='a'*32,restoreOwner='owner',restoreFingerprint='fp')
        control.prelease_originals(r,cut,claim,cut);control.prelease_originals(r,{**cut,'state':'Unknown'},claim,cut)
        for key,bad in (('state','Prepared'),('state','Published'),('state','Reserved'),('state','Aborted'),('state','Completed'),('pendingIdentity',[1,4]),('preparedIdentity',[1]*9),('publishedIdentity',[1]*9)):
            with self.subTest(key=key),self.assertRaises(AssertionError):control.prelease_originals(r,{**cut,key:bad},claim,cut)
        with self.assertRaises(AssertionError):control.prelease_originals(r,cut,None,cut)

    def lease(self):
        start=datetime(2026,11,7,10,tzinfo=timezone.utc);expiry=start+timedelta(seconds=60);raw=json.dumps(dict(state='Publishing',parentAfterStage='B'*64)).encode();claim=b'claim';hdd='A'*64
        row=dict(lease_token='old',lease_expires_at_utc=expiry.isoformat(),actor_subject='owner')
        audit=dict(count=1,atUtc=(expiry+timedelta(seconds=1)).isoformat(),actors=['owner'])
        samples=[dict(atUtc=(start+timedelta(seconds=n)).isoformat(),state='Applying',barrier=True,leaseToken='old',leaseExpiresAtUtc=expiry.isoformat(),rootSha256=hashlib.sha256(raw).hexdigest(),claimSha256=hashlib.sha256(claim).hexdigest(),hddDataReread=True,hddSha256=hdd,publishedTargetFullCopyUnchanged=True,stageEmpty=True,parentVersionAfterPublication='C'*64,rootState='Publishing') for n in range(0,60,2)]
        value=dict(waitBeganAtUtc=start.isoformat(),waitEndedAtUtc=expiry.isoformat(),monotonicSeconds=60,originalLeaseToken='old',originalLeaseExpiresAtUtc=expiry.isoformat(),clocksOrSqlModified=False,samples=samples);return row,audit,value,raw,claim,hdd

    def test_natural_lease_unknown_audit_follows_expiry_dense_namespace_samples(self):
        r,a,v,raw,claim,hdd=self.lease();control.verify_lease(r,a,v,raw,claim,hdd,'C'*64)
        for key,bad in (('monotonicSeconds',1),('clocksOrSqlModified',True),('originalLeaseToken','new'),('samples',v['samples'][::5])):
            with self.subTest(key=key),self.assertRaises(AssertionError):control.verify_lease(r,a,{**v,key:bad},raw,claim,hdd,'C'*64)
        with self.assertRaises(AssertionError):control.verify_lease(r,{**a,'atUtc':v['waitBeganAtUtc']},v,raw,claim,hdd,'C'*64)
        broken=copy.deepcopy(v);broken['samples'][1]['publishedTargetFullCopyUnchanged']=False
        with self.assertRaises(AssertionError):control.verify_lease(r,a,broken,raw,claim,hdd,'C'*64)

    def test_dense_samples_cannot_hide_filled_stage_wrong_parent_or_root_phase(self):
        r,a,v,raw,claim,hdd=self.lease()
        for key,bad in (('stageEmpty',False),('parentVersionAfterPublication','B'*64),('rootState','Prepared'),('rootState','Unknown'),('rootSha256','0'*64),('claimSha256','0'*64),('hddSha256','0'*64)):
            broken=copy.deepcopy(v);broken['samples'][1][key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):control.verify_lease(r,a,broken,raw,claim,hdd,'C'*64)

    def test_prepublication_parent_or_other_phase_cannot_authorize_namespace_lease(self):
        r,a,v,raw,claim,hdd=self.lease()
        with self.assertRaises(AssertionError):control.verify_lease(r,a,v,raw,claim,hdd,'B'*64)
        for state in ('Prepared','Published','Completed','Aborted'):
            with self.subTest(state=state),self.assertRaises(AssertionError):control.verify_lease(r,a,v,json.dumps(dict(state=state,parentAfterStage='B'*64)).encode(),claim,hdd,'C'*64)

    def test_ack_time_never_substitutes_for_original_unknown_audit(self):
        row=dict(actor_subject='owner',acknowledged_by='admin',completed_at_utc='2026-11-07T10:03:00+00:00');unknown=dict(count=1,actors=['owner'],atUtc='2026-11-07T10:01:00+00:00');settled=dict(observedAtUtc='2026-11-07T10:01:01+00:00');ack=dict(count=1,actors=['admin'],atUtc='2026-11-07T10:03:00+00:00');control.audit_order(row,unknown,settled,ack)
        for change in (dict(atUtc='2026-11-07T10:00:00+00:00'),dict(actors=['owner']),dict(count=2)):
            with self.subTest(change=change),self.assertRaises(AssertionError):control.audit_order(row,unknown,settled,{**ack,**change})

    def test_only_fixed_worker_or_agent_change_is_allowed(self):
        helper=earlier.ArchiveRestartTests()
        for kind in control.KINDS:
            a,b,s=helper.baseline(kind);control.preserved_restart(a,b,'a'*32,['b'*32,'c'*32],['d'*32,'e'*32],s,kind)
            bad=copy.deepcopy(b);bad['core']['/other'][3]='changed'
            with self.subTest(kind=kind),self.assertRaises(AssertionError):control.preserved_restart(a,bad,'a'*32,['b'*32,'c'*32],['d'*32,'e'*32],s,kind)

    def test_browser_uses_no_target_cancel_before_unknown_and_only_explicit_ack(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_published_restart.mjs').read_text();phase=s.split("phase='wait-published-service-fault-and-original-result'",1)[1]
        self.assertNotIn("name:'请求取消归档恢复'",phase);self.assertIn('ownerLogoutPostsBeforeOutcome=ownerLogoutPosts',phase);self.assertIn("coordinate('decision'",phase)
        self.assertNotIn('process.env.VALIDATION_PASSWORD=',s)
        for bad in ('page.route(','storageState(','tracing.start('):self.assertNotIn(bad,s)
        parent=(root/'validate_container_recycle_archive_published_restart.py').read_text();self.assertIn('restart(kind,held,worker,revision,e)',parent);self.assertIn('worker.start()',parent);self.assertIn('natural_lease(restart_row,fixture,e)',parent)
        self.assertLess(parent.index('independent(repo,e,True)'),parent.index('cleanup_owned_target(f['))
        verify=(root/'verify_container_recycle_archive_published_restart.py').read_text();self.assertIn('verify_restart(e,f,r',verify);self.assertNotIn("e/'cancel-request-began.json'",verify)

    def test_control_requires_publishing_eight_fds_and_supports_no_other_service(self):
        root=Path(__file__).parent;s=(root/'container_recycle_archive_published_restart_proof.py').read_text();self.assertIn("original['state']=='Publishing'",s);self.assertIn('assert bound_published_process(',s);self.assertIn("'stageFdIdentity','outputFdIdentity'",s)
        for kind in ('reboot','maintenance-restart','logout'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()


if __name__=='__main__':unittest.main()
