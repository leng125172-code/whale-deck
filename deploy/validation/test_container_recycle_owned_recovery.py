"""Real temp inode rename/no-overwrite proof; no installed or Docker mutation."""
import copy
import errno
import os
from pathlib import Path
import tempfile
import unittest
import uuid
from recover_container_recycle_owned_fixture import return_inode, rename_noreplace, moved_proof
from container_recycle_agent_io import PRIVATE, physical


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class OwnedRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup); self.root = Path(self.temp.name)
        self.private = self.root/PRIVATE; self.private.mkdir(mode=0o700); self.op = uuid.uuid4().hex
        self.retained = self.private/(self.op+'.trash'); self.retained.write_bytes(b'known original bytes\x00\xff'); self.retained.chmod(0o640); os.chown(self.retained, 1000, 1000); os.setxattr(self.retained, 'user.original', b'raw\x00metadata')
        self.before = physical(self.root, self.op)

    def test_original_inode_bytes_and_attributes_return_without_copy(self):
        after = return_inode(self.root, self.op, self.before)
        self.assertIsNone(after['retained']); self.assertEqual(after['source']['identity'][:8], self.before['retained']['identity'][:8]); self.assertEqual(after['source']['attributes'], self.before['retained']['attributes'])

    def test_atomic_no_replace_preserves_both_files_when_target_appears(self):
        target = self.root/'recycle-rpc.bin'; target.write_bytes(b'late destination')
        source = os.open(self.private, os.O_RDONLY | os.O_DIRECTORY); destination = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        try:
            with self.assertRaises(OSError) as caught: rename_noreplace(source, self.retained.name, destination, target.name)
            self.assertEqual(caught.exception.errno, errno.EEXIST)
        finally: os.close(source); os.close(destination)
        self.assertEqual(target.read_bytes(), b'late destination'); self.assertEqual(self.retained.read_bytes(), b'known original bytes\x00\xff')

    def test_changed_bytes_inode_or_metadata_refuse_before_move(self):
        for field in ('sha256', 'attributes', 'identity'):
            expected = copy.deepcopy(self.before)
            if field == 'sha256': expected['retained'][field] = 'F'*64
            elif field == 'attributes': expected['retained'][field] = []
            else: expected['retained'][field][1] += 1
            with self.subTest(field=field), self.assertRaises(AssertionError): return_inode(self.root, self.op, expected)
            self.assertTrue(self.retained.exists()); self.assertFalse((self.root/'recycle-rpc.bin').exists())

    def test_unrelated_private_entry_or_symlink_or_hardlink_refuses(self):
        extra = self.private/'not-owned'; extra.write_bytes(b'other')
        with self.assertRaises(AssertionError): return_inode(self.root, self.op, self.before)
        extra.unlink(); linked = self.root/'hardlink'; os.link(self.retained, linked)
        with self.assertRaises(AssertionError): return_inode(self.root, self.op, self.before)
        linked.unlink(); self.retained.unlink(); self.retained.symlink_to('/etc/passwd')
        with self.assertRaises(OSError): return_inode(self.root, self.op, self.before)

    def test_restored_proof_cannot_certify_different_inode_or_modified_attribute(self):
        after = return_inode(self.root, self.op, self.before)
        for field in ('identity', 'attributes', 'sha256'):
            bad = copy.deepcopy(after['source'])
            if field == 'identity': bad[field][1] += 1
            elif field == 'attributes': bad[field] = []
            else: bad[field] = 'F'*64
            with self.subTest(field=field), self.assertRaises(AssertionError): moved_proof(self.before['retained'], bad)
