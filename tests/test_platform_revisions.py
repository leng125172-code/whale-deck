"""Real temporary Git/filesystem and process cuts; no live deployment or credentials."""
import copy
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
SPEC = importlib.util.spec_from_file_location('platform_revisions', ROOT / 'deploy/host/whaledeck-platform-revisions.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class RevisionLedgerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='wd-revision-ledger-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state = self.root / 'state'
        self.state.mkdir(mode=0o700)
        self.repo = self.root / 'repo'
        self.repo.mkdir()
        self.git('init', '-q')
        self.revisions = []
        for index in range(3):
            (self.repo / 'source').write_text(str(index))
            self.git('add', '.')
            self.git('-c', 'user.name=Revision Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', str(index))
            self.revisions.append(self.git('rev-parse', 'HEAD').strip())
        self.a, self.b, self.c = self.revisions
        self.live = self.a
        self.inspections = 0

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.repo), *args], text=True, stderr=subprocess.DEVNULL)

    def exists(self, rev):
        p = subprocess.run(['git', '-C', str(self.repo), 'cat-file', '-t', rev], capture_output=True, text=True)
        return p.returncode == 0 and p.stdout.strip() == 'commit'

    def inspect(self):
        self.inspections += 1
        if isinstance(self.live, Exception):
            raise self.live
        return self.live

    def ledger(self):
        return M.Ledger(self.state, self.exists, self.inspect, owner=os.geteuid())

    def record(self, name, raw):
        p = self.state / name
        p.write_bytes(raw)
        p.chmod(0o600)
        return p

    def finish_b(self, ledger):
        op = ledger.prepare('update', self.b)
        self.live = self.b
        ledger.finish(op)
        return op

    def test_bootstrap_uses_live_revision_not_checkout_or_untrusted_legacy(self):
        self.record(M.PREVIOUS, (self.c + '\n').encode())
        with self.ledger() as ledger:
            op = ledger.prepare('update', self.b)
            state = ledger.load()
            self.assertEqual(state['currentRevision'], self.a)
            self.assertIsNone(state['previousRevision'])
            self.assertEqual(state['pending'], op)
            self.assertEqual((self.state / 'previous-revision.legacy').read_text().strip(), self.c)
            self.assertFalse((self.state / M.PREVIOUS).exists())
            self.live = self.b
            ledger.finish(op)
            self.assertEqual((self.state / M.PREVIOUS).read_text().strip(), self.a)
            self.assertEqual(ledger.load()['currentRevision'], self.b)

    def test_failure_and_retry_do_not_promote_target_or_lose_previous(self):
        with self.ledger() as ledger:
            self.finish_b(ledger)
            op = ledger.prepare('update', self.c)
            before = (self.state / M.LEDGER).read_bytes()
            self.live = M.Rejected()
            with self.assertRaises(M.Rejected):
                ledger.finish(op)
            self.assertEqual((self.state / M.LEDGER).read_bytes(), before)
            self.assertEqual(ledger.prepare('update', self.c), op)
            self.assertEqual((self.state / M.PREVIOUS).read_text().strip(), self.a)
            self.live = self.c
            ledger.finish(op)
            self.assertEqual(ledger.load()['previousRevision'], self.b)

    def test_failed_update_manual_recovery_uses_last_success_instead_of_older_previous(self):
        with self.ledger() as ledger:
            self.finish_b(ledger)
            ledger.prepare('update', self.c)
            self.live = M.Rejected()  # Mixed/offline runtime; saved baseline remains authoritative.
            recovery = ledger.prepare('rollback')
            self.assertEqual(recovery['revision'], self.b)
            self.live = self.b
            ledger.finish(recovery)
            self.assertEqual(ledger.load()['previousRevision'], self.a)

    def test_explicit_manual_rollback_and_retry_swap_only_successful_revisions(self):
        with self.ledger() as ledger:
            self.finish_b(ledger)
            op = ledger.prepare('rollback')
            self.assertEqual(op['revision'], self.a)
            self.assertEqual(ledger.prepare('rollback'), op)
            self.live = self.a
            ledger.finish(op)
            self.assertEqual(ledger.load()['currentRevision'], self.a)
            self.assertEqual(ledger.load()['previousRevision'], self.b)

    def test_same_revision_and_repeated_finish_preserve_real_previous(self):
        with self.ledger() as ledger:
            self.finish_b(ledger)
            op = ledger.prepare('update', self.b)
            ledger.finish(op)
            raw = (self.state / M.LEDGER).read_bytes()
            ledger.finish(op)
            self.assertEqual((self.state / M.LEDGER).read_bytes(), raw)
            self.assertEqual(ledger.load()['previousRevision'], self.a)

    def test_missing_trusted_previous_refuses_manual_rollback(self):
        with self.ledger() as ledger:
            with self.assertRaises(M.Rejected):
                ledger.prepare('rollback')
            self.assertFalse((self.state / M.LEDGER).exists())
            self.assertEqual(self.inspections, 0)

    def test_wrong_runtime_stale_token_or_action_cannot_finish(self):
        with self.ledger() as ledger:
            op = ledger.prepare('update', self.b)
            for wrong in ({**op, 'token': 'f' * 32}, {**op, 'action': 'rollback'}, {**op, 'revision': self.c}, op):
                with self.subTest(wrong=wrong), self.assertRaises(M.Rejected):
                    ledger.finish(wrong)
            self.assertEqual(ledger.load()['currentRevision'], self.a)

    def test_new_target_invalidates_late_old_completion(self):
        with self.ledger() as ledger:
            old = ledger.prepare('update', self.b)
            new = ledger.prepare('update', self.c)
            self.live = self.b
            with self.assertRaises(M.Rejected):
                ledger.finish(old)
            self.assertEqual(ledger.load()['pending'], new)

    def test_blob_missing_revision_and_bad_revision_refuse_without_publication(self):
        with self.ledger() as ledger:
            for target in (self.git('rev-parse', 'HEAD:source').strip(), 'f' * 40, '../source', True):
                with self.subTest(target=target), self.assertRaises(M.Rejected):
                    ledger.prepare('update', target)
            self.assertFalse((self.state / M.LEDGER).exists())

    def test_corrupt_or_extra_state_never_falls_back_to_runtime_guessing(self):
        with self.ledger() as ledger:
            ledger.prepare('update', self.b)
            valid = ledger.load()
            bad = [b'{broken', json.dumps({**valid, 'schemaVersion': True}).encode(), json.dumps({**valid, 'extra': 'ignored?'}).encode(),
                json.dumps({**valid, 'previousRevision': self.a}).encode(), b'{"schemaVersion":1,"schemaVersion":1}']
            before = self.inspections
            for raw in bad:
                self.record(M.LEDGER, raw)
                with self.subTest(raw=raw), self.assertRaises((M.Rejected, ValueError)):
                    ledger.prepare('update', self.c)
                self.assertEqual((self.state / M.LEDGER).read_bytes(), raw)
            self.assertEqual(before, self.inspections)

    def test_symlink_hardlink_or_world_readable_ledger_refuses(self):
        outside = self.record('outside', b'preserve')
        p = self.state / M.LEDGER
        with self.ledger() as ledger:
            p.symlink_to(outside)
            with self.assertRaises(OSError):
                ledger.load()
            p.unlink()
            os.link(outside, p)
            with self.assertRaises(M.Rejected):
                ledger.load()
            p.unlink()
            p.write_text('{}')
            p.chmod(0o644)
            with self.assertRaises(M.Rejected):
                ledger.load()
        self.assertEqual(outside.read_bytes(), b'preserve')

    def test_state_directory_symlink_or_wrong_owner_refuses(self):
        alias = self.root / 'alias'
        alias.symlink_to(self.state, target_is_directory=True)
        with self.assertRaises(OSError):
            M.Ledger(alias, self.exists, self.inspect, owner=os.geteuid())
        with self.assertRaises(M.Rejected):
            M.Ledger(self.state, self.exists, self.inspect, owner=os.geteuid() + 1)

    def test_replaced_directory_is_refused_without_writing_either_path(self):
        with self.ledger() as ledger:
            ledger.prepare('update', self.b)
            saved = (self.state / M.LEDGER).read_bytes()
            moved = self.root / 'moved'
            self.state.rename(moved)
            self.state.mkdir(mode=0o700)
            with self.assertRaises(M.Rejected):
                ledger.prepare('update', self.c)
            self.assertEqual(list(self.state.iterdir()), [])
            self.assertEqual((moved / M.LEDGER).read_bytes(), saved)

    def test_projection_failure_is_repaired_by_same_completed_token(self):
        with self.ledger() as ledger:
            op = ledger.prepare('update', self.b)
            self.live = self.b
            with patch.object(ledger, 'project_previous', side_effect=OSError('fixture-full')):
                with self.assertRaises(OSError):
                    ledger.finish(op)
            self.assertEqual(ledger.load()['currentRevision'], self.b)
            ledger.finish(op)
            self.assertEqual((self.state / M.PREVIOUS).read_text().strip(), self.a)

    def test_rename_failure_retains_complete_old_state_and_cleans_own_temp(self):
        with self.ledger() as ledger:
            ledger.prepare('update', self.b)
            before = (self.state / M.LEDGER).read_bytes()
            with patch.object(M.os, 'replace', side_effect=OSError('fixture-full')):
                with self.assertRaises(OSError):
                    ledger.prepare('update', self.c)
            self.assertEqual((self.state / M.LEDGER).read_bytes(), before)
            self.assertEqual(list(self.state.glob('.revision-*')), [])

    @unittest.skipUnless(hasattr(os, 'fork'), 'POSIX process cut required')
    def test_real_process_kill_before_and_after_atomic_rename(self):
        for after in (False, True):
            with self.subTest(after=after), self.ledger() as ledger:
                op = ledger.prepare('update', self.b)
                self.live = self.b
                pid = os.fork()
                if pid == 0:
                    replace = M.os.replace
                    def cut(*args, **kwargs):
                        if after:
                            replace(*args, **kwargs)
                        os.kill(os.getpid(), signal.SIGKILL)
                    M.os.replace = cut
                    ledger.finish(op)
                    os._exit(99)
                _, status = os.waitpid(pid, 0)
                self.assertTrue(os.WIFSIGNALED(status))
                self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                state = ledger.load()
                self.assertEqual(state['currentRevision'], self.b if after else self.a)
                if after:
                    ledger.finish(op)
                    self.assertEqual(state['previousRevision'], self.a)


