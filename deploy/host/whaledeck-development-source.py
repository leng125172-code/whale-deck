#!/usr/bin/env python3
"""Stage and publish development source with a bound, retained update preimage."""

import contextlib
import errno
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import struct
import sys
import tarfile
import time
import unicodedata
import zipfile
import zlib

IMPORTS = '.source-imports'
INTENT = 'intent.json'
PLAN = 'plan.json'
RECEIPT = 'receipt.json'
DISCARD = 'discard.json'
ABORT = 'abort.json'
UPDATE = 'update.json'
PREVIOUS = 'previous'
WORK = 'work'
TREE = 'tree'
MAX_ARCHIVE = 128 * 1024**2
MAX_EXPANDED = 1024**3
MAX_ENTRIES = 10000
MAX_EXPANSION = 256
BLOCK = 64 * 1024
DIGEST = re.compile(r'[a-f0-9]{64}\Z')
TOKEN = re.compile(
    rb'-----BEGIN [A-Z0-9 ]{0,32}PRIVATE KEY-----|'
    rb'gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,}|'
    rb'glpat-[A-Za-z0-9_-]{20,}|sk-proj-[A-Za-z0-9_-]{24,}|AKIA[A-Z0-9]{16}'
)
DIR = os.O_RDONLY | os.O_DIRECTORY
WRITE_NEW = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC


class SourceError(Exception):
    pass


def refuse(code):
    raise SourceError('WORKSPACE_SOURCE_' + code)


def source_path(files, name, directory):
    if name.startswith('./'):
        name = name[2:]
    if directory and name in ('', '.', './'):
        return ''
    if directory and name.endswith('/'):
        name = name[:-1]
    try:
        files.relative(name)
    except (UnicodeError, files.Rejected):
        refuse('PATH_INVALID')
    if ':' in name or any(unicodedata.category(c) in ('Cc', 'Cf', 'Cs', 'Zl', 'Zp') for c in name):
        refuse('PATH_INVALID')
    parts = name.lower().split('/')
    if len(parts) > 32:
        refuse('DEPTH_LIMIT')
    for part in parts:
        if (part in ('.git', '.hg', '.svn', '.ssh', '.npmrc', '.pypirc', '.netrc', 'id_rsa', 'id_ed25519')
                or part.startswith(('.whaledeck-', '.images.env'))
                or (part.startswith('.env') and part not in ('.env.example', '.env.sample', '.env.template'))
                or part.endswith(('.pem', '.key', '.p12', '.pfx', '.jks'))):
            refuse('PROTECTED_PATH')
    return name


