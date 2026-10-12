#!/usr/bin/env python3
"""Fixed-scope application archives. No restore, arbitrary paths, commands or downloads."""
from __future__ import annotations

import contextlib
import hashlib
import hmac
import json
import os
import re
import shutil
import signal
import stat
import subprocess
import sys
import tarfile
import time
import uuid
from pathlib import Path, PurePosixPath

APPS = Path('/data/WhaleDeck/apps')
TARGET = Path('/data/DockerData/whaledeck/application-backups')
STATE = Path('/var/lib/whaledeck-application-backup')
REGISTRY = Path('/etc/whaledeck/resources.json')
KEY = Path('/etc/whaledeck/agent-capability.key')
CONFIGURATION = ('compose.yml', '.env', 'manifest.json')
MAX_FILES = 200_000
MAX_BYTES = 100 * 1024**3
SLUG = re.compile(r'[a-z0-9][a-z0-9-]{0,47}\Z')
IDENTIFIER = re.compile(r'[a-f0-9]{64}\Z')
JOB = re.compile(r'[a-f0-9]{32}\Z')


class BackupError(Exception):
    """Never embeds file contents, CLI stderr or environment values."""


def require(condition):
    if not condition:
        raise BackupError('Application backup precondition failed.')


def safe_path(path: Path, *, directory=False):
    require(path.is_absolute())
    for ancestor in (path, *path.parents):
        require(not ancestor.is_symlink())
    if path.exists():
        require(path.is_dir() if directory else path.is_file())
    return path


def read_json(path: Path):
    safe_path(path)
    require(path.stat().st_size <= 65536)
    try:
        return json.loads(path.read_bytes())
    except (ValueError, UnicodeError):
        raise BackupError('Application backup metadata is invalid.') from None


