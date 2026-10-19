#!/usr/bin/env python3
"""Internal FD-only tar/tar.gz codec for directory backup and staged restoration.

No CLI, RPC, selected host path, publication, source removal, or cleanup. The
caller must authorize registered roots, persist intent, attest HDD storage and
provide a new private output FD or a private empty staging directory. Extracted
root ownership/mode stay root:0700 until a separate verified publication fence.
Never use extractall: parse bounded headers and create only exclusive regular
files/directories through the existing openat2 policy. Failure retains partial
private evidence; calling again never overwrites it.
"""
import contextlib
import fcntl
import gzip
import hashlib
import json
import math
import os
import re
import stat
import tarfile
import time
import zlib


class TreeArchive:
    MAX_METADATA = 16 * 1024**2
    MEMBER_METADATA = 16384
    CHUNK = 65536
    FORMATS = ('tar', 'tar.gz')

    def __init__(self, files, manifest_type, *, max_entries=10000, max_bytes=16 * 1024**3,
                 max_archive_bytes=17 * 1024**3, max_depth=32, timeout_seconds=600,
                 cancelled=None, clock=time.monotonic):
        self.f, self.manifest_type = files, manifest_type
        for value, lower, upper in ((max_entries, 1, 10000), (max_bytes, 0, 16 * 1024**3),
                (max_archive_bytes, 10240, 17 * 1024**3), (max_depth, 0, 32)):
            if type(value) is not int or not lower <= value <= upper: self.reject('FILES_ARCHIVE_LIMITS_INVALID')
        if type(timeout_seconds) not in (int, float) or not math.isfinite(timeout_seconds) or not 0.001 <= timeout_seconds <= 600 or not callable(clock) or cancelled is not None and not callable(cancelled):
            self.reject('FILES_ARCHIVE_LIMITS_INVALID')
        self.max_entries, self.max_bytes, self.max_archive_bytes, self.max_depth = max_entries, max_bytes, max_archive_bytes, max_depth
        self.timeout_seconds, self.cancelled, self.clock = timeout_seconds, cancelled, clock

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode('utf-8')

    def guard(self):
        deadline = self.clock() + self.timeout_seconds
        def check():
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if self.clock() >= deadline: self.reject('FILES_TREE_DEADLINE')
        check()
        return check

    def format(self, value):
        if value not in self.FORMATS: self.reject('FILES_ARCHIVE_FORMAT_INVALID')

    def regular(self, descriptor, private=False):
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size < 0 or info.st_size > self.max_archive_bytes:
            self.reject('FILES_ARCHIVE_FILE_INVALID')
        if private and (info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600): self.reject('FILES_ARCHIVE_FILE_INVALID')
        return info

    def checksum(self, descriptor, check):
        before = self.regular(descriptor); position = 0; digest = hashlib.sha256()
        while position < before.st_size:
            check(); block = os.pread(descriptor, min(self.CHUNK, before.st_size - position), position)
            if not block: self.reject('FILES_ARCHIVE_CHANGED')
            position += len(block); digest.update(block)
        if os.pread(descriptor, 1, position) or self.f.version(os.fstat(descriptor)) != self.f.version(before): self.reject('FILES_ARCHIVE_CHANGED')
        return digest.hexdigest().upper()

    def path(self, name, directory):
        if not isinstance(name, str): self.reject('FILES_ARCHIVE_PATH_INVALID')
        if name.startswith('./'): name = name[2:]
        if name in ('', '.'):
            if directory: return ''
            self.reject('FILES_ARCHIVE_PATH_INVALID')
        if directory and name.endswith('/'): name = name[:-1]
        try: self.f.relative(name)
        except (UnicodeError, self.f.Rejected): self.reject('FILES_ARCHIVE_PATH_INVALID')
        if self.f.protected(name): self.reject('FILES_TREE_PROTECTED')
        if len(name.split('/')) > self.max_depth: self.reject('FILES_TREE_DEPTH_LIMIT')
        return name

    @staticmethod
    def ns_text(value):
        sign = '-' if value < 0 else ''; seconds, fraction = divmod(abs(value), 1000000000)
        return sign + str(seconds) + ('.' + f'{fraction:09}' if fraction else '')

    def nanoseconds(self, value):
        if not isinstance(value, str) or not re.fullmatch(r'-?[0-9]{1,11}(?:\.[0-9]{1,9})?', value): self.reject('FILES_ARCHIVE_METADATA_INVALID')
        sign = -1 if value.startswith('-') else 1; seconds, _, fraction = value.lstrip('-').partition('.')
        result = sign * (int(seconds) * 1000000000 + int(fraction.ljust(9, '0') or '0'))
        if not -9223372036854775808 <= result <= 9223372036854775807: self.reject('FILES_ARCHIVE_METADATA_INVALID')
        return result

    def pax(self, payload):
        if len(payload) > self.MEMBER_METADATA: self.reject('FILES_ARCHIVE_METADATA_LIMIT')
        values = {}; position = 0
        while position < len(payload):
            space = payload.find(b' ', position, position + 8)
            if space < 0 or not re.fullmatch(b'[1-9][0-9]{0,5}', payload[position:space]): self.reject('FILES_ARCHIVE_METADATA_INVALID')
            count = int(payload[position:space]); end = position + count
            if count < 5 or end > len(payload) or payload[end - 1:end] != b'\n': self.reject('FILES_ARCHIVE_METADATA_INVALID')
            key, separator, value = payload[space + 1:end - 1].partition(b'=')
            try: key, value = key.decode('utf-8', 'strict'), value.decode('utf-8', 'strict')
            except UnicodeError: self.reject('FILES_ARCHIVE_METADATA_INVALID')
            if not separator or key not in ('path', 'mtime', 'size', 'uid', 'gid', 'uname', 'gname', 'atime', 'ctime') or key in values:
                self.reject('FILES_ARCHIVE_METADATA_INVALID')
            if key in ('mtime', 'atime', 'ctime'): self.nanoseconds(value)
            elif key in ('size', 'uid', 'gid') and not re.fullmatch('[0-9]{1,20}', value): self.reject('FILES_ARCHIVE_METADATA_INVALID')
            values[key] = value; position = end
        return values

    def create(self, command, manifest_sha256, output, archive_format='tar.gz'):
        self.format(archive_format); check = self.guard()
        inventory_engine = self.manifest_type(self.f, max_entries=self.max_entries, max_bytes=self.max_bytes,
            max_depth=self.max_depth, cancelled=lambda: (check(), False)[1])
        manifest = inventory_engine.verify(command, manifest_sha256)
        before = self.regular(output, private=True)
        flags = fcntl.fcntl(output, fcntl.F_GETFL)
        if flags & os.O_ACCMODE != os.O_RDWR or flags & os.O_APPEND or before.st_size or os.lseek(output, 0, os.SEEK_CUR) != 0 or any(row['sourceIdentity'][:2] == [before.st_dev, before.st_ino] for row in manifest['entries']):
            self.reject('FILES_ARCHIVE_OUTPUT_NOT_NEW')
        engine = self
        class Output:
            count = 0
            def write(self, data):
                check()
                if self.count + len(data) > engine.max_archive_bytes: engine.reject('FILES_ARCHIVE_SIZE_LIMIT')
                view = memoryview(data)
                while view:
                    check(); written = os.write(output, view[:engine.CHUNK])
                    if written <= 0: engine.reject('FILES_WRITE_FAILED')
                    self.count += written; view = view[written:]
                return len(data)
            def flush(self): os.fsync(output)
        sink = Output(); total_tar = 0
        @contextlib.contextmanager
        def compressed():
            # A streaming zlib gzip encoder owns no file object. If authority
            # or I/O fails, it leaves an incomplete private stream without a
            # GzipFile destructor trying to append a footer after cancellation.
            compressor = zlib.compressobj(level=6,wbits=16+zlib.MAX_WBITS)
            class Stream:
                def write(self,data):
                    check(); sink.write(compressor.compress(data)); return len(data)
            yield Stream()
            check(); sink.write(compressor.flush())
        with self.f.directory(command, ('list', 'download')) as (_, parent, leaf):
            source = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY)
            try:
                if self.f.version(os.fstat(source)) != command['expectedVersion']: self.reject('FILES_TREE_CHANGED')
                with (compressed() if archive_format == 'tar.gz' else contextlib.nullcontext(sink)) as stream:
                    for row in manifest['entries']:
                        check(); metadata = row['sourceIdentity']
                        header = tarfile.TarInfo(row['path'] or '.')
                        header.type = tarfile.DIRTYPE if row['kind'] == 'Directory' else tarfile.REGTYPE
                        header.size = row['sizeBytes']; header.mode = metadata[2] & 0o777
                        header.uid, header.gid = metadata[3:5]; header.mtime = metadata[7] // 1000000000
                        header.pax_headers = {'mtime': self.ns_text(metadata[7])}
                        raw = header.tobuf(format=tarfile.PAX_FORMAT, encoding='utf-8', errors='strict')
                        if len(raw) > self.MEMBER_METADATA + 1024: self.reject('FILES_ARCHIVE_METADATA_LIMIT')
                        stream.write(raw); total_tar += len(raw)
                        if row['kind'] == 'File':
                            original = self.f.open_at(source, row['path'], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                            try:
                                info = os.fstat(original)
                                if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or self.f.version(info) != row['sourceVersion']: self.reject('FILES_TREE_CHANGED')
                                digest = hashlib.sha256(); count = 0
                                while count < row['sizeBytes']:
                                    check(); block = os.read(original, min(self.CHUNK, row['sizeBytes'] - count))
                                    if not block: self.reject('FILES_READ_CHANGED')
                                    digest.update(block); count += len(block); stream.write(block)
                                path, _, name = row['path'].rpartition('/')
                                bound_parent = self.f.open_at(source,path or '.',os.O_RDONLY | os.O_DIRECTORY)
                                try: bound_version = self.f.version(os.stat(name,dir_fd=bound_parent,follow_symlinks=False))
                                finally: os.close(bound_parent)
                                if os.read(original, 1) or digest.hexdigest().upper() != row['sha256'] or self.f.version(os.fstat(original)) != row['sourceVersion'] or bound_version != row['sourceVersion']:
                                    self.reject('FILES_READ_CHANGED')
                            finally: os.close(original)
                            padding = -row['sizeBytes'] % 512
                            stream.write(b'\0' * padding); total_tar += row['sizeBytes'] + padding
                    stream.write(b'\0' * (1024 + -(total_tar + 1024) % 10240))
                inventory_engine.verify(command, manifest_sha256)
            finally: os.close(source)
        os.fsync(output)
        result = self.inspect(output, archive_format, check=check)
        expected = [self.from_manifest(row) for row in manifest['entries']]
        if result['entries'] != expected: self.reject('FILES_ARCHIVE_CONTENT_INVALID')
        return {**result, 'manifestSha256': manifest_sha256}

    @staticmethod
    def from_manifest(row):
        identity = row['sourceIdentity']
        return {'path':row['path'], 'kind':row['kind'], 'sizeBytes':row['sizeBytes'], 'sha256':row['sha256'],
            'mode':identity[2] & 0o777, 'uid':identity[3], 'gid':identity[4], 'mtimeNs':identity[7]}

    def inspect(self, descriptor, archive_format='tar.gz', *, check=None, write_file=None):
        self.format(archive_format); check = check or self.guard()
        before = self.regular(descriptor)
        if before.st_size == 0: self.reject('FILES_ARCHIVE_CORRUPT')
        engine = self; expanded = 0
        def exact(stream, length):
            nonlocal expanded
            if not 0 <= length <= engine.CHUNK: engine.reject('FILES_ARCHIVE_METADATA_LIMIT')
            blocks = []; remaining = length
            while remaining:
                check(); block = stream.read(remaining)
                if not block: engine.reject('FILES_ARCHIVE_CORRUPT')
                remaining -= len(block); expanded += len(block); blocks.append(block)
                if expanded > engine.max_bytes + 64 * 1024**2: engine.reject('FILES_ARCHIVE_SIZE_LIMIT')
                # Bound amplification independently of advertised member sizes.
                if expanded > max(10240, before.st_size * 4096): engine.reject('FILES_ARCHIVE_EXPANSION_LIMIT')
            return b''.join(blocks)
        root = {'path':'', 'kind':'Directory', 'sizeBytes':0, 'sha256':'', 'mode':0o700, 'uid':0, 'gid':0, 'mtimeNs':0}
        entries = {'':root}; explicit = set(); pending = {}; metadata_bytes = 0; total = 0; headers = 0; index_bytes = len(self.encode(root))
        def admit(row, is_explicit):
            nonlocal index_bytes
            path = row['path']
            if path in explicit or path in entries and entries[path]['kind'] != 'Directory': self.reject('FILES_ARCHIVE_DUPLICATE')
            if is_explicit: explicit.add(path)
            previous = entries.get(path)
            index_bytes += len(self.encode(row)) - (len(self.encode(previous)) if previous else 0)
            entries[path] = row
            if len(entries) > self.max_entries: self.reject('FILES_TREE_ENTRY_LIMIT')
            if index_bytes > self.MAX_METADATA: self.reject('FILES_ARCHIVE_METADATA_LIMIT')
        raw = os.fdopen(os.dup(descriptor), 'rb'); raw.seek(0)
        class GuardedInput:
            # gzip can read optional header strings one byte at a time. Keep
            # cancellation/deadline checks active inside those reads too.
            def read(self, length):
                check()
                if type(length) is not int or length < 0: engine.reject('FILES_ARCHIVE_SIZE_LIMIT')
                return raw.read(min(length, engine.CHUNK))
            def tell(self): return raw.tell()
            def seek(self, offset, whence=0): check(); return raw.seek(offset, whence)
        try:
            decoder = gzip.GzipFile(fileobj=GuardedInput(), mode='rb') if archive_format == 'tar.gz' else contextlib.nullcontext(raw)
            with decoder as stream:
                while True:
                    check(); block = exact(stream, 512)
                    if block == bytes(512):
                        if pending or exact(stream, 512) != bytes(512): self.reject('FILES_ARCHIVE_CORRUPT')
                        padding = 0
                        while True:
                            check(); tail = stream.read(512)
                            if not tail: break
                            padding += len(tail)
                            if len(tail) != 512 or any(tail) or padding > 10240: self.reject('FILES_ARCHIVE_TRAILING_DATA')
                        break
                    headers += 1
                    if headers > self.max_entries * 2 + 8: self.reject('FILES_TREE_ENTRY_LIMIT')
                    try: info = tarfile.TarInfo.frombuf(block, encoding='utf-8', errors='strict')
                    except (tarfile.HeaderError, UnicodeError, ValueError): self.reject('FILES_ARCHIVE_CORRUPT')
                    if info.type == tarfile.XHDTYPE:
                        if pending or not 0 < info.size <= self.MEMBER_METADATA: self.reject('FILES_ARCHIVE_METADATA_LIMIT')
                        payload = exact(stream, (info.size + 511) // 512 * 512)
                        if any(payload[info.size:]): self.reject('FILES_ARCHIVE_CORRUPT')
                        metadata_bytes += info.size
                        if metadata_bytes > self.MAX_METADATA: self.reject('FILES_ARCHIVE_METADATA_LIMIT')
                        pending = self.pax(payload[:info.size]); continue
                    # Do not call TarInfo._proc_member: that would process sparse
                    # or unbounded extension bodies before our budgets run.
                    if info.type not in (tarfile.REGTYPE, tarfile.AREGTYPE, tarfile.DIRTYPE): self.reject('FILES_ARCHIVE_SPECIAL_OBJECT')
                    is_directory = info.type == tarfile.DIRTYPE
                    size = int(pending.get('size', info.size)); uid = int(pending.get('uid', info.uid)); gid = int(pending.get('gid', info.gid))
                    mtime = self.nanoseconds(pending.get('mtime', str(info.mtime)))
                    path = self.path(pending.get('path', info.name), is_directory); pending = {}
                    if not 0 <= size <= self.max_bytes or is_directory and size != 0: self.reject('FILES_TREE_SIZE_LIMIT')
                    if not 0 <= uid < 4294967295 or not 0 <= gid < 4294967295 or not 0 <= info.mode <= 0o7777: self.reject('FILES_ARCHIVE_METADATA_INVALID')
                    if path in explicit or path in entries and not is_directory: self.reject('FILES_ARCHIVE_DUPLICATE')
                    parts = path.split('/') if path else []
                    for depth in range(1, len(parts)):
                        parent = '/'.join(parts[:depth])
                        if parent in entries:
                            if entries[parent]['kind'] != 'Directory': self.reject('FILES_ARCHIVE_PATH_CONFLICT')
                        else: admit({**root,'path':parent}, False)
                    row = {'path':path, 'kind':'Directory' if is_directory else 'File', 'sizeBytes':size,
                        'sha256':'', 'mode':info.mode & 0o777, 'uid':uid, 'gid':gid, 'mtimeNs':mtime}
                    if path not in entries and len(entries) >= self.max_entries: self.reject('FILES_TREE_ENTRY_LIMIT')
                    estimated = {**row, 'sha256':'' if is_directory else '0' * 64}
                    if index_bytes + len(self.encode(estimated)) - (len(self.encode(entries[path])) if path in entries else 0) > self.MAX_METADATA:
                        self.reject('FILES_ARCHIVE_METADATA_LIMIT')
                    if not is_directory:
                        if total + size > self.max_bytes: self.reject('FILES_TREE_SIZE_LIMIT')
                        total += size; digest = hashlib.sha256(); remaining = size
                        destination = contextlib.nullcontext(None) if write_file is None else write_file(row)
                        with destination as output:
                            while remaining:
                                data = exact(stream, min(self.CHUNK, remaining)); remaining -= len(data); digest.update(data)
                                if output is not None: output(data)
                        row['sha256'] = digest.hexdigest().upper()
                        if any(exact(stream, -size % 512)): self.reject('FILES_ARCHIVE_CORRUPT')
                    admit(row, True)
            if self.f.version(os.fstat(descriptor)) != self.f.version(before): self.reject('FILES_ARCHIVE_CHANGED')
            ordered = sorted(entries.values(), key=lambda row: tuple(part.encode('utf-8') for part in row['path'].split('/')) if row['path'] else ())
            return {'schemaVersion':1, 'format':archive_format, 'archiveBytes':before.st_size,
                'archiveSha256':self.checksum(descriptor, check), 'archiveVersion':self.f.version(before),
                'entryCount':len(ordered), 'totalBytes':total, 'entries':ordered,
                'contentSha256':hashlib.sha256(b'whaledeck-tree-archive-content-v1\n' + self.encode(ordered)).hexdigest().upper()}
        except (gzip.BadGzipFile, EOFError, zlib.error, tarfile.TarError, UnicodeError, OverflowError): self.reject('FILES_ARCHIVE_CORRUPT')
        finally: raw.close()

    def extract(self, archive, target, expected, archive_format='tar.gz', *, owner_uid=0, owner_gid=0):
        """Verify first; populate a private empty FD without publication/overwrite.

        owner IDs come from the caller's trusted target registry, never archive
        headers or an HTTP request. Root remains root:0700 for later publication.
        """
        if any(type(value) is not int or not 0 <= value < 2**31 for value in (owner_uid, owner_gid)): self.reject('FILES_ARCHIVE_OWNER_INVALID')
        if not isinstance(expected, dict): self.reject('FILES_ARCHIVE_PLAN_INVALID')
        if 'manifestSha256' in expected and (type(expected['manifestSha256']) is not str or not re.fullmatch('[A-F0-9]{64}', expected['manifestSha256'])):
            self.reject('FILES_ARCHIVE_PLAN_INVALID')
        check = self.guard(); verified = self.inspect(archive, archive_format, check=check)
        if verified != {key:value for key,value in expected.items() if key != 'manifestSha256'}: self.reject('FILES_ARCHIVE_CHANGED')
        info = os.fstat(target)
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_ARCHIVE_TARGET_UNSAFE')
        with os.scandir(target) as children:
            if next(children, None) is not None: self.reject('FILES_ARCHIVE_TARGET_NOT_EMPTY')
        for row in verified['entries']:
            check()
            if row['kind'] != 'Directory' or not row['path']: continue
            path, _, leaf = row['path'].rpartition('/')
            parent = self.f.open_at(target, path or '.', os.O_RDONLY | os.O_DIRECTORY)
            try: os.mkdir(leaf, 0o700, dir_fd=parent); os.fsync(parent)
            finally: os.close(parent)
        engine = self; by_path = {row['path']:row for row in verified['entries']}
        @contextlib.contextmanager
        def write_file(row):
            check()
            wanted = by_path.get(row['path'])
            if wanted is None or any(row[key] != wanted[key] for key in row if key != 'sha256'): engine.reject('FILES_ARCHIVE_CHANGED')
            path, _, leaf = row['path'].rpartition('/')
            parent = engine.f.open_at(target, path or '.', os.O_RDONLY | os.O_DIRECTORY); output = None
            try:
                output = os.open(leaf, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
                def write(data):
                    view = memoryview(data)
                    while view:
                        check(); count = os.write(output, view)
                        if count <= 0: engine.reject('FILES_WRITE_FAILED')
                        view = view[count:]
                yield write
                os.fchown(output, owner_uid, owner_gid); os.fchmod(output, wanted['mode'])
                os.utime(output, ns=(wanted['mtimeNs'],wanted['mtimeNs'])); os.fsync(output); os.fsync(parent)
            finally:
                if output is not None: os.close(output)
                os.close(parent)
        result = self.inspect(archive, archive_format, check=check, write_file=write_file)
        if result != verified: self.reject('FILES_ARCHIVE_CHANGED')
        for row in reversed(verified['entries']):
            check()
            if row['kind'] != 'Directory': continue
            descriptor = self.f.open_at(target, row['path'] or '.', os.O_RDONLY | os.O_DIRECTORY)
            try:
                if row['path']: os.fchown(descriptor, owner_uid, owner_gid); os.fchmod(descriptor, row['mode'])
                os.utime(descriptor, ns=(row['mtimeNs'],row['mtimeNs'])); os.fsync(descriptor)
            finally: os.close(descriptor)
        # Verify complete membership and directory metadata as well as payloads.
        children_by_parent = {}
        for row in verified['entries']:
            if row['path']:
                path, _, leaf = row['path'].rpartition('/')
                children_by_parent.setdefault(path,set()).add(leaf)
        for row in verified['entries']:
            check()
            if row['kind'] != 'Directory': continue
            folder = self.f.open_at(target, row['path'] or '.', os.O_RDONLY | os.O_DIRECTORY)
            try:
                before = os.fstat(folder)
                if row['path'] and (stat.S_IMODE(before.st_mode) != row['mode'] or before.st_uid != owner_uid or before.st_gid != owner_gid) or before.st_mtime_ns != row['mtimeNs']:
                    self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                wanted = children_by_parent.get(row['path'],set())
                actual = set()
                with os.scandir(folder) as children:
                    for child in children:
                        check()
                        if child.name not in wanted: self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                        actual.add(child.name)
                if actual != wanted or self.f.version(os.fstat(folder)) != self.f.version(before): self.reject('FILES_ARCHIVE_CONTENT_INVALID')
            finally: os.close(folder)
        # Rehash every materialized file. The stream checksum alone is not a
        # proof that the destination disk actually contains those bytes.
        for row in verified['entries']:
            check()
            if row['kind'] != 'File': continue
            descriptor = self.f.open_at(target, row['path'], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                value = os.fstat(descriptor)
                if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1 or value.st_size != row['sizeBytes'] or stat.S_IMODE(value.st_mode) != row['mode'] or value.st_uid != owner_uid or value.st_gid != owner_gid or value.st_mtime_ns != row['mtimeNs']:
                    self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                if self.checksum(descriptor, check) != row['sha256']: self.reject('FILES_ARCHIVE_CONTENT_INVALID')
            finally: os.close(descriptor)
        if [os.fstat(target).st_dev,os.fstat(target).st_ino] != [info.st_dev,info.st_ino] or stat.S_IMODE(os.fstat(target).st_mode) != 0o700 or os.fstat(target).st_uid != 0:
            self.reject('FILES_ARCHIVE_TARGET_UNSAFE')
        return verified