class FakeRuntime(M.RunningRevision):
    def __init__(self):
        super().__init__(self.command)
        self.rev = 'a' * 40
        self.change = None
        self.frames = {s: [str(i) * 64, 'sha256:' + str(i + 3) * 64, 'true'] for i, s in enumerate(('api', 'worker', 'gateway'), 1)}
        self.hostframes = {'agent': ('100', 1, 2, 3, 4), 'maintenance': ('101', 1, 5, 3, 6)}
        self.migrator = 'sha256:' + '7' * 64
        self.commands = []

    def hosts(self):
        return copy.deepcopy(self.hostframes)

    def command(self, args):
        self.commands.append(args)
        if args[:2] == ['docker', 'inspect']:
            service = args[-1].removeprefix('whaledeck-')
            return '|'.join(self.frames[service])
        if args[:3] == ['docker', 'image', 'inspect']:
            return self.migrator if args[-1] == 'whaledeck-migrator' else self.rev
        if args[:2] == ['docker', 'exec']:
            component = 'WhaleDeck.' + args[2].removeprefix('whaledeck-').title()
        elif args[:2] == ['docker', 'run']:
            component = 'WhaleDeck.Migrator'
            self.migrator_command = args
            if self.change == 'container-race':
                self.frames['api'][0] = 'f' * 64
        else:
            component = 'WhaleDeck.Agent' if '/100/' in args[0] else 'WhaleDeck.MaintenanceHost'
        data = {'component': component, 'version': '0.1.0', 'sourceRevision': self.rev}
        if self.change == component:
            data['sourceRevision'] = 'b' * 40
        if self.change == 'extra':
            data['untrusted'] = 'ignored?'
        return json.dumps(data)