def private_json(path: Path, value):
    safe_path(path)
    temporary = path.with_name('.' + path.name + '.' + uuid.uuid4().hex)
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(json.dumps(value, sort_keys=True, separators=(',', ':')).encode())
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def docker(*arguments):
    completed = subprocess.run(['/usr/bin/docker', *arguments], stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, timeout=30, check=False,
        env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': '/root', 'LANG': 'C.UTF-8'})
    require(completed.returncode == 0 and len(completed.stdout) <= 4 * 1024**2)
    return completed.stdout


def inspect_all():
    ids = docker('ps', '--no-trunc', '-aq').decode().split()
    require(len(ids) <= 1024 and all(IDENTIFIER.fullmatch(item) for item in ids))
    result = []
    for start in range(0, len(ids), 64):
        result.extend(json.loads(docker('inspect', '--type', 'container', *ids[start:start + 64])))
    return result


def registry():
    entries = read_json(REGISTRY)
    require(isinstance(entries, list))
    apps = next((item for item in entries if item.get('id') == 'whaledeck.apps'), {})
    require(apps.get('path') == str(APPS) and 'backup' in apps.get('allowedActions', []))
    return entries


def protected(container, entries):
    name = container.get('Name', '').lstrip('/')
    labels = container.get('Config', {}).get('Labels') or {}
    return (str(labels.get('io.whaledeck.protected', '')).lower() == 'true' or
        name.startswith(('database-platform-', 'whaledeck-api', 'whaledeck-worker', 'whaledeck-gateway')) or
        any(item.get('type') == 'Container' and item.get('externalId') == name and
            item.get('protectionLevel') != 'Managed' for item in entries))


def scope(slug):
    require(SLUG.fullmatch(slug) is not None)
    entries = registry()
    directory = safe_path(APPS / slug, directory=True)
    require(directory.is_dir())
    manifest = read_json(directory / 'manifest.json')
    require(manifest.get('slug') == slug)
    configuration = {}
    for name in CONFIGURATION:
        file = safe_path(directory / name)
        require(file.is_file() and file.stat().st_size <= 65536)
        configuration[name] = hashlib.sha256(file.read_bytes()).hexdigest()
    project = 'whaledeck-app-' + slug
    all_containers = inspect_all()
    members = [item for item in all_containers if
        (item.get('Config', {}).get('Labels') or {}).get('com.docker.compose.project') == project]
    require(members and len(members) <= 64 and not any(protected(item, entries) for item in members))
    volumes = {}
    def include_volume(name, *, mounted=False):
        require(name.startswith(project + '-') and
            re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}', name))
        inspected = json.loads(docker('volume', 'inspect', name))[0]
        labels = inspected.get('Labels') or {}
        owner = labels.get('com.docker.compose.project')
        require((mounted and owner in (None, project) or owner == project) and
            str(labels.get('io.whaledeck.protected', '')).lower() != 'true')
        require(inspected.get('Driver') == 'local' and not inspected.get('Options'))
        path = safe_path(Path(inspected['Mountpoint']), directory=True)
        require(path.name == '_data' and path.parent.name == name and path.parent.parent.name == 'volumes')
        volumes[name] = {'path': str(path), 'createdAt': inspected.get('CreatedAt', '')}

    # Include owned but currently unmounted volumes too; prefix alone is not proof of ownership.
    names = docker('volume', 'ls', '--filter', 'label=com.docker.compose.project=' + project,
        '--format', '{{.Name}}').decode().splitlines()
    require(len(names) <= 256)
    for name in names:
        include_volume(name)
    data = safe_path(directory / 'data', directory=True)
    for member in members:
        require(not member['State'].get('Restarting') and not member['State'].get('Dead'))
        for mount in member.get('Mounts', []):
            if mount['Type'] == 'bind':
                require(Path(mount['Source']) == data)
            elif mount['Type'] == 'volume':
                include_volume(mount.get('Name', ''), mounted=True)
            else:
                require(mount['Type'] == 'tmpfs')
    own_ids = {item['Id'] for item in members}
    for container in all_containers:
        if container['Id'] in own_ids:
            continue
        require(not any(mount.get('Name') in volumes or
            (mount.get('Type') == 'bind' and Path(mount['Source']) == data)
            for mount in container.get('Mounts', [])))
    material = {'slug': slug, 'configuration': configuration, 'data': str(data),
        'dataIdentity': [data.stat().st_dev, data.stat().st_ino] if data.exists() else None,
        'volumes': volumes, 'members': sorted([{'id': item['Id'],
            'image': item.get('Image', ''), 'running': item['State'].get('Running', False),
            'mounts': item.get('Mounts', [])} for item in members], key=lambda item: item['id'])}
    key = KEY.read_bytes()
    require(len(key) >= 32)
    fingerprint = hmac.new(key, json.dumps(material, sort_keys=True, separators=(',', ':')).encode(), hashlib.sha256).hexdigest()
    return directory, members, volumes, fingerprint


def plan(slug, consistency):
    require(consistency in ('live', 'quiesced'))
    _, members, volumes, fingerprint = scope(slug)
    require(not any(item['State'].get('Paused') for item in members))
    capacity = shutil.disk_usage(TARGET if TARGET.exists() else TARGET.parent.parent)
    return {'fingerprint': fingerprint, 'configurationFiles': list(CONFIGURATION), 'includeDataDirectory': True,
        'namedVolumes': sorted(volumes), 'excluded': ['container writable layers', 'tmpfs', 'external services'],
        'consistency': consistency, 'runningServices': sum(item['State'].get('Running', False) for item in members),
        'freeBytes': capacity.free, 'targetDirectoryId': 'database-platform.backup.hdd',
        'warning': 'Filesystem archive only; use a database-native backup for transaction consistency. '
            + ('Services are paused temporarily.' if consistency == 'quiesced' else 'Live files can change during the backup.')}


@contextlib.contextmanager
def lock(slug, *, blocking=True):
    import fcntl
    require(SLUG.fullmatch(slug) is not None)
    safe_path(STATE, directory=True)
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    path = safe_path(STATE / (slug + '.lock'))
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | getattr(os, 'O_NOFOLLOW', 0), 0o600)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | (0 if blocking else fcntl.LOCK_NB))
        yield
    finally:
        os.close(descriptor)


def journal_path(job):
    require(JOB.fullmatch(job) is not None)
    return STATE / (job + '.json')


