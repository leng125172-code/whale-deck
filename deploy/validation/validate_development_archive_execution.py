#!/usr/bin/env python3
"""Opt-in real systemd/cgroup/journal proof using inert archive-work doubles.

Only unique self-created units and a private /run tree are used. No installed
configuration, workspace or HDD is opened. This proves execution control, not
archive storage or the still-unimplemented authorized product workflow.
"""
import contextlib
import importlib.util
import json
import os
from pathlib import Path
import select
import shutil
import subprocess
import sys
import tempfile
import time
import uuid


def load(filename):
    source = Path(__file__).resolve().parents[1] / 'host' / filename
    spec = importlib.util.spec_from_file_location(filename, source)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


files = load('whaledeck-files.py')
storage = load('whaledeck-development-storage.py')
archives = load('whaledeck-development-archives.py')
operations = load('whaledeck-development-archive-operations.py')
control = load('whaledeck-development-archive-control.py')


class InertArchiveWorkDouble:
    durable_json = archives.DevelopmentArchives.durable_json

    def __init__(self, driver, private, task_id):
        self.d, self.private, self.task_id = driver, private, task_id

    def reject(self, code):
        self.d.reject('ARCHIVE_' + code)

    def plan(self, _storage, _epoch):
        child = subprocess.Popen(['/usr/bin/sleep', '45'])
        try:
            pending = self.private / (self.task_id + '.entered.pending')
            pending.write_text(json.dumps(dict(descendantPid=child.pid))); pending.chmod(0o600)
            pending.rename(self.private / (self.task_id + '.entered'))
            while not (self.private / (self.task_id + '.release')).exists():
                time.sleep(0.025)
        finally:
            child.terminate(); child.wait(timeout=3)
        (self.private / (self.task_id + '.computed')).touch(mode=0o600)
        return dict(nativeExecutionOnly=True)


def task_engine(private, value):
    operations.BASE = private / 'journal'
    driver = storage.WorkspaceStorage(files)
    execution = control.Execution(driver)
    return operations.ArchiveOperations(driver, InertArchiveWorkDouble(driver, private, value['taskId']), execution)


def run(*args, check=True):
    return subprocess.run(args, check=check, capture_output=True, text=True, timeout=10)


def wait(predicate):
    deadline = time.monotonic() + 15
    while not predicate():
        if time.monotonic() >= deadline:
            raise AssertionError('Native archive execution observation timed out')
        time.sleep(0.025)


def observe(tasks, value):
    try:
        return tasks.observe(value)['status']
    except FileNotFoundError:
        return None
    except files.Rejected as error:
        if str(error) != 'WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY':
            raise
        return None


def request():
    return dict(schemaVersion=1, taskId=str(uuid.uuid4()), archiveId=str(uuid.uuid4()), phase='plan',
        storage=dict(schemaVersion=1, rootId='native-test-double', workspaceId=str(uuid.uuid4()),
                     environmentSha256='a' * 64, diskMiB=512),
        expectedEpoch=2, planSha256='', receiptSha256='')


def start(private, value, owned, *, unit=None, sleep=False):
    unit = unit or 'whaledeck-development-archive-run-' + uuid.uuid4().hex + '.service'
    if sleep:
        command = ('/usr/bin/sleep', '45')
    else:
        command_file = private / (value['taskId'] + '.json')
        command_file.write_bytes(storage.WorkspaceStorage.canonical(value)); command_file.chmod(0o600)
        command = (sys.executable, '-B', str(private / 'validation' / Path(__file__).name),
                   '--child', str(private), str(command_file))
    run('/usr/bin/systemd-run', '--quiet', '--collect', '--service-type=exec', '--unit=' + unit,
        '--property=Slice=system.slice', '--property=RuntimeMaxSec=45', '--property=TimeoutStopSec=3',
        '--property=NoNewPrivileges=yes', '--property=CapabilityBoundingSet=',
        '--property=MemoryMax=256M', '--property=TasksMax=16', *command)
    try:
        info = os.stat(control.CGROUP / 'system.slice' / unit)
    except FileNotFoundError:
        pass  # The short pre-cancelled unit may already have exited.
    else:
        owned.append(dict(bootId=control.Execution(storage.WorkspaceStorage(files)).boot(),
                          path='/system.slice/' + unit, device=info.st_dev, inode=info.st_ino))
    return unit


