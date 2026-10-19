#!/usr/bin/env python3
"""Fixed registered-root file transport. No shell, arbitrary root, or transcript log.

JSON-lines over private stdin/stdout. Only regular files; Linux openat2 is
mandatory. Upload receipts distinguish receiving/applying/completed/unknown;
an uncertain operation is never automatically executed again.
"""
import base64
import contextlib
import ctypes
import errno
import fcntl
import hashlib
import heapq
import hmac
import importlib.util
import io
import json
import os
from pathlib import Path
import platform
import re
import select
import signal
import stat
import sys
import time
import uuid

REGISTRY = '/etc/whaledeck/file-roots.json'
BASE = '/data/WhaleDeck'
STATE = '/var/lib/whaledeck-files'
CHUNK = 32768
MAX_BYTES = 1024 * 1024 * 1024
SEARCH_VISITS = 10000
CONTENT_FILE_BYTES = 16 * 1024 * 1024
CONTENT_PAGE_BYTES = 64 * 1024 * 1024
LIST_ENTRIES_BYTES = 48 * 1024
STAGING = '.whaledeck-files'
PRIVATE = {'.git', '.ssh', '.gnupg', 'secrets', 'manifest.json', 'compose.yml', 'compose.yaml',
           'docker-compose.yml', 'docker-compose.yaml', 'resources.json', 'agent-capability.key'}
LIBC = ctypes.CDLL(None, use_errno=True)
# syscall(2) returns long; variadic arguments must have explicit native widths.
# In particular sizeof(open_how) is size_t, not ctypes' implicit signed int.
# Do not rely on implicit argument/register extension across C ABI variants.
LIBC.syscall.restype = ctypes.c_long


class Rejected(Exception):
    pass


class OpenHow(ctypes.Structure):
    _fields_ = [('flags', ctypes.c_uint64), ('mode', ctypes.c_uint64), ('resolve', ctypes.c_uint64)]


class InputLines:
    def __init__(self, descriptor):
        self.descriptor = descriptor
        self.buffer = bytearray()

    def readline(self, maximum):
        while True:
            index = self.buffer.find(b'\n')
            if index >= 0:
                value = bytes(self.buffer[:index + 1]); del self.buffer[:index + 1]
                return value
            if len(self.buffer) >= maximum:
                raise Rejected('FILES_INPUT_LIMIT')
            if not select.select([self.descriptor], [], [], 30)[0]:
                raise Rejected('FILES_INPUT_TIMEOUT')
            content = os.read(self.descriptor, 65536)
            if not content:
                value = bytes(self.buffer); self.buffer.clear(); return value
            self.buffer.extend(content)

    def finish(self):
        if self.buffer or not select.select([self.descriptor], [], [], 5)[0] or os.read(self.descriptor, 1):
            raise Rejected('FILES_COMMIT_TRAILING_DATA')


def open_at(root, path, flags, mode=0, cross_mount=False):
    if mode != 0 or flags & ~(os.O_RDONLY | os.O_DIRECTORY | os.O_NONBLOCK | os.O_NOFOLLOW):
        raise Rejected('FILES_OPEN_OPTIONS_INVALID')
    if platform.machine() not in ('x86_64', 'aarch64'):
        raise Rejected('FILES_KERNEL_UNSUPPORTED')
    # BENEATH | NO_SYMLINKS | NO_MAGICLINKS, and NO_XDEV below the registered root.
    how = OpenHow(flags | os.O_CLOEXEC, mode, 0x08 | 0x04 | 0x02 | (0 if cross_mount else 0x01))
    result = LIBC.syscall(ctypes.c_long(437), ctypes.c_int(root), ctypes.c_char_p(os.fsencode(path)),
                          ctypes.byref(how), ctypes.c_size_t(ctypes.sizeof(how)))
    if result < 0:
        number = ctypes.get_errno()
        if number in (errno.ENOSYS, errno.EINVAL):
            raise Rejected('FILES_KERNEL_UNSUPPORTED')
        raise OSError(number, 'FILES_PATH_REJECTED')
    return result


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise Rejected('FILES_DUPLICATE_FIELD')
        result[key] = value
    return result


def line(stream, timeout=30):
    if hasattr(stream, 'fileno'):
        try:
            descriptor = stream.fileno()
        except (OSError, ValueError):
            descriptor = None
        if descriptor is not None and not select.select([descriptor], [], [], timeout)[0]:
            raise Rejected('FILES_INPUT_TIMEOUT')
    value = stream.readline(65537)
    if not value or len(value) > 65536 or not value.endswith(b'\n'):
        raise Rejected('FILES_INPUT_TRUNCATED')
    try:
        result = json.loads(value, object_pairs_hook=unique_object)
    except (ValueError, UnicodeDecodeError):
        raise Rejected('FILES_INPUT_INVALID') from None
    if not isinstance(result, dict):
        raise Rejected('FILES_INPUT_INVALID')
    return result


def emit(value, output=None):
    output = output or sys.stdout.buffer
    encoded = json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n'
    if len(encoded) > 65536:
        raise Rejected('FILES_OUTPUT_LIMIT')
    output.write(encoded)
    output.flush()


def relative(value, empty=False):
    if not isinstance(value, str) or len(value.encode('utf-8')) > 4096 or '\\' in value or any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise Rejected('FILES_PATH_INVALID')
    if empty and value == '':
        return value
    parts = value.split('/')
    if any(part in ('', '.', '..') or len(part.encode('utf-8')) > 255 for part in parts):
        raise Rejected('FILES_PATH_INVALID')
    return value


def protected(path):
    return any(part.lower() in PRIVATE or part.lower().startswith(('.env', '.images.env', '.whaledeck-')) or
               part.lower().endswith(('.pem', '.key', '.p12', '.pfx', '.jks')) for part in path.split('/'))


def identity(value):
    if not isinstance(value, str) or not re.fullmatch(r'[a-f0-9]{32}', value) or uuid.UUID(value).int == 0:
        raise Rejected('FILES_OPERATION_INVALID')
    return value


def trusted_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    value = os.fstat(descriptor)
    if value.st_uid != 0 or value.st_mode & 0o022:
        os.close(descriptor)
        raise Rejected('FILES_PRIVATE_DIRECTORY_UNSAFE')
    return descriptor


