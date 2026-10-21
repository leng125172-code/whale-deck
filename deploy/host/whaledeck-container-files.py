#!/usr/bin/env python3
"""Container file read engine; no shell/Exec, host-path input or archive extraction.

Not installed or exposed by the current helper/RPC. The read foundation pins
one running container PID/root and reuses mandatory openat2. Upload/publication
must add independent staging, HDD proof, plans/jobs and audit, never a Docker
archive PUT shortcut. No file content, environment or Docker JSON is logged.
"""
import base64
from datetime import datetime
import hashlib
import heapq
import importlib.util
import json
import os
from pathlib import Path
import re
import select
import stat
import sys
import tempfile
import time

STATE = Path('/var/lib/whaledeck-container-files')
MAX_BYTES = 1024 ** 3
CHUNK = 32768
PAGE_BYTES = 48 * 1024
SCAN_LIMIT = 250000
ID = re.compile('[a-f0-9]{64}')
INSTANCE = re.compile(r'([1-9][0-9]{0,9}):([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z)')
VIRTUAL = {'proc', 'sys', 'dev', 'run'}


class Rejected(Exception):
    pass


def trusted_module(filename, name):
    assert filename in ('whaledeck-files.py', 'whaledeck-container-terminal.py')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_FILES_MODULE_UNSAFE')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


def canonical_path(value, empty=True):
    if type(value) is not str:
        raise Rejected('CONTAINER_FILES_PATH_INVALID')
    try: encoded = value.encode('utf-8', errors='strict')
    except UnicodeError: raise Rejected('CONTAINER_FILES_PATH_INVALID') from None
    if len(encoded) > 4096 or '\\' in value or any(ord(c) < 32 or 127 <= ord(c) < 160 for c in value):
        raise Rejected('CONTAINER_FILES_PATH_INVALID')
    if value == '' and empty: return value
    if any(part in ('', '.', '..') or len(part.encode('utf-8')) > 255 for part in value.split('/')):
        raise Rejected('CONTAINER_FILES_PATH_INVALID')
    return value


def target_binding(identifier, instance):
    if type(identifier) is not str or not ID.fullmatch(identifier):
        raise Rejected('CONTAINER_FILES_ID_REQUIRED')
    match = INSTANCE.fullmatch(instance) if type(instance) is str else None
    if match is None or not 2 <= int(match[1]) <= 2147483647:
        raise Rejected('CONTAINER_FILES_INSTANCE_INVALID')
    try: datetime.fromisoformat(match[2].replace('Z', '+00:00'))
    except ValueError: raise Rejected('CONTAINER_FILES_INSTANCE_INVALID') from None
    return int(match[1])


def request_fields(request, action):
    allowed = {'containerId', 'instanceId', 'path'}
    if action == 'list': allowed |= {'after', 'take'}
    elif action == 'download': allowed |= {'expectedVersion'}
    elif action != 'stat': raise Rejected('CONTAINER_FILES_ACTION_INVALID')
    if type(request) is not dict or set(request) != allowed:
        raise Rejected('CONTAINER_FILES_REQUEST_INVALID')
    target_binding(request['containerId'], request['instanceId'])
    canonical_path(request['path'], empty=action != 'download')
    if action == 'list':
        canonical_path(request['after'])
        if '/' in request['after'] or type(request['take']) is not int or not 1 <= request['take'] <= 100:
            raise Rejected('CONTAINER_FILES_LIST_INVALID')
    if action == 'download' and (type(request['expectedVersion']) is not str or not re.fullmatch('[A-F0-9]{64}', request['expectedVersion'])):
        raise Rejected('CONTAINER_FILES_VERSION_INVALID')


def protected(path, files):
    parts = path.split('/')
    return bool(path) and (parts[0] in VIRTUAL or parts[:2] == ['var', 'run'] or
        any(part.lower() in ('shadow', 'gshadow') for part in parts) or files.protected(path))


