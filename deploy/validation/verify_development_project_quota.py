#!/usr/bin/env python3
"""Positive and negative quota checks for two disposable mounted guest disks."""

import errno
import importlib.util
import os
from pathlib import Path
import sys


path = Path(__file__).resolve().parents[1] / 'host' / 'whaledeck-development-project-quota.py'
spec = importlib.util.spec_from_file_location('workspace_project_quota', path)
quota = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quota)
PROJECT = 1400000061


def check_writer(directory, name, length, error=None):
    pid = os.fork()
    if pid == 0:
        try:
            os.setgroups([])
            os.setgid(65534)
            os.setuid(65534)
            fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=directory)
            try:
                os.posix_fallocate(fd, 0, length)
            except OSError as failure:
                if failure.errno == error:
                    os._exit(0)
                print(f'Unexpected writer errno {failure.errno}', flush=True)
            else:
                if error is None:
                    os.fsync(fd)
                    os._exit(0)
        except OSError as failure:
            print(f'Unexpected writer setup errno {failure.errno}', flush=True)
        os._exit(1)
    _, status = os.waitpid(pid, 0)
    assert os.waitstatus_to_exitcode(status) == 0, 'Unprivileged writer did not get the required result'


def check(filesystem):
    mount = Path('/qa') / filesystem
    if not mount.is_mount() or mount.resolve() != mount or mount.stat().st_uid != 0:
        raise RuntimeError('Expected a root-owned disposable mount')
    if not {child.name for child in mount.iterdir()} <= {'lost+found'}:
        raise RuntimeError('Expected a blank disposable filesystem')
    project = PROJECT
    mib = 1024 * 1024
    expected_error = errno.EDQUOT if filesystem == 'ext4' else errno.ENOSPC
    payload = mount / 'quota-payload'
    payload.mkdir(mode=0o700)
    fd = os.open(payload, os.O_RDONLY | os.O_DIRECTORY)
    try:
        quota.require_unused(fd, project)
        quota.attach(fd, project, 256)
        quota.attach(fd, project, 256)
        quota.verify(fd, project, 256)
        work = payload / 'work'
        work.mkdir(mode=0o700)
        os.chown(work, 65534, 65534)
        work_fd = os.open(work, os.O_RDONLY | os.O_DIRECTORY)
        try:
            assert quota.read_attributes(work_fd)[3] == project
            check_writer(work_fd, 'below-budget', 1 * mib)
            check_writer(work_fd, 'over-budget', 260 * mib, expected_error)
        finally:
            os.close(work_fd)
        quota.verify(fd, project, 256)
        hard_blocks, _, used, hard_inodes, *_ = quota.read_quota(fd, project)
        assert hard_blocks == 262144 and hard_inodes == 65536 and mib <= used <= 256 * mib, (hard_blocks, hard_inodes, used)
        # XFS uses ENOSPC for project exhaustion. Check the same allocation
        # outside the project to distinguish it from physical exhaustion.
        control = mount / 'unlimited-control'
        control.mkdir(mode=0o700)
        os.chown(control, 65534, 65534)
        control_fd = os.open(control, os.O_RDONLY | os.O_DIRECTORY)
        try:
            check_writer(control_fd, 'same-size', 260 * mib)
        finally:
            os.close(control_fd)
        (control / 'same-size').unlink()
        control.rmdir()
        print(f'QUOTA_NATIVE_OK fs={filesystem} hardMiB=256 hardInodes=65536 usedBytes={used} '
              f'childProject=true overBudget={errno.errorcode[expected_error]} control=success', flush=True)
    finally:
        os.close(fd)


def check_remounted(filesystem):
    mount = Path('/qa') / filesystem
    if not mount.is_mount() or mount.resolve() != mount:
        raise RuntimeError('Expected a remounted disposable filesystem')
    fd = os.open(mount / 'quota-payload', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        quota.verify(fd, PROJECT, 256)
        assert quota.read_quota(fd, PROJECT)[2] >= 1024 * 1024
    finally:
        os.close(fd)
    print(f'QUOTA_REMOUNT_OK fs={filesystem}', flush=True)


def check_accounting_only():
    mount = Path('/qa/xfs')
    if not mount.is_mount() or mount.resolve() != mount or list(mount.iterdir()):
        raise RuntimeError('Expected a blank disposable XFS mount')
    fd = os.open(mount, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        try:
            quota.require_enforcing(fd)
        except quota.QuotaError as error:
            assert str(error) == 'QUOTA_NOT_ENFORCING'
        else:
            raise AssertionError('Accounting without enforcement was accepted')
    finally:
        os.close(fd)
    print('QUOTA_ACCOUNTING_ONLY_REJECTED fs=xfs', flush=True)


if __name__ == '__main__':
    if os.geteuid() != 0 or os.environ.get('WHALEDECK_QA_DISPOSABLE_PROJECT_QUOTA') != '1' or not __debug__:
        raise SystemExit('Requires an isolated disposable guest running with assertions enabled')
    if sys.argv[1:] not in ([], ['--verify-remounted'], ['--accounting-only']):
        raise SystemExit('Unknown verification mode')
    if sys.argv[1:] == ['--accounting-only']:
        check_accounting_only()
    else:
        verify = check_remounted if sys.argv[1:] else check
        verify('ext4')
        verify('xfs')