def resume(journal):
    require(SLUG.fullmatch(journal.get('slug', '')) is not None)
    entries = registry()
    remaining = []
    identities = {item['Id']: item for item in inspect_all()} if journal.get('paused') else {}
    for identity in journal.get('paused', []):
        require(IDENTIFIER.fullmatch(identity) is not None)
        if identity not in identities:
            continue  # Successful Engine listing proves the original container no longer exists.
        try:
            item = identities[identity]
            require(not protected(item, entries) and
                (item.get('Config', {}).get('Labels') or {}).get('com.docker.compose.project') ==
                'whaledeck-app-' + journal['slug'])
            if item['State'].get('Paused'):
                docker('unpause', identity)
        except (BackupError, subprocess.TimeoutExpired):
            remaining.append(identity)
    journal['paused'] = remaining
    return not remaining


def recover_job(job):
    path = safe_path(journal_path(job))
    if not path.exists():
        return
    journal = read_json(path)
    require(SLUG.fullmatch(journal.get('slug', '')) is not None)
    try:
        with lock(journal['slug'], blocking=False):
            require(resume(journal))
            private_json(path, journal)
    except BlockingIOError:
        return  # A live helper owns the project; do not interfere.


def archive_filter():
    files = 0
    total = 0
    def validate(member):
        nonlocal files, total
        files += 1
        require(member.size >= 0)
        total += member.size
        require(files <= MAX_FILES and total <= MAX_BYTES and len(member.name) <= 4096)
        require(not member.isdev() and not member.isfifo() and (member.isfile() or member.isdir() or member.issym() or member.islnk()))
        return member
    return validate


def verify_archive(path):
    safe_path(path)
    require(path.is_file() and path.stat().st_size <= MAX_BYTES)
    names = set()
    name_bytes = 0
    validate = archive_filter()
    with tarfile.open(path, 'r|gz') as archive:
        for member in archive:
            name_bytes += len(member.name.encode('utf-8'))
            require(name_bytes <= 64 * 1024**2)
            require(not member.name.startswith('/') and '..' not in PurePosixPath(member.name).parts and member.name not in names)
            if member.islnk():
                require(not member.linkname.startswith('/') and '..' not in PurePosixPath(member.linkname).parts)
            names.add(member.name)
            validate(member)
            if member.isfile():
                stream = archive.extractfile(member)
                require(stream is not None)
                while stream.read(1024 * 1024):
                    pass
    require({f'configuration/{name}' for name in CONFIGURATION}.issubset(names))
    return checksum(path)


