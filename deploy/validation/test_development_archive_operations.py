"""Real private-FD/journal/crash tests; execution/HDD/quota doubles named.

No service or installed configuration is used by this suite. The native gate
must independently exercise actual systemd cgroups and the real storage chain.
"""
import contextlib
import errno
import hashlib
import json
import os
from pathlib import Path
import signal
import shutil
import stat
import tempfile
import threading
import time
import unittest
import uuid
from unittest.mock import patch

import test_development_archives as captures

fixture = captures.fixture
operations = fixture.load('archive_operations_test', 'whaledeck-development-archive-operations.py')
control = fixture.load('archive_control_test', 'whaledeck-development-archive-control.py')


class ExecutionDouble:
    def __init__(self, driver):
        self.validator = control.Execution(driver)
        self.alive = True
        self.stops = 0
        self.identity_calls = 0
        self.proof = dict(bootId=str(uuid.uuid4()), path='/system.slice/whaledeck-development-archive-run-' + uuid.uuid4().hex + '.service', device=1, inode=2)

    def validate(self, proof):
        self.validator.validate(proof)

    def identity(self):
        self.identity_calls += 1
        return dict(self.proof)

    def settled(self, proof, *, stop=False):
        if proof != self.proof:
            raise fixture.files.Rejected('WORKSPACE_STORAGE_ARCHIVE_TASK_EXECUTION_CHANGED')
        if stop:
            self.stops += 1
            self.alive = False
        return not self.alive