@contextlib.contextmanager
def namespace_lock():
    """One nonblocking commit gate shared by uploads and file mutations."""
    state = trusted_directory(STATE); lock = None
    try:
        lock = os.open('namespace.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600, dir_fd=state)
        value = os.fstat(lock)
        if not stat.S_ISREG(value.st_mode) or value.st_uid != 0 or value.st_nlink != 1 or value.st_mode & 0o077:
            raise Rejected('FILES_RECEIPT_UNSAFE')
        try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: raise Rejected('FILES_OPERATION_BUSY') from None
        yield
    finally:
        if lock is not None: os.close(lock)
        os.close(state)


def roots():
    descriptor = os.open(REGISTRY, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022 or info.st_size > 65536:
            raise Rejected('FILES_REGISTRY_UNSAFE')
        data = os.read(descriptor, 65537)
    finally:
        os.close(descriptor)
    items = json.loads(data, object_pairs_hook=unique_object)
    if not isinstance(items, list):
        raise Rejected('FILES_REGISTRY_INVALID')
    found = {}
    for item in items:
        if not isinstance(item, dict) or item.get('type') != 'FileRoot':
            continue
        key, path = item.get('id'), item.get('path')
        if not isinstance(key, str) or not re.fullmatch(r'[a-z0-9.-]{1,64}', key) or key in found or item.get('protectionLevel') != 'Managed':
            raise Rejected('FILES_REGISTRY_INVALID')
        if not isinstance(item.get('externalId'), str) or not 1 <= len(item['externalId']) <= 80 or any(ord(c) < 32 for c in item['externalId']):
            raise Rejected('FILES_REGISTRY_INVALID')
        if not isinstance(path, str) or not path.startswith(BASE + '/') or relative(path[len(BASE) + 1:]) != path[len(BASE) + 1:] or protected(path):
            raise Rejected('FILES_REGISTRY_INVALID')
        actions = item.get('allowedActions', [])
        if not isinstance(actions, list) or not actions or any(action not in ('files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive', 'files-tree-copy', 'files-tree-move', 'files-tree-pack') for action in actions):
            raise Rejected('FILES_REGISTRY_INVALID')
        if any(type(item.get(field, 0)) is not int or not 0 <= item.get(field, 0) < 2 ** 31 for field in ('ownerUid', 'ownerGid')):
            raise Rejected('FILES_REGISTRY_INVALID')
        found[key] = item
    return found


@contextlib.contextmanager
def directory(request, action, allow_empty=False):
    root_id = request.get('rootId')
    registered = roots().get(root_id) if isinstance(root_id, str) else None
    required_actions = (action,) if isinstance(action, str) else action
    if registered is None or not all('files-' + required in registered['allowedActions'] for required in required_actions):
        raise Rejected('FILES_ROOT_FORBIDDEN')
    path = relative(request.get('path'), empty=allow_empty)
    if protected(path):
        raise Rejected('FILES_PROTECTED')
    # /data may be a dedicated disk. Cross-mount resolution is allowed only
    # while opening the root selected by the root-owned registry, never below it.
    slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
    root = parent = None
    try:
        root = open_at(slash, registered['path'].lstrip('/'), os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        parent_path, _, leaf = path.rpartition('/')
        parent = open_at(root, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY)
        yield root, parent, leaf
        if not isinstance(action, str) and roots().get(root_id) != registered:
            raise Rejected('FILES_ROOT_POLICY_CHANGED')
    finally:
        if parent is not None: os.close(parent)
        if root is not None: os.close(root)
        os.close(slash)


def version(info):
    return hashlib.sha256(json.dumps([info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
        info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns], separators=(',', ':')).encode()).hexdigest().upper()


def regular(descriptor):
    info = os.fstat(descriptor)
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        raise Rejected('FILES_REGULAR_SINGLE_LINK_REQUIRED')
    if info.st_size > MAX_BYTES:
        raise Rejected('FILES_SIZE_LIMIT')
    return info


def metadata(name, info):
    kind = 'Directory' if stat.S_ISDIR(info.st_mode) else 'File' if stat.S_ISREG(info.st_mode) else 'Symlink' if stat.S_ISLNK(info.st_mode) else 'Special'
    return {'name': name, 'kind': kind, 'sizeBytes': info.st_size, 'modifiedAtUnixMs': info.st_mtime_ns // 1000000,
            'version': version(info), 'mode': format(stat.S_IMODE(info.st_mode), '04o'),
            'isProtected': protected(name) or kind not in ('Directory', 'File') or kind == 'File' and info.st_nlink != 1}


def listing_page(folder, selected, take, before):
    # Count the actual UTF-8 JSON encoding, including quote escaping. A valid
    # 255-byte Linux name can double in JSON. Reserve frame space for the
    # cursor/version/envelope instead of increasing the private 64 KiB limit.
    rows = []; size = 0
    for name in selected[:take]:
        row = metadata(name, os.stat(name, dir_fd=folder, follow_symlinks=False))
        encoded_size = len(json.dumps(row, ensure_ascii=False, separators=(',', ':')).encode('utf-8')) + 1
        if size + encoded_size > LIST_ENTRIES_BYTES:
            if not rows: raise Rejected('FILES_OUTPUT_LIMIT')
            break
        rows.append(row); size += encoded_size
    return {'entries': rows, 'nextCursor': rows[-1]['name'] if len(selected) > len(rows) else '', 'version': before}


def list_directory(request):
    if set(request) != {'rootId', 'path', 'after', 'take'} or type(request['take']) is not int or not 1 <= request['take'] <= 100:
        raise Rejected('FILES_LIST_INVALID')
    after = relative(request['after'], empty=True)
    if '/' in after:
        raise Rejected('FILES_CURSOR_INVALID')
    with directory(request, 'list', allow_empty=True) as (root, parent, leaf):
        folder = open_at(parent, leaf or '.', os.O_RDONLY | os.O_DIRECTORY)
        try:
            before = version(os.fstat(folder))
            deadline = time.monotonic() + 5
            def names():
                with os.scandir(folder) as iterator:
                    for index, entry in enumerate(iterator):
                        if index >= 250000 or time.monotonic() > deadline:
                            raise Rejected('FILES_DIRECTORY_SCAN_LIMIT')
                        if entry.name.startswith('.whaledeck-'):
                            continue
                        try: relative(entry.name)
                        except (Rejected, UnicodeError): continue
                        if entry.name > after: yield entry.name
            selected = heapq.nsmallest(request['take'] + 1, names())
            result = listing_page(folder, selected, request['take'], before)
            if before != version(os.fstat(folder)):
                raise Rejected('FILES_VERSION_CONFLICT')
            return result
        finally:
            os.close(folder)


def browse_names(folder, after, deadline, directories=False):
    """Bounded lexical batches, no readdir-order or offset-as-snapshot assumption."""
    def candidates():
        with os.scandir(folder) as iterator:
            for index, entry in enumerate(iterator):
                if index >= 250000 or time.monotonic() > deadline:
                    raise Rejected('FILES_DIRECTORY_SCAN_LIMIT')
                try: relative(entry.name)
                except (Rejected, UnicodeError): continue
                if any(127 <= ord(character) < 160 for character in entry.name): continue
                if protected(entry.name) or entry.name <= after: continue
                if directories:
                    info = entry.stat(follow_symlinks=False)
                    if not stat.S_ISDIR(info.st_mode): continue
                    try: child = open_at(folder, entry.name, os.O_RDONLY | os.O_DIRECTORY)
                    except OSError: continue
                    else: os.close(child)
                yield entry.name
    return heapq.nsmallest(101 if directories else 128, candidates())


def list_directories(request):
    if set(request) != {'rootId', 'path', 'after', 'take'} or type(request['take']) is not int or not 1 <= request['take'] <= 100:
        raise Rejected('FILES_LIST_INVALID')
    after = relative(request['after'], empty=True)
    if '/' in after: raise Rejected('FILES_CURSOR_INVALID')
    with directory(request, 'list', allow_empty=True) as (_, parent, leaf):
        folder = open_at(parent, leaf or '.', os.O_RDONLY | os.O_DIRECTORY)
        try:
            before = version(os.fstat(folder))
            selected = browse_names(folder, after, time.monotonic() + 5, directories=True)
            result = listing_page(folder, selected, request['take'], before)
            rows = result['entries']
            if any(row['kind'] != 'Directory' or row['isProtected'] for row in rows) or before != version(os.fstat(folder)):
                raise Rejected('FILES_VERSION_CONFLICT')
            return result
        finally: os.close(folder)


def browse_key():
    state = trusted_directory(STATE)
    try:
        try: descriptor = os.open('browse-cursor.key', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=state)
        except FileNotFoundError:
            with namespace_lock():
                temporary = 'browse-cursor-' + uuid.uuid4().hex + '.key'
                key = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=state)
                try:
                    content = os.urandom(32); offset = 0
                    while offset < len(content):
                        written = os.write(key, content[offset:])
                        if written <= 0: raise Rejected('FILES_CURSOR_KEY_UNSAFE')
                        offset += written
                    os.fsync(key)
                    try: exchange(state, temporary, state, 'browse-cursor.key', 1)
                    except FileExistsError: pass
                    os.fsync(state)
                finally:
                    os.close(key)
                    try: os.unlink(temporary, dir_fd=state)
                    except FileNotFoundError: pass
            descriptor = os.open('browse-cursor.key', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=state)
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o077 or info.st_size != 32:
                raise Rejected('FILES_CURSOR_KEY_UNSAFE')
            key = os.read(descriptor, 33)
            if len(key) != 32: raise Rejected('FILES_CURSOR_KEY_UNSAFE')
            return key
        finally: os.close(descriptor)
    finally: os.close(state)


def search_cursor(value, scope):
    if not isinstance(value, str) or len(value) > 32768:
        raise Rejected('FILES_CURSOR_INVALID')
    if not value:
        return {'scope': scope, 'expires': int(time.time()) + 600, 'stack': [{'name': '', 'after': '', 'version': ''}], 'depthLimited': False}
    try:
        payload, mac = value.split('.')
        if not re.fullmatch('[A-Za-z0-9_-]+', payload) or not re.fullmatch('[a-f0-9]{64}', mac): raise ValueError()
        if not hmac.compare_digest(hmac.new(browse_key(), payload.encode('ascii'), hashlib.sha256).hexdigest(), mac): raise ValueError()
        result = json.loads(base64.urlsafe_b64decode(payload + '=' * (-len(payload) % 4)), object_pairs_hook=unique_object)
        if set(result) != {'scope', 'expires', 'stack', 'depthLimited'} or result['scope'] != scope or type(result['expires']) is not int or not time.time() < result['expires'] <= time.time() + 601:
            raise ValueError()
        if type(result['depthLimited']) is not bool or not isinstance(result['stack'], list) or not 1 <= len(result['stack']) <= 33: raise ValueError()
        names = []
        for index, frame in enumerate(result['stack']):
            if set(frame) != {'name', 'after', 'version'}: raise ValueError()
            relative(frame['name'], empty=index == 0); relative(frame['after'], empty=True)
            if '/' in frame['name'] or '/' in frame['after'] or index == 0 and frame['name'] != '' or protected(frame['name']): raise ValueError()
            if not isinstance(frame['version'], str) or not re.fullmatch('[A-F0-9]{64}', frame['version']): raise ValueError()
            if index: names.append(frame['name'])
        relative('/'.join(filter(None, [scope[1], *names])), empty=True)
        return result
    except (ValueError, TypeError, KeyError, UnicodeError):
        raise Rejected('FILES_CURSOR_INVALID') from None


def encode_search_cursor(value):
    payload = base64.urlsafe_b64encode(json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode()).decode().rstrip('=')
    token = payload + '.' + hmac.new(browse_key(), payload.encode('ascii'), hashlib.sha256).hexdigest()
    if len(token) > 32768: raise Rejected('FILES_CURSOR_LIMIT')
    return token


def content_match(descriptor, before, query, match_case, stats, deadline):
    """One complete, version-checked UTF-8 file; no regex or partial-prefix matches."""
    stats['scannedFiles'] += 1
    raw = bytearray()
    while len(raw) <= before.st_size:
        if time.monotonic() > deadline: return 'FileReadTimeLimit'
        chunk = os.read(descriptor, min(65536, before.st_size + 1 - len(raw)))
        if not chunk: break
        raw.extend(chunk); stats['readBytes'] += len(chunk)
    if version(before) != version(os.fstat(descriptor)) or len(raw) != before.st_size:
        raise Rejected('FILES_VERSION_CONFLICT')
    # Do not emit arbitrary binary/control payloads or decode with replacement.
    if any(byte in raw for byte in range(32) if byte not in (9, 10, 13)) or 127 in raw:
        stats['skippedNonTextFiles'] += 1; return None
    try: text = raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        stats['skippedNonTextFiles'] += 1; return None
    needle = query if match_case else query.casefold()
    with io.StringIO(text, newline=None) as lines:
        for number, line in enumerate(lines, 1):
            if time.monotonic() > deadline: return 'FileReadTimeLimit'
            candidate = line if match_case else line.casefold()
            position = candidate.find(needle)
            if position < 0: continue
            # Full Unicode case folding can expand characters (e.g. ß -> ss).
            # Map only a matched expanded line, without an unbounded per-char map.
            if not match_case and len(candidate) != len(line):
                low, high = 0, min(len(line), position + 1)
                while low < high:
                    if time.monotonic() > deadline: return 'FileReadTimeLimit'
                    middle = (low + high) // 2
                    if len(line[:middle].casefold()) > position: high = middle
                    else: low = middle + 1
                position = max(0, low - 1)
            line = line.rstrip('\n')
            start = max(0, position - 60)
            end = min(len(line), max(start + 240, position + len(query)))
            # At most 256-codepoint query + context, safely escaped by JSON/UI.
            end = min(end, start + 320)
            excerpt = ''.join(' ' if ord(character) < 32 or 127 <= ord(character) < 160 else character for character in line[start:end])
            if version(before) != version(os.fstat(descriptor)): raise Rejected('FILES_VERSION_CONFLICT')
            return {'lineNumber': number, 'excerpt': excerpt, 'excerptTruncated': start > 0 or end < len(line)}
    if version(before) != version(os.fstat(descriptor)): raise Rejected('FILES_VERSION_CONFLICT')
    return None


def search_content(request):
    if set(request) != {'rootId', 'path', 'query', 'matchCase', 'cursor', 'take', 'maxFileBytes'}:
        raise Rejected('FILES_SEARCH_INVALID')
    return search_files({**request, 'kind': 'File'}, content=True)


def search_files(request, content=False):
    allowed = {'rootId', 'path', 'query', 'kind', 'matchCase', 'cursor', 'take'}
    if content: allowed.add('maxFileBytes')
    if set(request) != allowed:
        raise Rejected('FILES_SEARCH_INVALID')
    query = request['query']
    if isinstance(query, str) and any(0xD800 <= ord(character) <= 0xDFFF for character in query):
        raise Rejected('FILES_SEARCH_INVALID')
    if not isinstance(query, str) or not query.strip() or len(query.encode('utf-8')) > 256 or any(ord(c) < 32 or 127 <= ord(c) < 160 for c in query) or (not content and ('/' in query or '\\' in query)) or request['kind'] not in ('All', 'File', 'Directory') or type(request['matchCase']) is not bool or type(request['take']) is not int or not 1 <= request['take'] <= 100:
        raise Rejected('FILES_SEARCH_INVALID')
    if content and (type(request['maxFileBytes']) is not int or not 1 <= request['maxFileBytes'] <= CONTENT_FILE_BYTES):
        raise Rejected('FILES_SEARCH_INVALID')
    scope = [request['rootId'], relative(request['path'], empty=True), query, request['kind'], request['matchCase']]
    if content: scope[3] = 'ContentUtf8V1'; scope.append(request['maxFileBytes'])
    state = search_cursor(request['cursor'], scope)
    rows = []; scanned = excluded = output_bytes = 0; reason = ''; hard_limit = False
    needle = query if request['matchCase'] else query.casefold()
    stats = {'scannedFiles': 0, 'readBytes': 0, 'skippedNonTextFiles': 0, 'skippedOversizedFiles': 0}
    deadline = time.monotonic() + 5
    with directory(request, ('list', 'download') if content else 'list', allow_empty=True) as (_, parent, leaf):
        start = open_at(parent, leaf or '.', os.O_RDONLY | os.O_DIRECTORY)
        try:
            # Retained ancestors must still match, not only the deepest frame.
            for index, frame in enumerate(state['stack']):
                descriptor = open_at(start, '/'.join(item['name'] for item in state['stack'][1:index + 1]) or '.', os.O_RDONLY | os.O_DIRECTORY)
                try:
                    if frame['version'] and frame['version'] != version(os.fstat(descriptor)): raise Rejected('FILES_VERSION_CONFLICT')
                finally: os.close(descriptor)
            while state['stack']:
                frame = state['stack'][-1]
                subpath = '/'.join(item['name'] for item in state['stack'][1:])
                folder = open_at(start, subpath or '.', os.O_RDONLY | os.O_DIRECTORY)
                try:
                    before = version(os.fstat(folder))
                    if frame['version'] and frame['version'] != before: raise Rejected('FILES_VERSION_CONFLICT')
                    frame['version'] = before
                    if scanned >= SEARCH_VISITS or time.monotonic() > deadline:
                        reason = 'ScanBudget'; break
                    try: selected = browse_names(folder, frame['after'], deadline)
                    except Rejected as error:
                        if str(error) != 'FILES_DIRECTORY_SCAN_LIMIT': raise
                        reason = 'DirectoryScanLimit'; hard_limit = True; break
                    descend = False
                    for name in selected:
                        if scanned >= SEARCH_VISITS or time.monotonic() > deadline:
                            reason = 'ScanBudget'; break
                        info = os.stat(name, dir_fd=folder, follow_symlinks=False)
                        entry = metadata(name, info)
                        path = '/'.join(filter(None, [request['path'], subpath, name]))
                        try: relative(path)
                        except Rejected:
                            excluded += 1; frame['after'] = name; scanned += 1; continue
                        child = None; match = None
                        if entry['kind'] == 'Directory':
                            try: child = open_at(folder, name, os.O_RDONLY | os.O_DIRECTORY)
                            except OSError: pass
                        elif entry['kind'] == 'File' and not entry['isProtected']:
                            if content and info.st_size <= request['maxFileBytes'] and stats['readBytes'] + info.st_size + 1 > CONTENT_PAGE_BYTES:
                                reason = 'ContentByteBudget'; break
                            # NO_XDEV rejects file bind mounts too; no content is read.
                            try: descriptor = open_at(folder, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                            except OSError: entry['isProtected'] = True
                            else:
                                try:
                                    if version(os.fstat(descriptor)) != entry['version']: raise Rejected('FILES_VERSION_CONFLICT')
                                    if content:
                                        if info.st_size > request['maxFileBytes']: stats['skippedOversizedFiles'] += 1
                                        else: match = content_match(descriptor, info, query, request['matchCase'], stats, deadline)
                                finally: os.close(descriptor)
                            if match == 'FileReadTimeLimit':
                                reason = 'FileReadTimeLimit'; hard_limit = True; break
                        eligible = not entry['isProtected'] and (entry['kind'] == 'File' or child is not None)
                        hit = eligible and bool(match) if content else eligible and (request['kind'] == 'All' or request['kind'] == entry['kind']) and needle in (name if request['matchCase'] else name.casefold())
                        row = {'path': path, 'entry': entry}
                        if content and hit: row.update(match)
                        size = len(json.dumps(row, ensure_ascii=False).encode())
                        if hit and output_bytes + size > 20000:
                            if child is not None: os.close(child)
                            reason = 'ResultBytes'; break
                        frame['after'] = name; scanned += 1
                        if not eligible: excluded += 1
                        if hit: rows.append(row); output_bytes += size
                        if child is not None:
                            try:
                                if len(state['stack']) <= 32:
                                    state['stack'].append({'name': name, 'after': '', 'version': version(os.fstat(child))}); descend = True
                                else: state['depthLimited'] = True
                            finally: os.close(child)
                        if len(rows) >= request['take']: reason = 'ResultPage'
                        if descend or reason: break
                    if before != version(os.fstat(folder)): raise Rejected('FILES_VERSION_CONFLICT')
                    if reason: break
                    if descend: continue
                    if len(selected) < 128: state['stack'].pop()
                finally: os.close(folder)
        finally: os.close(start)
    cursor = encode_search_cursor(state) if state['stack'] and not hard_limit else ''
    if not cursor and not reason and state['depthLimited']: reason = 'DepthLimit'
    response = {'entries': rows, 'nextCursor': cursor, 'scannedEntries': scanned, 'excludedEntries': excluded,
            'complete': not state['stack'] and not state['depthLimited'], 'stopReason': reason, 'depthLimited': state['depthLimited']}
    if content: response.update(stats)
    return response


def inspect(request):
    if set(request) != {'rootId', 'path'}:
        raise Rejected('FILES_STAT_INVALID')
    with directory(request, 'stat') as (_, parent, leaf):
        descriptor = open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISDIR(info.st_mode): info = regular(descriptor)
            return metadata(leaf, info)
        finally: os.close(descriptor)


def download(request, output=None):
    if set(request) != {'rootId', 'path', 'expectedVersion'}:
        raise Rejected('FILES_DOWNLOAD_INVALID')
    with directory(request, 'download') as (_, parent, leaf):
        descriptor = open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            info = regular(descriptor)
            if request['expectedVersion'] != version(info):
                raise Rejected('FILES_VERSION_CONFLICT')
            emit({'type': 'metadata', **metadata(leaf, info)}, output)
            digest = hashlib.sha256(); offset = 0
            while offset < info.st_size:
                content = os.read(descriptor, min(CHUNK, info.st_size - offset))
                if not content: raise Rejected('FILES_READ_CHANGED')
                emit({'type': 'chunk', 'offset': offset, 'data': base64.b64encode(content).decode('ascii')}, output)
                digest.update(content); offset += len(content)
            if version(regular(descriptor)) != version(info):
                raise Rejected('FILES_READ_CHANGED')
            emit({'type': 'end', 'sizeBytes': offset, 'sha256': digest.hexdigest().upper(), 'version': version(info)}, output)
        finally:
            os.close(descriptor)


def save_receipt(state, operation, value):
    temporary = '.' + operation + '-' + uuid.uuid4().hex
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=state)
    try:
        payload = json.dumps(value, separators=(',', ':')).encode()
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(payload); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, operation + '.json', src_dir_fd=state, dst_dir_fd=state)
        os.fsync(state)
    finally:
        with contextlib.suppress(FileNotFoundError): os.unlink(temporary, dir_fd=state)


def read_receipt(state, operation):
    try: descriptor = os.open(operation + '.json', os.O_RDONLY | os.O_NOFOLLOW, dir_fd=state)
    except FileNotFoundError: return None
    try:
        info = os.fstat(descriptor)
        if info.st_uid != 0 or info.st_mode & 0o077 or not stat.S_ISREG(info.st_mode) or info.st_size > 16384:
            raise Rejected('FILES_RECEIPT_UNSAFE')
        return json.loads(os.read(descriptor, 16385), object_pairs_hook=unique_object)
    finally: os.close(descriptor)


def exchange(source, name, target, leaf, flags):
    result = LIBC.renameat2(source, os.fsencode(name), target, os.fsencode(leaf), flags)
    if result != 0: raise OSError(ctypes.get_errno(), 'FILES_PUBLISH_REJECTED')


def staging(parent):
    try: os.mkdir(STAGING, 0o700, dir_fd=parent)
    except FileExistsError: pass
    descriptor = open_at(parent, STAGING, os.O_RDONLY | os.O_DIRECTORY)
    info = os.fstat(descriptor)
    if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
        os.close(descriptor); raise Rejected('FILES_STAGING_UNSAFE')
    return descriptor


def bound(request):
    allowed = {'rootId', 'path', 'operationId', 'owner', 'sizeBytes', 'sha256', 'expectedVersion'}
    if set(request) != allowed or type(request.get('sizeBytes')) is not int or not 0 <= request['sizeBytes'] <= MAX_BYTES:
        raise Rejected('FILES_UPLOAD_INVALID')
    identity(request['operationId'])
    if any(not isinstance(request.get(key), str) or not re.fullmatch(r'[A-F0-9]{64}', request[key]) for key in ('owner', 'sha256')):
        raise Rejected('FILES_UPLOAD_INVALID')
    if request['expectedVersion'] != 'ABSENT' and not re.fullmatch(r'[A-F0-9]{64}', request['expectedVersion'] or ''):
        raise Rejected('FILES_UPLOAD_INVALID')
    return hashlib.sha256(json.dumps(request, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()


def current(parent, leaf):
    try: descriptor = open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    except FileNotFoundError: return None
    try: return regular(descriptor)
    finally: os.close(descriptor)


def publication_identity(info):
    # rename changes ctime; content/mtime/mode/owner/identity must not change.
    return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid, info.st_nlink, info.st_size, info.st_mtime_ns]


def checksum(parent, leaf):
    descriptor = open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    try:
        before = regular(descriptor); digest = hashlib.sha256(); count = 0
        while count < before.st_size:
            value = os.read(descriptor, min(CHUNK, before.st_size - count))
            if not value: raise Rejected('FILES_READ_CHANGED')
            count += len(value); digest.update(value)
        if version(before) != version(regular(descriptor)): raise Rejected('FILES_READ_CHANGED')
        return digest.hexdigest().upper()
    finally: os.close(descriptor)


def recover(record, parent, private, state):
    if record['state'] != 'Applying': return record
    actual = current(parent, record['leaf'])
    pending = current(private, record['operationId'] + '.upload')
    valid = actual is not None and actual.st_ino == record['newInode'] and actual.st_dev == record['newDevice'] and checksum(parent, record['leaf']) == record['sha256']
    if valid and record.get('originalIdentity'):
        displaced = current(private, record['operationId'] + '.upload')
        valid = displaced is not None and publication_identity(displaced) == record['originalIdentity'] and checksum(private, record['operationId'] + '.upload') == record['originalSha256']
        if valid: record['previousRetained'] = True
    if valid:
        record.update(state='Completed', version=version(actual))
    elif pending is not None and pending.st_ino == record['newInode'] and pending.st_dev == record['newDevice']:
        # The private staging name still owns the new inode: rename never
        # published it. Cleanup must not touch the possibly changed target.
        os.unlink(record['operationId'] + '.upload', dir_fd=private); os.fsync(private)
        record['state'] = 'Aborted'
    else:
        # An ambiguous commit or concurrent external writer is not a license to
        # overwrite again. Preserve the private staged/displaced files for review.
        record['state'] = 'Unknown'
    save_receipt(state, record['operationId'], record)
    return record


def ensure_hdd_backup(record, parent, leaf, private, state, temporary):
    # This is only called before Applying/rename while holding both the
    # operation and namespace locks. A definite backup failure can abort the
    # owned stage; an uncertain publication cannot use this shortcut.
    try:
        record['hddBackup'] = backup_engine().ensure(record, parent, leaf)
        save_receipt(state, record['operationId'], record)
    except (Rejected, OSError) as error:
        code = str(error) if isinstance(error, Rejected) else 'FILES_BACKUP_UNAVAILABLE'
        if not code.startswith('FILES_BACKUP_'): raise
        pending = current(private, temporary)
        if pending is not None and pending.st_ino == record['newInode'] and pending.st_dev == record['newDevice']:
            os.unlink(temporary, dir_fd=private); os.fsync(private)
            record.update(state='Aborted', resultCode=code)
        else:
            record['state'] = 'Unknown'
        save_receipt(state, record['operationId'], record)
        raise Rejected(code) from None


def upload(request, source, staged_only=False):
    fingerprint = bound(request); operation = request['operationId']
    if staged_only: fingerprint = hashlib.sha256((fingerprint + ':stage-v1').encode()).hexdigest().upper()
    registered = roots().get(request['rootId'])
    if not registered or 'files-upload' not in registered['allowedActions']: raise Rejected('FILES_ROOT_FORBIDDEN')
    relative(request['path'])
    if protected(request['path']): raise Rejected('FILES_PROTECTED')
    state = trusted_directory(STATE)
    if len(os.listdir(state)) >= 20032:
        os.close(state); raise Rejected('FILES_RECEIPT_LIMIT')
    lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=state)
    try:
        try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: raise Rejected('FILES_OPERATION_BUSY') from None
        with directory(request, 'upload') as (_, parent, leaf):
            private = staging(parent); temporary = operation + '.upload'; created = False; descriptor = None
            record = read_receipt(state, operation)
            try:
                if record:
                    if record.get('fingerprint') != fingerprint: raise Rejected('FILES_IDEMPOTENCY_CONFLICT')
                    record = recover(record, parent, private, state)
                    if record['state'] not in ('Staged', 'Completed'): raise Rejected('FILES_PREVIOUS_OPERATION_INCOMPLETE')
                else:
                    original = current(parent, leaf)
                    if (version(original) if original else 'ABSENT') != request['expectedVersion']:
                        raise Rejected('FILES_VERSION_CONFLICT')
                    space = os.fstatvfs(parent)
                    reserve_bytes = max(512 * 1024 * 1024, space.f_blocks * space.f_frsize // 20) + 4 * MAX_BYTES
                    if space.f_bavail * space.f_frsize < request['sizeBytes'] + reserve_bytes:
                        raise Rejected('FILES_CAPACITY_REQUIRED')
                    if sum(name.endswith('.json') for name in os.listdir(state)) >= 10000:
                        raise Rejected('FILES_RECEIPT_LIMIT')
                    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=private)
                    created = True
                    saved = os.fstat(descriptor)
                    record = {'operationId': operation, 'owner': request['owner'], 'rootId': request['rootId'], 'path': request['path'],
                              'leaf': leaf, 'fingerprint': fingerprint, 'state': 'Receiving', 'newInode': saved.st_ino, 'newDevice': saved.st_dev,
                              'sizeBytes': request['sizeBytes'], 'sha256': request['sha256'], 'createdAtUnixMs': time.time_ns() // 1000000,
                              'expectedVersion': request['expectedVersion'], 'stagedOnly': staged_only}
                    if original:
                        record['originalIdentity'] = publication_identity(original)
                        record['originalSha256'] = checksum(parent, leaf)
                        latest = current(parent, leaf)
                        if latest is None or version(latest) != request['expectedVersion']: raise Rejected('FILES_VERSION_CONFLICT')
                    save_receipt(state, operation, record)
                digest = hashlib.sha256(); offset = 0
                while True:
                    row = line(source)
                    if row == {'type': 'commit'}:
                        if hasattr(source, 'finish'): source.finish()
                        elif source.read(1): raise Rejected('FILES_COMMIT_TRAILING_DATA')
                        break
                    if set(row) != {'type', 'offset', 'data'} or row['type'] != 'chunk' or type(row['offset']) is not int or row['offset'] != offset or not isinstance(row['data'], str) or len(row['data']) > 43692:
                        raise Rejected('FILES_CHUNK_INVALID')
                    try: content = base64.b64decode(row['data'], validate=True)
                    except ValueError: raise Rejected('FILES_CHUNK_INVALID') from None
                    if not content or len(content) > CHUNK or offset + len(content) > request['sizeBytes']:
                        raise Rejected('FILES_SIZE_MISMATCH')
                    digest.update(content); offset += len(content)
                    if created:
                        view = memoryview(content)
                        while view:
                            count = os.write(descriptor, view)
                            if count <= 0: raise Rejected('FILES_WRITE_FAILED')
                            view = view[count:]
                if offset != request['sizeBytes'] or digest.hexdigest().upper() != request['sha256']:
                    raise Rejected('FILES_CHECKSUM_MISMATCH')
                if not created:
                    actual = current(parent, leaf)
                    return {**public_receipt(record), 'replayed': True, 'bindingCurrent': actual is not None and version(actual) == record.get('version')}
                # Preserve ordinary permissions/ownership, never setuid/setgid.
                if original:
                    os.fchown(descriptor, original.st_uid, original.st_gid)
                    os.fchmod(descriptor, stat.S_IMODE(original.st_mode) & 0o777)
                else:
                    os.fchown(descriptor, registered.get('ownerUid', 0), registered.get('ownerGid', 0))
                os.fsync(descriptor)
                if staged_only:
                    os.fsync(private)
                    record['state'] = 'Staged'; save_receipt(state, operation, record)
                    return {**public_receipt(record), 'replayed': False, 'bindingCurrent': False}
                with namespace_lock():
                    latest = current(parent, leaf)
                    if (version(latest) if latest else 'ABSENT') != request['expectedVersion']:
                        raise Rejected('FILES_VERSION_CONFLICT')
                    if original:
                        ensure_hdd_backup(record, parent, leaf, private, state, temporary)
                        latest = current(parent, leaf)
                        if latest is None or version(latest) != request['expectedVersion']: raise Rejected('FILES_VERSION_CONFLICT')
                    record['state'] = 'Applying'; save_receipt(state, operation, record)
                    # NOREPLACE for creates; EXCHANGE retains the entire old object
                    # privately, instead of truncating or deleting it before commit.
                    exchange(private, temporary, parent, leaf, 1 if original is None else 2)
                    os.fsync(parent); os.fsync(private)
                    if original:
                        displaced = current(private, temporary)
                        if displaced is None or publication_identity(displaced) != record['originalIdentity'] or checksum(private, temporary) != record['originalSha256']:
                            record['state'] = 'Unknown'; save_receipt(state, operation, record)
                            raise Rejected('FILES_CONCURRENT_PUBLICATION_UNKNOWN')
                        record['previousRetained'] = True
                    actual = current(parent, leaf)
                    if actual is None or actual.st_ino != record['newInode'] or actual.st_dev != record['newDevice'] or checksum(parent, leaf) != request['sha256']:
                        record['state'] = 'Unknown'; save_receipt(state, operation, record)
                        raise Rejected('FILES_CONCURRENT_PUBLICATION_UNKNOWN')
                    record.update(state='Completed', version=version(actual)); save_receipt(state, operation, record)
                    return {**public_receipt(record), 'replayed': False, 'bindingCurrent': True}
            finally:
                if descriptor is not None: os.close(descriptor)
                if created and record and record['state'] == 'Receiving':
                    with contextlib.suppress(FileNotFoundError): os.unlink(temporary, dir_fd=private)
                    record['state'] = 'Aborted'; save_receipt(state, operation, record)
                os.close(private)
    finally:
        os.close(lock); os.close(state)


def staged_action(request, action):
    if set(request) != {'operationId', 'owner'} or action not in ('publish', 'discard'):
        raise Rejected('FILES_STATUS_INVALID')
    operation = identity(request['operationId']); state = trusted_directory(STATE); lock = None
    try:
        record = read_receipt(state, operation)
        if record and record.get('owner') != request['owner']: raise Rejected('FILES_OPERATION_NOT_FOUND')
        if not record and action != 'discard': raise Rejected('FILES_OPERATION_NOT_FOUND')
        if len(os.listdir(state)) >= 20032: raise Rejected('FILES_RECEIPT_LIMIT')
        lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=state)
        try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: raise Rejected('FILES_OPERATION_BUSY') from None
        # Re-read after acquiring the same lock held by receiving/publication.
        record = read_receipt(state, operation)
        if record is None and action == 'discard':
            if not isinstance(request['owner'], str) or not re.fullmatch(r'[A-F0-9]{64}', request['owner']): raise Rejected('FILES_UPLOAD_INVALID')
            if sum(name.endswith('.json') for name in os.listdir(state)) >= 10000: raise Rejected('FILES_RECEIPT_LIMIT')
            record = {'operationId': operation, 'owner': request['owner'], 'stagedOnly': True, 'canceledBeforeReceive': True,
                      'state': 'Aborted', 'sizeBytes': 0, 'sha256': '0' * 64}
            save_receipt(state, operation, record)
        if not record or record.get('owner') != request['owner']: raise Rejected('FILES_OPERATION_NOT_FOUND')
        if not record.get('stagedOnly'): raise Rejected('FILES_STAGED_OPERATION_REQUIRED')
        if record.get('canceledBeforeReceive'): return {**public_receipt(record), 'bindingCurrent': False}
        with directory({'rootId': record['rootId'], 'path': record['path']}, 'upload') as (_, parent, leaf):
            private = staging(parent); temporary = operation + '.upload'
            try:
                record = recover(record, parent, private, state)
                if record['state'] == 'Completed':
                    actual = current(parent, leaf)
                    return {**public_receipt(record), 'replayed': True, 'bindingCurrent': actual is not None and version(actual) == record.get('version')}
                if record['state'] in ('Unknown', 'Applying'):
                    return {**public_receipt(record), 'bindingCurrent': False}
                pending = current(private, temporary)
                owns_pending = pending is not None and pending.st_ino == record['newInode'] and pending.st_dev == record['newDevice']
                if action == 'discard' or record['state'] in ('Receiving', 'Aborted'):
                    if pending is not None and not owns_pending:
                        record['state'] = 'Unknown'
                    else:
                        if owns_pending: os.unlink(temporary, dir_fd=private); os.fsync(private)
                        record['state'] = 'Aborted'
                    save_receipt(state, operation, record)
                    return {**public_receipt(record), 'bindingCurrent': False}
                if record['state'] != 'Staged': raise Rejected('FILES_PREVIOUS_OPERATION_INCOMPLETE')
                if not owns_pending or pending.st_size != record['sizeBytes'] or checksum(private, temporary) != record['sha256']:
                    record['state'] = 'Unknown'; save_receipt(state, operation, record)
                    return {**public_receipt(record), 'bindingCurrent': False}
                with namespace_lock():
                    original = current(parent, leaf)
                    if (version(original) if original else 'ABSENT') != record['expectedVersion']:
                        os.unlink(temporary, dir_fd=private); os.fsync(private)
                        record['state'] = 'Aborted'; save_receipt(state, operation, record)
                        raise Rejected('FILES_VERSION_CONFLICT')
                    if original is not None and (publication_identity(original) != record['originalIdentity'] or checksum(parent, leaf) != record['originalSha256']):
                        raise Rejected('FILES_VERSION_CONFLICT')
                    if original is not None:
                        ensure_hdd_backup(record, parent, leaf, private, state, temporary)
                        latest = current(parent, leaf)
                        if latest is None or version(latest) != record['expectedVersion']: raise Rejected('FILES_VERSION_CONFLICT')
                    record['state'] = 'Applying'; save_receipt(state, operation, record)
                    exchange(private, temporary, parent, leaf, 1 if original is None else 2)
                    os.fsync(parent); os.fsync(private)
                    # Recovery verifies new inode/digest and any complete displaced object.
                    record = recover(record, parent, private, state)
                    return {**public_receipt(record), 'bindingCurrent': record['state'] == 'Completed'}
            finally: os.close(private)
    finally:
        if lock is not None: os.close(lock)
        os.close(state)


