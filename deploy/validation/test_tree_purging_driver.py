"""Fixed-driver dispatch and real Linux module metadata/capture I/O tests.

All payloads belong to TemporaryDirectory fixtures. Only backup_engine is a
fixture: its HDD identity is simulated, not workstation/Agent/BFF acceptance.
The support TestCase is composed, never inherited or imported into collection.
"""
import contextlib
import json
import os
from pathlib import Path
import shutil
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import uuid
import test_tree_purging as support_tests

driver = support_tests.driver
MODULES = (
    'whaledeck-file-tree.py', 'whaledeck-tree-archive.py',
    'whaledeck-tree-backups.py', 'whaledeck-tree-copies.py',
    'whaledeck-tree-moves.py', 'whaledeck-tree-recycling.py',
    'whaledeck-tree-unpacking.py', 'whaledeck-tree-restoring.py',
    'whaledeck-tree-overwrite-restoring.py', 'whaledeck-tree-purging.py',
)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0,
                     'requires real isolated Linux root I/O')
class TreePurgingDriverTests(unittest.TestCase):
    @contextlib.contextmanager
    def captured(self, kind='Trash'):
        support = support_tests.TreePurgingTests()
        try:
            with support.captured(kind) as fixture:
                yield fixture
        finally:
            support.doCleanups()

    @staticmethod
    def snapshot(fixture):
        tree = support_tests.TreePurgingTests.tree
        return (tree(fixture.retained), tree(fixture.store),
                tree(fixture.visible), fixture.origin.read_bytes())

    @contextlib.contextmanager
    def installed(self, fixture):
        folder = fixture.root.parent / 'installed-purging'
        folder.mkdir(mode=0o700)
        for name in MODULES:
            target = folder / name
            shutil.copyfile(Path(driver.__file__).with_name(name), target)
            target.chmod(0o640)
        # The real fixed loader and every loaded module remain unmocked. This
        # supplies only the existing isolated backup mount fixture.
        with patch.dict(sys.modules, {driver.__name__: driver}), \
                patch.object(driver, '__file__', str(folder / 'whaledeck-files.py')), \
                patch.object(driver, 'backup_engine', return_value=fixture.engine.captures.b):
            yield folder

    def invoke_main(self, action, value, *, engine=None, trailing=b''):
        reader, writer = os.pipe()
        try:
            os.write(writer, json.dumps(value).encode() + b'\n' + trailing)
        finally:
            os.close(writer)
        output = []
        with os.fdopen(reader, 'r') as stream, contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(sys, 'stdin', stream))
            stack.enter_context(patch.object(sys, 'argv', ['whaledeck-files.py', action]))
            stack.enter_context(patch.dict(os.environ, {'INVOCATION_ID': uuid.uuid4().hex}))
            stack.enter_context(patch.object(driver, 'emit', side_effect=output.append))
            if engine is not None:
                stack.enter_context(patch.object(driver, 'tree_purging_engine', return_value=engine))
                for name in ('tree_recycling_engine', 'tree_restoring_engine', 'tree_overwrite_restoring_engine'):
                    stack.enter_context(patch.object(driver, name, side_effect=AssertionError('purge must not use legacy dispatch')))
            driver.main()
        self.assertEqual(output[0]['type'], 'driver')
        self.assertEqual(len(output), 2)
        return output[1]

    def test_real_fixed_loader_reads_both_capture_kinds_without_changing_payloads(self):
        for kind in ('Trash', 'Overwrite'):
            with self.subTest(kind=kind), self.captured(kind) as fixture, self.installed(fixture):
                before = self.snapshot(fixture)
                loaded = driver.tree_purging_engine()
                self.assertEqual(loaded.DOMAIN, fixture.engine.DOMAIN)
                self.assertEqual(set(loaded.origins), {'Trash', 'Overwrite'})
                for scope in ('Archive', 'Originals', 'All'):
                    command = {**fixture.command, 'scope': scope}
                    self.assertEqual(loaded.plan(command), fixture.engine.plan(command))
                self.assertEqual(before, self.snapshot(fixture))

    def test_every_fixed_dependency_rejects_unsafe_real_linux_metadata(self):
        with self.captured() as fixture, self.installed(fixture) as folder:
            before = self.snapshot(fixture)
            driver.tree_purging_engine().plan(fixture.command)
            for name in MODULES:
                target = folder / name
                for mode in (0o660, 0o642):
                    with self.subTest(module=name, unsafe='writable', mode=oct(mode)):
                        try:
                            target.chmod(mode)
                            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'):
                                driver.tree_purging_engine()
                        finally:
                            target.chmod(0o640)
                with self.subTest(module=name, unsafe='hardlink'):
                    linked = folder / 'linked'
                    os.link(target, linked)
                    try:
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'):
                            driver.tree_purging_engine()
                    finally:
                        linked.unlink()
                with self.subTest(module=name, unsafe='owner'):
                    try:
                        os.chown(target, 12345, 0)
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'):
                            driver.tree_purging_engine()
                    finally:
                        os.chown(target, 0, 0)
                for kind in ('symlink', 'directory'):
                    with self.subTest(module=name, unsafe=kind):
                        original = folder / 'original.py'
                        target.rename(original)
                        try:
                            if kind == 'symlink': target.symlink_to(original)
                            else: target.mkdir(mode=0o700)
                            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'):
                                driver.tree_purging_engine()
                        finally:
                            if kind == 'symlink': target.unlink()
                            else: target.rmdir()
                            original.rename(target)
                with self.subTest(module=name, unsafe='oversized'):
                    contents = target.read_bytes()
                    try:
                        with target.open('ab') as stream: stream.write(b' ' * 131073)
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'):
                            driver.tree_purging_engine()
                    finally:
                        target.write_bytes(contents)
            self.assertEqual(before, self.snapshot(fixture))

    def test_main_four_verbs_fence_discard_and_replay_completed_without_redeleting(self):
        for kind in ('Trash', 'Overwrite'):
            with self.subTest(kind=kind), self.captured(kind) as fixture, self.installed(fixture):
                loaded = driver.tree_purging_engine()
                before = self.snapshot(fixture)
                plan = self.invoke_main('tree-purge-plan', fixture.command, engine=loaded)
                self.assertEqual(before, self.snapshot(fixture))
                canceled = {'operationId': uuid.uuid4().hex, 'owner': 'E' * 64, 'plan': plan}
                result = self.invoke_main('tree-purge-discard', canceled, engine=loaded)
                self.assertEqual(result['state'], 'Aborted')
                self.assertFalse(result['deletionStarted'])
                with patch.object(loaded, 'remove_originals', side_effect=AssertionError('discard fences originals')), \
                        patch.object(loaded, 'remove_archive', side_effect=AssertionError('discard fences archive')):
                    self.assertEqual(self.invoke_main('tree-purge-apply', canceled, engine=loaded), result)
                    self.assertEqual(self.invoke_main('tree-purge-status', {key: canceled[key] for key in ('operationId', 'owner')}, engine=loaded), result)
                self.assertEqual(before, self.snapshot(fixture))
                request = {**canceled, 'operationId': uuid.uuid4().hex}
                result = self.invoke_main('tree-purge-apply', request, engine=loaded)
                self.assertEqual((result['state'], result['kind'], result['scope']), ('Completed', 'DirectoryPurge', 'All'))
                self.assertTrue(result['archiveRemoved']); self.assertTrue(result['originalsRemoved'])
                self.assertFalse(os.path.lexists(fixture.archive)); self.assertFalse(os.path.lexists(fixture.retained))
                self.assertEqual(before[2:], self.snapshot(fixture)[2:])
                survivors = self.snapshot(fixture)
                with patch.object(loaded, 'remove_originals', side_effect=AssertionError('no repeated original deletion')), \
                        patch.object(loaded, 'remove_archive', side_effect=AssertionError('no repeated archive deletion')):
                    for action, value in (('tree-purge-apply', request), ('tree-purge-discard', request),
                            ('tree-purge-status', {key: request[key] for key in ('operationId', 'owner')})):
                        self.assertEqual(self.invoke_main(action, value, engine=loaded), result)
                self.assertEqual(driver.tree_purging_engine().lookup({key: request[key] for key in ('operationId', 'owner')}), result)
                self.assertEqual(survivors, self.snapshot(fixture))

    def test_main_unknown_only_observes_and_never_resumes_deletion(self):
        with self.captured() as fixture:
            request = support_tests.TreePurgingTests.request(fixture, 'Originals')
            fixture.engine.checkpoint = lambda phase: setattr(fixture.engine, 'cancelled', lambda: True) if phase == 'OriginalsQuarantined' else None
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'):
                self.invoke_main('tree-purge-apply', request, engine=fixture.engine)
            fixture.engine.cancelled = None
            before = self.snapshot(fixture)
            with patch.object(fixture.engine, 'remove_originals', side_effect=AssertionError('Unknown does not resume')), \
                    patch.object(fixture.engine, 'remove_archive', side_effect=AssertionError('Unknown does not resume')):
                result = self.invoke_main('tree-purge-status', {key: request[key] for key in ('operationId', 'owner')}, engine=fixture.engine)
                self.assertEqual(result['state'], 'Unknown'); self.assertTrue(result['deletionStarted'])
                self.assertEqual(self.invoke_main('tree-purge-apply', request, engine=fixture.engine), result)
                self.assertEqual(self.invoke_main('tree-purge-discard', request, engine=fixture.engine), result)
            self.assertEqual(before, self.snapshot(fixture))

    def test_trailing_input_and_unapproved_verbs_reject_before_loading_or_mutation(self):
        with self.captured() as fixture:
            request = support_tests.TreePurgingTests.request(fixture)
            before = self.snapshot(fixture)
            for action, value in (('tree-purge-plan', fixture.command), ('tree-purge-apply', request),
                    ('tree-purge-status', {key: request[key] for key in ('operationId', 'owner')}),
                    ('tree-purge-discard', request)):
                with self.subTest(action=action), patch.object(driver, 'tree_purging_engine', side_effect=AssertionError('trailing input must precede loader')):
                    with self.assertRaises(driver.Rejected): self.invoke_main(action, value, trailing=b'{}\n')
            for action in ('tree-purge-delete', 'tree-purge-shell', 'tree-purge', 'tree-purge-apply;true', 'tree-purge-apply --force'):
                with self.subTest(action=action), patch.object(driver, 'tree_purging_engine', side_effect=AssertionError('unapproved verb must precede loader')):
                    with self.assertRaisesRegex(driver.Rejected, '^FILES_ACTION_INVALID$'): self.invoke_main(action, request)
            self.assertEqual(before, self.snapshot(fixture))
            self.assertFalse((fixture.state / fixture.engine.FOLDER).exists())

    def test_all_legacy_restore_and_recycle_verbs_remain_separate_dispatch(self):
        # Dispatch-only sentinels are deliberate test doubles; no claim is made
        # that these execute a legacy restore. Real purge I/O is tested above.
        for prefix, factory, apply_method in (
                ('tree-trash', 'tree_recycling_engine', 'recycle'),
                ('tree-restore', 'tree_restoring_engine', 'restore'),
                ('tree-overwrite-restore', 'tree_overwrite_restoring_engine', 'restore')):
            for suffix, method in (('plan', 'plan'), ('apply', apply_method), ('status', 'lookup'), ('discard', 'discard')):
                with self.subTest(prefix=prefix, suffix=suffix):
                    selected = Mock(return_value={'legacy': prefix, 'verb': suffix})
                    legacy = SimpleNamespace(**{method: selected})
                    request = {'dispatchOnly': True}
                    with patch.object(driver, factory, return_value=legacy) as old_factory, \
                            patch.object(driver, 'tree_purging_engine', side_effect=AssertionError('legacy never invokes purge')):
                        self.assertEqual(self.invoke_main(prefix + '-' + suffix, request), {'legacy': prefix, 'verb': suffix})
                        old_factory.assert_called_once_with(); selected.assert_called_once_with(request)


if __name__ == '__main__': unittest.main()
