"""Real ACL captures and read-only history semantics on isolated Linux files.

Container binding/mount/UUID providers are explicit fixtures, not installed RPC.
"""
import copy
import errno
import json
import os
import stat
import unittest
from unittest.mock import patch

import test_container_file_acl as single
from test_container_file_staging import load

module = load('whaledeck-container-acl-history.py', 'acl_history_reader_candidate')


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class AclHistoryTests(unittest.TestCase):
    def setUp(self):
        self.base = single.LinuxContainerAclTests()
        self.base.setUp()
        self.addCleanup(self.base.doCleanups)
        self.reader = module.ContainerAclHistoryReader(self.base.engine)
        self.session = self.base.session

    def listing(self, **extra):
        return {**self.session.request, 'path': self.base.leaf.name, 'after': '', 'take': 50, 'expectedVersion': '', **extra}

    def capture(self, command=None):
        request = self.base.request(command)
        result = self.base.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Completed')
        return request, result

    def read(self, request, result, **extra):
        return {**self.session.request, 'path': request['plan']['command']['path'], 'captureId': request['operationId'],
            'expectedHistoryDigest': result['historyDigest'], **extra}

    def snapshot(self):
        return {str(p): (self.base.r.fingerprint(p.lstat()), None if p.is_dir() else p.read_bytes())
            for root in (self.base.root, self.base.state, self.base.backup) if root.exists()
            for p in [root, *root.rglob('*')] if not p.is_symlink()}

    def test_missing_acl_namespace_is_empty_without_creating_any_directory(self):
        before = self.snapshot()
        result = self.reader.listing(self.listing(), self.session)
        self.assertEqual(result['entries'], [])
        self.assertTrue(result['complete'])
        self.assertFalse(self.base.backup.exists())
        self.assertEqual(self.snapshot(), before)

    def test_original_access_snapshot_is_verified_not_the_new_target_and_reads_never_write(self):
        request, receipt = self.capture()
        before = self.snapshot()
        with patch.object(os, 'setxattr', side_effect=AssertionError('read must not set ACL')), \
             patch.object(os, 'fchmod', side_effect=AssertionError('read must not chmod')), \
             patch.object(self.base.f, 'save_receipt', side_effect=AssertionError('read must not journal')):
            result = self.reader.read(self.read(request, receipt), self.session)
            page = self.reader.listing(self.listing(), self.session)
        self.assertEqual(result['accessAcl'], single.acl.base_acl(0o640))
        self.assertNotEqual(result['accessAcl'], single.named_acl())
        self.assertEqual(result['defaultAcl'], [])
        self.assertEqual(len(page['entries']), 1)
        self.assertEqual(page['entries'][0]['historyDigest'], receipt['historyDigest'])
        for forbidden in ('owner', 'plan', 'uid', 'gid', 'targetProof', 'sourceIdentity'):
            self.assertNotIn(forbidden, result)
            self.assertNotIn(forbidden, page['entries'][0])
        self.assertEqual(before, self.snapshot())
        result['accessAcl'][0]['permissions'] = 0
        self.assertEqual(self.reader.read(self.read(request, receipt), self.session)['accessAcl'], single.acl.base_acl(0o640))

    def test_directory_default_history_keeps_both_old_acls(self):
        folder = self.base.root / 'directory'
        folder.mkdir(mode=0o750)
        folder.chmod(0o750)
        first, _ = self.capture(self.base.command('directory', 'default'))
        second, receipt = self.capture(self.base.command('directory', 'default', []))
        result = self.reader.read(self.read(second, receipt), self.session)
        self.assertEqual(result['defaultAcl'], single.named_acl())
        self.assertEqual(result['accessAcl'], single.acl.base_acl(0o750))
        self.assertEqual(result['changedAclType'], 'default')

    def test_same_container_old_instance_can_be_read_under_current_pinned_session(self):
        request, receipt = self.capture()
        old = request['plan']['command']['instanceId']
        self.session.request['instanceId'] = str(os.getpid()) + ':2026-11-03T00:00:00.123456789Z'
        result = self.reader.read(self.read(request, receipt), self.session)
        self.assertEqual(result['sourceInstanceId'], old)
        self.assertEqual(result['instanceId'], self.session.request['instanceId'])

    def test_wrong_path_other_container_or_wrong_expected_digest_returns_no_snapshot(self):
        request, receipt = self.capture()
        for change in ({'path': 'another'}, {'expectedHistoryDigest': 'F' * 64}):
            with self.subTest(change=change), self.assertRaises(self.base.r.Rejected):
                self.reader.read(self.read(request, receipt, **change), self.session)
        # Create another valid capture in a distinct CID, not a fake matcher.
        original = self.session.request['containerId']
        self.session.request['containerId'] = 'd' * 64
        try: other, other_receipt = self.capture()
        finally: self.session.request['containerId'] = original
        with self.assertRaisesRegex(self.base.r.Rejected, 'NOT_FOUND'):
            self.reader.read(self.read(other, other_receipt), self.session)
        ids = [v['captureId'] for v in self.reader.listing(self.listing(), self.session)['entries']]
        self.assertEqual(ids, [request['operationId']])

    def test_exact_pagination_has_no_duplicate_and_requires_the_inventory_version(self):
        captures = {self.capture()[0]['operationId'] for _ in range(4)}
        after = ''; version = ''; actual = []
        for _ in range(4):
            page = self.reader.listing(self.listing(after=after, take=1, expectedVersion=version), self.session)
            version = page['version']
            actual += [value['captureId'] for value in page['entries']]
            if page['complete']: break
            after = page['nextAfter']
        self.assertEqual(actual, sorted(captures))
        self.assertEqual(len(actual), len(set(actual)))
        with self.assertRaises(self.base.r.Rejected): self.reader.listing(self.listing(after=actual[0]), self.session)

    def test_append_and_in_place_change_both_invalidate_an_older_cursor(self):
        request, _ = self.capture()
        first = self.reader.listing(self.listing(), self.session)
        self.capture()
        with self.assertRaisesRegex(self.base.r.Rejected, 'VERSION_CONFLICT'):
            self.reader.listing(self.listing(expectedVersion=first['version']), self.session)
        fresh = self.reader.listing(self.listing(), self.session)
        path = self.base.history_file(request)
        raw = path.read_bytes(); path.write_bytes(raw.replace(b'ContainerAclMetadata', b'ContainerAclMetadatA'))
        with self.assertRaisesRegex(self.base.r.Rejected, 'VERSION_CONFLICT'):
            self.reader.listing(self.listing(expectedVersion=fresh['version']), self.session)

    def test_corrupt_digest_or_plan_never_becomes_a_valid_snapshot(self):
        request, receipt = self.capture()
        p = self.base.history_file(request)
        original = p.read_bytes()
        for field in ('digest', 'plan', 'owner', 'resource', 'extra'):
            value = json.loads(original)
            if field == 'digest': value['historyDigest'] = '0' * 64
            elif field == 'plan': value['plan']['accessAcl'][0]['permissions'] = 7
            elif field == 'owner': value['owner'] = 'invalid'
            elif field == 'resource': value['resourceType'] = 'ContainerPermissionMetadata'
            else: value['unexpected'] = 'never return'
            p.write_text(json.dumps(value))
            with self.subTest(field=field), self.assertRaises((self.base.r.Rejected, self.base.f.Rejected)):
                self.reader.read(self.read(request, receipt), self.session)
            listing = self.reader.listing(self.listing(), self.session)
            self.assertEqual(listing['entries'], [])
            self.assertEqual(listing['invalidCount'], 1)
        p.write_bytes(original)

    def test_metadata_links_permissions_and_oversize_fail_closed(self):
        request, receipt = self.capture()
        p = self.base.history_file(request); raw = p.read_bytes()
        for kind in ('symlink', 'hardlink', 'mode', 'oversize'):
            p.unlink()
            if kind == 'symlink': p.symlink_to(self.base.leaf)
            elif kind == 'hardlink': os.link(self.base.leaf, p)
            else: p.write_bytes(raw if kind == 'mode' else b' ' * 16385); p.chmod(0o644 if kind == 'mode' else 0o600)
            with self.subTest(kind=kind), self.assertRaises((self.base.r.Rejected, self.base.f.Rejected, OSError)):
                self.reader.read(self.read(request, receipt), self.session)
            self.assertEqual(self.reader.listing(self.listing(), self.session)['entries'], [])
        p.unlink(); p.write_bytes(raw); p.chmod(0o600)

    def test_hdd_attestation_or_mount_failure_never_returns_empty_history(self):
        for error in (self.base.f.Rejected('FILES_BACKUP_DEVICE_MISMATCH'), OSError(errno.EIO, 'fixture I/O')):
            with patch.object(self.base.disk.engine, 'attest', side_effect=error), self.assertRaises(type(error)):
                self.reader.listing(self.listing(), self.session)

    def test_invalid_request_bounds_and_protected_path_reject(self):
        for change in ({'take': True}, {'take': 0}, {'take': 51}, {'after': '../x'}, {'expectedVersion': 'x'}, {'path': ''}, {'path': '../x'}, {'path': 'proc/self'}, {'path': '.env'}, {'extra': 'x'}):
            with self.subTest(change=change), self.assertRaises((self.base.r.Rejected, self.base.f.Rejected)):
                self.reader.listing(self.listing(**change), self.session)

    def test_scan_budget_advances_even_when_records_do_not_match(self):
        for _ in range(3): self.capture()
        with patch.object(module, 'SCAN_LIMIT', 1), patch.object(self.reader, 'matches', return_value=False):
            first = self.reader.listing(self.listing(), self.session)
            self.assertFalse(first['complete']); self.assertEqual(first['scannedCount'], 1); self.assertEqual(first['entries'], [])
            second = self.reader.listing(self.listing(after=first['nextAfter'], expectedVersion=first['version']), self.session)
            self.assertGreater(second['nextAfter'], first['nextAfter'])

    def test_inventory_mutation_during_read_is_rejected_instead_of_partial_success(self):
        self.capture(); original = self.reader.metadata
        def changed(*args, **kwargs):
            value = original(*args, **kwargs)
            (self.base.backup / ('e' * 32 + '.json')).write_text('{}')
            return value
        with patch.object(self.reader, 'metadata', side_effect=changed), self.assertRaisesRegex(self.base.r.Rejected, 'VERSION_CONFLICT'):
            self.reader.listing(self.listing(), self.session)

    def test_session_revocation_before_or_after_read_returns_no_private_result(self):
        request, receipt = self.capture()
        for point in (1, 2):
            calls = [0]
            def current():
                calls[0] += 1
                if calls[0] == point: raise self.base.r.Rejected('SESSION_REVOKED')
            with patch.object(self.session, 'current', side_effect=current), self.assertRaisesRegex(self.base.r.Rejected, 'SESSION_REVOKED'):
                self.reader.read(self.read(request, receipt), self.session)

    def test_record_and_response_budgets_fail_without_any_mutation(self):
        self.capture(); before = self.snapshot()
        with patch.object(module, 'MAX_RECORDS', 0), self.assertRaisesRegex(self.base.r.Rejected, 'LIMIT'):
            self.reader.listing(self.listing(), self.session)
        with patch.object(module, 'RESPONSE_BYTES', 1), self.assertRaisesRegex(self.base.r.Rejected, 'RESPONSE_LIMIT'):
            self.reader.listing(self.listing(), self.session)
        self.assertEqual(before, self.snapshot())


if __name__ == '__main__': unittest.main()
