#!/usr/bin/env python3
"""Pure public DTO validation contracts; never run the installed BFF probe."""
import copy
import importlib.util
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


DIRECTORY = Path(__file__).resolve().parent


class OverwriteBffContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        script = DIRECTORY / 'validate_file_tree_overwrite_restoring_bff.py'
        spec = importlib.util.spec_from_file_location('overwrite_bff_public_contract_under_test', script)
        module = importlib.util.module_from_spec(spec)
        # Sibling imports define helpers only. No main(), environment or HTTP calls.
        sys.path.insert(0, str(DIRECTORY))
        try:
            spec.loader.exec_module(module)
        finally:
            sys.path.remove(str(DIRECTORY))
        cls.probe_module = module
        cls.validate = staticmethod(module.public_response)

    @staticmethod
    def ready(kind='Trash'):
        identifier = '972ace59-c6aa-4cab-a237-8796f8fd81ef'
        return {
            'id': identifier, 'planningJobId': identifier, 'executionJobId': None, 'state': 'Ready',
            'command': {
                'captureId': '129f14ed-3254-4868-9885-acad522acefa',
                'target': {'rootId': 'whaledeck.files.shared', 'path': '现有目录'},
                'targetExpectedVersion': 'B' * 64,
                'source': {'rootId': 'whaledeck.files.shared', 'path': '历史目录'}, 'captureKind': kind,
            },
            'entryCount': 4, 'totalBytes': 1073741831, 'targetEntryCount': 3, 'targetTotalBytes': 2097159,
            'readyExpiresAtUtc': '2099-10-21T01:05:00Z', 'resultCode': 'FILES_TREE_READY_FOR_CONFIRMATION',
            'version': 1, 'publishedVersion': None, 'partialRetained': False,
            'confirmation': {
                'planHash': 'unit-contract-only-plan', 'expiresAtUtc': '2099-10-21T01:05:00Z',
                'warning': '仅核验公开传输契约，不执行文件操作。',
            },
            'archiveBytes': 1073756160, 'archiveSha256': 'D' * 64,
            'overwrittenCaptureId': None, 'backupMayBeRetained': False, 'backupVerified': False,
            'originalsRetained': False, 'targetMayHaveChanged': False,
            'backupArchiveBytes': None, 'backupArchiveSha256': None,
        }

    @classmethod
    def succeeded(cls, kind='Trash'):
        row = cls.ready(kind)
        execution = '8572fa55-d18e-4ac2-a392-513b15bdc4ef'
        row.update(executionJobId=execution, state='Succeeded', version=3, confirmation=None,
            publishedVersion='F' * 64, resultCode='FILES_TREE_OVERWRITE_RESTORE_COMPLETED',
            overwrittenCaptureId=execution, backupMayBeRetained=True, backupVerified=True,
            originalsRetained=True, targetMayHaveChanged=True,
            backupArchiveBytes=2099200, backupArchiveSha256='A' * 64)
        return row

    @staticmethod
    def request(row):
        return {key: copy.deepcopy(row['command'][key]) for key in ('captureId', 'target', 'targetExpectedVersion')}

    def rejected(self, row, command=None):
        with self.assertRaises(AssertionError):
            self.validate(row, command)

    def test_ready_and_succeeded_public_contracts_accept_both_capture_kinds(self):
        for kind in ('Trash', 'Overwrite'):
            for factory in (self.ready, self.succeeded):
                with self.subTest(kind=kind, state=factory.__name__):
                    row = factory(kind)
                    self.assertEqual(24, len(row))
                    self.assertEqual(5, len(row['command']))
                    self.assertIs(row, self.validate(row))
                    self.assertIs(row, self.validate(row, self.request(row)))

    def test_exact_public_top_level_fields_reject_private_material_and_missing_fields(self):
        for private in ('captureOwner', 'ownerHash', 'loginBinding', 'planJson', 'commandJson', 'requestHash', 'shell'):
            with self.subTest(extra=private):
                row = self.ready(); row[private] = 'caller-must-not-see'
                self.rejected(row)
        for missing in self.ready():
            with self.subTest(missing=missing):
                row = self.ready(); del row[missing]
                self.rejected(row)

    def test_command_and_locations_reject_extra_private_fields_and_missing_members(self):
        for private in ('captureOwner', 'expectedVersion', 'manifestSha256', 'format', 'hostPath'):
            with self.subTest(extra=private):
                row = self.ready(); row['command'][private] = 'not-public'
                self.rejected(row)
        for missing in self.ready()['command']:
            with self.subTest(missing=missing):
                row = self.ready(); del row['command'][missing]
                self.rejected(row)
        for location in ('source', 'target'):
            for field in ('hostPath', 'ownerUid', 'expectedVersion'):
                with self.subTest(location=location, extra=field):
                    row = self.ready(); row['command'][location][field] = 'not-public'
                    self.rejected(row)
            for missing in ('rootId', 'path'):
                with self.subTest(location=location, missing=missing):
                    row = self.ready(); del row['command'][location][missing]
                    self.rejected(row)

    def test_zero_noncanonical_or_invalid_identifiers_are_rejected_everywhere(self):
        zero = '00000000-0000-0000-0000-000000000000'
        invalid = (zero, 'not-a-uuid', '972ACE59-C6AA-4CAB-A237-8796F8FD81EF',
            '972ace59c6aa4caba2378796f8fd81ef', '{972ace59-c6aa-4cab-a237-8796f8fd81ef}')
        for field in ('id', 'planningJobId', 'executionJobId', 'overwrittenCaptureId', 'captureId'):
            for value in invalid:
                with self.subTest(field=field, value=value):
                    row = self.succeeded()
                    if field == 'captureId': row['command'][field] = value
                    else: row[field] = value
                    self.rejected(row)

    def test_planning_job_and_execution_have_independent_exact_identities(self):
        row = self.ready(); row['planningJobId'] = row['command']['captureId']; self.rejected(row)
        for substitute in ('id', 'captureId'):
            row = self.succeeded()
            row['executionJobId'] = row['id'] if substitute == 'id' else row['command']['captureId']
            row['overwrittenCaptureId'] = row['executionJobId']
            self.rejected(row)
        row = self.succeeded(); row['executionJobId'] = None; self.rejected(row)

    def test_command_binding_rejects_different_capture_target_root_path_and_target_version(self):
        for field in ('captureId', 'rootId', 'path', 'targetExpectedVersion'):
            with self.subTest(field=field):
                row = self.ready(); command = self.request(row)
                if field == 'captureId': command[field] = '8572fa55-d18e-4ac2-a392-513b15bdc4ef'
                elif field == 'targetExpectedVersion': command[field] = 'C' * 64
                else: command['target'][field] = 'other.scope' if field == 'rootId' else 'different-target'
                self.rejected(row, command)

    def test_binding_request_cannot_smuggle_private_authority_or_omit_frozen_version(self):
        for extra in ('captureOwner', 'source', 'manifestSha256'):
            row = self.ready(); command = self.request(row); command[extra] = 'not-permitted'
            self.rejected(row, command)
        row = self.ready(); command = self.request(row); del command['targetExpectedVersion']
        self.rejected(row, command)

    def test_hashes_are_exactly_sixty_four_uppercase_hex_characters(self):
        for field in ('archiveSha256', 'backupArchiveSha256', 'publishedVersion', 'targetExpectedVersion'):
            for invalid in ('', 'a' * 64, 'A' * 63, 'A' * 65, 'G' * 64, None):
                with self.subTest(field=field, value=invalid):
                    row = self.succeeded()
                    if field == 'targetExpectedVersion': row['command'][field] = invalid
                    else: row[field] = invalid
                    self.rejected(row)

    def test_unrecognised_or_null_capture_kind_cannot_alias_trash(self):
        for kind in (None, '', 'trash', 'overwrite', 'Unknown'):
            with self.subTest(kind=kind):
                self.rejected(self.ready(kind))

    def test_ready_has_no_execution_backup_or_published_version_and_requires_confirmation(self):
        changes = (
            {'executionJobId': '8572fa55-d18e-4ac2-a392-513b15bdc4ef'},
            {'confirmation': None}, {'backupMayBeRetained': True}, {'backupVerified': True},
            {'targetMayHaveChanged': True}, {'originalsRetained': True}, {'publishedVersion': 'F' * 64},
        )
        for changed in changes:
            with self.subTest(changed=changed):
                row = self.ready(); row.update(changed); self.rejected(row)

    def test_ready_confirmation_requires_exact_fields_and_matching_expiry(self):
        for changed in ({'expiresAtUtc': '2099-10-21T01:06:00Z'}, {'expiresAtUtc': 'not-a-date'},
            {'planHash': ''}, {'warning': None}, {'captureOwner': 'private'}):
            with self.subTest(changed=changed):
                row = self.ready(); row['confirmation'].update(changed); self.rejected(row)
        for missing in ('planHash', 'expiresAtUtc', 'warning'):
            row = self.ready(); del row['confirmation'][missing]; self.rejected(row)

    def test_unverified_backup_proof_fields_must_all_be_null_not_zero_or_empty(self):
        for field, invalid in (('overwrittenCaptureId', '8572fa55-d18e-4ac2-a392-513b15bdc4ef'),
            ('overwrittenCaptureId', ''), ('backupArchiveBytes', 0), ('backupArchiveBytes', 1),
            ('backupArchiveSha256', ''), ('backupArchiveSha256', 'A' * 64)):
            with self.subTest(field=field, value=invalid):
                row = self.ready(); row[field] = invalid; self.rejected(row)

    def test_verified_backup_needs_current_execution_id_and_bounded_integer_archive_bytes(self):
        for invalid in (None, '', '129f14ed-3254-4868-9885-acad522acefa', '972ace59-c6aa-4cab-a237-8796f8fd81ef'):
            row = self.succeeded(); row['overwrittenCaptureId'] = invalid; self.rejected(row)
        for invalid in (None, 0, -1, 17 * 1024**3 + 1, 1.5, True):
            with self.subTest(bytes=invalid):
                row = self.succeeded(); row['backupArchiveBytes'] = invalid; self.rejected(row)
        row = self.succeeded(); row['backupArchiveBytes'] = 17 * 1024**3
        self.assertIs(row, self.validate(row))

    def test_succeeded_requires_every_independent_positive_effect_flag_and_new_version(self):
        for flag in ('backupVerified', 'backupMayBeRetained', 'originalsRetained', 'targetMayHaveChanged'):
            with self.subTest(flag=flag):
                row = self.succeeded(); row[flag] = False; self.rejected(row)
        for invalid in (None, '', 'bad'):
            row = self.succeeded(); row['publishedVersion'] = invalid; self.rejected(row)
        row = self.succeeded(); row['partialRetained'] = True; self.rejected(row)
        row = self.succeeded(); row['confirmation'] = self.ready()['confirmation']; self.rejected(row)

    def test_boolean_flags_are_not_integer_or_string_truthiness(self):
        for flag in ('partialRetained', 'backupMayBeRetained', 'backupVerified', 'originalsRetained', 'targetMayHaveChanged'):
            for invalid in (None, 1, 'true'):
                with self.subTest(flag=flag, value=invalid):
                    row = self.succeeded(); row[flag] = invalid; self.rejected(row)


