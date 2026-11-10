#!/usr/bin/env python3
"""Opt-in real provisioning/recovery proof on new, isolated 2 GB NVMe/HDD images.

Production directories/configuration are never passed to the provisioner. Only
the generated QA mount units, images and private directories are cleaned up.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
from types import SimpleNamespace
import uuid
from unittest.mock import patch


def main():
    assert os.geteuid() == 0 and __debug__
    os.umask(0o077)
    repo = Path(__file__).resolve().parents[2]
    script = repo / 'deploy/host/whaledeck-development-provision.py'
    spec = importlib.util.spec_from_file_location('provision', script)
    p = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(p)
    identifier = uuid.uuid4().hex
    home = Path('/var/lib/whaledeck-development-disks') / ('qa-provision-' + identifier)
    home.mkdir(mode=0o700)
    archive_home = Path('/data') / ('whaledeck-qa-provision-' + identifier)
    archive_home.mkdir(mode=0o700)
    evidence = repo / 'deploy/artifacts' / ('development-storage-provision-' + identifier)
    evidence.mkdir(mode=0o700)
    runtime, archive = home / 'runtime', archive_home / 'target'
    runtime.mkdir(mode=0o700)
    os.chown(runtime, 65534, 65534)
    runtime.chmod(0o775)
    archive.mkdir(mode=0o700)
    p.IMAGE_ROOT, p.BASE = home / 'images', home / 'base'
    p.CONFIG, p.LAYOUT = home / 'storage.json', p.IMAGE_ROOT / 'layout.json'
    p.files, p.storage, p.quota = p.load('files'), p.load('development-storage'), p.load('development-project-quota')
    p.storage.IMAGE_ROOT, p.storage.BASE, p.storage.CONFIG = p.IMAGE_ROOT, p.BASE, p.CONFIG
    p.driver = p.storage.WorkspaceStorage(p.files, quota=p.quota)
    arguments = SimpleNamespace(runtime=runtime, archive=archive, runtime_gb=2, archive_gb=2)
    raw_command = p.command
    formats = home / 'formats.jsonl'
    def audited(*args, **kwargs):
        if args[0] == 'mkfs.ext4':
            with formats.open('a') as stream:
                stream.write(json.dumps({'image': args[-1]}) + '\n')
        return raw_command(*args, **kwargs)
    p.command = audited
    record, units, images = None, [], []
    accepted = None
    try:
        # A nonempty target must be refused before creating any image/state.
        sentinel = runtime / 'keep'
        sentinel.write_bytes(b'must remain')
        try:
            p.plan(arguments)
        except RuntimeError as error:
            assert str(error) == 'TARGET_NOT_EMPTY'
        else:
            raise AssertionError('NONEMPTY_TARGET_ACCEPTED')
        assert sentinel.read_bytes() == b'must remain' and not p.LAYOUT.exists()
        sentinel.unlink()
        correct_root = p.IMAGE_ROOT
        p.IMAGE_ROOT = archive_home / 'wrong-device-images'
        try:
            p.plan(arguments)
        except RuntimeError as error:
            assert str(error) == 'IMAGE_DEVICE_CHANGED'
        else:
            raise AssertionError('WRONG_PHYSICAL_IMAGE_DEVICE_ACCEPTED')
        finally:
            p.IMAGE_ROOT = correct_root
        record = p.plan(arguments)
        p.own_directory(p.IMAGE_ROOT)
        p.save(record)
        # Death immediately after O_EXCL leaves only our declared empty file.
        # Resume can recover its inode; an unknown nonempty file cannot enter
        # this path. No existing filesystem has been formatted at this point.
        pid = os.fork()
        if pid == 0:
            native_open = os.open
            def cut_after_create(path, flags, *args, **kwargs):
                fd = native_open(path, flags, *args, **kwargs)
                if Path(path) == Path(record['plan']['runtime']['image']) and flags & os.O_EXCL:
                    os._exit(70)
                return fd
            p.os.open = cut_after_create
            try:
                p.image_ready(record, 'runtime')
                os._exit(76)
            except BaseException:
                os._exit(77)
        _, status = os.waitpid(pid, 0)
        assert os.waitstatus_to_exitcode(status) == 70
        record = p.private_json(p.LAYOUT)
        assert record['images']['runtime']['phase'] == 'creating'
        orphan = Path(record['plan']['runtime']['image'])
        assert orphan.stat().st_size == 0
        orphan.write_bytes(b'unknown nonempty image')
        try:
            p.image_ready(record, 'runtime')
        except RuntimeError as error:
            assert str(error) == 'IMAGE_IDENTITY_CHANGED'
        else:
            raise AssertionError('NONEMPTY_ORPHAN_REFORMATTED')
        assert orphan.read_bytes() == b'unknown nonempty image'
        # Clear only our exact deliberate test bytes; product code refused it.
        orphan.write_bytes(b'')
        # Real process death after the first filesystem is formatted. Resume
        # uses the original on-disk transaction/inode/UUID, not a new plan.
        pid = os.fork()
        if pid == 0:
            try:
                p.image_ready(record, 'runtime')
                os._exit(71)
            except BaseException:
                os._exit(72)
        _, status = os.waitpid(pid, 0)
        assert os.waitstatus_to_exitcode(status) == 71
        record = p.private_json(p.LAYOUT)
        previous = dict(record['images']['runtime'])
        assert previous['phase'] == 'formatted' and 'archive' not in record['images']
        for key in ('runtime', 'archive'):
            p.image_ready(record, key)
        images = [Path(record['plan'][key]['image']) for key in ('runtime', 'archive')]
        units = [p.unit_name(p.BASE), p.unit_name(runtime), p.unit_name(archive)]
        # An ordinary UID writes through an already-open directory FD after
        # the first empty check. The second check must retain the file and
        # refuse to hide it beneath the image mount.
        real_chown = os.fchown
        def late_write(fd, uid, gid):
            if os.fstat(fd).st_ino == record['plan']['runtime']['original']['inode']:
                pid = os.fork()
                if pid == 0:
                    try:
                        os.setgroups([])
                        os.setgid(65534)
                        os.setuid(65534)
                        child = os.open('late-write', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=fd)
                        os.write(child, b'preserve this late write')
                        os.close(child)
                        os._exit(0)
                    except BaseException:
                        os._exit(1)
                _, status = os.waitpid(pid, 0)
                assert os.waitstatus_to_exitcode(status) == 0
            real_chown(fd, uid, gid)
        with patch.object(p.os, 'fchown', side_effect=late_write):
            try:
                p.publish(record)
            except RuntimeError as error:
                assert str(error) == 'TARGET_CHANGED'
            else:
                raise AssertionError('LATE_TARGET_WRITE_WAS_HIDDEN')
        assert (runtime / 'late-write').read_bytes() == b'preserve this late write'
        assert not runtime.is_mount() and not p.CONFIG.exists()
        (runtime / 'late-write').unlink()
        pid = os.fork()
        if pid == 0:
            original_install = p.install_mount
            def cut_after_runtime(target, source, **kwargs):
                original_install(target, source, **kwargs)
                if target == runtime:
                    os._exit(73)
            p.install_mount = cut_after_runtime
            try:
                p.publish(record)
                os._exit(74)
            except BaseException:
                os._exit(75)
        _, status = os.waitpid(pid, 0)
        assert os.waitstatus_to_exitcode(status) == 73
        assert runtime.is_mount() and not archive.is_mount() and not p.CONFIG.exists()
        record = p.private_json(p.LAYOUT)
        pid = os.fork()
        if pid == 0:
            original_write = p.write_new
            def cut_after_config(path, raw, mode):
                original_write(path, raw, mode)
                if path == p.CONFIG:
                    os._exit(78)
            p.write_new = cut_after_config
            try:
                p.publish(record)
                os._exit(79)
            except BaseException:
                os._exit(80)
        _, status = os.waitpid(pid, 0)
        assert os.waitstatus_to_exitcode(status) == 78
        assert p.CONFIG.exists() and p.private_json(p.LAYOUT)['phase'] != 'ready'
        record = p.private_json(p.LAYOUT)
        p.publish(record)
        assert record['phase'] == 'ready' and record['images']['runtime']['inode'] == previous['inode']
        assert record['images']['runtime']['uuid'] == previous['uuid']
        config_bytes = p.CONFIG.read_bytes()
        mounted = {str(target): (target.stat().st_dev, target.stat().st_ino) for target in (p.BASE, runtime, archive)}
        config = p.driver.configuration('nvme-main')
        with p.driver.namespace('nvme-main', config) as fd:
            p.quota.require_enforcing(fd)
        with p.driver.absolute(archive) as fd:
            p.quota.require_enforcing(fd)
        # A completed request is safely replayable without format or remount.
        replay = p.plan(arguments)
        for key in ('runtime', 'archive'):
            p.image_ready(replay, key)
        p.publish(replay)
        assert p.CONFIG.read_bytes() == config_bytes
        assert mounted == {str(target): (target.stat().st_dev, target.stat().st_ino) for target in (p.BASE, runtime, archive)}
        format_rows = [json.loads(row) for row in formats.read_text().splitlines()]
        assert len(format_rows) == 2 and len({row['image'] for row in format_rows}) == 2
        for name, source in [('layout.json', p.LAYOUT), ('storage.json', p.CONFIG), ('formats.jsonl', formats)]:
            shutil.copyfile(source, evidence / name)
        for source in [script, Path(p.storage.__file__), Path(p.quota.__file__), Path(p.files.__file__), Path(__file__)]:
            shutil.copyfile(source, evidence / source.name)
        accepted = dict(passed=True, runtimeBytes=2 * 10**9, archiveBytes=2 * 10**9,
            nonemptyRefused=True, nonemptyOrphanRetained=True, wrongImageFilesystemRefused=True, lateTargetWriteRetained=True,
            interruptedAfterImageCreate=True, interruptedAfterFormat=True, interruptedAfterFirstMount=True,
            interruptedAfterConfigurationPublish=True,
            partialMountKeptConfigUnpublished=True, originalRuntimeInodeKept=True,
            originalRuntimeUuidKept=True, formatCount=2, actualMounts=3, completedReplay=True,
            replayMountsUnchanged=True, replayConfigurationUnchanged=True, actualNvmeAttested=True,
            bothProjectQuotasEnforced=True, productionConfigurationWrites=0)
    except BaseException as error:
        (evidence / 'failed.json').write_text(json.dumps({'failureType': type(error).__name__}))
        raise
    finally:
        # Unit names come only from this invocation's freshly created paths.
        for name in units:
            destination = p.UNIT_ROOT / name
            if destination.exists():
                assert 'Whale Deck bounded development storage' in destination.read_text()
                p.command('systemctl', 'disable', '--now', name)
                destination.unlink()
        p.command('systemctl', 'daemon-reload')
        assert not any(target.is_mount() for target in (p.BASE, runtime, archive))
        for image in images:
            assert not p.command('losetup', '--associated', str(image), '--noheadings', '--output', 'NAME')
        shutil.rmtree(home)
        shutil.rmtree(archive_home)
        if accepted is not None:
            accepted['temporaryMountsImagesUnitsRemoved'] = True
            (evidence / 'accepted.json').write_text(json.dumps(accepted, indent=2))
        for path in evidence.iterdir():
            path.chmod(0o600)
        listing = ''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.name + '\n'
                          for path in sorted(evidence.iterdir()) if path.is_file())
        (evidence / 'SHA256SUMS').write_text(listing)
        (evidence / 'SHA256SUMS').chmod(0o600)
    print(json.dumps(dict(passed=True, evidence=str(evidence), temporaryResourcesRemoved=True)))


if __name__ == '__main__':
    main()
