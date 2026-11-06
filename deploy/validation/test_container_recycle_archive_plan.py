"""Actual temporary purge/HDD I/O; explicit namespace/storage-attestation doubles.

Archive preflight only: no installed RPC, physical-disk, copy or UI acceptance.
"""
import copy
import fcntl
import json
import os
import uuid
import unittest
import hashlib
from unittest.mock import patch

import test_container_recycle_purges as purges
import test_container_file_staging as staging
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-plan.py','archive_preflight_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchivePlanTests(unittest.TestCase):
    def setUp(self):
        self.f=purges.PurgeTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.source=self.f.source;self.session=self.f.session;self.base=self.f.base
        self.q=self.f.request();self.f.engine.reserve(self.q,self.session);self.f.engine.apply(self.q,self.session)
        self.engine=candidate.ContainerRecycleArchivePreflight(self.f.engine)
        self.command=dict(purge=copy.deepcopy(self.q),historyDigest=self.q['plan']['command']['historyDigest'],mode='ArchiveCopy')

    def state(self):return [snapshot(p) for p in (self.base.root,self.base.state,self.f.f.state,self.f.native.state,self.base.target)]
    def plan(self):return self.engine.plan(self.command,self.session)
    def source_record(self):return self.base.state/(self.f.native.ref['operationId']+'.json')
    def change(self,path,**values):
        value=json.loads(path.read_bytes());value.update(values);path.write_text(json.dumps(value));path.chmod(0o600)

    def test_completed_purge_archive_is_readonly_and_distinct_from_inode_restore(self):
        before=self.state();plan=self.plan();self.assertEqual(plan,self.engine.revalidate(plan,self.session));self.assertEqual(self.state(),before)
        self.assertEqual(plan['archiveBytes'],len(self.base.content));self.assertEqual(plan['archiveSha256'],self.q['plan']['archiveSha256']);self.assertEqual(plan['deletedIdentity'],self.q['plan']['retainedIdentity']);self.assertEqual(plan['fingerprint'],self.engine.digest(plan))
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.f.hdd().read_bytes(),self.base.content)
        for name in ('copy','apply','restore','reserve','discard'):self.assertFalse(hasattr(self.engine,name))
        with self.assertRaises(Exception):self.f.native.engine.validate_plan(plan)

    def test_empty_private_directory_may_be_removed_but_new_plan_binds_absence(self):
        old=self.plan();self.f.native.retained().parent.rmdir();fresh=self.plan();self.assertIsNone(fresh['privateIdentity'])
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.revalidate(old,self.session)
        self.assertFalse(self.f.native.retained().parent.exists());self.assertFalse(self.base.leaf.exists())

    def test_unknown_completed_journal_is_not_reconciled_or_accepted(self):
        self.change(self.f.f.journal(self.q),state='Unknown');before=self.state()
        with self.assertRaisesRegex(Exception,'PURGE_UNCONFIRMED'):self.plan()
        self.assertEqual(before,self.state());self.assertEqual(json.loads(self.f.f.marker().read_bytes())['state'],'Purged')

    def test_source_or_marker_lifecycle_disagreement_is_rejected(self):
        for path,values in ((self.source_record(),dict(state='Purging')),(self.f.f.marker(),dict(state='Unknown'))):
            raw=path.read_bytes();self.change(path,**values);before=self.state()
            with self.subTest(path=path.name),self.assertRaises(Exception):self.plan()
            self.assertEqual(before,self.state());path.write_bytes(raw)

    def test_complete_source_history_is_bound_not_just_state(self):
        self.change(self.source_record(),privateIdentity=[self.q['plan']['privateIdentity'][0],self.q['plan']['privateIdentity'][1]+1])
        with self.assertRaisesRegex(Exception,'SOURCE_CHANGED'):self.plan()

    def test_unconfirmed_unlink_cannot_become_archive_authority(self):
        self.change(self.f.f.journal(self.q),unlinkConfirmed=False)
        with self.assertRaises(Exception):self.plan()

    def test_only_explicit_archive_copy_and_exact_command_fields(self):
        for update in (dict(mode='Originals'),dict(mode='All'),dict(mode=None),dict(target='/etc/target')):
            with self.subTest(update=update),self.assertRaises(Exception):self.engine.plan({**self.command,**update},self.session)
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.plan(self.command,None)
        bad=copy.deepcopy(self.command);bad['historyDigest']='F'*64
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.plan(bad,self.session)

    def test_target_file_directory_and_dangling_symlink_all_block(self):
        leaf=self.base.leaf
        for kind in ('file','directory','symlink'):
            if kind=='file':leaf.write_bytes(b'new foreign bytes')
            elif kind=='directory':leaf.mkdir()
            else:leaf.symlink_to('missing-foreign-destination')
            before=self.state()
            with self.subTest(kind=kind),self.assertRaisesRegex(Exception,'TARGET_EXISTS'):self.plan()
            self.assertEqual(before,self.state())
            if kind=='directory':leaf.rmdir()
            else:leaf.unlink()

    def test_retained_or_quarantine_name_reappearance_is_not_deleted(self):
        for leaf in (self.f.native.retained(),self.f.pending(self.q)):
            leaf.write_bytes(b'foreign occupant');before=self.state()
            with self.subTest(name=leaf.name),self.assertRaisesRegex(Exception,'SOURCE_CHANGED'):self.plan()
            self.assertEqual(before,self.state());leaf.unlink()

    def test_hdd_corruption_and_lifecycle_change_reject(self):
        data=self.f.hdd();raw=data.read_bytes();data.write_bytes(b'corrupted archive')
        with self.assertRaises(Exception):self.plan()
        data.write_bytes(raw)
        history=self.base.target/(self.f.native.ref['operationId']+'.json');self.change(history,captureState='Trashed')
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.plan()

    def test_same_archive_bytes_on_new_inode_or_hardlink_reject(self):
        data=self.f.hdd();other=data.with_suffix('.held');data.rename(other);data.write_bytes(other.read_bytes());data.chmod(0o600)
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.plan()
        data.unlink();other.rename(data);os.link(data,other)
        with self.assertRaises(Exception):self.plan()

    def test_absent_unsafe_or_unknown_existing_locks_never_get_created(self):
        for path in (self.f.f.state/(self.q['operationId']+'.lock'),self.base.state/(self.f.native.ref['operationId']+'.lock'),self.base.target/'history.lock'):
            raw=path.read_bytes();path.unlink();before=self.state()
            with self.subTest(path=path.name),self.assertRaises(Exception):self.plan()
            self.assertEqual(before,self.state());self.assertFalse(path.exists());path.write_bytes(raw);path.chmod(0o600)
            path.chmod(0o644)
            with self.assertRaises(Exception):self.plan()
            path.chmod(0o600)

    def test_unknown_purge_id_does_not_create_lock_or_journal(self):
        bad=copy.deepcopy(self.command);bad['purge']['operationId']=uuid.uuid4().hex;before=self.state()
        with self.assertRaises(Exception):self.engine.plan(bad,self.session)
        self.assertEqual(before,self.state())

    def test_restore_claim_registry_must_exist_match_and_be_empty(self):
        path=self.f.native.engine.claims_path;self.source.restore_claims_path=None
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.plan()
        self.source.restore_claims_path=path;path.rmdir()
        with self.assertRaises(Exception):self.plan()
        path.mkdir(mode=0o700);claim=self.f.native.engine.claim(dict(operationId=uuid.uuid4().hex,owner='F'*64,plan=dict(command=self.f.native.command,fingerprint='F'*64)))
        with self.f.native.engine.claims() as directory:self.source.f.save_receipt(directory,self.f.native.ref['operationId'],claim)
        before=self.state()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.plan()
        self.assertEqual(before,self.state())

    def test_revalidation_preserves_numeric_types_and_rejects_extra_fields(self):
        plan=self.plan()
        for key,value in (('archiveBytes',float(plan['archiveBytes'])),('uid',True),('deletedIdentity',[float(n) for n in plan['deletedIdentity']]),('extra',1),('schemaVersion',True)):
            bad=copy.deepcopy(plan);bad[key]=value;bad['fingerprint']=self.engine.digest(bad)
            with self.subTest(key=key),self.assertRaises(Exception):self.engine.revalidate(bad,self.session)

    def test_session_and_archive_attestation_fail_during_read_without_writes(self):
        before=self.state()
        with patch.object(self.session,'revalidate',side_effect=RuntimeError('expired session')),self.assertRaises(RuntimeError):self.plan()
        self.assertEqual(before,self.state())
        with patch.object(self.source.history.storage,'attest',side_effect=RuntimeError('lost attestation')),self.assertRaisesRegex(RuntimeError,'lost attestation'):self.plan()
        self.assertEqual(before,self.state())

    def test_existing_lock_contention_rejects_immediately_without_writes(self):
        for path in (self.f.f.state/(self.q['operationId']+'.lock'),self.base.state/(self.f.native.ref['operationId']+'.lock'),self.base.target/'history.lock'):
            fd=os.open(path,os.O_RDONLY)
            try:
                fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB);before=self.state()
                with self.subTest(path=path.name),self.assertRaisesRegex(Exception,'ARCHIVE_BUSY'):self.plan()
                self.assertEqual(before,self.state())
            finally:os.close(fd)

    def test_session_expiring_during_final_archive_attestation_is_rejected(self):
        original=self.source.history.storage.attest;calls=[]
        def attest(*args,**kwargs):
            original(*args,**kwargs)
            if len(args)>1 and os.fstat(args[1]).st_ino==self.base.target.stat().st_ino:
                calls.append(True)
                if len(calls)==3:self.session.revalidate.side_effect=RuntimeError('expired during final attest')
        before=self.state()
        with patch.object(self.source.history.storage,'attest',side_effect=attest),self.assertRaisesRegex(RuntimeError,'expired during final attest'):self.plan()
        self.assertEqual(len(calls),3)
        self.assertEqual(before,self.state())

    def test_real_unlinked_crash_remains_unknown_and_no_source_is_rebuilt(self):
        other=purges.PurgeTests();other.setUp()
        try:
            q=other.request();other.engine.reserve(q,other.session);other.kill_at(q,'Unlinked')
            engine=candidate.ContainerRecycleArchivePreflight(other.engine);command=dict(purge=q,historyDigest=q['plan']['command']['historyDigest'],mode='ArchiveCopy')
            before=[snapshot(p) for p in (other.base.root,other.base.state,other.f.state,other.base.target)]
            with self.assertRaisesRegex(Exception,'PURGE_UNCONFIRMED'):engine.plan(command,other.session)
            self.assertEqual(before,[snapshot(p) for p in (other.base.root,other.base.state,other.f.state,other.base.target)])
            self.assertFalse(other.base.leaf.exists());self.assertFalse(other.native.retained().exists());self.assertFalse(other.pending(q).exists())
        finally:other.doCleanups()

    def test_replaced_private_directory_and_archive_symlink_are_rejected(self):
        private=self.f.native.retained().parent;held=private.with_name(private.name+'-held');private.rename(held);private.mkdir(mode=0o700)
        with self.assertRaisesRegex(Exception,'PRIVATE_CHANGED'):self.plan()
        private.rmdir();held.rename(private)
        data=self.f.hdd();saved=data.with_suffix('.held');data.rename(saved);data.symlink_to(saved)
        with self.assertRaises(Exception):self.plan()

    def test_changed_storage_configuration_invalidates_pinned_archive(self):
        configuration=self.base.disk.configuration;raw=json.loads(configuration.read_bytes());raw['maxVersions']-=1;configuration.write_text(json.dumps(raw))
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.plan()

    def test_readonly_attestation_is_used_and_final_binding_is_rechecked(self):
        attest=self.source.history.storage.attest;calls=[];before=self.state()
        def observe(*args,**kwargs):calls.append(kwargs.get('read_only'));return attest(*args,**kwargs)
        with patch.object(self.source.history.storage,'attest',side_effect=observe):self.plan()
        self.assertTrue(calls);self.assertEqual(set(calls),{True})
        self.session.revalidate.reset_mock();guard=self.source.guard
        def expire(*args,**kwargs):
            result=guard(*args,**kwargs)
            if self.session.revalidate.call_count>4:raise RuntimeError('late session invalid')
            return result
        with patch.object(self.source,'guard',side_effect=expire),self.assertRaisesRegex(RuntimeError,'late session invalid'):self.plan()
        self.assertEqual(before,self.state())

    def test_actual_hdd_binary_snapshot_is_not_a_json_journal_snapshot(self):
        from validate_container_recycle_archive_plan import payload_snapshot
        before=self.state();saved=payload_snapshot(self.base.target);name=self.f.hdd().name
        self.assertEqual(saved[name]['sha256'],hashlib.sha256(self.base.content).hexdigest().upper());self.assertEqual(saved[name]['sizeBytes'],len(self.base.content));self.assertEqual(before,self.state())

    def test_hdd_payload_snapshot_rejects_symlinks_and_directories(self):
        from validate_container_recycle_archive_plan import payload_snapshot
        leaf=self.base.target/'foreign-link';leaf.symlink_to(self.f.hdd())
        with self.assertRaises(AssertionError):payload_snapshot(self.base.target)
        leaf.unlink();leaf.mkdir()
        with self.assertRaises(AssertionError):payload_snapshot(self.base.target)


if __name__=='__main__':unittest.main()
