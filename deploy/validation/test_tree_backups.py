"""Real Linux files, explicit synthetic HDD identity fixture. No physical-HDD claim."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import signal
import unittest
import uuid
from unittest.mock import patch
from types import SimpleNamespace
import test_file_transport as common
from test_file_tree_manifest import tree_module
from test_tree_archive_codec import codec
from test_file_archives import archives

spec = importlib.util.spec_from_file_location('tree_backups',Path(__file__).resolve().parents[1]/'host/whaledeck-tree-backups.py')
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0,'requires disposable real Linux root I/O')
class TreeBackupTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.source = self.fixture.root/'source'; self.source.mkdir(mode=0o750)
        (self.source/'empty').mkdir(); (self.source/'代码').mkdir()
        self.content = bytes(range(256))*257+b'\x00\xff'
        self.file = self.source/'代码'/'file.bin'; self.file.write_bytes(self.content)
        self.backups = self.fixture.backups.engine
        self.engine = module.TreeBackups(driver,self.backups,tree_module.FileTreeManifest,codec.TreeArchive)
        self.folder = self.fixture.backups.target/module.TreeBackups.FOLDER

    def tearDown(self): self.fixture.tearDown()

    def request(self):
        source = {'rootId':'test.shared','path':'source','expectedVersion':driver.version(self.source.stat())}
        manifest = tree_module.FileTreeManifest(driver).scan(source)
        return {'operationId':uuid.uuid4().hex,'owner':'A'*64,'source':source,'manifestSha256':manifest['manifestSha256'],'captureKind':'Trash'}

    def path(self,request,suffix): return self.folder/(request['operationId']+suffix)

    def saved(self,request): return json.loads(self.path(request,'.json').read_bytes())

    def source_proof(self): return (driver.version(self.source.stat()),driver.version(self.file.stat()),self.file.read_bytes())

    def test_complete_archive_has_verified_receipt_manifest_full_hash_and_unchanged_source(self):
        request = self.request(); before = self.source_proof(); result = self.engine.ensure(request)
        self.assertEqual(result['state'],'Verified'); self.assertEqual(result['entryCount'],4)
        self.assertEqual(result['totalBytes'],len(self.content)); self.assertFalse(result['differentSourceDevice'])
        archive = self.path(request,'.tar.gz')
        self.assertEqual(result['archiveSha256'],hashlib.sha256(archive.read_bytes()).hexdigest().upper())
        self.assertEqual(stat.S_IMODE(archive.stat().st_mode),0o600)
        self.assertEqual((stat.S_IMODE(self.folder.stat().st_mode),self.folder.stat().st_uid),(0o700,0))
        self.assertFalse(self.path(request,'.partial').exists())
        self.assertEqual(self.saved(request)['state'],'Verified'); self.assertEqual(self.source_proof(),before)
        with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must-not-recopy')):
            self.assertEqual(self.engine.ensure(request),result)

    def test_verified_capture_can_be_observed_after_original_was_removed_without_recopier(self):
        request = self.request(); result = self.engine.ensure(request)
        self.file.unlink(); (self.source/'代码').rmdir(); (self.source/'empty').rmdir(); self.source.rmdir()
        with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must-not-recopy')):
            self.assertEqual(self.engine.ensure(request),result)
        self.assertFalse(self.source.exists())

    def test_owner_reuse_and_changed_intent_are_denied_without_modifying_archive(self):
        request = self.request(); self.engine.ensure(request)
        before = self.path(request,'.tar.gz').read_bytes()
        with self.assertRaisesRegex(driver.Rejected,'^FILES_OPERATION_NOT_FOUND$'):
            self.engine.ensure({**request,'owner':'B'*64})
        with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_BACKUP_IDEMPOTENCY_CONFLICT$'):
            self.engine.ensure({**request,'captureKind':'Overwrite'})
        self.assertEqual(self.path(request,'.tar.gz').read_bytes(),before)

    def test_preparing_manifest_and_ready_interruptions_remain_incomplete_without_retry(self):
        for phase in ('Preparing','ManifestStored','ArchiveCreated','Ready'):
            with self.subTest(phase=phase):
                request = self.request(); before = self.source_proof()
                def interrupt(value):
                    if value == phase: raise RuntimeError('synthetic-interruption')
                self.engine.checkpoint = interrupt
                with self.assertRaises(RuntimeError): self.engine.ensure(request)
                partial = self.path(request,'.partial')
                prior = partial.read_bytes() if partial.exists() else None
                self.engine.checkpoint = lambda _:None
                with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must-not-recopy')):
                    with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_BACKUP_INCOMPLETE$'): self.engine.ensure(request)
                self.assertEqual(partial.read_bytes() if partial.exists() else None,prior)
                self.assertEqual(self.source_proof(),before)

    def test_crash_after_publication_recognizes_exact_archive_and_only_finishes_receipt(self):
        request = self.request(); before = self.source_proof()
        def interrupt(phase):
            if phase == 'Published': raise RuntimeError('synthetic-interruption')
        self.engine.checkpoint = interrupt
        with self.assertRaises(RuntimeError): self.engine.ensure(request)
        self.assertEqual(self.saved(request)['state'],'Ready')
        identity = self.path(request,'.tar.gz').stat().st_ino; payload = self.path(request,'.tar.gz').read_bytes()
        with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must-not-recopy')):
            result = self.engine.ensure(request)
        self.assertEqual(result['state'],'Verified'); self.assertEqual(self.saved(request)['state'],'Verified')
        self.assertEqual(self.path(request,'.tar.gz').stat().st_ino,identity)
        self.assertEqual(self.path(request,'.tar.gz').read_bytes(),payload); self.assertEqual(self.source_proof(),before)

    def test_existing_foreign_final_object_is_never_overwritten_or_adopted(self):
        request = self.request(); self.folder.mkdir(mode=0o700); target = self.path(request,'.tar.gz')
        target.write_bytes(b'external'); target.chmod(0o600); original = target.stat().st_ino
        with self.assertRaises(FileExistsError): self.engine.ensure(request)
        with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_BACKUP_CONTENT_INVALID$'): self.engine.ensure(request)
        self.assertEqual((target.stat().st_ino,target.read_bytes()),(original,b'external'))
        self.assertTrue(self.path(request,'.partial').exists()); self.assertEqual(self.file.read_bytes(),self.content)

    def test_tampered_archive_and_manifest_fail_closed_and_are_preserved(self):
        request = self.request(); self.engine.ensure(request)
        archive = self.path(request,'.tar.gz'); original = archive.read_bytes()
        archive.write_bytes(original[:-8]+b'corrupt!')
        with self.assertRaises(driver.Rejected): self.engine.ensure(request)
        self.assertEqual(archive.read_bytes(),original[:-8]+b'corrupt!'); archive.write_bytes(original)
        manifest = self.path(request,'.manifest.json'); value = json.loads(manifest.read_bytes())
        value['entries'][0]['sourceIdentity'][1] += 1; manifest.write_text(json.dumps(value))
        with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_BACKUP_METADATA_INVALID$'): self.engine.ensure(request)
        self.assertEqual(self.file.read_bytes(),self.content)

    def test_full_history_does_not_block_existing_receipts_and_never_auto_deletes(self):
        configuration = self.backups.configuration(); configuration['maxVersions']=1
        with patch.object(self.backups,'configuration',return_value=configuration):
            request = self.request(); result = self.engine.ensure(request)
            with self.assertRaisesRegex(driver.Rejected,'^FILES_BACKUP_HISTORY_LIMIT$'): self.engine.ensure(self.request())
            self.assertEqual(self.engine.ensure(request),result)
            self.assertTrue(self.path(request,'.tar.gz').exists())

    def test_capacity_failure_keeps_original_and_does_not_start_archive(self):
        request = self.request(); before = self.source_proof()
        full = SimpleNamespace(f_blocks=1000000,f_frsize=4096,f_bavail=0,f_flag=0)
        with patch.object(os,'fstatvfs',return_value=full):
            with self.assertRaisesRegex(driver.Rejected,'^FILES_BACKUP_CAPACITY_REQUIRED$'): self.engine.ensure(request)
        self.assertFalse(self.path(request,'.partial').exists()); self.assertEqual(self.source_proof(),before)

    def test_unattested_store_and_revoked_root_do_not_create_or_read_backup(self):
        request = self.request()
        with patch.object(self.backups,'attest',side_effect=driver.Rejected('FILES_BACKUP_DEVICE_MISMATCH')):
            with self.assertRaisesRegex(driver.Rejected,'^FILES_BACKUP_DEVICE_MISMATCH$'): self.engine.ensure(request)
        self.assertFalse(self.folder.exists())
        self.engine.ensure(request)
        registry = json.loads(self.fixture.registry.read_bytes()); registry[0]['allowedActions'].remove('files-download')
        self.fixture.registry.write_text(json.dumps(registry))
        with self.assertRaisesRegex(driver.Rejected,'^FILES_ROOT_FORBIDDEN$'): self.engine.ensure(request)

    def test_symlink_store_and_hardlinked_archive_are_rejected_without_following_them(self):
        request = self.request(); other = self.fixture.base/'outside'; other.mkdir(mode=0o700)
        self.folder.symlink_to(other,target_is_directory=True)
        with self.assertRaises(OSError): self.engine.ensure(request)
        self.assertEqual(list(other.iterdir()),[]); self.folder.unlink()
        self.engine.ensure(request); os.link(self.path(request,'.tar.gz'),other/'linked')
        with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_FILE_INVALID$'): self.engine.ensure(request)
        self.assertTrue((other/'linked').exists())

    def test_cancel_before_write_keeps_store_and_source_untouched(self):
        request = self.request(); before = self.source_proof()
        engine = module.TreeBackups(driver,self.backups,tree_module.FileTreeManifest,codec.TreeArchive,cancelled=lambda:True)
        with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_CANCELED$'): engine.ensure(request)
        self.assertFalse(self.folder.exists()); self.assertEqual(self.source_proof(),before)

    def test_actual_sigkill_ready_and_published_fences_never_recopy_or_remove_source(self):
        before = self.source_proof()
        for phase in ('Ready','Published'):
            with self.subTest(phase=phase):
                request = self.request()
                def killed(value):
                    if value==phase: os.kill(os.getpid(),signal.SIGKILL)
                self.engine.checkpoint=killed
                child = os.fork()
                if child==0:
                    try: self.engine.ensure(request)
                    finally: os._exit(72)
                _,status = os.waitpid(child,0)
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
                self.engine.checkpoint=lambda _:None
                with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must-not-recopy')):
                    if phase=='Ready':
                        with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_BACKUP_INCOMPLETE$'): self.engine.ensure(request)
                        self.assertTrue(self.path(request,'.partial').exists())
                    else: self.assertEqual(self.engine.ensure(request)['state'],'Verified')
                self.assertEqual(self.source_proof(),before)

    def test_ordinary_file_history_does_not_misinterpret_directory_capture_records(self):
        registry = json.loads(self.fixture.registry.read_bytes()); registry[0]['allowedActions'].append('files-history')
        self.fixture.registry.write_text(json.dumps(registry))
        request = self.request(); self.engine.ensure(request)
        # This is a class-level namespace regression, not proof of the installed
        # root-owned module loader. The public scratch checkout belongs to user.
        history = archives.FileArchives(driver,self.backups).list({'rootId':'test.shared','path':'','after':'','take':50,'expectedVersion':''})
        self.assertEqual(history['entries'],[])
        self.assertTrue(history['complete']); self.assertEqual(history['invalidRecords'],0)


if __name__ == '__main__': unittest.main()