class RunningRevisionTests(unittest.TestCase):
    def test_all_six_actual_identities_agree_and_migrator_uses_pinned_image(self):
        runtime = FakeRuntime()
        self.assertEqual(runtime(), runtime.rev)
        self.assertIn(runtime.migrator, runtime.migrator_command)
        self.assertIn('--read-only', runtime.migrator_command)
        self.assertEqual(runtime.migrator_command[runtime.migrator_command.index('--network') + 1], 'none')

    def test_mixed_metadata_extra_fields_or_changing_container_refuse_bootstrap(self):
        for mode in ('WhaleDeck.Api', 'WhaleDeck.Worker', 'WhaleDeck.Gateway', 'WhaleDeck.Agent', 'WhaleDeck.MaintenanceHost', 'WhaleDeck.Migrator', 'extra', 'container-race'):
            runtime = FakeRuntime()
            runtime.change = mode
            with self.subTest(mode=mode), self.assertRaises(M.Rejected):
                runtime()

    def test_ordinary_cli_refuses_before_any_live_command(self):
        if os.geteuid() == 0:
            self.skipTest('ordinary caller checked in non-root CI')
        p = subprocess.run([sys.executable, '-I', str(ROOT / 'deploy/host/whaledeck-platform-revisions.py'), 'probe'], capture_output=True, text=True, timeout=5)
        self.assertNotEqual(p.returncode, 0)
        self.assertEqual(p.stdout, '')
        self.assertTrue(p.stderr.startswith('PLATFORM_REVISION_UNPROVEN:'))


class PublicStatusTests(unittest.TestCase):
    def test_atomic_status_replaces_symlink_instead_of_writing_its_target(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            outside = root / 'unrelated'
            outside.write_bytes(b'preserve')
            status = root / 'status.json'
            status.symlink_to(outside)
            M.write_status(status, 'updating', os.getgid(), owner=os.geteuid())
            self.assertFalse(status.is_symlink())
            self.assertEqual(outside.read_bytes(), b'preserve')
            data = json.loads(status.read_text())
            self.assertEqual(set(data), {'state', 'message', 'updatedAtUtc'})
            self.assertEqual(data['state'], 'updating')
            self.assertEqual(status.stat().st_mode & 0o777, 0o640)

    def test_write_failure_leaves_original_status_complete_and_cleans_temp(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            status = root / 'status.json'
            status.write_bytes(b'original')
            with patch.object(M.os, 'fsync', side_effect=OSError('fixture-full')):
                with self.assertRaises(OSError):
                    M.write_status(status, 'failed', os.getgid(), owner=os.geteuid())
            self.assertEqual(status.read_bytes(), b'original')
            self.assertEqual(list(root.glob('.status-*')), [])

    def test_unknown_status_refuses_before_opening_a_directory(self):
        with self.assertRaises(M.Rejected):
            M.write_status(Path('/not-opened/status.json'), 'untrusted', os.getgid(), owner=os.geteuid())


if __name__ == '__main__':
    unittest.main()
