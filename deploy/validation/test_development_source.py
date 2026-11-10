import gzip
import importlib.util
import io
import os
from pathlib import Path
import stat
import tarfile
import tempfile
import unittest
import zipfile


def load(name):
    path = Path(__file__).resolve().parents[1] / 'host' / ('whaledeck-' + name + '.py')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


source = load('development-source')
files = load('files')
archive = load('tree-archive')
storage = load('development-storage')


def zip_bytes(entries, *, compress=zipfile.ZIP_STORED):
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w', compress) as writer:
        for name, mode, body in entries:
            entry = zipfile.ZipInfo(name)
            entry.create_system = 3
            entry.external_attr = mode << 16
            entry.compress_type = compress
            writer.writestr(entry, body)
    return data.getvalue()


def tar_bytes(entries):
    data = io.BytesIO()
    with tarfile.open(fileobj=data, mode='w', format=tarfile.PAX_FORMAT) as writer:
        for name, kind, mode, body in entries:
            entry = tarfile.TarInfo(name)
            entry.type, entry.mode, entry.size = kind, mode, len(body)
            if kind == tarfile.SYMTYPE or kind == tarfile.LNKTYPE:
                entry.linkname = 'elsewhere'
            writer.addfile(entry, io.BytesIO(body) if kind == tarfile.REGTYPE else None)
    return data.getvalue()


