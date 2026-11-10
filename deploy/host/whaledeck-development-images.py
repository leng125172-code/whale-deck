#!/usr/bin/env python3
"""Root-only import of digest-pinned OCI content into the private Runner cache."""
import argparse
import contextlib
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import stat
import subprocess
import sys
import tarfile
import uuid

BASE = Path('/var/lib/whaledeck-development-images')
LIMIT = 8 * 1024**3
BLOCK = 1024**2
STOP_GRACE_SECONDS = 5
BLOB = re.compile(r'blobs/sha256/([a-f0-9]{64})\Z')


class ImageError(Exception):
    pass


def require(value, code):
    if not value:
        raise ImageError(code)


def load(name):
    path = Path(__file__).with_name('whaledeck-' + name + '.py')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@contextlib.contextmanager
def archive_stream(files, path):
    # Bind every component with openat2; an operator path must not silently
    # traverse a symlinked parent, magic link, or replaced leaf.
    slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
    try:
        bound = files.open_at(slash, os.path.abspath(path).lstrip('/'), os.O_PATH, cross_mount=True)
    finally:
        os.close(slash)
    try:
        before = os.fstat(bound)
        require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1
                and 0 < before.st_size <= LIMIT + 64 * 1024**2, 'OCI_ARCHIVE_UNSAFE')
        descriptor = os.open(f'/proc/self/fd/{bound}', os.O_RDONLY | os.O_NONBLOCK | os.O_CLOEXEC)
        with os.fdopen(descriptor, 'rb') as stream:
            yield stream
            after = os.fstat(stream.fileno())
            require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns) ==
                    (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns),
                    'OCI_ARCHIVE_CHANGED')
    finally:
        os.close(bound)


def stop_exporter(process):
    process.stdout.close()
    if process.poll() is not None:
        process.wait()
        return
    try:
        process.terminate()
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=STOP_GRACE_SECONDS)
    except subprocess.TimeoutExpired:
        process.kill()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            raise ImageError('OCI_EXPORT_CLEANUP_UNCONFIRMED') from None


def extract(stream, destination):
    """No extractall: admit only bounded regular OCI metadata/blob names."""
    seen, directories, total, count = set(), set(), 0, 0
    (destination / 'blobs/sha256').mkdir(mode=0o700, parents=True)
    (destination / 'blobs').chmod(0o700)
    def exact(length):
        data = stream.read(length)
        require(len(data) == length, 'OCI_ARCHIVE_TRUNCATED')
        return data
    # Parse individual USTAR headers without tarfile's automatic PAX/long-name
    # processing, which could allocate unbounded extended-header data first.
    while True:
            header = exact(512)
            if header == bytes(512):
                require(exact(512) == bytes(512), 'OCI_ARCHIVE_TRUNCATED')
                padding = stream.read(65537)
                require(len(padding) <= 65536 and not padding.strip(b'\0'), 'OCI_ARCHIVE_TRAILING_BYTES')
                break
            entry = tarfile.TarInfo.frombuf(header, 'utf-8', 'strict')
            count += 1
            require(count <= 1024, 'OCI_ARCHIVE_SIZE_INVALID')
            name = entry.name
            if entry.isdir():
                require(name in ('blobs', 'blobs/sha256') and name not in directories
                        and entry.size == 0, 'OCI_ARCHIVE_PATH_INVALID')
                directories.add(name)
                continue
            match = BLOB.fullmatch(name)
            require(entry.type in (tarfile.REGTYPE, tarfile.AREGTYPE)
                    and (match is not None or name in ('index.json', 'oci-layout', 'manifest.json')),
                    'OCI_ARCHIVE_PATH_INVALID')
            require(name not in seen and len(seen) < 512, 'OCI_ARCHIVE_DUPLICATE')
            seen.add(name)
            maximum = LIMIT if match else 1024**2
            require(type(entry.size) is int and 0 < entry.size <= maximum, 'OCI_ARCHIVE_SIZE_INVALID')
            total += entry.size
            require(total <= LIMIT, 'OCI_ARCHIVE_SIZE_INVALID')
            if name == 'manifest.json':
                # Docker's tag map is not authoritative OCI input.
                remaining = entry.size
                while remaining:
                    data = exact(min(BLOCK, remaining))
                    remaining -= len(data)
                exact((-entry.size) % 512)
                continue
            fd = os.open(destination / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            try:
                digest = hashlib.sha256()
                with os.fdopen(fd, 'wb', closefd=False) as output:
                    remaining = entry.size
                    while remaining:
                        data = exact(min(BLOCK, remaining))
                        output.write(data)
                        digest.update(data)
                        remaining -= len(data)
                    output.flush()
                    os.fsync(fd)
                require(not match or digest.hexdigest() == match[1], 'OCI_BLOB_DIGEST_INVALID')
            finally:
                os.close(fd)
            exact((-entry.size) % 512)
    require({'index.json', 'oci-layout'} <= seen and any(BLOB.fullmatch(name) for name in seen), 'OCI_ARCHIVE_INCOMPLETE')
    return dict(entries=len(seen), bytes=total)


def verify(files, rootfs, source, reference, staging):
    staging.mkdir(mode=0o700)
    (staging / 'blobs/sha256').mkdir(mode=0o700, parents=True)
    (staging / 'blobs').chmod(0o700)
    descriptors = []
    try:
        for path in (source, staging, staging / 'blobs/sha256'):
            descriptors.append(os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW))
        return rootfs.VerifiedLayout(files, *descriptors).stage(rootfs.reference_digest(reference))
    finally:
        for fd in descriptors:
            os.close(fd)


