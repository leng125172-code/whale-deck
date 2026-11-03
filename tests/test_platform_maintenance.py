"""Execute the maintenance shell against real temporary Git and fake deployment boundaries.

Only fixed paths/required UID and the runtime-inspection boundary are relocated in
the fixture copy. Production has no environment override for privileged paths.
"""
import json
import os
from pathlib import Path
import subprocess
import signal
import time
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == 'posix', 'Linux shell required')
class MaintenanceShellTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='wd-maintenance-shell-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'repo'
        self.source.mkdir()
        self.state = self.root / 'state'
        self.state.mkdir(mode=0o700)
        self.libexec = self.root / 'libexec'
        self.libexec.mkdir()
        self.libexec.chmod(0o755)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.events = self.root / 'events'
        self.live = self.root / 'live'
        (self.source / 'deploy/host').mkdir(parents=True)
        (self.source / '.gitignore').write_text('.env\n.images.env\n')
        (self.source / 'compose.yml').write_text('name: whaledeck\nservices: {}\n')
        publish = self.source / 'deploy/host/publish-and-install.sh'
        publish.write_text('''#!/usr/bin/env bash
set -eu
printf 'install\\n' >> "$FIXTURE_ROOT/events"
if [[ ${OVERWRITE_CONTROL:-0} == 1 ]]; then
  printf 'old shell controller\\n' > "$FIXTURE_ROOT/libexec/whaledeck-platform-maintenance"
  printf 'old ledger controller\\n' > "$FIXTURE_ROOT/libexec/whaledeck-platform-revisions.py"
  printf 'old scheduler wrapper\\n' > "$FIXTURE_ROOT/libexec/whaledeck-privileged"
fi
if [[ ${PAUSE_AFTER_CONTROL:-0} == 1 ]]; then
  printf 'ready\\n' > "$FIXTURE_ROOT/old-installed"
  sleep 120
fi
[[ ${FAIL_INSTALL:-0} == 0 ]]
''')
        publish.chmod(0o700)
        (self.source / 'deploy/build-images.sh').write_text('''#!/usr/bin/env bash
set -eu
printf 'build\\n' >> "$FIXTURE_ROOT/events"
[[ ${FAIL_BUILD:-0} == 0 ]]
''')
        self.git('init', '-q')
        self.git('add', '.')
        self.git('-c', 'user.name=Maintenance Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'first')
        self.a = self.git('rev-parse', 'HEAD').strip()
        (self.source / 'source').write_text('next')
        self.git('add', '.')
        self.git('-c', 'user.name=Maintenance Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'second')
        self.b = self.git('rev-parse', 'HEAD').strip()
        self.live.write_text(self.a)
        (self.source / '.env').write_text('PRIVATE_FIXTURE=not-a-credential\n')
        (self.source / '.images.env').write_text('PINNED_FIXTURE=not-a-real-image\n')
        shell = (ROOT / 'deploy/host/whaledeck-platform-maintenance').read_text()
        substitutions = {
            '/data/GitRepos/whale-deck': str(self.source),
            '/var/lib/whaledeck-agent/platform-maintenance': str(self.state),
            '/run/whaledeck/maintenance/status.json': str(self.root / 'status/status.json'),
            '/run/lock/whaledeck-platform-maintenance.lock': str(self.root / 'lock'),
            '/usr/local/libexec': str(self.libexec),
            '[[ $EUID -eq 0 ]]': f'[[ $EUID -eq {os.geteuid()} ]]',
        }
        for old, new in substitutions.items():
            self.assertIn(old, shell)
            shell = shell.replace(old, new)
        self.helper = self.libexec / 'whaledeck-platform-maintenance'
        self.helper.write_text(shell)
        self.helper.chmod(0o750)
        manager = (ROOT / 'deploy/host/whaledeck-platform-revisions.py').read_text()
        manager = manager.replace("SOURCE = Path('/data/GitRepos/whale-deck')", 'SOURCE = Path(' + repr(str(self.source)) + ')')
        manager = manager.replace("STATE = Path('/var/lib/whaledeck-agent/platform-maintenance')", 'STATE = Path(' + repr(str(self.state)) + ')')
        manager = manager.replace("STATUS = Path('/run/whaledeck/maintenance/status.json')", 'STATUS = Path(' + repr(str(self.root / 'status/status.json')) + ')')
        manager = manager.replace("grp.getgrnam('whaledeck').gr_gid", 'os.getgid()')
        manager = manager.replace('require(os.geteuid() == 0)', f'require(os.geteuid() == {os.geteuid()})')
        manager = manager.replace('owner=0', f'owner={os.geteuid()}')
        manager = manager.replace('RunningRevision()', '(lambda: Path(' + repr(str(self.live)) + ').read_text())')
        self.manager = self.libexec / 'whaledeck-platform-revisions.py'
        self.manager.write_text(manager)
        self.manager.chmod(0o750)
        self.wrapper = self.libexec / 'whaledeck-privileged'
        self.wrapper.write_text('#!/bin/sh\n# fixture scheduler\n')
        self.wrapper.chmod(0o750)
        recovery = (ROOT / 'deploy/host/whaledeck-platform-control-recovery.py').read_text()
        for old, new in [("STATE = Path('/var/lib/whaledeck-agent/platform-maintenance')", 'STATE = Path(' + repr(str(self.state)) + ')'),
                ("LIBEXEC = Path('/usr/local/libexec')", 'LIBEXEC = Path(' + repr(str(self.libexec)) + ')'),
                ("LOCK = Path('/run/lock/whaledeck-platform-maintenance.lock')", 'LOCK = Path(' + repr(str(self.root / 'lock')) + ')'),
                ("STATUS = Path('/run/whaledeck/maintenance/status.json')", 'STATUS = Path(' + repr(str(self.root / 'status/status.json')) + ')')]:
            self.assertIn(old, recovery)
            recovery = recovery.replace(old, new)
        recovery = recovery.replace('owner=0', f'owner={os.geteuid()}').replace('group=0', f'group={os.getgid()}')
        recovery = recovery.replace('require(os.geteuid() == 0)', f'require(os.geteuid() == {os.geteuid()})')
        recovery = recovery.replace("os.fchown(fd, 0, grp.getgrnam('whaledeck').gr_gid)", f'os.fchown(fd, {os.geteuid()}, {os.getgid()})')
        self.recovery = self.libexec / 'whaledeck-platform-control-recovery.py'
        self.recovery.write_text(recovery)
        self.recovery.chmod(0o750)
        self.control = {p.name: p.read_bytes() for p in (self.helper, self.manager, self.wrapper)}
        install = self.bin / 'install'
        install.write_text('''#!/usr/bin/env python3
import os,subprocess,sys
a=sys.argv[1:];out=[];i=0
while i<len(a):
 if a[i] in ('-o','-g'):i+=2
 else:out.append(a[i]);i+=1
assert os.path.abspath(out[-1]).startswith(os.environ['FIXTURE_ROOT']+'/')
raise SystemExit(subprocess.run(['/usr/bin/install',*out]).returncode)
''')
        install.chmod(0o700)
        chown = self.bin / 'chown'
        chown.write_text('#!/bin/sh\nexit 0\n')
        chown.chmod(0o700)
        docker = self.bin / 'docker'
        docker.write_text('''#!/usr/bin/env python3
import os,pathlib,subprocess,sys
a=sys.argv[1:];root=pathlib.Path(os.environ['FIXTURE_ROOT']);assert a[:2]==['compose','--project-directory']
with (root/'events').open('a') as f:f.write('compose '+('migrate' if 'migrator' in a else 'up' if 'up' in a else 'config')+'\\n')
if 'up' in a:
 if os.environ.get('FAIL_UP')=='1':raise SystemExit(19)
 revision=subprocess.check_output(['git','-C',a[2],'rev-parse','HEAD'],text=True).strip();(root/'live').write_text(revision)
''')
        docker.chmod(0o700)

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.source), *args], text=True, stderr=subprocess.DEVNULL)

    def execute(self, action, **extra):
        env = {**os.environ, 'FIXTURE_ROOT': str(self.root), 'PATH': str(self.bin) + ':' + os.environ['PATH'], **extra}
        return subprocess.run(['bash', str(self.helper), action], env=env, capture_output=True, text=True, timeout=15)

    def ledger(self):
        return json.loads((self.state / 'deployment-revisions.json').read_text())

    def test_success_records_actual_running_revision_and_success_only(self):
        (self.state / 'previous-revision').write_text(self.b + '\n')
        (self.state / 'previous-revision').chmod(0o600)
        p = self.execute('update')
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual(self.ledger()['currentRevision'], self.b)
        self.assertEqual(self.ledger()['previousRevision'], self.a)
        self.assertEqual((self.state / 'previous-revision.legacy').read_text().strip(), self.b)
        self.assertEqual(json.loads((self.root / 'status/status.json').read_text())['state'], 'healthy')

    def test_failed_publish_preserves_baseline_pending_and_failed_status_then_retries(self):
        p = self.execute('update', FAIL_BUILD='1')
        self.assertNotEqual(p.returncode, 0)
        self.assertEqual(self.ledger()['currentRevision'], self.a)
        pending = self.ledger()['pending']
        self.assertEqual(json.loads((self.root / 'status/status.json').read_text())['state'], 'failed')
        self.assertEqual(self.live.read_text(), self.a)
        p = self.execute('update')
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual(self.ledger()['lastCompleted'], pending)
        self.assertEqual(self.ledger()['previousRevision'], self.a)

    def test_manual_rollback_uses_exact_git_worktree_without_database_downgrade(self):
        self.assertEqual(self.execute('update').returncode, 0)
        self.events.write_text('')
        p = self.execute('rollback', OVERWRITE_CONTROL='1')
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual(self.live.read_text(), self.a)
        self.assertEqual(self.ledger()['currentRevision'], self.a)
        self.assertEqual(self.ledger()['previousRevision'], self.b)
        self.assertNotIn('migrate', self.events.read_text())
        for name, raw in self.control.items():
            self.assertEqual((self.libexec / name).read_bytes(), raw)
        self.assertFalse((self.state / 'rollback-src').exists())
        self.assertEqual(list(self.state.glob('control.*')), [])
        self.assertEqual(self.git('status', '--porcelain'), '')

    def test_failed_manual_rollback_restores_controller_and_retains_pending(self):
        self.assertEqual(self.execute('update').returncode, 0)
        p = self.execute('rollback', OVERWRITE_CONTROL='1', FAIL_UP='1')
        self.assertNotEqual(p.returncode, 0)
        self.assertEqual(self.ledger()['currentRevision'], self.b)
        self.assertEqual(self.ledger()['pending']['revision'], self.a)
        for name, raw in self.control.items():
            self.assertEqual((self.libexec / name).read_bytes(), raw)
        self.assertFalse((self.state / 'rollback-src').exists())
        self.assertEqual(json.loads((self.root / 'status/status.json').read_text())['state'], 'failed')

    def test_dirty_checkout_never_calls_deployment_or_changes_ledger(self):
        (self.source / 'source').write_text('uncommitted')
        p = self.execute('update')
        self.assertNotEqual(p.returncode, 0)
        self.assertFalse(self.events.exists())
        self.assertFalse((self.state / 'deployment-revisions.json').exists())

    def test_no_recorded_previous_cannot_silently_choose_checkout(self):
        p = self.execute('rollback')
        self.assertNotEqual(p.returncode, 0)
        self.assertFalse(self.events.exists())
        self.assertEqual(self.live.read_text(), self.a)

    def test_rejected_preflight_leaves_existing_public_health_untouched(self):
        status = self.root / 'status'
        status.mkdir()
        value = b'{"state":"healthy","message":"original"}'
        (status / 'status.json').write_bytes(value)
        (self.state / 'deployment-revisions.json').write_bytes(b'corrupt')
        (self.state / 'deployment-revisions.json').chmod(0o600)
        p = self.execute('update')
        self.assertNotEqual(p.returncode, 0)
        self.assertEqual((status / 'status.json').read_bytes(), value)
        self.assertFalse(self.events.exists())

    def test_global_lock_rejects_a_second_publish(self):
        import fcntl
        with (self.root / 'lock').open('w') as lock:
            (self.root / 'lock').chmod(0o600)
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            p = self.execute('update')
            self.assertNotEqual(p.returncode, 0)
            self.assertFalse(self.events.exists())

    def test_sigkill_after_old_installer_cold_recovery_restores_only_controllers(self):
        self.assertEqual(self.execute('update').returncode, 0)
        self.events.write_text('')
        env = {**os.environ, 'FIXTURE_ROOT': str(self.root), 'PATH': str(self.bin) + ':' + os.environ['PATH'],
            'OVERWRITE_CONTROL': '1', 'PAUSE_AFTER_CONTROL': '1'}
        with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err:
            child = subprocess.Popen(['bash', str(self.helper), 'rollback'], env=env, stdout=out, stderr=err, start_new_session=True)
            try:
                deadline = time.monotonic() + 8
                while not (self.root / 'old-installed').exists():
                    self.assertIsNone(child.poll())
                    self.assertLess(time.monotonic(), deadline)
                    time.sleep(.02)
                self.assertEqual(os.getpgid(child.pid), child.pid)
                ledger = (self.state / 'deployment-revisions.json').read_bytes()
                events = self.events.read_bytes()
                os.killpg(child.pid, signal.SIGKILL)
                self.assertEqual(child.wait(timeout=5), -signal.SIGKILL)
                for _ in range(2):
                    result = subprocess.run(['/usr/bin/python3', '-I', str(self.recovery), 'recover'], env=env, capture_output=True, text=True, timeout=5)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(self.events.read_bytes(), events)
                    self.assertEqual((self.state / 'deployment-revisions.json').read_bytes(), ledger)
                    self.assertEqual(self.live.read_text(), self.b)
                    for name, raw in self.control.items():
                        self.assertEqual((self.libexec / name).read_bytes(), raw)
                self.assertEqual(json.loads((self.root / 'status/status.json').read_text())['state'], 'failed')
                self.assertFalse((self.state / 'control-recovery.json').exists())
                self.assertTrue((self.state / 'rollback-src').exists())  # No automatic Git cleanup/replay at boot.
            finally:
                if child.poll() is None:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=5)


if __name__ == '__main__':
    unittest.main()