def proof(private, value):
    return json.loads((private / 'journal' / value['taskId'] / 'execution.json').read_bytes())


@contextlib.contextmanager
def isolated_execution():
    private = Path(tempfile.mkdtemp(prefix='whaledeck-archive-execution-', dir='/run'))
    owned = []
    try:
        yield private, owned
    finally:
        execution = control.Execution(storage.WorkspaceStorage(files))
        # Reverse order also handles our deliberate same-name replacement. No
        # unit-name-only stop fallback is allowed, including on failed checks.
        settled_names, unconfirmed = set(), False
        for identity in reversed(owned):
            key = (identity['bootId'], identity['path'])
            if key in settled_names:
                continue  # A newer self-owned incarnation has already settled.
            try:
                if not execution.settled(identity, stop=True):
                    raise AssertionError('Native archive execution cleanup unconfirmed')
                settled_names.add(key)
            except BaseException:
                unconfirmed = True
        if unconfirmed:
            raise AssertionError('Native archive execution cleanup unconfirmed; retained ' + str(private)) from None
        shutil.rmtree(private)


def pin_sources(private):
    (private / 'journal').mkdir(mode=0o700)
    for folder in ('host', 'validation'):
        (private / folder).mkdir(mode=0o700)
    source = Path(__file__).resolve()
    for filename in ('whaledeck-files.py', 'whaledeck-development-storage.py',
                     'whaledeck-development-archives.py', 'whaledeck-development-archive-operations.py',
                     'whaledeck-development-archive-control.py'):
        (private / 'host' / filename).write_bytes((source.parents[1] / 'host' / filename).read_bytes())
        (private / 'host' / filename).chmod(0o600)
    (private / 'validation' / source.name).write_bytes(source.read_bytes())
    (private / 'validation' / source.name).chmod(0o600)


