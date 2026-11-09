#!/usr/bin/env python3
"""Host-side workspace directory allocation. The caller must authorize the registered workspace first."""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat

CONFIG = Path('/etc/whaledeck/development-storage.json')
BASE = Path('/var/lib/whaledeck-development')
MOUNTINFO = Path('/proc/self/mountinfo')
FDINFO = Path('/proc/self/fdinfo')
UDEV = Path('/run/udev/data')
SYS_DEV = Path('/sys/dev/block')
SYS_DEVICES = Path('/sys/devices')
NVME_CLASS = Path('/sys/class/nvme')
DIR = os.O_RDONLY | os.O_DIRECTORY
READ = os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW
MARKER = 'allocation.json'
PAYLOAD = 'data'
UUID = r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}'
KEY = r'[a-z0-9]+(?:-[a-z0-9]+)*'
DIGEST = r'[a-f0-9]{64}'


class WorkspaceStorage:
    def __init__(self, files, *, sandboxed=False, quota=None):
        self.files = files
        self.sandboxed = sandboxed
        self.quota = quota

    def reject(self, reason):
        raise self.files.Rejected('WORKSPACE_STORAGE_' + reason)

    @contextlib.contextmanager
    def opened(self, parent, name, flags, *, cross_mount=False):
        fd = self.files.open_at(parent, name, flags, cross_mount=cross_mount)
        try:
            yield fd
        finally:
            os.close(fd)

    @contextlib.contextmanager
    def absolute(self, path):
        with self.slash() as slash:
            with self.opened(slash, str(path).lstrip('/') or '.', DIR, cross_mount=True) as fd:
                yield fd

    @staticmethod
    @contextlib.contextmanager
    def slash():
        fd = os.open('/', DIR | os.O_CLOEXEC)
        try:
            yield fd
        finally:
            os.close(fd)

    def directory(self, fd, *, private=False):
        info = os.fstat(fd)
        if info.st_uid != 0 or not stat.S_ISDIR(info.st_mode) or (
                stat.S_IMODE(info.st_mode) != 0o700 if private else info.st_mode & 0o022):
            self.reject('DIRECTORY_UNSAFE')
        return info

    def read_file(self, fd, maximum, *, private=False):
        info = os.fstat(fd)
        mode = stat.S_IMODE(info.st_mode)
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                or (mode not in (0o400, 0o600) if private else mode & 0o7022)
                or not 0 < info.st_size <= maximum):
            self.reject('METADATA_UNSAFE')
        with os.fdopen(os.dup(fd), 'rb') as stream:
            raw = stream.read(maximum + 1)
        if len(raw) != info.st_size:
            self.reject('METADATA_UNSAFE')
        return raw

    def read_json(self, parent, name, maximum, *, expected=None, sync=False):
        with self.opened(parent, name, READ) as fd:
            try:
                value = json.loads(self.read_file(fd, maximum, private=True), object_pairs_hook=self.files.unique_object)
            except (ValueError, UnicodeError):
                self.reject('METADATA_INVALID')
            if not isinstance(value, dict):
                self.reject('METADATA_INVALID')
            if expected is not None and self.canonical(value) != self.canonical(expected):
                self.reject('DIRECTORY_CONFLICT')
            if sync:
                os.fsync(fd)
            return value

    def configuration(self, root_id):
        with self.absolute(CONFIG.parent) as config_dir:
            self.directory(config_dir)
            config = self.read_json(config_dir, CONFIG.name, 16384)
        if (set(config) != {'schemaVersion', 'roots'} or type(config['schemaVersion']) is not int
                or config['schemaVersion'] != (2 if self.quota else 1) or not isinstance(config['roots'], dict)
                or not 1 <= len(config['roots']) <= 8):
            self.reject('CONFIGURATION_INVALID')
        ranges = {}
        for key, root in config['roots'].items():
            self.validate_root(key, root)
            if self.quota:
                first, last = root['projectIdStart'], root['projectIdEnd']
                siblings = ranges.setdefault(root['filesystemUuid'], [])
                if any(first <= end and last >= start for start, end in siblings):
                    self.reject('QUOTA_RANGES_OVERLAP')
                siblings.append((first, last))
        if root_id not in config['roots']:
            self.reject('ROOT_NOT_APPROVED')
        return config['roots'][root_id]

    def validate_root(self, root_id, root):
        fields = {'mountPoint', 'filesystemUuid', 'reserveMiB'}
        if self.quota:
            fields.update(('projectIdStart', 'projectIdEnd'))
        if (not isinstance(root_id, str) or len(root_id) > 48 or not re.fullmatch(KEY, root_id)
                or not isinstance(root, dict)
                or set(root) != fields
                or not isinstance(root['filesystemUuid'], str) or not re.fullmatch(UUID, root['filesystemUuid'])
                or type(root['reserveMiB']) is not int or not 512 <= root['reserveMiB'] <= 1024 * 1024):
            self.reject('CONFIGURATION_INVALID')
        if self.quota:
            first, last = root['projectIdStart'], root['projectIdEnd']
            if type(first) is not int or type(last) is not int or not 1 <= first <= last <= 0x7fffffff or last - first < 31:
                self.reject('CONFIGURATION_INVALID')
        mount = root['mountPoint']
        if (not isinstance(mount, str) or not mount.startswith('/') or len(mount) > 4096
                or mount.startswith('//') or os.path.normpath(mount) != mount
                or any(ord(c) < 32 for c in mount) or not (BASE / root_id).is_relative_to(Path(mount))):
            self.reject('CONFIGURATION_INVALID')

    @staticmethod
    def mount_id(fd):
        with (FDINFO / str(fd)).open(encoding='ascii') as stream:
            content = stream.read(4097)
        if len(content) > 4096:
            return None
        lines = content.splitlines()
        ids = [line.split()[1] for line in lines if re.fullmatch(r'mnt_id:\s+[0-9]+', line)]
        return ids[0] if len(ids) == 1 else None

    def mount_entry(self, fd):
        info = self.directory(fd)
        dev = f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}'
        mount_id = self.mount_id(fd)
        with MOUNTINFO.open(encoding='utf-8') as stream:
            entries = stream.read(1024 * 1024 + 1)
        if len(entries) > 1024 * 1024 or mount_id is None:
            self.reject('MOUNT_MISMATCH')
        found = [entry.split() for entry in entries.splitlines() if entry.startswith(mount_id + ' ')]
        if len(found) != 1 or len(found[0]) < 10:
            self.reject('MOUNT_MISMATCH')
        row = found[0]
        mount = re.sub(r'\\([0-7]{3})', lambda match: chr(int(match[1], 8)), row[4])
        if row[2] != dev or '-' not in row:
            self.reject('MOUNT_MISMATCH')
        split = row.index('-')
        if split + 3 >= len(row) or row[split + 1] not in ('ext4', 'xfs'):
            self.reject('FILESYSTEM_UNSUPPORTED')
        source = re.sub(r'\\([0-7]{3})', lambda match: chr(int(match[1], 8)), row[3])
        return mount_id, row[1], dev, source, mount

    def require_writable(self, fd):
        if os.fstatvfs(fd).f_flag & os.ST_RDONLY:
            self.reject('READ_ONLY')

    def attest_mount(self, fd, root, *, writable=True):
        mount_id, _, dev, source, mount = self.mount_entry(fd)
        if source != '/' or mount != root['mountPoint']:
            self.reject('MOUNT_MISMATCH')
        if writable:
            self.require_writable(fd)
        with self.absolute(UDEV) as udev:
            self.directory(udev)
            with self.opened(udev, 'b' + dev, READ) as descriptor:
                records = self.read_file(descriptor, 65536).decode('utf-8').splitlines()
        uuids = [value for value in records if value.startswith('E:ID_FS_UUID=')]
        if uuids != ['E:ID_FS_UUID=' + root['filesystemUuid']] or 'E:ID_FS_USAGE=filesystem' not in records:
            self.reject('DEVICE_MISMATCH')
        self.require_nvme(dev)
        return mount_id

    def require_nvme(self, dev):
        device = (SYS_DEV / dev).resolve(strict=True)
        if not device.is_relative_to(SYS_DEVICES) or not re.fullmatch(r'nvme[0-9]+n[0-9]+(?:p[0-9]+)?', device.name):
            self.reject('NVME_REQUIRED')
        namespace = device.parent if (device / 'partition').is_file() else device
        controller = namespace.parent
        if (not re.fullmatch(r'nvme[0-9]+n[0-9]+', namespace.name)
                or not re.fullmatch(r'nvme[0-9]+', controller.name)
                or (controller / 'subsystem').resolve(strict=True) != NVME_CLASS
                or (controller / 'transport').read_bytes() != b'pcie\n'
                or (namespace / 'queue' / 'rotational').read_bytes() != b'0\n'):
            self.reject('NVME_REQUIRED')

    @contextlib.contextmanager
    def namespace(self, root_id, config):
        mount = Path(config['mountPoint'])
        with self.absolute(mount) as mounted, contextlib.ExitStack() as opened:
            scoped_bind = self.sandboxed and mount != BASE
            identity = self.attest_mount(mounted, config, writable=not scoped_bind)
            parent = mounted
            relative = (BASE / root_id).relative_to(mount)
            if self.sandboxed and mount == BASE:
                self.directory(mounted, private=True)
            if scoped_bind:
                # systemd exposes only BASE as writable. Prove that this
                # overmount is the exact subtree of the approved filesystem.
                parent = opened.enter_context(self.absolute(BASE))
                self.directory(parent, private=True)
                bound_id, parent_id, device, source, target = self.mount_entry(parent)
                host_device = os.fstat(mounted).st_dev
                expected_device = f'{os.major(host_device)}:{os.minor(host_device)}'
                if (bound_id == identity or parent_id != identity or device != expected_device
                        or source != '/' + BASE.relative_to(mount).as_posix() or target != str(BASE)):
                    self.reject('MOUNT_MISMATCH')
                self.require_writable(parent)
                identity, relative = bound_id, Path(root_id)
            for piece in relative.parts:
                parent = opened.enter_context(self.opened(parent, piece, DIR))
                self.directory(parent)
            self.directory(parent, private=True)
            if self.mount_id(parent) != identity:
                self.reject('MOUNT_MISMATCH')
            yield parent

    def recheck(self, parent, root_id, config):
        if self.configuration(root_id) != config:
            self.reject('CONFIGURATION_CHANGED')
        with self.namespace(root_id, config) as live:
            if not os.path.samestat(os.fstat(live), os.fstat(parent)) or self.mount_id(live) != self.mount_id(parent):
                self.reject('ROOT_CHANGED')

    def receipt(self, parent, child, request, config):
        return dict(request, filesystemUuid=config['filesystemUuid'], rootInode=os.fstat(parent).st_ino,
                    directoryInode=os.fstat(child).st_ino)

    def read_existing(self, parent, name, request, config, *, pending=False, sync=False):
        try:
            fd = self.files.open_at(parent, name, DIR)
        except FileNotFoundError:
            return None
        try:
            self.directory(fd, private=True)
            expected = self.receipt(parent, fd, request, config)
            try:
                self.read_json(fd, MARKER, 4096, expected=expected, sync=sync)
            except FileNotFoundError:
                self.reject('STAGING_UNKNOWN' if pending else 'DIRECTORY_CONFLICT')
            if sync:
                os.fsync(fd)
            return expected
        finally:
            os.close(fd)

    @staticmethod
    def canonical(value):
        return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('ascii')

    def write_new(self, parent, name, proof):
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(self.canonical(proof))
            stream.flush()
            os.fsync(stream.fileno())
        os.fsync(parent)

    def stage(self, parent, name, request, config):
        os.mkdir(name, 0o700, dir_fd=parent)
        with self.opened(parent, name, DIR) as child:
            self.directory(child, private=True)
            proof = self.receipt(parent, child, request, config)
            self.write_new(child, MARKER, proof)
        os.fsync(parent)
        return proof

    def quota_claim(self, parent, payload, proof, config, *, prepare):
        first, last = config['projectIdStart'], config['projectIdEnd']
        count = last - first + 1
        offset = int.from_bytes(hashlib.sha256(proof['workspaceId'].encode('ascii')).digest()[:8], 'big')
        candidates = [first + (offset + index) % count for index in range(32)]
        free = []
        owned = None
        payload_inode = os.fstat(payload).st_ino
        for project in candidates:
            name = f'.quota-{project}.json'
            expected = dict(proof, projectId=project, payloadInode=payload_inode)
            try:
                claim = self.read_json(parent, name, 4096)
            except FileNotFoundError:
                free.append((name, expected))
                continue
            if claim.get('workspaceId') == proof['workspaceId']:
                if self.canonical(claim) != self.canonical(expected) or owned is not None:
                    self.reject('QUOTA_CLAIM_CONFLICT')
                owned = name, expected
        if owned is not None:
            name, expected = owned
            if prepare:
                self.read_json(parent, name, 4096, expected=expected, sync=True)
                os.fsync(parent)
            return expected
        if not prepare:
            self.reject('QUOTA_PROOF_MISSING')
        for name, expected in free:
            try:
                self.quota.require_unused(payload, expected['projectId'])
            except self.quota.QuotaError as error:
                if str(error) == 'QUOTA_ID_IN_USE':
                    continue
                raise
            self.write_new(parent, name, expected)
            return expected
        self.reject('QUOTA_PROJECT_IDS_BUSY')

    @contextlib.contextmanager
    def quota_payload(self, parent, name, proof, config, *, prepare=False):
        with self.opened(parent, name, DIR) as child:
            if self.directory(child, private=True).st_ino != proof['directoryInode']:
                self.reject('DIRECTORY_CONFLICT')
            if prepare:
                try:
                    os.mkdir(PAYLOAD, 0o700, dir_fd=child)
                    os.fsync(child)
                except FileExistsError:
                    pass
            try:
                payload = self.files.open_at(child, PAYLOAD, DIR)
            except FileNotFoundError:
                self.reject('QUOTA_PROOF_MISSING')
            try:
                self.directory(payload, private=True)
                bound = self.quota_claim(parent, payload, proof, config, prepare=prepare)
                if prepare:
                    self.quota.attach(payload, bound['projectId'], proof['diskMiB'])
                else:
                    self.quota.verify(payload, bound['projectId'], proof['diskMiB'])
                yield payload, bound
            finally:
                os.close(payload)

    def quota_proof(self, parent, name, proof, config, *, prepare=False):
        with self.quota_payload(parent, name, proof, config, prepare=prepare) as (_, bound):
            return bound

    def validate_request(self, request):
        if not isinstance(request, dict) or set(request) != {'schemaVersion', 'rootId', 'workspaceId', 'environmentSha256', 'diskMiB'}:
            self.reject('REQUEST_INVALID')
        root_id, workspace = request['rootId'], request['workspaceId']
        if (type(request['schemaVersion']) is not int or request['schemaVersion'] != 1
                or not isinstance(root_id, str) or len(root_id) > 48 or not re.fullmatch(KEY, root_id)
                or not isinstance(workspace, str) or not re.fullmatch(UUID, workspace)
                or workspace == '00000000-0000-0000-0000-000000000000'
                or not isinstance(request['environmentSha256'], str) or not re.fullmatch(DIGEST, request['environmentSha256'])
                or type(request['diskMiB']) is not int or not 256 <= request['diskMiB'] <= 1024 * 1024):
            self.reject('REQUEST_INVALID')
        return root_id, workspace

    @contextlib.contextmanager
    def locked_root(self, root_id, *, exclusive):
        config = self.configuration(root_id)
        with self.namespace(root_id, config) as parent:
            try:
                lock = fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH
                fcntl.flock(parent, lock | fcntl.LOCK_NB)
            except BlockingIOError:
                self.reject('BUSY')
            self.recheck(parent, root_id, config)
            yield parent, config

    @contextlib.contextmanager
    def attested_payload(self, request):
        """Pin a published payload and hold the shared root lock while it is used."""
        if not self.quota:
            self.reject('QUOTA_UNAVAILABLE')
        root_id, workspace = self.validate_request(request)
        with self.locked_root(root_id, exclusive=False) as (parent, config):
            proof = self.read_existing(parent, workspace, request, config)
            if proof is None:
                self.reject('NOT_PUBLISHED')
            with self.quota_payload(parent, workspace, proof, config) as (payload, bound):
                self.recheck(parent, root_id, config)
                if self.read_existing(parent, workspace, request, config) != proof:
                    self.reject('DIRECTORY_CONFLICT')
                with self.quota_payload(parent, workspace, proof, config) as (current, current_bound):
                    if current_bound != bound or not os.path.samestat(os.fstat(current), os.fstat(payload)):
                        self.reject('QUOTA_BINDING_CHANGED')
                yield payload, bound

    def allocate(self, request):
        root_id, workspace = self.validate_request(request)
        pending = '.pending-' + workspace
        with self.locked_root(root_id, exclusive=True) as (parent, config):
            current = self.read_existing(parent, workspace, request, config, sync=True)
            if current is not None:
                # A prior rename may have happened just before a failed fsync.
                os.fsync(parent)
                self.recheck(parent, root_id, config)
                return self.quota_proof(parent, workspace, current, config) if self.quota else current

            if self.quota:
                self.quota.require_enforcing(parent)
            available = os.fstatvfs(parent)
            if available.f_bavail * available.f_frsize < (request['diskMiB'] + config['reserveMiB']) * 1024**2:
                self.reject('CAPACITY_REQUIRED')
            proof = self.read_existing(parent, pending, request, config, pending=True)
            if proof is None:
                proof = self.stage(parent, pending, request, config)
            self.recheck(parent, root_id, config)
            if self.read_existing(parent, pending, request, config, pending=True, sync=True) != proof:
                self.reject('STAGING_UNKNOWN')
            if self.quota:
                self.quota_proof(parent, pending, proof, config, prepare=True)
                self.recheck(parent, root_id, config)
                self.quota_proof(parent, pending, proof, config)
            self.files.exchange(parent, pending, parent, workspace, 1)  # RENAME_NOREPLACE
            os.fsync(parent)
            self.recheck(parent, root_id, config)
            if self.read_existing(parent, workspace, request, config) != proof:
                self.reject('PUBLICATION_UNKNOWN')
            return self.quota_proof(parent, workspace, proof, config) if self.quota else proof
