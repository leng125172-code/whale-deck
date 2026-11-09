import errno
import gzip
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import tarfile
import tempfile
import unittest
from unittest.mock import patch


def load(filename):
    path = Path(__file__).resolve().parents[1] / 'host' / filename
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


rootfs = load('whaledeck-development-rootfs.py')
files = load('whaledeck-files.py')


@unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
class RootfsTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.source = Path(directory.name) / 'oci'
        self.bundle = Path(directory.name) / 'bundle'
        self.source.mkdir(mode=0o700)
        self.bundle.mkdir(mode=0o700)
        self.blobs = self.source / 'blobs' / 'sha256'
        self.blobs.mkdir(parents=True)
        (self.source / 'oci-layout').write_text('{"imageLayoutVersion":"1.0.0"}')
        self.source_fd = os.open(self.source, rootfs.DIR)
        self.bundle_fd = os.open(self.bundle, rootfs.DIR)
        self.addCleanup(os.close, self.source_fd)
        self.addCleanup(os.close, self.bundle_fd)
        self.calls = 0

    def blob(self, value, media_type):
        data = value if isinstance(value, bytes) else rootfs.canonical(value)
        digest = hashlib.sha256(data).hexdigest()
        (self.blobs / digest).write_bytes(data)
        return {'mediaType': media_type, 'digest': 'sha256:' + digest, 'size': len(data)}

    def fixture(self, *, docker=False, wrong_diff=False, ambiguous=False):
        tar = io.BytesIO()
        with tarfile.open(fileobj=tar, mode='w') as archive:
            content = b'known'
            member = tarfile.TarInfo('example')
            member.size = len(content)
            archive.addfile(member, io.BytesIO(content))
        uncompressed = tar.getvalue()
        self.layer = self.blob(gzip.compress(uncompressed, mtime=0),
                               rootfs.DOCKER_GZIP_LAYER if docker else rootfs.GZIP_LAYER)
        native = rootfs.platform_name()
        config = self.blob({
            'architecture': native, 'os': 'linux',
            'rootfs': {'type': 'layers', 'diff_ids': ['sha256:' + (
                '0' * 64 if wrong_diff else hashlib.sha256(uncompressed).hexdigest())]},
        }, rootfs.DOCKER_CONFIG if docker else rootfs.CONFIG)
        manifest_type = rootfs.DOCKER_MANIFEST if docker else rootfs.MANIFEST
        manifest = self.blob({'mediaType': manifest_type, 'schemaVersion': 2, 'config': config, 'layers': [self.layer]}, manifest_type)
        if docker or ambiguous:
            index_type = rootfs.DOCKER_INDEX if docker else rootfs.INDEX
            item = dict(manifest, platform={'os': 'linux', 'architecture': native})
            parent = self.blob({'mediaType': index_type, 'schemaVersion': 2,
                                'manifests': [item, item] if ambiguous else [item]}, index_type)
        else:
            parent = manifest
        (self.source / 'index.json').write_bytes(rootfs.canonical({'schemaVersion': 2, 'manifests': [parent]}))
        self.reference = 'registry.example/runner/sdk@' + parent['digest']
        return manifest['digest']

    def unpack(self, parent, input_name, output_name):
        self.assertEqual(self.bundle_fd, parent)
        self.calls += 1
        layout = self.bundle / input_name
        index = json.loads((layout / 'index.json').read_bytes())
        manifest = index['manifests'][0]
        self.assertEqual({'org.opencontainers.image.ref.name': 'runner'}, manifest['annotations'])
        selected = json.loads((layout / 'blobs' / 'sha256' / manifest['digest'][7:]).read_bytes())
        self.assertEqual(rootfs.CONFIG, selected['config']['mediaType'])
        self.assertEqual(rootfs.GZIP_LAYER, selected['layers'][0]['mediaType'])
        self.assertEqual(self.layer['digest'], selected['layers'][0]['digest'])
        output = self.bundle / output_name
        output.mkdir(mode=0o700)
        (output / 'rootfs').mkdir(mode=0o755)

    def prepare(self):
        return rootfs.materialize(files, self.source_fd, self.bundle_fd, self.reference, self.unpack)

    def test_docker_index_is_resolved_and_only_verified_bytes_are_published(self):
        manifest_digest = self.fixture(docker=True)
        seal = self.prepare()
        self.assertEqual(manifest_digest, seal['manifestDigest'])
        self.assertNotEqual(manifest_digest, seal['stagedManifestDigest'])
        self.assertEqual(1, self.calls)
        self.assertEqual(seal, rootfs.require_materialized(files, self.bundle_fd, self.reference))
        with self.assertRaisesRegex(rootfs.RootfsError, 'SEAL_INVALID'):
            rootfs.require_materialized(files, self.bundle_fd, 'registry.example/runner/sdk@sha256:' + 'f' * 64)
        published = self.bundle / 'rootfs'
        published.rename(self.bundle / 'original')
        published.mkdir(mode=0o755)
        with self.assertRaisesRegex(rootfs.RootfsError, 'ROOTFS_CHANGED'):
            rootfs.require_materialized(files, self.bundle_fd, self.reference)

    def test_tampered_blob_never_reaches_unpack(self):
        self.fixture()
        layer = self.blobs / self.layer['digest'][7:]
        content = layer.read_bytes()
        layer.write_bytes(content[:-1] + bytes([content[-1] ^ 1]))
        with self.assertRaisesRegex(rootfs.RootfsError, 'DIGEST_MISMATCH'):
            self.prepare()
        self.assertFalse((self.bundle / 'rootfs').exists())
        self.assertEqual(0, self.calls)
        with self.assertRaisesRegex(rootfs.RootfsError, 'BUNDLE_NOT_EMPTY'):
            self.prepare()

    def test_json_with_duplicate_or_nonfinite_values_is_rejected(self):
        for document in (b'{"mediaType":0,"mediaType":1}', b'{"schemaVersion":NaN}'):
            with self.subTest(document=document), self.assertRaisesRegex(rootfs.RootfsError, 'JSON_INVALID'):
                rootfs.decode(document)

    def test_diff_id_is_checked_even_when_encoded_digest_matches(self):
        self.fixture(wrong_diff=True)
        with self.assertRaisesRegex(rootfs.RootfsError, 'DIFF_ID_INVALID'):
            self.prepare()
        self.assertEqual(0, self.calls)

    def test_ambiguous_native_platform_is_refused(self):
        self.fixture(ambiguous=True)
        with self.assertRaisesRegex(rootfs.RootfsError, 'PLATFORM_UNSUPPORTED'):
            self.prepare()
        self.assertEqual(0, self.calls)

    def test_blob_link_is_not_followed(self):
        self.fixture()
        layer = self.blobs / self.layer['digest'][7:]
        parked = layer.with_name('parked')
        layer.rename(parked)
        layer.symlink_to(parked)
        with self.assertRaises(OSError) as denied:
            self.prepare()
        self.assertEqual(errno.ELOOP, denied.exception.errno)
        self.assertEqual(0, self.calls)

    def test_interruption_after_seal_cannot_expose_incomplete_rootfs(self):
        self.fixture()
        pid = os.fork()
        if pid == 0:
            with patch.object(files, 'exchange', side_effect=lambda *_: os.kill(os.getpid(), signal.SIGKILL)):
                self.prepare()
            os._exit(1)
        _, status = os.waitpid(pid, 0)
        self.assertEqual(-signal.SIGKILL, os.waitstatus_to_exitcode(status))
        self.assertTrue((self.bundle / rootfs.SEAL).exists())
        with self.assertRaises(FileNotFoundError):
            rootfs.require_materialized(files, self.bundle_fd, self.reference)
        with self.assertRaisesRegex(rootfs.RootfsError, 'BUNDLE_NOT_EMPTY'):
            self.prepare()


if __name__ == '__main__':
    unittest.main()