def fingerprint(info):
    return (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
        info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


def version(request, path, info):
    value = [request['containerId'], request['instanceId'], path, *fingerprint(info)]
    return hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps(value, ensure_ascii=False,
        separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()


def metadata(request, path, info, files):
    kind = 'Directory' if stat.S_ISDIR(info.st_mode) else 'File' if stat.S_ISREG(info.st_mode) else 'Symlink' if stat.S_ISLNK(info.st_mode) else 'Special'
    return {'name': path.rsplit('/', 1)[-1], 'kind': kind, 'sizeBytes': info.st_size,
        'modifiedAtUnixMs': info.st_mtime_ns // 1000000, 'mode': format(stat.S_IMODE(info.st_mode), '04o'),
        'version': version(request, path, info),
        'isProtected': protected(path, files) or kind not in ('Directory', 'File') or kind == 'File' and info.st_nlink != 1}


def process_identity(pid, identifier):
    base = Path('/proc') / str(pid)
    raw = (base / 'stat').read_text(); tail = raw[raw.rfind(')') + 2:].split()
    if len(tail) < 20 or tail[0] in ('Z', 'X'):
        raise Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
    cgroups = (base / 'cgroup').read_text()
    if not re.search(r'(?:/docker/|/docker-)' + identifier + r'(?:\.scope)?(?:/|$)', cgroups, re.MULTILINE):
        raise Rejected('CONTAINER_FILES_PROCESS_TARGET_CHANGED')
    status = (base / 'status').read_text().splitlines()
    nspid = next((line.split()[1:] for line in status if line.startswith('NSpid:')), [])
    if len(nspid) < 2 or nspid[-1] != '1':
        raise Rejected('CONTAINER_FILES_NAMESPACE_INVALID')
    namespaces = tuple((base / 'ns' / kind).stat().st_ino for kind in ('pid', 'mnt'))
    if any(value == (Path('/proc/self/ns') / kind).stat().st_ino for kind, value in zip(('pid', 'mnt'), namespaces)):
        raise Rejected('CONTAINER_FILES_NAMESPACE_INVALID')
    return tail[19], *namespaces


def mount_fingerprint(container):
    # Only target identity/config, never Env, arguments, credentials or file data.
    value = {key: container.get(key) for key in ('Id', 'Name', 'Image', 'HostConfig', 'Mounts')}
    value['labels'] = (container.get('Config') or {}).get('Labels') or {}
    value['user'] = (container.get('Config') or {}).get('User') or ''
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).digest()


def is_core(container, resources):
    protected_ids = {row.get('externalId') for row in resources if row.get('type') == 'Container'}
    names = container.get('Names') or [container.get('Name', '')]
    labels = container.get('Labels') or (container.get('Config') or {}).get('Labels') or {}
    return container.get('Id') in protected_ids or any(name.lstrip('/') in protected_ids for name in names) or str(labels.get('io.whaledeck.protected', '')).lower() == 'true'


def core_sources(docker, resources):
    rows = docker.api('GET', '/containers/json?all=1')
    if type(rows) is not list or len(rows) > 1000 or any(type(row) is not dict for row in rows):
        raise Rejected('CONTAINER_FILES_DOCKER_RESPONSE_INVALID')
    result = []
    for row in rows:
        if not is_core(row, resources): continue
        for mount in row.get('Mounts') or []:
            if mount.get('Type') not in ('bind', 'volume'): continue
            source = Path(mount.get('Source', ''))
            if not source.is_absolute(): raise Rejected('CONTAINER_FILES_CORE_MOUNT_INVALID')
            result.append(source.resolve(strict=True))
    return result


def check_mounts(container, root, files, docker, resources):
    forbidden = core_sources(docker, resources)
    for mount in container.get('Mounts') or []:
        destination = mount.get('Destination')
        if type(destination) is not str or not destination.startswith('/'):
            raise Rejected('CONTAINER_FILES_MOUNT_INVALID')
        relative = canonical_path(destination[1:], empty=False)
        if protected(relative, files): raise Rejected('CONTAINER_FILES_MOUNT_PROTECTED')
        descriptor = files.open_at(root, relative, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
        try:
            info = os.fstat(descriptor)
            if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
                raise Rejected('CONTAINER_FILES_MOUNT_INVALID')
            if mount.get('Type') == 'tmpfs': continue
            source = Path(mount.get('Source', ''))
            if not source.is_absolute() or source != source.resolve(strict=True):
                raise Rejected('CONTAINER_FILES_MOUNT_IDENTITY_CHANGED')
            if any(source.is_relative_to(other) or other.is_relative_to(source) for other in forbidden):
                raise Rejected('CONTAINER_FILES_SHARED_CORE_MOUNT')
            if mount.get('Type') == 'volume':
                name = mount.get('Name', '')
                if type(name) is not str or not re.fullmatch(r'whaledeck-app-[a-z0-9][a-z0-9_.-]{0,127}', name):
                    raise Rejected('CONTAINER_FILES_VOLUME_INVALID')
                volume = docker.api('GET', '/volumes/' + name)
                if type(volume) is not dict or volume.get('Name') != name or volume.get('Driver') != 'local' or volume.get('Options') or volume.get('Mountpoint') != str(source):
                    raise Rejected('CONTAINER_FILES_VOLUME_INVALID')
            elif mount.get('Type') != 'bind':
                raise Rejected('CONTAINER_FILES_MOUNT_INVALID')
            actual = source.stat()
            if (info.st_dev, info.st_ino) != (actual.st_dev, actual.st_ino):
                raise Rejected('CONTAINER_FILES_MOUNT_IDENTITY_CHANGED')
        finally: os.close(descriptor)


class ContainerRoot:
    """Trusted construction only: bound request, Docker identity and pinned root FD."""
    def __init__(self, request, files, docker):
        self.request, self.files, self.docker = request, files, docker
        self.root = self.pidfd = None

    def __enter__(self):
        try:
            self.resources = self.docker.registry()
            self.container = self.docker.api('GET', '/containers/' + self.request['containerId'] + '/json')
            self.target = self.docker.target(self.container, self.request['containerId'], self.resources)
            if self.target['instanceId'] != self.request['instanceId']:
                raise Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
            self.pid = target_binding(self.target['containerId'], self.target['instanceId'])
            self.pidfd = os.pidfd_open(self.pid)
            self.identity = process_identity(self.pid, self.target['containerId'])
            self.root = os.open('/proc/' + str(self.pid) + '/root', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
            root_stat, host_stat = os.fstat(self.root), os.stat('/')
            if (root_stat.st_dev, root_stat.st_ino) == (host_stat.st_dev, host_stat.st_ino):
                raise Rejected('CONTAINER_FILES_HOST_ROOT')
            self.root_identity = root_stat.st_dev, root_stat.st_ino
            self.mounts = mount_fingerprint(self.container)
            check_mounts(self.container, self.root, self.files, self.docker, self.resources)
            self.revalidate(); return self
        except BaseException: self.__exit__(None, None, None); raise

    def current(self):
        try:
            if select.select([self.pidfd], [], [], 0)[0] or process_identity(self.pid, self.request['containerId']) != self.identity:
                raise Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
            root = os.stat('/proc/' + str(self.pid) + '/root')
        except (FileNotFoundError, ProcessLookupError):
            raise Rejected('CONTAINER_FILES_INSTANCE_CHANGED') from None
        if (root.st_dev, root.st_ino) != self.root_identity:
            raise Rejected('CONTAINER_FILES_ROOT_CHANGED')

    def revalidate(self):
        self.current()
        resources = self.docker.registry()
        container = self.docker.api('GET', '/containers/' + self.request['containerId'] + '/json')
        target = self.docker.target(container, self.request['containerId'], resources)
        if resources != self.resources or target != self.target or mount_fingerprint(container) != self.mounts:
            raise Rejected('CONTAINER_FILES_TARGET_CHANGED')
        check_mounts(container, self.root, self.files, self.docker, resources)
        self.current()

    def __exit__(self, *_):
        for field in ('root', 'pidfd'):
            descriptor = getattr(self, field, None)
            if descriptor is not None: os.close(descriptor); setattr(self, field, None)


def listing(request, session, files):
    request_fields(request, 'list')
    if protected(request['path'], files): raise Rejected('CONTAINER_FILES_PROTECTED')
    folder = files.open_at(session.root, request['path'] or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
    try:
        before = os.fstat(folder); deadline = time.monotonic() + 5
        def candidates():
            with os.scandir(folder) as iterator:
                for index, entry in enumerate(iterator):
                    if index >= SCAN_LIMIT or time.monotonic() > deadline:
                        raise Rejected('CONTAINER_FILES_SCAN_LIMIT')
                    try: canonical_path(entry.name, empty=False)
                    except Rejected: continue
                    if entry.name.startswith('.whaledeck-'): continue
                    if entry.name > request['after']: yield entry.name
        selected = heapq.nsmallest(request['take'] + 1, candidates())
        rows, size = [], 0
        for name in selected[:request['take']]:
            path = '/'.join(part for part in (request['path'], name) if part)
            row = metadata(request, path, os.stat(name, dir_fd=folder, follow_symlinks=False), files)
            length = len(json.dumps(row, ensure_ascii=False, separators=(',', ':')).encode()) + 1
            if size + length > PAGE_BYTES:
                if not rows: raise Rejected('CONTAINER_FILES_OUTPUT_LIMIT')
                break
            rows.append(row); size += length
        if fingerprint(before) != fingerprint(os.fstat(folder)):
            raise Rejected('CONTAINER_FILES_VERSION_CONFLICT')
        session.revalidate()
        return {'entries': rows, 'nextCursor': rows[-1]['name'] if len(selected) > len(rows) else '',
            'version': version(request, request['path'], before)}
    finally: os.close(folder)


def inspect(request, session, files):
    request_fields(request, 'stat')
    if protected(request['path'], files): raise Rejected('CONTAINER_FILES_PROTECTED')
    descriptor = files.open_at(session.root, request['path'] or '.', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISDIR(info.st_mode) and not (stat.S_ISREG(info.st_mode) and info.st_nlink == 1):
            raise Rejected('CONTAINER_FILES_REGULAR_SINGLE_LINK_REQUIRED')
        session.revalidate(); return metadata(request, request['path'], info, files)
    finally: os.close(descriptor)


def spool_directory():
    if not STATE.is_absolute() or STATE != STATE.resolve(strict=True):
        raise Rejected('CONTAINER_FILES_STATE_UNSAFE')
    info = STATE.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
        raise Rejected('CONTAINER_FILES_STATE_UNSAFE')
    return STATE


def download(request, session, files, output=None):
    request_fields(request, 'download')
    if protected(request['path'], files): raise Rejected('CONTAINER_FILES_PROTECTED')
    descriptor = files.open_at(session.root, request['path'], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise Rejected('CONTAINER_FILES_REGULAR_SINGLE_LINK_REQUIRED')
        if info.st_size > MAX_BYTES: raise Rejected('CONTAINER_FILES_SIZE_LIMIT')
        if request['expectedVersion'] != version(request, request['path'], info):
            raise Rejected('CONTAINER_FILES_VERSION_CONFLICT')
        directory = spool_directory(); capacity = os.statvfs(directory)
        if capacity.f_bavail * capacity.f_frsize < info.st_size + max(128 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20):
            raise Rejected('CONTAINER_FILES_CAPACITY_LOW')
        with tempfile.TemporaryFile(mode='w+b', dir=directory) as captured:
            if os.fstat(captured.fileno()).st_nlink != 0: raise Rejected('CONTAINER_FILES_STATE_UNSAFE')
            digest = hashlib.sha256(); copied = 0; deadline = time.monotonic() + 300
            while copied < info.st_size:
                session.current()
                if time.monotonic() > deadline: raise Rejected('CONTAINER_FILES_TIMEOUT')
                data = os.read(descriptor, min(CHUNK, info.st_size - copied))
                if not data: raise Rejected('CONTAINER_FILES_READ_CHANGED')
                remaining = os.statvfs(directory)
                reserve = max(128 * 1024**2, remaining.f_blocks * remaining.f_frsize // 20)
                if remaining.f_bavail * remaining.f_frsize < len(data) + reserve:
                    raise Rejected('CONTAINER_FILES_CAPACITY_LOW')
                captured.write(data); digest.update(data); copied += len(data)
            if fingerprint(info) != fingerprint(os.fstat(descriptor)):
                raise Rejected('CONTAINER_FILES_READ_CHANGED')
            captured.flush(); os.fsync(captured.fileno()); captured.seek(0)
            session.revalidate()
            # No metadata/content leaves before a complete stable capture.
            files.emit({'type': 'metadata', **metadata(request, request['path'], info, files)}, output)
            sent = 0
            while sent < copied:
                session.current()
                if time.monotonic() > deadline: raise Rejected('CONTAINER_FILES_TIMEOUT')
                data = captured.read(min(CHUNK, copied - sent))
                if not data: raise Rejected('CONTAINER_FILES_CAPTURE_CHANGED')
                files.emit({'type': 'chunk', 'offset': sent, 'data': base64.b64encode(data).decode('ascii')}, output); sent += len(data)
            session.revalidate()
            files.emit({'type': 'end', 'sizeBytes': sent, 'sha256': digest.hexdigest().upper(),
                'version': version(request, request['path'], info)}, output)
    finally: os.close(descriptor)


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('list', 'stat', 'download'):
        raise Rejected('CONTAINER_FILES_ACTION_INVALID')
    files = trusted_module('whaledeck-files.py', 'whaledeck_container_files_primitives')
    docker = trusted_module('whaledeck-container-terminal.py', 'whaledeck_container_files_docker')
    request = files.line(sys.stdin.buffer); request_fields(request, sys.argv[1])
    if protected(request['path'], files): raise Rejected('CONTAINER_FILES_PROTECTED')
    with ContainerRoot(request, files, docker) as session:
        if sys.argv[1] == 'list': files.emit(listing(request, session, files))
        elif sys.argv[1] == 'stat': files.emit(inspect(request, session, files))
        else: download(request, session, files)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        # Stable code only: never a path, Docker exception body or contents.
        value = str(error)
        code = value if re.fullmatch(r'(?:CONTAINER_FILES|FILES|CONTAINER)_[A-Z_]{1,80}', value) else 'CONTAINER_FILES_UNAVAILABLE'
        print(json.dumps({'type': 'error', 'code': code}), file=sys.stdout, flush=True)
        raise SystemExit(1) from None
