"""Kernel and file authorization boundaries of the QA-only chmod observer."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch
import uuid

import bound_container_permission_cut as cut


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class PermissionFaultScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-permission-cut-test-'); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.source = self.root / 'source'; self.source.mkdir()
        self.file = self.source / 'original.bin'; self.file.write_bytes(b'preserved'); self.file.chmod(0o640)
        self.operation = uuid.uuid4().hex; self.owner = 'B' * 64
        old = cut.fingerprint(self.file.stat())
        self.plan = {'command': {'containerId': 'a' * 64, 'instanceId': '100:2026-10-30T01:00:00Z', 'path': 'work/original.bin', 'mode': '0600', 'expectedVersion': 'E' * 64},
            'sourceIdentity': old, 'parentIdentity': list(cut.identity(self.source)), 'kind': 'File', 'targetProof': 'T' * 64,
            'attributesDigest': 'D' * 64, 'noChange': False}
        self.record = {'operationId': self.operation, 'owner': self.owner, 'plan': self.plan, 'state': 'Prepared',
            'modeConfirmed': None, 'historyMayExist': True, 'historyVerified': True}
        self.row = {'execution_job_id': str(uuid.UUID(self.operation)), 'owner_hash': self.owner, 'container_id': 'a' * 64,
            'instance_id': self.plan['command']['instanceId'], 'relative_path': 'work/original.bin'}
        self.managed = {'Command': {'Mode': '0600', 'ExpectedVersion': 'E' * 64}, 'SourceIdentity': dict(zip(
            ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs'), old[:8])),
            'SourceChangedAtUnixNs': old[8], 'Kind': 'File', 'TargetProof': 'T' * 64, 'AttributesDigest': 'D' * 64, 'NoChange': False}
        self.bind()

    def bind(self):
        digest = hashlib.sha256(b'whaledeck-container-permission-plan-v1\n' + json.dumps({k: v for k, v in self.plan.items() if k != 'fingerprint'},
            ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
        self.plan['fingerprint'] = self.managed['Fingerprint'] = digest
        self.row['plan_json'] = json.dumps(self.managed)
        self.path = self.root / (self.operation + '.json'); self.path.write_text(json.dumps(self.record)); self.path.chmod(0o600)

    def test_full_permanent_root_plan_and_sql_binding_are_required(self):
        with patch.object(cut, 'ROOT', self.root):
            self.assertEqual(cut.record_for(self.row), self.record)
            for key, value in [('owner_hash', 'C' * 64), ('container_id', 'b' * 64), ('relative_path', 'work/foreign'), ('instance_id', '200:other')]:
                with self.subTest(key=key), self.assertRaises(AssertionError): cut.record_for({**self.row, key: value})

    def test_changed_raw_plan_fingerprint_cannot_authorize_signal(self):
        self.plan['command']['mode'] = '0777'; self.path.write_text(json.dumps(self.record))
        with patch.object(cut, 'ROOT', self.root), self.assertRaises(AssertionError): cut.record_for(self.row)

    def test_sql_source_owner_and_ctime_must_match_raw_plan(self):
        with patch.object(cut, 'ROOT', self.root):
            for key in ('OwnerUid', 'ModifiedAtUnixNs'):
                m = json.loads(self.row['plan_json']); m['SourceIdentity'][key] += 1
                with self.subTest(key=key), self.assertRaises(AssertionError): cut.record_for({**self.row, 'plan_json': json.dumps(m)})
            m = json.loads(self.row['plan_json']); m['SourceChangedAtUnixNs'] += 1
            with self.assertRaises(AssertionError): cut.record_for({**self.row, 'plan_json': json.dumps(m)})

    def test_only_original_pre_mode_inode_is_a_counted_cut(self):
        proof = cut.original_proof(self.record, self.source)
        self.assertEqual(proof['originalIdentity'], cut.fingerprint(self.file.stat()))
        self.file.chmod(0o600)
        with self.assertRaisesRegex(AssertionError, 'SOURCE_CHANGED'): cut.original_proof(self.record, self.source)

    def test_applying_completed_aborted_unknown_are_not_pre_mode_cuts(self):
        for phase in ('Applying', 'Completed', 'Aborted', 'Unknown'):
            with self.subTest(phase=phase): self.assertIsNone(cut.original_proof({**self.record, 'state': phase}, self.source))

    def test_named_symlink_or_replaced_parent_cannot_authorize_signal(self):
        old = self.source / 'old'; self.file.rename(old); self.file.symlink_to(old)
        with self.assertRaises(AssertionError): cut.original_proof(self.record, self.source)
        self.file.unlink(); old.rename(self.file); self.plan['parentIdentity'] = [1, 2]
        with self.assertRaises(AssertionError): cut.original_proof(self.record, self.source)

    def test_path_must_be_one_owned_leaf_not_an_arbitrary_host_path(self):
        for path in ('/etc/shadow', 'work/../other', 'work/nested/file', 'original.bin'):
            with self.subTest(path=path), self.assertRaises(AssertionError):
                changed = {**self.record, 'plan': {**self.plan, 'command': {**self.plan['command'], 'path': path}}}
                cut.original_proof(changed, self.source)

    def test_foreign_process_holding_both_fds_still_requires_exact_transport(self):
        lock = self.root / 'lock'; lock.touch(mode=0o600)
        with lock.open('rb') as stream:
            root_fd = os.open(self.source, os.O_RDONLY | os.O_DIRECTORY)
            try:
                child = subprocess.Popen(['/bin/sleep', '5'], pass_fds=(root_fd, stream.fileno()))
                try: self.assertFalse(cut.bound_fds(child.pid, cut.identity(self.source), cut.identity(lock)))
                finally: child.terminate(); child.wait(timeout=2)
            finally: os.close(root_fd)

    def test_scope_refuses_non_owned_source_before_pidfd_access(self):
        with patch.object(cut.os, 'pidfd_open', side_effect=AssertionError('must not open pidfd')) as opened:
            with self.assertRaises(AssertionError): cut.freeze_before_mode(self.row, self.source, 100)
            opened.assert_not_called()

    def test_lock_symlink_nonempty_file_wrong_mode_and_hardlink_are_refused(self):
        lock = self.root / 'lock'; lock.touch(mode=0o600); self.assertEqual(cut.operation_lock(lock), cut.identity(lock))
        lock.chmod(0o644)
        with self.assertRaises(AssertionError): cut.operation_lock(lock)
        lock.chmod(0o600); lock.write_bytes(b'x')
        with self.assertRaises(AssertionError): cut.operation_lock(lock)
        lock.write_bytes(b''); os.link(lock, self.root / 'linked')
        with self.assertRaises(AssertionError): cut.operation_lock(lock)
        link = self.root / 'symlink'; link.symlink_to(lock)
        with self.assertRaises(AssertionError): cut.operation_lock(link)

    def test_atomic_replaced_receipt_is_not_a_signal_authorization(self):
        original_read = os.read
        def replace(fd, size):
            result = original_read(fd, size); new = self.root / 'new'; new.write_bytes(self.path.read_bytes()); new.chmod(0o600); new.replace(self.path)
            return result
        with patch.object(cut.os, 'read', side_effect=replace): self.assertIsNone(cut.receipt_json(self.path))

    def test_actual_pidfd_exit_is_measured_before_a_longer_service_drain(self):
        child = subprocess.Popen(['/bin/sleep', '5']); held = cut.HeldPermission(child.pid, os.pidfd_open(child.pid), 'test', {}, {})
        try:
            began = time.monotonic()
            with ThreadPoolExecutor(max_workers=1) as pool:
                observe = pool.submit(held.wait_exit_seconds, began, 2)
                child.terminate(); child.wait(timeout=2); elapsed = observe.result(timeout=2); time.sleep(0.03)
            self.assertIsNotNone(elapsed); self.assertLess(elapsed, time.monotonic() - began)
        finally:
            if child.poll() is None: child.kill(); child.wait(timeout=2)
            held.release()


if __name__ == '__main__': unittest.main()
