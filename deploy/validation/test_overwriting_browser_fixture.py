"""Isolated Linux-root tests of the QA observer with explicitly fabricated receipts.

No API, Agent, Worker, real browser or deployed overwrite acceptance is claimed.
"""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import stat
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import uuid


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root')
class OverwritingObserverTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('overwriting_observer_test', Path(__file__).with_name('overwriting_browser_fixture.py'))
        self.module = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.module)
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-overwrite-observer-')
        self.addCleanup(self.scratch.cleanup)
        self.previous_umask = os.umask(0o077); self.addCleanup(os.umask, self.previous_umask)
        self.directory = Path(self.scratch.name).resolve(); self.module.SIZE = 4096 + 7  # Explicit small unit-test payload.
        self.module.BASE = self.directory / 'files'; self.module.BASE.mkdir()
        self.module.ARTIFACTS = self.directory / 'artifacts'; self.module.ARTIFACTS.mkdir()
        self.module.STATE = self.directory / 'state'
        for kind in ('tree-recycling', 'tree-overwrite-restoring'): (self.module.STATE / kind).mkdir(parents=True)
        self.module.POLICY = self.directory / 'roots.json'
        actions = ['files-' + value for value in ('list', 'download', 'history', 'mutate', 'upload', 'archive', 'tree-trash', 'tree-restore', 'tree-overwrite-restore')]
        self.module.POLICY.write_text(json.dumps([{'id': self.module.ROOT, 'path': str(self.module.BASE), 'ownerUid': 0, 'ownerGid': 0, 'allowedActions': actions}]))
        self.module.BACKUPS = self.directory / 'backups.json'
        self.module.BACKUPS.write_text(json.dumps({'mountPoint': str(self.directory), 'targetRelative': 'backup'}))
        self.store = self.directory / 'backup/directory-archives'; self.store.mkdir(parents=True)
        with contextlib.redirect_stdout(io.StringIO()): self.created = self.module.create_fixture()
        self.marker = self.created['marker']; self.folder, self.evidence = self.module.paths(self.marker)
        self.baseline = self.module.private_json(self.evidence / 'baseline.json'); self.operations = []

    def write(self, path, value):
        path.write_bytes(self.module.encode(value)); path.chmod(0o600)

    def archive(self, operation, kind, owner, location, before, theme, original_kind):
        """Real TAR/manifest bytes, deliberately synthetic capture provenance."""
        entries = []
        for name, field in (('', 'root'), ('空目录', 'empty'), (self.module.MEMBER, 'file')):
            entries.append({'path': name, 'kind': 'File' if field == 'file' else 'Directory', 'sourceIdentity': before[field][:8],
                'sizeBytes': self.module.SIZE if field == 'file' else 0, 'sha256': before['file'][-1] if field == 'file' else ''})
        rows = [{key: entry[key] for key in ('path', 'kind', 'sizeBytes', 'sha256')} | {'mode': entry['sourceIdentity'][2] & 0o777,
            'uid': entry['sourceIdentity'][3], 'gid': entry['sourceIdentity'][4], 'mtimeNs': entry['sourceIdentity'][7]} for entry in entries]
        content = hashlib.sha256(b'whaledeck-tree-archive-content-v1\n' + self.module.encode(rows)).hexdigest().upper()
        tree = hashlib.sha256(b'whaledeck-tree-overwrite-restoring-v1\n' + self.module.encode(entries)).hexdigest().upper()
        manifest = {'manifestSha256': 'D' * 64, 'rootId': self.module.ROOT, 'path': location['path'],
            'rootVersion': location['expectedVersion'], 'entryCount': 3, 'totalBytes': self.module.SIZE, 'entries': entries}
        self.write(self.store / (operation + '.manifest.json'), manifest)
        archive = self.store / (operation + '.tar.gz')
        with tarfile.open(archive, 'w:gz', format=tarfile.PAX_FORMAT) as output:
            for entry in entries:
                info = tarfile.TarInfo(entry['path'] or '.'); identity = entry['sourceIdentity']
                info.mode = identity[2] & 0o777; info.uid, info.gid = identity[3:5]
                seconds, nanoseconds = divmod(identity[7], 1_000_000_000)
                info.mtime = seconds; info.pax_headers = {'mtime': str(seconds) + '.' + str(nanoseconds).zfill(9)}
                if entry['kind'] == 'Directory': info.type = tarfile.DIRTYPE; output.addfile(info)
                else:
                    data = self.module.payload(self.marker, theme, original_kind); info.size = len(data)
                    output.addfile(info, io.BytesIO(data))
        archive.chmod(0o600); value = self.module.proof(archive)
        saved = {'state': 'Verified', 'operationId': operation, 'owner': owner, 'captureKind': kind, 'source': location,
            'manifestSha256': 'D' * 64, 'entryCount': 3, 'totalBytes': self.module.SIZE,
            'manifestFileSha256': self.module.proof(self.store / (operation + '.manifest.json'))[-1], 'contentSha256': content,
            'archiveIdentity': value[:2], 'archiveBytes': value[6], 'archiveSha256': value[-1],
            'differentSourceDevice': before['file'][0] != value[0]}
        self.write(self.store / (operation + '.json'), saved); self.write(self.store / (operation + '.lock'), {})
        return saved, content, tree

    def complete(self):
        """Fabricate both output trees and root receipts; never invoke product mutations."""
        namespace = self.folder / '.whaledeck-files'; namespace.mkdir(mode=0o700)
        for theme in self.module.THEMES:
            capture, operation = uuid.uuid4(), uuid.uuid4()
            source = self.folder / ('source-' + theme); retained = namespace / (capture.hex + '.tree-trash')
            source.rename(retained); retained.chmod(0o700)
            location = {'rootId': self.module.ROOT, 'path': self.marker + '/source-' + theme, 'expectedVersion': 'A' * 64}
            saved, content, _ = self.archive(capture.hex, 'Trash', 'C' * 64, location, self.baseline['trees']['source-' + theme], theme, 'source')
            self.write(self.module.STATE / 'tree-recycling' / (capture.hex + '.json'), {'operationId': capture.hex, 'state': 'Completed',
                'captureVerified': True, 'sourceRemoved': True, 'sourceRetained': True, 'retainedIdentity': self.module.proof(retained)[:2],
                'owner': 'C' * 64, 'archiveBytes': saved['archiveBytes'], 'archiveSha256': saved['archiveSha256'],
                'plan': {'source': location, 'manifestSha256': 'D' * 64}})
            target = self.folder / ('target-' + theme); old = namespace / (operation.hex + '.tree-overwrite-restore')
            target.rename(old); shutil.copytree(retained, target); target.chmod(0o750); (target / '空目录').chmod(0o710)
            target_location = {'rootId': self.module.ROOT, 'path': self.marker + '/target-' + theme, 'expectedVersion': 'B' * 64}
            backup, target_content, tree = self.archive(operation.hex, 'Overwrite', 'E' * 64, target_location, self.baseline['trees']['target-' + theme], theme, 'target')
            self.write(self.module.STATE / 'tree-overwrite-restoring' / (operation.hex + '.json'), {
                'operationId': operation.hex, 'state': 'Completed', 'backupStarted': True, 'backupVerified': True, 'swapStarted': True,
                'originalsRetained': True, 'version': 'F' * 64, 'owner': 'E' * 64, 'newIdentity': self.module.proof(target)[:2],
                'backupArchiveBytes': backup['archiveBytes'], 'backupArchiveSha256': backup['archiveSha256'],
                'plan': {'command': {'source': location, 'captureId': capture.hex, 'captureOwner': 'C' * 64, 'captureKind': 'Trash',
                    'format': 'tar.gz', 'target': target_location}, 'archiveBytes': saved['archiveBytes'], 'archiveSha256': saved['archiveSha256'],
                    'contentSha256': content, 'entryCount': 3, 'totalBytes': self.module.SIZE,
                    'targetIdentity': self.baseline['trees']['target-' + theme]['root'][:2], 'targetManifestSha256': 'D' * 64,
                    'targetContentSha256': target_content, 'targetTreeSha256': tree}})
            self.operations.append({'theme': theme, 'captureId': str(capture), 'overwritingOperationId': str(operation), 'publishedVersion': 'F' * 64})

    def inspect(self, finish=False): return self.module.inspect_completed(self.marker, self.operations, finish)

    def preserved(self):
        self.assertTrue(self.folder.is_dir()); self.assertTrue((self.folder / 'target-light' / self.module.MEMBER).exists())
        self.assertEqual(16, len(list(self.store.iterdir())))
        self.assertFalse((self.evidence / 'result.json').exists())

    def test_creation_sets_exact_directory_and_private_evidence_permissions(self):
        self.assertEqual(0o700, stat.S_IMODE(self.folder.stat().st_mode)); self.assertEqual(0o700, stat.S_IMODE(self.evidence.stat().st_mode))
        self.assertEqual(0o600, stat.S_IMODE((self.evidence / 'baseline.json').stat().st_mode))
        for name in self.baseline['trees']:
            self.assertEqual(0o750, stat.S_IMODE((self.folder / name).stat().st_mode))
            self.assertEqual(0o710, stat.S_IMODE((self.folder / name / '空目录').stat().st_mode))
            self.assertEqual(1712345678123456789, (self.folder / name / self.module.MEMBER).stat().st_mtime_ns)

    def test_marker_and_identifier_reject_escape_alias_and_noncanonical_input(self):
        for marker in ('../escape', self.marker + '/child', self.marker.upper(), 'wdv-browser-overwrite-' + '0' * 15):
            with self.subTest(marker=marker), self.assertRaises(AssertionError): self.module.paths(marker)
        for value in (uuid.uuid4().hex, 'ABCDEFAB-CDEF-4ABC-8ABC-ABCDEFABCDEF', str(uuid.UUID(int=0)), '../escape'):
            with self.subTest(value=value), self.assertRaises(AssertionError): self.module.identifier(value)

    def test_ready_target_check_is_read_only_with_source_provenance_explicitly_stubbed(self):
        with patch.object(self.module, 'attest_source', return_value=None):
            result = self.module.inspect_ready(self.marker, 'light', str(uuid.uuid4()))
        self.assertEqual('passed', result['browserOverwriteReadyDiskProof'])
        self.assertEqual(0, result['mutations']); self.assertTrue(self.folder.is_dir())

    def test_ready_rejects_changed_empty_directory_metadata_and_nested_unknown_object(self):
        empty = self.folder / 'target-light/空目录'
        with patch.object(self.module, 'attest_source', return_value=None):
            empty.chmod(0o700)
            with self.assertRaises(AssertionError): self.module.inspect_ready(self.marker, 'light', str(uuid.uuid4()))
            empty.chmod(0o710)
            # Updating the private synthetic baseline isolates the member-set
            # assertion; this is not evidence for a real source capture.
            path = self.evidence / 'baseline.json'
            value = self.module.private_json(path); value['trees']['target-light']['empty'] = self.module.proof(empty)
            self.write(path, value)
            foreign = empty / 'unknown'; foreign.write_bytes(b'preserve')
            value['trees']['target-light']['empty'] = self.module.proof(empty); self.write(path, value)
            with self.assertRaises(AssertionError): self.module.inspect_ready(self.marker, 'light', str(uuid.uuid4()))
            self.assertEqual(b'preserve', foreign.read_bytes())

    def test_evidence_is_exclusive_and_limited_to_approved_names_and_scope(self):
        baseline = (self.evidence / 'baseline.json').read_bytes()
        with self.assertRaises(FileExistsError): self.module.save_evidence(self.evidence, 'baseline.json', {})
        with self.assertRaises(AssertionError): self.module.save_evidence(self.evidence, '../elsewhere', {})
        with self.assertRaises(AssertionError): self.module.save_evidence(self.folder, 'result.json', {})
        self.assertEqual(baseline, (self.evidence / 'baseline.json').read_bytes())

    def test_baseline_marker_identity_and_exact_schema_are_bound(self):
        path = self.evidence / 'baseline.json'; original = path.read_bytes()
        for change in ({'marker': 'wdv-browser-overwrite-' + '0' * 16}, {'identity': [0, 1]}, {'unexpected': True}):
            with self.subTest(change=change):
                self.write(path, {**self.baseline, **change})
                with self.assertRaises(AssertionError): self.module.fixture(self.marker)
                path.write_bytes(original)

    def test_private_evidence_rejects_symlink_hardlink_and_world_readable_file(self):
        path = self.evidence / 'baseline.json'; path.chmod(0o644)
        with self.assertRaises(AssertionError): self.module.fixture(self.marker)
        path.chmod(0o600); link = self.evidence / 'linked'; os.link(path, link)
        with self.assertRaises(AssertionError): self.module.fixture(self.marker)
        link.unlink(); original = self.evidence / 'original'; path.rename(original); path.symlink_to(original)
        with self.assertRaises(AssertionError): self.module.fixture(self.marker)

    def test_inspect_checks_real_test_tar_and_content_domains_without_deletion(self):
        self.complete(); result = self.inspect()
        self.assertEqual('passed', result['realBrowserDirectoryOverwritingDiskObserver']); self.assertFalse(result['exactOwnedPayloadsRemoved'])
        self.preserved()

    def test_finish_removes_only_fixture_payloads_and_retains_archives_receipts_and_evidence(self):
        self.complete(); archives = {path: self.module.proof(path) for path in self.store.iterdir()}
        result = self.inspect(True); self.assertTrue(result['exactOwnedPayloadsRemoved']); self.assertFalse(self.folder.exists())
        self.assertEqual(archives, {path: self.module.proof(path) for path in self.store.iterdir()})
        self.assertEqual(4, len(list(self.module.STATE.rglob('*.json')))); self.assertTrue((self.evidence / 'baseline.json').exists())
        self.assertTrue(self.module.private_json(self.evidence / 'result.json')['fullProofBeforeCleanup'])

    def test_unknown_object_is_never_treated_as_owned_cleanup(self):
        self.complete(); foreign = self.folder / 'foreign'; foreign.write_bytes(b'leave untouched')
        with self.assertRaises(AssertionError): self.inspect(True)
        self.assertEqual(b'leave untouched', foreign.read_bytes()); self.preserved()

    def test_unknown_symbolic_link_is_preserved_and_refused(self):
        self.complete(); outside = self.directory / 'outside'; outside.write_bytes(b'foreign')
        (self.folder / 'alias').symlink_to(outside)
        with self.assertRaises(AssertionError): self.inspect(True)
        self.assertEqual(b'foreign', outside.read_bytes()); self.assertTrue((self.folder / 'alias').is_symlink()); self.preserved()

    def test_changed_manifest_content_or_domain_cannot_pass_as_target_tree_proof(self):
        self.complete(); operation = uuid.UUID(self.operations[0]['overwritingOperationId']).hex
        path = self.store / (operation + '.json'); original = path.read_bytes(); value = json.loads(original)
        root = json.loads((self.module.STATE / 'tree-overwrite-restoring' / (operation + '.json')).read_bytes())
        self.assertNotEqual(value['contentSha256'], root['plan']['targetTreeSha256'])
        self.write(path, {**value, 'contentSha256': root['plan']['targetTreeSha256']})
        with self.assertRaises(AssertionError): self.inspect(True)
        path.write_bytes(original); manifest = self.store / (operation + '.manifest.json')
        data = json.loads(manifest.read_bytes()); data['entries'][0]['sourceIdentity'][7] += 1; self.write(manifest, data)
        with self.assertRaises(AssertionError): self.inspect(True)
        self.preserved()

    def test_modified_new_output_or_repeated_operation_is_refused_before_cleanup(self):
        self.complete(); self.operations[1]['overwritingOperationId'] = self.operations[0]['overwritingOperationId']
        with self.assertRaises(AssertionError): self.inspect(True)
        self.preserved()

    def test_modified_output_is_not_removed(self):
        self.complete(); path = self.folder / 'target-dark' / self.module.MEMBER; path.write_bytes(b'changed')
        with self.assertRaises(AssertionError): self.inspect(True)
        self.assertEqual(b'changed', path.read_bytes()); self.preserved()


if __name__ == '__main__': unittest.main()
