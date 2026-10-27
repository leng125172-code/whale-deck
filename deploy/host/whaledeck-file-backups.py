#!/usr/bin/env python3
"""Root-owned, version-bound HDD copies before any managed file overwrite.

No CLI, arbitrary source/target, retention deletion or restore entry point.
The file driver supplies an already-authorized directory FD and sealed receipt.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import time

CONFIGURATION = '/etc/whaledeck/file-backups.json'
UDEV = '/run/udev/data'
SYS_DEVICES = '/sys/dev/block'
MOUNTINFO = '/proc/self/mountinfo'
STORAGE = 'database-platform.backup.hdd'


class FileBackups:
    def __init__(self, files, *, namespace=None):
        self.f = files
        if namespace not in (None, 'container-files-v1'):
            self.reject('FILES_BACKUP_METADATA_UNSAFE')
        self.namespace = namespace

    def reject(self, code='FILES_BACKUP_UNAVAILABLE'):
        raise self.f.Rejected(code)

    def read_json(self, parent, name, maximum=16384, private=True):
        descriptor = self.f.open_at(parent, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & (0o077 if private else 0o022) or info.st_size > maximum:
                self.reject('FILES_BACKUP_METADATA_UNSAFE')
            content = os.read(descriptor, maximum + 1)
            if len(content) > maximum or len(content) != info.st_size: self.reject('FILES_BACKUP_METADATA_UNSAFE')
            value = json.loads(content, object_pairs_hook=self.f.unique_object)
            if not isinstance(value, dict): self.reject('FILES_BACKUP_METADATA_UNSAFE')
            return value
        except (ValueError, UnicodeError): self.reject('FILES_BACKUP_METADATA_UNSAFE')
        finally: os.close(descriptor)

    def configuration(self):
        slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
        try: parent = self.f.open_at(slash, str(Path(CONFIGURATION).parent).lstrip('/'), os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        finally: os.close(slash)
        try:
            info = os.fstat(parent)
            if info.st_uid != 0 or info.st_mode & 0o022: self.reject('FILES_BACKUP_METADATA_UNSAFE')
            value = self.read_json(parent, Path(CONFIGURATION).name, private=False)
        finally: os.close(parent)
        required = {'schemaVersion', 'storageId', 'mountPoint', 'filesystemUuid', 'targetRelative', 'reserveBytes', 'reservePercent', 'maxVersions'}
        if set(value) != required or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1 or value['storageId'] != STORAGE:
            self.reject('FILES_BACKUP_CONFIGURATION_INVALID')
        mount = value['mountPoint']
        if not isinstance(mount, str) or not mount.startswith('/') or mount == '/' or os.path.normpath(mount) != mount or len(mount.encode()) > 4096 or any(ord(c) < 32 for c in mount):
            self.reject('FILES_BACKUP_CONFIGURATION_INVALID')
        if not isinstance(value['filesystemUuid'], str) or not re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}', value['filesystemUuid']):
            self.reject('FILES_BACKUP_CONFIGURATION_INVALID')
        self.f.relative(value['targetRelative'])
        target = mount + '/' + value['targetRelative']
        if target == self.f.BASE or target.startswith(self.f.BASE + '/'):
            self.reject('FILES_BACKUP_CONFIGURATION_INVALID')
        for key, minimum, maximum in (('reserveBytes', 512 * 1024**2, 1024**4), ('reservePercent', 5, 90), ('maxVersions', 1, 10000)):
            if type(value[key]) is not int or not minimum <= value[key] <= maximum: self.reject('FILES_BACKUP_CONFIGURATION_INVALID')
        return value

    def attest(self, configuration, descriptor, *, read_only=False):
        """Works with PrivateDevices: attest kernel mount + root-owned udev DB.

        Never infer a mounted HDD from directory existence or a parent st_dev.
        Missing udev information fails closed, without probing device contents.
        """
        info = os.fstat(descriptor); device = f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}'
        mount = configuration['mountPoint']
        def unescape(value):
            return re.sub(r'\\([0-7]{3})', lambda match: chr(int(match[1], 8)), value)
        with open(MOUNTINFO, encoding='utf-8') as stream:
            lines = stream.read(1024 * 1024 + 1)
        if len(lines) > 1024 * 1024: self.reject()
        entries = [line.split() for line in lines.splitlines()]
        matches = [entry for entry in entries if len(entry) > 6 and entry[2] == device and unescape(entry[4]) == mount]
        if len(matches) != 1: self.reject('FILES_BACKUP_MOUNT_MISMATCH')
        entry = matches[0]
        separator = entry.index('-')
        if separator + 3 >= len(entry) or entry[separator + 1] not in ('ext4', 'xfs'): self.reject('FILES_BACKUP_FILESYSTEM_UNSUPPORTED')
        udev = os.open(UDEV, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try:
            source = self.f.open_at(udev, 'b' + device, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                record = os.fstat(source)
                if not stat.S_ISREG(record.st_mode) or record.st_uid != 0 or record.st_nlink != 1 or record.st_mode & 0o022 or record.st_size > 65536:
                    self.reject('FILES_BACKUP_DEVICE_METADATA_UNSAFE')
                raw = os.read(source, 65537)
                if len(raw) != record.st_size or len(raw) > 65536: self.reject('FILES_BACKUP_DEVICE_METADATA_UNSAFE')
                values = raw.decode('utf-8').splitlines()
                if values.count('E:ID_FS_UUID=' + configuration['filesystemUuid']) != 1 or 'E:ID_FS_USAGE=filesystem' not in values:
                    self.reject('FILES_BACKUP_DEVICE_MISMATCH')
            finally: os.close(source)
        finally: os.close(udev)
        block = Path(SYS_DEVICES, device).resolve(strict=True)
        rotational = block / 'queue' / 'rotational'
        if not rotational.is_file(): rotational = block.parent / 'queue' / 'rotational'
        if rotational.read_bytes() != b'1\n': self.reject('FILES_BACKUP_HDD_REQUIRED')
        # ProtectSystem=strict deliberately remounts the history reader's HDD
        # read-only. Readers still attest the pinned mount/UUID/HDD identity;
        # writers must continue to fail closed on a read-only filesystem.
        if not read_only and os.fstatvfs(descriptor).f_flag & os.ST_RDONLY: self.reject('FILES_BACKUP_READ_ONLY')
        # Check the pinned mount's own directory too, not just an unrelated
        # same-device entry fabricated by a bind mount elsewhere.
        slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
        try: mounted = self.f.open_at(slash, mount.lstrip('/'), os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        finally: os.close(slash)
        try:
            if os.fstat(mounted).st_dev != info.st_dev: self.reject('FILES_BACKUP_MOUNT_MISMATCH')
        finally: os.close(mounted)

    @contextlib.contextmanager
    def target(self, *, create_namespace=True, read_only=False):
        if read_only and create_namespace: self.reject('FILES_BACKUP_READ_ONLY')
        configuration = self.configuration()
        slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
        try:
            path = configuration['mountPoint'].lstrip('/') + '/' + configuration['targetRelative']
            descriptor = self.f.open_at(slash, path, os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        finally: os.close(slash)
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_BACKUP_DIRECTORY_UNSAFE')
            self.attest(configuration, descriptor, read_only=read_only)
            if self.namespace is None:
                yield configuration, descriptor
            else:
                # A fixed resource namespace keeps host history readers and
                # container captures independent. Never accept a user path.
                if create_namespace:
                    try: os.mkdir(self.namespace, 0o700, dir_fd=descriptor); os.fsync(descriptor)
                    except FileExistsError: pass
                child = self.f.open_at(descriptor, self.namespace, os.O_RDONLY | os.O_DIRECTORY)
                try:
                    info = os.fstat(child)
                    if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_BACKUP_DIRECTORY_UNSAFE')
                    self.attest(configuration, child, read_only=read_only)
                    yield configuration, child
                finally: os.close(child)
        finally: os.close(descriptor)

    def metadata(self, record, original):
        return {'schemaVersion': 1, 'storageId': STORAGE, 'operationId': self.f.identity(record['operationId']),
            'owner': record['owner'], 'rootId': record['rootId'], 'path': record['path'],
            'sourceVersion': record['expectedVersion'], 'sourceIdentity': self.f.publication_identity(original),
            'sizeBytes': original.st_size, 'sha256': record['originalSha256']}

    def verify_data(self, target, operation, expected):
        descriptor = self.f.open_at(target, operation + '.data', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            info = self.f.regular(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size != expected['sizeBytes']:
                self.reject('FILES_BACKUP_CONTENT_INVALID')
        finally: os.close(descriptor)
        if self.f.checksum(target, operation + '.data') != expected['sha256']: self.reject('FILES_BACKUP_CONTENT_INVALID')

    def check_capacity(self, target, configuration, size):
        objects = os.listdir(target)
        if len(objects) >= configuration['maxVersions'] * 3 or sum(name.endswith('.json') for name in objects) >= configuration['maxVersions']:
            self.reject('FILES_BACKUP_HISTORY_LIMIT')
        usage = os.fstatvfs(target)
        reserve = max(configuration['reserveBytes'], usage.f_blocks * usage.f_frsize * configuration['reservePercent'] // 100) + 4 * self.f.MAX_BYTES
        if usage.f_bavail * usage.f_frsize < size + reserve: self.reject('FILES_BACKUP_CAPACITY_REQUIRED')

    def ensure(self, record, parent, leaf, capture_kind='Overwrite', *, source_binding=None):
        """No target namespace publication until this returns a verified copy.

        Preparing without a complete data object is explicit incomplete state,
        not permission to truncate/re-copy it or overwrite the source again.
        """
        # A trusted in-process adapter may bind a container source. It changes
        # source identity only, never HDD policy, paths, capacity or durability.
        version = self.f.version if source_binding is None else source_binding.version
        validate = (lambda: None) if source_binding is None else source_binding.validate
        metadata = self.metadata if source_binding is None else source_binding.metadata
        if capture_kind not in ('Overwrite', 'Trash') or source_binding is not None and capture_kind != 'Overwrite' or \
                (source_binding is None) != (self.namespace is None):
            self.reject('FILES_BACKUP_METADATA_UNSAFE')
        validate()
        original = self.f.current(parent, leaf)
        if original is None or version(original) != record['expectedVersion'] or self.f.publication_identity(original) != record['originalIdentity']:
            self.reject('FILES_VERSION_CONFLICT')
        expected = metadata(record, original); operation = expected['operationId']
        with self.target() as (configuration, target):
            try: saved = self.read_json(target, operation + '.json')
            except FileNotFoundError: saved = None
            if saved is not None:
                if saved.get('captureKind', 'Overwrite') != capture_kind: self.reject('FILES_BACKUP_IDEMPOTENCY_CONFLICT')
                if any(saved.get(key) != value for key, value in expected.items()) or saved.get('state') not in ('Preparing', 'Verified') or type(saved.get('createdAtUnixMs')) is not int or not 0 <= saved['createdAtUnixMs'] <= 253402300799999 or type(saved.get('differentSourceDevice')) is not bool:
                    self.reject('FILES_BACKUP_IDEMPOTENCY_CONFLICT')
                try: self.verify_data(target, operation, expected)
                except FileNotFoundError: self.reject('FILES_BACKUP_INCOMPLETE')
                validate()
                # Observed bytes after restart can complete metadata, never copy
                # or publish the source as part of recovery.
                if saved['state'] != 'Verified':
                    saved['state'] = 'Verified'; self.f.save_receipt(target, operation, saved)
                return self.public(saved)
            self.check_capacity(target, configuration, original.st_size)
            source = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            pending = operation + '.partial'; output = None
            try:
                before = self.f.regular(source)
                if version(before) != record['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                created = time.time_ns() // 1000000
                if not 0 <= created <= 253402300799999: self.reject('FILES_BACKUP_METADATA_UNSAFE')
                try: output = os.open(pending, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=target)
                except FileExistsError: self.reject('FILES_BACKUP_INCOMPLETE')
                saved = {**expected, 'state': 'Preparing', 'createdAtUnixMs': created, 'captureKind': capture_kind,
                    'captureState': 'Pending' if capture_kind == 'Trash' else 'Available',
                    'differentSourceDevice': before.st_dev != os.fstat(target).st_dev}
                self.f.save_receipt(target, operation, saved)
                checksum = hashlib.sha256(); count = 0
                while count < before.st_size:
                    validate()
                    data = os.read(source, min(self.f.CHUNK, before.st_size - count))
                    if not data: self.reject('FILES_READ_CHANGED')
                    checksum.update(data); count += len(data); view = memoryview(data)
                    while view:
                        written = os.write(output, view)
                        if written <= 0: self.reject('FILES_BACKUP_WRITE_FAILED')
                        view = view[written:]
                current = self.f.current(parent, leaf)
                if checksum.hexdigest().upper() != expected['sha256'] or version(self.f.regular(source)) != record['expectedVersion'] or current is None or version(current) != record['expectedVersion']:
                    self.reject('FILES_READ_CHANGED')
                os.fsync(output); os.close(output); output = None
                self.attest(configuration, target)
                self.f.exchange(target, pending, target, operation + '.data', 1)
                os.fsync(target)
                self.verify_data(target, operation, expected)
                validate()
                saved['state'] = 'Verified'; self.f.save_receipt(target, operation, saved)
                return self.public(saved)
            finally:
                if output is not None: os.close(output)
                os.close(source)

    def public(self, record):
        return {key: record[key] for key in ('operationId', 'storageId', 'sourceVersion', 'sizeBytes', 'sha256', 'createdAtUnixMs', 'differentSourceDevice')}
