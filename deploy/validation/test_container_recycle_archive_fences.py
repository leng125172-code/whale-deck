"""Actual temporary journals/claim/fsync/owned SIGKILL, no archive copy.

Namespace/storage-attestation doubles; not installed RPC or business evidence.
"""
import copy
import json
import os
import select
import signal
import unittest
import uuid
from unittest.mock import patch
import test_container_recycle_archive_plan as plans
import test_container_file_recycling as recycling
import test_container_file_staging as staging
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-fences.py','archive_fences_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveFenceTests(unittest.TestCase):
    def setUp(self):
        self.f=plans.ArchivePlanTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.p=self.f.engine;self.session=self.f.session;self.base=self.f.base
        self.state=self.base.disk.disk/'archive-state';self.state.mkdir(mode=0o700)
        self.engine=candidate.archive_fences_type(recycling.directories.ContainerDirectories)(self.p,self.state)
        self.q=dict(operationId=uuid.uuid4().hex,owner='A'*64,plan=self.f.plan())

    def protected(self):return [snapshot(p) for p in (self.base.root,self.base.state,self.f.f.f.state,self.base.target)]
    def journal(self,q=None):return self.state/((q or self.q)['operationId']+'.json')
    def claim(self):return self.f.f.native.engine.claims_path/(self.f.f.native.ref['operationId']+'.json')
    def record(self):return json.loads(self.journal().read_bytes())
    def kill_at(self,phase,discard=False):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                (self.engine.discard if discard else self.engine.reserve)(self.q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_reserve_keeps_purged_source_payload_and_hdd_lifecycle_unchanged(self):
        before=self.protected();r=self.engine.reserve(self.q,self.session)
        self.assertEqual(r['state'],'Reserved');self.assertTrue(r['ownedFence']);self.assertTrue(r['sourceFenced']);self.assertFalse(r['archiveCopied']);self.assertEqual(before,self.protected())
        self.assertEqual(json.loads(self.claim().read_bytes()),self.engine.claim(self.q));self.assertEqual(self.record()['state'],'Reserved')
        with self.assertRaisesRegex(Exception,'RESERVATION_ONLY'):self.engine.apply(self.q,self.session)
        self.assertEqual(before,self.protected())
        for name in ('copy','publish'):self.assertFalse(hasattr(self.engine,name))

    def test_reserved_source_blocks_new_archive_plans_and_competitors(self):
        self.engine.reserve(self.q,self.session);raw=self.claim().read_bytes();other={**self.q,'operationId':uuid.uuid4().hex}
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.p.plan(self.f.command,self.session)
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.assertEqual(raw,self.claim().read_bytes());self.assertFalse(self.journal(other).exists())

    def test_old_retained_inode_restore_and_purge_cannot_reuse_archive_claim(self):
        self.engine.reserve(self.q,self.session);before=self.protected();raw=self.claim().read_bytes();native=self.f.f.native
        for action in (lambda:native.engine.plan(native.command,self.session),lambda:native.source.restore(native.ref,self.session),lambda:self.f.f.f.p.plan(self.f.f.f.command,self.session)):
            with self.assertRaisesRegex(Exception,'PURGE_CLAIMED|RESTORE_UNPROVEN'):action()
        self.assertEqual(before,self.protected());self.assertEqual(raw,self.claim().read_bytes())

    def test_explicit_cancel_persists_abort_before_release_then_new_id_can_reserve(self):
        self.engine.reserve(self.q,self.session);before=self.protected();r=self.engine.discard(self.q,self.session)
        self.assertEqual(r['state'],'Aborted');self.assertFalse(r['sourceFenced']);self.assertFalse(self.claim().exists());self.assertEqual(before,self.protected())
        other={**self.q,'operationId':uuid.uuid4().hex};self.assertEqual(self.engine.reserve(other,self.session)['state'],'Reserved');raw=self.claim().read_bytes()
        self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Aborted');self.engine.discard(self.q,self.session);self.assertEqual(raw,self.claim().read_bytes())

    def test_discard_before_reserve_is_durable_and_never_late_reserves(self):
        self.assertEqual(self.engine.discard(self.q)['state'],'Aborted')
        with patch.object(self.p,'plan_locked',side_effect=AssertionError('must only observe')):self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Aborted')
        self.assertFalse(self.claim().exists())

    def test_new_operation_ids_must_differ_from_source_and_purge_ids(self):
        for operation in (self.f.q['operationId'],self.f.f.native.ref['operationId']):
            with self.subTest(operation=operation),self.assertRaisesRegex(Exception,'NEW_OPERATION_REQUIRED'):self.engine.reserve({**self.q,'operationId':operation},self.session)
        self.assertFalse(list(self.state.iterdir()))

    def test_foreign_owner_cannot_inspect_cancel_or_change_existing_reservation(self):
        self.engine.reserve(self.q,self.session);raw=self.claim().read_bytes()
        for action in (self.engine.reserve,self.engine.inspect,self.engine.discard):
            with self.subTest(action=action.__name__),self.assertRaisesRegex(Exception,'NOT_FOUND'):action({**self.q,'owner':'B'*64},self.session)
        self.assertEqual(raw,self.claim().read_bytes())

    def test_changed_namespace_or_data_refuses_reservation_without_creating_claim(self):
        self.base.leaf.write_bytes(b'foreign target')
        with self.assertRaisesRegex(Exception,'TARGET_EXISTS'):self.engine.reserve(self.q,self.session)
        self.assertFalse(self.claim().exists());self.assertFalse(self.journal().exists());self.base.leaf.unlink()
        self.f.f.hdd().write_bytes(b'corrupt archive')
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED|HISTORY_CORRUPT|FILES_BACKUP'):self.engine.reserve(self.q,self.session)
        self.assertFalse(self.claim().exists());self.assertFalse(self.journal().exists())

    def test_session_missing_or_changed_plan_cannot_create_reservation(self):
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.reserve(self.q,None)
        self.assertFalse(self.claim().exists());self.assertFalse(self.journal().exists())
        bad=copy.deepcopy(self.q);bad['plan']['archiveBytes']=float(bad['plan']['archiveBytes']);bad['plan']['fingerprint']=self.p.digest(bad['plan'])
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.reserve(bad,self.session)
        self.assertFalse(self.claim().exists());self.assertFalse(self.journal().exists())

    def test_cancel_after_archive_change_keeps_reserved_claim(self):
        self.engine.reserve(self.q,self.session);raw=self.claim().read_bytes();self.f.f.hdd().write_bytes(b'corrupt archive')
        with self.assertRaises(Exception):self.engine.discard(self.q,self.session)
        self.assertEqual(self.record()['state'],'Reserved');self.assertEqual(raw,self.claim().read_bytes())

    def test_crash_before_claim_aborts_without_any_late_reservation(self):
        self.kill_at('Preparing');before=self.protected();self.assertFalse(self.claim().exists());self.assertEqual(self.engine.inspect(self.q)['state'],'Aborted');self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Aborted');self.assertEqual(before,self.protected())

    def test_crash_after_claim_before_commit_is_unknown_and_never_released(self):
        self.kill_at('SourceClaimed');before=self.protected();raw=self.claim().read_bytes()
        for action in (self.engine.inspect,self.engine.discard,self.engine.reserve):
            self.assertEqual(action(self.q,self.session)['state'],'Unknown');self.assertEqual(raw,self.claim().read_bytes())
        self.assertEqual(before,self.protected())

    def test_durable_reservation_survives_process_death_without_recapturing(self):
        self.kill_at('Reserved');raw=self.claim().read_bytes();before=self.protected()
        with patch.object(self.p,'plan_locked',side_effect=AssertionError('no automatic new authorization')):
            self.assertEqual(self.engine.inspect(self.q)['state'],'Reserved');self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Reserved')
        self.assertEqual(raw,self.claim().read_bytes());self.assertEqual(before,self.protected())

    def test_crash_after_durable_abort_finishes_only_own_claim_release(self):
        self.engine.reserve(self.q,self.session);self.kill_at('AbortedSaved',True);self.assertTrue(self.claim().exists());self.assertEqual(self.record()['state'],'Aborted');before=self.protected()
        self.assertEqual(self.engine.inspect(self.q)['state'],'Aborted');self.assertFalse(self.claim().exists());self.assertEqual(before,self.protected())

    def test_crash_after_release_never_reacquires_same_operation(self):
        self.engine.reserve(self.q,self.session);self.kill_at('Released',True);self.assertFalse(self.claim().exists());self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Aborted');self.assertFalse(self.claim().exists())

    def test_lost_claim_reestablishes_only_protection_as_unknown(self):
        self.engine.reserve(self.q,self.session);raw=self.claim().read_bytes();self.claim().unlink();before=self.protected()
        self.assertEqual(self.engine.inspect(self.q)['state'],'Unknown');self.assertEqual(raw,self.claim().read_bytes());self.assertEqual(before,self.protected());self.assertEqual(self.engine.discard(self.q,self.session)['state'],'Unknown');self.assertEqual(raw,self.claim().read_bytes())

    def test_orphan_claim_does_not_reconstruct_a_reserved_or_successful_operation(self):
        self.engine.reserve(self.q,self.session);self.journal().unlink();raw=self.claim().read_bytes()
        self.assertEqual(self.engine.reserve(self.q,self.session)['state'],'Unknown');self.assertEqual(raw,self.claim().read_bytes());self.assertFalse(self.engine.discard(self.q,self.session)['archiveCopied'])

    def test_conflicting_claim_is_never_overwritten_or_cleared(self):
        self.engine.reserve(self.q,self.session);claim=json.loads(self.claim().read_bytes());claim.update(restoreOperationId=uuid.uuid4().hex,restoreOwner='C'*64,restoreFingerprint='C'*64);self.claim().write_text(json.dumps(claim));raw=self.claim().read_bytes()
        for action in (self.engine.inspect,self.engine.reserve,self.engine.discard):
            r=action(self.q,self.session);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['sourceFenced']);self.assertFalse(r['ownedFence']);self.assertEqual(raw,self.claim().read_bytes())

    def test_unsafe_claim_file_and_registry_fail_closed(self):
        self.engine.reserve(self.q,self.session);self.claim().chmod(0o644)
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.engine.inspect(self.q)
        self.claim().chmod(0o600);held=self.claim().with_suffix('.held');self.claim().rename(held);self.claim().symlink_to(held)
        with self.assertRaises(OSError):self.engine.discard(self.q,self.session)
        self.assertEqual(self.record()['state'],'Reserved');self.claim().unlink();held.rename(self.claim())

    def test_source_and_purge_locks_exclude_archive_reservation(self):
        for engine,q in ((self.p.purges,self.f.q),(self.p.source,self.f.f.native.ref)):
            with engine.locked(q):
                with self.assertRaisesRegex(Exception,'BUSY'):self.engine.reserve(self.q,self.session)
            self.assertFalse(self.claim().exists());self.assertFalse(self.journal().exists())

    def test_state_namespace_must_not_alias_any_existing_journal_or_claim_registry(self):
        for path in (self.base.state,self.f.f.f.state,self.f.f.native.state,self.f.f.native.engine.claims_path):
            with self.subTest(path=path.name),self.assertRaisesRegex(ValueError,'ARCHIVE_STATE_MUST_BE_INDEPENDENT'):candidate.archive_fences_type(recycling.directories.ContainerDirectories)(self.p,path)

    def legacy_claim(self):
        # A format-compatible stale index fault, not a permitted post-purge
        # legacy producer: that producer is already blocked by Purged.
        value=dict(schemaVersion=1,source=self.f.f.native.ref,restoreOperationId=uuid.uuid4().hex,restoreOwner='F'*64,restoreFingerprint='F'*64)
        with self.f.f.native.engine.claims() as directory:self.engine.f.save_receipt(directory,self.f.f.native.ref['operationId'],value)
        return self.claim().read_bytes()

    def test_preparing_with_foreign_claim_aborts_without_releasing_it(self):
        self.kill_at('Preparing');raw=self.legacy_claim();before=self.protected()
        r=self.engine.inspect(self.q);self.assertEqual(r['state'],'Aborted');self.assertTrue(r['sourceFenced']);self.assertFalse(r['ownedFence']);self.assertEqual(raw,self.claim().read_bytes());self.assertEqual(before,self.protected())

    def test_unknown_with_claim_lost_again_restores_only_the_same_protection(self):
        self.engine.reserve(self.q,self.session);self.claim().unlink();self.assertEqual(self.engine.inspect(self.q)['state'],'Unknown');self.claim().unlink();before=self.protected()
        r=self.engine.discard(self.q,self.session);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['ownedFence']);self.assertFalse(r['archiveCopied']);self.assertEqual(json.loads(self.claim().read_bytes()),self.engine.claim(self.q));self.assertEqual(before,self.protected())

    def test_original_purged_marker_bytes_and_identity_never_become_archive_state(self):
        marker=self.f.f.f.marker();raw=marker.read_bytes();identity=self.p.source.metadata(marker.stat())
        self.engine.reserve(self.q,self.session);self.kill_at('AbortedSaved',True);self.engine.inspect(self.q)
        self.assertEqual(marker.read_bytes(),raw);self.assertEqual(self.p.source.metadata(marker.stat()),identity);self.assertEqual(json.loads(raw)['state'],'Purged')

    def test_legacy_claim_blocks_reservation_and_discard_never_releases_foreign_claim(self):
        raw=self.legacy_claim();before=self.protected()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(self.q,self.session)
        self.assertEqual(self.engine.discard(self.q,self.session)['state'],'Aborted');self.assertEqual(raw,self.claim().read_bytes());self.assertEqual(before,self.protected())

    def test_independent_archive_journal_cannot_nest_existing_namespaces(self):
        for path in (self.base.state/'nested-archive',self.f.f.native.engine.claims_path/'nested-archive',self.base.disk.disk):
            with self.subTest(path=path.name),self.assertRaisesRegex(ValueError,'ARCHIVE_STATE_MUST_BE_INDEPENDENT'):candidate.archive_fences_type(recycling.directories.ContainerDirectories)(self.p,path)


if __name__=='__main__':unittest.main()