def rows_digest(rows, domain=b'whaledeck-development-source-tree-v1\n'):
    digest = hashlib.sha256(domain)
    for path, row in sorted(rows.items()):
        digest.update(json.dumps([path, *row], ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n')
    return digest.hexdigest()


class SourceTree:
    def __init__(self, files, root, owner, archive_size, maximum, check):
        self.files, self.root, self.owner, self.check = files, root, owner, check
        self.maximum = min(maximum, archive_size * MAX_EXPANSION)
        self.rows = {'': ('Directory',)}
        self.bytes = 0
        self.count = 0

    def admit(self, path, row):
        if path in self.rows:
            refuse('PATH_CONFLICT')
        if len(self.rows) >= MAX_ENTRIES:
            refuse('ENTRY_LIMIT')
        self.rows[path] = row

    def directory(self, path):
        current = ''
        for leaf in path.split('/') if path else ():
            self.check()
            parent = current
            current = parent + '/' + leaf if parent else leaf
            if self.rows.get(current) == ('Directory',):
                continue
            self.admit(current, ('Directory',))
            with contextlib.closing(Fd(self.files.open_at(self.root, parent or '.', DIR))) as target:
                os.mkdir(leaf, 0o700, dir_fd=target.value)

    @contextlib.contextmanager
    def file(self, row):
        self.check()
        path = row['path']
        size = row['sizeBytes']
        executable = bool(row['mode'] & 0o111)
        if self.bytes + size > self.maximum:
            refuse('EXPANSION_LIMIT')
        self.bytes += size
        self.count += 1
        parent, _, leaf = path.rpartition('/')
        self.directory(parent)
        self.admit(path, ('Pending',))
        digest, written, carry = hashlib.sha256(), 0, b''
        with contextlib.closing(Fd(self.files.open_at(self.root, parent or '.', DIR))) as directory:
            fd = os.open(leaf, WRITE_NEW, 0o600, dir_fd=directory.value)
            with os.fdopen(fd, 'wb') as output:
                def write(data):
                    nonlocal written, carry
                    self.check()
                    written += len(data)
                    if written > size:
                        refuse('ARCHIVE_CHANGED')
                    probe = carry + data
                    if TOKEN.search(probe):
                        refuse('CREDENTIAL_CONTENT')
                    carry = probe[-256:]
                    output.write(data)
                    digest.update(data)

                yield write
                if written != size:
                    refuse('ARCHIVE_CHANGED')
                output.flush()
                os.fchown(fd, self.owner, self.owner)
                os.fchmod(fd, 0o700 if executable else 0o600)
                os.fsync(fd)
        self.rows[path] = ('File', size, digest.hexdigest(), executable)

    def finish(self):
        if self.count == 0:
            refuse('ARCHIVE_EMPTY')
        for name, row in sorted(self.rows.items(), key=lambda item: item[0].count('/') + bool(item[0]), reverse=True):
            self.check()
            if row == ('Directory',):
                fd = self.files.open_at(self.root, name or '.', DIR)
                try:
                    os.fchown(fd, self.owner, self.owner)
                    os.fchmod(fd, 0o700)
                    os.fsync(fd)
                finally:
                    os.close(fd)
        return {'contentSha256': rows_digest(self.rows), 'fileCount': self.count, 'totalBytes': self.bytes}


def work_manifest(driver, root, owner, check, *, durable=False, export_blocks=None):
    rows, versions = {}, {}
    total = 0
    normalized = True

    def scan(fd, path, info, depth):
        nonlocal total, normalized
        check()
        if len(versions) >= MAX_ENTRIES or depth > 32:
            refuse('WORK_SCAN_LIMIT')
        version = driver.files.version(info)
        if driver.files.version(os.fstat(fd)) != version:
            refuse('WORK_CHANGED')
        versions[path] = (version,)
        if export_blocks is not None:
            source_path(driver.files, path, stat.S_ISDIR(info.st_mode))
        mode = stat.S_IMODE(info.st_mode)
        if stat.S_ISDIR(info.st_mode):
            rows[path] = ('Directory',)
            expected = 0o700
            with os.scandir(fd) as entries:
                for entry in entries:
                    check()
                    name = '/'.join(filter(None, (path, entry.name)))
                    try:
                        driver.files.relative(name)
                    except (UnicodeError, driver.files.Rejected):
                        refuse('WORK_UNSUPPORTED')
                    seen = entry.stat(follow_symlinks=False)
                    if not (stat.S_ISDIR(seen.st_mode) or stat.S_ISREG(seen.st_mode) and seen.st_nlink == 1):
                        refuse('WORK_UNSUPPORTED')
                    flags = DIR if stat.S_ISDIR(seen.st_mode) else os.O_PATH
                    with driver.opened(fd, entry.name, flags) as child:
                        if flags == DIR:
                            scan(child, name, seen, depth + 1)
                        else:
                            if driver.files.version(os.fstat(child)) != driver.files.version(seen):
                                refuse('WORK_CHANGED')
                            descriptor = os.open(f'/proc/self/fd/{child}', os.O_RDONLY | os.O_NONBLOCK | os.O_CLOEXEC)
                            with contextlib.closing(Fd(descriptor)):
                                scan(descriptor, name, seen, depth + 1)
        else:
            if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
                refuse('WORK_UNSUPPORTED')
            total += info.st_size
            if total > MAX_EXPANDED:
                refuse('WORK_SCAN_LIMIT')
            digest = hashlib.sha256()
            if export_blocks is not None:
                blocks, carry = [], b''
            remaining = info.st_size
            while remaining:
                check()
                length = min(BLOCK, remaining)
                block = os.read(fd, length)
                if len(block) != length:
                    refuse('WORK_CHANGED')
                if export_blocks is not None:
                    probe = carry + block
                    if TOKEN.search(probe):
                        refuse('CREDENTIAL_CONTENT')
                    carry = probe[-256:]
                    blocks.append(hashlib.sha256(block).digest())
                digest.update(block)
                remaining -= len(block)
            if export_blocks is not None:
                export_blocks[path] = blocks
            if os.read(fd, 1):
                refuse('WORK_CHANGED')
            executable = bool(mode & 0o111)
            rows[path] = ('File', info.st_size, digest.hexdigest(), executable)
            expected = 0o700 if executable else 0o600
        if info.st_uid != owner or info.st_gid != owner or mode != expected or os.listxattr(fd):
            normalized = False
        if durable:
            os.fsync(fd)
        if driver.files.version(os.fstat(fd)) != version:
            refuse('WORK_CHANGED')

    try:
        scan(root, '', os.fstat(root), 0)
        for path, (version,) in versions.items():
            check()
            with driver.opened(root, path or '.', os.O_PATH) as fd:
                if driver.files.version(os.fstat(fd)) != version:
                    refuse('WORK_CHANGED')
    except OSError as error:
        if error.errno in (errno.EXDEV, errno.ELOOP, errno.ENOTDIR, errno.ENOENT):
            refuse('WORK_CHANGED')
        raise
    content = rows_digest(rows)
    fingerprint = rows_digest(versions, b'whaledeck-development-source-work-v1\n' + content.encode('ascii') + b'\n')
    return content, fingerprint, normalized, rows


class VerifiedPreviousFile:
    def __init__(self, fd, blocks, check):
        self.fd, self.blocks, self.check = fd, iter(blocks), check

    def read(self, size):
        self.check()
        data = os.read(self.fd, size)
        # An open writer may outlive publication. Only emit blocks scanned
        # before the archive started, including the credential check.
        if len(data) != size or hashlib.sha256(data).digest() != next(self.blocks, None):
            refuse('WORK_CHANGED')
        return data


def write_previous_tar(driver, root, rows, blocks, output, check):
    with tarfile.open(fileobj=output, mode='w|', format=tarfile.PAX_FORMAT, copybufsize=BLOCK) as archive:
        for path, row in sorted(rows.items()):
            check()
            if not path:
                continue
            entry = tarfile.TarInfo(path)
            if row[0] == 'Directory':
                entry.type, entry.mode = tarfile.DIRTYPE, 0o700
                archive.addfile(entry)
                continue
            _, entry.size, _, executable = row
            entry.mode = 0o700 if executable else 0o600
            with driver.opened(root, path, os.O_PATH) as node:
                info = os.fstat(node)
                if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size != entry.size:
                    refuse('WORK_CHANGED')
                descriptor = os.open(f'/proc/self/fd/{node}', os.O_RDONLY | os.O_NONBLOCK | os.O_CLOEXEC)
                with contextlib.closing(Fd(descriptor)):
                    archive.addfile(entry, VerifiedPreviousFile(descriptor, blocks[path], check))


class Fd:
    def __init__(self, value):
        self.value = value

    def close(self):
        os.close(self.value)


def extract_zip(descriptor, archive_size, tree):
    # Bound the central directory before ZipFile can allocate it. Split,
    # ZIP64 and comment-bearing archives are outside this ingress contract.
    if archive_size < 22 or os.pread(descriptor, 4, 0) != b'PK\x03\x04':
        refuse('ZIP_INVALID')
    end = os.pread(descriptor, 22, archive_size - 22)
    if len(end) != 22:
        refuse('ZIP_INVALID')
    signature, disk, central_disk, disk_count, count, length, offset, comment = struct.unpack('<4s4H2LH', end)
    if (signature != b'PK\x05\x06' or disk or central_disk or disk_count != count or comment
            or not 1 <= count < MAX_ENTRIES or not 0 < length <= 4 * 1024**2
            or offset + length != archive_size - 22
            or (archive_size >= 42 and os.pread(descriptor, 4, archive_size - 42) == b'PK\x06\x07')):
        refuse('ZIP_INVALID')
    explicit = set()
    try:
        with os.fdopen(os.dup(descriptor), 'rb') as stream, zipfile.ZipFile(stream) as archive:
            entries = archive.infolist()
            if len(entries) != count:
                refuse('ZIP_INVALID')
            for entry in entries:
                tree.check()
                directory = entry.is_dir()
                path = source_path(tree.files, entry.orig_filename, directory)
                mode = entry.external_attr >> 16
                if path in explicit or stat.S_IFMT(mode) not in (0, stat.S_IFDIR if directory else stat.S_IFREG):
                    refuse('ZIP_ENTRY_INVALID')
                if (entry.flag_bits & ~0x80e or entry.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                        or entry.file_size > MAX_EXPANSION * max(1, entry.compress_size)
                        or (directory and entry.file_size)):
                    refuse('ZIP_ENTRY_INVALID')
                explicit.add(path)
                if directory:
                    tree.directory(path)
                    continue
                row = {'path': path, 'sizeBytes': entry.file_size, 'mode': mode if entry.create_system == 3 else 0}
                with archive.open(entry) as source, tree.file(row) as write:
                    while data := source.read(BLOCK):
                        write(data)
    except (zipfile.BadZipFile, NotImplementedError, UnicodeError, RuntimeError, EOFError, zlib.error):
        refuse('ZIP_INVALID')


def extract_tar(archive_module, descriptor, fmt, tree, maximum):
    class SourceTar(archive_module.TreeArchive):
        def path(self, name, directory):
            return source_path(self.f, name, directory)

    reader = SourceTar(tree.files, None, max_bytes=maximum, max_archive_bytes=MAX_ARCHIVE,
                       max_entries=MAX_ENTRIES, max_depth=32)
    try:
        result = reader.inspect(descriptor, fmt, check=tree.check, write_file=tree.file)
    except tree.files.Rejected:
        refuse('TAR_INVALID')
    for row in result['entries']:
        if row['kind'] == 'Directory':
            tree.directory(row['path'])


class SourceImports:
    def __init__(self, driver, runner, rootfs, archive):
        self.driver, self.runner, self.rootfs, self.archive = driver, runner, rootfs, archive

    def request(self, request, stage, publish=False):
        fields = {'storage', 'importId', 'archiveFormat', 'archiveBytes', 'archiveSha256'}
        if not isinstance(request, dict):
            refuse('REQUEST_INVALID')
        update = 'previousImportId' in request
        if update:
            fields.add('previousImportId')
        accepted = fields if stage else fields | {'planSha256'}
        if update and publish:
            accepted |= {'conflictResolution'}
        if set(request) != accepted:
            refuse('REQUEST_INVALID')
        _, workspace = self.driver.validate_request(request['storage'])
        try:
            self.driver.files.identity(request['importId'])
            if update:
                self.driver.files.identity(request['previousImportId'])
        except self.driver.files.Rejected:
            refuse('REQUEST_INVALID')
        if (request['archiveFormat'] not in ('zip', 'tar', 'tar.gz') or type(request['archiveBytes']) is not int
                or not 0 < request['archiveBytes'] <= MAX_ARCHIVE
                or not isinstance(request['archiveSha256'], str) or not DIGEST.fullmatch(request['archiveSha256'])
                or (update and (request['archiveFormat'] != 'tar' or request['previousImportId'] == request['importId']))
                or (update and publish and request['conflictResolution'] not in ('clean-only', 'retain-and-replace'))
                or (not stage and (not isinstance(request['planSha256'], str) or not DIGEST.fullmatch(request['planSha256'])))):
            refuse('REQUEST_INVALID')
        return {'workspaceId': workspace, 'storageSha256': hashlib.sha256(self.rootfs.canonical(request['storage'])).hexdigest(),
                **{key: request[key] for key in fields - {'storage'}}}

    @contextlib.contextmanager
    def locked(self, storage):
        with self.driver.attested_payload(storage) as (payload, proof):
            try:
                fcntl.flock(payload, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                refuse('BUSY')
            yield payload, proof

    def metadata(self, directory, name):
        try:
            return self.driver.read_json(directory, name, 4096)
        except FileNotFoundError:
            return None

    def store(self, directory, name, value):
        temporary = '.' + name + '.new'
        with contextlib.suppress(FileNotFoundError):
            os.unlink(temporary, dir_fd=directory)
        try:
            self.rootfs.write_metadata(directory, temporary, value)
            self.driver.files.exchange(directory, temporary, directory, name, 1)
            os.fsync(directory)
        finally:
            with contextlib.suppress(FileNotFoundError):
                os.unlink(temporary, dir_fd=directory)

    def directory(self, parent, name, proof):
        return contextlib.closing(Fd(self.runner.workspace_directory(self.driver, self.rootfs, parent, name, proof)))

    def verify_intent(self, slot, intent, create=False):
        prior = self.metadata(slot, INTENT)
        if prior is None and create:
            self.store(slot, INTENT, intent)
        elif prior != intent:
            refuse('IMPORT_CONFLICT')

    def public_plan(self, plan):
        public = {key: value for key, value in plan.items() if key not in ('treeInode', 'updateBase')}
        if 'updateBase' in plan:
            public['update'] = {key: value for key, value in plan['updateBase'].items() if key not in ('inode', 'fingerprint')}
        return dict(public, planSha256=hashlib.sha256(self.rootfs.canonical(plan)).hexdigest())

    def verified_plan(self, slot, intent):
        plan = self.metadata(slot, PLAN)
        fields = set(intent) | {'contentSha256', 'fileCount', 'totalBytes', 'treeInode'}
        if 'previousImportId' in intent:
            fields.add('updateBase')
        if plan is not None and (set(plan) != fields
                or any(plan[key] != value for key, value in intent.items())
                or not isinstance(plan['contentSha256'], str) or not DIGEST.fullmatch(plan['contentSha256'])
                or type(plan['fileCount']) is not int or not 0 < plan['fileCount'] < MAX_ENTRIES
                or type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= MAX_EXPANDED
                or type(plan['treeInode']) is not int or plan['treeInode'] <= 0):
            refuse('PLAN_CHANGED')
        if plan is not None and 'updateBase' in plan:
            base = plan['updateBase']
            counts = ('addedEntries', 'removedEntries', 'changedEntries')
            if (not isinstance(base, dict) or set(base) != {'inode', 'fingerprint', 'workContentSha256',
                    'previousContentSha256', 'dirty', *counts} or type(base['inode']) is not int or base['inode'] <= 0
                    or base['inode'] == plan['treeInode'] or type(base['dirty']) is not bool
                    or any(not isinstance(base[key], str) or not DIGEST.fullmatch(base[key])
                           for key in ('fingerprint', 'workContentSha256', 'previousContentSha256'))
                    or any(type(base[key]) is not int or not 0 <= base[key] < MAX_ENTRIES for key in counts)):
                refuse('PLAN_CHANGED')
        return plan

    def existing_update(self, payload, imports, proof, intent, check):
        try:
            with self.driver.opened(imports, intent['previousImportId'], DIR) as previous:
                self.rootfs.private_directory(previous)
                prior_intent = self.metadata(previous, INTENT)
                if (not isinstance(prior_intent, dict) or prior_intent.get('importId') != intent['previousImportId']
                        or prior_intent.get('storageSha256') != intent['storageSha256']
                        or prior_intent.get('workspaceId') != intent['workspaceId']):
                    refuse('WORK_BASE_CONFLICT')
                plan = self.verified_plan(previous, prior_intent)
                if plan is None or self.metadata(previous, RECEIPT) != dict(self.public_plan(plan), state='Published'):
                    refuse('WORK_BASE_CONFLICT')
            self.verify_tree(payload, WORK, plan, proof)
            with self.driver.opened(payload, WORK, DIR) as work:
                content, fingerprint, normalized, rows = work_manifest(self.driver, work, self.runner.RUN_UID, check)
        except FileNotFoundError:
            refuse('WORK_BASE_CONFLICT')
        return ({'inode': plan['treeInode'], 'fingerprint': fingerprint, 'workContentSha256': content,
                 'previousContentSha256': plan['contentSha256'], 'dirty': not normalized or content != plan['contentSha256']}, rows)

    @staticmethod
    def exists(parent, name):
        try:
            os.stat(name, dir_fd=parent, follow_symlinks=False)
            return True
        except FileNotFoundError:
            return False

    def read_archive(self, source, intent, check, output=None):
        checksum, remaining = hashlib.sha256(), intent['archiveBytes']
        while remaining:
            check()
            block = source.read(min(remaining, BLOCK))
            if not block:
                refuse('ARCHIVE_CHANGED')
            if output is not None:
                output.write(block)
            checksum.update(block)
            remaining -= len(block)
        check()
        if source.read(1) or checksum.hexdigest() != intent['archiveSha256']:
            refuse('ARCHIVE_CHANGED')

    def copy_archive(self, bundle, source, intent, check):
        fd = os.open('archive', WRITE_NEW, 0o600, dir_fd=bundle)
        with os.fdopen(fd, 'wb') as output:
            self.read_archive(source, intent, check, output)
            output.flush()
            os.fsync(fd)

    def stage(self, request, source):
        intent = self.request(request, True)
        deadline = time.monotonic() + 90
        def check():
            if time.monotonic() >= deadline:
                refuse('DEADLINE')
        with self.locked(request['storage']) as (payload, proof), self.directory(payload, IMPORTS, proof) as imports:
            update = 'previousImportId' in intent
            if not update and self.exists(payload, WORK) and not self.exists(imports.value, intent['importId']):
                refuse('WORK_ALREADY_PRESENT')
            with self.directory(imports.value, intent['importId'], proof) as operation:
                os.fsync(imports.value)
                os.fsync(payload)
                slot = operation.value
                self.verify_intent(slot, intent, create=True)
                if self.metadata(slot, DISCARD) is not None or self.metadata(slot, ABORT) is not None:
                    refuse('DISCARDED')
                if (plan := self.verified_plan(slot, intent)) is not None:
                    self.read_archive(source, intent, check)
                    return self.public_plan(plan)
                if not update and self.exists(payload, WORK):
                    refuse('WORK_ALREADY_PRESENT')
                base, before = self.existing_update(payload, imports.value, proof, intent, check) if update else (None, None)
                self.runner.discard_bundle(self.driver, self.rootfs, slot)
                try:
                    with self.directory(slot, 'bundle', proof) as bundle:
                        self.copy_archive(bundle.value, source, intent, check)
                        with self.directory(bundle.value, TREE, proof) as tree_fd, self.driver.opened(
                                bundle.value, 'archive', os.O_RDONLY | os.O_NONBLOCK) as raw:
                            maximum = min(MAX_EXPANDED, proof['diskMiB'] * 1024**2)
                            tree = SourceTree(self.driver.files, tree_fd.value, self.runner.RUN_UID,
                                              intent['archiveBytes'], maximum, check)
                            if intent['archiveFormat'] == 'zip':
                                extract_zip(raw, intent['archiveBytes'], tree)
                            else:
                                extract_tar(self.archive, raw, intent['archiveFormat'], tree, maximum)
                            manifest = tree.finish()
                            self.driver.quota.verify(tree_fd.value, proof['projectId'], proof['diskMiB'])
                            plan = {**intent, **manifest, 'treeInode': os.fstat(tree_fd.value).st_ino}
                            if base is not None:
                                base.update(addedEntries=len(tree.rows.keys() - before.keys()),
                                            removedEntries=len(before.keys() - tree.rows.keys()),
                                            changedEntries=sum(before[name] != tree.rows[name] for name in before.keys() & tree.rows.keys()))
                                plan['updateBase'] = base
                        os.unlink('archive', dir_fd=bundle.value)
                        os.fsync(bundle.value)
                    os.fsync(slot)
                    self.store(slot, PLAN, plan)
                except Exception:
                    if self.metadata(slot, PLAN) is None:
                        self.runner.discard_bundle(self.driver, self.rootfs, slot)
                    raise
                return self.public_plan(plan)

    def settle(self, request, *, discard=False):
        intent = self.request(request, False, publish=not discard)
        with self.locked(request['storage']) as (payload, proof):
            with self.driver.opened(payload, IMPORTS, DIR) as imports, self.driver.opened(imports, intent['importId'], DIR) as slot:
                self.rootfs.private_directory(imports)
                self.rootfs.private_directory(slot)
                self.verify_intent(slot, intent)
                plan = self.verified_plan(slot, intent)
                if plan is None or self.public_plan(plan)['planSha256'] != request['planSha256']:
                    refuse('PLAN_CONFLICT')
                if discard:
                    if 'updateBase' in plan and self.work_is_candidate(payload, plan):
                        refuse('PUBLICATION_UNCONFIRMED')
                    return self.discard(slot, plan, proof)
                if self.metadata(slot, DISCARD) is not None:
                    refuse('DISCARDED')
                if 'updateBase' in plan:
                    return self.publish_update(payload, slot, proof, plan, request['conflictResolution'])
                receipt = dict(self.public_plan(plan), state='Published')
                if (previous := self.metadata(slot, RECEIPT)) is not None:
                    if previous != receipt:
                        refuse('RECEIPT_CHANGED')
                    return previous
                if not self.exists(payload, WORK):
                    with self.driver.opened(slot, 'bundle', DIR) as bundle:
                        self.rootfs.private_directory(bundle)
                        self.verify_tree(bundle, TREE, plan, proof)
                        try:
                            self.driver.files.exchange(bundle, TREE, payload, WORK, 1)
                        except FileExistsError:
                            refuse('WORK_ALREADY_PRESENT')
                        os.fsync(bundle)
                self.verify_tree(payload, WORK, plan, proof)
                os.fsync(payload)
                with contextlib.suppress(FileNotFoundError):
                    with self.driver.opened(slot, 'bundle', DIR) as bundle:
                        self.rootfs.private_directory(bundle)
                        os.fsync(bundle)
                        os.rmdir('bundle', dir_fd=slot)
                self.store(slot, RECEIPT, receipt)
                return receipt

    def update_position(self, payload, plan):
        try:
            info = os.stat(WORK, dir_fd=payload, follow_symlinks=False)
        except FileNotFoundError:
            refuse('WORK_BASE_CONFLICT')
        if not stat.S_ISDIR(info.st_mode):
            refuse('WORK_BASE_CONFLICT')
        if info.st_ino == plan['treeInode']:
            return 'exchanged'
        if info.st_ino == plan['updateBase']['inode']:
            return 'staged'
        refuse('WORK_BASE_CONFLICT')

    def work_is_candidate(self, payload, plan):
        try:
            info = os.stat(WORK, dir_fd=payload, follow_symlinks=False)
        except FileNotFoundError:
            return False
        return stat.S_ISDIR(info.st_mode) and info.st_ino == plan['treeInode']

    def publish_update(self, payload, slot, proof, plan, choice):
        public = self.public_plan(plan)
        receipt = dict(public, state='Published')
        marker = {'planSha256': public['planSha256'], 'conflictResolution': choice}
        recorded = self.metadata(slot, UPDATE)
        if recorded is not None and recorded != marker:
            refuse('UPDATE_CHOICE_CONFLICT')
        if (previous := self.metadata(slot, RECEIPT)) is not None:
            if previous != receipt or recorded != marker:
                refuse('RECEIPT_CHANGED')
            return previous
        base = plan['updateBase']
        old_tree = {'treeInode': base['inode']}
        position = self.update_position(payload, plan)
        if position == 'staged':
            if self.exists(slot, PREVIOUS):
                refuse('WORK_BASE_CONFLICT')
            if base['dirty'] and choice == 'clean-only':
                refuse('WORK_DIRTY')
            self.verify_tree(payload, WORK, old_tree, proof)
            with self.driver.opened(slot, 'bundle', DIR) as bundle, self.driver.opened(payload, WORK, DIR) as work:
                self.rootfs.private_directory(bundle)
                self.verify_tree(bundle, TREE, plan, proof)
                deadline = time.monotonic() + 90
                def check():
                    if time.monotonic() >= deadline:
                        refuse('DEADLINE')
                _, fingerprint, _, _ = work_manifest(self.driver, work, self.runner.RUN_UID, check, durable=True)
                if fingerprint != base['fingerprint']:
                    refuse('WORK_CHANGED')
                if recorded is None:
                    self.store(slot, UPDATE, marker)
                self.driver.files.exchange(bundle, TREE, payload, WORK, 2)
                os.fsync(bundle)
                os.fsync(payload)
        elif recorded != marker:
            refuse('RECEIPT_CHANGED')
        self.verify_tree(payload, WORK, plan, proof)
        if not self.exists(slot, PREVIOUS):
            with self.driver.opened(slot, 'bundle', DIR) as bundle:
                self.rootfs.private_directory(bundle)
                self.verify_tree(bundle, TREE, old_tree, proof)
                self.driver.files.exchange(bundle, TREE, slot, PREVIOUS, 1)
                os.fsync(bundle)
                os.fsync(slot)
        self.verify_tree(slot, PREVIOUS, old_tree, proof)
        os.fsync(payload)
        if self.exists(slot, 'bundle'):
            with self.driver.opened(slot, 'bundle', DIR) as bundle:
                self.rootfs.private_directory(bundle)
                os.fsync(bundle)
            os.rmdir('bundle', dir_fd=slot)
            os.fsync(slot)
        self.store(slot, RECEIPT, receipt)
        return receipt

    def inspect(self, request):
        intent = self.request(request, True)
        with self.locked(request['storage']) as (payload, proof):
            with contextlib.ExitStack() as directories:
                try:
                    imports = directories.enter_context(self.driver.opened(payload, IMPORTS, DIR))
                    self.rootfs.private_directory(imports)
                    slot = directories.enter_context(self.driver.opened(imports, intent['importId'], DIR))
                    self.rootfs.private_directory(slot)
                except FileNotFoundError:
                    refuse('NOT_FOUND')
                if self.metadata(slot, INTENT) is None:
                    refuse('NOT_FOUND')
                self.verify_intent(slot, intent)
                plan = self.verified_plan(slot, intent)
                if plan is None:
                    refuse('NOT_FOUND')
                public = self.public_plan(plan)
                for name, state in ((RECEIPT, 'Published'), (DISCARD, 'Discarded')):
                    if (recorded := self.metadata(slot, name)) is not None:
                        if recorded != dict(public, state=state):
                            refuse('RECEIPT_CHANGED')
                        if name == DISCARD and self.exists(slot, 'bundle'):
                            return dict(public, state='DiscardUnconfirmed')
                        return recorded
                if 'updateBase' in plan and self.work_is_candidate(payload, plan):
                    marker = self.metadata(slot, UPDATE)
                    if (not isinstance(marker, dict) or set(marker) != {'planSha256', 'conflictResolution'}
                            or marker['planSha256'] != public['planSha256']
                            or marker['conflictResolution'] not in ('clean-only', 'retain-and-replace')):
                        refuse('RECEIPT_CHANGED')
                    self.verify_tree(payload, WORK, plan, proof)
                    return dict(public, state='PublishUnconfirmed')
                if self.exists(slot, 'bundle'):
                    with self.driver.opened(slot, 'bundle', DIR) as bundle:
                        self.rootfs.private_directory(bundle)
                        if self.exists(bundle, TREE):
                            self.verify_tree(bundle, TREE, plan, proof)
                            return dict(public, state='Staged')
                self.verify_tree(payload, WORK, plan, proof)
                return dict(public, state='PublishUnconfirmed')

    def discard(self, slot, plan, proof):
        if self.metadata(slot, RECEIPT) is not None:
            refuse('ALREADY_PUBLISHED')
        receipt = dict(self.public_plan(plan), state='Discarded')
        recorded = self.metadata(slot, DISCARD)
        if recorded is None:
            try:
                with self.driver.opened(slot, 'bundle', DIR) as bundle:
                    self.rootfs.private_directory(bundle)
                    self.verify_tree(bundle, TREE, plan, proof)
            except FileNotFoundError:
                refuse('PUBLICATION_UNCONFIRMED')
            self.store(slot, DISCARD, receipt)
        elif recorded != receipt:
            refuse('RECEIPT_CHANGED')
        self.runner.discard_bundle(self.driver, self.rootfs, slot)
        return receipt

    def abort(self, request):
        intent = self.request(request, True)
        with self.locked(request['storage']) as (payload, proof), self.directory(payload, IMPORTS, proof) as imports:
            with self.directory(imports.value, intent['importId'], proof) as operation:
                os.fsync(imports.value)
                os.fsync(payload)
                slot = operation.value
                self.verify_intent(slot, intent, create=True)
                if self.verified_plan(slot, intent) is not None:
                    refuse('PLAN_CONFLICT')
                marker = dict(intent, state='Aborted')
                previous = self.metadata(slot, ABORT)
                if previous is not None and previous != marker:
                    refuse('RECEIPT_CHANGED')
                # An incomplete raw archive may have used the last project
                # block. Free it under the lock before recording the fence.
                self.runner.discard_bundle(self.driver, self.rootfs, slot)
                if previous is None:
                    self.store(slot, ABORT, marker)
                return {'state': 'Aborted'}

    def export_previous(self, request, output):
        intent = self.request(request, False, publish=True)
        if 'previousImportId' not in intent:
            refuse('REQUEST_INVALID')
        with self.locked(request['storage']) as (payload, proof), contextlib.ExitStack() as directories:
            try:
                imports = directories.enter_context(self.driver.opened(payload, IMPORTS, DIR))
                self.rootfs.private_directory(imports)
                slot = directories.enter_context(self.driver.opened(imports, intent['importId'], DIR))
                self.rootfs.private_directory(slot)
                self.verify_intent(slot, intent)
                plan = self.verified_plan(slot, intent)
                if plan is None:
                    refuse('NOT_FOUND')
                public = self.public_plan(plan)
                if public['planSha256'] != request['planSha256']:
                    refuse('PLAN_CONFLICT')
                if self.metadata(slot, RECEIPT) != dict(public, state='Published'):
                    refuse('NOT_FOUND')
                if self.metadata(slot, UPDATE) != {'planSha256': public['planSha256'],
                                                   'conflictResolution': request['conflictResolution']}:
                    refuse('UPDATE_CHOICE_CONFLICT')
                base = plan['updateBase']
                self.verify_tree(slot, PREVIOUS, {'treeInode': base['inode']}, proof)
                previous = directories.enter_context(self.driver.opened(slot, PREVIOUS, DIR))
                if os.fstat(previous).st_ino != base['inode']:
                    refuse('WORK_CHANGED')
                blocks = {}
                content, _, _, rows = work_manifest(self.driver, previous, self.runner.RUN_UID, lambda: None,
                                                    export_blocks=blocks)
                if content != base['workContentSha256']:
                    refuse('WORK_CHANGED')
                write_previous_tar(self.driver, previous, rows, blocks, output, lambda: None)
            except FileNotFoundError:
                refuse('NOT_FOUND')

    def verify_tree(self, parent, name, plan, proof):
        try:
            with self.driver.opened(parent, name, DIR) as tree:
                info = os.fstat(tree)
                if (info.st_ino != plan['treeInode'] or info.st_uid != self.runner.RUN_UID
                        or info.st_gid != self.runner.RUN_UID or stat.S_IMODE(info.st_mode) != 0o700):
                    refuse('WORK_ALREADY_PRESENT' if name == WORK else 'STAGE_CHANGED')
                self.driver.quota.verify(tree, proof['projectId'], proof['diskMiB'])
        except OSError as error:
            if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR):
                refuse('WORK_ALREADY_PRESENT' if name == WORK else 'STAGE_CHANGED')
            raise


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2:
        return 2
    mode = sys.argv[1]
    sandboxed = mode.startswith('service-')
    action = mode.removeprefix('service-')
    if action not in ('stage', 'inspect', 'publish', 'discard', 'abort', 'export'):
        return 2
    from importlib.util import module_from_spec, spec_from_file_location
    try:
        path = Path(__file__).with_name('whaledeck-development-runner.py')
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o7022:
            refuse('HELPER_UNAVAILABLE')
        spec = spec_from_file_location('workspace_runner', path)
        runner = module_from_spec(spec)
        spec.loader.exec_module(runner)
        files = runner.load('whaledeck-files.py')
        storage = runner.load('whaledeck-development-storage.py')
        quota = runner.load('whaledeck-development-project-quota.py')
        rootfs = runner.load('whaledeck-development-rootfs.py')
        archive = runner.load('whaledeck-tree-archive.py')
        if sandboxed:
            guard = runner.load('whaledeck-development-quota-guard.py')
            guard.install(quota, read_only=True)
            if action == 'export':
                guard.restrict_path_writes()
    except (OSError, ImportError, SourceError):
        print('WORKSPACE_SOURCE_HELPER_UNAVAILABLE', file=sys.stderr)
        return 2
    try:
        signal.signal(signal.SIGALRM, lambda *_: refuse('DEADLINE'))
        signal.setitimer(signal.ITIMER_REAL, 100)
        header = sys.stdin.buffer.readline(4097)
        if not header.endswith(b'\n') or len(header) > 4096:
            refuse('REQUEST_INVALID')
        request = rootfs.decode(header)
        service = SourceImports(storage.WorkspaceStorage(files, sandboxed=sandboxed, quota=quota), runner, rootfs, archive)
        if action == 'stage':
            runner.enforce_staging_quota()
            result = service.stage(request, sys.stdin.buffer)
        else:
            if sys.stdin.buffer.read(1):
                refuse('REQUEST_INVALID')
            if action == 'export':
                service.export_previous(request, sys.stdout.buffer)
                return 0
            elif action == 'inspect':
                result = service.inspect(request)
            elif action == 'abort':
                result = service.abort(request)
            else:
                result = service.settle(request, discard=action == 'discard')
        print(json.dumps(result, separators=(',', ':')))
        return 0
    except (SourceError, files.Rejected, runner.RunError, rootfs.RootfsError) as error:
        code = str(error)
        if not re.fullmatch(r'(WORKSPACE|FILES)_[A-Z_]{1,80}', code):
            code = 'WORKSPACE_SOURCE_REJECTED'
    except OSError as error:
        code = 'WORKSPACE_SOURCE_CAPACITY' if error.errno in (errno.EDQUOT, errno.ENOSPC) else 'WORKSPACE_SOURCE_UNAVAILABLE'
    except (ValueError, TypeError, KeyError, RuntimeError, quota.QuotaError):
        code = 'WORKSPACE_SOURCE_REJECTED'
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
    print(code, file=sys.stderr)
    return 2


if __name__ == '__main__':
    sys.exit(main())