def public_receipt(record):
    return {key: record[key] for key in ('operationId', 'state', 'sizeBytes', 'sha256', 'version', 'previousRetained', 'hddBackup', 'resultCode') if key in record}


def status(request):
    if set(request) != {'operationId', 'owner'}:
        raise Rejected('FILES_STATUS_INVALID')
    operation = identity(request['operationId']); state = trusted_directory(STATE); lock = None
    try:
        record = read_receipt(state, operation)
        if not record or record.get('owner') != request['owner']: raise Rejected('FILES_OPERATION_NOT_FOUND')
        if record.get('canceledBeforeReceive'): return {**public_receipt(record), 'bindingCurrent': False, 'isInProgress': False}
        lock = os.open(operation + '.lock', os.O_WRONLY | os.O_NOFOLLOW, dir_fd=state)
        try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: return {**public_receipt(record), 'isInProgress': True}
        if record['state'] in ('Applying', 'Receiving'):
            with namespace_lock() if record['state'] == 'Applying' else contextlib.nullcontext():
                try:
                    with directory({'rootId': record['rootId'], 'path': record['path']}, 'upload') as (_, parent, leaf):
                        private = staging(parent)
                        try:
                            if record['state'] == 'Applying': record = recover(record, parent, private, state)
                            else:
                                pending = current(private, operation + '.upload')
                                if pending is not None and pending.st_ino == record['newInode'] and pending.st_dev == record['newDevice']:
                                    os.unlink(operation + '.upload', dir_fd=private); os.fsync(private)
                                    record['state'] = 'Aborted'
                                elif pending is None: record['state'] = 'Aborted'
                                else: record['state'] = 'Unknown'
                                save_receipt(state, operation, record)
                        finally: os.close(private)
                except (OSError, Rejected):
                    record['state'] = 'Unknown'; save_receipt(state, operation, record)
        try:
            with directory({'rootId': record['rootId'], 'path': record['path']}, 'stat') as (_, parent, leaf):
                actual = current(parent, leaf)
                binding = actual is not None and version(actual) == record.get('version')
        except (OSError, Rejected): binding = False
        return {**public_receipt(record), 'bindingCurrent': binding, 'isInProgress': False}
    finally:
        if lock is not None: os.close(lock)
        os.close(state)