def cache(reference, archive_path=None):
    require(os.geteuid() == 0 and __debug__, 'ROOT_REQUIRED')
    os.umask(0o077)
    files, rootfs, storage = load('files'), load('development-rootfs'), load('development-storage')
    key = rootfs.reference_digest(reference)[7:]
    BASE.mkdir(mode=0o700, exist_ok=True)
    driver = storage.WorkspaceStorage(files)
    with driver.absolute(BASE.parent) as containing:
        driver.directory(containing)
        os.fsync(containing)
    with driver.absolute(BASE) as parent:
        driver.directory(parent, private=True)
        info = os.fstat(parent)
        driver.require_nvme(f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}')
        lock = os.open('.' + key + '.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC,
                       0o600, dir_fd=parent)
        try:
            lock_info = os.fstat(lock)
            require(stat.S_ISREG(lock_info.st_mode) and lock_info.st_uid == 0 and lock_info.st_nlink == 1
                    and stat.S_IMODE(lock_info.st_mode) == 0o600, 'CACHE_LOCK_UNSAFE')
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            name = '.prepare-' + key + '-' + uuid.uuid4().hex
            workspace = BASE / name
            space = os.fstatvfs(parent)
            require(space.f_bavail * space.f_frsize >= LIMIT + 32 * 1024**3, 'CACHE_RESERVE_INSUFFICIENT')
            workspace.mkdir(mode=0o700)
            try:
                target = BASE / key
                if target.exists() or target.is_symlink():
                    with driver.opened(parent, key, os.O_RDONLY | os.O_DIRECTORY) as current:
                        rootfs.private_directory(current)
                    provenance = verify(files, rootfs, target, reference, workspace / 'verified')
                    return dict(ready=True, replayed=True, **provenance)
                space = os.fstatvfs(parent)
                require(space.f_bavail * space.f_frsize >= 3 * LIMIT + 32 * 1024**3, 'CACHE_RESERVE_INSUFFICIENT')
                source = workspace / 'source'
                source.mkdir(mode=0o700)
                if archive_path is not None:
                    with archive_stream(files, archive_path) as stream:
                        exported = extract(stream, source)
                else:
                    # Local daemon export only; no pull, credentials or caller
                    # command is passed to API/Worker or a Runner.
                    process = subprocess.Popen(['docker', 'image', 'save', reference],
                        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
                    try:
                        exported = extract(process.stdout, source)
                        process.stdout.close()
                        require(process.wait(timeout=120) == 0, 'OCI_EXPORT_FAILED')
                    finally:
                        stop_exporter(process)
                provenance = verify(files, rootfs, source, reference, workspace / 'verified')
                source_fd = os.open(source, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    rootfs.write_metadata(source_fd, 'cache-receipt.json', dict(**provenance, **exported))
                finally:
                    os.close(source_fd)
                for directory in (source / 'blobs/sha256', source / 'blobs', source):
                    fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                    try:
                        os.fsync(fd)
                    finally:
                        os.close(fd)
                with driver.absolute(workspace) as origin:
                    files.exchange(origin, 'source', parent, key, 1)
                    os.fsync(parent)
                    os.fsync(origin)
                return dict(ready=True, replayed=False, **provenance, **exported)
            finally:
                # Only this invocation's new private staging child is removed.
                shutil.rmtree(workspace)
        finally:
            os.close(lock)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', required=True)
    parser.add_argument('--archive', type=Path)
    arguments = parser.parse_args()
    try:
        def interrupted(signum, frame):
            raise ImageError('CACHE_DEADLINE' if signum == signal.SIGALRM else 'CACHE_CANCELLED')
        signal.signal(signal.SIGALRM, interrupted)
        signal.signal(signal.SIGTERM, interrupted)
        signal.setitimer(signal.ITIMER_REAL, 600)
        print(json.dumps(cache(arguments.reference, arguments.archive)))
    except Exception as error:
        print(json.dumps(dict(cache='failed', error=str(error) if isinstance(error, ImageError) else type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
