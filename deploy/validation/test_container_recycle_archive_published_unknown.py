"""Synthetic business joins and actual temporary published target cleanup; not UI."""
import ast,copy,json,os,unittest,uuid
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from archive_published_unknown_proof import row_proof,published_proof,require_ack
from test_container_recycle_archive_inflight_logout import sample as old_sample
import test_container_recycle_archive_published_cut as native
from test_container_recycle_archive_browser_proof import managed,public
from test_container_recycle_purge_plan import snapshot
import cleanup_owned_archive_published_target as cleanup
import container_recycle_archive_agent_io as io
from recycle_archive_browser_proof import execution


def sample(ack=False):
    v,f,p,_=old_sample();r=v['row'];r.update(state='Acknowledged' if ack else 'Unknown',result_code='CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN',root_state='Unknown',archive_copied=None,source_fenced=True,owned_fence=True,binding_current=False,publish_barrier=not ack,completed_at_utc='now' if ack else None,cancel_requested_at_utc='requested',acknowledged_by='admin' if ack else None,acknowledgement_json='receipt' if ack else None)
    v.update(pending=0 if ack else 2);v['executionJob'].update(state='Failed' if ack else 'Running',phase='Acknowledged' if ack else 'AwaitingContainerRecycleArchiveReceipt',completed_at_utc='now' if ack else None,cancel_requested_at_utc='requested');v['executionJob']['result_json'].update(state=r['state'],code=r['result_code'],receiptState='Unknown',archiveCopied=None,sourceFenced=True,ownedFence=True,bindingCurrent=False,publishBarrier=not ack)
    return v,f,p