@contextlib.contextmanager
def slot():
    state = trusted_directory(STATE); lease = None
    try:
        for index in range(4):
            candidate = os.open('slot-' + str(index) + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=state)
            try: fcntl.flock(candidate, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                os.close(candidate); continue
            lease = candidate; break
        if lease is None: raise Rejected('FILES_TRANSFER_LIMIT')
        yield
    finally:
        if lease is not None: os.close(lease)
        os.close(state)


def operation_engine():
    module_path = Path(__file__).with_name('whaledeck-file-operations.py')
    info = os.stat(module_path, follow_symlinks=False)
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('FILES_MODULE_UNSAFE')
    specification = importlib.util.spec_from_file_location('whaledeck_file_operations', module_path)
    module = importlib.util.module_from_spec(specification); specification.loader.exec_module(module)
    return module.FileOperations(sys.modules[__name__])


def backup_engine():
    module_path = Path(__file__).with_name('whaledeck-file-backups.py')
    info = os.stat(module_path, follow_symlinks=False)
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('FILES_MODULE_UNSAFE')
    specification = importlib.util.spec_from_file_location('whaledeck_file_backups', module_path)
    module = importlib.util.module_from_spec(specification); specification.loader.exec_module(module)
    return module.FileBackups(sys.modules[__name__])


def archive_engine():
    module_path = Path(__file__).with_name('whaledeck-file-archives.py')
    info = os.stat(module_path, follow_symlinks=False)
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('FILES_MODULE_UNSAFE')
    specification = importlib.util.spec_from_file_location('whaledeck_file_archives', module_path)
    module = importlib.util.module_from_spec(specification); specification.loader.exec_module(module)
    return module.FileArchives(sys.modules[__name__], backup_engine())


def tree_modules(moving=False):
    modules = []
    filenames = ('whaledeck-file-tree.py', 'whaledeck-tree-copies.py') + (('whaledeck-tree-moves.py',) if moving else ())
    for filename in filenames:
        path = Path(__file__).with_name(filename); info = os.stat(path, follow_symlinks=False)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
            raise Rejected('FILES_MODULE_UNSAFE')
        specification = importlib.util.spec_from_file_location(filename.replace('-', '_'), path)
        module = importlib.util.module_from_spec(specification); specification.loader.exec_module(module); modules.append(module)
    return modules


def tree_copy_engine():
    modules = tree_modules()
    return modules[1].FileTreeCopies(sys.modules[__name__], modules[0].FileTreeManifest)


def tree_move_engine():
    modules = tree_modules(True)
    return modules[2].FileTreeMoves(sys.modules[__name__], modules[0].FileTreeManifest, modules[1].FileTreeCopies)


def tree_packing_engine():
    modules = []
    for filename in ('whaledeck-file-tree.py','whaledeck-tree-archive.py','whaledeck-tree-packing.py'):
        path=Path(__file__).with_name(filename); info=os.stat(path,follow_symlinks=False)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072: raise Rejected('FILES_MODULE_UNSAFE')
        specification=importlib.util.spec_from_file_location(filename.replace('-','_'),path)
        module=importlib.util.module_from_spec(specification); specification.loader.exec_module(module); modules.append(module)
    return modules[2].FileTreePacking(sys.modules[__name__],modules[0].FileTreeManifest,modules[1].TreeArchive)


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('roots', 'list', 'directories', 'search', 'content-search', 'stat', 'download', 'upload', 'stage', 'publish', 'discard', 'status', 'operation-plan', 'operation-apply', 'operation-status', 'operation-discard', 'archive-list', 'archive-plan', 'archive-apply', 'archive-status', 'archive-discard', 'tree-copy-plan', 'tree-copy-apply', 'tree-copy-status', 'tree-copy-discard', 'tree-move-plan', 'tree-move-apply', 'tree-move-status', 'tree-move-discard', 'tree-pack-plan', 'tree-pack-apply', 'tree-pack-status', 'tree-pack-discard', 'tree-pack-download'):
        raise Rejected('FILES_ACTION_INVALID')
    invocation = identity(os.environ.get('INVOCATION_ID'))
    emit({'type': 'driver', 'invocationId': invocation})
    source = InputLines(sys.stdin.fileno())
    request = line(source)
    action = sys.argv[1]
    if action == 'roots':
        if request != {}: raise Rejected('FILES_ROOTS_INVALID')
        emit({'roots': [{'id': key, 'name': item['externalId'], 'actions': item['allowedActions']} for key, item in roots().items()]})
    elif action == 'list': emit(list_directory(request))
    elif action == 'directories': emit(list_directories(request))
    elif action == 'search': emit(search_files(request))
    elif action == 'content-search': emit(search_content(request))
    elif action == 'stat': emit(inspect(request))
    elif action == 'download': download(request)
    elif action == 'upload': emit(upload(request, source))
    elif action == 'stage': emit(upload(request, source, staged_only=True))
    elif action in ('publish', 'discard'): emit(staged_action(request, action))
    elif action.startswith('operation-'):
        source.finish(); engine = operation_engine()
        emit(engine.plan(request) if action == 'operation-plan' else engine.apply(request) if action == 'operation-apply' else engine.discard(request) if action == 'operation-discard' else engine.lookup(request))
    elif action.startswith('tree-pack-'):
        source.finish(); engine=tree_packing_engine()
        if action == 'tree-pack-download': engine.download(request)
        else: emit(engine.plan(request) if action == 'tree-pack-plan' else engine.pack(request) if action == 'tree-pack-apply' else engine.discard(request) if action == 'tree-pack-discard' else engine.lookup(request))
    elif action.startswith('tree-copy-'):
        source.finish(); engine = tree_copy_engine()
        emit(engine.plan(request) if action == 'tree-copy-plan' else engine.copy(request) if action == 'tree-copy-apply' else engine.discard(request) if action == 'tree-copy-discard' else engine.lookup(request))
    elif action.startswith('tree-move-'):
        source.finish(); engine = tree_move_engine()
        emit(engine.plan(request) if action == 'tree-move-plan' else engine.move(request) if action == 'tree-move-apply' else engine.discard(request) if action == 'tree-move-discard' else engine.lookup(request))
    elif action.startswith('archive-'):
        source.finish(); engine = archive_engine()
        emit(engine.list(request) if action == 'archive-list' else engine.plan(request) if action == 'archive-plan' else engine.apply(request) if action == 'archive-apply' else engine.discard(request) if action == 'archive-discard' else engine.lookup(request))
    else: emit(status(request))


if __name__ == '__main__':
    try:
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(Rejected('FILES_CANCELLED')))
        signal.alarm(600)
        with slot(): main()
    except BaseException as error:
        code = str(error) if isinstance(error, Rejected) and re.fullmatch(r'FILES_[A-Z_]{1,64}', str(error)) else 'FILES_IO_REJECTED'
        if isinstance(error, FileNotFoundError): code = 'FILES_NOT_FOUND'
        elif isinstance(error, PermissionError): code = 'FILES_ACCESS_DENIED'
        elif isinstance(error, OSError):
            code = {errno.ELOOP: 'FILES_PATH_REJECTED', errno.EXDEV: 'FILES_PATH_REJECTED', errno.ENOTDIR: 'FILES_PATH_REJECTED',
                    errno.EEXIST: 'FILES_VERSION_CONFLICT', errno.ENOSPC: 'FILES_CAPACITY_REQUIRED', errno.EDQUOT: 'FILES_CAPACITY_REQUIRED'}.get(error.errno, code)
        # Never emit an exception object, user path or file content.
        print(json.dumps({'errorCode': code}), file=sys.stderr)
        raise SystemExit(2) from None
