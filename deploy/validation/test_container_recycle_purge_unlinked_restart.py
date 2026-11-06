"""Constructed restart/lease proofs; never direct service mutation in tests."""
import copy,hashlib,unittest
from datetime import datetime,timedelta,timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_purge_unlinked_restart_proof as proof
import container_recycle_purge_restart_proof as service_scope
import test_container_recycle_purge_restart as prior
from test_container_recycle_purge_unlinked_logout import data
from recycle_purge_unlinked_logout_proof import row_proof
import validate_container_recycle_purge_unlinked_restart as gate


class UnlinkedRestartTests(unittest.TestCase):
    def originals(self):
        row=dict(execution_job_id='a'*32,owner_hash='owner');p=dict(fingerprint='fp',command=dict(source={'operationId':'b'*32}));root=dict(operationId='a'*32,owner='owner',plan=p,state='Removing',pendingIdentity=[1]*9,unlinkConfirmed=False);marker=dict(operationId='a'*32,owner='owner',fingerprint='fp',source=p['command']['source'],state='Deleting');src=dict(state='Purging',actualIdentity=None)
        return row,root,marker,src,copy.deepcopy(root),copy.deepcopy(src)

    def test_only_post_unlink_removing_or_unknown_and_original_purging_source_pass(self):
        args=self.originals();proof.originals(*args)
        for key,bad in [('state','Reserved'),('state','Aborted'),('pendingIdentity',None),('unlinkConfirmed',True),('owner','other')]:
            changed=list(copy.deepcopy(args));changed[1][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.originals(*changed)
        for state in ('Reserved','Released','Purged'):
            changed=list(copy.deepcopy(args));changed[2]['state']=state
            with self.subTest(marker=state),self.assertRaises(AssertionError):proof.originals(*changed)

    def lease(self):
        start=datetime(2026,11,6,10,tzinfo=timezone.utc);expiry=start+timedelta(seconds=60);row=dict(actor_subject='s',lease_token='old',lease_expires_at_utc=expiry.isoformat());raw,marker,source=b'removing',b'deleting',b'purging'
        samples=[dict(atUtc=(start+timedelta(seconds=n)).isoformat(),state='Applying',barrier=True,leaseToken='old',leaseExpiresAtUtc=expiry.isoformat(),threeNamesAbsent=True,hddDataReread=True,rootSha256=hashlib.sha256(raw).hexdigest(),markerSha256=hashlib.sha256(marker).hexdigest(),sourceSha256=hashlib.sha256(source).hexdigest()) for n in range(0,60,2)]
        value=dict(waitBeganAtUtc=start.isoformat(),waitEndedAtUtc=expiry.isoformat(),monotonicSeconds=60,originalLeaseToken='old',originalLeaseExpiresAtUtc=expiry.isoformat(),samples=samples,clocksOrSqlModified=False);audit=dict(count=1,actors=['s'],atUtc=(expiry+timedelta(seconds=1)).isoformat())
        return row,audit,value,raw,marker,source

    def test_unknown_requires_actual_result_audit_not_a_forged_completed_timestamp(self):
        row,a,v,*raw=self.lease();proof.verify_lease(row,a,v,*raw)
        for key,bad in [('count',0),('count',2),('actors',['other']),('atUtc',v['waitBeganAtUtc'])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.verify_lease(row,{**a,key:bad},v,*raw)

    def test_natural_window_rejects_fake_time_new_token_sparse_or_changed_byte_samples(self):
        row,a,v,*raw=self.lease()
        for key,bad in [('monotonicSeconds',1),('clocksOrSqlModified',True),('originalLeaseToken','new'),('samples',v['samples'][::5])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.verify_lease(row,a,{**v,key:bad},*raw)
        for key,bad in [('rootSha256','changed'),('markerSha256','changed'),('sourceSha256','changed'),('threeNamesAbsent',False),('hddDataReread',False),('barrier',False)]:
            wrong=copy.deepcopy(v);wrong['samples'][0][key]=bad
            with self.subTest(sample=key),self.assertRaises(AssertionError):proof.verify_lease(row,a,wrong,*raw)

    def baseline(self,kind):
        a,b,s=prior.PurgeRestartTests().baseline(kind)
        b['recycleRoot'].update({'a'*32+x:{} for x in ('.json','.lock')});b['recycleHdd'].update({'a'*32+x:{} for x in ('.json','.data')});b['purgeRoot']['c'*32+'.lock']={}
        return a,b,s

    def test_worker_baseline_allows_start_time_but_no_recreation_or_unrelated_changes(self):
        a,b,s=self.baseline('worker-crash');proof.preserved_restart(a,b,'a'*32,'c'*32,s,'worker-crash')
        for key,value in [('core',{'/other':['other','image','running','new']}),('protected',{'original':'changed'}),('recycleRestoreRoot',{'unrelated':{}})]:
            wrong=copy.deepcopy(b);wrong[key].update(value)
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.preserved_restart(a,wrong,'a'*32,'c'*32,s,'worker-crash')

    def test_agent_baseline_cannot_hide_maintenance_restart(self):
        a,b,s=self.baseline('agent-restart');proof.preserved_restart(a,b,'a'*32,'c'*32,s,'agent-restart');b['hosts']['whaledeck-maintenance.service']['pid']=99;s['hostsAfter']=b['hosts']
        with self.assertRaises(AssertionError):proof.preserved_restart(a,b,'a'*32,'c'*32,s,'agent-restart')

    def test_other_file_work_blocks_fixed_service_control_before_any_command(self):
        for table in proof.TABLES:
            counts=dict.fromkeys(proof.TABLES,0);counts[table]=1
            with self.subTest(table=table),patch.object(service_scope,'preflight',return_value={}),patch.object(service_scope,'query',return_value=counts),patch.object(service_scope,'command') as cmd,self.assertRaisesRegex(AssertionError,'OTHER_FILE_WORK'):
                proof.restart_allowed(SimpleNamespace(),dict(id='intent',execution_job_id='execution'))
            cmd.assert_not_called()

    def test_actual_owner_authority_and_no_cancel_are_distinct_from_unknown_lifecycle(self):
        r=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True);f=dict(kind='worker-crash',subject='s',recoverySubject='other');b=dict(actualUi=True,kind='worker-crash',subject='s',recoverySubject='other',registrations=1,confirmations=1,acknowledgements=1,managementWrites=1,cancellations=0,ownerLogoutPostsBeforeOutcome=0,originalOwnerStillAdministrator=True,originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,jobProofs=[{},{}],logoutReceipts=[r,r]);gate.browser_proof(b,f)
        for key,bad in [('originalOwnerStillAdministrator',False),('ownerLogoutPostsBeforeOutcome',1),('cancellations',1)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:bad},f)
        v,fixture=data();row_proof(v,fixture,False);v['row']['state']='Failed'
        with self.assertRaises(AssertionError):row_proof(v,fixture,False)

    def test_source_entries_exist_and_verifier_never_controls_services_or_deletes(self):
        root=Path(gate.__file__).parent
        for n in gate.SOURCES:self.assertTrue((root/n).exists(),n)
        s=(root/'validate_container_recycle_purge_unlinked_restart.mjs').read_text();self.assertNotIn("name:'请求取消留存清理'",s);self.assertIn("assert.equal(owner.isAdministrator,true)",s)
        v=(root/'verify_container_recycle_purge_unlinked_restart.py').read_text()
        for bad in ('.unlink(','.rmdir(','.Popen(','.kill('):self.assertNotIn(bad,v)


if __name__=='__main__':unittest.main()
