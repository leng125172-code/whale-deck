#!/usr/bin/env python3
"""Opt-in native archive proof using only two new disposable ext4 images.

Requires the workstation's physical NVMe/HDD and ready development layout.
The configured 300 GB archive is attested read-only; no production workspace,
configuration or business/audit row is changed. Only O_EXCL-created, inode-bound
QA images are formatted and precisely removed after unmount. On failure, keep
the private evidence; never delete a mounted or unexpectedly replaced image.
"""
import errno
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import stat
import subprocess
import sys
import tarfile
import uuid
from unittest.mock import patch

MIB = 1024**2
SIZE = 2 * 1024**3


def load(root, name):
    filename = root / ('whaledeck-' + name + '.py')
    spec = importlib.util.spec_from_file_location(name, filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def command(*argv):
    result = subprocess.run(argv, capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise RuntimeError('NATIVE_ARCHIVE_COMMAND_FAILED:' + argv[0])
    return result.stdout.strip()


def writer(directory, name, size, expected=None):
    child = os.fork()
    if child == 0:
        try:
            os.setgroups([]); os.setgid(65534); os.setuid(65534)
            fd = os.open(name, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=directory)
            try:
                os.posix_fallocate(fd, 0, size)
            except OSError as error:
                os._exit(0 if error.errno == expected else 2)
            os.fsync(fd)
            os._exit(0 if expected is None else 3)
        except BaseException:
            os._exit(4)
    _, status = os.waitpid(child, 0)
    assert os.waitstatus_to_exitcode(status) == 0, 'NATIVE_UNPRIVILEGED_WRITER_FAILED'


def identity(driver, path):
    with driver.absolute(path) as fd:
        info = os.fstat(fd)
        return dict(device=f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}', inode=info.st_ino,
            uid=info.st_uid, gid=info.st_gid, mode=stat.S_IMODE(info.st_mode), mountId=driver.mount_id(fd))


def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True) + chr(10))
    path.chmod(0o600)


def physical(driver, quota, request, payload):
    with driver.attested_payload(request) as (fd, proof):
        state = quota.read_quota(fd, proof['projectId'])
    tree = {}
    for file in [payload, *sorted(payload.rglob('*'))]:
        info = file.lstat()
        assert stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode)
        tree[str(file.relative_to(payload))] = dict(version=driver.files.version(info),
            sha256=hashlib.sha256(file.read_bytes()).hexdigest() if file.is_file() else None)
    return dict(proof=proof, quota=list(state), tree=tree)


