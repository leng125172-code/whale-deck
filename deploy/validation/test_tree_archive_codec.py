"""Disposable real Linux tar/gzip I/O only; no installed RPC/UI/HDD claim."""
import contextlib
import gzip
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tarfile
import unittest
from unittest.mock import patch
import test_file_transport as common
from test_file_tree_manifest import tree_module

spec = importlib.util.spec_from_file_location('tree_archive_codec', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-archive.py')
codec = importlib.util.module_from_spec(spec); spec.loader.exec_module(codec)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires disposable real Linux root I/O')
class TreeArchiveCodecTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root, self.state = self.fixture.root, self.fixture.state
        self.source = self.root / 'source'; self.source.mkdir(mode=0o750)
        (self.source / '空目录').mkdir(mode=0o710); (self.source / '代码').mkdir(mode=0o750)
        self.data = bytes(range(256)) * 257 + b'\x00\xff'
        self.file = self.source / '代码' / ('中文' * 30 + '.bin')
        self.file.write_bytes(self.data); self.file.chmod(0o751)
        os.utime(self.file, ns=(1712345678123456789,1712345678123456789))
        self.engine = codec.TreeArchive(driver, tree_module.FileTreeManifest)

    def tearDown(self): self.fixture.tearDown()

    def command(self): return {'rootId':'test.shared','path':'source','expectedVersion':driver.version(self.source.stat())}

    @contextlib.contextmanager
    def output(self, name='archive'):
        descriptor = os.open(self.state / name, os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
        try: yield descriptor
        finally: os.close(descriptor)

    def create(self, descriptor, archive_format='tar.gz', engine=None):
        manifest = tree_module.FileTreeManifest(driver).scan(self.command())
        return (engine or self.engine).create(self.command(), manifest['manifestSha256'], descriptor, archive_format)

    def archive(self, members, archive_format='tar', tail=b''):
        output = io.BytesIO()
        with tarfile.open(fileobj=output, mode='w', format=tarfile.PAX_FORMAT) as writer:
            for info, data in members: writer.addfile(info, io.BytesIO(data) if data is not None else None)
        data = output.getvalue() + tail
        if archive_format == 'tar.gz': data = gzip.compress(data, mtime=0)
        path = self.state / 'input'; path.write_bytes(data); path.chmod(0o600)
        return os.open(path, os.O_RDONLY)

    def file_info(self, name, data=b'x'):
        info = tarfile.TarInfo(name); info.size = len(data); info.mode = 0o644
        return info, data

    def snapshot(self):
        return {str(path.relative_to(self.source)):(driver.version(path.stat()),hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None)
            for path in [self.source,*self.source.rglob('*')]}

    def test_pax_tar_and_gzip_roundtrip_full_binary_unicode_empty_directory_and_ns_metadata(self):
        for archive_format in ('tar','tar.gz'):
            with self.subTest(archive_format=archive_format), self.output(archive_format) as output:
                before = self.snapshot(); result = self.create(output,archive_format)
                self.assertEqual(result['entryCount'],4); self.assertEqual(result['totalBytes'],len(self.data))
                self.assertEqual(result['archiveSha256'],hashlib.sha256(os.pread(output,os.fstat(output).st_size,0)).hexdigest().upper())
                self.assertEqual(self.snapshot(),before)
                target_path = self.state / ('restored-' + archive_format); target_path.mkdir(mode=0o700)
                target = os.open(target_path,os.O_RDONLY | os.O_DIRECTORY)
                try: self.engine.extract(output,target,result,archive_format,owner_uid=1000,owner_gid=1001)
                finally: os.close(target)
                restored = target_path / self.file.relative_to(self.source)
                self.assertEqual(restored.read_bytes(),self.data)
                value = restored.stat(); self.assertEqual((stat.S_IMODE(value.st_mode),value.st_uid,value.st_gid,value.st_mtime_ns),(0o751,1000,1001,self.file.stat().st_mtime_ns))
                self.assertTrue((target_path / '空目录').is_dir())
                self.assertEqual((target_path.stat().st_uid,stat.S_IMODE(target_path.stat().st_mode)),(0,0o700))
                with os.fdopen(os.dup(output),'rb') as compatible:
                    compatible.seek(0)
                    with tarfile.open(fileobj=compatible,mode='r:*') as standard:
                        self.assertEqual(standard.extractfile('代码/' + self.file.name).read(),self.data)

    def test_archive_is_byte_deterministic_and_does_not_include_host_names_or_file_contents_in_index(self):
        with self.output('a') as first, self.output('b') as second:
            one = self.create(first); two = self.create(second)
            self.assertEqual(one['archiveSha256'],two['archiveSha256'])
            self.assertNotIn(str(self.fixture.base),json.dumps(one))
            self.assertNotIn('data',one['entries'][1])

    def test_gnu_long_name_metadata_is_bounded_and_uses_all_ordinary_path_checks(self):
        def extension(payload, kind=tarfile.GNUTYPE_LONGNAME):
            header = tarfile.TarInfo('././@LongLink'); header.type = kind; header.size = len(payload)
            return header.tobuf(format=tarfile.USTAR_FORMAT) + payload + bytes(-len(payload) % 512)
        info = tarfile.TarInfo('placeholder'); info.size = 1; info.mode = 0o644
        member = info.tobuf(format=tarfile.USTAR_FORMAT) + b'x' + bytes(511)
        valid = extension(('代码/' + '中文' * 35 + '.bin').encode() + b'\0')
        malformed = [extension(name) for name in (
            b'', b'not-terminated', b'a\0hidden\0', b'\xff\0', b'../outside\0',
            b'/absolute\0', b'nested/.env\0', b'a/' * 33 + b'file\0',
            b'x' * self.engine.MEMBER_METADATA + b'\0')]
        malformed += [valid + valid, extension(b'target\0', tarfile.GNUTYPE_LONGLINK)]
        path = self.state / 'gnu-malformed'
        for number, prefix in enumerate(malformed):
            path.write_bytes(prefix + member + bytes(1024))
            descriptor = os.open(path, os.O_RDONLY)
            try:
                with self.subTest(case=number), self.assertRaises(driver.Rejected): self.engine.inspect(descriptor, 'tar')
            finally: os.close(descriptor)

    def test_gnu_long_name_cannot_hide_a_special_member_or_duplicate_path(self):
        payload = ('长文件名' * 12 + '.bin').encode() + b'\0'
        header = tarfile.TarInfo('././@LongLink'); header.type = tarfile.GNUTYPE_LONGNAME; header.size = len(payload)
        prefix = header.tobuf(format=tarfile.USTAR_FORMAT) + payload + bytes(-len(payload) % 512)
        path = self.state / 'gnu-special'
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.FIFOTYPE, tarfile.CHRTYPE):
            info = tarfile.TarInfo('short'); info.type = kind; info.linkname = 'outside'
            path.write_bytes(prefix + info.tobuf(format=tarfile.USTAR_FORMAT) + bytes(1024))
            descriptor = os.open(path, os.O_RDONLY)
            try:
                with self.subTest(kind=kind), self.assertRaises(driver.Rejected): self.engine.inspect(descriptor, 'tar')
            finally: os.close(descriptor)
        info = tarfile.TarInfo('short'); info.size = 1
        member = prefix + info.tobuf(format=tarfile.USTAR_FORMAT) + b'x' + bytes(511)
        path.write_bytes(member + member + bytes(1024)); descriptor = os.open(path, os.O_RDONLY)
        try:
            with self.assertRaisesRegex(driver.Rejected, '^FILES_ARCHIVE_DUPLICATE$'): self.engine.inspect(descriptor, 'tar')
        finally: os.close(descriptor)

    def test_changed_manifest_and_protected_descendant_fail_before_output(self):
        manifest = tree_module.FileTreeManifest(driver).scan(self.command()); command = self.command()
        self.file.write_bytes(b'changed')
        with self.output() as output:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_CHANGED$'): self.engine.create(command,manifest['manifestSha256'],output)
            self.assertEqual(os.fstat(output).st_size,0)
        (self.source / '.env').write_bytes(b'private')
        with self.output('second') as output:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_PROTECTED$'): self.create(output)
            self.assertEqual(os.fstat(output).st_size,0)

    def test_both_root_permissions_are_required_before_archive_bytes(self):
        registry = json.loads(self.fixture.registry.read_bytes()); registry[0]['allowedActions'].remove('files-download')
        self.fixture.registry.write_text(json.dumps(registry))
        with self.output() as output:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ROOT_FORBIDDEN$'): self.create(output)
            self.assertEqual(os.fstat(output).st_size,0)

    def test_output_must_be_empty_private_single_link_and_not_a_source_member(self):
        with self.output() as output:
            os.write(output,b'existing')
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_OUTPUT_NOT_NEW$'): self.create(output)
            self.assertEqual(os.pread(output,8,0),b'existing')
        with self.output('unsafe') as output:
            os.fchmod(output,0o644)
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_FILE_INVALID$'): self.create(output)
        path = self.source / 'empty'; descriptor = os.open(path,os.O_CREAT | os.O_EXCL | os.O_RDWR,0o600)
        try:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_OUTPUT_NOT_NEW$'): self.create(descriptor)
            self.assertEqual(path.read_bytes(),b'')
        finally: os.close(descriptor)

    def test_existing_target_and_changed_archive_plan_do_not_overwrite_anything(self):
        with self.output() as output:
            result = self.create(output); target_path = self.state / 'target'; target_path.mkdir(mode=0o700)
            marker = target_path / 'existing'; marker.write_bytes(b'keep')
            target = os.open(target_path,os.O_RDONLY | os.O_DIRECTORY)
            try:
                with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_TARGET_NOT_EMPTY$'): self.engine.extract(output,target,result)
                self.assertEqual(marker.read_bytes(),b'keep'); marker.unlink()
                with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_CHANGED$'): self.engine.extract(output,target,{**result,'archiveSha256':'0'*64})
                self.assertEqual(list(target_path.iterdir()),[])
            finally: os.close(target)

    def test_absolute_traversal_backslash_protected_and_deep_members_are_rejected(self):
        for name in ('/etc/passwd','../escape','a/../../escape','a\\b','.env','a/private.key','a/' * 33 + 'x'):
            with self.subTest(name=name):
                descriptor = self.archive([self.file_info(name)])
                try:
                    with self.assertRaises(driver.Rejected): self.engine.inspect(descriptor,'tar')
                finally: os.close(descriptor); (self.state / 'input').unlink()

    def test_links_devices_fifos_sparse_and_global_headers_are_never_extracted(self):
        for kind in (tarfile.SYMTYPE,tarfile.LNKTYPE,tarfile.CHRTYPE,tarfile.BLKTYPE,tarfile.FIFOTYPE,tarfile.GNUTYPE_SPARSE,tarfile.XGLTYPE):
            with self.subTest(kind=kind):
                info = tarfile.TarInfo('link'); info.type = kind; info.linkname = '../outside'
                descriptor = self.archive([(info,None)])
                try:
                    with self.assertRaises(driver.Rejected): self.engine.inspect(descriptor,'tar')
                finally: os.close(descriptor); (self.state / 'input').unlink()

    def test_duplicate_paths_and_file_parent_conflicts_are_rejected(self):
        for names in (('a','a'),('a','a/b'),('a/b','a')):
            with self.subTest(names=names):
                descriptor = self.archive([self.file_info(name) for name in names])
                try:
                    with self.assertRaises(driver.Rejected): self.engine.inspect(descriptor,'tar')
                finally: os.close(descriptor); (self.state / 'input').unlink()

    def test_implicit_parents_and_dot_prefix_have_one_canonical_result(self):
        descriptor = self.archive([self.file_info('./a/b.txt',b'hello')])
        try:
            value = self.engine.inspect(descriptor,'tar')
            self.assertEqual([row['path'] for row in value['entries']],['','a','a/b.txt'])
            self.assertEqual(value['totalBytes'],5)
        finally: os.close(descriptor)

    def test_sparse_pax_and_duplicate_pax_keys_are_rejected_without_interpreting_extensions(self):
        info, data = self.file_info('x'); info.pax_headers = {'GNU.sparse.map':'0,1'}
        descriptor = self.archive([(info,data)])
        try:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_METADATA_INVALID$'): self.engine.inspect(descriptor,'tar')
        finally: os.close(descriptor)
        with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_METADATA_INVALID$'): self.engine.pax(b'9 path=a\n9 path=b\n')

    def test_oversized_extension_header_rejected_before_payload_read(self):
        info = tarfile.TarInfo('pax'); info.type = tarfile.XHDTYPE; info.size = 10**9
        path = self.state / 'input'; path.write_bytes(info.tobuf(format=tarfile.USTAR_FORMAT)); descriptor = os.open(path,os.O_RDONLY)
        try:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_METADATA_LIMIT$'): self.engine.inspect(descriptor,'tar')
        finally: os.close(descriptor)

    def test_bad_header_truncation_gzip_crc_and_nonzero_trailing_data_are_rejected(self):
        with self.output() as output:
            self.create(output); content = os.pread(output,os.fstat(output).st_size,0)
        cases = [('tar',b'bad' * 200),('tar.gz',content[:-5]),('tar.gz',content[:-8]+bytes([content[-8]^1])+content[-7:])]
        for index,(archive_format,data) in enumerate(cases):
            path = self.state / str(index); path.write_bytes(data); descriptor = os.open(path,os.O_RDONLY)
            try:
                with self.assertRaises(driver.Rejected): self.engine.inspect(descriptor,archive_format)
            finally: os.close(descriptor)
        descriptor = self.archive([self.file_info('x')],tail=b'not-another-archive')
        try:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_TRAILING_DATA$'): self.engine.inspect(descriptor,'tar')
        finally: os.close(descriptor)

    def test_entry_and_total_byte_admission_fail_before_materialization(self):
        descriptor = self.archive([self.file_info('a',b'1234'),self.file_info('b',b'5678')])
        try:
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_ENTRY_LIMIT$'):
                codec.TreeArchive(driver,tree_module.FileTreeManifest,max_entries=1).inspect(descriptor,'tar')
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_SIZE_LIMIT$'):
                codec.TreeArchive(driver,tree_module.FileTreeManifest,max_bytes=5).inspect(descriptor,'tar')
        finally: os.close(descriptor)

    def test_cancel_and_deadline_stop_without_source_mutation(self):
        before = self.snapshot()
        with self.output() as output:
            engine = codec.TreeArchive(driver,tree_module.FileTreeManifest,cancelled=lambda:True)
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_CANCELED$'): self.create(output,engine=engine)
            ticks = iter((0.0,2.0))
            engine = codec.TreeArchive(driver,tree_module.FileTreeManifest,timeout_seconds=1,clock=lambda:next(ticks))
            with self.assertRaisesRegex(driver.Rejected,'^FILES_TREE_DEADLINE$'): self.create(output,engine=engine)
            self.assertEqual(os.fstat(output).st_size,0)
        self.assertEqual(self.snapshot(),before)

    def test_partial_write_failure_retains_private_evidence_without_retry_or_cleanup(self):
        before = self.snapshot(); real_write = os.write; calls = 0
        def failed(descriptor,data):
            nonlocal calls
            calls += 1
            if calls >= 3: raise OSError('synthetic-write-failure')
            return real_write(descriptor,data)
        with self.output() as output:
            with patch.object(os,'write',failed), self.assertRaises(OSError): self.create(output,'tar')
            self.assertGreater(os.fstat(output).st_size,0)
            self.assertEqual(stat.S_IMODE(os.fstat(output).st_mode),0o600)
            os.lseek(output,0,os.SEEK_SET)
            with self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_OUTPUT_NOT_NEW$'): self.create(output,'tar')
        self.assertEqual(self.snapshot(),before)

    def test_setuid_mode_and_foreign_owners_are_not_adopted_from_uploaded_archives(self):
        info,data = self.file_info('x',b'content'); info.mode = 0o6755; info.uid = 12345; info.gid = 23456
        descriptor = self.archive([(info,data)]); target_path = self.state / 'target'; target_path.mkdir(mode=0o700)
        target = os.open(target_path,os.O_RDONLY | os.O_DIRECTORY)
        try:
            plan = self.engine.inspect(descriptor,'tar'); self.engine.extract(descriptor,target,plan,'tar',owner_uid=1000,owner_gid=1001)
            value = (target_path / 'x').stat()
            self.assertEqual((stat.S_IMODE(value.st_mode),value.st_uid,value.st_gid),(0o755,1000,1001))
        finally: os.close(target); os.close(descriptor)

    def test_full_two_megabyte_tail_and_aggregate_byte_counts(self):
        data = b'a' * (2 * 1024**2) + b'TAIL\x00\xff!'
        self.file.write_bytes(data)
        with self.output() as output:
            result = self.create(output)
            row = next(row for row in result['entries'] if row['kind'] == 'File')
            self.assertEqual((row['sizeBytes'],row['sha256']),(len(data),hashlib.sha256(data).hexdigest().upper()))
            self.assertEqual(result['totalBytes'],len(data))

    def test_complete_one_gibibyte_plus_tail_stream_uses_full_hash_not_prefix(self):
        size = 1024**3+7
        with self.file.open('wb') as stream:
            stream.truncate(size); stream.seek(size-7); stream.write(b'TAIL\x00\xff!')
        with self.file.open('rb') as stream: digest = hashlib.file_digest(stream,'sha256').hexdigest().upper()
        with self.output() as output:
            result = self.create(output)
            row = next(row for row in result['entries'] if row['kind']=='File')
            self.assertEqual((row['sizeBytes'],row['sha256']),(size,digest))
            self.assertEqual(result['totalBytes'],size)

    def test_root_revocation_after_private_output_starts_never_returns_a_capture(self):
        changed = False
        def revoke():
            nonlocal changed
            path = self.state/'archive'
            if not changed and path.exists() and path.stat().st_size>0:
                changed = True; registry = json.loads(self.fixture.registry.read_bytes())
                registry[0]['allowedActions'].remove('files-download'); self.fixture.registry.write_text(json.dumps(registry))
            return False
        before = self.snapshot()
        with self.output() as output:
            engine = codec.TreeArchive(driver,tree_module.FileTreeManifest,cancelled=revoke)
            with self.assertRaises(driver.Rejected): self.create(output,engine=engine)
            self.assertTrue(changed); self.assertGreater(os.fstat(output).st_size,0)
        self.assertEqual(self.snapshot(),before)

    def test_corrupted_destination_write_is_rejected_by_independent_disk_rehash(self):
        with self.output() as output:
            result = self.create(output); target_path = self.state/'target'; target_path.mkdir(mode=0o700)
            target = os.open(target_path,os.O_RDONLY | os.O_DIRECTORY); real_write = os.write; changed = False
            def faulty(descriptor,data):
                nonlocal changed
                data = bytes(data)
                if not changed and data: changed = True; data = bytes([data[0]^1])+data[1:]
                return real_write(descriptor,data)
            try:
                with patch.object(os,'write',faulty), self.assertRaisesRegex(driver.Rejected,'^FILES_ARCHIVE_CONTENT_INVALID$'):
                    self.engine.extract(output,target,result)
                self.assertTrue(changed)
                self.assertEqual((target_path.stat().st_uid,stat.S_IMODE(target_path.stat().st_mode)),(0,0o700))
                self.assertEqual(self.file.read_bytes(),self.data)
            finally: os.close(target)

    def test_relative_metadata_lookups_pin_the_parent_instead_of_following_intermediate_links(self):
        real_stat = os.stat
        def leaf_only(path,*args,**kwargs):
            if kwargs.get('dir_fd') is not None and '/' in os.fspath(path):
                raise AssertionError('relative metadata lookup must be a leaf within an openat2-pinned parent')
            return real_stat(path,*args,**kwargs)
        with self.output() as output, patch.object(os,'stat',leaf_only):
            result = self.create(output)
            self.assertEqual(result['totalBytes'],len(self.data))


if __name__ == '__main__': unittest.main()
