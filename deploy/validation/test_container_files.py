"""Pure policy + isolated Linux-root file read tests, not installed RPC/UI proof."""
import base64
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import select
import stat
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

HOST = Path(__file__).resolve().parents[1] / 'host'


def load(filename, name):
    specification = importlib.util.spec_from_file_location(name, HOST / filename)
    module = importlib.util.module_from_spec(specification); specification.loader.exec_module(module)
    return module


engine = load('whaledeck-container-files.py', 'container_file_test_engine')
ID = 'a' * 64
INSTANCE = '777:2026-10-21T01:02:03.123456789Z'


def request(path='', **extra):
    return {'containerId': ID, 'instanceId': INSTANCE, 'path': path, **extra}


class PolicyTests(unittest.TestCase):
    def test_immutable_exact_target_and_valid_startup(self):
        self.assertEqual(777, engine.target_binding(ID, INSTANCE))
        for identifier in ('a' * 12, 'A' * 64, '../ordinary', 'ordinary', '', None, True):
            with self.subTest(identifier=identifier), self.assertRaises(engine.Rejected): engine.target_binding(identifier, INSTANCE)
        for value in ('1:2026-10-21T01:02:03Z', '0777:2026-10-21T01:02:03Z', '777:invalid',
                '777:2026-02-30T01:02:03Z', '777:2026-10-21T01:02:03Z\n', '2147483648:2026-10-21T01:02:03Z', None):
            with self.subTest(instance=value), self.assertRaises(engine.Rejected): engine.target_binding(ID, value)

    def test_canonical_relative_paths_do_not_normalize_dangerous_input(self):
        for path in ('/etc/passwd', 'a/../b', 'a/./b', 'a//b', 'a/', '.', '..', 'a\\b', 'a\0b',
                '\x1bname', '\x7f', '\x85', '\ud800', '中' * 86, 'a/' * 2049, None, True):
            with self.subTest(path=repr(path)), self.assertRaises(engine.Rejected): engine.canonical_path(path)
        self.assertEqual('目录/中文.txt', engine.canonical_path('目录/中文.txt'))
        self.assertEqual('', engine.canonical_path(''))
        with self.assertRaises(engine.Rejected): engine.canonical_path('', empty=False)

    def test_exact_query_shapes_no_host_path_command_or_unselected_target(self):
        valid = request('', after='', take=25); engine.request_fields(valid, 'list')
        for key, value in (('hostPath', '/'), ('command', 'cat'), ('user', 'root'), ('rootId', 'host'), ('owner', 'other')):
            with self.subTest(key=key), self.assertRaises(engine.Rejected): engine.request_fields({**valid, key: value}, 'list')
        for take in (True, 0, 101, '25'):
            with self.subTest(take=take), self.assertRaises(engine.Rejected): engine.request_fields({**valid, 'take': take}, 'list')
        with self.assertRaises(engine.Rejected): engine.request_fields({**valid, 'after': 'a/b'}, 'list')
        with self.assertRaises(engine.Rejected): engine.request_fields(request(), 'upload')
        with self.assertRaises(engine.Rejected): engine.request_fields(request('file', expectedVersion='absent'), 'download')

    def test_target_fingerprint_does_not_include_environment_or_command(self):
        value = {'Id': ID, 'HostConfig': {}, 'Config': {'User': '1000', 'Labels': {}, 'Env': ['secret-a'], 'Cmd': ['private']}}
        first = engine.mount_fingerprint(value)
        value['Config']['Env'] = ['secret-b']; value['Config']['Cmd'] = ['changed']
        self.assertEqual(first, engine.mount_fingerprint(value))
        value['Mounts'] = [{'Type': 'tmpfs', 'Destination': '/data'}]
        self.assertNotEqual(first, engine.mount_fingerprint(value))

    def test_core_registry_and_label_are_independent(self):
        resources = [{'type': 'Container', 'externalId': 'whaledeck-api'}]
        self.assertTrue(engine.is_core({'Id': ID, 'Names': ['/whaledeck-api'], 'Labels': {}}, resources))
        self.assertTrue(engine.is_core({'Id': ID, 'Names': ['/ordinary'], 'Labels': {'io.whaledeck.protected': 'TRUE'}}, []))
        self.assertFalse(engine.is_core({'Id': ID, 'Names': ['/ordinary'], 'Labels': {}}, resources))

    def test_version_domain_binds_container_startup_and_relative_path(self):
        info = SimpleNamespace(st_dev=1, st_ino=2, st_mode=stat.S_IFREG | 0o640, st_uid=0, st_gid=0,
            st_nlink=1, st_size=8, st_mtime_ns=10, st_ctime_ns=11)
        first = engine.version(request('a'), 'a', info)
        self.assertRegex(first, '^[A-F0-9]{64}$')
        for target, path in (({**request('a'), 'containerId': 'b' * 64}, 'a'),
                ({**request('a'), 'instanceId': INSTANCE.replace('777:', '778:')}, 'a'), (request('b'), 'b')):
            self.assertNotEqual(first, engine.version(target, path, info))

    def test_core_mount_query_is_bounded_and_failure_closed(self):
        for value in ({}, [None], [{}] * 1001):
            with self.subTest(valueType=type(value).__name__), self.assertRaises(engine.Rejected):
                engine.core_sources(SimpleNamespace(api=Mock(return_value=value)), [])

    def test_namespace_and_cgroup_identity_are_required(self):
        def content(path):
            if path.name == 'stat': return '777 (space ) name) ' + 'S ' + '0 ' * 18 + '1234 '
            if path.name == 'cgroup': return '0::/system.slice/docker-' + ID + '.scope\n'
            if path.name == 'status': return 'NSpid:\t777\t1\n'
            raise AssertionError('unexpected read')
        def value(path): return SimpleNamespace(st_ino=1 if 'self' in path.parts else 2)
        with patch.object(engine.Path, 'read_text', autospec=True, side_effect=content), patch.object(engine.Path, 'stat', autospec=True, side_effect=value):
            self.assertEqual(('1234', 2, 2), engine.process_identity(777, ID))
        def wrong_cgroup(path): return '0::/system.slice/docker-' + 'b' * 64 + '.scope\n' if path.name == 'cgroup' else content(path)
        with patch.object(engine.Path, 'read_text', autospec=True, side_effect=wrong_cgroup), self.assertRaisesRegex(engine.Rejected, 'PROCESS_TARGET_CHANGED'):
            engine.process_identity(777, ID)


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxReadTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-container-read-'); self.addCleanup(self.scratch.cleanup)
        self.base = Path(self.scratch.name).resolve(); self.root = self.base / 'root'; self.root.mkdir()
        self.spool = self.base / 'spool'; self.spool.mkdir(mode=0o700)
        self.files = load('whaledeck-files.py', 'container_read_test_primitives')
        self.folder = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY); self.addCleanup(os.close, self.folder)
        self.session = SimpleNamespace(root=self.folder, current=Mock(), revalidate=Mock())
        state = patch.object(engine, 'STATE', self.spool); state.start(); self.addCleanup(state.stop)
        self.content = hashlib.shake_256(b'isolated-container-files-full-content-v1').digest(2 * 1024**2 + 7)
        self.leaf = self.root / '中文.bin'; self.leaf.write_bytes(self.content); self.leaf.chmod(0o640)
        self.location = request(self.leaf.name)

    def download_request(self):
        return {**self.location, 'expectedVersion': engine.version(self.location, self.leaf.name, self.leaf.stat())}

    def test_real_complete_snapshot_download_and_no_content_files_remain(self):
        output = io.BytesIO(); before = engine.fingerprint(self.leaf.stat())
        engine.download(self.download_request(), self.session, self.files, output)
        packets = [json.loads(row) for row in output.getvalue().splitlines()]
        self.assertEqual('metadata', packets[0]['type']); self.assertEqual('end', packets[-1]['type'])
        content = bytearray()
        for row in packets[1:-1]:
            self.assertEqual('chunk', row['type']); self.assertEqual(len(content), row['offset'])
            content.extend(base64.b64decode(row['data'], validate=True))
        self.assertEqual(self.content, content); self.assertEqual(len(content), packets[-1]['sizeBytes'])
        self.assertEqual(hashlib.sha256(self.content).hexdigest().upper(), packets[-1]['sha256'])
        self.assertEqual(before, engine.fingerprint(self.leaf.stat()))
        self.assertEqual([], list(self.spool.iterdir())); self.assertEqual(2, self.session.revalidate.call_count)

    def test_stale_version_emits_no_metadata_or_content(self):
        output = io.BytesIO(); old = self.download_request(); self.leaf.write_bytes(b'changed')
        with self.assertRaisesRegex(engine.Rejected, 'VERSION_CONFLICT'): engine.download(old, self.session, self.files, output)
        self.assertEqual(b'', output.getvalue()); self.assertEqual([], list(self.spool.iterdir()))

    def test_change_during_copy_emits_nothing_and_removes_anonymous_capture(self):
        output = io.BytesIO(); before = self.download_request()
        self.session.current.side_effect = lambda: self.leaf.write_bytes(b'changed')
        with self.assertRaisesRegex(engine.Rejected, 'READ_CHANGED'): engine.download(before, self.session, self.files, output)
        self.assertEqual(b'', output.getvalue()); self.assertEqual([], list(self.spool.iterdir()))

    def test_target_change_before_first_byte_emits_nothing(self):
        output = io.BytesIO(); self.session.revalidate.side_effect = engine.Rejected('CONTAINER_FILES_TARGET_CHANGED')
        with self.assertRaisesRegex(engine.Rejected, 'TARGET_CHANGED'): engine.download(self.download_request(), self.session, self.files, output)
        self.assertEqual(b'', output.getvalue()); self.assertEqual([], list(self.spool.iterdir()))

    def test_target_loss_during_delivery_never_emits_a_success_end(self):
        output = io.BytesIO(); count = 0
        def current():
            nonlocal count
            count += 1
            if count == 67: raise engine.Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
        self.session.current.side_effect = current
        with self.assertRaisesRegex(engine.Rejected, 'INSTANCE_CHANGED'): engine.download(self.download_request(), self.session, self.files, output)
        packets = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertTrue(packets); self.assertFalse(any(row['type'] == 'end' for row in packets))
        self.assertEqual([], list(self.spool.iterdir()))

    def test_hardlinks_and_fifo_are_never_content(self):
        output = io.BytesIO(); os.link(self.leaf, self.root / 'second')
        with self.assertRaisesRegex(engine.Rejected, 'SINGLE_LINK'): engine.download(self.download_request(), self.session, self.files, output)
        os.mkfifo(self.root / 'pipe')
        with self.assertRaisesRegex(engine.Rejected, 'SINGLE_LINK'): engine.inspect(request('pipe'), self.session, self.files)
        self.assertEqual(b'', output.getvalue())

    def test_symlink_parent_and_final_cannot_escape_or_leak_contents(self):
        (self.root / 'linked').symlink_to(self.base, target_is_directory=True)
        (self.root / 'leaf-link').symlink_to(self.leaf)
        (self.base / 'outside').write_text('private outside')
        for path in ('linked/outside', 'leaf-link'):
            with self.subTest(path=path), self.assertRaises(OSError): engine.inspect(request(path), self.session, self.files)

    def test_virtual_and_secret_paths_are_refused_before_open(self):
        for path in ('proc/self/status', 'sys/kernel', 'dev/null', 'run/secret', 'var/run/socket', '.env', 'config/key.pem', 'etc/shadow', 'data/.ssh/key'):
            with self.subTest(path=path), patch.object(self.files, 'open_at') as opening, self.assertRaisesRegex(engine.Rejected, 'PROTECTED'):
                engine.inspect(request(path), self.session, self.files)
            opening.assert_not_called()

    def test_paginated_listing_versions_and_protected_metadata(self):
        for name in ('a', 'b', 'c', '.env'): (self.root / name).write_text('x')
        (self.root / 'link').symlink_to(self.base)
        first = engine.listing(request('', after='', take=2), self.session, self.files)
        self.assertEqual(['.env', 'a'], [row['name'] for row in first['entries']])
        self.assertTrue(first['entries'][0]['isProtected']); self.assertEqual('a', first['nextCursor'])
        second = engine.listing(request('', after=first['nextCursor'], take=100), self.session, self.files)
        self.assertEqual(['b', 'c', 'link', '中文.bin'], [row['name'] for row in second['entries']])
        self.assertEqual('', second['nextCursor']); self.assertEqual(first['version'], second['version'])
        self.assertTrue(next(row for row in second['entries'] if row['name'] == 'link')['isProtected'])

    def test_escaped_long_names_respect_utf8_page_budget(self):
        for number in range(100): (self.root / (str(number).zfill(3) + '"' * 245)).write_text('x')
        page = engine.listing(request('', after='', take=100), self.session, self.files)
        self.assertTrue(page['nextCursor']); self.assertLess(len(page['entries']), 100)
        self.assertLessEqual(len(json.dumps(page, ensure_ascii=False, separators=(',', ':')).encode()), 65536)

    def test_directory_scan_budget_fails_instead_of_partial_success(self):
        with patch.object(engine, 'SCAN_LIMIT', 0), self.assertRaisesRegex(engine.Rejected, 'SCAN_LIMIT'):
            engine.listing(request('', after='', take=25), self.session, self.files)

    def test_unsafe_spool_permissions_and_capacity_fail_before_output(self):
        self.spool.chmod(0o755); output = io.BytesIO()
        with self.assertRaisesRegex(engine.Rejected, 'STATE_UNSAFE'): engine.download(self.download_request(), self.session, self.files, output)
        self.spool.chmod(0o700)
        with patch.object(engine.os, 'statvfs', return_value=SimpleNamespace(f_bavail=0, f_frsize=4096, f_blocks=1024)), self.assertRaisesRegex(engine.Rejected, 'CAPACITY_LOW'):
            engine.download(self.download_request(), self.session, self.files, output)
        self.assertEqual(b'', output.getvalue())

    def test_capacity_loss_during_capture_does_not_emit_or_leave_files(self):
        high = SimpleNamespace(f_bavail=10**9, f_frsize=4096, f_blocks=10**9)
        low = SimpleNamespace(f_bavail=0, f_frsize=4096, f_blocks=10**9)
        output = io.BytesIO()
        with patch.object(engine.os, 'statvfs', side_effect=[high, low]), self.assertRaisesRegex(engine.Rejected, 'CAPACITY_LOW'):
            engine.download(self.download_request(), self.session, self.files, output)
        self.assertEqual(b'', output.getvalue()); self.assertEqual([], list(self.spool.iterdir()))

    def test_native_probe_accepts_only_fixed_root_or_trusted_agent_owner(self):
        path = Path(__file__).with_name('validate_container_file_reads.py')
        specification = importlib.util.spec_from_file_location('container_native_probe_unit', path)
        probe = importlib.util.module_from_spec(specification); specification.loader.exec_module(probe)
        approved = Mock(spec=Path); approved.resolve.return_value = approved; approved.is_dir.return_value = True
        with patch.object(probe, 'APPS', approved):
            for owner in (0, 997):
                approved.lstat.return_value = SimpleNamespace(st_uid=owner, st_mode=stat.S_IFDIR | 0o750)
                probe.verify_apps_directory(approved, 997)
            for owner, mode in ((1000, 0o750), (0, 0o775), (997, 0o777)):
                approved.lstat.return_value = SimpleNamespace(st_uid=owner, st_mode=stat.S_IFDIR | mode)
                with self.subTest(owner=owner, mode=mode), self.assertRaises(AssertionError): probe.verify_apps_directory(approved, 997)
            approved.resolve.return_value = Path('/other')
            with self.assertRaises(AssertionError): probe.verify_apps_directory(approved, 997)

    def test_two_kernel_leases_bound_concurrent_reads_and_release_on_close(self):
        first, second = engine.ReadLease(), engine.ReadLease()
        try:
            first.__enter__(); second.__enter__()
            with self.assertRaisesRegex(engine.Rejected, 'BUSY'): engine.ReadLease().__enter__()
            first.__exit__()
            with engine.ReadLease(): pass
        finally:
            first.__exit__(); second.__exit__()
        self.assertTrue({path.name for path in self.spool.iterdir()} <= {'read-slot-0', 'read-slot-1'})
        self.assertTrue(all(path.stat().st_size == 0 for path in self.spool.iterdir()))

    def test_lease_rejects_symlink_hardlink_nonempty_and_unsafe_permissions(self):
        slot = self.spool / 'read-slot-0'
        slot.symlink_to(self.leaf)
        with self.assertRaises(OSError): engine.ReadLease().__enter__()
        slot.unlink(); os.link(self.leaf, slot)
        with self.assertRaisesRegex(engine.Rejected, 'STATE_UNSAFE'): engine.ReadLease().__enter__()
        slot.unlink(); slot.write_bytes(b'not-a-lease')
        with self.assertRaisesRegex(engine.Rejected, 'STATE_UNSAFE'): engine.ReadLease().__enter__()
        slot.write_bytes(b''); slot.chmod(0o666)
        with self.assertRaisesRegex(engine.Rejected, 'STATE_UNSAFE'): engine.ReadLease().__enter__()

    def test_sigkill_releases_both_kernel_read_slots_without_recovery_mutation(self):
        code = """import importlib.util,pathlib,signal,sys
spec=importlib.util.spec_from_file_location('owned_read_slot_child',sys.argv[1])
engine=importlib.util.module_from_spec(spec);spec.loader.exec_module(engine)
engine.STATE=pathlib.Path(sys.argv[2])
first=engine.ReadLease();second=engine.ReadLease()
first.__enter__();second.__enter__()
print('ready',flush=True)
signal.pause()
"""
        child = subprocess.Popen([sys.executable, '-c', code, str(HOST / 'whaledeck-container-files.py'), str(self.spool)],
            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            self.assertTrue(select.select([child.stdout], [], [], 5)[0])
            self.assertEqual('ready', child.stdout.readline().strip())
            with self.assertRaisesRegex(engine.Rejected, 'BUSY'): engine.ReadLease().__enter__()
            child.kill(); child.wait(timeout=5); self.assertEqual(-9, child.returncode)
            with engine.ReadLease(), engine.ReadLease(): pass
        finally:
            if child.poll() is None: child.kill()
            child.communicate(timeout=5)

    def pinned_root(self):
        """Real temporary descriptors; fake proc/Docker identity, never a live PID or container."""
        original_open, original_stat = os.open, os.stat
        descriptors = []
        def pidfd(_pid):
            descriptor = original_open('/dev/null', os.O_RDONLY); descriptors.append(descriptor); return descriptor
        def opening(path, flags, *args, **kwargs):
            if path == '/proc/777/root':
                descriptor = os.dup(self.folder); descriptors.append(descriptor); return descriptor
            return original_open(path, flags, *args, **kwargs)
        def status(path, *args, **kwargs):
            if path == '/proc/777/root': return os.fstat(self.folder)
            return original_stat(path, *args, **kwargs)
        docker = SimpleNamespace(registry=Mock(return_value=[]), api=Mock(return_value={'Id': ID}),
            target=Mock(return_value={'containerId': ID, 'instanceId': INSTANCE}))
        patches = [patch.object(engine.os, 'pidfd_open', side_effect=pidfd), patch.object(engine.os, 'open', side_effect=opening),
            patch.object(engine.os, 'stat', side_effect=status), patch.object(engine, 'process_identity', return_value=('1234', 2, 3)),
            patch.object(engine.select, 'select', return_value=([], [], [])), patch.object(engine, 'check_mounts')]
        for item in patches: item.start(); self.addCleanup(item.stop)
        return engine.ContainerRoot(request(), self.files, docker), docker, descriptors

    def test_partial_root_setup_closes_all_real_descriptors_on_revalidation_failure(self):
        session, docker, descriptors = self.pinned_root()
        docker.registry.side_effect = [[], [{'changed': True}]]
        with self.assertRaisesRegex(engine.Rejected, 'TARGET_CHANGED'): session.__enter__()
        self.assertIsNone(session.root); self.assertIsNone(session.pidfd)
        self.assertEqual(2, len(descriptors))
        for descriptor in descriptors:
            with self.assertRaises(OSError): os.fstat(descriptor)

    def test_pinned_session_rechecks_registry_target_and_mount_configuration(self):
        session, docker, _ = self.pinned_root()
        with session:
            docker.target.return_value = {'containerId': ID, 'instanceId': INSTANCE.replace('777:', '778:')}
            with self.assertRaisesRegex(engine.Rejected, 'TARGET_CHANGED'): session.revalidate()
            docker.target.return_value = session.target
            docker.api.return_value = {'Id': ID, 'Mounts': [{'Destination': '/changed'}]}
            with self.assertRaisesRegex(engine.Rejected, 'TARGET_CHANGED'): session.revalidate()

    def test_pinned_session_detects_exit_missing_proc_and_root_change(self):
        session, _, _ = self.pinned_root()
        with session:
            with patch.object(engine.select, 'select', return_value=([session.pidfd], [], [])), self.assertRaisesRegex(engine.Rejected, 'INSTANCE_CHANGED'):
                session.current()
            for error in (FileNotFoundError(), ProcessLookupError()):
                with patch.object(engine, 'process_identity', side_effect=error), self.assertRaisesRegex(engine.Rejected, 'INSTANCE_CHANGED'):
                    session.current()
            actual = os.fstat(self.folder)
            with patch.object(engine.os, 'stat', return_value=SimpleNamespace(st_dev=actual.st_dev, st_ino=actual.st_ino+1)), self.assertRaisesRegex(engine.Rejected, 'ROOT_CHANGED'):
                session.current()

    def test_core_shared_bind_is_rejected_even_when_ordinary_name_is_safe(self):
        source = self.root / 'data'; source.mkdir()
        container = {'Mounts': [{'Type': 'bind', 'Source': str(source), 'Destination': '/data'}]}
        with patch.object(engine, 'core_sources', return_value=[source]), self.assertRaisesRegex(engine.Rejected, 'SHARED_CORE_MOUNT'):
            engine.check_mounts(container, self.folder, self.files, SimpleNamespace(), [])

    def test_named_volume_driver_bind_options_and_wrong_mountpoint_are_rejected(self):
        source = self.root / 'data'; source.mkdir(); name = 'whaledeck-app-owned-data'
        container = {'Mounts': [{'Type': 'volume', 'Name': name, 'Source': str(source), 'Destination': '/data'}]}
        valid = {'Name': name, 'Driver': 'local', 'Options': None, 'Mountpoint': str(source)}
        with patch.object(engine, 'core_sources', return_value=[]):
            engine.check_mounts(container, self.folder, self.files, SimpleNamespace(api=Mock(return_value=valid)), [])
            for change in ({'Driver': 'custom'}, {'Options': {'type': 'none', 'device': '/etc'}}, {'Mountpoint': '/etc'}, {'Name': 'other'}):
                with self.subTest(change=change), self.assertRaisesRegex(engine.Rejected, 'VOLUME_INVALID'):
                    engine.check_mounts(container, self.folder, self.files, SimpleNamespace(api=Mock(return_value={**valid, **change})), [])

    def test_actual_mount_identity_must_match_not_just_allowed_source_path(self):
        source = self.base / 'data'; source.mkdir(); (self.root / 'data').mkdir()
        container = {'Mounts': [{'Type': 'bind', 'Source': str(source), 'Destination': '/data'}]}
        with patch.object(engine, 'core_sources', return_value=[]), self.assertRaisesRegex(engine.Rejected, 'MOUNT_IDENTITY_CHANGED'):
            engine.check_mounts(container, self.folder, self.files, SimpleNamespace(), [])


if __name__ == '__main__': unittest.main()