def checksum(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


class CapacityWriter:
    """Recheck the HDD reserve while streaming, not merely before a potentially large archive."""
    def __init__(self, stream, directory, critical):
        self.stream = stream
        self.directory = directory
        self.critical = critical
        self.bytes_since_check = 0
        self.next_check = 0.0

    def write(self, value):
        self.bytes_since_check += len(value)
        now = time.monotonic()
        if self.bytes_since_check >= 16 * 1024**2 or now >= self.next_check:
            capacity = shutil.disk_usage(self.directory)
            require(capacity.used * 100 < capacity.total * self.critical and capacity.free >= 256 * 1024**2)
            self.next_check = now + 1.0
            self.bytes_since_check = 0
        return self.stream.write(value)

    def __getattr__(self, name):
        return getattr(self.stream, name)


def target(slug):
    require(SLUG.fullmatch(slug) is not None)
    directory = safe_path(TARGET / slug, directory=True)
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(TARGET, 0o700)
    os.chmod(directory, 0o700)
    return directory


def verify(slug, job):
    require(SLUG.fullmatch(slug) is not None and JOB.fullmatch(job) is not None)
    directory = safe_path(TARGET / slug, directory=True)
    metadata = read_json(directory / (job + '.json'))
    path = directory / (job + '.tar.gz')
    require(metadata.get('backupId') == job and metadata.get('slug') == slug and
        metadata.get('relativePath') == str(path.relative_to(TARGET)))
    require(hmac.compare_digest(verify_archive(path), metadata.get('checksum', '')))
    metadata['verified'] = True
    return metadata


def run(slug, job, consistency, fingerprint, critical=90):
    require(JOB.fullmatch(job) is not None and IDENTIFIER.fullmatch(fingerprint) is not None and 51 <= critical <= 99)
    require(consistency in ('live', 'quiesced'))
    with lock(slug):
        directory, members, volumes, current = scope(slug)
        require(hmac.compare_digest(current, fingerprint) and not any(item['State'].get('Paused') for item in members))
        destination = target(slug)
        capacity = shutil.disk_usage(destination)
        require(capacity.used * 100 < capacity.total * critical and capacity.free >= 256 * 1024**2)
        artifact = destination / (job + '.tar.gz')
        metadata_path = destination / (job + '.json')
        if artifact.exists() or metadata_path.exists():
            return verify(slug, job)
        journal = {'slug': slug, 'paused': [], 'job': job}
        private_json(journal_path(job), journal)
        temporary = destination / (job + '.partial')
        descriptor = None
        result = None
        try:
            if consistency == 'quiesced':
                for member in members:
                    if member['State'].get('Running'):
                        journal['paused'].append(member['Id'])
                        private_json(journal_path(job), journal)  # Persist BEFORE pause, including timeout ambiguity.
                        docker('pause', member['Id'])
            require(hmac.compare_digest(scope(slug)[3], fingerprint))
            descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, 'wb') as stream:
                descriptor = None
                with tarfile.open(fileobj=CapacityWriter(stream, destination, critical), mode='w|gz', dereference=False) as archive:
                    validate = archive_filter()
                    for name in CONFIGURATION:
                        archive.add(safe_path(directory / name), arcname='configuration/' + name, recursive=False, filter=validate)
                    data = safe_path(directory / 'data', directory=True)
                    if data.exists():
                        archive.add(data, arcname='data', filter=validate)
                    for name, volume in sorted(volumes.items()):
                        archive.add(safe_path(Path(volume['path']), directory=True), arcname='volumes/' + name, filter=validate)
                stream.flush()
                os.fsync(stream.fileno())
            require(hmac.compare_digest(scope(slug)[3], fingerprint))
            digest = verify_archive(temporary)
            os.replace(temporary, artifact)
            result = {'backupId': job, 'slug': slug, 'relativePath': str(artifact.relative_to(TARGET)),
                'sizeBytes': artifact.stat().st_size, 'checksumAlgorithm': 'SHA256', 'checksum': digest,
                'verified': True, 'compression': 'gzip', 'consistency': consistency, 'transactionConsistent': False,
                'createdAtUnix': int(time.time()), 'namedVolumes': sorted(volumes), 'dataIncluded': data.exists(),
                'configurationIncluded': list(CONFIGURATION), 'excluded': ['container writable layers', 'tmpfs', 'external services']}
            private_json(metadata_path, result)
        finally:
            if descriptor is not None:
                os.close(descriptor)
            temporary.unlink(missing_ok=True)
            # systemd's ExecStopPost repeats recovery after forced termination; boot recovery covers power loss.
            with contextlib.suppress(ValueError):
                signal.signal(signal.SIGTERM, signal.SIG_IGN)
            resumed = resume(journal)
            private_json(journal_path(job), journal)
            require(resumed)
        return result


def main():
    require(os.geteuid() == 0)
    verb, *arguments = sys.argv[1:]
    if verb == 'plan' and len(arguments) == 2:
        result = plan(*arguments)
    elif verb == 'run' and len(arguments) == 5:
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(BackupError('Application backup canceled.')))
        result = run(*arguments[:4], critical=int(arguments[4]))
    elif verb == 'verify' and len(arguments) == 2:
        result = verify(*arguments)
    elif verb == 'recover-job' and len(arguments) == 1:
        recover_job(*arguments)
        return
    elif verb == 'recover-all' and not arguments:
        for path in STATE.glob('*.json'):
            if JOB.fullmatch(path.stem):
                recover_job(path.stem)
        return
    else:
        raise BackupError('Application backup action is invalid.')
    print(json.dumps(result, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except (BackupError, OSError, ValueError, TypeError, KeyError, tarfile.TarError, subprocess.TimeoutExpired):
        print('Application backup failed; consult operation status.', file=sys.stderr)
        raise SystemExit(1)