@unittest.skipUnless(os.geteuid() == 0, 'Requires disposable root-owned private journals')
class OperationTests(unittest.TestCase):
    write_config = captures.ArchiveTests.write_config
    write_mountinfo = captures.ArchiveTests.write_mountinfo
    enable_quota = captures.ArchiveTests.enable_quota
    stop = captures.ArchiveTests.stop
    snapshot = captures.ArchiveTests.snapshot

    def setUp(self):
        captures.ArchiveTests.setUp(self)
        home = tempfile.TemporaryDirectory(); self.addCleanup(home.cleanup)
        self.journal = Path(home.name)
        p = patch.object(operations, 'BASE', self.journal); p.start(); self.addCleanup(p.stop)
        self.execution = ExecutionDouble(self.engine)
        self.tasks = operations.ArchiveOperations(self.engine, self.capture, self.execution)
        self.base = dict(schemaVersion=1, taskId=str(uuid.uuid4()), archiveId=str(uuid.uuid4()),
            phase='plan', storage=self.request, expectedEpoch=2, planSha256='', receiptSha256='')

    def directory(self, value=None):
        return self.journal / (value or self.base)['taskId']

    def finish(self, value):
        result = self.tasks.run(value)
        self.assertEqual('Finishing', result['status'])
        self.execution.alive = False
        result = self.tasks.observe(value)
        self.assertEqual('Succeeded', result['status'])
        self.execution.alive = True
        return result

    def test_plan_capture_and_independent_verify_are_separate_bound_long_tasks(self):
        before = self.snapshot(); quota = dict(self.quota.projects)
        plan = self.finish(self.base)
        capture = dict(self.base, taskId=str(uuid.uuid4()), phase='capture', planSha256=plan['result']['planSha256'])
        copied = self.finish(capture)
        verify = dict(capture, taskId=str(uuid.uuid4()), phase='verify', receiptSha256=copied['result']['receiptSha256'])
        checked = self.finish(verify)
        self.assertEqual(copied['result'], checked['result'])
        self.assertEqual(before, self.snapshot()); self.assertEqual(quota, self.quota.projects)
        for command in (self.base, capture, verify):
            for p in self.directory(command).iterdir():
                self.assertEqual((0, 0o600, 1), (p.stat().st_uid, stat.S_IMODE(p.stat().st_mode), p.stat().st_nlink))
            self.assertEqual(hashlib.sha256(self.engine.canonical(command)).hexdigest(), self.tasks.observe(command)['requestSha256'])

    def test_prestart_cancel_is_durable_and_delayed_start_never_enters_engine(self):
        result = self.tasks.cancel(self.base)
        self.assertEqual('Stopped', result['status'])
        with patch.object(self.capture, 'plan', side_effect=AssertionError('late execution')):
            self.assertEqual(result, self.tasks.run(self.base))
            self.assertEqual(result, self.tasks.cancel(self.base))
        self.assertEqual(0, self.execution.identity_calls)
        self.assertEqual([], list(self.hdd.iterdir()))

    def test_immutable_task_rejects_every_different_payload_before_observe_or_stop(self):
        self.tasks.cancel(self.base)
        for change in ({'expectedEpoch': 4}, {'archiveId': str(uuid.uuid4())}, {'storage': dict(self.request, diskMiB=512)},
                       {'phase': 'capture', 'planSha256': 'a' * 64}):
            wrong = dict(self.base, **change)
            for action in (self.tasks.observe, self.tasks.cancel, self.tasks.run):
                with self.subTest(change=change, action=action.__name__), self.assertRaises(fixture.files.Rejected):
                    action(wrong)
        self.assertEqual(0, self.execution.stops)

    def test_unknown_task_observation_has_no_side_effect(self):
        with self.assertRaises(FileNotFoundError): self.tasks.observe(self.base)
        self.assertEqual([], list(self.journal.iterdir()))

    def test_actual_sigkill_during_first_cancel_initialization_is_recoverable(self):
        ready_r, ready_w = os.pipe(); original_write = os.write
        child = os.fork()
        if child == 0:
            os.close(ready_r)
            def interrupted(fd, data):
                if data == b'0':
                    original_write(ready_w, b'1'); signal.pause()
                return original_write(fd, data)
            with patch.object(os, 'write', side_effect=interrupted): self.tasks.cancel(self.base)
            os._exit(4)
        os.close(ready_w)
        try:
            self.assertEqual(b'1', os.read(ready_r, 1))
            os.kill(child, signal.SIGKILL)
            self.assertEqual(-signal.SIGKILL, os.waitstatus_to_exitcode(os.waitpid(child, 0)[1]))
        finally:
            os.close(ready_r)
        self.assertEqual(b'', (self.directory() / 'cancel').read_bytes())
        self.assertEqual('Stopped', self.tasks.cancel(self.base)['status'])
        with patch.object(self.capture, 'plan', side_effect=AssertionError('late execution')):
            self.assertEqual('Stopped', self.tasks.run(self.base)['status'])
        self.assertEqual(0, self.execution.identity_calls)

    def test_empty_cancel_with_execution_is_corruption_and_never_reinitialized(self):
        with patch.object(self.capture, 'plan', side_effect=RuntimeError('interrupted')):
            with self.assertRaises(RuntimeError): self.tasks.run(self.base)
        (self.directory() / 'cancel').write_bytes(b'')
        for action in (self.tasks.run, self.tasks.observe, self.tasks.cancel):
            with self.subTest(action=action.__name__), self.assertRaisesRegex(fixture.files.Rejected, 'JOURNAL_UNSAFE'):
                action(self.base)
        self.assertEqual(b'', (self.directory() / 'cancel').read_bytes())

    def test_identical_copied_root_or_task_cannot_receive_a_long_task_result(self):
        original_plan = self.capture.plan
        for scope in ('root', 'task'):
            value = dict(self.base, taskId=str(uuid.uuid4()))
            def replace(*args):
                result = original_plan(*args)
                target = self.journal if scope == 'root' else self.directory(value)
                retained = target.with_name(target.name + '-retained')
                target.rename(retained); shutil.copytree(retained, target)
                self.addCleanup(shutil.rmtree, retained)
                return result
            with self.subTest(scope=scope), patch.object(self.capture, 'plan', side_effect=replace):
                with self.assertRaisesRegex(fixture.files.Rejected, 'JOURNAL_CHANGED'): self.tasks.run(value)
            self.assertFalse((self.directory(value) / 'result.json').exists())

    def test_completed_fact_is_not_downgraded_by_a_late_cancel(self):
        self.finish(self.base); self.execution.alive = False
        before = {p.name: p.read_bytes() for p in self.directory().iterdir()}
        result = self.tasks.cancel(self.base)
        self.assertEqual('Succeeded', result['status'])
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.directory().iterdir()})
        self.assertEqual(0, self.execution.stops)

    def test_result_is_not_terminal_until_original_execution_is_empty(self):
        self.tasks.run(self.base)
        self.assertEqual('Finishing', self.tasks.observe(self.base)['status'])
        self.execution.alive = False
        self.assertEqual('Succeeded', self.tasks.observe(self.base)['status'])

    def test_corrupt_completed_schema_or_request_binding_is_refused(self):
        self.finish(self.base); self.execution.alive = False
        path = self.directory() / 'result.json'
        original = json.loads(path.read_bytes())
        for change in ({'schemaVersion': True}, {'schemaVersion': 1.0}, {'taskId': str(uuid.uuid4())},
                       {'requestSha256': 'a' * 64}, {'status': 'Stopped'}, {'code': 'secret body'}):
            with self.subTest(change=change):
                path.write_bytes(self.engine.canonical(dict(original, **change)))
                with self.assertRaisesRegex(fixture.files.Rejected, 'JOURNAL_UNSAFE'):
                    self.tasks.observe(self.base)
        path.write_bytes(self.engine.canonical(original))
        self.assertEqual('Succeeded', self.tasks.observe(self.base)['status'])

    def test_dead_execution_is_interrupted_and_same_task_never_reexecutes(self):
        with patch.object(self.capture, 'plan', side_effect=RuntimeError('simulated crash')):
            with self.assertRaises(RuntimeError): self.tasks.run(self.base)
        self.assertEqual('Running', self.tasks.observe(self.base)['status'])
        self.execution.alive = False
        self.assertEqual('Interrupted', self.tasks.observe(self.base)['status'])
        with patch.object(self.capture, 'plan', side_effect=AssertionError('automatic replay')):
            self.assertEqual('Interrupted', self.tasks.run(self.base)['status'])
        self.assertEqual(1, self.execution.identity_calls)

    def test_observe_and_stop_remain_available_while_both_storage_locks_are_held(self):
        with patch.object(self.capture, 'plan', side_effect=RuntimeError('crashed at active work')):
            with self.assertRaises(RuntimeError): self.tasks.run(self.base)
        with self.engine.locked_root(self.request['rootId'], exclusive=True):
            with self.store.locked(exclusive=True):
                started = time.monotonic()
                self.assertEqual('Running', self.tasks.observe(self.base)['status'])
                self.assertEqual('Stopped', self.tasks.cancel(self.base)['status'])
                self.assertLess(time.monotonic() - started, 1)
        self.assertEqual(1, self.execution.stops)

    def test_codes_are_bounded_and_filesystem_failures_never_leak_arbitrary_text(self):
        for error, code in ((OSError(errno.ENOSPC, '/private/filename'), 'NO_SPACE'),
                            (OSError(errno.EDQUOT, 'private body'), 'QUOTA_EXCEEDED'),
                            (fixture.files.Rejected('secret text'), 'UNCONFIRMED')):
            value = dict(self.base, taskId=str(uuid.uuid4()))
            with patch.object(self.capture, 'plan', side_effect=error): self.tasks.run(value)
            self.execution.alive = False
            result = self.tasks.observe(value)
            self.assertEqual('Rejected', result['status']); self.assertEqual('WORKSPACE_ARCHIVE_' + code, result['code'])
            self.assertNotIn('private', json.dumps(result)); self.assertNotIn('secret', json.dumps(result))
            self.execution.alive = True

    def test_corrupt_cancellation_metadata_and_unsafe_journal_are_fail_closed(self):
        self.tasks.cancel(self.base)
        p = self.directory() / 'cancel'; p.write_bytes(b'yes')
        with self.assertRaisesRegex(fixture.files.Rejected, 'JOURNAL_UNSAFE'): self.tasks.observe(self.base)
        p.write_bytes(b'1'); p.chmod(0o644)
        with self.assertRaisesRegex(fixture.files.Rejected, 'JOURNAL_UNSAFE'): self.tasks.run(self.base)

    def test_history_limit_allows_original_task_but_refuses_new_ids(self):
        self.tasks.cancel(self.base)
        with patch.object(operations, 'MAX_TASKS', 1):
            self.assertEqual('Stopped', self.tasks.cancel(self.base)['status'])
            with self.assertRaisesRegex(fixture.files.Rejected, 'HISTORY_LIMIT'):
                self.tasks.cancel(dict(self.base, taskId=str(uuid.uuid4())))

    def test_invalid_request_shapes_do_not_allocate_journal_entries(self):
        for change in ({'expectedEpoch': True}, {'expectedEpoch': 3}, {'taskId': operations.ZERO}, {'archiveId': '../path'},
                       {'phase': 'delete'}, {'phase': 'plan', 'receiptSha256': 'a' * 64}, {'planSha256': 'a' * 64},
                       {'schemaVersion': True}, {'extra': True}):
            with self.subTest(change=change), self.assertRaises(fixture.files.Rejected): self.tasks.cancel(dict(self.base, **change))
        self.assertEqual([], list(self.journal.iterdir()))

    def test_actual_sigkill_after_execution_proof_preserves_interrupted_state(self):
        ready_r, ready_w = os.pipe()
        child = os.fork()
        if child == 0:
            os.close(ready_r)
            def blocked(*_):
                os.write(ready_w, b'1')
                signal.pause()
            with patch.object(self.capture, 'plan', side_effect=blocked): self.tasks.run(self.base)
            os._exit(4)
        os.close(ready_w)
        try:
            self.assertEqual(b'1', os.read(ready_r, 1))
            self.assertEqual('Running', self.tasks.observe(self.base)['status'])
            os.kill(child, signal.SIGKILL)
            self.assertEqual(-signal.SIGKILL, os.waitstatus_to_exitcode(os.waitpid(child, 0)[1]))
        finally:
            os.close(ready_r)
        self.execution.alive = False
        before = {p.name: p.read_bytes() for p in self.directory().iterdir()}
        self.assertEqual('Interrupted', self.tasks.run(self.base)['status'])
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.directory().iterdir()})


