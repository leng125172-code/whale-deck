#!/usr/bin/env python3
"""Fixed retirement task controller/worker; no arbitrary command or path.

Only the worker may mutate the registered NVMe namespace. HDD is always
read-only. This transport does not authorize an actor or release app budgets.
"""
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


def attest_runtime(mode, text=None):
    if text is None:
        with Path('/proc/self/status').open(encoding='ascii') as stream:
            text = stream.read(16385)
    names = {'CapEff', 'CapPrm', 'CapBnd', 'NoNewPrivs', 'Seccomp'}
    rows = [line.split(':', 1) for line in text.splitlines() if line.split(':', 1)[0] in names]
    values = {name: value.strip() for name, value in rows}
    # Normal runner-created directories may be owned by the sandbox UID and
    # mode 0700. Removing their children requires DAC_OVERRIDE, not READ_SEARCH.
    caps = (1 << 21) | (1 << 1) if mode == 'run' else 0
    if (len(text) > 16384 or len(rows) != 5 or set(values) != names or values['NoNewPrivs'] != '1'
            or values['Seccomp'] != '2' or any(not re.fullmatch('[a-fA-F0-9]{16}', values[name])
                or int(values[name], 16) != caps for name in ('CapEff', 'CapPrm', 'CapBnd'))):
        raise RuntimeError('WORKSPACE_RETIREMENT_ISOLATION_UNCONFIRMED')


def load(filename):
    path = Path(__file__).with_name(filename)
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
            or info.st_mode & 0o7022 or not 0 < info.st_size <= 256 * 1024):
        raise PermissionError()
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def worker_request(tasks, task):
    unit = 'whaledeck-development-retirement-run-' + task + '.service'
    with tasks.d.absolute(Path('/run/credentials') / unit) as credentials:
        tasks.d.directory(credentials)
        value = tasks.d.read_json(credentials, 'retirement-task', 4096)
    tasks.validate(value)
    if value['taskId'].replace('-', '') != task:
        tasks.refuse('REQUEST_INVALID')
    with tasks.entry(value, wait=True) as directory:
        tasks.require_dispatch(directory, value)
    return value


def dispatch(tasks, value, quiet=subprocess.DEVNULL):
    if tasks.admit_dispatch(value):
        try:
            result = subprocess.run([PRIVILEGED, 'development-retirement-run', value['taskId'].replace('-', '')],
                stdin=quiet, stdout=quiet, stderr=quiet, timeout=6, check=False)
            if result.returncode != 0:
                tasks.refuse('DISPATCH_UNCONFIRMED')
        except (OSError, subprocess.TimeoutExpired):
            tasks.refuse('DISPATCH_UNCONFIRMED')
    try:
        return tasks.observe(value)
    except tasks.d.files.Rejected as error:
        if str(error) != 'WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY':
            raise
        return tasks.receipt(value, 'Pending')


def confine(tasks, value, mode, path_guard):
    d = tasks.d
    with contextlib.ExitStack() as scope:
        writable = []
        if mode != 'observe':
            root = scope.enter_context(d.absolute(tasks.base()))
            d.directory(root, private=True); writable.append(root)
        if mode == 'run' and value['phase'] == 'execute':
            # Root comes only from the validated fixed storage registry.
            # Keep its lock through rule installation; no stdin path becomes
            # a write grant. The engine subsequently re-attests and re-locks.
            root_id, _ = d.validate_request(value['prerequisite']['storage'])
            parent, _ = scope.enter_context(d.locked_root(root_id, exclusive=True))
            writable.append(parent)
        path_guard.install(writable)


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
        files, storage, quota, quota_guard, path_guard, archives, shared, operations, control, tree, codec, plans, execution = (load(name) for name in (
            'whaledeck-files.py', 'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py',
            'whaledeck-development-quota-guard.py', 'whaledeck-development-archive-guard.py',
            'whaledeck-development-archives.py', 'whaledeck-development-archive-operations.py',
            'whaledeck-development-retirement-operations.py', 'whaledeck-development-archive-control.py',
            'whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-development-retirement.py',
            'whaledeck-development-retirement-execution.py'))
        driver = storage.WorkspaceStorage(files, sandboxed=True, quota=quota)
        engine = archives.DevelopmentArchives(driver, tree.FileTreeManifest, codec.TreeArchive,
            archives.HddArchiveStorage(driver, read_only=True))
        tasks = operations.create(shared, driver, plans.DevelopmentRetirementPlans(engine),
            execution.DevelopmentRetirementExecution(engine, archives.PayloadFiles), control.Execution(driver, kind='retirement'))
        common = load('whaledeck-development-archive-transport.py')
        value = worker_request(tasks, args[1]) if mode == 'run' else common.read_request(sys.stdin.buffer, tasks)
        if mode == 'start':
            null_fd = os.open('/dev/null', os.O_RDWR | os.O_CLOEXEC)
        if mode == 'cancel':
            result = common.cancel(tasks, value, path_guard, quota_guard, quota, operations, control)
        else:
            confine(tasks, value, mode, path_guard)
            if mode == 'start':
                result = dispatch(tasks, value, null_fd)
                quota_guard.install(quota, read_only=True)
            else:
                release = mode == 'run' and value['phase'] == 'execute'
                quota_guard.install(quota, read_only=not release, release_only=release, archive_flags=mode == 'run')
                result = {'observe': tasks.observe, 'run': tasks.run}[mode](value)
        print(json.dumps(result, separators=(',', ':')))
        return 0
    except Exception as error:
        code = str(error)
        if not re.fullmatch('WORKSPACE_(?:STORAGE|ARCHIVE|RETIREMENT)_[A-Z_]{1,80}', code):
            code = 'WORKSPACE_RETIREMENT_HELPER_UNCONFIRMED'
        print(code, file=sys.stderr)
        return 2
    finally:
        if null_fd is not None:
            os.close(null_fd)


if __name__ == '__main__':
    raise SystemExit(main())
