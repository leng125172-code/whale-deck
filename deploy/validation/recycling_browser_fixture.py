#!/usr/bin/env python3
"""Owned real-browser sources and independent HDD/original-inode observer.

No engine invocation, credentials, broad deletion or execution replay. The raw
create JSON must be returned unchanged to preserve nanosecond/UInt64 proofs.
Failure preserves all unverified objects. Permanent jobs/receipts are not erased.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tarfile
import uuid

BASE = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
SOURCE = '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789'
MEMBER = '代码/' + '中文' * 20 + '.bin'
SIZE = 2 * 1024 * 1024 + 7


def payload(marker):
    return hashlib.shake_256(marker.encode()).digest(SIZE)


def proof(path):
    info = path.lstat()
    assert stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)
    digest = None
    if stat.S_ISREG(info.st_mode):
        assert info.st_nlink == 1
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
    return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
        info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns, digest]


def private_json(path, maximum=32768):
    info = path.lstat()
    assert path.resolve() == path and stat.S_ISREG(info.st_mode)
    assert info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= maximum
    return json.loads(path.read_bytes())


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(65537))
    if request == {'action': 'create'}:
        marker = 'wd-browser-recycling-' + uuid.uuid4().hex
        folder = BASE / marker
        assert not os.path.lexists(folder)
        folder.mkdir(mode=0o700)
        sources = {}
        for theme in ('light', 'dark'):
            name = SOURCE + '-' + theme
            source = folder / name
            source.mkdir(mode=0o750)
            (source / '空目录').mkdir(mode=0o710)
            (source / '代码').mkdir(mode=0o750)
            leaf = source / MEMBER
            leaf.write_bytes(payload(marker))
            leaf.chmod(0o640)
            os.utime(leaf, ns=(1712345678123456789, 1712345678123456789))
            sources[theme] = {'name': name, 'identity': proof(leaf)}
        print(json.dumps({'marker': marker, 'identity': proof(folder)[:2],
            'source': SOURCE, 'bytes': SIZE, 'sources': sources}, ensure_ascii=False), flush=True)
        return
    assert set(request) == {'action', 'fixture', 'recyclings'} and request['action'] == 'finish'
    assert isinstance(request['fixture'], str) and len(request['fixture'].encode()) <= 8192
    fixture = json.loads(request['fixture'])
    assert set(fixture) == {'marker', 'identity', 'source', 'bytes', 'sources'}
    marker = fixture['marker']
    assert re.fullmatch('wd-browser-recycling-[a-f0-9]{32}', marker)
    assert fixture['source'] == SOURCE and fixture['bytes'] == SIZE and set(fixture['sources']) == {'light', 'dark'}
    folder = BASE / marker
    initial = folder.lstat()
    assert folder.resolve() == folder and stat.S_ISDIR(initial.st_mode) and stat.S_IMODE(initial.st_mode) == 0o700
    assert proof(folder)[:2] == fixture['identity'] and initial.st_uid == 0
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    policy = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert policy['path'] == str(BASE) and 'files-tree-trash' in policy['allowedActions']
    operations = request['recyclings']
    assert isinstance(operations, list) and len(operations) == 2
    config = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
    store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_absolute()
    seen, private_roots, archive_proofs, reports = set(), {}, {}, []
    for operation in operations:
        assert set(operation) == {'theme', 'operationId', 'captureId', 'archiveBytes', 'archiveSha256'}
        theme = operation['theme']
        assert theme in ('light', 'dark') and theme not in seen
        seen.add(theme)
        source = fixture['sources'][theme]
        assert set(source) == {'name', 'identity'} and source['name'] == SOURCE + '-' + theme
        assert not os.path.lexists(folder / source['name'])
        identifier = uuid.UUID(operation['operationId'])
        assert str(identifier) == operation['operationId'] and identifier.int != 0 and operation['captureId'] == str(identifier)
        record = private_json(Path('/var/lib/whaledeck-files/tree-recycling') / (identifier.hex + '.json'))
        assert record['operationId'] == identifier.hex and record['state'] == 'Completed'
        assert record['sourceRemoved'] is True and record['sourceRetained'] is True
        assert record['plan']['source']['rootId'] == ROOT and record['plan']['source']['path'] == marker + '/' + source['name']
        assert record['plan']['entryCount'] == 4 and record['plan']['totalBytes'] == SIZE
        private = folder / '.whaledeck-files' / (identifier.hex + '.tree-trash')
        assert private.resolve() == private and proof(private)[:2] == record['retainedIdentity']
        assert private.lstat().st_uid == 0 and stat.S_IMODE(private.lstat().st_mode) == 0o700
        assert {str(path.relative_to(private)) for path in private.rglob('*')} == {'空目录', '代码', MEMBER}
        retained = proof(private / MEMBER)
        assert retained[:8] == source['identity'][:8] and retained[-1] == source['identity'][-1]
        assert (private / MEMBER).read_bytes() == payload(marker)
        private_roots[private] = retained
        archive = store / (identifier.hex + '.tar.gz')
        current = proof(archive)
        assert current[6] == operation['archiveBytes'] and current[-1] == operation['archiveSha256']
        assert current[6] == record['archiveBytes'] and current[-1] == record['archiveSha256']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers()
            assert {member.name.rstrip('/') for member in members} == {'.', '空目录', '代码', MEMBER}
            assert all(member.isdir() or member.isfile() for member in members)
            member = next(item for item in members if item.name == MEMBER)
            assert member.size == SIZE
            with reader.extractfile(member) as content:
                assert content.read(SIZE + 1) == payload(marker) and not content.read(1)
        saved = private_json(store / (identifier.hex + '.json'))
        assert saved['state'] == 'Verified' and type(saved['differentSourceDevice']) is bool
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            path = store / (identifier.hex + suffix)
            value = path.lstat()
            assert path.resolve() == path and stat.S_ISREG(value.st_mode) and value.st_uid == 0
            assert value.st_nlink == 1 and stat.S_IMODE(value.st_mode) == 0o600
            archive_proofs[path] = proof(path)
        reports.append({'theme': theme, 'bytes': SIZE, 'fullHddArchiveAndRetainedOriginalInode': True,
            'differentSourceDevice': saved['differentSourceDevice']})
    assert {item.name for item in folder.iterdir()} == {'.whaledeck-files'}
    assert {item.name for item in (folder / '.whaledeck-files').iterdir()} == {path.name for path in private_roots}
    assert all(proof(path) == value for path, value in archive_proofs.items())
    assert all(proof(path / MEMBER) == value for path, value in private_roots.items())
    # All identities/full bytes were verified before the first exact deletion.
    for path in archive_proofs:
        path.unlink()
    for path in private_roots:
        (path / MEMBER).unlink()
        (path / '代码').rmdir()
        (path / '空目录').rmdir()
        path.rmdir()
    (folder / '.whaledeck-files').rmdir()
    folder.rmdir()
    print(json.dumps({'realBrowserDirectoryRecyclingDiskObserver': 'passed', 'reports': reports,
        'exactOwnedPayloadsRemoved': True, 'historicalCatalogueJobsAuditAndRootReceiptsRetained': True}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except BaseException as error:
        print(json.dumps({'realBrowserDirectoryRecyclingDiskObserver': 'failed',
            'failureType': type(error).__name__, 'unknownObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
