"""Controller checkpoints, actual temporary process kills and no application replay."""
import fcntl
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('platform_control_recovery', ROOT / 'deploy/host/whaledeck-platform-control-recovery.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class ControlRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='wd-control-recovery-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state, self.libexec = self.root / 'state', self.root / 'libexec'
        self.state.mkdir(mode=0o700)
        self.libexec.mkdir(mode=0o755)
        self.libexec.chmod(0o755)
        self.originals = {name: ('trusted ' + name + '\n').encode() for name in M.CONTROLS}
        self.current = 'a' * 40
        self.op = dict(action='rollback', revision='d' * 40, token='c' * 32)
        self.ledger = dict(schemaVersion=1, currentRevision=self.current, previousRevision='d' * 40, pending=self.op,
            lastCompleted=dict(action='update', revision=self.current, token='e' * 32))
        self.status = self.root / 'public-status'
        self.app = self.root / 'application'
        self.app.write_bytes(b'application bytes must never change')
        self.install(self.originals)
        self.save_ledger()

    def save_ledger(self):
        p = self.state / 'deployment-revisions.json'
        p.write_bytes(M.encoded(self.ledger))
        p.chmod(0o600)

    def install(self, values):
        for name, raw in values.items():
            path = self.libexec / name
            path.write_bytes(raw)
            path.chmod(0o750)

    def recovery(self):
        return M.Recovery(self.state, self.libexec, owner=os.geteuid(), group=os.getgid(), status=lambda value: self.status.write_text(value))

    def checkpoint(self):
        capture = self.recovery().checkpoint(self.op)
        self.assertEqual(len(capture), 32)
        return capture

    def complete_ledger(self):
        self.ledger.update(currentRevision=self.op['revision'], previousRevision=self.current, pending=None, lastCompleted=self.op)
        self.save_ledger()

    def assert_originals(self):
        for name, raw in self.originals.items():
            self.assertEqual((self.libexec / name).read_bytes(), raw)
            self.assertEqual((self.libexec / name).stat().st_mode & 0o777, 0o750)
        self.assertEqual(self.app.read_bytes(), b'application bytes must never change')

    def test_durable_checkpoint_precedes_mutation_and_repeated_request_reuses_it(self):
        capture = self.checkpoint()
        self.assertEqual(self.recovery().checkpoint(self.op), capture)
        self.install({name: b'old installer replacement' for name in M.CONTROLS})
        self.assertEqual(self.recovery().checkpoint(self.op), capture)
        state = self.state / ('control-' + capture)
        self.assertEqual(set(p.name for p in state.iterdir()), {*M.CONTROLS, 'checkpoint.json'})
        self.assertEqual(state.stat().st_mode & 0o777, 0o700)
        self.assertTrue(all(p.stat().st_mode & 0o777 == 0o600 for p in state.iterdir()))

    def test_new_process_style_recovery_restores_only_controllers_not_ledger_or_application(self):
        capture = self.checkpoint()
        self.install({name: b'old code' for name in M.CONTROLS})
        raw = (self.state / 'deployment-revisions.json').read_bytes()
        result = self.recovery().recover()
        self.assert_originals()
        self.assertEqual((self.state / 'deployment-revisions.json').read_bytes(), raw)
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertFalse(result['applicationReplayed'])
        self.assertFalse(result['automaticRollback'])
        self.assertEqual(self.status.read_text(), 'failed')
        self.assertFalse((self.state / M.ACTIVE).exists())
        self.assertEqual(json.loads((self.state / ('control-' + capture) / 'receipt.json').read_text()), result)
        before = [(p.name, p.stat().st_mtime_ns) for p in self.libexec.iterdir()]
        self.assertIsNone(self.recovery().recover())
        self.assertEqual(before, [(p.name, p.stat().st_mtime_ns) for p in self.libexec.iterdir()])

    def test_finished_update_keeps_new_controls_instead_of_downgrading_them(self):
        self.op['action'] = 'update'
        self.save_ledger()
        capture = self.checkpoint()
        new = {name: b'verified new version' for name in M.CONTROLS}
        self.install(new)
        self.complete_ledger()
        result = self.recovery().complete(capture, self.op)
        self.assertFalse(result['controlsRestored'])
        self.assertEqual(result['outcome'], 'completed')
        for name, raw in new.items():
            self.assertEqual((self.libexec / name).read_bytes(), raw)
        self.assertEqual(self.status.read_text(), 'healthy')

    def test_completed_ledger_without_explicit_commit_is_interrupted_but_not_downgraded(self):
        self.op['action'] = 'update'
        self.save_ledger()
        self.checkpoint()
        new = {name: b'new deployed controller' for name in M.CONTROLS}
        self.install(new)
        self.complete_ledger()
        result = self.recovery().recover()
        self.assertFalse(result['controlsRestored'])
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(self.status.read_text(), 'failed')
        self.assertEqual((self.libexec / M.CONTROLS[0]).read_bytes(), new[M.CONTROLS[0]])

    def test_manual_completion_restores_current_controller_and_marks_healthy(self):
        capture = self.checkpoint()
        self.install({name: b'old application installer controller' for name in M.CONTROLS})
        self.complete_ledger()
        result = self.recovery().complete(capture, self.op)
        self.assert_originals()
        self.assertTrue(result['controlsRestored'])
        self.assertEqual(result['outcome'], 'completed')
        self.assertEqual(self.status.read_text(), 'healthy')

    def test_wrong_token_changed_pending_or_invalid_manifest_refuses_before_replacement(self):
        self.checkpoint()
        old = {name: b'old controller' for name in M.CONTROLS}
        self.install(old)
        self.ledger['pending'] = {**self.op, 'token': 'f' * 32}
        self.save_ledger()
        with self.assertRaises(M.Rejected):
            self.recovery().recover()
        for name, raw in old.items():
            self.assertEqual((self.libexec / name).read_bytes(), raw)
        self.assertFalse(self.status.exists())

    def test_bad_source_hash_permissions_link_or_extra_member_never_restores_any_file(self):
        for mode in ('hash', 'mode', 'symlink', 'hardlink', 'extra'):
            with self.subTest(mode=mode):
                self.temp.cleanup()
                self.setUp()
                capture = self.checkpoint()
                self.install({name: b'untouched until all sources verified' for name in M.CONTROLS})
                directory = self.state / ('control-' + capture)
                p = directory / M.CONTROLS[-1]
                if mode == 'hash':
                    p.write_bytes(b'corrupt')
                elif mode == 'mode':
                    p.chmod(0o644)
                elif mode == 'symlink':
                    p.unlink()
                    p.symlink_to(self.app)
                elif mode == 'hardlink':
                    p.unlink()
                    os.link(self.app, p)
                else:
                    (directory / 'unknown').write_text('preserve')
                with self.assertRaises((M.Rejected, OSError)):
                    self.recovery().recover()
                self.assertTrue(all((self.libexec / name).read_bytes() == b'untouched until all sources verified' for name in M.CONTROLS))
                self.assertEqual(self.app.read_bytes(), b'application bytes must never change')

    def test_restore_replaces_destination_symlink_without_following_it(self):
        self.checkpoint()
        p = self.libexec / M.CONTROLS[0]
        p.unlink()
        p.symlink_to(self.app)
        self.recovery().recover()
        self.assertFalse(p.is_symlink())
        self.assert_originals()

    def test_status_failure_after_receipt_retries_without_application_replay(self):
        self.checkpoint()
        self.install({name: b'old controller' for name in M.CONTROLS})
        raw = (self.state / 'deployment-revisions.json').read_bytes()
        r = self.recovery()
        r.status = lambda _: (_ for _ in ()).throw(OSError('fixture-full'))
        with self.assertRaises(OSError):
            r.recover()
        self.assertTrue((self.state / M.ACTIVE).exists())
        self.assert_originals()
        self.recovery().recover()
        self.assert_originals()
        self.assertEqual((self.state / 'deployment-revisions.json').read_bytes(), raw)

    @unittest.skipUnless(hasattr(os, 'fork'), 'POSIX process cuts required')
    def test_real_sigkill_before_and_after_each_fixed_control_replacement_converges(self):
        for name in M.CONTROLS:
            for after in (False, True):
                with self.subTest(name=name, after=after):
                    self.temp.cleanup()
                    self.setUp()
                    self.checkpoint()
                    self.install({key: b'old installer code' for key in M.CONTROLS})
                    raw = (self.state / 'deployment-revisions.json').read_bytes()
                    child = os.fork()
                    if child == 0:
                        original = M.Directory.write
                        def cut(directory, target, content, mode=0o600):
                            if directory.root == self.libexec and target == name:
                                if after:
                                    original(directory, target, content, mode)
                                os.kill(os.getpid(), signal.SIGKILL)
                            return original(directory, target, content, mode)
                        M.Directory.write = cut
                        self.recovery().recover()
                        os._exit(99)
                    _, status = os.waitpid(child, 0)
                    self.assertTrue(os.WIFSIGNALED(status))
                    self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                    self.recovery().recover()
                    self.assert_originals()
                    self.assertEqual((self.state / 'deployment-revisions.json').read_bytes(), raw)

    @unittest.skipUnless(hasattr(os, 'fork'), 'POSIX process cut required')
    def test_kill_after_commit_marker_is_completed_by_fresh_recovery(self):
        capture = self.checkpoint()
        self.install({name: b'old controller' for name in M.CONTROLS})
        self.complete_ledger()
        child = os.fork()
        if child == 0:
            original = M.Directory.write
            def cut(directory, name, raw, mode=0o600):
                original(directory, name, raw, mode)
                if directory.root == self.state and name == M.ACTIVE:
                    os.kill(os.getpid(), signal.SIGKILL)
            M.Directory.write = cut
            self.recovery().complete(capture, self.op)
            os._exit(99)
        _, status = os.waitpid(child, 0)
        self.assertTrue(os.WIFSIGNALED(status))
        result = self.recovery().recover()
        self.assertEqual(result['outcome'], 'completed')
        self.assertEqual(self.status.read_text(), 'healthy')
        self.assert_originals()

    @unittest.skipUnless(hasattr(os, 'fork'), 'POSIX process cut required')
    def test_unpublished_checkpoint_cut_never_triggers_restoration(self):
        child = os.fork()
        if child == 0:
            original = M.Directory.write
            def cut(directory, name, raw, mode=0o600):
                if directory.root == self.state and name == M.ACTIVE:
                    os.kill(os.getpid(), signal.SIGKILL)
                return original(directory, name, raw, mode)
            M.Directory.write = cut
            self.checkpoint()
            os._exit(99)
        _, status = os.waitpid(child, 0)
        self.assertTrue(os.WIFSIGNALED(status))
        self.assertIsNone(self.recovery().recover())
        self.assertFalse(self.status.exists())
        self.assert_originals()

    def test_installation_preserves_recovery_runner_during_manual_rollback(self):
        self.checkpoint()
        runner = self.libexec / 'whaledeck-platform-control-recovery.py'
        runner.write_bytes(b'trusted existing recovery')
        runner.chmod(0o750)
        result = M.install_sources(self.root / 'not-read', state=self.state, libexec=self.libexec, owner=os.geteuid(), group=os.getgid())
        self.assertEqual(result, {'installationMode': 'preserve', 'filesReplaced': 0})
        self.assertEqual(runner.read_bytes(), b'trusted existing recovery')

    def test_installation_atomically_installs_four_bounded_files(self):
        source, units, tmpfiles = (self.root / n for n in ('source', 'systemd', 'tmpfiles'))
        for directory in (source, units, tmpfiles):
            directory.mkdir(mode=0o755)
            directory.chmod(0o755)
        for name in ('whaledeck-platform-control-recovery.py', 'whaledeck-platform-control-recovery.service', 'whaledeck-platform-control-recovery.timer', 'whaledeck-platform-control.conf'):
            (source / name).write_bytes((ROOT / 'deploy/host' / name).read_bytes())
        result = M.install_sources(source, state=self.state, libexec=self.libexec, systemd=units, tmpfiles=tmpfiles, owner=os.geteuid(), group=os.getgid())
        self.assertEqual(result['filesReplaced'], 4)
        self.assertEqual((self.libexec / 'whaledeck-platform-control-recovery.py').stat().st_mode & 0o777, 0o750)
        self.assertEqual((units / 'whaledeck-platform-control-recovery.service').stat().st_mode & 0o777, 0o644)

    @unittest.skipUnless(hasattr(os, 'fork'), 'POSIX lock inheritance required')
    def test_inherited_fd_lock_verifies_identity_and_serializes_other_openers(self):
        child = os.fork()
        if child == 0:
            try:
                path = self.root / 'lock'
                fd = M.lock_file(path, owner=os.geteuid())
                os.dup2(fd, 9)
                if fd != 9:
                    os.close(fd)
                fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
                M.require_held(path, owner=os.geteuid())
                other = M.lock_file(path, owner=os.geteuid())
                try:
                    fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    os._exit(98)
                except BlockingIOError:
                    pass
                finally:
                    os.close(other)
                os._exit(0)
            except BaseException:
                os._exit(99)
        _, status = os.waitpid(child, 0)
        self.assertTrue(os.WIFEXITED(status))
        self.assertEqual(os.WEXITSTATUS(status), 0)

    def test_ordinary_recovery_cli_is_rejected_without_reading_production_state(self):
        if os.geteuid() == 0:
            self.skipTest('ordinary caller covered in non-root CI')
        p = subprocess.run([sys.executable, '-I', str(ROOT / 'deploy/host/whaledeck-platform-control-recovery.py'), 'recover'], capture_output=True, text=True, timeout=5)
        self.assertNotEqual(p.returncode, 0)
        self.assertEqual(p.stdout, '')
        self.assertEqual(json.loads(p.stderr)['code'], 'PLATFORM_CONTROL_RECOVERY_UNPROVEN')


if __name__ == '__main__':
    unittest.main()
