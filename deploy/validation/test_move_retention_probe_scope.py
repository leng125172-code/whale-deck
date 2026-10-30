"""Real temporary Linux inodes for the QA-only partial-retention cut."""
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
import uuid

import bound_container_move_retention_cut as cut


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class MoveRetentionScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-retention-cut-'); self.root = Path(self.temporary.name)
        self.folder = self.root / 'fixture'; self.folder.mkdir(); self.operation = uuid.uuid4().hex
        self.source = self.folder / 'source'; self.source.mkdir(); (self.source / 'empty').mkdir()
        for index in range(4): (self.source / ('part-' + str(index))).write_bytes(('source-' + str(index)).encode())
        self.target = self.folder / 'target'; shutil.copytree(self.source, self.target)
        self.private = self.folder / '.whaledeck-container-moves-v1'; self.private.mkdir(mode=0o700)
        self.retained = self.private / (self.operation + '.tree-move'); self.retained.mkdir(mode=0o700); (self.retained / 'empty').mkdir()
        self.inner_plan = {'copyPlan': {'entryCount': 6}}
        self.plan = {'kind': 'Directory', 'operationKind': 'Move', 'sourceParent': list(cut.identity(self.folder)),
            'targetParent': list(cut.identity(self.folder)), 'sourceIdentity': list(cut.identity(self.source)),
            'command': {'sourcePath': 'work/source', 'targetPath': 'work/target'}, 'innerPlan': self.inner_plan}
        self.outer = {'state': 'Running', 'plan': self.plan, 'operationId': self.operation, 'owner': 'A'*64}
        self.inner = {'state': 'SourceRetaining', 'operationId': self.operation, 'owner': self.outer['owner'], 'plan': self.inner_plan,
            'destinationPublished': True, 'sourceMutationStarted': True, 'retainedIdentity': list(cut.identity(self.retained))}
        self.copied = {'state': 'Completed', 'operationId': self.operation, 'owner': self.outer['owner'],
            'plan': self.inner_plan['copyPlan'], 'newIdentity': list(cut.identity(self.target))}
        self.tree_state = self.root / 'engines/tree-moves'; self.tree_state.mkdir(parents=True)
        self.copy_state = self.root / 'engines/tree-move-copies'; self.copy_state.mkdir()
        self.save()
        (self.source / 'part-0').rename(self.retained / 'part-0')

    def tearDown(self): self.temporary.cleanup()

    def save(self):
        for directory, value in ((self.tree_state, self.inner), (self.copy_state, self.copied)):
            path = directory / (self.operation + '.json'); path.write_text(json.dumps(value)); path.chmod(0o600)

    def proof(self):
        with patch.object(cut, 'ROOT', self.root): return cut.retention_proof(self.outer, self.folder)

    def test_only_real_published_target_and_split_originals_count(self):
        value = self.proof()
        self.assertEqual(value['retainedFiles'], ['part-0'])
        self.assertEqual(value['remainingFiles'], ['part-1', 'part-2', 'part-3'])
        self.assertEqual((self.retained / 'part-0').read_bytes(), b'source-0')

    def test_no_retained_or_all_retained_is_not_a_partial_retention(self):
        (self.retained / 'part-0').rename(self.source / 'part-0'); self.assertIsNone(self.proof())
        for index in range(4): (self.source / ('part-' + str(index))).rename(self.retained / ('part-' + str(index)))
        self.assertIsNone(self.proof())

    def test_unpublished_or_terminal_result_cannot_authorize_a_cut(self):
        self.inner['destinationPublished'] = False; self.save(); self.assertIsNone(self.proof())
        self.inner['destinationPublished'] = True; self.inner['state'] = 'Completed'; self.save(); self.assertIsNone(self.proof())
        self.outer['state'] = 'Unknown'; self.assertIsNone(self.proof())

    def test_foreign_owner_execution_and_plan_are_rejected(self):
        for field, value in (('owner', 'B'*64), ('operationId', uuid.uuid4().hex), ('plan', {'copyPlan': {'entryCount': 7}})):
            original = self.inner[field]; self.inner[field] = value; self.save()
            with self.assertRaises(AssertionError): self.proof()
            self.inner[field] = original
        self.save()

    def test_replaced_target_or_original_retention_inode_is_refused(self):
        self.copied['newIdentity'] = [1, 2]; self.save()
        with self.assertRaises(AssertionError): self.proof()
        self.copied['newIdentity'] = list(cut.identity(self.target)); self.inner['retainedIdentity'] = [1, 2]; self.save()
        with self.assertRaises(AssertionError): self.proof()

    def test_extra_private_data_is_preserved_and_refused(self):
        other = self.private / 'foreign'; other.write_bytes(b'keep')
        with self.assertRaises(AssertionError): self.proof()
        self.assertEqual(other.read_bytes(), b'keep')

    def test_symlink_or_duplicate_original_cannot_prove_retention(self):
        target = self.target / 'part-0'; target.unlink(); target.symlink_to(self.retained / 'part-0')
        with self.assertRaises(AssertionError): self.proof()
        target.unlink(); target.write_bytes(b'source-0'); (self.source / 'part-0').write_bytes(b'duplicate')
        with self.assertRaises(AssertionError): self.proof()

    def test_source_replacement_and_unsafe_path_are_refused(self):
        self.plan['sourceIdentity'] = [1, 2]
        with self.assertRaises(AssertionError): self.proof()
        self.plan['sourceIdentity'] = list(cut.identity(self.source)); self.plan['command']['sourcePath'] = 'work/../source'
        with self.assertRaises(AssertionError): self.proof()

    def test_non_owned_source_is_refused_before_signal_or_pid_access(self):
        with self.assertRaises(AssertionError): cut.freeze_retention({'container_id': 'a'*64}, self.folder, 100)


if __name__ == '__main__': unittest.main()
