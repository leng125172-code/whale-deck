"""Real FD/codec/unlink/journal tests; kernel quota/device remain explicit doubles."""
import hashlib
import json
import os
import signal
import unittest
import uuid
from unittest.mock import patch

import test_development_retirement as fixture

execution = fixture.fixture.fixture.load('retirement_execution_test', 'whaledeck-development-retirement-execution.py')
files = fixture.files


@unittest.skipUnless(os.geteuid() == 0, 'Requires disposable private root fixtures')
class RetirementExecutionTests(fixture.RetirementPlanTests):
    # Inherited tests still verify that read-only plans never mutate either copy.
    def prepare_execution(self):
        planner, prerequisite, archive, receipt = self.prepare()
        self.policy = planner
        self.executor = execution.DevelopmentRetirementExecution(self.capture, fixture.fixture.archives.PayloadFiles)
        self.releases = []
        def release(parent, project, size):
            self.assertEqual(self.request['diskMiB'], size)
            self.assertFalse(self.target.exists())
            self.assertFalse((self.journal / 'source').exists())
            self.quota.require_enforcing(parent)
            self.releases.append(project)
            self.quota.projects.pop(project, None)
        self.quota.release_empty = release
        value = dict(schemaVersion=1, retirementId=str(uuid.uuid4()), prerequisite=prerequisite,
            retirementPlanSha256=planner.plan(prerequisite)['retirementPlanSha256'])
        self.journal = self.root / ('.retirement-' + self.request['workspaceId'])
        return value, archive, receipt

    def run_execution(self, value):
        return self.executor.run(value, self.policy)

    def test_removes_only_pinned_source_retains_hdd_and_permanent_audit_fence(self):
        value, archive, saved = self.prepare_execution()
        hdd = self.disk_snapshot(self.hdd)
        result = self.run_execution(value)
        self.assertTrue(result['sourceRemoved']); self.assertTrue(result['quotaReleased'])
        self.assertFalse(result['releasesApplicationBudget'])
        self.assertFalse(self.target.exists()); self.assertFalse((self.journal / 'source').exists())
        self.assertEqual({}, self.quota.projects); self.assertEqual(1, len(self.releases))
        self.assertEqual(hdd, self.disk_snapshot(self.hdd))
        self.assertEqual(saved, self.capture.verify(archive, saved['receiptSha256']))
        self.assertEqual(result, self.run_execution(value)); self.assertEqual(1, len(self.releases))
        self.assertTrue((self.journal / 'prepared.json').exists())
        self.assertTrue((self.root / ('.lifecycle-' + self.request['workspaceId'] + '.json')).exists())
        for operation in (lambda: self.engine.allocate(self.request), lambda: self.capture.plan(self.request, 2),
                          lambda: self.policy.plan(value['prerequisite'])):
            with self.assertRaisesRegex(files.Rejected, 'STORAGE_RETIRED'):operation()

    def test_invalid_request_never_enters_source(self):
        value, _, _ = self.prepare_execution()
        for bad in (None, {}, dict(value, extra=1), dict(value, schemaVersion=True),
                    dict(value, retirementId=self.request['workspaceId']), dict(value, retirementPlanSha256='f'*63)):
            with self.subTest(bad=bad), self.assertRaises((files.Rejected, TypeError)):
                self.run_execution(bad)
        self.assertTrue(self.file.exists()); self.assertFalse(self.journal.exists())

    def test_stale_plan_and_changed_or_corrupt_source_are_refused_before_fence(self):
        value, _, _ = self.prepare_execution()
        with self.assertRaisesRegex(files.Rejected, 'PLAN_CHANGED'):
            self.run_execution(dict(value, retirementPlanSha256='f'*64))
        self.file.write_bytes(b'unarchived change')
        with self.assertRaisesRegex(files.Rejected, 'SOURCE_CHANGED'):
            self.run_execution(value)
        self.assertEqual(b'unarchived change', self.file.read_bytes()); self.assertFalse(self.journal.exists())

    def test_busy_source_refuses_before_fence_or_hdd_work(self):
        value, _, _ = self.prepare_execution()
        with self.engine.attested_payload(self.request), patch.object(self.capture,'verify',side_effect=AssertionError('busy')):
            with self.assertRaisesRegex(files.Rejected,'STORAGE_BUSY'):self.run_execution(value)
        self.assertFalse(self.journal.exists()); self.assertTrue(self.file.exists())

    def test_fence_alone_blocks_old_allocation_resume_and_attested_entry(self):
        value, _, _ = self.prepare_execution()
        self.journal.mkdir(mode=0o700)
        with self.assertRaisesRegex(files.Rejected,'STORAGE_RETIRED'):self.engine.allocate(self.request)
        with self.assertRaisesRegex(files.Rejected,'STORAGE_RETIRED'):
            with self.engine.attested_payload(self.request):pass
        command=dict(storage=self.request,action='resume',expectedEpoch=2,operationId='',planSha256='',storageProofSha256='')
        with self.assertRaisesRegex(files.Rejected,'STORAGE_RETIRED'):self.control.run(command)
        self.assertTrue(self.file.exists())
        self.assertTrue(self.run_execution(value)['sourceRemoved'])

    def test_interruption_after_rename_resumes_same_inode_and_rejects_changed_request(self):
        value, _, _ = self.prepare_execution()
        original=self.executor.remove_tree
        with patch.object(self.executor,'remove_tree',side_effect=RuntimeError('cut after detach')):
            with self.assertRaises(RuntimeError):self.run_execution(value)
        self.assertFalse(self.target.exists()); self.assertTrue((self.journal/'source/data').exists())
        with self.assertRaisesRegex(files.Rejected,'REQUEST_CHANGED'):
            self.run_execution(dict(value,retirementId=str(uuid.uuid4())))
        self.assertTrue(self.run_execution(value)['sourceRemoved']); self.assertEqual(1,len(self.releases))

    def test_unrecorded_missing_source_cannot_be_mistaken_for_completed_unlink(self):
        value, _, _ = self.prepare_execution()
        with patch.object(self.executor,'remove_tree',side_effect=RuntimeError('cut')):
            with self.assertRaises(RuntimeError):self.run_execution(value)
        (self.journal/'source/data/代码/二进制.bin').unlink()
        with self.assertRaisesRegex(files.Rejected,'SOURCE_MISSING_WITHOUT_INTENT'):self.run_execution(value)
        self.assertFalse((self.journal/'receipt.json').exists()); self.assertEqual([],self.releases)

    def test_late_unarchived_entry_keeps_unknown_without_quota_or_completion(self):
        value, _, _ = self.prepare_execution()
        with patch.object(self.executor,'remove_tree',side_effect=RuntimeError('cut')):
            with self.assertRaises(RuntimeError):self.run_execution(value)
        extra=self.journal/'source/data/must-remain';extra.write_bytes(b'unarchived')
        with self.assertRaises(files.Rejected):self.run_execution(value)
        self.assertEqual(b'unarchived',extra.read_bytes()); self.assertEqual([],self.releases)
        self.assertFalse((self.journal/'receipt.json').exists())

    def test_replaced_or_symlink_file_and_allocation_fail_closed(self):
        value, _, _ = self.prepare_execution()
        with patch.object(self.executor,'remove_tree',side_effect=RuntimeError('cut')):
            with self.assertRaises(RuntimeError):self.run_execution(value)
        original=self.journal/'source/data/代码/二进制.bin';original.unlink();original.symlink_to('/etc/passwd')
        with self.assertRaises((files.Rejected,OSError)):self.run_execution(value)
        self.assertTrue(original.is_symlink());self.assertFalse((self.journal/'receipt.json').exists())

    def test_late_quota_failure_retains_original_claim_and_retries_only_after_all_fds_close(self):
        value, _, _ = self.prepare_execution()
        release=self.quota.release_empty
        with patch.object(self.quota,'release_empty',side_effect=fixture.fixture.fixture.quota_module.QuotaError('QUOTA_STILL_REFERENCED')):
            with self.assertRaisesRegex(self.quota.QuotaError,'STILL_REFERENCED'):self.run_execution(value)
        self.assertFalse(self.target.exists());self.assertFalse((self.journal/'receipt.json').exists())
        self.assertEqual(1,len(list(self.root.glob('.quota-*.json'))));self.assertEqual([],self.releases)
        self.assertTrue(self.run_execution(value)['quotaReleased']);self.assertEqual(1,len(self.releases))

    def test_release_checkpoint_never_changes_a_reused_project_claim_or_kernel(self):
        value, _, _ = self.prepare_execution()
        write=self.executor.write
        def cut(parent,name,data):
            write(parent,name,data)
            if name=='quota-released.json':raise RuntimeError('after durable release')
        with patch.object(self.executor,'write',side_effect=cut):
            with self.assertRaises(RuntimeError):self.run_execution(value)
        claim=next(self.root.glob('.quota-*.json'))
        other=json.loads(claim.read_bytes());other['workspaceId']=str(uuid.uuid4())
        claim.write_bytes(self.engine.canonical(other));claim.chmod(0o600)
        before=claim.read_bytes(),claim.stat().st_ino
        with patch.object(self.quota,'release_empty',side_effect=AssertionError('must not clear reused ID')):
            result=self.run_execution(value)
        self.assertTrue(result['claimReleased']);self.assertEqual(before,(claim.read_bytes(),claim.stat().st_ino))

    def test_actual_process_death_after_unlink_resumes_only_the_original_intent(self):
        value, _, _ = self.prepare_execution()
        unlink=os.unlink
        def cut(name,*args,**kwargs):
            unlink(name,*args,**kwargs)
            if name=='二进制.bin':os.kill(os.getpid(),signal.SIGSTOP)
        child=os.fork()
        if child==0:
            try:
                with patch.object(os,'unlink',side_effect=cut):self.run_execution(value)
            finally:os._exit(4)
        _,status=os.waitpid(child,os.WUNTRACED);self.assertTrue(os.WIFSTOPPED(status))
        os.kill(child,signal.SIGKILL);os.waitpid(child,0)
        self.assertFalse((self.journal/'receipt.json').exists());self.assertEqual([],self.releases)
        self.assertTrue(self.run_execution(value)['sourceRemoved']);self.assertEqual(1,len(self.releases))

    def test_failed_intent_directory_sync_blocks_unlink_on_every_retry(self):
        value, _, _ = self.prepare_execution()
        fsync = os.fsync
        def fail_journal_sync(fd):
            if (self.journal.exists() and os.fstat(fd).st_ino == self.journal.stat().st_ino
                    and list(self.journal.glob('remove-*.json'))):
                raise OSError('injected intent directory sync failure')
            return fsync(fd)
        with patch.object(os, 'fsync', side_effect=fail_journal_sync):
            with self.assertRaisesRegex(OSError, 'intent directory sync'):
                self.run_execution(value)
            source = self.journal / 'source'
            before = self.disk_snapshot(source)
            with self.assertRaisesRegex(OSError, 'intent directory sync'):
                self.run_execution(value)
            self.assertEqual(before, self.disk_snapshot(source))
        self.assertEqual([], self.releases)
        self.assertTrue(self.run_execution(value)['sourceRemoved'])

    def test_failed_release_directory_sync_keeps_claim_on_every_retry(self):
        value, _, _ = self.prepare_execution()
        fsync = os.fsync
        def fail_journal_sync(fd):
            if (self.journal.exists() and os.fstat(fd).st_ino == self.journal.stat().st_ino
                    and (self.journal / 'quota-released.json').exists()):
                raise OSError('injected release directory sync failure')
            return fsync(fd)
        with patch.object(os, 'fsync', side_effect=fail_journal_sync):
            with self.assertRaisesRegex(OSError, 'release directory sync'):
                self.run_execution(value)
            claim = next(self.root.glob('.quota-*.json'))
            before = claim.read_bytes(), claim.stat().st_ino
            with self.assertRaisesRegex(OSError, 'release directory sync'):
                self.run_execution(value)
            self.assertEqual(before, (claim.read_bytes(), claim.stat().st_ino))
        self.assertTrue(self.run_execution(value)['quotaReleased'])
        self.assertEqual(1, len(self.releases))

if __name__=='__main__':unittest.main()
