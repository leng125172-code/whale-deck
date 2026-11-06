"""Actual temporary FD/raw-record joins; explicit namespace/HDD doubles."""
import ast
import copy
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import container_recycle_archive_agent_io as io
import test_container_recycle_archive_rpc_proof as fixture
from test_container_recycle_purge_plan import snapshot


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveAcquisitionTests(unittest.TestCase):
    def setUp(self):
        self.f=fixture.ArchiveRecordJoinTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.engine=self.f.engine;self.q=self.f.q;self.native=self.f.f;self.session=self.native.session;self.folder=self.native.work
        self.baseline=dict(sourceRoot=self.f.source,root=self.f.purge,marker=self.f.marker,history=self.f.history,data=self.f.data)
        for change in (patch.object(io,'STATE',self.f.state),patch.object(io,'CLAIMS',self.engine.restore.claims_path),patch.object(io,'installed',return_value=SimpleNamespace(storage=self.native.base.disk.engine,files=self.native.base.f))):change.start();self.addCleanup(change.stop)

    def protected(self):return [snapshot(p) for p in (self.native.base.state,self.native.state,self.native.base.target,self.f.state,self.engine.restore.claims_path,self.folder)]

    def capture(self,r,name):return io.read_operation(self.q,r,self.baseline,self.native.base.disk.disk/name,create=True)

    def test_real_reserved_and_new_inode_capture_never_mutates_original_states(self):
        r=self.engine.reserve(self.q,self.session);before=self.protected();records=self.capture(r,'reserved');value=io.physical(self.folder,self.q);self.assertFalse(io.physical_proof(value,self.native.original,self.q,records,r)['newInodeProven']);self.assertEqual(before,self.protected())
        r=self.engine.apply(self.q,self.session);before=self.protected();records=self.capture(r,'completed');value=io.physical(self.folder,self.q);self.assertTrue(io.physical_proof(value,self.native.original,self.q,records,r)['newInodeProven']);self.assertEqual(before,self.protected())
        io.read_operation(self.q,r,self.baseline,self.native.base.disk.disk/'completed');self.assertEqual(before,self.protected())

    def test_old_private_absence_is_observed_without_creating_or_adopting_it(self):
        private=self.folder/io.source_io.PRIVATE;private.rmdir();self.q={**self.q,'plan':self.engine.plan(self.q['plan']['command'],self.session)};self.assertIsNone(self.q['plan']['privateIdentity'])
        r=self.engine.reserve(self.q,self.session);records=self.capture(r,'no-old-private');value=io.physical(self.folder,self.q);self.assertIsNone(value['oldPrivateIdentity']);io.physical_proof(value,self.native.original,self.q,records,r);self.assertFalse(private.exists())
        r=self.engine.apply(self.q,self.session);records=self.capture(r,'copied-no-old-private');io.physical_proof(io.physical(self.folder,self.q),self.native.original,self.q,records,r);self.assertFalse(private.exists())

    def test_unsafe_root_or_claim_registry_cannot_be_read(self):
        r=self.engine.reserve(self.q,self.session);self.f.state.chmod(0o755)
        with self.assertRaises(AssertionError):self.capture(r,'unsafe-root')
        self.f.state.chmod(0o700);self.engine.restore.claims_path.chmod(0o755)
        with self.assertRaises(AssertionError):self.capture(r,'unsafe-claim')
        self.engine.restore.claims_path.chmod(0o700)

    def test_missing_source_journal_does_not_turn_into_optional_absence(self):
        r=self.engine.reserve(self.q,self.session);source=self.engine.source.state_path/(self.engine.ref(self.q)['operationId']+'.json');raw=source.read_bytes();source.unlink()
        try:
            with self.assertRaises(FileNotFoundError):self.capture(r,'source-missing')
        finally:source.write_bytes(raw);source.chmod(0o600)

    def test_source_replacement_hardlink_and_unexpected_stage_entries_fail(self):
        r=self.engine.reserve(self.q,self.session);self.engine.apply(self.q,self.session);r=self.engine.inspect(self.q,self.session);records=self.capture(r,'before-foreign')
        stage=self.folder/(io.STAGE_PREFIX+self.q['operationId']);(stage/'foreign').write_bytes(b'preserve')
        with self.assertRaises(AssertionError):io.physical(self.folder,self.q)
        (stage/'foreign').unlink();leaf=self.folder/'recycle-rpc.bin';held=self.folder/'held';leaf.rename(held);leaf.write_bytes(held.read_bytes());leaf.chmod(0o640)
        with self.assertRaises(AssertionError):io.physical_proof(io.physical(self.folder,self.q),self.native.original,self.q,records,r)
        leaf.unlink();held.rename(leaf);os.link(leaf,self.folder/'alias')
        with self.assertRaises(AssertionError):io.physical(self.folder,self.q)

    def test_raw_archive_changed_since_capture_or_current_hdd_corruption_rejects(self):
        r=self.engine.reserve(self.q,self.session);folder=self.native.base.disk.disk/'saved';io.read_operation(self.q,r,self.baseline,folder,create=True);raw=folder/'archive-root.json';raw.write_bytes(raw.read_bytes()+b' ')
        with self.assertRaises(AssertionError):io.read_operation(self.q,r,self.baseline,folder)
        data=self.native.base.target/(self.engine.ref(self.q)['operationId']+'.data');data.write_bytes(b'corrupt')
        with self.assertRaises(Exception):self.capture(r,'corrupted-data')

    def test_unknown_never_becomes_an_automatic_physical_success(self):
        r=self.engine.reserve(self.q,self.session);records=self.capture(r,'unknown-physical');r={**r,'state':'Unknown','archiveCopied':None}
        with self.assertRaisesRegex(AssertionError,'UNKNOWN_REQUIRES'):io.physical_proof(io.physical(self.folder,self.q),self.native.original,self.q,records,r)

    def test_exact_cleanup_removes_only_new_copy_and_keeps_durable_history_and_claim(self):
        from validate_container_recycle_archive_agent import cleanup_new_copy
        self.engine.reserve(self.q,self.session);r=self.engine.apply(self.q,self.session);records=self.capture(r,'known-cleanup');value=io.physical(self.folder,self.q);io.physical_proof(value,self.native.original,self.q,records,r)
        root=(self.f.state/(self.q['operationId']+'.json')).read_bytes();claim=(self.engine.restore.claims_path/(self.engine.ref(self.q)['operationId']+'.json')).read_bytes();cleanup_new_copy(self.folder,self.q,value['source'],records['root'])
        self.assertFalse(self.native.leaf.exists());self.assertFalse((self.folder/(io.STAGE_PREFIX+self.q['operationId'])).exists());self.assertEqual((self.f.state/(self.q['operationId']+'.json')).read_bytes(),root);self.assertEqual((self.engine.restore.claims_path/(self.engine.ref(self.q)['operationId']+'.json')).read_bytes(),claim)
        r=self.engine.inspect(self.q,self.session);self.assertTrue(r['archiveCopied']);self.assertFalse(r['bindingCurrent'])

    def test_wrong_cleanup_leaf_or_stage_identity_refuses_before_target_deletion(self):
        from validate_container_recycle_archive_agent import cleanup_new_copy
        self.engine.reserve(self.q,self.session);r=self.engine.apply(self.q,self.session);records=self.capture(r,'wrong-cleanup');value=io.physical(self.folder,self.q);expected=copy.deepcopy(value['source']);expected['identity'][1]+=1
        with self.assertRaises(AssertionError):cleanup_new_copy(self.folder,self.q,expected,records['root'])
        wrong=copy.deepcopy(records['root']);wrong['privateIdentity'][1]+=1
        with self.assertRaises(AssertionError):cleanup_new_copy(self.folder,self.q,value['source'],wrong)
        self.assertEqual(io.source_io.file_snapshot(self.native.leaf),value['source'])


class ArchiveIoScopeTests(unittest.TestCase):
    def test_acquisition_has_no_mutation_engine_rpc_or_process_control_imports(self):
        path=Path(io.__file__);tree=ast.parse(path.read_bytes());modules=[]
        for node in ast.walk(tree):
            if isinstance(node,ast.Import):modules.extend(n.name for n in node.names)
            elif isinstance(node,ast.ImportFrom):modules.append(node.module or '')
        self.assertFalse(any(n in ('subprocess','signal') or 'validate_container' in n or 'archive_copies' in n for n in modules));self.assertIn('read_only=True',path.read_text());self.assertIn('O_NOFOLLOW',path.read_text())


if __name__=='__main__':unittest.main()
