#!/usr/bin/env python3
"""Private history/trash engine. No CLI, shell, user-selected UID or host path.

This module is not a public write surface. The later dedicated Agent adapter
must bind each method to its capability and permanent, human-owned SQL intent.
Recovery observes journal-bound objects; it never repeats a missing rename,
publication or unlink. Uncertain objects remain private for explicit handling.
"""
import contextlib
import fcntl
import hashlib
import json
import os
import re
import stat


class FileArchives:
    COMMAND = {'kind', 'rootId', 'path', 'versionId', 'expectedVersion'}
    PLAN = {'command', 'parentIdentity', 'sourceIdentity', 'sourceSha256', 'archiveIdentity', 'restoreMetadata', 'sizeBytes', 'sha256', 'fingerprint'}
    STATES = ('Available', 'Pending', 'Trashed', 'Restored', 'Canceled')

    def __init__(self, files, backups):
        self.f = files; self.b = backups

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def digest(value):
        return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()

    def hash(self, value):
        if not isinstance(value, str) or not re.fullmatch('[A-F0-9]{64}', value): self.reject('FILES_ARCHIVE_INVALID')

    def authorize(self, root, path, writing=False, empty=False):
        entry = self.f.roots().get(root) if isinstance(root, str) else None
        required = {'files-list', 'files-download', 'files-history'} | ({'files-mutate', 'files-upload', 'files-archive'} if writing else set())
        if entry is None or not required.issubset(entry['allowedActions']): self.reject('FILES_ROOT_FORBIDDEN')
        self.f.relative(path, empty=empty)
        if self.f.protected(path): self.reject('FILES_PROTECTED')

    def command(self, value, authorize=True):
        if not isinstance(value, dict) or set(value) != self.COMMAND or any(not isinstance(item, str) for item in value.values()): self.reject('FILES_ARCHIVE_INVALID')
        if value['kind'] not in ('trash', 'restore', 'purge'): self.reject('FILES_ARCHIVE_INVALID')
        if not re.fullmatch('[a-z0-9.-]{1,64}', value['rootId']): self.reject('FILES_ARCHIVE_INVALID')
        self.f.relative(value['path'])
        if authorize and self.f.protected(value['path']): self.reject('FILES_PROTECTED')
        if authorize: self.authorize(value['rootId'], value['path'], writing=True)
        if value['kind'] == 'trash':
            if value['versionId'] != '': self.reject('FILES_ARCHIVE_INVALID')
            self.hash(value['expectedVersion'])
        else:
            self.f.identity(value['versionId'])
            if value['kind'] == 'purge':
                if value['expectedVersion'] != 'ABSENT': self.reject('FILES_ARCHIVE_INVALID')
            elif value['expectedVersion'] != 'ABSENT': self.hash(value['expectedVersion'])

    def metadata(self, target, version):
        self.f.identity(version)
        value = self.b.read_json(target, version + '.json')
        fields = {'schemaVersion', 'storageId', 'operationId', 'owner', 'rootId', 'path', 'sourceVersion', 'sourceIdentity', 'sizeBytes', 'sha256', 'state', 'createdAtUnixMs', 'differentSourceDevice'}
        if not fields.issubset(value) or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1 or value['operationId'] != version or value['storageId'] != 'database-platform.backup.hdd': self.reject('FILES_ARCHIVE_METADATA_INVALID')
        for field in ('owner', 'sourceVersion', 'sha256'): self.hash(value[field])
        self.f.relative(value['path'])
        if not isinstance(value['rootId'], str) or type(value['sizeBytes']) is not int or not 0 <= value['sizeBytes'] <= self.f.MAX_BYTES or type(value['createdAtUnixMs']) is not int or not 0 <= value['createdAtUnixMs'] <= 253402300799999 or type(value['differentSourceDevice']) is not bool:
            self.reject('FILES_ARCHIVE_METADATA_INVALID')
        identity = value['sourceIdentity']
        if not self.valid_identity(identity) or identity[6] != value['sizeBytes']:
            self.reject('FILES_ARCHIVE_METADATA_INVALID')
        if value['state'] not in ('Preparing', 'Verified') or value.get('captureKind', 'Overwrite') not in ('Overwrite', 'Trash') or value.get('captureState', 'Available') not in self.STATES:
            self.reject('FILES_ARCHIVE_METADATA_INVALID')
        if value.get('captureKind', 'Overwrite') == 'Overwrite' and value.get('captureState', 'Available') != 'Available' or value.get('captureKind') == 'Trash' and value.get('captureState', 'Available') == 'Available':
            self.reject('FILES_ARCHIVE_METADATA_INVALID')
        return value

    def valid_identity(self, identity):
        return isinstance(identity, list) and len(identity) == 8 and all(type(item) is int for item in identity) and all(0 <= item <= 18446744073709551615 for item in identity[:7]) and identity[1] > 0 and identity[2] <= 65535 and stat.S_ISREG(identity[2]) and identity[3] < 4294967295 and identity[4] < 4294967295 and identity[5] == 1 and identity[6] <= self.f.MAX_BYTES and -9223372036854775808 <= identity[7] <= 9223372036854775807

    def list(self, request):
        if not isinstance(request, dict) or set(request) != {'rootId', 'path', 'after', 'take', 'expectedVersion'} or type(request['take']) is not int or not 1 <= request['take'] <= 50:
            self.reject('FILES_ARCHIVE_LIST_INVALID')
        self.authorize(request['rootId'], request['path'], empty=True)
        if request['after']: self.f.identity(request['after'])
        if request['expectedVersion']: self.hash(request['expectedVersion'])
        with self.b.target() as (_, target):
            before = self.f.version(os.fstat(target))
            if request['expectedVersion'] and request['expectedVersion'] != before: self.reject('FILES_VERSION_CONFLICT')
            names = os.listdir(target)
            if len(names) > 30000: self.reject('FILES_BACKUP_HISTORY_LIMIT')
            names = sorted(name[:-5] for name in names if re.fullmatch('[a-f0-9]{32}\\.json', name) and name[:-5] > request['after'])
            entries = []; scanned = invalid = encoded = 0; cursor = ''; complete = True
            for version in names:
                if scanned >= 500 or len(entries) >= request['take']:
                    complete = False; break
                scanned += 1; cursor = version
                try:
                    value = self.metadata(target, version)
                    if value['rootId'] != request['rootId'] or request['path'] and value['path'] != request['path']: continue
                    if self.f.protected(value['path']): continue
                    self.authorize(value['rootId'], value['path'])
                    availability = 'Incomplete' if value['state'] != 'Verified' else 'Present'
                    try:
                        descriptor = self.f.open_at(target, version + '.data', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                        try:
                            info = self.f.regular(descriptor)
                            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size != value['sizeBytes']: availability = 'Invalid'
                        finally: os.close(descriptor)
                    except FileNotFoundError: availability = 'Missing' if value['state'] == 'Verified' else 'Incomplete'
                    except (OSError, self.f.Rejected): availability = 'Invalid'
                    entry = {'versionId': version, 'rootId': value['rootId'], 'path': value['path'], 'sourceVersion': value['sourceVersion'],
                        'sizeBytes': value['sizeBytes'], 'sha256': value['sha256'], 'createdAtUnixMs': value['createdAtUnixMs'],
                        'captureKind': value.get('captureKind', 'Overwrite'), 'captureState': value.get('captureState', 'Available'),
                        'availability': availability, 'differentSourceDevice': value['differentSourceDevice']}
                    length = len(json.dumps(entry, ensure_ascii=False, separators=(',', ':')).encode()) + 1
                    if encoded + length > self.f.LIST_ENTRIES_BYTES:
                        # Resume before the non-included row, never skip it.
                        scanned -= 1; cursor = entries[-1]['versionId'] if entries else request['after']; complete = False; break
                    entries.append(entry); encoded += length
                except (OSError, self.f.Rejected, ValueError, KeyError, TypeError): invalid += 1
            if self.f.version(os.fstat(target)) != before: self.reject('FILES_VERSION_CONFLICT')
            return {'entries': entries, 'nextCursor': '' if complete else cursor, 'version': before, 'scanned': scanned, 'invalidRecords': invalid, 'complete': complete}

    def plan(self, command):
        self.command(command)
        current_identity = parent_identity = None; current_hash = ''; archive_identity = None; restore_metadata = None
        size = 0; checksum = ''
        if command['kind'] != 'purge':
            with self.f.directory(command, 'mutate') as (_, parent, leaf):
                info = self.f.current(parent, leaf)
                if (self.f.version(info) if info else 'ABSENT') != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                parent_info = os.fstat(parent); parent_identity = [parent_info.st_dev, parent_info.st_ino]
                if info is not None:
                    current_identity = self.f.publication_identity(info); current_hash = self.f.checksum(parent, leaf)
                    latest = self.f.current(parent, leaf)
                    if latest is None or self.f.version(latest) != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                elif command['kind'] == 'trash': self.reject('FILES_NOT_FOUND')
        if command['kind'] == 'trash':
            size = current_identity[6]; checksum = current_hash
            with self.b.target() as (configuration, target): self.b.check_capacity(target, configuration, size)
        else:
            with self.b.target() as (_, target):
                value = self.metadata(target, command['versionId'])
                if value['rootId'] != command['rootId'] or value['path'] != command['path']: self.reject('FILES_ARCHIVE_NOT_FOUND')
                if value['state'] != 'Verified' or value.get('captureState', 'Available') == 'Pending': self.reject('FILES_BACKUP_INCOMPLETE')
                self.b.verify_data(target, command['versionId'], value)
                info = self.f.current(target, command['versionId'] + '.data')
                archive_identity = {'metadataHash': self.digest(value), 'dataIdentity': self.f.publication_identity(info)}
                restore_metadata = value['sourceIdentity']; size = value['sizeBytes']; checksum = value['sha256']
                if command['kind'] == 'restore' and current_identity is not None:
                    self.b.check_capacity(target, self.b.configuration(), current_identity[6])
        result = {'command': command, 'parentIdentity': parent_identity, 'sourceIdentity': current_identity, 'sourceSha256': current_hash,
            'archiveIdentity': archive_identity, 'restoreMetadata': restore_metadata, 'sizeBytes': size, 'sha256': checksum}
        result['fingerprint'] = self.digest({'domain': 'whaledeck-file-archive-v1', 'plan': result})
        return result

    def request(self, request, authorize=True):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'} or not isinstance(request['plan'], dict) or set(request['plan']) != self.PLAN:
            self.reject('FILES_ARCHIVE_INVALID')
        self.f.identity(request['operationId']); self.hash(request['owner']); self.command(request['plan']['command'], authorize=authorize)
        plan = request['plan']; kind = plan['command']['kind']
        if type(plan['sizeBytes']) is not int or not 0 <= plan['sizeBytes'] <= self.f.MAX_BYTES: self.reject('FILES_PLAN_INVALID')
        self.hash(plan['sha256'])
        parent = plan['parentIdentity']
        if kind == 'purge':
            if parent is not None or plan['sourceIdentity'] is not None or plan['sourceSha256'] != '': self.reject('FILES_PLAN_INVALID')
        else:
            if not isinstance(parent, list) or len(parent) != 2 or any(type(item) is not int or not 0 <= item <= 18446744073709551615 for item in parent) or parent[1] == 0: self.reject('FILES_PLAN_INVALID')
            if plan['command']['expectedVersion'] == 'ABSENT':
                if plan['sourceIdentity'] is not None or plan['sourceSha256'] != '': self.reject('FILES_PLAN_INVALID')
            else:
                if not self.valid_identity(plan['sourceIdentity']): self.reject('FILES_PLAN_INVALID')
                self.hash(plan['sourceSha256'])
        if kind == 'trash':
            if plan['archiveIdentity'] is not None or plan['restoreMetadata'] is not None or plan['sizeBytes'] != plan['sourceIdentity'][6] or plan['sha256'] != plan['sourceSha256']: self.reject('FILES_PLAN_INVALID')
        else:
            archived = plan['archiveIdentity']
            if not isinstance(archived, dict) or set(archived) != {'metadataHash', 'dataIdentity'} or not self.valid_identity(archived['dataIdentity']) or not self.valid_identity(plan['restoreMetadata']) or plan['restoreMetadata'][6] != plan['sizeBytes'] or archived['dataIdentity'][6] != plan['sizeBytes']:
                self.reject('FILES_PLAN_INVALID')
            self.hash(archived['metadataHash'])
        unsigned = {key: value for key, value in request['plan'].items() if key != 'fingerprint'}
        if request['plan']['fingerprint'] != self.digest({'domain': 'whaledeck-file-archive-v1', 'plan': unsigned}): self.reject('FILES_PLAN_INVALID')
        if request['operationId'] == request['plan']['command']['versionId']: self.reject('FILES_ARCHIVE_INVALID')

    @contextlib.contextmanager
    def state(self, operation, owner, create=False):
        self.f.identity(operation); self.hash(owner)
        base = self.f.trusted_directory(self.f.STATE); folder = lock = None
        try:
            try: os.mkdir('archives', 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base, 'archives', os.O_RDONLY | os.O_DIRECTORY)
            info = os.fstat(folder)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            try: record = self.b.read_json(folder, operation + '.json', maximum=32768)
            except FileNotFoundError: record = None
            if record is None and not create or record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            if len(os.listdir(folder)) >= 20000 and record is None: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600, dir_fd=folder)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o077: self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            try: record = self.b.read_json(folder, operation + '.json', maximum=32768)
            except FileNotFoundError: record = None
            if record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            with self.f.namespace_lock(): yield folder, record
        finally:
            if lock is not None: os.close(lock)
            if folder is not None: os.close(folder)
            os.close(base)

    def save(self, folder, record):
        if len(json.dumps(record).encode()) > 32768: self.reject('FILES_RECEIPT_LIMIT')
        self.f.save_receipt(folder, record['operationId'], record)

    def change_capture(self, version, expected, state):
        with self.b.target() as (_, target):
            value = self.metadata(target, version)
            for key in ('operationId', 'owner', 'rootId', 'path', 'sourceVersion', 'sizeBytes', 'sha256'):
                if value[key] != expected[key]: self.reject('FILES_BACKUP_IDEMPOTENCY_CONFLICT')
            if value.get('captureKind', 'Overwrite') != 'Trash': return
            current = value.get('captureState', 'Available')
            if current == state: return
            if (current, state) not in (('Pending', 'Trashed'), ('Pending', 'Canceled'), ('Trashed', 'Restored')): self.reject('FILES_BACKUP_INCOMPLETE')
            self.b.verify_data(target, version, value)
            value['captureState'] = state; self.f.save_receipt(target, version, value)

    def apply(self, request):
        self.request(request)
        with self.state(request['operationId'], request['owner'], create=True) as (folder, record):
            if record is not None:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record); return self.public(record)
            plan = self.plan(request['plan']['command'])
            if plan != request['plan']: self.reject('FILES_VERSION_CONFLICT')
            record = {'operationId': request['operationId'], 'owner': request['owner'], 'plan': plan, 'state': 'Preparing'}
            self.save(folder, record)
            try:
                if plan['command']['kind'] == 'trash': self.trash(folder, record)
                elif plan['command']['kind'] == 'restore': self.restore(folder, record)
                else: self.purge(folder, record)
            except BaseException:
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(folder, record)
                raise
            return self.public(record)

    def original_record(self, record):
        plan = record['plan']; command = plan['command']
        return {'operationId': record['operationId'], 'owner': record['owner'], 'rootId': command['rootId'], 'path': command['path'],
            'expectedVersion': command['expectedVersion'], 'originalIdentity': plan['sourceIdentity'], 'originalSha256': plan['sourceSha256']}

    def parent(self, parent, plan):
        info = os.fstat(parent)
        if [info.st_dev, info.st_ino] != plan['parentIdentity']: self.reject('FILES_PARENT_CHANGED')
        # Also reopen the registered lexical path. A pinned FD alone does not
        # establish that an external parent rename still names this directory.
        with self.f.directory(plan['command'], 'mutate') as (_, current, _):
            info = os.fstat(current)
            if [info.st_dev, info.st_ino] != plan['parentIdentity']: self.reject('FILES_PARENT_CHANGED')

    def trash(self, folder, record):
        plan = record['plan']; command = plan['command']
        with self.f.directory(command, 'mutate') as (_, parent, leaf):
            self.parent(parent, plan)
            previous = self.original_record(record)
            record['hddBackup'] = self.b.ensure(previous, parent, leaf, capture_kind='Trash'); self.save(folder, record)
            info = self.f.current(parent, leaf)
            if info is None or self.f.version(info) != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
            self.parent(parent, plan)
            private = self.f.staging(parent)
            try:
                record['state'] = 'TrashRetaining'; self.save(folder, record)
                self.f.exchange(parent, leaf, private, record['operationId'] + '.archive', 1)
                os.fsync(parent); os.fsync(private)
            finally: os.close(private)
        self.recover(folder, record)

    def restore(self, folder, record):
        plan = record['plan']; command = plan['command']; temporary = record['operationId'] + '.archive'
        with self.f.directory(command, 'mutate') as (_, parent, leaf), self.b.target() as (_, target):
            self.parent(parent, plan)
            space = os.fstatvfs(parent)
            reserve = max(512 * 1024**2, space.f_blocks * space.f_frsize // 20) + 4 * self.f.MAX_BYTES
            if space.f_bavail * space.f_frsize < plan['sizeBytes'] + reserve: self.reject('FILES_CAPACITY_REQUIRED')
            archived = self.metadata(target, command['versionId'])
            if self.digest(archived) != plan['archiveIdentity']['metadataHash']: self.reject('FILES_VERSION_CONFLICT')
            private = self.f.staging(parent); output = source = None
            try:
                output = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=private)
                record['newIdentity'] = self.f.publication_identity(os.fstat(output)); self.save(folder, record)
                source = self.f.open_at(target, command['versionId'] + '.data', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                before = self.f.regular(source)
                if self.f.publication_identity(before) != plan['archiveIdentity']['dataIdentity']: self.reject('FILES_VERSION_CONFLICT')
                checksum = hashlib.sha256(); count = 0
                while count < plan['sizeBytes']:
                    part = os.read(source, min(self.f.CHUNK, plan['sizeBytes'] - count))
                    if not part: self.reject('FILES_READ_CHANGED')
                    checksum.update(part); count += len(part); remaining = memoryview(part)
                    while remaining:
                        written = os.write(output, remaining)
                        if written <= 0: self.reject('FILES_WRITE_FAILED')
                        remaining = remaining[written:]
                if checksum.hexdigest().upper() != plan['sha256'] or self.f.version(self.f.regular(source)) != self.f.version(before): self.reject('FILES_READ_CHANGED')
                metadata = plan['restoreMetadata']; os.fchown(output, metadata[3], metadata[4]); os.fchmod(output, metadata[2] & 0o777)
                os.utime(output, ns=(metadata[7], metadata[7])); os.fsync(output); os.fsync(private)
                record['newIdentity'] = self.f.publication_identity(os.fstat(output)); self.save(folder, record)
                if self.plan(command) != plan: self.reject('FILES_VERSION_CONFLICT')
                if plan['sourceIdentity'] is not None:
                    record['hddBackup'] = self.b.ensure(self.original_record(record), parent, leaf); self.save(folder, record)
                self.parent(parent, plan)
                current = self.f.current(parent, leaf)
                if (self.f.version(current) if current else 'ABSENT') != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                # Re-attest the pinned disk after the copy and before publication.
                with self.b.target(): pass
                record['state'] = 'RestorePublishing'; self.save(folder, record)
                self.f.exchange(private, temporary, parent, leaf, 2 if current else 1)
                os.fsync(parent); os.fsync(private)
            finally:
                if output is not None: os.close(output)
                if source is not None: os.close(source)
                os.close(private)
        self.recover(folder, record)

    def purge(self, folder, record):
        plan = record['plan']; command = plan['command']; version = command['versionId']; quarantine = record['operationId'] + '.purging'
        with self.b.target() as (_, target):
            value = self.metadata(target, version)
            if self.digest(value) != plan['archiveIdentity']['metadataHash']: self.reject('FILES_VERSION_CONFLICT')
            self.b.verify_data(target, version, value)
            record['state'] = 'PurgeQuarantining'; self.save(folder, record)
            self.f.exchange(target, version + '.data', target, quarantine, 1); os.fsync(target)
            info = self.f.current(target, quarantine)
            if info is None or self.f.publication_identity(info) != plan['archiveIdentity']['dataIdentity'] or self.f.checksum(target, quarantine) != plan['sha256']: self.reject('FILES_CONCURRENT_PUBLICATION_UNKNOWN')
            record['state'] = 'PurgeRemoving'; self.save(folder, record)
            os.unlink(quarantine, dir_fd=target); os.fsync(target)
            if self.digest(self.metadata(target, version)) != plan['archiveIdentity']['metadataHash']: self.reject('FILES_VERSION_CONFLICT')
            record['state'] = 'PurgeMetadataRemoving'; self.save(folder, record)
            os.unlink(version + '.json', dir_fd=target); os.fsync(target)
            record.update(state='Completed', version=self.digest({'purged': version, 'operation': record['operationId']})); self.save(folder, record)

    def recover(self, folder, record):
        if record['state'] in ('Completed', 'Aborted', 'Unknown'): return
        plan = record['plan']; command = plan['command']; kind = command['kind']; state = record['state']
        try:
            if kind == 'purge':
                with self.b.target() as (_, target):
                    names = set(os.listdir(target)); version = command['versionId']; quarantine = record['operationId'] + '.purging'
                    if state == 'Preparing' or state == 'PurgeQuarantining' and version + '.data' in names and quarantine not in names: record['state'] = 'Aborted'
                    elif state == 'PurgeMetadataRemoving' and version + '.data' not in names and version + '.json' not in names and quarantine not in names:
                        record.update(state='Completed', version=self.digest({'purged': version, 'operation': record['operationId']}))
                    else: record['state'] = 'Unknown'
            else:
                with self.f.directory(command, 'mutate') as (_, parent, leaf):
                    self.parent(parent, plan); private = self.f.staging(parent)
                    try:
                        retained = self.f.current(private, record['operationId'] + '.archive'); current = self.f.current(parent, leaf)
                        if kind == 'trash':
                            valid = retained is not None and self.f.publication_identity(retained) == plan['sourceIdentity'] and self.f.checksum(private, record['operationId'] + '.archive') == plan['sha256']
                            if valid:
                                self.change_capture(record['operationId'], {**self.original_record(record), 'sourceVersion': command['expectedVersion'], 'sha256': plan['sha256'], 'sizeBytes': plan['sizeBytes']}, 'Trashed')
                                record.update(state='Completed', sourceRetained=True, version=self.digest({'trashed': record['operationId'], 'source': plan['sourceIdentity']}))
                            elif retained is None and current is not None and self.f.version(current) == command['expectedVersion']:
                                # SIGKILL can occur after the verified HDD copy
                                # was fsynced but before its proof reached this
                                # journal. Observe that exact copy; never copy,
                                # rename or delete a payload during recovery.
                                with self.b.target() as (_, target):
                                    try: captured = self.metadata(target, record['operationId'])
                                    except FileNotFoundError: captured = None
                                    if captured is not None:
                                        expected = {**self.original_record(record), 'sourceVersion': command['expectedVersion'], 'sha256': plan['sha256'], 'sizeBytes': plan['sizeBytes']}
                                        if captured['sourceIdentity'] != plan['sourceIdentity'] or captured.get('captureKind') != 'Trash': self.reject('FILES_BACKUP_IDEMPOTENCY_CONFLICT')
                                        for key in ('operationId', 'owner', 'rootId', 'path', 'sourceVersion', 'sizeBytes', 'sha256'):
                                            if captured[key] != expected[key]: self.reject('FILES_BACKUP_IDEMPOTENCY_CONFLICT')
                                        if captured['state'] == 'Verified':
                                            self.b.verify_data(target, record['operationId'], captured)
                                            record['hddBackup'] = self.b.public(captured)
                                            self.change_capture(record['operationId'], expected, 'Canceled')
                                record['state'] = 'Aborted'
                            else: record['state'] = 'Unknown'
                        elif state == 'RestorePublishing':
                            valid = current is not None and self.f.publication_identity(current) == record.get('newIdentity') and self.f.checksum(parent, leaf) == plan['sha256']
                            if valid and plan['sourceIdentity'] is not None:
                                valid = retained is not None and self.f.publication_identity(retained) == plan['sourceIdentity'] and self.f.checksum(private, record['operationId'] + '.archive') == plan['sourceSha256']
                            if valid:
                                with self.b.target() as (_, target): archived = self.metadata(target, command['versionId'])
                                if archived.get('captureKind') == 'Trash' and archived.get('captureState') == 'Trashed': self.change_capture(command['versionId'], archived, 'Restored')
                                record.update(state='Completed', version=self.f.version(current), sourceRetained=plan['sourceIdentity'] is not None)
                            elif retained is not None and self.f.publication_identity(retained) == record.get('newIdentity'): record['state'] = 'Aborted'
                            else: record['state'] = 'Unknown'
                        else: record['state'] = 'Aborted'
                    finally: os.close(private)
        except (OSError, self.f.Rejected): record['state'] = 'Unknown'
        self.save(folder, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (folder, record):
            self.recover(folder, record); return self.public(record)

    def discard(self, request):
        # The future trusted adapter validates the original signed plan, but
        # cancellation does not depend on its source still existing/authorized.
        self.request(request, authorize=False)
        with self.state(request['operationId'], request['owner'], create=True) as (folder, record):
            if record is not None:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record)
            else:
                record = {'operationId': request['operationId'], 'owner': request['owner'], 'plan': request['plan'], 'state': 'Aborted'}; self.save(folder, record)
            return self.public(record)

    def public(self, record):
        plan = record['plan']; command = plan['command']; binding = False
        if record['state'] == 'Completed':
            try:
                if command['kind'] == 'purge':
                    with self.b.target() as (_, target): binding = command['versionId'] + '.data' not in os.listdir(target) and command['versionId'] + '.json' not in os.listdir(target)
                else:
                    with self.f.directory(command, 'stat') as (_, parent, leaf):
                        current = self.f.current(parent, leaf)
                        binding = current is None if command['kind'] == 'trash' else current is not None and self.f.version(current) == record.get('version')
            except (OSError, self.f.Rejected): pass
        return {'operationId': record['operationId'], 'kind': command['kind'], 'state': record['state'], 'fingerprint': plan['fingerprint'],
            'version': record.get('version', '') if record['state'] == 'Completed' else '', 'sizeBytes': plan['sizeBytes'], 'sha256': plan['sha256'],
            'sourceRetained': record['state'] == 'Completed' and bool(record.get('sourceRetained')), 'bindingCurrent': binding,
            'hddBackup': record.get('hddBackup'), 'versionId': command['versionId'] if command['kind'] != 'trash' else record['operationId']}
