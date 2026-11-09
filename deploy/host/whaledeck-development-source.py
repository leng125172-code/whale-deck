#!/usr/bin/env python3
"""Host-side stage and no-replace publication of development source archives."""

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
        manifest = hashlib.sha256(b'whaledeck-development-source-tree-v1\n')
        for path, row in sorted(self.rows.items()):
            manifest.update(json.dumps([path, *row], ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n')
        return {'contentSha256': manifest.hexdigest(), 'fileCount': self.count, 'totalBytes': self.bytes}


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

    def request(self, request, stage):
        fields = {'storage', 'importId', 'archiveFormat', 'archiveBytes', 'archiveSha256'}
        if not isinstance(request, dict) or set(request) != (fields if stage else fields | {'planSha256'}):
            refuse('REQUEST_INVALID')
        _, workspace = self.driver.validate_request(request['storage'])
        try:
            self.driver.files.identity(request['importId'])
        except self.driver.files.Rejected:
            refuse('REQUEST_INVALID')
        if (request['archiveFormat'] not in ('zip', 'tar', 'tar.gz') or type(request['archiveBytes']) is not int
                or not 0 < request['archiveBytes'] <= MAX_ARCHIVE
                or not isinstance(request['archiveSha256'], str) or not DIGEST.fullmatch(request['archiveSha256'])
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
        return {**{key: value for key, value in plan.items() if key != 'treeInode'},
                'planSha256': hashlib.sha256(self.rootfs.canonical(plan)).hexdigest()}

    def verified_plan(self, slot, intent):
        plan = self.metadata(slot, PLAN)
        if plan is not None and (set(plan) != set(intent) | {'contentSha256', 'fileCount', 'totalBytes', 'treeInode'}
                or any(plan[key] != value for key, value in intent.items())
                or not isinstance(plan['contentSha256'], str) or not DIGEST.fullmatch(plan['contentSha256'])
                or type(plan['fileCount']) is not int or not 0 < plan['fileCount'] < MAX_ENTRIES
                or type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= MAX_EXPANDED
                or type(plan['treeInode']) is not int or plan['treeInode'] <= 0):
            refuse('PLAN_CHANGED')
        return plan

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
            if self.exists(payload, WORK) and not self.exists(imports.value, intent['importId']):
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
                if self.exists(payload, WORK):
                    refuse('WORK_ALREADY_PRESENT')
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
        intent = self.request(request, False)
        with self.locked(request['storage']) as (payload, proof):
            with self.driver.opened(payload, IMPORTS, DIR) as imports, self.driver.opened(imports, intent['importId'], DIR) as slot:
                self.rootfs.private_directory(imports)
                self.rootfs.private_directory(slot)
                self.verify_intent(slot, intent)
                plan = self.verified_plan(slot, intent)
                if plan is None or self.public_plan(plan)['planSha256'] != request['planSha256']:
                    refuse('PLAN_CONFLICT')
                if discard:
                    return self.discard(slot, plan, proof)
                if self.metadata(slot, DISCARD) is not None:
                    refuse('DISCARDED')
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
    if action not in ('stage', 'inspect', 'publish', 'discard', 'abort'):
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
            runner.load('whaledeck-development-quota-guard.py').install(quota, read_only=True)
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
            if action == 'inspect':
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