class UnknownJoinTests(unittest.TestCase):
    def test_unknown_and_ack_keep_null_fact_fence_and_nineteen_field_jobs(self):
        for ack in (False,True):
            v,f,p=sample(ack);row_proof(v,f,p,ack)

    def test_false_success_or_barrier_release_is_not_unknown(self):
        for key,bad in (('archive_copied',False),('archive_copied',True),('root_state','Completed'),('owned_fence',False),('publish_barrier',False),('completed_at_utc','now'),('lease_token','lease'),('acknowledged_by','admin')):
            v,f,p=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):row_proof(v,f,p,False)

    def test_ack_does_not_create_a_success_or_change_original_operation(self):
        a,f,p=sample();b,_,_=sample(True);b['row']['version']=a['row'].get('version',0)+1;a['row']['version']=0
        from container_recycle_archive_restart_proof import IMMUTABLE
        for key in IMMUTABLE:
            if key not in a['row']:a['row'][key]=b['row'][key]=key
        before=dict(sql=a,receipt={'state':'Unknown'},records={'root':'kept'},physical={'stage':'kept'},hdd={'data':'kept'});after={**before,'sql':b};require_ack(before,after,'admin')
        changed=copy.deepcopy(after);changed['records']['root']='rewritten'
        with self.assertRaises(AssertionError):require_ack(before,changed,'admin')
        with self.assertRaises(AssertionError):require_ack(before,after,f['subject'])

    def test_two_outbox_messages_and_original_job_phase_are_required(self):
        v,f,p=sample();v['pending']=0
        with self.assertRaises(AssertionError):row_proof(v,f,p,False)
        v,f,p=sample();v['executionJob']['phase']='Succeeded'
        with self.assertRaises(AssertionError):row_proof(v,f,p,False)

    def test_browser_is_visible_and_only_one_explicit_ack_follows_decline(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_published_unknown.mjs').read_text()
        for x in ('page.route(','storageState(','tracing.start(','addCookies('):self.assertNotIn(x,s)
        self.assertLess(s.index("coordinate('decision'"),s.index("name:'确认处置并释放保护'"));self.assertIn('credentials.recovery',s);self.assertIn('managementWrites,1',s)
        parent=(root/'validate_container_recycle_archive_published_unknown.py').read_text();self.assertIn('freeze_published_namespace(',parent);self.assertIn('credentials\':dict(owner=',parent);self.assertNotIn("'VALIDATION_PASSWORD':password",parent)
        self.assertLess(parent.index('independent(repo,e,True)'),parent.index('cleanup_owned_target(f['))
        verifier=(root/'verify_container_recycle_archive_published_unknown.py').read_text();calls=[ast.unparse(n.func) for n in ast.walk(ast.parse(verifier)) if isinstance(n,ast.Call)]
        for x in ('cleanup_owned_target','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(x,calls)


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary root disk IO required')
class ExactPublishedCleanupTests(unittest.TestCase):
    def setUp(self):
        self.f=native.PublishedDiskTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.cut=self.f.publish();self.q=self.f.q;self.folder=self.f.folder;self.root=json.loads(self.f.path.read_bytes());self.value=io.physical(self.folder,self.q);self.version=cleanup.parent_version(self.root,self.folder)
        self.row=dict(state='Acknowledged',root_state='Unknown',archive_copied=None,completed_at_utc='now',acknowledged_by='admin',actor_subject='owner',publish_barrier=False,lease_token=None,lease_expires_at_utc=None,execution_job_id=str(uuid.UUID(self.q['operationId'])),owner_hash=self.q['owner'],plan_json=json.dumps(managed(self.q['plan'])))
        self.row['receipt_json']=json.dumps({k[0].upper()+k[1:]:v for k,v in public(self.q,'Unknown').items()});self.row['acknowledgement_json']=self.row['receipt_json']
        for p in (patch.object(cleanup,'owned_fixture'),patch.object(cleanup.io,'STATE',self.f.state),patch.object(cleanup.source_io,'installed',return_value=SimpleNamespace(storage=self.f.f.f.base.disk.engine,files=self.f.f.f.base.f))):p.start();self.addCleanup(p.stop)

    def test_exact_owned_target_removed_after_ack_keeps_all_root_claim_hdd_bytes(self):
        published_proof(self.value,self.q,self.root,self.cut);base=self.f.f.f.base;before=[snapshot(p) for p in (base.state,self.f.f.f.state,self.f.state,self.f.engine.restore.claims_path,base.target)]
        result=cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row);self.assertTrue(result['onlyOwnedPublishedTargetAndEmptyStageRemoved']);self.assertFalse((self.folder/(io.STAGE_PREFIX+self.q['operationId'])).exists());self.assertFalse((self.folder/'recycle-rpc.bin').exists())
        self.assertEqual(before,[snapshot(p) for p in (base.state,self.f.f.f.state,self.f.state,self.f.engine.restore.claims_path,base.target)])

    def test_no_ack_or_wrong_record_identity_refuses_before_unlink(self):
        for key,bad in (('state','Unknown'),('publish_barrier',True),('archive_copied',True),('acknowledged_by','owner'),('lease_token','lease')):
            with self.subTest(key=key),patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,{**self.row,key:bad})
            remove.assert_not_called()
        changed=copy.deepcopy(self.root);changed['preparedIdentity'][1]+=1
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,changed,self.cut,self.value,self.version,self.row)
        remove.assert_not_called()

    def test_foreign_target_is_preserved(self):
        target=self.folder/'recycle-rpc.bin';target.unlink();target.write_bytes(b'foreign')
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called();self.assertEqual(target.read_bytes(),b'foreign')

    def test_unknown_receipt_cannot_hide_new_physical_state(self):
        for key,bad in (('source',None),('stageIdentity',[1,2]),('staged',{'foreign':True})):
            with self.subTest(key=key),self.assertRaises(AssertionError):published_proof({**self.value,key:bad},self.q,self.root,self.cut)

    def test_hardlinked_target_never_authorizes_deletion(self):
        stage=self.folder/(io.STAGE_PREFIX+self.q['operationId']);file=self.folder/'recycle-rpc.bin';alias=self.folder/'alias';os.link(file,alias)
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called()

    def test_changed_hdd_is_rejected_before_any_target_unlink(self):
        data=self.f.f.f.base.target/(self.f.f.f.ref['operationId']+'.data')
        with data.open('r+b') as out:out.write(b'corrupt')
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called();self.assertTrue((self.folder/'recycle-rpc.bin').exists())

    def test_mismatched_ack_receipt_is_rejected_before_unlink(self):
        wrong={**self.row,'acknowledgement_json':'{}'}
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,wrong)
        remove.assert_not_called()

    def test_busy_original_operation_lock_is_not_bypassed(self):
        import fcntl
        fd=os.open(self.f.state/(self.q['operationId']+'.lock'),os.O_RDWR)
        try:
            fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
            with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(BlockingIOError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
            remove.assert_not_called()
        finally:os.close(fd)

    def test_historical_parent_version_cannot_authorize_post_publication_cleanup(self):
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.root['parentAfterStage'],self.row)
        remove.assert_not_called()

    def test_extra_stage_entry_or_changed_parent_never_authorizes_removal(self):
        stage=self.folder/(io.STAGE_PREFIX+self.q['operationId']);extra=stage/'foreign';extra.write_bytes(b'preserve')
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called();self.assertTrue(extra.exists())

    def test_changed_target_payload_or_attributes_are_refused_even_with_current_snapshot(self):
        target=self.folder/'recycle-rpc.bin';os.setxattr(target,'user.foreign',b'changed');changed=io.physical(self.folder,self.q)
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,changed,self.version,self.row)
        remove.assert_not_called()

    def test_changed_claim_or_root_is_refused_before_target_removal(self):
        claim=self.f.engine.restore.claims_path/(self.f.f.f.ref['operationId']+'.json');raw=claim.read_bytes();v=json.loads(raw);claim.write_text(json.dumps({**v,'restoreOwner':'F'*64}))
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called();claim.write_bytes(raw)
        raw=self.f.path.read_bytes();v=json.loads(raw);self.f.path.write_text(json.dumps({**v,'state':'Completed'}))
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises(AssertionError):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called()

    def test_wrong_stage_inode_or_symlink_is_refused(self):
        stage=self.folder/(io.STAGE_PREFIX+self.q['operationId']);stage.rename(self.folder/'foreign-stage');stage.symlink_to(self.folder/'foreign-stage',target_is_directory=True)
        with patch.object(cleanup.os,'unlink') as remove,self.assertRaises((AssertionError,OSError)):cleanup.cleanup_owned_target(self.folder,self.q,self.root,self.cut,self.value,self.version,self.row)
        remove.assert_not_called()



class OwnedScopeTest(unittest.TestCase):
    def test_foreign_folder_blocks_before_file_mutation(self):
        with patch.object(cleanup.os,'open') as opened,patch.object(cleanup.os,'unlink') as removed,self.assertRaises(AssertionError):cleanup.owned_fixture(Path('/tmp/foreign'))
        opened.assert_not_called();removed.assert_not_called()


if __name__=='__main__':unittest.main()
