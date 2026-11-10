import hashlib
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('development_images',
    Path(__file__).resolve().parents[1] / 'host/whaledeck-development-images.py')
images = importlib.util.module_from_spec(spec)
spec.loader.exec_module(images)


def archive(rows):
    result = io.BytesIO()
    with tarfile.open(fileobj=result, mode='w', format=tarfile.USTAR_FORMAT) as output:
        for name, kind, body in rows:
            entry = tarfile.TarInfo(name)
            entry.type, entry.size = kind, len(body)
            entry.linkname = 'outside' if kind in (tarfile.SYMTYPE, tarfile.LNKTYPE) else ''
            output.addfile(entry, io.BytesIO(body) if kind == tarfile.REGTYPE else None)
    return io.BytesIO(result.getvalue())


class DevelopmentImageImportTests(unittest.TestCase):
    def test_only_regular_oci_content_is_written_with_private_modes(self):
        body = b'example image content'
        blob = 'blobs/sha256/' + hashlib.sha256(body).hexdigest()
        rows = [('blobs', tarfile.DIRTYPE, b''), ('blobs/sha256', tarfile.DIRTYPE, b''),
                ('index.json', tarfile.REGTYPE, b'{}'), ('oci-layout', tarfile.REGTYPE, b'{}'),
                ('manifest.json', tarfile.REGTYPE, b'not authoritative'), (blob, tarfile.REGTYPE, body)]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = images.extract(archive(rows), root)
            self.assertEqual(body, (root / blob).read_bytes())
            self.assertEqual(0o600, (root / blob).stat().st_mode & 0o777)
            self.assertFalse((root / 'manifest.json').exists())
            self.assertEqual(4, result['entries'])

    def test_paths_links_and_special_nodes_never_escape(self):
        for name, kind in [('../outside', tarfile.REGTYPE), ('/outside', tarfile.REGTYPE),
                           ('blobs/sha256/' + 'a' * 64, tarfile.SYMTYPE), ('index.json', tarfile.LNKTYPE),
                           ('index.json', tarfile.FIFOTYPE)]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                with self.assertRaises(images.ImageError):
                    images.extract(archive([(name, kind, b'x' if kind == tarfile.REGTYPE else b'')]), Path(directory))

    def test_digest_duplicate_and_incomplete_archives_are_rejected(self):
        for rows in [[('blobs/sha256/' + 'a' * 64, tarfile.REGTYPE, b'wrong')],
                     [('index.json', tarfile.REGTYPE, b'{}')] * 2,
                     [('index.json', tarfile.REGTYPE, b'{}')]]:
            with self.subTest(rows=rows), tempfile.TemporaryDirectory() as directory:
                with self.assertRaises(images.ImageError):
                    images.extract(archive(rows), Path(directory))

    def test_aggregate_size_is_bounded_before_blob_write(self):
        data = b'a' * 101
        rows = [('blobs/sha256/' + hashlib.sha256(data).hexdigest(), tarfile.REGTYPE, data)]
        with tempfile.TemporaryDirectory() as directory, patch.object(images, 'LIMIT', 100):
            with self.assertRaisesRegex(images.ImageError, 'OCI_ARCHIVE_SIZE_INVALID'):
                images.extract(archive(rows), Path(directory))

    def test_extended_headers_are_rejected_before_reading_their_payload(self):
        for kind in (tarfile.XHDTYPE, tarfile.XGLTYPE, tarfile.GNUTYPE_LONGNAME):
            header = tarfile.TarInfo('index.json')
            header.type, header.size = kind, 10**12
            stream = io.BytesIO(header.tobuf(format=tarfile.GNU_FORMAT))
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(images.ImageError, 'OCI_ARCHIVE_PATH_INVALID'):
                    images.extract(stream, Path(directory))

    def test_archive_parent_links_and_late_writes_are_rejected(self):
        files = images.load('files')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'real').mkdir()
            source = root / 'real/image.tar'
            source.write_bytes(b'archive')
            (root / 'alias').symlink_to(root / 'real', target_is_directory=True)
            with self.assertRaises(OSError):
                with images.archive_stream(files, root / 'alias/image.tar'):
                    self.fail('Symlinked source accepted')
            with self.assertRaisesRegex(images.ImageError, 'OCI_ARCHIVE_CHANGED'):
                with images.archive_stream(files, source) as stream:
                    self.assertEqual(b'archive', stream.read())
                    source.write_bytes(b'changed archive')

    def test_exporter_that_ignores_termination_is_killed_and_reaped(self):
        process = subprocess.Popen([sys.executable, '-c',
            'import signal,time;signal.signal(signal.SIGTERM,signal.SIG_IGN);print("ready",flush=True);time.sleep(60)'],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        try:
            self.assertEqual(b'ready\n', process.stdout.readline())
            with patch.object(images, 'STOP_GRACE_SECONDS', 0.05):
                images.stop_exporter(process)
            self.assertIsNotNone(process.poll())
            self.assertLess(process.returncode, 0)
            self.assertTrue(process.stdout.closed)
        finally:
            if process.poll() is None:
                process.kill()
            process.wait(timeout=5)
            process.stdout.close()


if __name__ == '__main__':
    unittest.main()
