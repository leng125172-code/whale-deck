#!/usr/bin/env python3
"""Internal stopped-workspace capture into the bounded development HDD.

No CLI, privilege grant, RPC, source deletion, quota release or general file
root. The future authorized workflow supplies a registered storage request.
Only the provisioner's ready, fully reserved HDD image is accepted. Complete
private tar content is inspected independently before a durable receipt exists.
Partial evidence is retained; a retry never truncates or recopies an object.
"""
import contextlib
import fcntl
import hashlib
import os
from pathlib import Path
import re
import stat
import struct

LAYOUT = Path('/var/lib/whaledeck-development-disks/layout.json')
NAMESPACE = 'workspace-archives-v1'
MAX_CAPTURES = 10000
MAX_BYTES = 16 * 1024**3
MAX_ARCHIVE_BYTES = 17 * 1024**3
RESERVE_BYTES = 512 * 1024**2
UUID = r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}'
ZERO = '00000000-0000-0000-0000-000000000000'
DIR = os.O_RDONLY | os.O_DIRECTORY
READ = os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW
GET_FLAGS = 0x80086601  # FS_IOC_GETFLAGS on the driver's 64-bit Linux ABI.
REPRESENTATION_FLAGS = 0x20081000  # project inheritance, extents and directory index.


def digest(driver, value):
    return hashlib.sha256(driver.canonical(value)).hexdigest()


class HddArchiveStorage:
    def __init__(self, driver):
        self.d = driver

    def reject(self, code):
        self.d.reject('ARCHIVE_' + code)

    def declaration(self):
        d = self.d
        with d.absolute(LAYOUT.parent) as parent:
            d.directory(parent, private=True)
            value = d.read_json(parent, LAYOUT.name, 65536)
        if (set(value) != {'schemaVersion', 'plan', 'planSha256', 'phase', 'images'}
                or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1
                or value['phase'] != 'ready' or not isinstance(value['plan'], dict)
                or set(value['plan']) != {'runtime', 'archive'}
                or not isinstance(value['images'], dict) or set(value['images']) != {'runtime', 'archive'}
                or value['planSha256'] != digest(d, value['plan'])):
            self.reject('LAYOUT_INVALID')
        spec, image = value['plan']['archive'], value['images']['archive']
        if (not isinstance(spec, dict) or set(spec) != {'target', 'sizeBytes', 'original', 'image', 'filesystemUuid'}
                or not isinstance(image, dict) or set(image) != {'inode', 'uuid', 'phase'}
                or image['phase'] != 'mounted' or type(image['inode']) is not int or image['inode'] <= 0
                or not isinstance(image['uuid'], str) or not re.fullmatch(UUID, image['uuid'])
                or not isinstance(spec['filesystemUuid'], str) or not re.fullmatch(UUID, spec['filesystemUuid'])
                or type(spec['sizeBytes']) is not int or not 2 * 10**9 <= spec['sizeBytes'] <= 300 * 10**9
                or spec['sizeBytes'] % 512 or not isinstance(spec['target'], str)
                or not re.fullmatch(r'/[A-Za-z0-9/_-]+', spec['target'])
                or os.path.normpath(spec['target']) != spec['target'] or spec['target'] == '/'
                or not isinstance(spec['original'], dict)
                or set(spec['original']) != {'device', 'inode', 'uid', 'gid', 'mode', 'mountId'}
                or not isinstance(spec['original']['device'], str)
                or not re.fullmatch(r'[0-9]+:[0-9]+', spec['original']['device'])
                or spec['image'] != str(Path(spec['target']).parent / '.whaledeck-development-disks/archive.ext4')):
            self.reject('LAYOUT_INVALID')
        return dict(mountPoint=spec['target'], filesystemUuid=image['uuid'],
            layoutSha256=digest(d, value), diskImage=dict(path=spec['image'],
                device=spec['original']['device'], inode=image['inode'], sizeBytes=spec['sizeBytes'],
                filesystemUuid=spec['filesystemUuid']))

    def attest(self, target, configuration):
        d = self.d
        d.directory(target, private=True)
        mount_id, _, dev, source, mount = d.mount_entry(target)
        if source != '/' or mount != configuration['mountPoint']:
            self.reject('MOUNT_MISMATCH')
        d.require_writable(target)
        records = d.device_records(dev)
        if ([v for v in records if v.startswith('E:ID_FS_UUID=')] !=
                ['E:ID_FS_UUID=' + configuration['filesystemUuid']] or 'E:ID_FS_USAGE=filesystem' not in records):
            self.reject('DEVICE_MISMATCH')
        image = configuration['diskImage']
        d.require_reserved_image(dev, image, Path(image['path']).parent, d.require_hdd)
        return dict(configuration, mountId=mount_id, device=dev, rootInode=os.fstat(target).st_ino)

    @contextlib.contextmanager
    def locked(self, *, exclusive):
        d = self.d
        configuration = self.declaration()
        with d.absolute(configuration['mountPoint']) as target:
            try:
                fcntl.flock(target, (fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH) | fcntl.LOCK_NB)
            except BlockingIOError:
                self.reject('BUSY')
            proof = self.attest(target, configuration)
            self.recheck(target, proof)
            yield target, proof

    def recheck(self, target, proof):
        configuration = self.declaration()
        with self.d.absolute(configuration['mountPoint']) as live:
            if (not os.path.samestat(os.fstat(live), os.fstat(target))
                    or self.attest(live, configuration) != proof):
                self.reject('STORAGE_CHANGED')

    def capacity(self, target, expected_bytes):
        usage = os.fstatvfs(target)
        if usage.f_bavail * usage.f_frsize < expected_bytes + RESERVE_BYTES:
            self.reject('CAPACITY_REQUIRED')


