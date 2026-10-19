"""Real disposable Linux move/crash/race I/O. Never an installed RPC claim."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import shutil
import stat
import subprocess
import sys
import unittest
import uuid
import test_file_transport as common
from test_file_tree_manifest import tree_module
from test_tree_copies import copies

spec = importlib.util.spec_from_file_location('tree_moves', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-moves.py')
moves = importlib.util.module_from_spec(spec); spec.loader.exec_module(moves)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires real Linux root I/O')
class FileTreeMoveTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root, self.base, self.registry = self.fixture.root, self.fixture.base, self.fixture.registry
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'] += ['files-mutate', 'files-tree-copy', 'files-tree-move']; self.registry.write_text(json.dumps(policy))
        self.source = self.root / 'source'; self.source.mkdir(); (self.source / '空目录').mkdir(); (self.source / '代码').mkdir()
        self.content = bytes(range(256)) * 257 + b'\x00\xff'; self.file = self.source / '代码/run.bin'; self.file.write_bytes(self.content); self.file.chmod(0o751)
        self.engine = moves.FileTreeMoves(driver, tree_module.FileTreeManifest, copies.FileTreeCopies)

    def tearDown(self): self.fixture.tearDown()

    def command(self, target='target'):
        return {'source': {'rootId': 'test.shared', 'path': 'source', 'expectedVersion': driver.version(self.source.stat())}, 'target': {'rootId': 'test.shared', 'path': target}}

    def request(self, target='target'):
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(self.command(target))}

    def receipt(self, request): return self.fixture.state / 'tree-moves' / (request['operationId'] + '.json')

    def retained(self, request): return self.root / driver.STAGING / (request['operationId'] + '.tree-move')

    def lookup(self, request): return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def test_renamed_private_base_during_retention_is_unknown_and_preserves_published_and_original_bytes(self):
        request = self.request(); moved = self.root / 'private-relocated'; fired = False
        def rename(phase):
            nonlocal fired
            if phase == 'SourceRetained' and not fired:
                fired = True; self.retained(request).parent.rename(moved)
        self.engine.checkpoint = rename
        with self.assertRaises(OSError): self.engine.move(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertTrue(self.source.exists())
        self.assertEqual((self.root / 'target/代码/run.bin').read_bytes(), self.content)
        self.assertEqual((moved / (request['operationId'] + '.tree-move') / '代码/run.bin').read_bytes(), self.content)

    def test_source_parent_replaced_after_validation_does_not_modify_detached_original(self):
        parent = self.root / 'parent'; parent.mkdir(); self.source.rename(parent / 'source')
        self.source = parent / 'source'; self.file = self.source / '代码/run.bin'
        self.command = lambda target='target': {'source': {'rootId': 'test.shared', 'path': 'parent/source', 'expectedVersion': driver.version(self.source.stat())}, 'target': {'rootId': 'test.shared', 'path': target}}
        request = self.request(); original = driver.publication_identity(self.file.stat())
        def replace(phase):
            if phase == 'SourceValidated': parent.rename(self.root / 'parent-relocated'); parent.mkdir()
        self.engine.checkpoint = replace
        with self.assertRaises(driver.Rejected): self.engine.move(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown')
        preserved = self.root / 'parent-relocated/source/代码/run.bin'
        self.assertEqual((preserved.read_bytes(), driver.publication_identity(preserved.stat())), (self.content, original))
        self.assertEqual((self.root / 'target/代码/run.bin').read_bytes(), self.content)

    def test_complete_retains_original_inodes_and_publishes_independent_full_tree(self):
        before = self.file.stat(); request = self.request(); result = self.engine.move(request)
        self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRemoved']); self.assertTrue(result['sourceRetained']); self.assertTrue(result['destinationPublished'])
        self.assertFalse(result['partialRetained'])
        self.assertFalse(self.source.exists()); target = self.root / 'target/代码/run.bin'; retained = self.retained(request) / '代码/run.bin'
        self.assertEqual(target.read_bytes(), self.content); self.assertEqual(retained.read_bytes(), self.content)
        self.assertEqual(retained.stat().st_ino, before.st_ino); self.assertNotEqual(target.stat().st_ino, before.st_ino)
        self.assertEqual((target.stat().st_mtime_ns, stat.S_IMODE(target.stat().st_mode)), (before.st_mtime_ns, 0o751))
        self.assertTrue((self.root / 'target/空目录').is_dir()); self.assertTrue((self.retained(request) / '空目录').is_dir())
        self.assertEqual(self.engine.move(request), result); self.assertEqual(self.lookup(request), result)
        self.assertLess(self.receipt(request).stat().st_size, 32768)

    def test_completed_history_never_recreates_a_later_moved_destination(self):
        request = self.request(); result = self.engine.move(request); (self.root / 'target').rename(self.root / 'external-target')
        self.assertEqual(self.engine.move(request), result); self.assertFalse((self.root / 'target').exists()); self.assertFalse(self.source.exists())
        self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), self.content)

    def test_explicit_move_flag_required_on_both_roots_not_granted_by_copy(self):
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-tree-move'); self.registry.write_text(json.dumps(policy))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.request()
        self.assertEqual(self.file.read_bytes(), self.content); self.assertFalse((self.root / 'target').exists())

    def test_move_flag_does_not_enable_copy_and_copy_plan_cannot_be_reinterpreted(self):
        command = self.command(); copy_plan = copies.FileTreeCopies(driver, tree_module.FileTreeManifest).plan(command)
        plan = {'copyPlan': copy_plan, 'sourceParent': [self.root.stat().st_dev, self.root.stat().st_ino]}
        with self.assertRaisesRegex(driver.Rejected, '^FILES_PLAN_INVALID$'):
            self.engine.validate({**plan, 'fingerprint': self.engine.digest(plan)})
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-tree-copy'); self.registry.write_text(json.dumps(policy))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): copies.FileTreeCopies(driver, tree_module.FileTreeManifest).plan(command)
        self.engine.plan(command)

    def test_wrong_owner_and_rebound_plan_cannot_take_completed_or_canceled_operation(self):
        request = self.request(); result = self.engine.discard(request)
        self.assertEqual(result['state'], 'Aborted'); self.assertEqual(self.engine.move(request), result)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'): self.engine.move({**request, 'owner': 'B' * 64})
        other = self.request('other')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_IDEMPOTENCY_CONFLICT$'): self.engine.move({**request, 'plan': other['plan']})
        self.assertTrue(self.source.exists()); self.assertFalse((self.root / 'target').exists())

    def test_late_protected_and_ordinary_members_are_never_moved_with_an_ancestor(self):
        for index, name in enumerate(('.env', 'late.txt', '代码/.env')):
            with self.subTest(name=name):
                request = self.request('target-late-' + str(index))
                def insert(phase):
                    if phase == 'SourceValidated': (self.source / name).write_bytes(b'late-must-stay')
                self.engine.checkpoint = insert
                with self.assertRaises(OSError): self.engine.move(request)
                self.assertEqual((self.source / name).read_bytes(), b'late-must-stay'); self.assertEqual(self.lookup(request)['state'], 'Unknown')
                self.assertFalse((self.retained(request) / name).exists()); self.assertFalse((self.root / request['plan']['copyPlan']['command']['target']['path'] / name).exists())
                self.tearDown(); self.setUp()

    def test_late_member_at_final_rmdir_is_preserved_without_recursive_cleanup(self):
        request = self.request()
        def insert(phase):
            if phase == 'SourceRemoving' and json.loads(self.receipt(request).read_bytes())['currentPath'] == '': (self.source / '.env').write_bytes(b'late-protected')
        self.engine.checkpoint = insert
        with self.assertRaises(OSError): self.engine.move(request)
        self.assertEqual((self.source / '.env').read_bytes(), b'late-protected'); self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), self.content)

    def test_source_leaf_replacement_is_retained_and_never_deleted_or_claimed_success(self):
        request = self.request(); replaced = False
        def replace(phase):
            nonlocal replaced
            if phase == 'SourceRetaining' and not replaced:
                replaced = True; self.file.rename(self.root / 'external-original'); self.file.write_bytes(b'foreign-replacement')
        self.engine.checkpoint = replace
        with self.assertRaises(driver.Rejected): self.engine.move(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertEqual((self.root / 'external-original').read_bytes(), self.content)
        self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), b'foreign-replacement'); self.assertEqual((self.root / 'target/代码/run.bin').read_bytes(), self.content)

    def test_destination_changed_before_source_retention_does_not_remove_source(self):
        request = self.request()
        def change(phase):
            if phase == 'DestinationCompleted': (self.root / 'target/代码/run.bin').write_bytes(b'changed-target')
        self.engine.checkpoint = change
        with self.assertRaises(driver.Rejected): self.engine.move(request)
        self.assertEqual(self.file.read_bytes(), self.content); self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertFalse(self.retained(request).exists())

    def test_source_replaced_after_destination_keeps_both_external_trees(self):
        request = self.request()
        def change(phase):
            if phase == 'DestinationCompleted':
                self.source.rename(self.root / 'external-source'); self.source.mkdir(); (self.source / '.env').write_bytes(b'new-source')
        self.engine.checkpoint = change
        with self.assertRaises(driver.Rejected): self.engine.move(request)
        self.assertEqual((self.root / 'external-source/代码/run.bin').read_bytes(), self.content); self.assertEqual((self.source / '.env').read_bytes(), b'new-source')
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_policy_revoked_after_destination_prevents_source_mutation(self):
        request = self.request()
        def revoke(phase):
            if phase == 'DestinationCompleted':
                policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-tree-move'); self.registry.write_text(json.dumps(policy))
        self.engine.checkpoint = revoke
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.move(request)
        self.assertEqual(self.file.read_bytes(), self.content); self.assertFalse(self.retained(request).exists()); self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_cancel_after_destination_is_unknown_not_false_success_or_automatic_rollback(self):
        request = self.request(); canceled = False
        def cancel(phase):
            nonlocal canceled
            if phase == 'DestinationCompleted': canceled = True
        self.engine.checkpoint = cancel; self.engine.cancelled = lambda: canceled
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.move(request)
        result = self.lookup(request); self.assertEqual(result['state'], 'Unknown'); self.assertTrue(result['destinationPublished']); self.assertFalse(result['sourceRemoved'])
        self.assertEqual(self.file.read_bytes(), self.content); self.assertEqual((self.root / 'target/代码/run.bin').read_bytes(), self.content)

    def test_real_sigkill_phases_never_resume_source_retention_or_republish(self):
        phases = ('Preparing', 'CopyStaging', 'CopyReady', 'CopyPublishing', 'CopyPublished', 'DestinationCompleted', 'SourceValidated', 'SourcePrepared', 'SourceRetaining', 'SourceRetained', 'SourceRemoving', 'SourceRemoved', 'Completed')
        for phase in phases:
            with self.subTest(phase=phase):
                request = self.request('target-' + phase.lower()); pid = os.fork()
                if pid == 0:
                    self.engine.checkpoint = lambda actual: os.kill(os.getpid(), signal.SIGKILL) if actual == phase else None
                    try: self.engine.move(request)
                    finally: os._exit(7)
                _, status = os.waitpid(pid, 0); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                before = sorted(str(x.relative_to(self.root)) for x in self.root.rglob('*'))
                result = self.lookup(request)
                expected = 'Completed' if phase == 'Completed' else 'Aborted' if phase in ('Preparing', 'CopyStaging', 'CopyReady', 'CopyPublishing') else 'Unknown'
                self.assertEqual(result['state'], expected); self.assertEqual(self.engine.move(request), result)
                self.assertEqual(sorted(str(x.relative_to(self.root)) for x in self.root.rglob('*')), before)
                # Each phase needs a new source; never repair a production tree.
                self.tearDown(); self.setUp()

    def test_killed_after_last_empty_source_removal_can_prove_completion_without_replay(self):
        request = self.request(); pid = os.fork()
        if pid == 0:
            def crash(phase):
                if phase == 'SourceRemoved' and json.loads(self.receipt(request).read_bytes())['currentPath'] == '': os.kill(os.getpid(), signal.SIGKILL)
            self.engine.checkpoint = crash
            try: self.engine.move(request)
            finally: os._exit(7)
        _, status = os.waitpid(pid, 0); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
        self.assertFalse(self.source.exists()); result = self.lookup(request); self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRetained'])
        self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), self.content); self.assertEqual(self.engine.move(request), result)

    def test_cross_device_destination_uses_target_copy_and_same_source_retention(self):
        target = self.base / 'mounted'; target.mkdir(); subprocess.run(['mount', '-t', 'tmpfs', '-o', 'size=1g', 'tmpfs', str(target)], check=True)
        try:
            policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0], 'id': 'test.other', 'path': str(target), 'ownerUid': 1000, 'ownerGid': 1000}); self.registry.write_text(json.dumps(policy))
            command = self.command(); command['target']['rootId'] = 'test.other'; request = {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(command)}
            self.assertNotEqual(self.source.stat().st_dev, target.stat().st_dev); result = self.engine.move(request)
            self.assertEqual(result['state'], 'Completed'); self.assertFalse(self.source.exists()); self.assertEqual((target / 'target/代码/run.bin').read_bytes(), self.content)
            self.assertEqual((target / 'target/代码/run.bin').stat().st_uid, 1000); self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), self.content)
        finally: subprocess.run(['umount', str(target)], check=True)

    def test_private_manifest_corruption_after_kill_is_unknown_and_never_executes_more(self):
        request = self.request(); pid = os.fork()
        if pid == 0:
            def crash(phase):
                if phase == 'SourceRemoved' and json.loads(self.receipt(request).read_bytes())['currentPath'] == '': os.kill(os.getpid(), signal.SIGKILL)
            self.engine.checkpoint = crash
            try: self.engine.move(request)
            finally: os._exit(7)
        os.waitpid(pid, 0); path = self.fixture.state / 'tree-moves' / (request['operationId'] + '.manifest.json'); path.write_bytes(b'{}')
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertFalse(self.source.exists())
        self.assertEqual((self.retained(request) / '代码/run.bin').read_bytes(), self.content)

    def test_single_file_larger_than_legacy_upload_limit_is_fully_moved_and_hashed(self):
        size = 1024**3 + 7
        with self.file.open('wb') as stream: stream.truncate(size)
        before = self.file.stat(); request = self.request(); result = self.engine.move(request)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['totalBytes'], size)
        target = self.root / 'target/代码/run.bin'; retained = self.retained(request) / '代码/run.bin'
        with target.open('rb') as stream: actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        with retained.open('rb') as stream: original = hashlib.file_digest(stream, 'sha256').hexdigest()
        self.assertEqual(actual, original); self.assertEqual(retained.stat().st_ino, before.st_ino); self.assertNotEqual(target.stat().st_ino, before.st_ino)
        self.assertEqual((target.stat().st_size, retained.stat().st_size), (size, size)); self.assertFalse(self.source.exists())

    def test_source_is_protected_against_same_descendant_and_protected_targets(self):
        for target in ('source', 'source/child'):
            with self.subTest(target=target), self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_OVERLAP$'): self.request(target)
        (self.source / '.env').touch()
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_PROTECTED$'): self.request()

    def test_source_bind_mount_is_rejected_even_when_backed_by_the_same_device(self):
        external = self.base / 'external-bind'; external.mkdir(); (external / 'keep').write_bytes(b'outside')
        mounted = self.source / 'bound'; mounted.mkdir()
        subprocess.run(['mount', '--bind', str(external), str(mounted)], check=True)
        try:
            self.assertEqual(external.stat().st_dev, self.source.stat().st_dev)
            with self.assertRaises(OSError): self.request()
            self.assertFalse((self.root / 'target').exists()); self.assertEqual((external / 'keep').read_bytes(), b'outside')
        finally: subprocess.run(['umount', str(mounted)], check=True)

    def test_real_bind_alias_target_cannot_place_destination_inside_source(self):
        alias = self.base / 'source-alias'; alias.mkdir(); subprocess.run(['mount', '--bind', str(self.source), str(alias)], check=True)
        try:
            policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0], 'id': 'test.alias', 'path': str(alias)}); self.registry.write_text(json.dumps(policy))
            command = self.command('inside'); command['target']['rootId'] = 'test.alias'
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_OVERLAP$'): self.engine.plan(command)
            self.assertFalse((alias / 'inside').exists()); self.assertEqual(self.file.read_bytes(), self.content)
        finally: subprocess.run(['umount', str(alias)], check=True)

    def test_legacy_copy_digest_and_state_namespace_remain_unchanged(self):
        engine = copies.FileTreeCopies(driver, tree_module.FileTreeManifest); value = {'x': '中文'}
        self.assertEqual(engine.digest(value), hashlib.sha256(b'whaledeck-tree-copy-v1\n' + engine.encode(value)).hexdigest().upper())
        self.assertEqual(engine.STATE_DIRECTORY, 'tree-copies'); self.assertNotEqual(self.engine.copy.digest(value), engine.digest(value))

    def test_entirely_empty_tree_moves_and_retains_empty_directory_structure(self):
        self.file.unlink(); request = self.request(); result = self.engine.move(request)
        self.assertEqual((result['state'], result['entryCount'], result['totalBytes']), ('Completed', 3, 0))
        self.assertFalse(self.source.exists()); self.assertTrue((self.root / 'target/代码').is_dir()); self.assertTrue((self.retained(request) / '空目录').is_dir())

    def test_large_unicode_private_manifest_is_bounded_without_entering_public_receipt(self):
        for index in range(400): (self.source / ('文件-' + str(index) + '-中' * 20)).write_bytes(b'content')
        request = self.request(); result = self.engine.move(request); self.assertEqual(result['state'], 'Completed')
        manifest = self.fixture.state / 'tree-moves' / (request['operationId'] + '.manifest.json')
        self.assertGreater(manifest.stat().st_size, 65536); self.assertLess(manifest.stat().st_size, self.engine.MAX_MANIFEST)
        self.assertLess(self.receipt(request).stat().st_size, 32768); self.assertEqual(json.loads(manifest.read_bytes())['entryCount'], 404)

    def test_same_operation_lock_refuses_concurrent_start_before_any_receipt_or_copy(self):
        request = self.request(); ready_read, ready_write = os.pipe(); release_read, release_write = os.pipe(); pid = os.fork()
        if pid == 0:
            os.close(ready_read); os.close(release_write)
            try:
                with self.engine.state(request['operationId'], request['owner'], True): os.write(ready_write, b'1'); os.read(release_read, 1)
            finally: os._exit(0)
        os.close(ready_write); os.close(release_read)
        try:
            self.assertEqual(os.read(ready_read, 1), b'1')
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.move(request)
            self.assertFalse(self.receipt(request).exists()); self.assertEqual(self.file.read_bytes(), self.content)
        finally:
            os.write(release_write, b'1'); os.close(ready_read); os.close(release_write); os.waitpid(pid, 0)
        self.assertEqual(self.engine.move(request)['state'], 'Completed')

    def test_global_namespace_busy_never_replays_copy_and_does_not_falsify_a_terminal_state(self):
        request = self.request(); killed = os.fork()
        if killed == 0:
            self.engine.checkpoint = lambda phase: os.kill(os.getpid(), signal.SIGKILL) if phase == 'CopyReady' else None
            try: self.engine.move(request)
            finally: os._exit(7)
        _, status = os.waitpid(killed, 0); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
        ready_read, ready_write = os.pipe(); release_read, release_write = os.pipe(); pid = os.fork()
        if pid == 0:
            os.close(ready_read); os.close(release_write)
            try:
                with driver.namespace_lock(): os.write(ready_write, b'1'); os.read(release_read, 1)
            finally: os._exit(0)
        os.close(ready_write); os.close(release_read)
        try:
            self.assertEqual(os.read(ready_read, 1), b'1')
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.move(request)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.lookup(request)
            self.assertEqual(json.loads(self.receipt(request).read_bytes())['state'], 'DestinationCopying')
            self.assertFalse((self.root / 'target').exists()); self.assertEqual(self.file.read_bytes(), self.content)
        finally:
            os.write(release_write, b'1'); os.close(ready_read); os.close(release_write); os.waitpid(pid, 0)
        result = self.lookup(request); self.assertEqual(result['state'], 'Aborted'); self.assertEqual(self.engine.move(request), result)
        self.assertFalse((self.root / 'target').exists())

    def test_missing_child_receipt_after_contention_is_unknown_and_never_reapplied(self):
        request = self.request()
        with driver.namespace_lock():
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.move(request)
            result = self.lookup(request)
            self.assertEqual(result['state'], 'Unknown'); self.assertFalse(result['destinationPublished']); self.assertFalse(result['sourceRemoved'])
        self.assertEqual(self.engine.move(request), result); self.assertEqual(self.file.read_bytes(), self.content); self.assertFalse((self.root / 'target').exists())

    def test_receipt_mode_hardlink_state_and_integer_corruption_are_rejected(self):
        request = self.request(); self.engine.move(request); original = self.receipt(request).read_bytes()
        self.receipt(request).chmod(0o666)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'): self.lookup(request)
        self.receipt(request).chmod(0o600); linked = self.fixture.state / 'linked-receipt'; os.link(self.receipt(request), linked)
        try:
            with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'): self.lookup(request)
        finally: linked.unlink()
        for update in ({'state': 'arbitrary'}, {'sourceRetained': 'true'}, {'retainedIdentity': [2**64, 1]}):
            self.receipt(request).write_text(json.dumps({**json.loads(original), **update}))
            with self.subTest(update=update), self.assertRaises(driver.Rejected): self.lookup(request)
        self.receipt(request).write_bytes(original); self.assertEqual(self.lookup(request)['state'], 'Completed')

    def test_late_directory_replacement_at_empty_removal_is_not_removed(self):
        request = self.request(); replaced = False
        def replace(phase):
            nonlocal replaced
            if phase == 'SourceRemoving' and not replaced:
                path = json.loads(self.receipt(request).read_bytes())['currentPath']
                if path:
                    replaced = True; (self.source / path).rename(self.root / 'external-empty'); (self.source / path).mkdir()
        self.engine.checkpoint = replace
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CHANGED$'): self.engine.move(request)
        self.assertTrue((self.root / 'external-empty').is_dir()); self.assertTrue((self.source / '空目录').is_dir())
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_repeated_rejected_lookups_do_not_leak_descriptors(self):
        request = self.request(); self.engine.move(request); before = len(os.listdir('/proc/self/fd'))
        for _ in range(100):
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
                self.engine.lookup({'operationId': request['operationId'], 'owner': 'B' * 64})
        self.assertEqual(len(os.listdir('/proc/self/fd')), before)

    def test_existing_private_manifest_is_preserved_not_overwritten_before_source_mutation(self):
        request = self.request(); manifest = self.fixture.state / 'tree-moves' / (request['operationId'] + '.manifest.json')
        def insert(phase):
            if phase == 'DestinationCompleted': manifest.write_bytes(b'other-retained-evidence'); manifest.chmod(0o600)
        self.engine.checkpoint = insert
        with self.assertRaises(FileExistsError): self.engine.move(request)
        self.assertEqual(manifest.read_bytes(), b'other-retained-evidence'); self.assertEqual(self.file.read_bytes(), self.content)
        self.assertFalse(self.retained(request).exists()); self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_fixed_driver_loader_uses_exact_trusted_modules_and_registered_move_policy(self):
        from unittest.mock import patch
        modules = self.base / 'installed-modules'; modules.mkdir(mode=0o700)
        for name in ('whaledeck-file-tree.py', 'whaledeck-tree-copies.py', 'whaledeck-tree-moves.py'):
            target = modules / name; shutil.copyfile(Path(driver.__file__).with_name(name), target); target.chmod(0o640)
        with patch.dict(sys.modules, {driver.__name__: driver}), patch.object(driver, '__file__', str(modules / 'whaledeck-files.py')):
            loaded = driver.tree_move_engine(); request = self.request()
            self.assertEqual(loaded.plan(request['plan']['copyPlan']['command']), request['plan'])
            self.assertEqual(loaded.move(request)['state'], 'Completed')
        self.assertEqual((self.root / 'target/代码/run.bin').read_bytes(), self.content); self.assertFalse(self.source.exists())

    def test_move_loader_rejects_writable_foreign_hardlinked_and_symlink_modules(self):
        from unittest.mock import patch
        modules = self.base / 'installed-modules'; modules.mkdir(mode=0o700)
        for name in ('whaledeck-file-tree.py', 'whaledeck-tree-copies.py', 'whaledeck-tree-moves.py'):
            target = modules / name; shutil.copyfile(Path(driver.__file__).with_name(name), target); target.chmod(0o640)
        target = modules / 'whaledeck-tree-moves.py'
        with patch.dict(sys.modules, {driver.__name__: driver}), patch.object(driver, '__file__', str(modules / 'whaledeck-files.py')):
            target.chmod(0o660)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_move_engine()
            target.chmod(0o640); alias = modules / 'linked'; os.link(target, alias)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_move_engine()
            alias.unlink(); os.chown(target, 12345, 0)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_move_engine()
            os.chown(target, 0, 0); original = modules / 'original.py'; target.rename(original); target.symlink_to(original)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_move_engine()
