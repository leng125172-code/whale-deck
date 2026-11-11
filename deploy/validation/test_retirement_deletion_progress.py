"""Real descriptor/manifest checks for the partial deletion acceptance proof."""
import copy
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock
from contextlib import nullcontext

import validate_development_archive_execution as native
from interrupt_qa_retirement_execution import interrupt_locked
from retirement_deletion_progress import bind_prepared, entry_stat, inventory, manifest_digest

tree = native.load('whaledeck-file-tree.py')


@unittest.skipUnless(os.geteuid() == 0, 'Private root-owned source metadata')
class ProgressTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.payload = self.root/'data'; self.payload.mkdir(mode=0o700)
        self.journal = self.root/'journal'; self.journal.mkdir(mode=0o700)
        for name in ('一.txt', '二.txt', '三.txt'):
            (self.payload/name).write_bytes(name.encode())
        self.driver = native.storage.WorkspaceStorage(native.files)
        with self.driver.absolute(self.root) as parent:
            command = dict(rootId='private-test',path='data',expectedVersion=native.files.version(self.payload.stat()))
            adapter = native.archives.PayloadFiles(native.files,parent,command,dict(allowedActions=['files-list','files-download']))
            self.manifest = tree.FileTreeManifest(adapter).scan(command)
        plan = dict(sourceManifestSha256=self.manifest['manifestSha256'].lower(),entryCount=self.manifest['entryCount'])
        plan['retirementPlanSha256'] = hashlib.sha256(self.driver.canonical(plan)).hexdigest()
        self.request = dict(retirementPlanSha256=plan['retirementPlanSha256'])
        self.prepared = dict(request=self.request,bindingSha256=hashlib.sha256(self.driver.canonical(self.request)).hexdigest(),plan=plan,manifest=self.manifest)

    def test_unicode_digest_matches_production_and_corruption_or_rebinding_rejected(self):
        self.assertEqual(manifest_digest(self.driver,self.manifest),self.manifest['manifestSha256'])
        bind_prepared(self.driver,self.prepared,self.request)
        for field in ('request','bindingSha256','plan','manifest'):
            altered = copy.deepcopy(self.prepared)
            if field == 'request': altered[field] = dict(self.request,extra=True)
            elif field == 'bindingSha256': altered[field] = 'f'*64
            elif field == 'plan': altered[field]['entryCount'] += 1
            else: altered[field]['entries'][1]['sha256'] = '0'*64
            with self.subTest(field=field), self.assertRaises(AssertionError):
                bind_prepared(self.driver,altered,self.request)

    def test_missing_file_requires_exact_intent_and_positive_remaining(self):
        row = self.manifest['entries'][1]
        (self.payload/row['path']).unlink()
        with self.driver.absolute(self.journal) as journal, self.driver.absolute(self.payload) as payload:
            with self.assertRaises(FileNotFoundError): inventory(self.driver,journal,payload,self.manifest)
            intent = dict(index=1,entry=row)
            (self.journal/'remove-1.json').write_bytes(self.driver.canonical(intent))
            (self.journal/'remove-1.json').chmod(0o600)
            result = inventory(self.driver,journal,payload,self.manifest)
            self.assertEqual((1,2),(result['removedFiles'],result['remainingFiles']))
            intent['entry'] = dict(row,path='substituted')
            (self.journal/'remove-1.json').write_bytes(self.driver.canonical(intent))
            with self.assertRaises(native.files.Rejected): inventory(self.driver,journal,payload,self.manifest)

    def test_no_deletion_or_replaced_remaining_file_cannot_pass(self):
        with self.driver.absolute(self.journal) as journal, self.driver.absolute(self.payload) as payload:
            with self.assertRaisesRegex(AssertionError,'PARTIAL_FILE_DELETION_NOT_PROVEN'):
                inventory(self.driver,journal,payload,self.manifest)
            path = self.payload/self.manifest['entries'][1]['path']
            path.write_bytes(b'changed')
            with self.assertRaises(AssertionError): inventory(self.driver,journal,payload,self.manifest)

    def test_all_files_removed_cannot_be_counted_as_partial_deletion(self):
        for index,row in enumerate(self.manifest['entries'][1:],1):
            (self.payload/row['path']).unlink()
            intent = self.journal/f'remove-{index}.json'
            intent.write_bytes(self.driver.canonical(dict(index=index,entry=row))); intent.chmod(0o600)
        with self.driver.absolute(self.journal) as journal, self.driver.absolute(self.payload) as payload:
            with self.assertRaisesRegex(AssertionError,'PARTIAL_FILE_DELETION_NOT_PROVEN'):
                inventory(self.driver,journal,payload,self.manifest)

    def test_symlink_ancestor_cannot_traverse_outside_payload(self):
        (self.payload/'redirect').symlink_to(self.root,target_is_directory=True)
        with self.driver.absolute(self.payload) as payload:
            with self.assertRaises(OSError): entry_stat(self.driver,payload,'redirect/anything')


class ControlTests(unittest.TestCase):
    def test_progress_failure_cannot_trigger_kill(self):
        task = '44444444-4444-4444-4444-444444444444'
        tasks = Mock(); tasks.entry.return_value = nullcontext(1)
        tasks.completed.return_value = None; tasks.cancelled.return_value = False
        tasks.execution.boot.return_value = 'current'
        tasks.proof.return_value = dict(bootId='current',path='/system.slice/whaledeck-development-retirement-run-'+task.replace('-','')+'.service')
        tasks.execution.settled.return_value = False; tasks.d.canonical.return_value = b'{}'
        progress = Mock(); progress.wait.side_effect = TimeoutError('No deletion')
        with self.assertRaises(TimeoutError): interrupt_locked(tasks,dict(taskId=task),Mock(),progress=progress)
        self.assertFalse(any(call.kwargs.get('stop') for call in tasks.execution.settled.call_args_list))
        progress.stopped.assert_not_called()