@unittest.skipUnless(os.geteuid() == 0, 'Requires private root-owned execution proof fixtures')
class ExecutionIdentityTests(unittest.TestCase):
    def setUp(self):
        home = tempfile.TemporaryDirectory(); self.addCleanup(home.cleanup)
        self.home = Path(home.name); self.cg = self.home / 'cgroup'; self.cg.mkdir(mode=0o700)
        (self.cg / 'system.slice').mkdir(mode=0o700)
        self.path = '/system.slice/whaledeck-development-archive-run-' + uuid.uuid4().hex + '.service'
        self.group = self.cg / self.path[1:]; self.group.mkdir(mode=0o700)
        (self.group / 'cgroup.events').write_bytes(b'populated 1\nfrozen 0\n')
        (self.group / 'cgroup.kill').write_bytes(b'0')
        self.boot = self.home / 'boot'; self.boot.write_text(str(uuid.uuid4()) + '\n')
        self.self = self.home / 'self'; self.self.write_text('0::' + self.path + '\n')
        for key, value in (('CGROUP', self.cg), ('BOOT', self.boot), ('SELF', self.self)):
            p = patch.object(control, key, value); p.start(); self.addCleanup(p.stop)
        self.driver = fixture.storage.WorkspaceStorage(fixture.files)
        self.execution = control.Execution(self.driver); self.proof = self.execution.identity()

    def test_live_and_empty_execution_are_distinct(self):
        self.assertFalse(self.execution.settled(self.proof))
        (self.group / 'cgroup.events').write_bytes(b'populated 0\n')
        self.assertTrue(self.execution.settled(self.proof))

    def test_wrong_inode_or_device_never_writes_kill(self):
        for key in ('inode', 'device'):
            with self.subTest(key=key), self.assertRaisesRegex(fixture.files.Rejected, 'EXECUTION_CHANGED'):
                self.execution.settled(dict(self.proof, **{key: self.proof[key] + 1}), stop=True)
        self.assertEqual(b'0', (self.group / 'cgroup.kill').read_bytes())

    def test_other_boot_and_missing_exact_path_prove_only_old_execution_gone(self):
        self.assertTrue(self.execution.settled(dict(self.proof, bootId=str(uuid.uuid4())), stop=True))
        for p in self.group.iterdir(): p.unlink()
        self.group.rmdir()
        self.assertTrue(self.execution.settled(self.proof, stop=True))

    def test_replaced_cgroup_at_same_name_is_refused(self):
        fd = os.open(self.group, os.O_RDONLY | os.O_DIRECTORY)
        try:
            for p in self.group.iterdir(): p.unlink()
            self.group.rmdir(); self.group.mkdir(mode=0o700)
            (self.group / 'cgroup.events').write_bytes(b'populated 1\n')
            (self.group / 'cgroup.kill').write_bytes(b'0')
            with self.assertRaisesRegex(fixture.files.Rejected, 'EXECUTION_CHANGED'): self.execution.settled(self.proof, stop=True)
            self.assertEqual(b'0', (self.group / 'cgroup.kill').read_bytes())
        finally:
            os.close(fd)

    def test_wrong_unit_family_or_malformed_proof_is_refused(self):
        for change in ({'path': '/system.slice/whaledeck-agent.service'}, {'inode': True}, {'bootId': 'unknown'}, {'extra': 1}):
            with self.subTest(change=change), self.assertRaisesRegex(fixture.files.Rejected, 'EXECUTION_INVALID'):
                self.execution.settled(dict(self.proof, **change), stop=True)

    def test_invalid_population_is_not_an_empty_proof(self):
        (self.group / 'cgroup.events').write_bytes(b'populated 0\npopulated 1\n')
        with self.assertRaisesRegex(fixture.files.Rejected, 'EXECUTION_INVALID'): self.execution.settled(self.proof)

    def test_stop_waits_for_exact_scope_to_be_empty(self):
        failures = []
        def kernel_double():
            try:
                end = time.monotonic() + 3
                while (self.group / 'cgroup.kill').read_bytes() != b'1':
                    if time.monotonic() >= end: raise AssertionError('No scoped kill')
                    time.sleep(0.005)
                (self.group / 'cgroup.events').write_bytes(b'populated 0\n')
            except BaseException as error: failures.append(error)
        thread = threading.Thread(target=kernel_double); thread.start()
        try: self.assertTrue(self.execution.settled(self.proof, stop=True))
        finally: thread.join(4)
        self.assertFalse(thread.is_alive()); self.assertEqual([], failures)


if __name__ == '__main__':
    unittest.main()