class DevelopmentSourceTests(unittest.TestCase):
    def extract(self, body, fmt, output, maximum=source.MAX_EXPANDED):
        root = os.open(output, source.DIR)
        try:
            tree = source.SourceTree(files, root, os.geteuid(), len(body), maximum, lambda: None)
            with tempfile.NamedTemporaryFile() as staged:
                staged.write(body)
                staged.flush()
                if fmt == 'zip':
                    source.extract_zip(staged.fileno(), len(body), tree)
                else:
                    source.extract_tar(archive, staged.fileno(), fmt, tree, maximum)
                return tree.finish()
        finally:
            os.close(root)

    def test_normalizes_zip_tar_and_gzip_to_same_content_identity(self):
        body = b'print("example")\n'
        variants = {
            'zip': zip_bytes([('src/main.py', stat.S_IFREG | 0o7777, body), ('empty/', stat.S_IFDIR, b'')]),
            'tar': tar_bytes([('./src/main.py', tarfile.REGTYPE, 0o775, body), ('empty', tarfile.DIRTYPE, 0o755, b'')]),
        }
        variants['tar.gz'] = gzip.compress(variants['tar'])
        hashes = set()
        for fmt, content in variants.items():
            with self.subTest(format=fmt), tempfile.TemporaryDirectory() as directory:
                result = self.extract(content, fmt, directory)
                hashes.add(result['contentSha256'])
                self.assertEqual((1, len(body)), (result['fileCount'], result['totalBytes']))
                self.assertEqual(body, (Path(directory) / 'src/main.py').read_bytes())
                self.assertEqual(0o700, (Path(directory) / 'src/main.py').stat().st_mode & 0o7777)
                self.assertEqual(0o700, (Path(directory) / 'empty').stat().st_mode & 0o7777)
        self.assertEqual(1, len(hashes))

    def test_paths_special_members_and_credential_signatures_are_rejected(self):
        for name in ('../escape', '/outside', 'C:/Windows', 'directory\\file', '.git/config', 'src/.env', 'src/id_ed25519'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                body = zip_bytes([(name, stat.S_IFREG | 0o600, b'hello')])
                with self.assertRaises(source.SourceError):
                    self.extract(body, 'zip', directory)
        cases = [
            ('zip', zip_bytes([('linked', stat.S_IFLNK | 0o777, b'outside')])),
            ('tar', tar_bytes([('linked', tarfile.LNKTYPE, 0o777, b'')])),
            ('tar', tar_bytes([('special', tarfile.FIFOTYPE, 0o600, b'')])),
            ('zip', zip_bytes([('.env.example', stat.S_IFREG | 0o600,
                b'x' * (source.BLOCK - 8) + b'-----BEGIN OPENSSH PRIVATE KEY-----')])),
        ]
        for fmt, body in cases:
            with self.subTest(fmt=fmt), tempfile.TemporaryDirectory() as directory:
                with self.assertRaises((source.SourceError, files.Rejected)):
                    self.extract(body, fmt, directory)

    def test_entry_size_expansion_limit_and_trailing_zip_bytes(self):
        sparse = b'0' * (1024 * 1024)
        cases = [
            ('zip', zip_bytes([('packed', stat.S_IFREG | 0o600, sparse)], compress=zipfile.ZIP_DEFLATED), source.MAX_EXPANDED),
            ('tar.gz', gzip.compress(tar_bytes([('packed', tarfile.REGTYPE, 0o600, sparse)])), source.MAX_EXPANDED),
            ('zip', zip_bytes([('large', stat.S_IFREG | 0o600, b'abc')]), 2),
            ('zip', zip_bytes([('large', stat.S_IFREG | 0o600, b'abc')]) + b'trailing', source.MAX_EXPANDED),
        ]
        for fmt, body, limit in cases:
            with self.subTest(fmt=fmt), tempfile.TemporaryDirectory() as directory:
                with self.assertRaises(source.SourceError):
                    self.extract(body, fmt, directory, limit)

    def test_work_manifest_matches_original_and_flags_local_changes(self):
        original = tar_bytes([('src/main.py', tarfile.REGTYPE, 0o775, b'original')])
        with tempfile.TemporaryDirectory() as directory:
            result = self.extract(original, 'tar', directory)
            path = Path(directory) / 'src/main.py'
            driver = storage.WorkspaceStorage(files)
            with driver.absolute(directory) as fd:
                content, fingerprint, normalized, _ = source.work_manifest(driver, fd, os.geteuid(), lambda: None)
                self.assertEqual(result['contentSha256'], content)
                self.assertTrue(normalized)
                path.chmod(0o750)
                changed, second, normalized, _ = source.work_manifest(driver, fd, os.geteuid(), lambda: None)
                self.assertEqual(content, changed)
                self.assertNotEqual(fingerprint, second)
                self.assertFalse(normalized)
                path.write_bytes(b'modified')
                self.assertNotEqual(content, source.work_manifest(driver, fd, os.geteuid(), lambda: None)[0])
                (Path(directory) / 'link').symlink_to('/etc/passwd')
                with self.assertRaisesRegex(source.SourceError, 'WORK_UNSUPPORTED'):
                    source.work_manifest(driver, fd, os.geteuid(), lambda: None)

    def test_previous_tar_preserves_approved_content_and_refuses_late_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = b'a' * source.BLOCK + b'tail'
            script = root / '脚本'
            script.write_bytes(original)
            script.chmod(0o700)
            (root / 'empty').mkdir()
            (root / 'zero').touch()
            driver = storage.WorkspaceStorage(files)
            with driver.absolute(directory) as fd:
                blocks = {}
                _, _, _, rows = source.work_manifest(driver, fd, os.geteuid(), lambda: None,
                                                      export_blocks=blocks)
                result = io.BytesIO()
                source.write_previous_tar(driver, fd, rows, blocks, result, lambda: None)
                with tarfile.open(fileobj=io.BytesIO(result.getvalue())) as downloaded:
                    self.assertEqual({'脚本', 'empty', 'zero'}, set(downloaded.getnames()))
                    self.assertEqual(original, downloaded.extractfile('脚本').read())
                    self.assertEqual(0o700, downloaded.getmember('脚本').mode)
                    self.assertTrue(downloaded.getmember('empty').isdir())
                    self.assertEqual(b'', downloaded.extractfile('zero').read())
                script.write_bytes(original[:-4] + b'late')
                rejected = io.BytesIO()
                with self.assertRaisesRegex(source.SourceError, 'WORK_CHANGED'):
                    source.write_previous_tar(driver, fd, rows, blocks, rejected, lambda: None)
                self.assertNotIn(b'late', rejected.getvalue())

    def test_previous_tar_refuses_protected_paths_and_split_credential_signatures(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            driver = storage.WorkspaceStorage(files)
            with driver.absolute(directory) as fd:
                protected = root / '.env'
                protected.touch()
                with self.assertRaisesRegex(source.SourceError, 'PROTECTED_PATH'):
                    source.work_manifest(driver, fd, os.geteuid(), lambda: None, export_blocks={})
                protected.unlink()
                (root / 'config.txt').write_bytes(
                    b'x' * (source.BLOCK - 5) + b'-----BEGIN OPENSSH PRIVATE KEY-----')
                with self.assertRaisesRegex(source.SourceError, 'CREDENTIAL_CONTENT'):
                    source.work_manifest(driver, fd, os.geteuid(), lambda: None, export_blocks={})


if __name__ == '__main__':
    unittest.main()
