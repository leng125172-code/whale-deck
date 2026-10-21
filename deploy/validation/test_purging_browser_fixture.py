"""Linux-root observer unit tests with fabricated receipts, NOT product proof."""
import copy
import os
import unittest
from unittest.mock import patch
import uuid

import purging_browser_fixture as observer
import test_overwriting_browser_fixture as original_tests


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root')
class PurgingBrowserObserverTests(unittest.TestCase):
    def setUp(self):
        self.fixture = original_tests.OverwritingObserverTests(methodName='runTest')
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.m = self.fixture.module; self.marker = self.fixture.marker
        replacement = patch.object(observer, 'original', self.m); replacement.start(); self.addCleanup(replacement.stop)
        network = patch('requests.Session.request', side_effect=AssertionError('No network in observer tests'))
        network.start(); self.addCleanup(network.stop)
        self.folder = self.fixture.folder; self.store = self.fixture.store; self.ops = []
        (self.m.STATE / 'tree-purging').mkdir()

    def recycle(self, theme):
        capture = uuid.uuid4(); source = self.folder / ('source-' + theme)
        namespace = self.folder / '.whaledeck-files'; namespace.mkdir(mode=0o700, exist_ok=True)
        retained = namespace / (capture.hex + '.tree-trash'); source.rename(retained); retained.chmod(0o700)
        location = {'rootId': self.m.ROOT, 'path': self.marker + '/source-' + theme, 'expectedVersion': 'A' * 64}
        saved, _, _ = self.fixture.archive(capture.hex, 'Trash', 'C' * 64, location,
            self.fixture.baseline['trees']['source-' + theme], theme, 'source')
        self.fixture.write(self.m.STATE / 'tree-recycling' / (capture.hex + '.json'), {
            'operationId': capture.hex, 'state': 'Completed', 'captureVerified': True,
            'sourceRemoved': True, 'sourceRetained': True, 'retainedIdentity': self.m.proof(retained)[:2],
            'owner': 'C' * 64, 'archiveBytes': saved['archiveBytes'], 'archiveSha256': saved['archiveSha256'],
            'plan': {'source': location, 'manifestSha256': 'D' * 64}})
        return str(capture)

    def complete(self, theme):
        capture_id = self.recycle(theme)
        observer.ready(self.marker, theme, capture_id)
        capture = uuid.UUID(capture_id); execution = uuid.uuid4()
        saved_path = self.store / (capture.hex + '.json'); saved = self.m.private_json(saved_path)
        saved.update(state='Purged', originalsPurged=True); self.fixture.write(saved_path, saved)
        (self.store / (capture.hex + '.tar.gz')).unlink()
        retained = self.folder / '.whaledeck-files' / (capture.hex + '.tree-trash')
        (retained / self.m.MEMBER).unlink(); (retained / '空目录').rmdir(); retained.rmdir()
        self.fixture.write(self.m.STATE / 'tree-purging' / (execution.hex + '.json'), {
            'operationId': execution.hex, 'owner': 'E' * 64, 'state': 'Completed',
            'deletionStarted': True, 'archiveRemoved': True, 'originalsRemoved': True,
            'plan': {'command': {'captureId': capture.hex, 'scope': 'All',
                'captureOwner': 'C' * 64, 'source': saved['source']}}})
        self.ops.append({'theme': theme, 'captureId': capture_id, 'executionJobId': str(execution)})

    def both(self):
        for theme in self.m.THEMES: self.complete(theme)

    def reject(self, operations=None, finish=True):
        with self.assertRaises((AssertionError, FileNotFoundError, ValueError)):
            observer.completed(self.marker, self.ops if operations is None else operations, finish)
        self.assertTrue(self.folder.is_dir())
        self.assertFalse((self.fixture.evidence / 'purge-result.json').exists())

    def test_completed_proofs_and_explicit_final_cleanup_preserve_history(self):
        self.both()
        manifests = {path: path.read_bytes() for path in self.store.glob('*.manifest.json')}
        self.assertFalse(observer.completed(self.marker, self.ops)['onlyUntouchedOwnedLiveTargetsRemoved'])
        result = observer.completed(self.marker, self.ops, True)
        self.assertTrue(result['onlyUntouchedOwnedLiveTargetsRemoved']); self.assertFalse(self.folder.exists())
        self.assertEqual(2, len(manifests))
        for path, raw in manifests.items(): self.assertEqual(raw, path.read_bytes())
        self.assertEqual(2, len(list((self.m.STATE / 'tree-purging').glob('*.json'))))

    def test_changed_target_bytes_block_any_live_deletion(self):
        self.both(); (self.folder / 'target-dark' / self.m.MEMBER).write_bytes(b'other'); self.reject()
        self.assertTrue((self.folder / 'target-light' / self.m.MEMBER).exists())

    def test_changed_target_inode_even_same_bytes_is_rejected(self):
        self.both(); leaf = self.folder / 'target-dark' / self.m.MEMBER
        raw = leaf.read_bytes(); leaf.rename(leaf.with_suffix('.old')); leaf.write_bytes(raw); self.reject()

    def test_extra_unregistered_object_is_preserved(self):
        self.both(); (self.folder / 'other').write_text('unknown'); self.reject()
        self.assertEqual('unknown', (self.folder / 'other').read_text())

    def test_manifest_changed_after_ready_is_rejected(self):
        self.both(); path = self.store / (uuid.UUID(self.ops[0]['captureId']).hex + '.manifest.json')
        path.write_text('{}'); self.reject()

    def test_missing_ready_proof_is_not_reconstructed_after_deletion(self):
        self.both(); (self.fixture.evidence / 'purge-ready-light.json').unlink(); self.reject()

    def test_selected_positive_deletion_fact_must_be_explicit(self):
        self.both(); path = self.m.STATE / 'tree-purging' / (uuid.UUID(self.ops[0]['executionJobId']).hex + '.json')
        value = self.m.private_json(path); value.pop('originalsRemoved'); self.fixture.write(path, value); self.reject()

    def test_unknown_receipt_is_not_treated_as_completed(self):
        self.both(); path = self.m.STATE / 'tree-purging' / (uuid.UUID(self.ops[0]['executionJobId']).hex + '.json')
        value = self.m.private_json(path); value['state'] = 'Unknown'; self.fixture.write(path, value); self.reject()

    def test_wrong_capture_owner_is_rejected(self):
        self.both(); path = self.m.STATE / 'tree-purging' / (uuid.UUID(self.ops[0]['executionJobId']).hex + '.json')
        value = self.m.private_json(path); value['plan']['command']['captureOwner'] = 'F' * 64
        self.fixture.write(path, value); self.reject()

    def test_duplicate_theme_and_unexpected_fields_are_rejected(self):
        self.both(); self.reject([self.ops[0], self.ops[0]])
        ops = copy.deepcopy(self.ops); ops[0]['path'] = '/'; self.reject(ops)

    def test_one_theme_cannot_authorize_finish(self):
        self.complete('light'); self.reject()

    def test_ready_snapshot_cannot_be_overwritten(self):
        cap = self.recycle('light'); observer.ready(self.marker, 'light', cap)
        path = self.fixture.evidence / 'purge-ready-light.json'; raw = path.read_bytes()
        with self.assertRaises(FileExistsError): observer.ready(self.marker, 'light', cap)
        self.assertEqual(raw, path.read_bytes())


if __name__ == '__main__': unittest.main()
