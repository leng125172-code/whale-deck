#!/usr/bin/env python3
"""Opt-in native loop/ext4/NVMe proof using only a new disposable 1 GiB image.

Never formats a block device or writes production storage configuration. The
temporary mount and image are precisely removed; bounded evidence is retained.
"""
import errno
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

MIB = 1024**2
PROJECT = 1400000061


def load(directory, name):
    path = directory / ('whaledeck-' + name + '.py')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(*args):
    result = subprocess.run(args, capture_output=True, text=True, timeout=90)
    if result.returncode:
        raise RuntimeError('NATIVE_IMAGE_COMMAND_FAILED: ' + args[0])
    return result.stdout.strip()


def writer(directory, name, size, expected=None):
    pid = os.fork()
    if pid == 0:
        try:
            os.setgroups([])
            os.setgid(65534)
            os.setuid(65534)
            fd = os.open(name, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600, dir_fd=directory)
            try:
                os.posix_fallocate(fd, 0, size)
            except OSError as error:
                os._exit(0 if error.errno == expected else 2)
            os.fsync(fd)
            os._exit(0 if expected is None else 3)
        except BaseException:
            os._exit(4)
    _, status = os.waitpid(pid, 0)
    assert os.waitstatus_to_exitcode(status) == 0, 'NATIVE_UNPRIVILEGED_WRITER_FAILED'


def probe():
    home = Path(__file__).resolve().parent
    declaration = json.loads((home / 'declaration.json').read_text())
    files = load(home, 'files')
    storage = load(home, 'development-storage')
    quota = load(home, 'development-project-quota')
    guard = load(home, 'development-quota-guard')
    guard.install(quota, read_only=True)
    guard.restrict_path_writes()
    engine = storage.WorkspaceStorage(files, quota=quota)
    with engine.absolute(declaration['mountPoint']) as mounted:
        engine.attest_mount(mounted, declaration)
    with engine.absolute(Path(declaration['mountPoint']) / 'payload') as payload:
        quota.verify(payload, PROJECT, 256)
        try:
            quota.quota_call(payload, quota.Q_SETQUOTA, PROJECT, bytearray(quota.QUOTA.size))
        except OSError as error:
            assert error.errno == errno.EPERM
        else:
            raise AssertionError('READONLY_QUOTA_FILTER_NOT_ENFORCED')
        try:
            os.open('forbidden-write', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=payload)
        except OSError as error:
            assert error.errno in (errno.EPERM, errno.EACCES, errno.EROFS)
        else:
            raise AssertionError('READONLY_PATH_FILTER_NOT_ENFORCED')
    print(json.dumps(dict(passed=True, privateDevices=True, physicalNvmeAttested=True,
        readonlyQuotaVerified=True, quotaMutationDenied=True, pathMutationDenied=True)))


