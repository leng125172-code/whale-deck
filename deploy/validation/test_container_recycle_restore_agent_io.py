"""Trusted temp FD/HDD-format reads and physical proof; not installed host acceptance."""
import copy
import os
import unittest
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_restore_agent_io as io
import test_container_recycle_restore_rpc_proof as fixture


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'root temporary Linux metadata required')
class RestoreIoTests(unittest.TestCase):
    def setUp(self):
        self.f = fixture.RestoreOriginalProofTests(); self.f.setUp(); self.addCleanup(self.f.doCleanups)
        for p in (patch.object(io, 'STATE', self.f.state), patch.object(io.source_io, 'STATE', self.f.base.state), patch.object(io, 'installed', return_value=SimpleNamespace(storage=self.f.base.disk.engine))):
            p.start(); self.addCleanup(p.stop)
        self.folder = self.f.base.disk.disk/'evidence'

    def test_raw_originals_are_archived_and_reread_without_any_new_hdd_history(self):
        f = self.f; result = f.engine.apply(f.q, f.base.session)
        before = sorted(p.name for p in f.base.target.iterdir())
        first = io.read_operation(f.q, result, self.folder, create=True)
        self.assertEqual(first, io.read_operation(f.q, result, self.folder))
        self.assertEqual(set(p.name for p in self.folder.iterdir()), {'restore-root.json', 'source-claim.json', 'source-root.json', 'source-hdd.json'})
        self.assertEqual(before, sorted(p.name for p in f.base.target.iterdir()))
        self.assertEqual((self.folder/'restore-root.json').read_bytes(), (f.state/(f.q['operationId']+'.json')).read_bytes())
        self.assertEqual((self.folder/'source-root.json').read_bytes(), (f.base.state/(f.ref['operationId']+'.json')).read_bytes())

    def test_unclaimed_cancel_archives_claim_absence_and_cannot_normalize_a_changed_archive(self):
        f = self.f; result = f.engine.discard(f.q, f.base.session)
        io.read_operation(f.q, result, self.folder, create=True)
        self.assertEqual((self.folder/'source-claim.json').read_bytes(), b'null')
        (self.folder/'source-root.json').write_bytes(b'{}')
        with self.assertRaises(AssertionError): io.read_operation(f.q, result, self.folder)

    def test_unsafe_directory_record_permissions_and_symlinks_refuse(self):
        f = self.f; result = f.engine.apply(f.q, f.base.session); (f.state/'claims').chmod(0o750)
        with self.assertRaises(AssertionError): io.read_operation(f.q, result, self.folder, create=True)
        (f.state/'claims').chmod(0o700)
        path = f.state/(f.q['operationId']+'.json'); path.chmod(0o640)
        with self.assertRaises(AssertionError): io.read_operation(f.q, result, self.folder)
        path.chmod(0o600); raw = path.read_bytes(); path.unlink(); target = f.state/'original'; target.write_bytes(raw); target.chmod(0o600); path.symlink_to(target)
        with self.assertRaises(OSError): io.read_operation(f.q, result, self.folder)

    def test_physical_return_proof_rejects_other_inode_wrong_attributes_or_lingering_trash(self):
        f = self.f; result = f.engine.apply(f.q, f.base.session)
        records = io.read_operation(f.q, result, self.folder, create=True)
        actual = io.source_io.physical(f.base.leaf.parent, f.ref['operationId'])
        p = f.ref['plan']; original = dict(identity=p['sourceIdentity'], attributes=p['attributes'], sha256=p['sha256'], sizeBytes=p['sourceIdentity'][6])
        self.assertTrue(io.physical_proof(actual, original, records, result)['physicalNamespaceChecked'])
        for field in ('inode', 'attributes', 'trash', 'private'):
            bad = copy.deepcopy(actual)
            if field == 'inode': bad['source']['identity'][1] += 1
            elif field == 'attributes': bad['source']['attributes'] = []
            elif field == 'trash': bad['retained'] = copy.deepcopy(bad['source'])
            else: bad['privateIdentity'][1] += 1
            with self.subTest(field=field), self.assertRaises(AssertionError): io.physical_proof(bad, original, records, result)
