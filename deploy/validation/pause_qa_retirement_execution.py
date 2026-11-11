#!/usr/bin/env python3
"""Bounded freeze of one new QA execution; only product Cancel may stop it.

No SQL/source/quota/journal writes and no direct kill. Boot, complete request,
task path and pinned cgroup inode must match. The task flock is released before
the UI can request cancellation. EOF, exceptions and the 20s alarm thaw only
the original pinned group, never a replacement with the same unit name.
"""
import contextlib
import errno
import hashlib
import json
import os
from pathlib import Path
import signal
import sys
import time

from interrupt_qa_retirement_execution import check_scope, checkpoint, identifier, installed_tasks, settled_snapshot


def events(tasks, group):
    with tasks.d.opened(group, 'cgroup.events', os.O_RDONLY) as fd:
        raw = os.read(fd, 513)
    assert 0 < len(raw) <= 512
    rows = [line.split() for line in raw.splitlines() if line.startswith((b'populated ', b'frozen '))]
    assert len(rows) == 2 and {row[0] for row in rows} == {b'populated', b'frozen'}
    assert all(len(row) == 2 and row[1] in (b'0', b'1') for row in rows)
    return {key.decode(): int(value) for key, value in rows}


def write_flag(group, value):
    assert value in (b'0', b'1')
    fd = os.open('cgroup.freeze', os.O_WRONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=group)
    try:
        assert os.write(fd, value) == 1
    finally:
        os.close(fd)


@contextlib.contextmanager
def paused_live(tasks, value, save):
    with contextlib.ExitStack() as pins:
        proof = None
        freeze_requested = False
        try:
            with tasks.entry(value) as directory:
                tasks.require_dispatch(directory, value)
                assert tasks.completed(directory, value) is None and not tasks.cancelled(directory)
                proof = tasks.proof(directory)
                if proof is None:
                    yield None
                    return
                tasks.execution.validate(proof)
                assert proof['path'] == '/system.slice/whaledeck-development-retirement-run-'+value['taskId'].replace('-', '')+'.service'
                assert proof['bootId'] == tasks.execution.boot() and not tasks.execution.settled(proof)
                root = pins.enter_context(tasks.d.absolute(Path('/sys/fs/cgroup')))
                group = pins.enter_context(tasks.d.opened(root, proof['path'][1:], os.O_RDONLY | os.O_DIRECTORY))
                info = os.fstat(group)
                assert (info.st_dev, info.st_ino) == (proof['device'], proof['inode'])
                assert events(tasks, group) == dict(populated=1, frozen=0)
                before = dict(taskId=value['taskId'], execution=proof,
                    requestSha256=hashlib.sha256(tasks.d.canonical(value)).hexdigest(),
                    liveExecution=True, resultAbsent=True, cancellationAbsent=True)
                save('before-freeze', before)
                freeze_requested = True  # A write with an uncertain reply still requires thaw.
                write_flag(group, b'1')
                deadline = time.monotonic() + 2
                while events(tasks, group) != dict(populated=1, frozen=1):
                    assert time.monotonic() < deadline, 'QA_EXECUTION_FREEZE_UNCONFIRMED'
                    time.sleep(0.01)
                assert tasks.completed(directory, value) is None and not tasks.cancelled(directory)
                save('frozen', dict(before, frozen=True, taskFlockReleasedBeforeUiCancel=True))
            # Product Observe/Cancel needs this task flock; do not hold it while
            # awaiting the genuine UI request or the signed helper's response.
            yield before
        finally:
            if freeze_requested:
                try:
                    write_flag(group, b'0')
                except OSError as error:
                    if error.errno not in (errno.ENOENT, errno.ENODEV) or not tasks.execution.empty(root, group, proof):
                        raise
                else:
                    deadline = time.monotonic() + 2
                    while True:
                        try:
                            current = events(tasks, group)
                        except (OSError, AssertionError):
                            assert tasks.execution.empty(root, group, proof)
                            break
                        if current['frozen'] == 0:
                            break
                        assert time.monotonic() < deadline, 'QA_EXECUTION_THAW_UNCONFIRMED'
                        time.sleep(0.01)
                save('thawed', dict(passed=True, execution=proof, onlyPinnedGroupTouched=True))