def main():
    assert os.geteuid() == 0 and __debug__, 'Requires explicit root invocation with assertions enabled'
    repo = Path(__file__).resolve().parents[2]
    sources = repo / 'deploy/host'
    files = load(sources, 'files')
    storage = load(sources, 'development-storage')
    quota = load(sources, 'development-project-quota')
    image_root = storage.IMAGE_ROOT
    image_root.mkdir(mode=0o700, exist_ok=True)
    with storage.WorkspaceStorage(files).absolute(image_root) as fd:
        storage.WorkspaceStorage(files).directory(fd, private=True)
    identifier = uuid.uuid4().hex
    home = image_root / ('qa-' + identifier)
    home.mkdir(mode=0o700)
    image = image_root / ('qa-' + identifier + '.ext4')
    mount = home / 'mount'
    mount.mkdir(mode=0o700)
    evidence = repo / 'deploy/artifacts' / ('development-disk-image-' + identifier)
    evidence.mkdir(mode=0o700)
    attached = False
    image_identity = None
    accepted = None
    try:
        for name in ('files', 'development-storage', 'development-project-quota', 'development-quota-guard'):
            target = home / ('whaledeck-' + name + '.py')
            shutil.copyfile(sources / target.name, target)
            target.chmod(0o600)
        shutil.copyfile(__file__, home / 'probe.py')
        (home / 'probe.py').chmod(0o600)
        fd = os.open(image, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        try:
            os.posix_fallocate(fd, 0, 1024**3)
            os.fsync(fd)
            image_identity = os.fstat(fd)
        finally:
            os.close(fd)
        run('mkfs.ext4', '-q', '-F', '-m', '0', '-O', 'project,quota', '-E',
            'quotatype=prjquota,nodiscard,lazy_itable_init=0,lazy_journal_init=0', str(image))
        run('mount', '-t', 'ext4', '-o', 'loop,prjquota,nosuid,nodev', str(image), str(mount))
        attached = True
        run('udevadm', 'settle', '--timeout=20')
        engine = storage.WorkspaceStorage(files, quota=quota)
        inner = run('blkid', '-p', '-s', 'UUID', '-o', 'value', str(image))
        backing_device = f'{os.major(image_identity.st_dev)}:{os.minor(image_identity.st_dev)}'
        props = run('udevadm', 'info', '--query=property', '--path=/sys/dev/block/' + backing_device).splitlines()
        outer = [v.removeprefix('ID_FS_UUID=') for v in props if v.startswith('ID_FS_UUID=')]
        assert len(outer) == 1
        declaration = dict(mountPoint=str(mount), filesystemUuid=inner, reserveMiB=512,
            diskImage=dict(path=str(image), device=backing_device, inode=image_identity.st_ino,
                sizeBytes=1024**3, filesystemUuid=outer[0]))
        (home / 'declaration.json').write_text(json.dumps(declaration))
        (home / 'declaration.json').chmod(0o600)
        with engine.absolute(mount) as mounted:
            engine.attest_mount(mounted, declaration)
            quota.require_enforcing(mounted)
        payload = mount / 'payload'
        payload.mkdir(mode=0o700)
        fd = os.open(payload, os.O_RDONLY | os.O_DIRECTORY)
        try:
            quota.require_unused(fd, PROJECT)
            quota.attach(fd, PROJECT, 256)
            work = payload / 'work'
            work.mkdir(mode=0o700)
            os.chown(work, 65534, 65534)
            work_fd = os.open(work, os.O_RDONLY | os.O_DIRECTORY)
            try:
                writer(work_fd, 'small', MIB)
                writer(work_fd, 'over-project-limit', 260 * MIB, errno.EDQUOT)
            finally:
                os.close(work_fd)
            quota.verify(fd, PROJECT, 256)
        finally:
            os.close(fd)
        control = mount / 'control'
        control.mkdir(mode=0o700)
        os.chown(control, 65534, 65534)
        fd = os.open(control, os.O_RDONLY | os.O_DIRECTORY)
        try:
            writer(fd, 'same-size-outside-project', 260 * MIB)
            (control / 'same-size-outside-project').unlink()
            writer(fd, 'over-filesystem-limit', 2 * 1024**3, errno.ENOSPC)
            (control / 'over-filesystem-limit').unlink()
        finally:
            os.close(fd)
        run('umount', str(mount))
        attached = False
        run('mount', '-t', 'ext4', '-o', 'loop,prjquota,nosuid,nodev', str(image), str(mount))
        attached = True
        run('udevadm', 'settle', '--timeout=20')
        result = run('systemd-run', '--quiet', '--wait', '--pipe', '--collect',
            '--unit=whaledeck-qa-disk-image-' + identifier, '--property=Type=exec',
            '--property=PrivateDevices=yes', '--property=NoNewPrivileges=yes',
            '--property=ProtectSystem=strict', '--property=ProtectHome=yes', '--property=PrivateTmp=yes',
            '--property=PrivateNetwork=yes', '--property=RestrictNamespaces=yes',
            '--property=CapabilityBoundingSet=CAP_SYS_ADMIN', '--property=RestrictAddressFamilies=AF_UNIX',
            '--property=SystemCallFilter=@system-service prctl quotactl_fd',
            '--property=ReadWritePaths=' + str(mount), '--property=RuntimeMaxSec=30',
            '/usr/bin/python3', '-B', '-I', str(home / 'probe.py'), '--probe')
        value = json.loads(result)
        assert value['passed']
        value.update(projectHardMiB=256, projectExceeded='EDQUOT', filesystemExceeded='ENOSPC',
            physicalImageBytes=image.stat().st_size, imageFullyReserved=image.stat().st_blocks * 512 >= 1024**3,
            imageInodeUnchanged=image.stat().st_ino == image_identity.st_ino, quotaSurvivedRemount=True,
            productionConfigurationWrites=0)
        assert value['imageFullyReserved'] and value['imageInodeUnchanged']
        accepted = value
        for p in home.iterdir():
            if p.is_file():
                shutil.copyfile(p, evidence / p.name)
    except BaseException as error:
        (evidence / 'failed.json').write_text(json.dumps(dict(passed=False, failureType=type(error).__name__)))
        print(json.dumps(dict(passed=False, evidence=str(evidence))), file=sys.stderr)
        raise
    finally:
        if attached:
            run('umount', str(mount))
        if image_identity is not None:
            assert os.path.samestat(image_identity, image.stat())
            assert not run('losetup', '--associated', str(image), '--noheadings', '--output', 'NAME')
            image.unlink()
        shutil.rmtree(home)
        if accepted is not None:
            accepted['temporaryMountAndImageRemoved'] = True
            (evidence / 'accepted.json').write_text(json.dumps(accepted, indent=2))
        for p in evidence.iterdir():
            p.chmod(0o600)
        listing = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                          for p in sorted(evidence.iterdir()) if p.is_file() and p.name != 'SHA256SUMS')
        (evidence / 'SHA256SUMS').write_text(listing)
        (evidence / 'SHA256SUMS').chmod(0o600)
    print(json.dumps(dict(passed=True, evidence=str(evidence), projectExceeded='EDQUOT', filesystemExceeded='ENOSPC')))


if __name__ == '__main__':
    if sys.argv[1:] == ['--probe']:
        probe()
    elif sys.argv[1:] == []:
        main()
    else:
        raise SystemExit('Unknown verification mode')
