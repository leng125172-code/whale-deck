"""Disposable actual Linux publication/crash tests, not installed RPC/UI proof."""
import contextlib
import base64
import hashlib
import importlib.util
import json
import io
import os
from pathlib import Path
import signal
import shutil
import stat
import tarfile
import unittest
from unittest.mock import patch
import uuid
import sys
import test_file_transport as common
from test_file_tree_manifest import tree_module
from test_tree_archive_codec import codec

spec = importlib.util.spec_from_file_location('tree_packing',Path(__file__).resolve().parents[1]/'host/whaledeck-tree-packing.py')
packing = importlib.util.module_from_spec(spec); spec.loader.exec_module(packing)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0,'requires disposable real Linux root I/O')
class TreePackingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root,self.registry,self.state = self.fixture.root,self.fixture.registry,self.fixture.state
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'] += ['files-mutate','files-archive','files-tree-pack']; self.registry.write_text(json.dumps(policy))
        self.source = self.root/'source'; self.source.mkdir(); (self.source/'空目录').mkdir()
        self.data = bytes(range(256))*8192+b'\x00tail\xff\n'; self.file = self.source/'中文.bin'; self.file.write_bytes(self.data); self.file.chmod(0o751)
        self.engine = packing.FileTreePacking(driver,tree_module.FileTreeManifest,codec.TreeArchive)

    def tearDown(self): self.fixture.tearDown()

    def command(self, archive_format='tar.gz', target=None):
        return {'source':{'rootId':'test.shared','path':'source','expectedVersion':driver.version(self.source.stat())},
            'target':{'rootId':'test.shared','path':target or 'target.'+archive_format},'format':archive_format}

    def request(self, archive_format='tar.gz', target=None):
        return {'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(self.command(archive_format,target))}

    def lookup(self, request): return self.engine.lookup({key:request[key] for key in ('operationId','owner')})

    def receipt(self, request): return self.state/'tree-packing'/(request['operationId']+'.json')

    def pending(self, request): return self.root/driver.STAGING/(request['operationId']+'.tree-pack')

    def source_identity(self): return driver.publication_identity(self.file.stat())+[self.file.stat().st_ctime_ns]

    def test_complete_both_formats_source_bytes_metadata_preserved_and_normal_tar_compatible(self):
        before = self.source_identity()
        for archive_format in ('tar','tar.gz'):
            request = self.request(archive_format); result = self.engine.pack(request); target = self.root/('target.'+archive_format)
            self.assertEqual(result['state'],'Completed'); self.assertFalse(result['partialRetained'])
            self.assertEqual(result['archiveSha256'],hashlib.sha256(target.read_bytes()).hexdigest().upper())
            self.assertEqual(result['archiveBytes'],target.stat().st_size); self.assertEqual(result['totalBytes'],len(self.data)); self.assertEqual(result['entryCount'],3)
            self.assertEqual(stat.S_IMODE(target.stat().st_mode),0o640)
            with tarfile.open(target) as archive:
                self.assertEqual(archive.extractfile('中文.bin').read(),self.data); self.assertTrue(archive.getmember('空目录').isdir())
                self.assertEqual(archive.getmember('中文.bin').mode,0o751)
            self.assertEqual(self.source_identity(),before); self.assertFalse(self.pending(request).exists())
            with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must not recompress')):
                self.assertEqual(self.lookup(request),result); self.assertEqual(self.engine.pack(request),result)

    def test_long_unicode_paths_and_owner_mapping_only_from_registered_policy(self):
        policy = json.loads(self.registry.read_bytes()); policy[0].update(ownerUid=1000,ownerGid=1001); self.registry.write_text(json.dumps(policy))
        long_name = '中文'*40+'.txt'; self.assertLessEqual(len(long_name.encode()),255); (self.source/long_name).write_bytes(b'long')
        request = self.request(); self.engine.pack(request); target = self.root/'target.tar.gz'
        self.assertEqual((target.stat().st_uid,target.stat().st_gid),(1000,1001))
        with tarfile.open(target) as archive: self.assertEqual(archive.extractfile(long_name).read(),b'long')

    def test_existing_file_directory_symlink_and_late_target_never_overwritten(self):
        for kind in ('file','directory','symlink'):
            with self.subTest(kind=kind):
                target = self.root/'target.tar.gz'
                if kind == 'file': target.write_bytes(b'foreign')
                elif kind == 'directory': target.mkdir()
                else: target.symlink_to(self.fixture.base/'outside')
                with self.assertRaises(driver.Rejected): self.request()
                if kind == 'directory': target.rmdir()
                else: target.unlink()
        request = self.request()
        def checkpoint(phase):
            if phase == 'Publishing': (self.root/'target.tar.gz').write_bytes(b'foreign')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(OSError): self.engine.pack(request)
        self.assertEqual((self.root/'target.tar.gz').read_bytes(),b'foreign'); self.assertEqual(self.lookup(request)['state'],'Unknown')
        self.assertTrue(self.pending(request).exists()); self.assertEqual(self.file.read_bytes(),self.data)

    def test_source_changes_and_protected_descendants_reject_before_staging(self):
        request = self.request(); self.file.write_bytes(b'changed')
        with self.assertRaises(driver.Rejected): self.engine.pack(request)
        self.assertFalse(self.pending(request).exists()); self.assertFalse(self.receipt(request).exists())
        (self.source/'.env').write_bytes(b'private')
        with self.assertRaises(driver.Rejected): self.request()

    def test_explicit_source_and_target_permissions_and_policy_binding(self):
        original = self.registry.read_bytes()
        for permission in ('files-download','files-list','files-archive','files-upload','files-mutate','files-tree-pack'):
            with self.subTest(permission=permission):
                request = self.request(); policy = json.loads(original); policy[0]['allowedActions'].remove(permission); self.registry.write_text(json.dumps(policy))
                with self.assertRaises(driver.Rejected): self.engine.pack(request)
                self.assertFalse(self.pending(request).exists()); self.registry.write_bytes(original)

    def test_nested_overlap_alias_roots_and_source_ancestor_targets_are_denied(self):
        for target in ('source/inside.tar.gz','source'):
            with self.subTest(target=target), self.assertRaises(driver.Rejected): self.engine.plan(self.command(target=target))
        policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0],'id':'test.alias'}); self.registry.write_text(json.dumps(policy))
        command = self.command(target='source/alias.tar.gz'); command['target']['rootId']='test.alias'
        with self.assertRaisesRegex(driver.Rejected,'FILES_TREE_OVERLAP'): self.engine.plan(command)

    def test_invalid_command_plan_hash_formats_and_extra_fields_are_denied(self):
        for value in ('zip','TAR','tar.gz\x00'):
            command = self.command(); command['format']=value
            with self.assertRaises(driver.Rejected): self.engine.plan(command)
        command = self.command(target='wrong.zip')
        with self.assertRaises(driver.Rejected): self.engine.plan(command)
        for field in ('fingerprint','manifestSha256','sourcePolicyHash','targetPolicyHash'):
            request = self.request(); request['plan'][field]='B'*64
            with self.assertRaises(driver.Rejected): self.engine.pack(request)
        request = self.request(); request['plan']['shell']='tar'
        with self.assertRaises(driver.Rejected): self.engine.pack(request)

    def test_wrong_owner_and_same_id_other_plan_are_not_adopted(self):
        request = self.request(); self.engine.pack(request)
        wrong = {**request,'owner':'B'*64}
        with self.assertRaisesRegex(driver.Rejected,'FILES_OPERATION_NOT_FOUND'): self.lookup(wrong)
        other = self.request(target='other.tar.gz'); other['operationId']=request['operationId']
        with self.assertRaisesRegex(driver.Rejected,'FILES_IDEMPOTENCY_CONFLICT'): self.engine.pack(other)

    def test_cancel_before_start_and_after_ready_preserves_source_and_does_not_publish(self):
        request = self.request(); self.engine.cancelled=lambda:True
        with self.assertRaisesRegex(driver.Rejected,'FILES_TREE_CANCELED'): self.engine.pack(request)
        self.assertFalse(self.pending(request).exists())
        self.engine.cancelled=None; request=self.request(); before=self.source_identity()
        def checkpoint(phase):
            if phase == 'Ready': self.engine.cancelled=lambda:True
        self.engine.checkpoint=checkpoint
        with self.assertRaisesRegex(driver.Rejected,'FILES_TREE_CANCELED'): self.engine.pack(request)
        self.assertEqual(self.lookup(request)['state'],'Aborted'); self.assertEqual(self.source_identity(),before)
        self.assertFalse((self.root/'target.tar.gz').exists()); self.assertTrue(self.pending(request).exists())

    def test_discard_is_durable_before_start_even_after_root_revocation(self):
        request = self.request(); policy=json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-upload'); self.registry.write_text(json.dumps(policy))
        result=self.engine.discard(request); self.assertEqual(result['state'],'Aborted')
        with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('no replay')):
            self.assertEqual(self.engine.pack(request),result)
        self.assertFalse(self.pending(request).exists()); self.assertFalse((self.root/'target.tar.gz').exists())

    def test_private_partial_foreign_object_and_identity_replacement_remain_unknown(self):
        request=self.request(); parent=os.open(self.root,os.O_RDONLY | os.O_DIRECTORY)
        try: private=driver.staging(parent)
        finally: os.close(parent)
        os.close(private); self.pending(request).write_bytes(b'foreign')
        with self.assertRaises(FileExistsError): self.engine.pack(request)
        self.assertEqual(self.pending(request).read_bytes(),b'foreign'); self.assertEqual(self.lookup(request)['state'],'Unknown')

    def test_full_archive_tampering_after_ready_is_detected_without_publication(self):
        request=self.request()
        def checkpoint(phase):
            if phase == 'Ready':
                path=self.pending(request); info=path.stat()
                with path.open('r+b') as stream: stream.seek(-20,os.SEEK_END); stream.write(b'corrupt')
                os.utime(path,ns=(info.st_atime_ns,info.st_mtime_ns))
        self.engine.checkpoint=checkpoint
        with self.assertRaises(driver.Rejected): self.engine.pack(request)
        self.assertFalse((self.root/'target.tar.gz').exists()); self.assertEqual(self.file.read_bytes(),self.data)

    def test_late_policy_revocation_and_parent_replacement_prevent_publish(self):
        for kind in ('policy','parent'):
            with self.subTest(kind=kind):
                parent=self.root/('parent-'+kind); parent.mkdir(); request=self.request(target=parent.name+'/target.tar.gz')
                def checkpoint(phase):
                    if phase == 'Publishing':
                        if kind == 'policy':
                            policy=json.loads(self.registry.read_bytes()); policy[0]['ownerUid']=1001; self.registry.write_text(json.dumps(policy))
                        else: parent.rename(self.root/'original-parent'); parent.mkdir()
                self.engine.checkpoint=checkpoint
                with self.assertRaises(driver.Rejected): self.engine.pack(request)
                self.assertFalse((parent/'target.tar.gz').exists()); self.assertEqual(self.lookup(request)['state'],'Unknown')
                self.engine.checkpoint=lambda phase:None

    def test_real_sigkill_fences_never_recompress_or_promote_private_outputs(self):
        before=self.source_identity()
        for phase,expected in (('Preparing','Aborted'),('StagingIntent','Unknown'),('Created','Unknown'),('Staging','Aborted'),('Ready','Aborted'),('Publishing','Aborted'),('Published','Completed')):
            with self.subTest(phase=phase):
                request=self.request(target=phase+'.tar.gz'); child=os.fork()
                if child == 0:
                    self.engine.checkpoint=lambda actual:os.kill(os.getpid(),signal.SIGKILL) if actual == phase else None
                    try: self.engine.pack(request)
                    except BaseException: os._exit(97)
                    os._exit(98)
                _,status=os.waitpid(child,0); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
                with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('must not restart codec')):
                    result=self.lookup(request); self.assertEqual(result['state'],expected); self.assertEqual(self.engine.pack(request),result)
                self.assertEqual(self.source_identity(),before)
                self.assertEqual((self.root/(phase+'.tar.gz')).exists(),expected == 'Completed')

    def test_corrupt_receipt_and_wrong_namespace_are_not_adopted(self):
        request=self.request(); self.engine.discard(request); path=self.receipt(request)
        value=json.loads(path.read_bytes()); value['plan']['fingerprint']='B'*64; path.write_text(json.dumps(value))
        with self.assertRaises(driver.Rejected): self.lookup(request)
        self.assertFalse((self.root/'target.tar.gz').exists())

    def test_late_source_change_at_publication_is_not_silently_archived_as_current(self):
        request=self.request()
        def checkpoint(phase):
            if phase == 'Publishing': self.file.write_bytes(b'late edit')
        self.engine.checkpoint=checkpoint
        with self.assertRaises(driver.Rejected): self.engine.pack(request)
        self.assertFalse((self.root/'target.tar.gz').exists()); self.assertTrue(self.pending(request).exists())
        self.assertEqual(self.lookup(request)['state'],'Aborted'); self.assertEqual(self.file.read_bytes(),b'late edit')

    def test_capacity_and_deadline_reject_without_publishing(self):
        request=self.request()
        capacity=os.statvfs_result((4096,4096,1000000,0,0,1000,1000,1000,0,255))
        with patch.object(os,'fstatvfs',return_value=capacity),self.assertRaisesRegex(driver.Rejected,'FILES_CAPACITY_REQUIRED'):
            self.engine.pack(request)
        self.assertFalse(self.pending(request).exists()); self.assertEqual(self.lookup(request)['state'],'Aborted')
        request=self.request()
        with patch.object(packing.time,'monotonic',side_effect=[0,601]),self.assertRaisesRegex(driver.Rejected,'FILES_TREE_DEADLINE'):
            self.engine.pack(request)
        self.assertFalse((self.root/'target.tar.gz').exists())

    def test_existing_completed_receipt_remains_observable_at_full_history_limit(self):
        request=self.request(); completed=self.engine.pack(request)
        with patch.object(os,'scandir',side_effect=AssertionError('existing receipt must bypass quota scan')):
            self.assertEqual(self.lookup(request),completed)

    def test_failed_private_write_retains_partial_without_recoding_on_inspection(self):
        request=self.request(); before=self.source_identity()
        def broken(instance,command,manifest,descriptor,archive_format):
            os.write(descriptor,b'partial'); os.fsync(descriptor); raise OSError('synthetic target write failure')
        with patch.object(codec.TreeArchive,'create',broken),self.assertRaises(OSError): self.engine.pack(request)
        with patch.object(codec.TreeArchive,'create',side_effect=AssertionError('no restart')):
            self.assertEqual(self.lookup(request)['state'],'Aborted'); self.assertEqual(self.engine.pack(request)['state'],'Aborted')
        self.assertEqual(self.pending(request).read_bytes(),b'partial'); self.assertEqual(self.source_identity(),before)

    def test_complete_one_gib_plus_tail_is_streamed_not_only_prefix_checked(self):
        size=1024**3+7
        with self.file.open('r+b') as stream: stream.truncate(size); stream.seek(size-7); stream.write(b'TAIL\x00\xff!')
        before=self.source_identity(); request=self.request(); result=self.engine.pack(request)
        self.assertEqual(result['totalBytes'],size); self.assertEqual(result['state'],'Completed')
        with self.file.open('rb') as original: expected=hashlib.file_digest(original,'sha256').hexdigest()
        with tarfile.open(self.root/'target.tar.gz') as archive:
            with archive.extractfile('中文.bin') as stream: actual=hashlib.file_digest(stream,'sha256').hexdigest()
        self.assertEqual(actual,expected); self.assertEqual(self.source_identity(),before)

    def test_download_exact_completed_archive_with_chunks_hash_and_version(self):
        request=self.request(); completed=self.engine.pack(request); output=io.BytesIO()
        self.engine.download({key:request[key] for key in ('operationId','owner')},output)
        rows=[json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual(rows[0]['type'],'metadata'); self.assertEqual(rows[0]['sizeBytes'],completed['archiveBytes'])
        self.assertEqual(rows[-1],{'type':'end','sizeBytes':completed['archiveBytes'],'sha256':completed['archiveSha256'],'version':completed['version']})
        payload=bytearray()
        for row in rows[1:-1]:
            self.assertEqual(row['offset'],len(payload)); block=base64.b64decode(row['data'],validate=True); self.assertLessEqual(len(block),32768); payload.extend(block)
        self.assertEqual(bytes(payload),(self.root/'target.tar.gz').read_bytes())
        self.assertEqual(hashlib.sha256(payload).hexdigest().upper(),completed['archiveSha256'])

    def test_download_rejects_other_owner_incomplete_tamper_and_revoked_target_permissions(self):
        request=self.request(); self.engine.discard(request)
        with self.assertRaisesRegex(driver.Rejected,'FILES_PACK_NOT_COMPLETED'): self.engine.download({key:request[key] for key in ('operationId','owner')},io.BytesIO())
        request=self.request(); self.engine.pack(request); lookup={key:request[key] for key in ('operationId','owner')}
        with self.assertRaisesRegex(driver.Rejected,'FILES_OPERATION_NOT_FOUND'): self.engine.download({**lookup,'owner':'B'*64},io.BytesIO())
        original=self.registry.read_bytes()
        for permission in ('files-download','files-archive','files-tree-pack'):
            policy=json.loads(original); policy[0]['allowedActions'].remove(permission); self.registry.write_text(json.dumps(policy)); output=io.BytesIO()
            with self.assertRaises(driver.Rejected): self.engine.download(lookup,output)
            self.assertEqual(output.getvalue(),b''); self.registry.write_bytes(original)
        (self.root/'target.tar.gz').write_bytes(b'changed'); output=io.BytesIO()
        with self.assertRaises(driver.Rejected): self.engine.download(lookup,output)
        self.assertEqual(output.getvalue(),b'')

    def test_download_does_not_need_original_source_or_origin_policy_but_binds_target(self):
        policy=json.loads(self.registry.read_bytes()); policy.append({**policy[0],'id':'test.origin'}); self.registry.write_text(json.dumps(policy))
        command=self.command(); command['source']['rootId']='test.origin'
        request={'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(command)}; self.engine.pack(request)
        self.file.unlink(); (self.source/'空目录').rmdir(); self.source.rmdir()
        self.registry.write_text(json.dumps(policy[:1])); output=io.BytesIO()
        self.engine.download({key:request[key] for key in ('operationId','owner')},output)
        self.assertEqual(json.loads(output.getvalue().splitlines()[-1])['type'],'end')

    def test_download_aborts_on_midstream_root_revocation_and_never_emits_end(self):
        request=self.request(archive_format='tar'); self.engine.pack(request); rows=[]
        def revoke(value,output=None):
            rows.append(value)
            if value['type']=='chunk' and len(rows)==2:
                policy=json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-tree-pack'); self.registry.write_text(json.dumps(policy))
        with patch.object(driver,'emit',revoke),self.assertRaises(driver.Rejected): self.engine.download({key:request[key] for key in ('operationId','owner')})
        self.assertFalse(any(row['type']=='end' for row in rows))

    def test_fixed_loader_and_unsafe_modules_are_rejected_before_invocation(self):
        modules=self.fixture.base/'installed-packing'; modules.mkdir(mode=0o700)
        names=('whaledeck-file-tree.py','whaledeck-tree-archive.py','whaledeck-tree-packing.py')
        for name in names:
            target=modules/name; shutil.copyfile(Path(driver.__file__).with_name(name),target); target.chmod(0o640)
        with patch.dict(sys.modules,{driver.__name__:driver}),patch.object(driver,'__file__',str(modules/'whaledeck-files.py')):
            loaded=driver.tree_packing_engine(); request=self.request(); self.assertEqual(loaded.plan(request['plan']['command']),request['plan'])
            self.assertEqual(loaded.pack(request)['state'],'Completed')
            target=modules/names[-1]
            target.chmod(0o660)
            with self.assertRaisesRegex(driver.Rejected,'FILES_MODULE_UNSAFE'): driver.tree_packing_engine()
            target.chmod(0o640); linked=modules/'linked'; os.link(target,linked)
            with self.assertRaisesRegex(driver.Rejected,'FILES_MODULE_UNSAFE'): driver.tree_packing_engine()
            linked.unlink(); os.chown(target,12345,0)
            with self.assertRaisesRegex(driver.Rejected,'FILES_MODULE_UNSAFE'): driver.tree_packing_engine()
            os.chown(target,0,0); original=modules/'original.py'; target.rename(original); target.symlink_to(original)
            with self.assertRaisesRegex(driver.Rejected,'FILES_MODULE_UNSAFE'): driver.tree_packing_engine()

    def test_completed_download_does_not_hold_global_write_gate_and_disconnect_preserves_receipt(self):
        request=self.request(); result=self.engine.pack(request); lookup={key:request[key] for key in ('operationId','owner')}
        output=io.BytesIO()
        with patch.object(driver,'namespace_lock',side_effect=AssertionError('read-only download must not acquire write gate')):
            self.engine.download(lookup,output)
        before=len(os.listdir('/proc/self/fd')); rows=[]
        def disconnect(value,output=None):
            rows.append(value['type'])
            if value['type']=='chunk': raise BrokenPipeError('synthetic disconnected private client')
        with patch.object(driver,'emit',disconnect),self.assertRaises(BrokenPipeError): self.engine.download(lookup)
        self.assertEqual(len(os.listdir('/proc/self/fd')),before); self.assertNotIn('end',rows); self.assertEqual(self.lookup(request),result)