def require_stopped(tasks, value):
    with tasks.entry(value) as directory:
        receipt = tasks.state(directory, value)
        if receipt['status'] != 'Stopped':
            return None
        proof = tasks.proof(directory)
        assert proof is not None and tasks.execution.settled(proof)
        assert tasks.cancelled(directory) and tasks.completed(directory, value) is None
        return dict(stopped=True, receipt=receipt, exactExecutionSettled=True,
            cancellationPresent=True, resultAbsent=True, execution=proof)


def expired(signum, frame):
    raise TimeoutError('QA_EXECUTION_FREEZE_DEADLINE')


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 1
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_STOP_PROBE') == '1'
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE') != '1'
    raw = sys.stdin.readline(8193); assert len(raw) <= 8192 and raw.endswith('\n')
    payload = json.loads(raw)
    assert set(payload) == {'evidence', 'workspaceId', 'ownerSubject', 'retirementId', 'attempt'}
    evidence = Path(payload['evidence']).resolve(strict=True)
    assert evidence.is_relative_to(Path(__file__).resolve().parents[2]/'deploy/artifacts') and evidence.is_dir()
    attempt = payload['attempt']; assert type(attempt) is int and 1 <= attempt <= 4
    state = json.loads((evidence/'state.json').read_bytes()); declaration = json.loads((evidence/'declaration.json').read_bytes())
    check_scope(payload, state, declaration, checkpoint(payload['retirementId']))
    tasks = installed_tasks(); os.umask(0o077)
    def save(suffix, value):
        target = evidence/f'stop-{attempt}-{suffix}.json'; pending = target.with_suffix('.pending')
        with pending.open('x') as stream:
            json.dump(value, stream, indent=2); stream.flush(); os.fsync(stream.fileno())
        os.link(pending, target); pending.unlink()
    save('armed', dict(workspaceId=payload['workspaceId'], retirementId=payload['retirementId'],
        productCancelOnly=True, freezeDeadlineSeconds=20))
    print(json.dumps(dict(armed=True)), flush=True)
    raw = sys.stdin.readline(128); assert len(raw) < 128 and raw.endswith('\n')
    message = json.loads(raw); assert set(message) == {'taskId'}
    task = identifier(message['taskId']); row = checkpoint(payload['retirementId'])
    check_scope(payload, state, declaration, row, task=task)
    value = json.loads(row['request']); tasks.validate(value)
    save('binding', {k: v for k, v in row.items() if k != 'request'})
    signal.signal(signal.SIGALRM, expired)
    signal.signal(signal.SIGTERM, expired)
    deadline = time.monotonic() + 30
    result = None
    while time.monotonic() < deadline and result is None:
        try:
            signal.setitimer(signal.ITIMER_REAL, 20)
            with paused_live(tasks, value, save) as before:
                if before is not None:
                    print(json.dumps(dict(frozen=True, before=before)), flush=True)
                    assert sys.stdin.readline(32) == 'verify\n'
                    while result is None:
                        try:
                            result = require_stopped(tasks, value)
                        except tasks.d.files.Rejected as error:
                            if str(error) != 'WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY':
                                raise
                        if result is None:
                            time.sleep(0.05)
                    save('stopped', result)
        except FileNotFoundError:
            pass
        except tasks.d.files.Rejected as error:
            if str(error) != 'WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY':
                raise
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
        if result is None:
            time.sleep(0.01)
    assert result is not None, 'QA_LIVE_EXECUTION_NOT_OBSERVED'
    deadline = time.monotonic() + 60
    while True:
        original = settled_snapshot(task, 'Stopped')
        if original:
            assert len(original) == 1 and original[0]['proofSha256']
            result['original'] = original; save('settled', result)
            print(json.dumps(result), flush=True)
            return
        assert time.monotonic() < deadline, 'QA_STOPPED_STEP_NOT_SETTLED'
        time.sleep(0.5)


if __name__ == '__main__':
    main()
