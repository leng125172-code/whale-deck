"""Actual temporary trash/HDD-format preflight; namespace/attestation doubles.

No production deletion authority or installed/RPC/business acceptance.
"""
import copy,hashlib,json,os,unittest
from pathlib import Path
import test_container_file_recycle_restores as native
import test_container_file_staging as staging
from recursive_acl_restore_agent_io import frame

def snapshot(root):
    return {str(p.relative_to(root)):dict(identity=frame(p.stat()),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in root.rglob('*') if p.is_file()}

candidate=staging.load('whaledeck-container-recycle-purge-plan.py','container_purge_preflight_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class PurgePlanTests(unittest.TestCase):
    def setUp(self):
        self.f=native.LinuxContainerRecycleRestoreTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.p=candidate.ContainerRecyclePurgePreflight(self.f.engine);self.command=dict(source=copy.deepcopy(self.f.ref),historyDigest=self.f.command['historyDigest'],scope='Originals')

    def test_valid_plan_binds_both_copies_without_journal_claim_or_data_writes(self):
        before=[snapshot(p) for p in (self.f.base.state,self.f.state,self.f.base.target)];data=self.f.retained().read_bytes();identity=self.f.source.metadata(self.f.retained().stat())
        plan=self.p.plan(self.command,self.f.session);self.assertEqual(plan['fingerprint'],self.p.digest(plan));self.assertEqual(plan['retainedIdentity'],identity);self.assertEqual(plan['archiveBytes'],len(data));self.assertEqual(plan['scope'] if 'scope' in plan else plan['command']['scope'],'Originals')
        self.assertEqual(plan,self.p.revalidate(plan,self.f.session));self.assertEqual(before,[snapshot(p) for p in (self.f.base.state,self.f.state,self.f.base.target)]);self.assertEqual(self.f.retained().read_bytes(),data);self.assertFalse(self.f.base.leaf.exists())
        for method in ('apply','discard','delete','purge'):self.assertFalse(hasattr(self.p,method))

    def test_restore_claim_or_unknown_source_is_never_a_purge_candidate(self):
        q=self.f.request();self.f.kill_at(q,'SourceClaimed')
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)

    def test_completed_restore_and_its_matching_claim_are_not_eligible(self):
        q=self.f.request();self.assertEqual(self.f.engine.apply(q,self.f.session)['state'],'Completed')
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)
        self.assertEqual(self.f.base.leaf.read_bytes(),self.f.base.content)

    def test_no_implicit_archive_or_all_scope_and_no_caller_private_path(self):
        for change in (dict(scope='All'),dict(scope='Archive'),dict(scope=None),dict(privatePath='/etc')):
            with self.subTest(change=change),self.assertRaises(Exception):self.p.plan({**self.command,**change},self.f.session)
        with self.assertRaises(Exception):self.p.plan(self.command,None)

    def test_corrupt_hdd_cannot_authorize_destroying_last_verified_original(self):
        data=self.f.base.target/(self.f.ref['operationId']+'.data');data.write_bytes(b'corrupt');data.chmod(0o600)
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)
        self.assertEqual(self.f.retained().read_bytes(),self.f.base.content)

    def test_changed_retained_inode_or_attributes_is_not_the_selected_original(self):
        path=self.f.retained();os.setxattr(path,'user.changed',b'unexpected')
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)

    def test_plan_fingerprint_domain_cannot_be_used_as_restore_plan(self):
        plan=self.p.plan(self.command,self.f.session);request=dict(operationId='c'*32,owner='C'*64,plan=plan)
        with self.assertRaises(Exception):self.f.engine.request(request)

    def test_revalidation_rejects_changed_namespace_metadata_and_lossy_identity(self):
        plan=self.p.plan(self.command,self.f.session)
        for key,value in [('retainedIdentity',[*plan['retainedIdentity'][:1],float(plan['retainedIdentity'][1]),*plan['retainedIdentity'][2:]]),('archiveBytes',True),('extra',1)]:
            bad=copy.deepcopy(plan);bad[key]=value;bad['fingerprint']=self.p.digest(bad)
            with self.subTest(key=key),self.assertRaises(Exception):self.p.revalidate(bad,self.f.session)

    def test_unknown_or_history_state_mismatch_is_not_eligible(self):
        path=self.f.base.state/(self.f.ref['operationId']+'.json');record=json.loads(path.read_text());record.update(state='Unknown',actualIdentity=None);path.write_text(json.dumps(record));path.chmod(0o600)
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)

    def test_unconfigured_or_missing_restore_claim_registry_fails_closed(self):
        self.f.source.restore_claims_path=None
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)
        self.f.source.restore_claims_path=self.f.engine.claims_path;self.f.engine.claims_path.rmdir()
        with self.assertRaises(Exception):self.p.plan(self.command,self.f.session)

    def test_verified_hdd_lifecycle_must_match_the_selected_trashed_source(self):
        self.f.source.history.mark(self.f.ref,self.command['historyDigest'],'Restored')
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.p.plan(self.command,self.f.session)

    def test_plan_then_restore_cannot_be_revalidated_for_cleanup(self):
        plan=self.p.plan(self.command,self.f.session);self.f.source.restore(self.f.ref,self.f.session)
        with self.assertRaises(Exception):self.p.revalidate(plan,self.f.session)
        self.assertEqual(self.f.base.leaf.read_bytes(),self.f.base.content)

    def test_plan_then_hdd_lifecycle_change_is_rejected(self):
        plan=self.p.plan(self.command,self.f.session);self.f.source.history.mark(self.f.ref,self.command['historyDigest'],'Restored')
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.p.revalidate(plan,self.f.session)

    def test_same_hdd_bytes_on_a_new_inode_are_not_the_selected_archive(self):
        plan=self.p.plan(self.command,self.f.session);data=self.f.base.target/(self.f.ref['operationId']+'.data');saved=data.with_suffix('.held-data');data.rename(saved);data.write_bytes(saved.read_bytes());data.chmod(0o600)
        self.assertNotEqual(data.stat().st_ino,saved.stat().st_ino)
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.p.revalidate(plan,self.f.session)

    def test_claim_created_after_plan_invalidates_readonly_observation(self):
        plan=self.p.plan(self.command,self.f.session);request=self.f.request();self.f.engine.write_claim(request)
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.p.revalidate(plan,self.f.session)


if __name__=='__main__':unittest.main()
