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
SLUG = re.compile(r'[a-z0-9][a-z0-9-]{0,62}\Z')
IDENTIFIER = re.compile(r'[a-f0-9]{64}\Z')
JOB = re.compile(r'[a-f0-9]{32}\Z')
CANCELED = False


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


def read_json(path: Path, *, limit=65536):
    safe_path(path)
    require(path.stat().st_size <= limit)
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
        env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': str(STATE), 'LANG': 'C.UTF-8'})
    require(completed.returncode == 0 and len(completed.stdout) <= 4 * 1024**2)
    return completed.stdout


def capacity():
    # Same approved HDD tree as application archives and database-platform backups.
    registry()
    destination = safe_path(TARGET, directory=True)
    base = safe_path(TARGET.parents[1], directory=True)
    require(destination.is_dir() and base.is_dir() and destination.stat().st_dev == base.stat().st_dev)
    usage = shutil.disk_usage(destination)
    require(usage.total > 0 and 0 <= usage.used <= usage.total and 0 <= usage.free <= usage.total)
    return {'targetDirectoryId': 'database-platform.backup.hdd', 'totalBytes': usage.total,
        'usedBytes': usage.used, 'freeBytes': usage.free, 'observedAtUnixMs': time.time_ns() // 1_000_000}


def inspect_all():
    ids = docker('ps', '--no-trunc', '-aq').decode().split()
    require(len(ids) <= 1024 and all(IDENTIFIER.fullmatch(item) for item in ids))
    result = []
    # Do not capture Env/Cmd/Entrypoint for unrelated containers into Python stdout.
    projection = '{"Id":{{json .Id}},"Name":{{json .Name}},"Image":{{json .Image}},"State":{"Running":{{json .State.Running}},"Paused":{{json .State.Paused}},"Restarting":{{json .State.Restarting}},"Dead":{{json .State.Dead}},"StartedAt":{{json .State.StartedAt}}},"RestartCount":{{json .RestartCount}},"Mounts":{{json .Mounts}},"Config":{"Labels":{"com.docker.compose.project":{{json (index .Config.Labels "com.docker.compose.project")}},"com.docker.compose.service":{{json (index .Config.Labels "com.docker.compose.service")}},"com.docker.compose.config-hash":{{json (index .Config.Labels "com.docker.compose.config-hash")}},"io.whaledeck.protected":{{json (index .Config.Labels "io.whaledeck.protected")}}}}}'
    for start in range(0, len(ids), 8):
        result.extend(json.loads(line) for line in docker('inspect', '--format', projection, '--type', 'container',
            *ids[start:start + 8]).decode().splitlines() if line)
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
        require((name.startswith(project + '-') or mounted and IDENTIFIER.fullmatch(name)) and
            re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}', name))
        inspected = json.loads(docker('volume', 'inspect', name))[0]
        labels = inspected.get('Labels') or {}
        owner = labels.get('com.docker.compose.project')
        require((mounted and owner in (None, project) or owner == project) and
            str(labels.get('io.whaledeck.protected', '')).lower() != 'true')
        require(inspected.get('Driver') == 'local' and not inspected.get('Options'))
        path = safe_path(Path(inspected['Mountpoint']), directory=True)
        require(path.name == '_data' and path.parent.name == name and path.parent.parent.name == 'volumes')
        require(path.is_dir())
        volumes[name] = {'path': str(path), 'createdAt': inspected.get('CreatedAt', ''),
            'identity': [path.stat().st_dev, path.stat().st_ino]}

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
    sources = [data, *(Path(volume['path']) for volume in volumes.values())]
    for container in all_containers:
        if container['Id'] in own_ids:
            continue
        require(not any(mount.get('Name') in volumes or
            (mount.get('Type') == 'bind' and any(paths_overlap(Path(mount['Source']).resolve(), source) for source in sources))
            for mount in container.get('Mounts', [])))
    material = {'slug': slug, 'configuration': configuration, 'data': str(data),
        'dataIdentity': [data.stat().st_dev, data.stat().st_ino] if data.exists() else None,
        'volumes': volumes, 'members': sorted([{'id': item['Id'],
            'image': item.get('Image', ''), 'running': item['State'].get('Running', False),
            'startedAt': item['State'].get('StartedAt', ''), 'restartCount': item.get('RestartCount', 0),
            'composeConfigHash': (item.get('Config', {}).get('Labels') or {}).get('com.docker.compose.config-hash', ''),
            'mounts': item.get('Mounts', [])} for item in members], key=lambda item: item['id'])}
    key = KEY.read_bytes()
    require(len(key) >= 32)
    fingerprint = hmac.new(key, json.dumps(material, sort_keys=True, separators=(',', ':')).encode(), hashlib.sha256).hexdigest()
    return directory, members, volumes, fingerprint


def paths_overlap(left, right):
    return left == right or left in right.parents or right in left.parents


def plan(slug, consistency, critical=90):
    require(consistency in ('live', 'quiesced'))
    require(51 <= critical <= 99)
    directory, members, volumes, fingerprint = scope(slug)
    require(not any(item['State'].get('Paused') for item in members))
    capacity = shutil.disk_usage(TARGET if TARGET.exists() else TARGET.parent.parent)
    require(capacity.used * 100 < capacity.total * critical and capacity.free >= 256 * 1024**2)
    data_exists = (directory / 'data').exists()
    return {'fingerprint': fingerprint, 'configurationFiles': list(CONFIGURATION), 'includeDataDirectory': data_exists,
        'namedVolumes': sorted(volumes), 'excluded': ['container writable layers', 'tmpfs', 'external services'],
        'consistency': consistency, 'runningServices': sum(item['State'].get('Running', False) for item in members),
        'freeBytes': capacity.free, 'targetDirectoryId': 'database-platform.backup.hdd',
        'warning': 'Filesystem archive only; use a database-native backup for transaction consistency. '
            + ('Services are paused temporarily.' if consistency == 'quiesced' else 'Live files can change during the backup.')
            + ('' if data_exists or volumes else ' No persistent data was found; configuration only.')}


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
    journal = read_json(path, limit=1024 * 1024)
    require(SLUG.fullmatch(journal.get('slug', '')) is not None)
    try:
        with lock(journal['slug'], blocking=False):
            require(resume(journal))
            if journal.get('cleanup'):
                if journal.get('pendingCleanup'):
                    finish_cleanup_entry(journal)
                    private_json(path, journal)
                receipt_path = STATE / (job + '.done.json')
                receipt = read_json(receipt_path) if receipt_path.exists() else {
                    'state': 'Failed', 'phase': 'Interrupted', 'slug': journal['slug'], 'job': job,
                    'errorCode': 'APPLICATION_BACKUP_CLEANUP_INTERRUPTED'}
                receipt['result'] = cleanup_result(journal)
                private_json(receipt_path, receipt)
            partial = safe_path(TARGET / journal['slug'] / (job + '.partial'))
            partial.unlink(missing_ok=True)
            path.unlink(missing_ok=True)
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


def archive_catalog(slug):
    """Only sealed root-owned archive pairs. Orphan/unknown files are retained, never guessed."""
    require(SLUG.fullmatch(slug) is not None)
    directory = safe_path(TARGET / slug, directory=True)
    if not directory.exists(): return []
    files = list(directory.glob('*.json'))
    require(len(files) <= 1024)
    entries = []
    for metadata_path in files:
        if not JOB.fullmatch(metadata_path.stem): continue
        safe_path(metadata_path)
        metadata = read_json(metadata_path)
        identity = metadata_path.stem
        artifact = safe_path(directory / (identity + '.tar.gz'))
        require(artifact.is_file() and metadata.get('slug') == slug and metadata.get('backupId') == identity and
            metadata.get('relativePath') == slug + '/' + identity + '.tar.gz' and metadata.get('verified') is True and
            IDENTIFIER.fullmatch(metadata.get('checksum', '')) is not None and metadata.get('checksumAlgorithm') == 'SHA256')
        archive_stat, metadata_stat = artifact.stat(), metadata_path.stat()
        require(archive_stat.st_size == metadata.get('sizeBytes') and
            isinstance(metadata.get('createdAtUnix'), int) and 0 <= metadata['createdAtUnix'] <= int(time.time()) + 300)
        created_ns = metadata.get('createdAtUnixNs', metadata['createdAtUnix'] * 1_000_000_000)
        require(type(created_ns) is int and metadata['createdAtUnix'] * 1_000_000_000 <= created_ns <
            (metadata['createdAtUnix'] + 1) * 1_000_000_000)
        if os.name == 'posix':
            require(archive_stat.st_uid == metadata_stat.st_uid == os.geteuid() and
                stat.S_IMODE(archive_stat.st_mode) == stat.S_IMODE(metadata_stat.st_mode) == 0o600)
        entries.append({'backupId': identity, 'createdAtUnix': metadata['createdAtUnix'],
            'createdAtUnixNs': created_ns,
            'identity': [archive_stat.st_dev, archive_stat.st_ino, archive_stat.st_size],
            'changedAtNs': archive_stat.st_ctime_ns,
            'metadataDigest': hashlib.sha256(metadata_path.read_bytes()).hexdigest()})
    return sorted(entries, key=lambda item: (item['createdAtUnixNs'], item['backupId']), reverse=True)


def cleanup_plan(slug, retention_days, retention_count):
    require(1 <= retention_days <= 3650 and 1 <= retention_count <= 365)
    require(SLUG.fullmatch(slug) is not None)
    entries = registry()
    members = [item for item in inspect_all() if (item.get('Config', {}).get('Labels') or {}).get('com.docker.compose.project') == 'whaledeck-app-' + slug]
    require(not any(protected(item, entries) for item in members))
    source = {'slug': slug, 'registry': entries, 'memberIds': sorted(item['Id'] for item in members)}
    catalog = archive_catalog(slug)
    cutoff = int(time.time()) - retention_days * 86400
    candidates = [entry for index, entry in enumerate(catalog) if index > 0 and
        (index >= retention_count or entry['createdAtUnix'] < cutoff)]
    material = json.dumps({'source': source, 'catalog': catalog, 'candidates': candidates,
        'retentionDays': retention_days, 'retentionCount': retention_count}, sort_keys=True).encode()
    key = KEY.read_bytes()
    require(len(key) == 32)
    return {'fingerprint': hmac.new(key, material, hashlib.sha256).hexdigest(),
        'removeBackupIds': [entry['backupId'] for entry in candidates], 'retainedCount': len(catalog) - len(candidates),
        'warning': 'Only verified private archives of this application are removed; newest archive is always retained. Runtime data is untouched.'}


def cleanup_result(journal):
    removed = journal.get('removedBackupIds', [])
    require(isinstance(removed, list) and len(removed) <= 1024 and all(JOB.fullmatch(item) for item in removed))
    return {'removedBackupIds': removed, 'retentionDays': journal['retentionDays'], 'retentionCount': journal['retentionCount']}


def finish_cleanup_entry(journal):
    entry = journal['pendingCleanup']
    identity = entry['backupId']
    require(JOB.fullmatch(identity) is not None and SLUG.fullmatch(journal['slug']) is not None)
    directory = safe_path(TARGET / journal['slug'], directory=True)
    artifact = safe_path(directory / (identity + '.tar.gz'))
    metadata = safe_path(directory / (identity + '.json'))
    if metadata.exists():
        require(hmac.compare_digest(hashlib.sha256(metadata.read_bytes()).hexdigest(), entry['metadataDigest']))
    if artifact.exists():
        info = artifact.stat()
        require([info.st_dev, info.st_ino, info.st_size] == entry['identity'] and info.st_ctime_ns == entry['changedAtNs'])
    artifact.unlink(missing_ok=True)
    metadata.unlink(missing_ok=True)
    if identity not in journal['removedBackupIds']: journal['removedBackupIds'].append(identity)
    journal['pendingCleanup'] = None


def cleanup(slug, job, retention_days, retention_count, fingerprint):
    require(JOB.fullmatch(job) is not None and IDENTIFIER.fullmatch(fingerprint) is not None)
    with lock(slug):
        planned = cleanup_plan(slug, retention_days, retention_count)
        require(hmac.compare_digest(planned['fingerprint'], fingerprint))
        candidates = {entry['backupId']: entry for entry in archive_catalog(slug)}
        journal = {'slug': slug, 'job': job, 'paused': [], 'cleanup': True, 'pendingCleanup': None,
            'removedBackupIds': [], 'retentionDays': retention_days, 'retentionCount': retention_count}
        private_json(journal_path(job), journal)
        for identity in planned['removeBackupIds']:
            journal['pendingCleanup'] = candidates[identity]
            private_json(journal_path(job), journal)  # Intent persisted before either unlink.
            finish_cleanup_entry(journal)
            private_json(journal_path(job), journal)
        return cleanup_result(journal)


def verify(slug, job):
    require(SLUG.fullmatch(slug) is not None and JOB.fullmatch(job) is not None)
    directory = safe_path(TARGET / slug, directory=True)
    metadata = read_json(directory / (job + '.json'))
    path = directory / (job + '.tar.gz')
    require(metadata.get('backupId') == job and metadata.get('slug') == slug and
        metadata.get('relativePath') == path.relative_to(TARGET).as_posix())
    require(hmac.compare_digest(verify_archive(path), metadata.get('checksum', '')))
    metadata['verified'] = True
    return metadata


def verify_plan(slug, job):
    require(SLUG.fullmatch(slug) is not None and JOB.fullmatch(job) is not None)
    directory = safe_path(TARGET / slug, directory=True)
    metadata = read_json(directory / (job + '.json'))
    artifact = safe_path(directory / (job + '.tar.gz'))
    require(metadata.get('slug') == slug and metadata.get('backupId') == job and artifact.is_file())
    material = json.dumps(metadata, sort_keys=True).encode() + str(artifact.stat().st_size).encode()
    key = KEY.read_bytes()
    require(len(key) == 32)
    return {'fingerprint': hmac.new(key, material, hashlib.sha256).hexdigest(),
        'backupId': job, 'consistency': metadata.get('consistency', 'unknown'),
        'sizeBytes': artifact.stat().st_size, 'targetDirectoryId': 'database-platform.backup.hdd',
        'warning': 'Read-only archive and SHA256 verification. No restore is performed.'}


def status(slug, job):
    require(SLUG.fullmatch(slug) is not None and JOB.fullmatch(job) is not None)
    receipt = safe_path(STATE / (job + '.done.json'))
    unit = 'whaledeck-application-backup-' + job + '.service'
    completed = subprocess.run(['/usr/bin/systemctl', 'show', unit, '--property=ActiveState',
        '--property=SubState', '--property=LoadState'], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        timeout=20, check=False, env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'LANG': 'C.UTF-8'})
    fields = dict(line.split('=', 1) for line in completed.stdout.decode().splitlines() if '=' in line)
    if fields.get('ActiveState') in ('active', 'activating', 'deactivating'):
        return {'state': 'Running', 'phase': fields.get('SubState', 'Executing')}
    # A polling/daemon error does not prove that the independent service stopped.
    require(fields.get('LoadState') in ('loaded', 'not-found'))
    journal = safe_path(journal_path(job))
    if journal.exists():
        snapshot = read_json(journal, limit=1024 * 1024)
        if snapshot.get('paused') or snapshot.get('cleanup'):
            return {'state': 'Running', 'phase': 'WaitingForBackupCleanup' if snapshot.get('cleanup') else 'WaitingForApplicationResume'}
    if receipt.exists():
        result = read_json(receipt)
        require(result.get('slug') == slug and result.get('job') == job)
        return result
    return {'state': 'Failed', 'phase': 'Interrupted', 'errorCode': 'APPLICATION_BACKUP_NO_RECEIPT'}


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
            created_ns = time.time_ns()
            result = {'backupId': job, 'slug': slug, 'relativePath': artifact.relative_to(TARGET).as_posix(),
                'sizeBytes': artifact.stat().st_size, 'checksumAlgorithm': 'SHA256', 'checksum': digest,
                'verified': True, 'compression': 'gzip', 'consistency': consistency, 'transactionConsistent': False,
                'createdAtUnix': created_ns // 1_000_000_000, 'createdAtUnixNs': created_ns, 'namedVolumes': sorted(volumes), 'dataIncluded': data.exists(),
                'runtimeServices': [{'service': (member.get('Config', {}).get('Labels') or {}).get('com.docker.compose.service', ''),
                    'imageId': member.get('Image', ''), 'wasRunning': member['State'].get('Running', False)} for member in members],
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


def approved_service(job, cgroup):
    require(JOB.fullmatch(job) is not None and len(cgroup) <= 16384)
    expected = 'whaledeck-application-backup-' + job + '.service'
    return any(expected in PurePosixPath(line.split(':', 2)[2]).parts
        for line in cgroup.splitlines() if len(line.split(':', 2)) == 3)


def main():
    require(os.geteuid() == 0)
    verb, *arguments = sys.argv[1:]
    if verb == 'plan' and len(arguments) == 3:
        result = plan(*arguments[:2], critical=int(arguments[2]))
    elif verb in ('run', 'cleanup-run') and len(arguments) == 5:
        # Never pause from an ad-hoc sudo invocation without the systemd recovery contract.
        require(approved_service(arguments[1], Path('/proc/self/cgroup').read_text()))
        def cancel(*_):
            global CANCELED
            CANCELED = True
            raise BackupError('Application backup canceled.')
        signal.signal(signal.SIGTERM, cancel)
        try:
            result = (run(*arguments[:4], critical=int(arguments[4])) if verb == 'run' else
                cleanup(arguments[0], arguments[1], int(arguments[2]), int(arguments[3]), arguments[4]))
            private_json(STATE / (arguments[1] + '.done.json'),
                {'state': 'Succeeded', 'phase': 'Completed', 'slug': arguments[0], 'job': arguments[1], 'result': result})
        except BaseException:
            private_json(STATE / (arguments[1] + '.done.json'), {'state': 'Canceled' if CANCELED else 'Failed',
                'phase': 'Canceled' if CANCELED else 'Failed', 'slug': arguments[0], 'job': arguments[1],
                'errorCode': 'APPLICATION_BACKUP_CANCELED' if CANCELED else 'APPLICATION_BACKUP_FAILED'})
            raise
    elif verb == 'capacity' and not arguments:
        result = capacity()
    elif verb == 'cleanup-plan' and len(arguments) == 3:
        result = cleanup_plan(arguments[0], int(arguments[1]), int(arguments[2]))
    elif verb == 'verify-plan' and len(arguments) == 2:
        result = verify_plan(*arguments)
    elif verb == 'verify' and len(arguments) == 3:
        require(hmac.compare_digest(verify_plan(*arguments[:2])['fingerprint'], arguments[2]))
        result = verify(*arguments[:2])
    elif verb == 'status' and len(arguments) == 2:
        result = status(*arguments)
    elif verb == 'recover-job' and len(arguments) == 1:
        recover_job(*arguments)
        return
    elif verb == 'recover-all' and not arguments:
        failed = False
        for path in STATE.glob('*.json'):
            if JOB.fullmatch(path.stem):
                try:
                    recover_job(path.stem)
                except (BackupError, OSError, ValueError, subprocess.TimeoutExpired):
                    failed = True
        require(not failed)
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
