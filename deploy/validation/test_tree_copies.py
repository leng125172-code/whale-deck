"""Disposable actual Linux tree-copy I/O/crash tests; no installed API claim."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import stat
import subprocess
import unittest
import uuid
import test_file_transport as common
from test_file_tree_manifest import tree_module

spec = importlib.util.spec_from_file_location('tree_copies', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-copies.py')
copies = importlib.util.module_from_spec(spec); spec.loader.exec_module(copies)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires real Linux root I/O')
class FileTreeCopyTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root, self.base, self.registry = self.fixture.root, self.fixture.base, self.fixture.registry
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'] += ['files-mutate']; self.registry.write_text(json.dumps(policy))
        self.source = self.root / 'source'; self.source.mkdir(); (self.source / '空目录').mkdir()
        nested = self.source / '代码'; nested.mkdir(); self.content = bytes(range(256)) * 257 + b'\x00\xff'
        (nested / 'run.bin').write_bytes(self.content); (nested / 'run.bin').chmod(0o751)
        self.engine = copies.FileTreeCopies(driver, tree_module.FileTreeManifest)

    def tearDown(self): self.fixture.tearDown()

    def command(self, target='target'):
        return {'source': {'rootId': 'test.shared', 'path': 'source', 'expectedVersion': driver.version(self.source.stat())},
                'target': {'rootId': 'test.shared', 'path': target}}

    def request(self, target='target'):
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(self.command(target))}

    def receipt(self, request): return self.fixture.state / 'tree-copies' / (request['operationId'] + '.json')

    def pending(self, request): return self.root / driver.STAGING / (request['operationId'] + '.tree-copy')

    def test_complete_binary_unicode_empty_directories_modes_and_source_preserved(self):
        request = self.request(); before = (self.source / '代码' / 'run.bin').stat()
        result = self.engine.copy(request); target = self.root / 'target'
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['totalBytes'], len(self.content))
        self.assertEqual(result['entryCount'], 4); self.assertFalse(result['partialRetained'])
        self.assertEqual((target / '代码' / 'run.bin').read_bytes(), self.content); self.assertTrue((target / '空目录').is_dir())
        after = (target / '代码' / 'run.bin').stat()
        self.assertEqual(stat.S_IMODE(after.st_mode), 0o751); self.assertEqual(after.st_mtime_ns, before.st_mtime_ns)
        self.assertNotEqual(after.st_ino, before.st_ino); self.assertEqual((self.source / '代码' / 'run.bin').read_bytes(), self.content)
        self.assertEqual(self.engine.lookup({key: request[key] for key in ('operationId', 'owner')}), result)
        self.assertEqual(self.engine.copy(request), result)
        self.assertFalse(self.pending(request).exists())

    def test_existing_file_directory_symlink_are_never_overwritten(self):
        for kind in ('file', 'directory', 'symlink'):
            with self.subTest(kind=kind):
                target = self.root / 'target'
                if kind == 'file': target.write_bytes(b'external')
                elif kind == 'directory': target.mkdir()
                else: target.symlink_to(self.base / 'outside')
                with self.assertRaisesRegex(driver.Rejected, '^FILES_VERSION_CONFLICT$'): self.request()
                if kind == 'directory': target.rmdir()
                else: target.unlink()

    def test_source_changed_after_plan_does_not_create_a_copy(self):
        request = self.request(); (self.source / '代码' / 'run.bin').write_bytes(b'changed')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_VERSION_CONFLICT$'): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists()); self.assertFalse(self.receipt(request).exists())

    def test_policy_change_and_nested_protected_file_reject_before_copy(self):
        request = self.request(); (self.source / '代码' / '.env').write_bytes(b'private')
        with self.assertRaises(driver.Rejected): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists()); self.assertFalse(self.pending(request).exists())
        (self.source / '代码' / '.env').unlink(); request = self.request()
        policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-upload'); self.registry.write_text(json.dumps(policy))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.copy(request)

    def test_copy_into_source_or_ancestor_is_rejected_including_alias_roots(self):
        for target in ('source', 'source/inside'):
            with self.subTest(target=target), self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_OVERLAP$'):
                self.engine.plan(self.command(target))
        policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0], 'id': 'test.alias'}); self.registry.write_text(json.dumps(policy))
        command = self.command('source/inside'); command['target']['rootId'] = 'test.alias'
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_OVERLAP$'): self.engine.plan(command)

    def test_external_target_at_publication_is_preserved_and_not_claimed_success(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Publishing': (self.root / 'target').mkdir(); (self.root / 'target' / 'external').write_bytes(b'external')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(FileExistsError): self.engine.copy(request)
        result = self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
        self.assertEqual(result['state'], 'Unknown'); self.assertTrue(result['partialRetained'])
        self.assertEqual((self.root / 'target' / 'external').read_bytes(), b'external')
        self.assertEqual((self.pending(request) / '代码' / 'run.bin').read_bytes(), self.content)
        self.assertEqual(self.engine.copy(request), result)

    def test_staged_bytes_changed_before_publish_are_not_adopted_as_a_good_copy(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Ready': (self.pending(request) / '代码' / 'run.bin').write_bytes(b'corrupt')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists()); self.assertEqual(json.loads(self.receipt(request).read_bytes())['state'], 'Aborted')
        self.assertEqual((self.pending(request) / '代码' / 'run.bin').read_bytes(), b'corrupt')
        self.assertEqual((self.source / '代码' / 'run.bin').read_bytes(), self.content)

    def test_source_replaced_after_copy_is_not_silently_removed_or_replaced(self):
        request = self.request(); original_populate = self.engine.populate
        def populate(*args):
            original_populate(*args); self.source.rename(self.root / 'externally-retained'); self.source.mkdir(); (self.source / 'external').touch()
        self.engine.populate = populate
        with self.assertRaises(driver.Rejected): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists())
        self.assertEqual((self.root / 'externally-retained' / '代码' / 'run.bin').read_bytes(), self.content)
        self.assertTrue((self.source / 'external').exists())

    def test_cancellation_preserves_private_partial_tree_without_publication(self):
        request = self.request(); canceled = False
        def checkpoint(phase):
            nonlocal canceled
            if phase == 'Ready': canceled = True
        self.engine.checkpoint = checkpoint; self.engine.cancelled = lambda: canceled
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists()); self.assertTrue(self.pending(request).is_dir())
        self.assertEqual(json.loads(self.receipt(request).read_bytes())['state'], 'Aborted')

    def test_real_sigkill_at_staging_ready_publishing_and_published_never_replays(self):
        for phase in ('Staging', 'Ready', 'Publishing', 'Published'):
            with self.subTest(phase=phase):
                request = self.request('target-' + phase.lower()); pid = os.fork()
                if pid == 0:
                    def crash(actual):
                        if actual == phase: os.kill(os.getpid(), signal.SIGKILL)
                    self.engine.checkpoint = crash
                    try: self.engine.copy(request)
                    finally: os._exit(7)
                _, status = os.waitpid(pid, 0); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                result = self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
                self.assertEqual(result['state'], 'Completed' if phase == 'Published' else 'Aborted')
                self.assertEqual(self.engine.copy(request), result)
                destination = self.root / request['plan']['command']['target']['path']
                self.assertEqual(destination.exists(), phase == 'Published')
                if phase == 'Published': self.assertEqual((destination / '代码' / 'run.bin').read_bytes(), self.content)
                else: self.assertTrue(self.pending(request).is_dir())
                self.assertEqual((self.source / '代码' / 'run.bin').read_bytes(), self.content)

    def test_missing_published_destination_after_sigkill_is_unknown_not_recreated(self):
        request = self.request(); pid = os.fork()
        if pid == 0:
            self.engine.checkpoint = lambda phase: os.kill(os.getpid(), signal.SIGKILL) if phase == 'Published' else None
            try: self.engine.copy(request)
            finally: os._exit(7)
        _, status = os.waitpid(pid, 0); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
        (self.root / 'target').rename(self.root / 'external-retained')
        result = self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
        self.assertEqual(result['state'], 'Unknown'); self.assertFalse((self.root / 'target').exists())
        self.assertEqual(self.engine.copy(request), result); self.assertEqual((self.root / 'external-retained' / '代码' / 'run.bin').read_bytes(), self.content)

    def test_other_owner_and_rebound_plan_cannot_adopt_an_existing_receipt(self):
        request = self.request(); self.engine.copy(request)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
            self.engine.lookup({'operationId': request['operationId'], 'owner': 'B' * 64})
        with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'): self.engine.copy({**request, 'owner': 'B' * 64})
        rebound = self.request('other')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_IDEMPOTENCY_CONFLICT$'):
            self.engine.copy({**rebound, 'operationId': request['operationId']})
        self.assertFalse((self.root / 'other').exists())

    def test_nested_symlink_and_hardlink_are_rejected_without_private_copy(self):
        outside = self.base / 'outside'; outside.write_bytes(b'private'); nested = self.source / '代码' / 'linked'
        for link in ('symbolic', 'hard'):
            if link == 'symbolic': nested.symlink_to(outside)
            else: os.link(outside, nested)
            with self.assertRaises(driver.Rejected): self.request()
            nested.unlink()
        self.assertEqual(outside.read_bytes(), b'private')

    def test_changed_published_bytes_do_not_retroactively_change_completed_history(self):
        request = self.request(); result = self.engine.copy(request)
        (self.root / 'target' / '代码' / 'run.bin').write_bytes(b'external-later')
        self.assertEqual(self.engine.copy(request), result)
        self.assertEqual((self.root / 'target' / '代码' / 'run.bin').read_bytes(), b'external-later')

    def test_cancel_tombstone_blocks_delayed_copy_even_after_source_changes(self):
        request = self.request(); (self.source / '代码' / 'run.bin').write_bytes(b'external')
        result = self.engine.discard(request)
        self.assertEqual(result['state'], 'Aborted'); self.assertFalse(result['partialRetained'])
        self.assertEqual(self.engine.copy(request), result); self.assertFalse((self.root / 'target').exists())
        self.assertFalse(self.pending(request).exists())

    def test_policy_revoked_at_ready_does_not_publish_or_remove_source(self):
        request = self.request()
        def revoke(phase):
            if phase == 'Ready':
                policy = json.loads(self.registry.read_bytes()); policy[0]['allowedActions'].remove('files-upload'); self.registry.write_text(json.dumps(policy))
        self.engine.checkpoint = revoke
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.copy(request)
        self.assertFalse((self.root / 'target').exists()); self.assertTrue(self.pending(request).exists())
        self.assertEqual((self.source / '代码' / 'run.bin').read_bytes(), self.content)
        self.assertEqual(json.loads(self.receipt(request).read_bytes())['state'], 'Unknown')

    def test_receipt_permissions_hardlink_or_invalid_plan_never_become_success(self):
        request = self.request(); self.engine.copy(request); path = self.receipt(request)
        path.chmod(0o644)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'):
            self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
        path.chmod(0o600); alias = self.base / 'receipt-link'; os.link(path, alias)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'):
            self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
        alias.unlink(); value = json.loads(path.read_bytes()); value['plan']['totalBytes'] = 0; path.write_text(json.dumps(value))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'):
            self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def test_invalid_plan_types_and_non_ascii_fingerprints_are_bounded_rejections(self):
        request = self.request()
        for changes in ({'fingerprint': '字' * 64}, {'entryCount': True}, {'totalBytes': 16 * 1024**3 + 1}, {'targetParent': [0, False]}, {'extra': 'x'}):
            with self.subTest(changes=changes), self.assertRaisesRegex(driver.Rejected, '^FILES_PLAN_INVALID$'):
                self.engine.copy({**request, 'plan': {**request['plan'], **changes}})
        self.assertFalse((self.root / 'target').exists()); self.assertFalse(self.receipt(request).exists())

    def test_transfer_to_another_root_uses_registered_owner_and_drops_special_bits(self):
        target = self.base / 'other'; target.mkdir(); policy = json.loads(self.registry.read_bytes())
        policy.append({**policy[0], 'id': 'test.other', 'path': str(target), 'ownerUid': 12345, 'ownerGid': 12346}); self.registry.write_text(json.dumps(policy))
        (self.source / '代码' / 'run.bin').chmod(0o6751)
        command = self.command(); command['target']['rootId'] = 'test.other'
        request = {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(command)}
        result = self.engine.copy(request); self.assertEqual(result['state'], 'Completed')
        info = (target / 'target' / '代码' / 'run.bin').stat()
        self.assertEqual((info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)), (12345, 12346, 0o751))
        self.assertEqual((target / 'target' / '代码' / 'run.bin').read_bytes(), self.content)

    def test_registered_bind_alias_cannot_hide_copying_into_the_source(self):
        alias = self.base / 'alias'; alias.mkdir()
        subprocess.run(['mount', '--bind', str(self.source / '代码'), str(alias)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0], 'id': 'test.alias', 'path': str(alias)}); self.registry.write_text(json.dumps(policy))
            command = self.command(); command['target']['rootId'] = 'test.alias'
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_OVERLAP$'): self.engine.plan(command)
            self.assertFalse((alias / driver.STAGING).exists()); self.assertFalse((alias / 'target').exists())
        finally: subprocess.run(['umount', str(alias)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def test_copy_between_actual_devices_preserves_bytes_without_moving_source(self):
        target = self.base / 'other-device'; target.mkdir()
        subprocess.run(['mount', '-t', 'tmpfs', '-o', 'size=1G', 'tmpfs', str(target)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            self.assertNotEqual(target.stat().st_dev, self.source.stat().st_dev)
            policy = json.loads(self.registry.read_bytes()); policy.append({**policy[0], 'id': 'test.other', 'path': str(target)}); self.registry.write_text(json.dumps(policy))
            command = self.command(); command['target']['rootId'] = 'test.other'
            request = {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(command)}
            self.assertEqual(self.engine.copy(request)['state'], 'Completed')
            self.assertEqual((target / 'target' / '代码' / 'run.bin').read_bytes(), self.content)
            self.assertEqual((self.source / '代码' / 'run.bin').read_bytes(), self.content)
        finally: subprocess.run(['umount', str(target)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def test_large_manifest_is_not_embedded_in_plan_or_receipt(self):
        for index in range(250): (self.source / ('manifest-not-a-rpc-frame-' + str(index))).touch()
        manifest = tree_module.FileTreeManifest(driver).scan(self.command()['source'])
        self.assertGreater(len(self.engine.encode(manifest)), 65536)
        request = self.request(); self.assertLess(len(self.engine.encode(request['plan'])), 2048)
        result = self.engine.copy(request); self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['entryCount'], 254)
        self.assertLess(len(self.receipt(request).read_bytes()), 4096)

    def test_root_namespace_lock_serializes_with_other_writes_and_same_operation(self):
        request = self.request(); ready_read, ready_write = os.pipe(); release_read, release_write = os.pipe(); pid = os.fork()
        if pid == 0:
            os.close(ready_read); os.close(release_write)
            def pause(phase):
                if phase == 'Ready': os.write(ready_write, b'R'); os.read(release_read, 1)
            self.engine.checkpoint = pause
            try:
                result = self.engine.copy(request); os._exit(0 if result['state'] == 'Completed' else 8)
            except BaseException: os._exit(9)
        os.close(ready_write); os.close(release_read)
        try:
            import select
            self.assertTrue(select.select([ready_read], [], [], 10)[0]); self.assertEqual(os.read(ready_read, 1), b'R')
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.copy(request)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'):
                with driver.namespace_lock(): self.fail('namespace lock incorrectly acquired')
        finally:
            os.write(release_write, b'R'); os.close(release_write); os.close(ready_read); _, status = os.waitpid(pid, 0)
        self.assertTrue(os.WIFEXITED(status)); self.assertEqual(os.WEXITSTATUS(status), 0)
        self.assertEqual(self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})['state'], 'Completed')


if __name__ == '__main__': unittest.main()