def main():
    if os.geteuid() != 0 or sys.argv[1:] != ['--run']:
        raise SystemExit('Requires root and explicit --run for isolated native execution proof.')
    evidence = {}
    with isolated_execution() as (private, owned):
        # Empty capability sets cannot traverse a user's private home. Pin a
        # root-owned copy instead of granting DAC bypass to the inert worker.
        pin_sources(private)
        # A short launch call returns while the independent long task remains
        # alive. Cancellation must kill both its main process and descendants.
        value = request(); tasks = task_engine(private, value); unit = start(private, value, owned)
        wait(lambda: (private / (value['taskId'] + '.entered')).exists())
        assert observe(tasks, value) == 'Running'
        original = proof(private, value)
        descendant = json.loads((private / (value['taskId'] + '.entered')).read_bytes())['descendantPid']
        descendant_fd = os.pidfd_open(descendant)
        with tasks.d.absolute(control.CGROUP / original['path'][1:]) as pinned_group:
            try:
                assert tasks.cancel(value)['status'] == 'Stopped'
                assert select.select([descendant_fd], [], [], 0)[0]
            finally:
                os.close(descendant_fd)
            assert tasks.execution.settled(original)
            assert tasks.run(value)['status'] == 'Stopped'
            evidence['preciseCancelAndNoReplay'] = 'passed'
            # Reuse the exact name for a new owned inert process; the pinned
            # old inode must not confer authority over the replacement.
            wait(lambda: run('/usr/bin/systemctl', 'show', unit, '--property=MainPID', '--value').stdout.strip() == '0')
            start(private, value, owned, unit=unit, sleep=True)
            current = run('/usr/bin/systemctl', 'show', unit, '--property=MainPID', '--value').stdout.strip()
            assert int(current) > 1
            try:
                tasks.execution.settled(original, stop=True)
                raise AssertionError('Reused name accepted old cgroup proof')
            except files.Rejected as error:
                assert str(error) == 'WORKSPACE_STORAGE_ARCHIVE_TASK_EXECUTION_CHANGED'
            assert run('/usr/bin/systemctl', 'show', unit, '--property=MainPID', '--value').stdout.strip() == current
            replacement = dict(original, device=os.stat(control.CGROUP / original['path'][1:]).st_dev,
                               inode=os.stat(control.CGROUP / original['path'][1:]).st_ino)
            assert tasks.execution.settled(replacement, stop=True)
            evidence['sameNameReplacementSurvivesOldProof'] = 'passed'

        # Killing a live execution without a cancellation record is Interrupted,
        # never a completed archive and never an automatic second dispatch.
        value = request(); tasks = task_engine(private, value); start(private, value, owned)
        wait(lambda: (private / (value['taskId'] + '.entered')).exists())
        original = proof(private, value)
        assert tasks.execution.settled(original, stop=True)
        assert observe(tasks, value) == 'Interrupted'
        assert tasks.run(value)['status'] == 'Interrupted'
        evidence['unrecordedKillStaysInterrupted'] = 'passed'

        value = request(); tasks = task_engine(private, value); start(private, value, owned)
        wait(lambda: (private / (value['taskId'] + '.entered')).exists())
        (private / (value['taskId'] + '.release')).touch(mode=0o600)
        wait(lambda: observe(tasks, value) == 'Succeeded')
        assert tasks.cancel(value)['status'] == 'Succeeded'
        evidence['committedResultAfterRealExitAndLateCancel'] = 'passed'

        # An independent observer may hold the short metadata lock exactly
        # when the original unit finishes storage work. The original process
        # must persist its result after that observer returns, without a new
        # execution or any wait while source/HDD locks are held.
        value = request(); tasks = task_engine(private, value); unit = start(private, value, owned)
        wait(lambda: (private / (value['taskId'] + '.entered')).exists())
        original = proof(private, value)
        with tasks.entry(value):
            (private / (value['taskId'] + '.release')).touch(mode=0o600)
            wait(lambda: (private / (value['taskId'] + '.computed')).exists())
            time.sleep(0.05)
            assert not (private / 'journal' / value['taskId'] / 'result.json').exists()
            assert int(run('/usr/bin/systemctl', 'show', unit, '--property=MainPID', '--value').stdout.strip()) > 1
        wait(lambda: observe(tasks, value) == 'Succeeded')
        assert proof(private, value) == original and tasks.execution.settled(original)
        assert tasks.run(value)['status'] == 'Succeeded'
        evidence['resultCommitWaitsForIndependentObserverWithoutReplay'] = 'passed'

        value = request(); tasks = task_engine(private, value)
        assert tasks.cancel(value)['status'] == 'Stopped'
        unit = start(private, value, owned)
        wait(lambda: run('/usr/bin/systemctl', 'show', unit, '--property=MainPID', '--value').stdout.strip() == '0')
        assert observe(tasks, value) == 'Stopped'
        assert not (private / (value['taskId'] + '.entered')).exists()
        evidence['durablePrestartCancelRejectsDelayedUnit'] = 'passed'
        assert all(tasks.execution.settled(json.loads(path.read_bytes()))
                   for path in (private / 'journal').glob('*/execution.json'))
        evidence['originalExecutionResourcesSettled'] = 'passed'
    class ExpectedCut(Exception):
        pass
    try:
        with isolated_execution() as (private, owned):
            pin_sources(private); value = request(); tasks = task_engine(private, value)
            start(private, value, owned)
            wait(lambda: (private / (value['taskId'] + '.entered')).exists())
            original = proof(private, value)
            raise ExpectedCut()
    except ExpectedCut:
        assert not private.exists() and tasks.execution.settled(original)
    evidence['failedProbePreciselySettlesOwnExecutionBeforeRemoval'] = 'passed'
    print(json.dumps(dict(schemaVersion=1, kind='real-systemd-execution-inert-storage-double', checks=evidence), sort_keys=True))


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--child':
        private = Path(sys.argv[2]); value = json.loads(Path(sys.argv[3]).read_bytes())
        task_engine(private, value).run(value)
    else:
        main()