class PayloadFiles:
    """Codec-only adapter for one already attested private payload.

    Private names are captured, not omitted or exposed as public file roots.
    openat2 still rejects links/mount crossing; the shared codec rejects special
    objects. No arbitrary command, path, root registration or extraction here.
    """
    def __init__(self, files, parent=None, command=None, policy=None):
        self.f, self.parent, self.command, self.policy = files, parent, command, policy

    def __getattr__(self, key):
        return getattr(self.f, key)

    @staticmethod
    def protected(_):
        return False

    def roots(self):
        return {self.command['rootId']: self.policy} if self.command is not None else {}

    @contextlib.contextmanager
    def directory(self, command, action):
        actions = (action,) if isinstance(action, str) else action
        if (self.parent is None or command != self.command or not set(actions) <= {'list', 'download'}):
            raise self.f.Rejected('WORKSPACE_STORAGE_ARCHIVE_SCOPE_INVALID')
        yield self.policy, self.parent, 'data'


class DevelopmentArchives:
    def __init__(self, driver, manifest_type, codec_type, target):
        self.d, self.manifest_type, self.codec_type, self.target = driver, manifest_type, codec_type, target

    def reject(self, code):
        self.d.reject('ARCHIVE_' + code)

    def operation(self, value):
        if not isinstance(value, str) or not re.fullmatch(UUID, value) or value == ZERO:
            self.reject('REQUEST_INVALID')
        return value

    @contextlib.contextmanager
    def source(self, request, epoch, *, exclusive):
        d = self.d
        root, workspace = d.validate_request(request)
        if type(epoch) is not int or not 2 <= epoch < 0x7ffffffffffffffe or epoch % 2:
            self.reject('REQUEST_INVALID')
        with d.locked_root(root, exclusive=exclusive) as (parent, configuration):
            original = d.read_existing(parent, workspace, request, configuration)
            if original is None:
                self.reject('NOT_ALLOCATED')
            with d.quota_payload(parent, workspace, original, configuration) as (payload, proof):
                marker = d.lifecycle_state(parent, workspace, request, proof=proof)
                if marker is None or marker['state'] != 'Stopped' or marker['epoch'] != epoch:
                    self.reject('NOT_STOPPED')
                with d.opened(parent, workspace, DIR) as container:
                    if (d.directory(container, private=True).st_ino != proof['directoryInode']
                            or not os.path.samestat(os.stat('data', dir_fd=container, follow_symlinks=False), os.fstat(payload))):
                        self.reject('SOURCE_CHANGED')
                    command = dict(rootId='development-private-payload', path='data', expectedVersion=d.files.version(os.fstat(payload)))
                    policy = dict(allowedActions=['files-list', 'files-download'], storageProofSha256=digest(d, proof))
                    adapter = PayloadFiles(d.files, container, command, policy)
                    manifest = self.manifest_type(adapter, max_bytes=MAX_BYTES).scan(command)
                    self.require_metadata(payload, manifest)
                    yield adapter, command, manifest, proof, payload
                d.recheck(parent, root, configuration)
                if d.read_existing(parent, workspace, request, configuration) != original:
                    self.reject('SOURCE_CHANGED')
                d.quota_proof(parent, workspace, original, configuration)

    def require_metadata(self, payload, manifest):
        # The shared codec preserves ordinary mode/uid/gid/mtime/content, not
        # ACLs, xattrs or special mode bits. Refuse instead of silently losing
        # them and later presenting an incomplete copy as a retirement proof.
        for row in manifest['entries']:
            flags = READ | (os.O_DIRECTORY if row['kind'] == 'Directory' else 0)
            with self.d.opened(payload, row['path'] or '.', flags) as fd:
                info = os.fstat(fd)
                if self.d.files.version(info) != row['sourceVersion']:
                    self.reject('SOURCE_CHANGED')
                flags = struct.unpack('=I', fcntl.ioctl(fd, GET_FLAGS, bytes(8))[:4])[0]
                if info.st_mode & 0o7000 or os.listxattr(fd) or flags & ~REPRESENTATION_FLAGS:
                    self.reject('METADATA_UNSUPPORTED')

    def facts(self, request, epoch, manifest, proof, hdd):
        # Mount IDs/loop device numbers are live-operation guards, not durable
        # archive identities. The filesystem UUID, root/image inode and pinned
        # backing remain the persisted binding across a supported remount.
        durable_hdd = {k: v for k, v in hdd.items() if k not in ('mountId', 'device')}
        return dict(schemaVersion=1, storage=request, expectedEpoch=epoch,
            storageProofSha256=digest(self.d, proof), sourceVersion=manifest['rootVersion'],
            sourceManifestSha256=manifest['manifestSha256'].lower(), entryCount=manifest['entryCount'],
            totalBytes=manifest['totalBytes'], archiveFilesystemUuid=hdd['filesystemUuid'],
            hddProofSha256=digest(self.d, durable_hdd))

    def plan(self, request, epoch):
        with self.source(request, epoch, exclusive=False) as (_, _, manifest, proof, payload):
            with self.target.locked(exclusive=False) as (target, hdd):
                if os.fstat(target).st_dev == os.fstat(payload).st_dev:
                    self.reject('SEPARATE_STORAGE_REQUIRED')
                facts = self.facts(request, epoch, manifest, proof, hdd)
                self.target.recheck(target, hdd)
                return dict(facts, planSha256=digest(self.d, facts))

    def durable_json(self, directory, name, value):
        d = self.d
        try:
            d.read_json(directory, name, 8192, expected=value, sync=True)
            os.fsync(directory)
            return
        except FileNotFoundError:
            pass
        raw = d.canonical(value)
        pending = '.' + name + '.pending'
        try:
            fd = os.open(pending, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=directory)
        except FileExistsError:
            fd = os.open(pending, os.O_RDWR | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=directory)
        try:
            info = os.fstat(fd)
            if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                    or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size > len(raw)):
                self.reject('METADATA_CONFLICT')
            previous = os.read(fd, len(raw) + 1)
            if not raw.startswith(previous):
                self.reject('METADATA_CONFLICT')
            remaining = memoryview(raw)[len(previous):]
            while remaining:
                written = os.write(fd, remaining)
                if written <= 0:
                    self.reject('WRITE_FAILED')
                remaining = remaining[written:]
            os.fsync(fd)
        finally:
            os.close(fd)
        d.files.exchange(directory, pending, directory, name, 1)
        os.fsync(directory)

    @contextlib.contextmanager
    def entry(self, target, operation, *, create):
        d = self.d
        if create:
            try:
                os.mkdir(NAMESPACE, 0o700, dir_fd=target)
                os.fsync(target)
            except FileExistsError:
                pass
        with d.opened(target, NAMESPACE, DIR) as namespace:
            d.directory(namespace, private=True)
            if create:
                count, found = 0, False
                with os.scandir(namespace) as rows:
                    for row in rows:
                        count += 1
                        found |= row.name == operation
                        if count > MAX_CAPTURES:
                            self.reject('HISTORY_LIMIT')
                if count >= MAX_CAPTURES and not found:
                    self.reject('HISTORY_LIMIT')
                try:
                    os.mkdir(operation, 0o700, dir_fd=namespace)
                    os.fsync(namespace)
                except FileExistsError:
                    pass
            with d.opened(namespace, operation, DIR) as directory:
                d.directory(directory, private=True)
                yield directory

    def summary(self, operation, facts, plan_sha, archive, descriptor):
        info = os.fstat(descriptor)
        return dict(facts, operationId=operation, planSha256=plan_sha, archiveFormat='tar.gz',
            archiveBytes=archive['archiveBytes'], archiveSha256=archive['archiveSha256'].lower(),
            archiveIndexSha256=digest(self.d, archive['entries']),
            archiveInode=info.st_ino)

    def inspect(self, directory, codec, manifest):
        with self.d.opened(directory, 'payload.tar.gz', READ) as descriptor:
            codec.regular(descriptor, private=True)
            archive = codec.inspect(descriptor)
            if archive['entries'] != [codec.from_manifest(row) for row in manifest['entries']]:
                self.reject('CONTENT_INVALID')
            os.fsync(descriptor)
            return archive, os.fstat(descriptor)

    def capture(self, value):
        if (not isinstance(value, dict) or set(value) != {'storage', 'expectedEpoch', 'operationId', 'planSha256'}
                or not isinstance(value['planSha256'], str) or not re.fullmatch('[a-f0-9]{64}', value['planSha256'])):
            self.reject('REQUEST_INVALID')
        operation = self.operation(value['operationId'])
        d = self.d
        with self.source(value['storage'], value['expectedEpoch'], exclusive=True) as (adapter, command, manifest, proof, payload):
            with self.target.locked(exclusive=True) as (target, hdd):
                if os.fstat(target).st_dev == os.fstat(payload).st_dev:
                    self.reject('SEPARATE_STORAGE_REQUIRED')
                facts = self.facts(value['storage'], value['expectedEpoch'], manifest, proof, hdd)
                if value['planSha256'] != digest(d, facts):
                    self.reject('PLAN_CHANGED')
                codec = self.codec_type(adapter, self.manifest_type, max_bytes=MAX_BYTES, max_archive_bytes=MAX_ARCHIVE_BYTES)
                with self.entry(target, operation, create=True) as directory:
                    self.durable_json(directory, 'request.json', value)
                    try:
                        with d.opened(directory, 'payload.tar.gz', READ):
                            published = True
                    except FileNotFoundError:
                        published = False
                    if not published:
                        try:
                            os.stat('archive.partial', dir_fd=directory, follow_symlinks=False)
                        except FileNotFoundError:
                            estimate = manifest['totalBytes'] + (manifest['totalBytes'] + 999) // 1000 + manifest['entryCount'] * 18432 + 65536
                            self.target.capacity(target, min(MAX_ARCHIVE_BYTES, estimate))
                            output = os.open('archive.partial', os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=directory)
                            try:
                                codec.create(command, manifest['manifestSha256'], output)
                            finally:
                                os.close(output)
                        else:
                            # A complete private stream may survive just before
                            # rename. Inspect it without rewriting even one byte.
                            with d.opened(directory, 'archive.partial', READ) as existing:
                                codec.regular(existing, private=True)
                                try:
                                    previous = codec.inspect(existing)
                                except (d.files.Rejected, OSError):
                                    self.reject('INCOMPLETE')
                                if previous['entries'] != [codec.from_manifest(row) for row in manifest['entries']]:
                                    self.reject('CONTENT_INVALID')
                                os.fsync(existing)
                        d.files.exchange(directory, 'archive.partial', directory, 'payload.tar.gz', 1)
                        os.fsync(directory)
                    archive, _ = self.inspect(directory, codec, manifest)
                    self.manifest_type(adapter, max_bytes=MAX_BYTES).verify(command, manifest['manifestSha256'])
                    self.require_metadata(payload, manifest)
                    self.target.recheck(target, hdd)
                    with d.opened(directory, 'payload.tar.gz', READ) as descriptor:
                        result = self.summary(operation, facts, value['planSha256'], archive, descriptor)
                    self.durable_json(directory, 'receipt.json', result)
                    return dict(result, receiptSha256=digest(d, result))

    def verify(self, value, receipt_sha256):
        """Re-read only HDD, including every content byte, without the source.

        The future workflow must bind receipt_sha256 to its persisted capture
        result. A checksum alone is never authorization to remove NVMe data.
        """
        d = self.d
        if (not isinstance(value, dict) or set(value) != {'storage', 'expectedEpoch', 'operationId', 'planSha256'}
                or not isinstance(receipt_sha256, str) or not re.fullmatch('[a-f0-9]{64}', receipt_sha256)
                or not isinstance(value['planSha256'], str) or not re.fullmatch('[a-f0-9]{64}', value['planSha256'])):
            self.reject('REQUEST_INVALID')
        d.validate_request(value['storage'])
        if type(value['expectedEpoch']) is not int or not 2 <= value['expectedEpoch'] < 0x7ffffffffffffffe or value['expectedEpoch'] % 2:
            self.reject('REQUEST_INVALID')
        operation = self.operation(value['operationId'])
        with self.target.locked(exclusive=False) as (target, hdd):
            with self.entry(target, operation, create=False) as directory:
                d.read_json(directory, 'request.json', 8192, expected=value)
                receipt = d.read_json(directory, 'receipt.json', 8192)
                keys = {'schemaVersion', 'storage', 'expectedEpoch', 'storageProofSha256', 'sourceVersion',
                    'sourceManifestSha256', 'entryCount', 'totalBytes', 'hddProofSha256', 'archiveFilesystemUuid'}
                durable_hdd = {k: v for k, v in hdd.items() if k not in ('mountId', 'device')}
                if (set(receipt) != keys | {'operationId', 'planSha256', 'archiveFormat', 'archiveBytes',
                        'archiveSha256', 'archiveIndexSha256', 'archiveInode'}
                        or digest(d, receipt) != receipt_sha256 or receipt['schemaVersion'] != 1
                        or receipt['operationId'] != operation or receipt['storage'] != value['storage']
                        or receipt['expectedEpoch'] != value['expectedEpoch'] or receipt['planSha256'] != value['planSha256']
                        or receipt['archiveFilesystemUuid'] != hdd['filesystemUuid']
                        or receipt['hddProofSha256'] != digest(d, durable_hdd)):
                    self.reject('RECEIPT_CHANGED')
                facts = {k: receipt[k] for k in keys}
                if digest(d, facts) != value['planSha256']:
                    self.reject('RECEIPT_CHANGED')
                codec = self.codec_type(PayloadFiles(d.files), self.manifest_type, max_bytes=MAX_BYTES, max_archive_bytes=MAX_ARCHIVE_BYTES)
                with d.opened(directory, 'payload.tar.gz', READ) as fd:
                    codec.regular(fd, private=True)
                    archive = codec.inspect(fd)
                    actual = self.summary(operation, facts, value['planSha256'], archive, fd)
                    if d.canonical(actual) != d.canonical(receipt):
                        self.reject('CONTENT_INVALID')
                self.target.recheck(target, hdd)
                return dict(receipt, receiptSha256=receipt_sha256)
