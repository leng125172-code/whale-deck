"""Read-only retirement eligibility with real FDs/codec; quota/device doubles.

Native acceptance separately uses fully reserved ext4 on real NVMe and HDD.
No product configuration, source, identities, budgets or services are changed.
"""
import hashlib
import json
import os
import unittest
from unittest.mock import patch

import test_development_archives as fixture

retirement = fixture.fixture.load('development_retirement_test', 'whaledeck-development-retirement.py')
files = fixture.fixture.files


@unittest.skipUnless(os.geteuid() == 0, 'Requires disposable private root fixtures')
class RetirementPlanTests(unittest.TestCase):
    setUp = fixture.ArchiveTests.setUp
    write_config = fixture.ArchiveTests.write_config
    write_mountinfo = fixture.ArchiveTests.write_mountinfo
    enable_quota = fixture.ArchiveTests.enable_quota
    stop = fixture.ArchiveTests.stop
    command = fixture.ArchiveTests.command
    entry = fixture.ArchiveTests.entry
    snapshot = fixture.ArchiveTests.snapshot

    def prepare(self):
        command = self.command()
        receipt = self.capture.capture(command)
        value = dict(schemaVersion=1, storage=self.request, expectedEpoch=2,
            archiveId=command['operationId'], archivePlanSha256=command['planSha256'],
            captureReceiptSha256=receipt['receiptSha256'])
        return retirement.DevelopmentRetirementPlans(self.capture), value, command, receipt

    def disk_snapshot(self, root):
        return {str(p.relative_to(root)): dict(version=files.version(p.lstat()),
            sha256=hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None)
            for p in [root, *sorted(root.rglob('*'))]}

    def test_complete_matching_archive_produces_a_repeatable_non_authorizing_plan(self):
        planner, value, _, receipt = self.prepare()
        before = self.disk_snapshot(self.root), self.disk_snapshot(self.hdd), dict(self.quota.projects)
        with patch.object(self.engine, 'write_new', side_effect=AssertionError('no metadata writes')), \
                patch.object(self.capture, 'durable_json', side_effect=AssertionError('no archive writes')):
            plan = planner.plan(value)
            self.assertEqual(plan, planner.plan(value))
        self.assertEqual(before, (self.disk_snapshot(self.root), self.disk_snapshot(self.hdd), dict(self.quota.projects)))
        expected = dict(plan); digest = expected.pop('retirementPlanSha256')
        self.assertEqual(hashlib.sha256(self.engine.canonical(expected)).hexdigest(), digest)
        self.assertFalse(plan['deletionAuthorized'])
        self.assertTrue(plan['retainsSource']); self.assertTrue(plan['reservesBudget'])
        self.assertEqual(receipt['archiveSha256'], plan['archiveSha256'])
        self.assertEqual(receipt['archiveInode'], plan['archiveInode'])
        self.assertEqual(receipt['sourceManifestSha256'], plan['sourceManifestSha256'])
        self.assertNotIn('private-fixture-value', json.dumps(plan))
        self.assertNotIn('二进制', json.dumps(plan, ensure_ascii=False))

    def test_invalid_envelope_is_refused_before_source_access(self):
        planner, value, _, _ = self.prepare()
        invalid = [None, [], {}, dict(value, unknown=True)]
        invalid += [dict(value, **{key: changed}) for key, changed in (
            ('schemaVersion', True), ('schemaVersion', 2), ('expectedEpoch', True),
            ('expectedEpoch', 1), ('expectedEpoch', 3), ('expectedEpoch', 0x7ffffffffffffffe),
            ('archiveId', '../other'), ('archiveId', fixture.archives.ZERO),
            ('archivePlanSha256', 'A' * 64), ('captureReceiptSha256', ''), ('captureReceiptSha256', None))]
        with patch.object(self.capture, 'source', side_effect=AssertionError('invalid input reached source')):
            for changed in invalid:
                with self.subTest(changed=changed), self.assertRaises(files.Rejected):
                    planner.plan(changed)

    def test_wrong_saved_receipt_is_refused_and_neither_copy_is_changed(self):
        planner, value, _, _ = self.prepare()
        before = self.disk_snapshot(self.root), self.disk_snapshot(self.hdd)
        with self.assertRaisesRegex(files.Rejected, 'ARCHIVE_RECEIPT_CHANGED'):
            planner.plan(dict(value, captureReceiptSha256='f' * 64))
        self.assertEqual(before, (self.disk_snapshot(self.root), self.disk_snapshot(self.hdd)))

    def test_source_edit_invalidates_an_otherwise_valid_hdd_copy(self):
        planner, value, _, _ = self.prepare()
        self.file.write_bytes(b'new source which must be retained')
        before = self.disk_snapshot(self.root), self.disk_snapshot(self.hdd)
        with self.assertRaisesRegex(files.Rejected, 'RETIREMENT_SOURCE_CHANGED'):
            planner.plan(value)
        self.assertEqual(before, (self.disk_snapshot(self.root), self.disk_snapshot(self.hdd)))

    def test_same_bytes_and_mtime_with_a_replaced_inode_are_not_the_original_source(self):
        planner, value, _, _ = self.prepare()
        info, body = self.file.stat(), self.file.read_bytes()
        retained = self.file.with_name('retained'); self.file.rename(retained)
        self.file.write_bytes(body); self.file.chmod(info.st_mode & 0o777)
        os.chown(self.file, info.st_uid, info.st_gid); os.utime(self.file, ns=(info.st_atime_ns, info.st_mtime_ns))
        retained.unlink()
        with self.assertRaisesRegex(files.Rejected, 'RETIREMENT_SOURCE_CHANGED'):
            planner.plan(value)
        self.assertEqual(body, self.file.read_bytes())

    def test_directory_mode_change_and_added_source_are_not_covered_by_old_archive(self):
        planner, value, _, _ = self.prepare()
        (self.payload / 'empty').chmod(0o750)
        (self.payload / 'later').write_bytes(b'not in archive')
        with self.assertRaisesRegex(files.Rejected, 'RETIREMENT_SOURCE_CHANGED'):
            planner.plan(value)
        self.assertEqual(b'not in archive', (self.payload / 'later').read_bytes())

    def test_stale_epoch_is_refused(self):
        planner, value, _, _ = self.prepare()
        with self.assertRaisesRegex(files.Rejected, 'ARCHIVE_NOT_STOPPED'):
            planner.plan(dict(value, expectedEpoch=4))

    def test_resumed_workspace_cannot_use_the_stopped_plan(self):
        planner, value, _, _ = self.prepare()
        command = dict(storage=self.request, action='resume', expectedEpoch=2, operationId='', planSha256='', storageProofSha256='')
        with patch.object(fixture.lifecycle, 'GIT_CGROUP', self.mount / 'no-git-cgroup'):
            planned = self.control.run(command)
            self.control.run(dict(command, operationId=str(fixture.uuid.uuid4()),
                planSha256=planned['planSha256'], storageProofSha256=planned['storageProofSha256']), apply=True)
        with self.assertRaisesRegex(files.Rejected, 'ARCHIVE_NOT_STOPPED'):
            planner.plan(value)
        self.assertTrue(self.file.exists())

    def test_changed_environment_budget_and_workspace_cannot_rebind_a_copy(self):
        planner, value, _, _ = self.prepare()
        for key, changed in (('environmentSha256', 'b' * 64), ('diskMiB', 512), ('workspaceId', str(fixture.uuid.uuid4()))):
            with self.subTest(key=key), self.assertRaises(files.Rejected):
                planner.plan(dict(value, storage=dict(self.request, **{key: changed})))
        self.assertTrue(self.file.exists())

    def test_reader_lock_blocks_plan_before_archive_scan(self):
        planner, value, _, _ = self.prepare()
        with self.engine.attested_payload(self.request), \
                patch.object(self.capture, 'verify', side_effect=AssertionError('busy source cannot reach HDD')):
            with self.assertRaisesRegex(files.Rejected, 'STORAGE_BUSY'):
                planner.plan(value)

    def test_hdd_corruption_and_missing_receipt_cannot_yield_a_plan(self):
        planner, value, command, _ = self.prepare()
        archive = self.entry(command) / 'payload.tar.gz'
        original = archive.read_bytes(); archive.write_bytes(original[:-10])
        with self.assertRaises(files.Rejected): planner.plan(value)
        archive.write_bytes(original)
        receipt = self.entry(command) / 'receipt.json'; original_receipt = receipt.read_bytes(); receipt.unlink()
        with self.assertRaises(FileNotFoundError): planner.plan(value)
        self.assertEqual(original, archive.read_bytes()); self.assertTrue(self.file.exists())
        self.assertFalse(receipt.exists()); self.assertGreater(len(original_receipt), 0)

    def test_foreign_hdd_or_quota_proof_is_refused(self):
        planner, value, _, _ = self.prepare()
        self.store.proof['declared'] = 'different-hdd'
        with self.assertRaisesRegex(files.Rejected, 'ARCHIVE_RECEIPT_CHANGED'): planner.plan(value)
        self.store.proof['declared'] = 'unit-test-double'
        project = next(iter(self.quota.projects)); self.quota.projects[project] = (1, 1)
        with self.assertRaises(fixture.fixture.quota_module.QuotaError): planner.plan(value)

    def test_live_mount_number_change_preserves_durable_plan_binding(self):
        planner, value, _, _ = self.prepare()
        original = planner.plan(value)
        self.store.proof.update(mountId='different-live-mount', device='different-live-loop')
        self.assertEqual(original, planner.plan(value))

    def test_final_source_verification_and_namespace_recheck_must_complete(self):
        planner, value, _, _ = self.prepare()
        with patch.object(self.capture.manifest_type, 'verify', side_effect=files.Rejected('LATE_SOURCE_CHANGE')):
            with self.assertRaisesRegex(files.Rejected, 'LATE_SOURCE_CHANGE'): planner.plan(value)
        original = self.engine.recheck; calls = []
        def last_check(*args):
            original(*args); calls.append(1)
            if len(calls) == 2: raise files.Rejected('LATE_NAMESPACE_CHANGE')
        with patch.object(self.engine, 'recheck', side_effect=last_check):
            with self.assertRaisesRegex(files.Rejected, 'LATE_NAMESPACE_CHANGE'): planner.plan(value)


if __name__ == '__main__':
    unittest.main()
