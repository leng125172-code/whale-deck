"""Real isolated Linux filesystem tests; synthetic pinned Docker scope.

These prove internal adapter semantics only, not deployed RPC/BFF/UI support.
"""
import json
import os
import stat
import unittest
from unittest.mock import patch
import uuid

import test_container_file_directories as common

scope_module = common.common.load('whaledeck-container-copy-scope.py', 'container_copy_scope')
operations = common.common.load('whaledeck-file-operations.py', 'container_copy_file_engine')
copies = common.common.load('whaledeck-tree-copies.py', 'container_copy_tree_engine')
trees = common.common.load('whaledeck-file-tree.py', 'container_copy_manifest_engine')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerCopyScopeTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.LinuxContainerDirectoryTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root, self.session = self.fixture.root, self.fixture.session
        self.state = self.fixture.fixture.base / 'copies'; self.state.mkdir(mode=0o700)
        self.source = self.root / '中文.bin'
        self.content = self.fixture.fixture.content
        self.source.write_bytes(self.content); self.source.chmod(0o640)
        os.utime(self.source, ns=(1700000000123456789, 1700000000123456789))

    def command(self, source='中文.bin', target='复制.bin'):
        return {**self.session.request, 'sourcePath': source, 'targetPath': target,
                'expectedVersion': self.fixture.r.version(self.session.request, source, (self.root / source).stat())}

    def scope(self, command=None, state=None, **kwargs):
        return scope_module.ContainerCopyScope(self.fixture.u, self.fixture.publisher, self.session,
            self.command() if command is None else command, self.state if state is None else state, **kwargs)

    @staticmethod
    def request(plan): return {'operationId': uuid.uuid4().hex, 'owner': common.common.OWNER, 'plan': plan}

    def tree(self):
        path = self.root / '项目'; path.mkdir(mode=0o750)
        (path / 'empty').mkdir(mode=0o700)
        (path / '模块').mkdir(mode=0o755)
        (path / '模块' / '代码.txt').write_bytes(self.content)
        (path / '模块' / '代码.txt').chmod(0o640)
        for child in (path / '模块' / '代码.txt', path / '模块', path / 'empty', path):
            os.utime(child, ns=(1700000000123456789, 1700000000123456789))
        return path

    def test_read_only_plan_translates_container_etag_without_creating_state(self):
        scope = self.scope(); before = self.root.stat(); names = set(self.root.iterdir())
        plan = operations.FileOperations(scope).plan(scope.file_command())
        self.assertNotEqual(self.command()['expectedVersion'], plan['command']['expectedVersion'])
        self.assertEqual(self.fixture.f.version(self.source.stat()), plan['command']['expectedVersion'])
        self.assertEqual(len(self.content), plan['sizeBytes']); self.assertEqual([], list(self.state.iterdir()))
        self.assertEqual(names, set(self.root.iterdir())); self.assertEqual(before.st_mtime_ns, self.root.stat().st_mtime_ns)

    def test_file_copy_full_content_mode_mtime_kernel_owner_and_idempotency(self):
        scope = self.scope(); engine = operations.FileOperations(scope)
        request = self.request(engine.plan(scope.file_command())); before = self.fixture.r.fingerprint(self.source.stat())
        result = engine.apply(request); target = self.root / '复制.bin'
        self.assertEqual('Completed', result['state']); self.assertEqual(self.content, target.read_bytes())
        self.assertEqual(before, self.fixture.r.fingerprint(self.source.stat()))
        self.assertEqual(0o640, stat.S_IMODE(target.stat().st_mode)); self.assertEqual(self.source.stat().st_mtime_ns, target.stat().st_mtime_ns)
        self.assertEqual(scope.owner, (target.stat().st_uid, target.stat().st_gid))
        inode = target.stat().st_ino; self.assertEqual(result, engine.apply(request)); self.assertEqual(inode, target.stat().st_ino)
        self.assertTrue((self.state / 'operations' / (request['operationId'] + '.json')).exists())
        self.assertFalse((self.root / '.whaledeck-files-v1').exists())

    def test_directory_copy_full_tree_empty_folders_and_metadata_without_removing_source(self):
        source = self.tree(); command = self.command('项目', '项目副本'); scope = self.scope(command)
        engine = copies.FileTreeCopies(scope, trees.FileTreeManifest)
        request = self.request(engine.plan(scope.tree_command())); result = engine.copy(request)
        self.assertEqual('Completed', result['state']); target = self.root / '项目副本'
        self.assertEqual(self.content, (target / '模块' / '代码.txt').read_bytes())
        self.assertTrue((target / 'empty').is_dir()); self.assertTrue(source.exists())
        for relative in ('', '模块', '模块/代码.txt', 'empty'):
            original, actual = (source / relative).stat(), (target / relative).stat()
            self.assertEqual(stat.S_IMODE(original.st_mode), stat.S_IMODE(actual.st_mode))
            self.assertEqual(original.st_mtime_ns, actual.st_mtime_ns)
            self.assertEqual(scope.owner, (actual.st_uid, actual.st_gid))
        self.assertEqual(result, engine.copy(request))

    def test_host_registry_and_global_namespace_are_never_used(self):
        with patch.object(self.fixture.f, 'roots', side_effect=AssertionError('host registry')):
            with patch.object(self.fixture.f, 'directory', side_effect=AssertionError('host path')), \
                    patch.object(self.fixture.f, 'namespace_lock', side_effect=AssertionError('host namespace')), \
                    patch.object(self.fixture.f, 'staging', side_effect=AssertionError('host staging')):
                scope = self.scope(); engine = operations.FileOperations(scope)
                self.assertEqual('Completed', engine.apply(self.request(engine.plan(scope.file_command())))['state'])

    def test_source_readonly_mount_is_allowed_but_target_readonly_and_mount_root_are_refused(self):
        source = self.root / 'read'; source.mkdir(); (source / 'data').write_bytes(self.content)
        target = self.root / 'write'; target.mkdir()
        self.session.container = {'HostConfig': {'ReadonlyRootfs': True},
            'Mounts': [{'Destination': '/read', 'RW': False}, {'Destination': '/write', 'RW': True}]}
        scope = self.scope(self.command('read/data', 'write/data')); engine = operations.FileOperations(scope)
        self.assertEqual('Completed', engine.apply(self.request(engine.plan(scope.file_command())))['state'])
        for destination in ('read/new', 'write'):
            scope = self.scope(self.command('read/data', destination))
            with self.assertRaisesRegex(self.fixture.r.Rejected, 'PUBLISH_(READ_ONLY|MOUNT_ROOT)'):
                operations.FileOperations(scope).plan(scope.file_command())

    def test_protected_noncanonical_and_ancestor_paths_are_rejected_before_state(self):
        for target in ('../out', '/absolute', 'a//b', 'proc/new', 'run/new', 'var/run/new', '.env', 'safe/shadow', '.whaledeck-private/new', 'a\x80b'):
            with self.subTest(target=target), self.assertRaises((self.fixture.r.Rejected, self.fixture.f.Rejected)):
                self.scope({**self.command(), 'targetPath': target})
        for source, target in (('a', 'a'), ('a', 'a/b'), ('a/b', 'a')):
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'OVERLAP'):
                self.scope({**self.command(), 'sourcePath': source, 'targetPath': target})
        self.assertEqual([], list(self.state.iterdir()))

    def test_source_version_and_foreign_root_or_location_are_refused(self):
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'VERSION_CONFLICT'):
            self.scope({**self.command(), 'expectedVersion': 'F' * 64}).file_command()
        scope = self.scope()
        for location in ({'rootId': 'apps', 'path': '/etc/passwd'}, {'rootId': scope.SOURCE, 'path': 'other'},
                         {'rootId': scope.TARGET, 'path': '复制.bin', 'uid': 0}):
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'LOCATION_INVALID'):
                with scope.directory(location, 'mutate'): pass

    def test_changed_container_mount_proof_instance_and_kernel_owner_refused(self):
        scope = self.scope(); self.session.mounts = b'Z' * 32
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'TARGET_CHANGED'): scope.file_command()
        self.session.mounts = b'M' * 32
        with patch.object(self.fixture.publisher, 'owner', return_value=[123, 456]):
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'OWNER_CHANGED'): scope.file_command()
        self.session.request['instanceId'] = '777:2026-10-27T01:02:03Z'
        with self.assertRaisesRegex(self.fixture.r.Rejected, 'SESSION_SCOPE_MISMATCH'): scope.file_command()

    def test_symlink_parents_and_source_hardlinks_or_special_bits_are_refused(self):
        outside = self.fixture.fixture.base / 'outside'; outside.mkdir()
        (self.root / 'link').symlink_to(outside, target_is_directory=True)
        scope = self.scope(self.command(target='link/new'))
        with self.assertRaises(OSError): operations.FileOperations(scope).plan(scope.file_command())
        os.link(self.source, self.root / 'hard')
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'SINGLE_LINK'): self.scope().file_command()
        (self.root / 'hard').unlink(); self.source.chmod(0o4640)
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'SPECIAL_MODE'): self.scope().file_command()
        self.assertEqual([], list(outside.iterdir()))

    def test_tree_protected_descendant_symlink_hardlink_and_special_mode_refused(self):
        source = self.tree()
        for kind in ('protected', 'symlink', 'hardlink', 'mode'):
            child = source / ('.env' if kind == 'protected' else 'bad')
            if kind == 'symlink': child.symlink_to(self.source)
            elif kind == 'hardlink': os.link(self.source, child)
            else:
                child.write_bytes(b'keep')
                if kind == 'mode': child.chmod(0o4644)
            scope = self.scope(self.command('项目', '项目副本'))
            with self.subTest(kind=kind), self.assertRaises(self.fixture.f.Rejected):
                copies.FileTreeCopies(scope, trees.FileTreeManifest).plan(scope.tree_command())
            child.unlink()
        self.assertFalse((self.root / '项目副本').exists())

    def test_source_changed_after_plan_never_publishes(self):
        scope = self.scope(); engine = operations.FileOperations(scope); request = self.request(engine.plan(scope.file_command()))
        self.source.write_bytes(b'changed')
        with self.assertRaises(self.fixture.f.Rejected): engine.apply(request)
        self.assertFalse((self.root / '复制.bin').exists())

    def test_atomic_destination_race_preserves_external_file(self):
        scope = self.scope(); engine = operations.FileOperations(scope); request = self.request(engine.plan(scope.file_command()))
        rename = self.fixture.f.exchange
        def race(*args):
            (self.root / '复制.bin').write_bytes(b'external')
            return rename(*args)
        with patch.object(self.fixture.f, 'exchange', side_effect=race):
            with self.assertRaises(FileExistsError): engine.apply(request)
        self.assertEqual(b'external', (self.root / '复制.bin').read_bytes()); self.assertEqual(self.content, self.source.read_bytes())
        self.assertEqual('Aborted', json.loads((self.state / 'operations' / (request['operationId'] + '.json')).read_text())['state'])

    def test_publication_revalidates_container_after_staging_and_keeps_unknown_evidence(self):
        scope = self.scope(); engine = operations.FileOperations(scope); request = self.request(engine.plan(scope.file_command()))
        save = engine.save
        def interrupt(state, record):
            save(state, record)
            if record['state'] == 'DestinationApplying': self.session.mounts = b'Z' * 32
        with patch.object(engine, 'save', side_effect=interrupt):
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'TARGET_CHANGED'): engine.apply(request)
        record = json.loads((self.state / 'operations' / (request['operationId'] + '.json')).read_text())
        self.assertEqual('Unknown', record['state']); self.assertFalse((self.root / '复制.bin').exists())
        pending = self.root / scope.PRIVATE / (request['operationId'] + '.mutation')
        self.assertEqual(self.content, pending.read_bytes())
        with patch.object(self.fixture.f, 'exchange', side_effect=AssertionError('no replay')):
            self.assertEqual('Unknown', engine.apply(request)['state'])

    def test_target_parent_swap_before_publication_is_refused_without_writing_new_parent(self):
        work = self.root / 'work'; work.mkdir()
        scope = self.scope(self.command(target='work/copy')); engine = operations.FileOperations(scope)
        request = self.request(engine.plan(scope.file_command())); save = engine.save
        def swap(state, record):
            save(state, record)
            if record['state'] == 'DestinationApplying':
                work.rename(self.root / 'old-work'); work.mkdir()
        with patch.object(engine, 'save', side_effect=swap):
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'PARENT_CHANGED'): engine.apply(request)
        self.assertEqual([], list(work.iterdir())); self.assertFalse((self.root / 'old-work' / 'copy').exists())
        self.assertTrue((self.root / 'old-work' / scope.PRIVATE / (request['operationId'] + '.mutation')).is_file())

    def test_directory_partial_stage_recovery_does_not_repeat_copy_or_delete_evidence(self):
        self.tree(); scope = self.scope(self.command('项目', '项目副本')); engine = copies.FileTreeCopies(scope, trees.FileTreeManifest)
        request = self.request(engine.plan(scope.tree_command()))
        def interrupt(phase):
            if phase == 'Staging': raise RuntimeError('interrupted')
        with patch.object(engine, 'checkpoint', side_effect=interrupt):
            with self.assertRaises(RuntimeError): engine.copy(request)
        pending = self.root / scope.PRIVATE / (request['operationId'] + '.tree-copy')
        self.assertTrue(pending.is_dir()); self.assertFalse((self.root / '项目副本').exists())
        with patch.object(engine, 'populate', side_effect=AssertionError('no replay')):
            result = engine.copy(request)
        self.assertEqual('Aborted', result['state']); self.assertTrue(result['partialRetained']); self.assertTrue(pending.is_dir())

    def test_resumed_scope_uses_original_sealed_target_parent(self):
        work = self.root / 'work'; work.mkdir(); command = self.command(target='work/copy')
        scope = self.scope(command); engine = operations.FileOperations(scope)
        plan = engine.plan(scope.file_command())
        work.rename(self.root / 'original-work'); work.mkdir()
        resumed = self.scope(command, target_parent=plan['targetParent'])
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'PARENT_CHANGED'):
            with resumed.directory({'rootId': resumed.TARGET, 'path': 'work/copy'}, 'mutate'): pass
        self.assertEqual([], list(work.iterdir()))
        for invalid in ([1, True], [1, 0], [1], '1,2', [1, -2]):
            with self.subTest(invalid=invalid), self.assertRaisesRegex(self.fixture.f.Rejected, 'PARENT_INVALID'):
                self.scope(command, target_parent=invalid)

    def test_namespace_lock_is_dedicated_nonblocking_and_metadata_survives_liveness_loss(self):
        scope = self.scope()
        with scope.namespace_lock():
            with self.assertRaisesRegex(self.fixture.f.Rejected, 'BUSY'):
                with scope.namespace_lock(): pass
        self.session.revalidate.side_effect = self.fixture.r.Rejected('CONTAINER_FILES_TARGET_UNAVAILABLE')
        with scope.namespace_lock(): pass
        with self.assertRaises(self.fixture.r.Rejected): scope.file_command()
        self.assertEqual(0o600, stat.S_IMODE((self.state / 'namespace.lock').stat().st_mode))

    def test_symlink_state_and_unsafe_private_directory_preserve_foreign_content(self):
        link = self.fixture.fixture.base / 'state-link'; link.symlink_to(self.state, target_is_directory=True)
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'STATE_UNSAFE'): self.scope(state=link)
        self.state.chmod(0o755)
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'STATE_UNSAFE'): self.scope()
        self.state.chmod(0o700)
        private = self.root / scope_module.ContainerCopyScope.PRIVATE; private.mkdir(mode=0o755)
        (private / 'foreign').write_bytes(b'keep')
        scope = self.scope(); engine = operations.FileOperations(scope)
        with self.assertRaisesRegex(self.fixture.f.Rejected, 'PRIVATE_UNSAFE'):
            engine.apply(self.request(engine.plan(scope.file_command())))
        self.assertEqual(b'keep', (private / 'foreign').read_bytes()); self.assertFalse((self.root / '复制.bin').exists())


if __name__ == '__main__': unittest.main()
