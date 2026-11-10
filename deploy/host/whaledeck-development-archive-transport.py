#!/usr/bin/env python3
"""Fixed archive task controller/worker. No payload deletion or quota writes."""
import contextlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

PRIVILEGED = '/usr/local/libexec/whaledeck-privileged'
HDD = Path('/data/DevSpace')


def attest_runtime(mode, text=None):
    if text is None:
        with Path('/proc/self/status').open(encoding='ascii') as stream:
            text = stream.read(16385)
    names = {'CapEff', 'CapPrm', 'CapBnd', 'NoNewPrivs', 'Seccomp'}
    rows = [line.split(':', 1) for line in text.splitlines() if line.split(':', 1)[0] in names]
    values = {name: value.strip() for name, value in rows}
    caps = (1 << 21) | (1 << 2) if mode == 'run' else 0  # SYS_ADMIN + DAC_READ_SEARCH only.
    if (len(text) > 16384 or len(rows) != 5 or set(values) != names or values['NoNewPrivs'] != '1'
            or values['Seccomp'] != '2' or any(not re.fullmatch('[a-fA-F0-9]{16}', values[name])
                or int(values[name], 16) != caps for name in ('CapEff', 'CapPrm', 'CapBnd'))):
        raise RuntimeError('WORKSPACE_ARCHIVE_ISOLATION_UNCONFIRMED')


def load(filename):
    path = Path(__file__).with_name(filename)
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
            or info.st_mode & 0o7022 or not 0 < info.st_size <= 256 * 1024):
        raise PermissionError()
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def read_request(source, tasks):
    raw = source.read(4097)
    if not 0 < len(raw) <= 4096:
        tasks.refuse('REQUEST_INVALID')
    try:
        value = json.loads(raw, object_pairs_hook=tasks.d.files.unique_object)
    except (ValueError, UnicodeError, RecursionError):
        tasks.refuse('REQUEST_INVALID')
    tasks.validate(value)
    return value


def worker_request(tasks, task):
    unit = 'whaledeck-development-archive-run-' + task + '.service'
    with tasks.d.absolute(Path('/run/credentials') / unit) as credentials:
        tasks.d.directory(credentials)
        value = tasks.d.read_json(credentials, 'archive-task', 4096)
    tasks.validate(value)
    if value['taskId'].replace('-', '') != task:
        tasks.refuse('REQUEST_INVALID')
    with tasks.entry(value) as directory:
        tasks.require_dispatch(directory, value)
    return value


def dispatch(tasks, value, quiet=subprocess.DEVNULL):
    if tasks.admit_dispatch(value):
        try:
            # The immutable fence above survives process/RPC loss. Do not
            # remove it or repeat submission after any ambiguous manager reply.
            result = subprocess.run([PRIVILEGED, 'development-archive-run', value['taskId'].replace('-', '')],
                stdin=quiet, stdout=quiet, stderr=quiet, timeout=6, check=False)
            if result.returncode != 0:
                tasks.refuse('DISPATCH_UNCONFIRMED')
        except (OSError, subprocess.TimeoutExpired):
            tasks.refuse('DISPATCH_UNCONFIRMED')
    try:
        return tasks.observe(value)
    except tasks.d.files.Rejected as error:
        if str(error) != 'WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY':
            raise
        return tasks.receipt(value, 'Pending')


def confine(tasks, value, mode, path_guard, operations, control, directory=None):
    d = tasks.d
    with contextlib.ExitStack() as scope:
        writable = []
        if mode != 'observe':
            root = scope.enter_context(d.absolute(operations.BASE))
            d.directory(root, private=True); writable.append(root)
        if mode == 'run':
            hdd = scope.enter_context(d.absolute(HDD))
            d.directory(hdd, private=True); writable.append(hdd)
        elif mode == 'cancel':
            # A pre-start cancellation needs only the journal. Existing
            # execution authority is narrowed to its exact original cgroup.
            if directory is None:
                raise ValueError('Cancellation requires the original task lock')
            proof = tasks.proof(directory)
            if proof is not None and proof['bootId'] == tasks.execution.boot():
                try:
                    group = scope.enter_context(d.absolute(control.CGROUP / proof['path'][1:]))
                except FileNotFoundError:
                    pass
                else:
                    info = os.fstat(group)
                    if (info.st_dev, info.st_ino) != (proof['device'], proof['inode']):
                        tasks.refuse('EXECUTION_CHANGED')
                    writable.append(group)
        path_guard.install(writable)


def cancel(tasks, value, path_guard, quota_guard, quota, operations, control):
    # Keep admission, original proof selection, confinement and the cancellation
    # marker in one task lock. An execution cannot enter between observing
    # a missing proof and installing a journal-only write policy.
    with tasks.entry(value, create=True) as directory:
        confine(tasks, value, 'cancel', path_guard, operations, control, directory)
        quota_guard.install(quota, read_only=True)
        return tasks.state(directory, value, stop=True)


def main():
    null_fd = None
    try:
        args = sys.argv[1:]
        if os.geteuid() != 0 or not args or args[0] not in ('start', 'observe', 'cancel', 'run'):
            raise PermissionError()
        mode = args[0]
        if ((mode != 'run' and len(args) != 1) or (mode == 'run' and (len(args) != 2
                or not re.fullmatch('[a-f0-9]{32}', args[1]) or args[1] == '0' * 32))):
            raise PermissionError()
        attest_runtime(mode)
        files, storage, quota, quota_guard, path_guard, archives, operations, control, tree, codec = (load(name) for name in (
            'whaledeck-files.py', 'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py',
            'whaledeck-development-quota-guard.py', 'whaledeck-development-archive-guard.py',
            'whaledeck-development-archives.py', 'whaledeck-development-archive-operations.py',
            'whaledeck-development-archive-control.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py'))
        driver = storage.WorkspaceStorage(files, sandboxed=True, quota=quota)
        engine = archives.DevelopmentArchives(driver, tree.FileTreeManifest, codec.TreeArchive, archives.HddArchiveStorage(driver))
        tasks = operations.ArchiveOperations(driver, engine, control.Execution(driver))
        value = worker_request(tasks, args[1]) if mode == 'run' else read_request(sys.stdin.buffer, tasks)
        if mode == 'start':
            # Landlock also denies new writable opens of /dev/null. Pin this
            # fixed sink before confinement; no source or caller path is opened.
            null_fd = os.open('/dev/null', os.O_RDWR | os.O_CLOEXEC)
        if mode == 'cancel':
            result = cancel(tasks, value, path_guard, quota_guard, quota, operations, control)
        else:
            confine(tasks, value, mode, path_guard, operations, control)
            if mode == 'start':
                result = dispatch(tasks, value, null_fd)
                quota_guard.install(quota, read_only=True)
            else:
                quota_guard.install(quota, read_only=True, archive_flags=mode == 'run')
                result = {'observe': tasks.observe, 'run': tasks.run}[mode](value)
        print(json.dumps(result, separators=(',', ':')))
        return 0
    except Exception as error:
        code = str(error)
        if not re.fullmatch('WORKSPACE_(?:STORAGE|ARCHIVE)_[A-Z_]{1,80}', code):
            code = 'WORKSPACE_ARCHIVE_HELPER_UNCONFIRMED'
        print(code, file=sys.stderr)
        return 2
    finally:
        if null_fd is not None:
            os.close(null_fd)


if __name__ == '__main__':
    raise SystemExit(main())