@unittest.skipUnless(sys.platform.startswith('linux') and getattr(os, 'geteuid', lambda: -1)() == 0,
    'cleanup isolation requires Linux root; Windows and non-root explicitly skip')
class OverwriteBffCleanupIsolationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        OverwriteBffContractTests.setUpClass()
        cls.probe = OverwriteBffContractTests.probe_module

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-overwrite-cleanup-isolation-')
        self.addCleanup(self.temporary.cleanup)
        self.data = Path(self.temporary.name).resolve()
        self.folder = self.data / 'wdv-overwrite-bff-0123456789abcdef'
        self.folder.mkdir(mode=0o700)
        self.folder.chmod(0o700)
        self.tree = self.folder / 'target'
        self.tree.mkdir(mode=0o750)
        (self.tree / '空目录').mkdir(mode=0o710)
        self.payload = self.tree / '中文.bin'
        self.content = b'isolated-own-payload\0' * 23
        self.payload.write_bytes(self.content)
        self.payload.chmod(0o640)
        self.identity = self.probe.proof(self.folder)[:2]
        self.expected_paths = {'target', 'target/空目录', 'target/中文.bin'}
        self.file_proofs = {self.payload: self.probe.proof(self.payload)}
        self.data_patch = patch.object(self.probe, 'DATA', self.data)
        self.data_patch.start()
        self.addCleanup(self.data_patch.stop)
        self.assertEqual(self.folder.parent, self.data)
        self.assertTrue(self.payload.is_relative_to(self.data))

    def cleanup(self, folder=None, identity=None, expected_paths=None, file_proofs=None):
        target = folder if folder is not None else self.folder
        self.assertTrue(target.is_relative_to(self.data))
        self.probe.cleanup_payloads(target, identity if identity is not None else self.identity,
            expected_paths if expected_paths is not None else self.expected_paths,
            file_proofs if file_proofs is not None else self.file_proofs)

    def rejected_without_payload_deletion(self, **kwargs):
        with self.assertRaises(AssertionError):
            self.cleanup(**kwargs)
        self.assertTrue(self.folder.is_dir())
        self.assertTrue(self.payload.is_file())

    def test_exact_owned_directory_and_byte_proofs_are_removed_without_touching_sibling(self):
        sibling = self.data / 'unrelated-sentinel.bin'
        sibling.write_bytes(b'keep-this-separate-fixture')
        self.cleanup()
        self.assertFalse(self.folder.exists())
        self.assertEqual(b'keep-this-separate-fixture', sibling.read_bytes())

    def test_unknown_new_child_refuses_cleanup_and_preserves_all_payload(self):
        unknown = self.folder / 'not-recorded.bin'
        unknown.write_bytes(b'new-object-must-not-be-deleted')
        self.rejected_without_payload_deletion()
        self.assertEqual(self.content, self.payload.read_bytes())
        self.assertEqual(b'new-object-must-not-be-deleted', unknown.read_bytes())

    def test_incomplete_expected_path_set_refuses_cleanup_before_any_unlink(self):
        self.rejected_without_payload_deletion(expected_paths=self.expected_paths - {'target/空目录'})
        self.assertEqual(self.content, self.payload.read_bytes())
        self.assertTrue((self.tree / '空目录').is_dir())

    def test_replaced_root_inode_refuses_cleanup_and_preserves_both_roots(self):
        retained = self.data / 'retained-original-fixture'
        self.assertTrue(retained.is_relative_to(self.data))
        self.folder.rename(retained)
        original_payload = retained / 'target/中文.bin'
        self.folder.mkdir(mode=0o700)
        self.folder.chmod(0o700)
        self.tree.mkdir(mode=0o750)
        (self.tree / '空目录').mkdir(mode=0o710)
        self.payload.write_bytes(b'new-root-payload')
        self.assertNotEqual(self.identity, self.probe.proof(self.folder)[:2])
        self.rejected_without_payload_deletion()
        self.assertEqual(self.content, original_payload.read_bytes())
        self.assertEqual(b'new-root-payload', self.payload.read_bytes())

    def test_changed_file_bytes_refuse_cleanup_even_when_path_set_is_unchanged(self):
        changed = bytes(value ^ 0x20 for value in self.content)
        self.payload.write_bytes(changed)
        self.rejected_without_payload_deletion()
        self.assertEqual(changed, self.payload.read_bytes())

    def test_symbolic_link_refuses_cleanup_and_never_follows_target(self):
        sentinel = self.data / 'symlink-target-sentinel.bin'
        sentinel.write_bytes(b'sentinel-remains-intact')
        link = self.folder / 'unexpected-link'
        link.symlink_to(sentinel)
        self.rejected_without_payload_deletion(expected_paths=self.expected_paths | {'unexpected-link'})
        self.assertTrue(link.is_symlink())
        self.assertEqual(b'sentinel-remains-intact', sentinel.read_bytes())
        self.assertEqual(self.content, self.payload.read_bytes())

    def test_root_or_nested_mount_boundary_refuses_cleanup_without_mounting_anything(self):
        original_read_text = Path.read_text
        for mount in (self.folder, self.tree):
            with self.subTest(mount=mount):
                def mountinfo(path, *args, **kwargs):
                    if path == Path('/proc/self/mountinfo'):
                        return '57 42 0:1 / ' + str(mount) + ' rw - tmpfs tmpfs rw\n'
                    return original_read_text(path, *args, **kwargs)

                # Simulated mountinfo only: never execute mount, sudo or host helpers.
                with patch.object(Path, 'read_text', mountinfo):
                    self.rejected_without_payload_deletion()
                self.assertEqual(self.content, self.payload.read_bytes())

    def test_wrong_marker_and_nonprivate_root_mode_refuse_cleanup(self):
        self.folder.chmod(0o755)
        self.rejected_without_payload_deletion()
        self.folder.chmod(0o700)
        old_folder = self.folder
        wrong = self.data / 'not-an-owned-overwrite-marker'
        old_folder.rename(wrong)
        self.folder = wrong
        self.tree = wrong / 'target'
        self.payload = self.tree / '中文.bin'
        self.rejected_without_payload_deletion(folder=wrong)
        self.assertEqual(self.content, self.payload.read_bytes())


if __name__ == '__main__':
    unittest.main()