def main():
    assert os.geteuid() == 0 and __debug__, 'Requires explicit root invocation with assertions enabled'
    os.umask(0o077)
    repo = Path(__file__).resolve().parents[2]
    sources = repo / 'deploy/host'
    files, storage, quota, lifecycle, tree, codec, archives = (load(sources, name) for name in
        ('files', 'development-storage', 'development-project-quota', 'development-lifecycle',
         'file-tree', 'tree-archive', 'development-archives'))
    driver = storage.WorkspaceStorage(files, quota=quota)
    identifier = uuid.uuid4().hex
    evidence = repo / 'deploy/artifacts' / ('development-archives-native-' + identifier)
    evidence.mkdir(mode=0o700)
    for name in ('files', 'development-storage', 'development-project-quota', 'development-lifecycle',
                 'file-tree', 'tree-archive', 'development-archives'):
        destination = evidence / ('whaledeck-' + name + '.py')
        shutil.copyfile(sources / destination.name, destination); destination.chmod(0o600)
    shutil.copyfile(__file__, evidence / 'probe.py'); (evidence / 'probe.py').chmod(0o600)
    before_layout = archives.LAYOUT.read_bytes()
    before_config = storage.CONFIG.read_bytes()
    # Real configured HDD attestation performs only opens, stats, reads/flock.
    with archives.HddArchiveStorage(driver).locked(exclusive=False) as (_, configured):
        assert configured['mountPoint'] == '/data/DevSpace'
        assert configured['diskImage']['sizeBytes'] == 300 * 10**9
        save(evidence / 'configured-hdd-readonly.json', configured)
    runtime_home = storage.IMAGE_ROOT / ('qa-archive-' + identifier)
    runtime_image = storage.IMAGE_ROOT / ('qa-archive-' + identifier + '.ext4')
    hdd_home = Path('/data') / ('whaledeck-archive-qa-' + identifier)
    archive_image_root = hdd_home / '.whaledeck-development-disks'
    archive_image = archive_image_root / 'archive.ext4'
    mounted = []
    owned = {}
    home_pins = {}
    accepted = None
    def resources():
        save(evidence / 'owned-resources.json', dict(runtimeHome=str(runtime_home), archiveHome=str(hdd_home),
            mounted=[str(p) for p in mounted], images=[dict(path=p, device=i.st_dev, inode=i.st_ino) for p, i in owned.items()],
            homes=[dict(path=p, device=i.st_dev, inode=i.st_ino) for p, i in home_pins.items()]))
    print(json.dumps(dict(phase='creating-isolated-images', evidence=str(evidence))), flush=True)
    try:
        for home in (runtime_home, hdd_home):
            home.mkdir(mode=0o700); home_pins[str(home)] = home.lstat(); resources()
        archive_image_root.mkdir(mode=0o700)
        specs, images = {}, {}
        for key, home, image in (('runtime', runtime_home, runtime_image), ('archive', hdd_home, archive_image)):
            target = home / 'mount'; target.mkdir(mode=0o700)
            original = identity(driver, target)
            (driver.require_nvme if key == 'runtime' else driver.require_hdd)(original['device'])
            free = os.statvfs(home)
            assert free.f_bavail * free.f_frsize >= SIZE + (64 if key == 'runtime' else 8) * 1024**3
            fd = os.open(image, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            try:
                owned[str(image)] = os.fstat(fd)
                resources()
                os.posix_fallocate(fd, 0, SIZE); os.fsync(fd)
            finally:
                os.close(fd)
            command('mkfs.ext4', '-q', '-F', '-m', '0', '-O', 'project,quota', '-E',
                'quotatype=prjquota,nodiscard,lazy_itable_init=0,lazy_journal_init=0', str(image))
            command('mount', '-t', 'ext4', '-o', 'loop,prjquota,nosuid,nodev', str(image), str(target))
            mounted.append(target)
            resources()
            command('udevadm', 'settle', '--timeout=20')
            target.chmod(0o700)
            filesystem_uuid = command('blkid', '-p', '-s', 'UUID', '-o', 'value', str(image))
            outer = [v.removeprefix('E:ID_FS_UUID=') for v in driver.device_records(original['device']) if v.startswith('E:ID_FS_UUID=')]
            assert len(outer) == 1
            specs[key] = dict(target=str(target), sizeBytes=SIZE, original=original,
                image=str(image), filesystemUuid=outer[0])
            images[key] = dict(inode=image.stat().st_ino, uuid=filesystem_uuid, phase='mounted')
        assert specs['runtime']['original']['device'] != specs['archive']['original']['device']
        root_id = 'qa-archive-nvme'
        runtime = specs['runtime']
        base = Path(runtime['target']) / 'registered'; base.mkdir(mode=0o700)
        (base / root_id).mkdir(mode=0o700)
        configuration = dict(schemaVersion=2, roots={root_id:dict(mountPoint=runtime['target'],
            filesystemUuid=images['runtime']['uuid'], reserveMiB=512,
            projectIdStart=1600000000, projectIdEnd=1600000031,
            diskImage=dict(path=runtime['image'], device=runtime['original']['device'],
                inode=images['runtime']['inode'], sizeBytes=SIZE, filesystemUuid=runtime['filesystemUuid']))})
        configuration_path = runtime_home / 'storage.json'; save(configuration_path, configuration)
        layout = dict(schemaVersion=1, plan=specs, planSha256=archives.digest(driver, specs), phase='ready', images=images)
        layout_path = runtime_home / 'layout.json'; save(layout_path, layout)
        save(evidence / 'isolated-layout.json', layout); save(evidence / 'isolated-storage.json', configuration)
        storage.CONFIG, storage.BASE, archives.LAYOUT = configuration_path, base, layout_path
        request = dict(schemaVersion=1, rootId=root_id, workspaceId=str(uuid.uuid4()), environmentSha256='a' * 64, diskMiB=256)
        driver.allocate(request)
        payload = base / root_id / request['workspaceId'] / 'data'
        work = payload / 'work'; work.mkdir(mode=0o700); os.chown(work, 65534, 65534)
        fd = os.open(work, os.O_RDONLY | os.O_DIRECTORY)
        try: writer(fd, 'quota-denied', 260 * MIB, errno.EDQUOT)
        finally: os.close(fd)
        (work / 'quota-denied').unlink()
        data = bytes(range(256)) * 4097
        source = work / '真实源码.bin'; source.write_bytes(data); source.chmod(0o750); os.chown(source, 65534, 65534)
        os.utime(source, ns=(1712345678123456789, 1712345678123456789))
        (work / '.env').write_bytes(b'private-public-fixture-only')
        (work / 'empty').mkdir(mode=0o710)
        archive_target = Path(specs['archive']['target'])
        control = archive_target / 'capacity-test'; control.mkdir(mode=0o700); os.chown(control, 65534, 65534)
        fd = os.open(control, os.O_RDONLY | os.O_DIRECTORY)
        try: writer(fd, 'filesystem-denied', 3 * 1024**3, errno.ENOSPC)
        finally: os.close(fd)
        (control / 'filesystem-denied').unlink(); control.rmdir()
        transition = dict(storage=request, action='stop', expectedEpoch=1, operationId='', planSha256='', storageProofSha256='')
        manager = lifecycle.Lifecycle(driver); planned = manager.run(transition)
        manager.run(dict(transition, operationId=str(uuid.uuid4()), planSha256=planned['planSha256'], storageProofSha256=planned['storageProofSha256']), apply=True)
        # The native paths must also reject a different pinned image/UUID and
        # the opposite physical kind, without changing the real configuration.
        for verifier, device in ((driver.require_hdd, specs['runtime']['original']['device']),
                                 (driver.require_nvme, specs['archive']['original']['device'])):
            try: verifier(device)
            except files.Rejected: pass
            else: raise AssertionError('CROSS_PHYSICAL_KIND_ACCEPTED')
        for key, changed in (('inode', images['archive']['inode'] + 1), ('uuid', 'ffffffff-ffff-ffff-ffff-ffffffffffff')):
            wrong = json.loads(json.dumps(layout)); wrong['images']['archive'][key] = changed
            save(layout_path, wrong)
            try:
                try:
                    with archives.HddArchiveStorage(driver).locked(exclusive=False): pass
                except (files.Rejected, OSError): pass
                else: raise AssertionError('WRONG_ARCHIVE_PIN_ACCEPTED')
            finally: save(layout_path, layout)
        os.setxattr(source, 'user.qa-native-fixture', b'unsupported metadata')
        try:
            try:
                archives.DevelopmentArchives(driver, tree.FileTreeManifest, codec.TreeArchive, archives.HddArchiveStorage(driver)).plan(request, 2)
            except files.Rejected as error: assert str(error) == 'WORKSPACE_STORAGE_ARCHIVE_METADATA_UNSUPPORTED'
            else: raise AssertionError('EXTENDED_METADATA_SILENTLY_LOST')
        finally: os.removexattr(source, 'user.qa-native-fixture')
        before = physical(driver, quota, request, payload); save(evidence / 'before.json', before)
        helper_path = Path(__file__).with_name('validate_development_archive_helper_native.py')
        spec = importlib.util.spec_from_file_location('native_archive_helper', helper_path)
        helper_probe = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper_probe)
        helper_proof = helper_probe.exercise(sources, evidence, runtime_home, configuration_path, base, layout_path, archive_target, request)
        engine = archives.DevelopmentArchives(driver, tree.FileTreeManifest, codec.TreeArchive, archives.HddArchiveStorage(driver))
        print(json.dumps(dict(phase='capturing-and-rehashing', kernelQuotaDenied=True, kernelCapacityDenied=True)), flush=True)
        captures = []
        original_exchange = files.exchange
        for cut in ('none', 'before-rename', 'after-rename'):
            plan = engine.plan(request, 2)
            capture = dict(storage=request, expectedEpoch=2, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'])
            private = archive_target / archives.NAMESPACE / capture['operationId']
            if cut != 'none':
                def interrupted(parent, name, target, leaf, flags):
                    selected = name == 'archive.partial' and leaf == 'payload.tar.gz'
                    if selected and cut == 'before-rename': os.kill(os.getpid(), signal.SIGSTOP)
                    result = original_exchange(parent, name, target, leaf, flags)
                    if selected and cut == 'after-rename': os.kill(os.getpid(), signal.SIGSTOP)
                    return result
                child = os.fork()
                if child == 0:
                    try:
                        with patch.object(files, 'exchange', interrupted): engine.capture(capture)
                    finally: os._exit(3)
                _, status = os.waitpid(child, os.WUNTRACED)
                assert os.WIFSTOPPED(status)
                os.kill(child, signal.SIGKILL); os.waitpid(child, 0)
                previous = private / ('archive.partial' if cut == 'before-rename' else 'payload.tar.gz')
                previous_identity = (previous.stat().st_ino, hashlib.sha256(previous.read_bytes()).hexdigest())
            result = engine.capture(capture)
            archive = private / 'payload.tar.gz'
            if cut != 'none': assert previous_identity == (archive.stat().st_ino, hashlib.sha256(archive.read_bytes()).hexdigest())
            assert result == engine.verify(capture, result['receiptSha256'])
            assert result == engine.capture(capture)
            with tarfile.open(archive) as reader:
                assert reader.extractfile('work/真实源码.bin').read() == data
                assert reader.extractfile('work/.env').read() == b'private-public-fixture-only'
            copied = evidence / (cut + '.tar.gz'); shutil.copyfile(archive, copied); copied.chmod(0o600)
            assert hashlib.sha256(copied.read_bytes()).hexdigest() == result['archiveSha256']
            captures.append(dict(cut=cut, request=capture, result=result, preservedBytesAndInode=True))
            save(evidence / 'captures.json', captures)
            if cut == 'none':
                old_mount = identity(driver, archive_target)['mountId']
                command('umount', str(archive_target)); mounted.remove(archive_target); resources()
                # Linux can immediately reuse a released mount ID. Consume
                # that identity with another new, private QA bind mount so the
                # archive actually returns with a different live mount ID.
                guard = hdd_home / 'mount-identity-guard'; guard.mkdir(mode=0o700)
                command('mount', '--bind', str(archive_image_root), str(guard))
                mounted.append(guard); resources()
                command('mount', '-t', 'ext4', '-o', 'loop,prjquota,nosuid,nodev', str(archive_image), str(archive_target))
                mounted.append(archive_target); resources(); command('udevadm', 'settle', '--timeout=20')
                new_mount = identity(driver, archive_target)['mountId']; assert old_mount != new_mount
                assert result == engine.verify(capture, result['receiptSha256'])
                save(evidence / 'remount.json', dict(oldMountId=old_mount, newMountId=new_mount, sameReceiptVerified=True))
        # Prove verification can re-read HDD while the original source path is
        # genuinely absent. Its directory remains retained in this QA image.
        root = base / root_id
        original = root / request['workspaceId']; retained = root / 'retained-source-for-proof'
        original.rename(retained)
        try: assert captures[0]['result'] == engine.verify(captures[0]['request'], captures[0]['result']['receiptSha256'])
        finally: retained.rename(original)
        after = physical(driver, quota, request, payload); save(evidence / 'after.json', after)
        assert before == after
        save(evidence / 'captures.json', captures)
        for name in ('files', 'development-storage', 'development-project-quota', 'development-lifecycle',
                     'file-tree', 'tree-archive', 'development-archives'):
            filename = 'whaledeck-' + name + '.py'
            assert (evidence / filename).read_bytes() == (sources / filename).read_bytes()
        assert before_config == Path('/etc/whaledeck/development-storage.json').read_bytes()
        assert before_layout == Path('/var/lib/whaledeck-development-disks/layout.json').read_bytes()
        accepted = dict(passed=True, configuredArchiveReadOnlyAttested=True, configuredArchiveBytes=300 * 10**9,
            isolatedNvmeBytes=SIZE, isolatedHddBytes=SIZE, realExt4ProjectQuotaDenied=True,
            realExt4FilesystemCapacityDenied=True, captures=3, nativeProcessCuts=2,
            changedImageAndUuidRefused=True, crossPhysicalKindRefused=True, extendedMetadataRefused=True,
            archiveRemountPreservesVerification=True,
            independentHddVerificationWithoutSource=True, sourceInodesBytesAndQuotaUnchanged=True,
            productionConfigurationUnchanged=True, productionWorkspaceMutations=0, sandboxedArchiveHelper=helper_proof,
            scope='Internal helper/optional isolated signed RPC only; no product UI, retirement, deletion or quota release')
    except BaseException as error:
        save(evidence / 'failure.json', dict(type=type(error).__name__,
            reason=str(error) if isinstance(error, RuntimeError) else 'Native proof assertion or dependency failed'))
        raise
    finally:
        for mount in reversed(mounted):
            command('umount', str(mount))
        for path, expected in owned.items():
            image = Path(path); info = image.lstat()
            assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and os.path.samestat(info, expected)
            assert not command('losetup', '--associated', str(image), '--noheadings', '--output', 'NAME')
            image.unlink()
        for path, expected in home_pins.items():
            home = Path(path); info = home.lstat()
            assert os.path.samestat(info, expected) and stat.S_ISDIR(info.st_mode)
            assert not any(p.is_mount() for p in home.rglob('*') if p.is_dir())
            assert info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
            shutil.rmtree(home)
        save(evidence / 'cleanup.json', dict(passed=True, ownedImagesRemoved=list(owned),
            originalMountsRemoved=[str(p) for p in mounted], homesRemoved=list(home_pins)))
        if accepted is not None:
            accepted['isolatedImagesAndMountsRemoved'] = True
            save(evidence / 'accepted.json', accepted)
            print(json.dumps(dict(**accepted, evidence=str(evidence))), flush=True)


if __name__ == '__main__':
    main()
