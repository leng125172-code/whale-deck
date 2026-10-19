#!/usr/bin/env python3
"""Exact-owned real browser inputs and independent full output observer.

No credentials, arbitrary paths, extraction, operation replay or record deletion.
Raw create JSON is returned unchanged inside the finish envelope so nanosecond
and device/inode integers never round-trip through JavaScript floating point.
Failure preserves the complete fixture for explicit reconciliation.
"""
from decimal import Decimal
import hashlib
import io
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
SOURCE = '长归档-中文-abcdefghijklmnopqrstuvwxyz-0123456789'
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
        info.st_size, info.st_mtime_ns, info.st_ctime_ns, digest]


def private_json(path):
    info = path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
    assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 32768
    return json.loads(path.read_bytes())


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(65537))
    if request == {'action': 'create'}:
        marker = 'wd-browser-unpacking-' + uuid.uuid4().hex
        folder = BASE / marker
        assert not os.path.lexists(folder)
        folder.mkdir(mode=0o700)
        archives = {}
        for theme, format, standard in (('light', 'tar', tarfile.PAX_FORMAT), ('dark', 'tar.gz', tarfile.GNU_FORMAT)):
            name = SOURCE + '-' + theme + '.' + format
            path = folder / name
            with tarfile.open(path, 'w:gz' if format == 'tar.gz' else 'w', format=standard) as writer:
                for member, mode in (('.', 0o750), ('空目录', 0o710), ('代码', 0o750), (MEMBER, 0o6751)):
                    entry = tarfile.TarInfo(member)
                    entry.mode, entry.uid, entry.gid, entry.mtime = mode, 12345, 23456, 1712345678
                    if standard == tarfile.PAX_FORMAT:
                        entry.pax_headers['mtime'] = '1712345678.123456789'
                    if member == MEMBER:
                        entry.size = SIZE
                        writer.addfile(entry, io.BytesIO(payload(marker)))
                    else:
                        entry.type = tarfile.DIRTYPE
                        writer.addfile(entry)
            path.chmod(0o600)
            archives[theme] = {'name': name, 'format': format, 'identity': proof(path)}
        info = folder.lstat()
        print(json.dumps({'marker': marker, 'device': info.st_dev, 'inode': info.st_ino,
            'source': SOURCE, 'bytes': SIZE, 'archives': archives}, ensure_ascii=False), flush=True)
        return
    assert set(request) == {'action', 'fixture', 'unpackings'} and request['action'] == 'finish'
    assert isinstance(request['fixture'], str) and len(request['fixture'].encode()) <= 8192
    fixture = json.loads(request['fixture'])
    assert set(fixture) == {'marker', 'device', 'inode', 'source', 'bytes', 'archives'}
    marker = fixture['marker']
    assert re.fullmatch(r'wd-browser-unpacking-[a-f0-9]{32}', marker)
    assert fixture['source'] == SOURCE and fixture['bytes'] == SIZE and set(fixture['archives']) == {'light', 'dark'}
    folder = BASE / marker
    initial = folder.lstat()
    assert folder.resolve() == folder and stat.S_ISDIR(initial.st_mode) and stat.S_IMODE(initial.st_mode) == 0o700
    assert (initial.st_dev, initial.st_ino, initial.st_uid) == (fixture['device'], fixture['inode'], 0)
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    policy = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert policy['path'] == str(BASE)
    operations = request['unpackings']
    assert isinstance(operations, list) and len(operations) == 2
    seen, expected, inputs, outputs, reports = set(), {'.whaledeck-files'}, {}, {}, []
    digest = hashlib.sha256(payload(marker)).hexdigest().upper()
    for operation in operations:
        assert set(operation) == {'theme', 'operationId', 'format', 'archiveBytes', 'archiveSha256', 'publishedVersion'}
        theme = operation['theme']
        assert theme in ('light', 'dark') and theme not in seen
        seen.add(theme)
        archive_format = 'tar' if theme == 'light' else 'tar.gz'
        name, target_name = SOURCE + '-' + theme + '.' + archive_format, 'unpacked-' + theme
        archive = fixture['archives'][theme]
        assert set(archive) == {'name', 'format', 'identity'} and archive['name'] == name and archive['format'] == archive_format
        assert operation['format'] == archive_format and operation['archiveBytes'] == archive['identity'][5]
        assert operation['archiveSha256'] == archive['identity'][-1] and re.fullmatch('[A-F0-9]{64}', operation['publishedVersion'])
        source = folder / name
        assert proof(source) == archive['identity']
        inputs[name] = archive['identity']
        identifier = uuid.UUID(operation['operationId'])
        assert str(identifier) == operation['operationId'] and identifier.int != 0
        record = private_json(Path('/var/lib/whaledeck-files/tree-unpacking') / (identifier.hex + '.json'))
        assert record['operationId'] == identifier.hex and record['state'] == 'Completed' and record['version'] == operation['publishedVersion']
        plan = record['plan']
        command = plan['command']
        assert command['source']['rootId'] == ROOT and command['source']['path'] == marker + '/' + name
        assert re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion'])
        assert command['target'] == {'rootId': ROOT, 'path': marker + '/' + target_name} and command['format'] == archive_format
        assert plan['archiveSha256'] == operation['archiveSha256'] and plan['archiveBytes'] == operation['archiveBytes']
        assert plan['entryCount'] == 4 and plan['totalBytes'] == SIZE
        target = folder / target_name
        info = target.lstat()
        assert [info.st_dev, info.st_ino] == record['newIdentity']
        assert {''} | {str(item.relative_to(target)) for item in target.rglob('*')} == {'', '空目录', '代码', MEMBER}
        with tarfile.open(source) as reader:
            assert set(reader.getnames()) == {'.', '空目录', '代码', MEMBER}
            for entry in reader.getmembers():
                relative = '' if entry.name == '.' else entry.name
                path = target / relative
                metadata = path.lstat()
                assert (stat.S_ISDIR(metadata.st_mode) if entry.isdir() else stat.S_ISREG(metadata.st_mode))
                assert (metadata.st_dev, metadata.st_uid, metadata.st_gid, stat.S_IMODE(metadata.st_mode), metadata.st_mtime_ns) == (
                    initial.st_dev, policy.get('ownerUid', 0), policy.get('ownerGid', 0), entry.mode & 0o777,
                    int(Decimal(entry.pax_headers.get('mtime', str(entry.mtime))) * 1000000000))
                if entry.isfile():
                    assert metadata.st_size == SIZE and metadata.st_nlink == 1 and proof(path)[-1] == digest
                    with reader.extractfile(entry) as stream:
                        assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == digest
                key = str(path.relative_to(folder))
                outputs[key] = proof(path)
                expected.add(key)
        expected.add(name)
        reports.append({'theme': theme, 'format': archive_format, 'archiveBytes': operation['archiveBytes'], 'archiveSha256': operation['archiveSha256']})
    assert {str(item.relative_to(folder)) for item in folder.rglob('*')} == expected
    private = folder / '.whaledeck-files'
    metadata = private.lstat()
    assert stat.S_ISDIR(metadata.st_mode) and metadata.st_uid == 0 and stat.S_IMODE(metadata.st_mode) == 0o700 and not any(private.iterdir())
    assert {name: proof(folder / name) for name in inputs} == inputs
    assert {name: proof(folder / name) for name in outputs} == outputs
    # All membership/inode/hash/owner/metadata checks complete before deletion.
    # Only exact regular files and known empty directories; no recursive delete.
    for name, value in outputs.items():
        if stat.S_ISREG(value[2]):
            (folder / name).unlink()
    for name in sorted((name for name, value in outputs.items() if stat.S_ISDIR(value[2])), key=lambda value: (value.count('/'), value), reverse=True):
        (folder / name).rmdir()
    for name in inputs:
        (folder / name).unlink()
    private.rmdir()
    folder.rmdir()
    print(json.dumps({'realBrowserUnpackingIndependentProof': 'passed', 'contentBytes': SIZE,
        'archives': reports, 'exactOwnedFixtureRemoved': True, 'immutableRecordsRetained': True}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'unpackingBrowserFixtureFailed': True, 'failureType': type(error).__name__,
            'fixturePreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
