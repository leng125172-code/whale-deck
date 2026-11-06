"""Post-return revocation requires authority loss and the same completed restore."""
import copy,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_restore_postreturn_revocation as gate
import validate_container_recycle_restore_postreturn_worker as completed
import test_container_recycle_revocation as prior
from recycle_restore_browser_proof import SUMMARY


class PostreturnRevocationProofTests(unittest.TestCase):
    def browser(self,kind):
        b=prior.RecycleRevocationTests().browser(kind)
        value=dict(id='intent',executionJobId='execution',sourceIntentId='source',sourceOperationId='original',state='Succeeded',confirmation=None,
            receipt=dict(id='execution',sourceOperationId='original',state='Completed',sourceState='Restored',sourceRetained=False,sourceRestored=True,restoredByThisOperation=True,bindingCurrent=True,historyVerified=True,historyAvailable=True))
        safe=dict.fromkeys(SUMMARY,None);safe.update(schemaVersion=1,intentId='intent',jobId='execution',sourceIntentId='source',sourceOperationId='original',stage='execution',state='Succeeded',code='CONTAINER_RECYCLE_RESTORE_COMPLETED',receiptState='Completed',sourceState='Restored',bindingCurrent=True,sourceRetained=False,sourceRestored=True,restoredByThisOperation=True,historyAvailable=True,historyVerified=True,publishBarrier=False)
        return {**b,'result':value,'job':dict(id='execution',state='Succeeded',resultJson=json.dumps(safe))}

    def test_revoke_kinds_rejected_before_runtime_access(self):
        for kind in ('logout','worker-crash','delete-user'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()

    def test_each_authority_loss_requires_complete_same_operation_in_api_receipt_and_job(self):
        for kind in ('group-removal','account-disable'):
            b=self.browser(kind);f=dict(kind=kind,subject=prior.SUBJECT);gate.browser_proof(b,f)
            for field,value in [('state','Canceled'),('executionJobId','foreign')]:
                bad=copy.deepcopy(b);bad['result'][field]=value
                with self.subTest(kind=kind,field=field),self.assertRaises(AssertionError):gate.browser_proof(bad,f)
            for field,value in [('state','Aborted'),('sourceState','Trashed'),('sourceRetained',True),('restoredByThisOperation',False),('historyVerified',False),('bindingCurrent',None),('id','foreign')]:
                bad=copy.deepcopy(b);bad['result']['receipt'][field]=value
                with self.subTest(kind=kind,receipt=field),self.assertRaises(AssertionError):gate.browser_proof(bad,f)
            for field,value in [('state','Canceled'),('sourceOperationId','foreign'),('restoredByThisOperation',None),('historyAvailable',False),('publishBarrier',True)]:
                bad=copy.deepcopy(b);safe=json.loads(bad['job']['resultJson']);safe[field]=value;bad['job']['resultJson']=json.dumps(safe)
                with self.subTest(kind=kind,summary=field),self.assertRaises(AssertionError):gate.browser_proof(bad,f)

    def test_cancel_logout_or_same_observer_cannot_substitute_for_live_revocation(self):
        for kind in ('group-removal','account-disable'):
            b=self.browser(kind);f=dict(kind=kind,subject=prior.SUBJECT)
            for key,value in [('cancellations',1),('ownerLogoutPosts',1),('managementWrites',1),('recoverySubject',prior.SUBJECT),('privatePanelsAbsent',False),('hintsCleared',False)]:
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:value},f)

    def test_expired_authority_or_unrelated_user_change_is_rejected(self):
        for kind in ('group-removal','account-disable'):
            a=prior.RecycleRevocationTests().authority(kind);f=dict(kind=kind,subject=prior.SUBJECT);gate.authority_proof(a,f)
            a['executionExpiresAtUtc']=a['confirmedAtUtc']
            with self.subTest(kind=kind),self.assertRaises(AssertionError):gate.authority_proof(a,f)

    def test_completed_result_with_cancel_or_pre_return_failure_is_rejected(self):
        for row in [dict(state='Succeeded',cancel_requested_at_utc='explicit',result_code='CONTAINER_RECYCLE_RESTORE_COMPLETED'),dict(state='Canceled',cancel_requested_at_utc=None,result_code='CONTAINER_RECYCLE_RESTORE_ABORTED')]:
            with self.subTest(row=row),patch.object(completed,'prove_result',return_value=dict(sql=dict(row=row))),self.assertRaises(AssertionError):gate.prove({}, {}, {}, Path('/unused'))

    def test_cancel_audit_or_missing_returned_hdd_proof_cannot_pass(self):
        row=dict(state='Succeeded',cancel_requested_at_utc=None,result_code='CONTAINER_RECYCLE_RESTORE_COMPLETED',execution_job_id='execution')
        with tempfile.TemporaryDirectory() as d:
            e=Path(d);pending=dict(verification=dict(hddDataReread=False),root=dict(state='Applying'));(e/'cut-source-proof.json').write_text(json.dumps(pending))
            for audits in (1,0):
                with self.subTest(cancelAudits=audits),patch.object(completed,'prove_result',return_value=dict(sql=dict(row=row))),patch.object(completed,'query',return_value=dict(n=audits)),self.assertRaises(AssertionError):gate.prove({}, {}, {}, e)

    def test_original_cleanup_follows_independent_proof_and_no_pre_return_cut_is_used(self):
        s=Path(gate.__file__).read_text();browser=Path(gate.__file__).with_suffix('.mjs').read_text()
        self.assertIn('returned.freeze_after_return',s);self.assertNotIn('freeze_before_return(',s)
        self.assertLess(s.index('read_pending(request'),s.index('identity.change(kind)'))
        self.assertLess(s.index('independent(repo,e,True)'),s.index('leaf.unlink()'));self.assertNotIn('retained.unlink()',s)
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', "name: '确认请求取消'"):self.assertNotIn(forbidden,browser)

    def test_cleanup_accepts_rename_ctime_only_with_exact_independent_returned_snapshot(self):
        original=dict(identity=[2049,77,0o100640,1000,1000,1,7,10,11],sha256='A'*64,attributes=[['user.test','value']],sizeBytes=7)
        actual=copy.deepcopy(original);actual['identity'][8]=19
        gate.cleanup_returned_proof(actual,original,dict(physical=dict(source=copy.deepcopy(actual),retained=None)))
        with self.assertRaises(AssertionError):gate.cleanup_returned_proof(actual,original,dict(physical=dict(source=original,retained=None)))

    def test_cleanup_rejects_new_inode_changed_bytes_stale_verifier_or_retained_copy(self):
        original=dict(identity=[2049,77,0o100640,1000,1000,1,7,10,11],sha256='A'*64,attributes=[['user.test','value']],sizeBytes=7)
        actual=copy.deepcopy(original);actual['identity'][8]=19
        for key,value in [('sha256','B'*64),('attributes',[]),('sizeBytes',8)]:
            bad=copy.deepcopy(actual);bad[key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.cleanup_returned_proof(bad,original,dict(physical=dict(source=copy.deepcopy(bad),retained=None)))
        for offset in range(8):
            bad=copy.deepcopy(actual);bad['identity'][offset]+=1
            with self.subTest(identity=offset),self.assertRaises(AssertionError):gate.cleanup_returned_proof(bad,original,dict(physical=dict(source=copy.deepcopy(bad),retained=None)))
        with self.assertRaises(AssertionError):gate.cleanup_returned_proof(actual,original,dict(physical=dict(source=actual,retained=actual)))
        stale=copy.deepcopy(actual);stale['identity'][8]+=1
        with self.assertRaises(AssertionError):gate.cleanup_returned_proof(stale,original,dict(physical=dict(source=actual,retained=None)))


if __name__=='__main__':unittest.main()
