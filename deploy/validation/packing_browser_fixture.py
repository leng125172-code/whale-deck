#!/usr/bin/env python3
"""Exact-owned real browser packing data and independent full archive observer.

No credentials or arbitrary paths. Failure preserves fixtures. Permanent jobs,
audit records and root receipts are never deleted. Does not extract archives.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import tarfile
import uuid

BASE = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
SOURCE = '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789'
SIZE = 2 * 1024 * 1024 + 7


def payload(marker):
    seed = hashlib.sha256(marker.encode()).digest()
    return (seed * (SIZE // len(seed) + 1))[:SIZE]


def identity(info):
    return [info.st_dev, info.st_ino, info.st_uid, info.st_gid, info.st_size,
            info.st_mtime_ns, stat.S_IMODE(info.st_mode), info.st_nlink]


def private_json(path):
    info = path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == 0
    assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 32768
    return json.loads(path.read_bytes())


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(16385))
    if request == {'action': 'create'}:
        marker = 'wd-browser-packing-' + uuid.uuid4().hex
        folder = BASE / marker
        assert not os.path.lexists(folder)
        folder.mkdir(mode=0o700)
        before = {}
        for theme in ('light', 'dark'):
            source = folder / (SOURCE + '-' + theme)
            source.mkdir(mode=0o750)
            (source / '空目录').mkdir(mode=0o750)
            (source / '代码').mkdir(mode=0o750)
            data = source / '代码/payload.bin'
            data.write_bytes(payload(marker))
            data.chmod(0o640)
            before[theme] = identity(data.stat())
        info = folder.stat()
        print(json.dumps({'marker': marker, 'device': info.st_dev, 'inode': info.st_ino,
            'source': SOURCE, 'bytes': SIZE, 'identities': before}, ensure_ascii=False))
        return
    assert set(request) == {'action', 'marker', 'device', 'inode', 'identities', 'packings'}
    assert request['action'] == 'finish'
    marker = request['marker']
    assert re.fullmatch(r'wd-browser-packing-[a-f0-9]{32}', marker)
    folder = BASE / marker
    before = folder.lstat()
    assert folder.resolve() == folder and stat.S_ISDIR(before.st_mode)
    assert (before.st_dev, before.st_ino, before.st_uid) == (request['device'], request['inode'], 0)
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    policy = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert policy['path'] == str(BASE)
    assert set(request['identities']) == {'light', 'dark'}
    packings = request['packings']
    assert isinstance(packings, list) and len(packings) == 2
    seen = set()
    expected_dirs, expected_files, reports = {'.whaledeck-files'}, set(), []
    content_digest = hashlib.sha256(payload(marker)).digest()
    for packing in packings:
        assert set(packing) == {'theme', 'operationId', 'format', 'archiveBytes', 'archiveSha256'}
        theme = packing['theme']
        assert theme in ('light', 'dark') and theme not in seen
        seen.add(theme)
        archive_format = 'tar' if theme == 'light' else 'tar.gz'
        assert packing['format'] == archive_format
        operation = uuid.UUID(packing['operationId']).hex
        receipt = private_json(Path('/var/lib/whaledeck-files/tree-packing') / (operation + '.json'))
        assert receipt['operationId'] == operation and receipt['state'] == 'Completed'
        assert re.fullmatch('[A-F0-9]{64}', receipt['version'])
        command = receipt['plan']['command']
        source_name = SOURCE + '-' + theme
        target_name = 'packed-' + theme + '.' + archive_format
        assert command['source']['rootId'] == ROOT and command['source']['path'] == marker + '/' + source_name
        assert re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion'])
        assert command['target'] == {'rootId': ROOT, 'path': marker + '/' + target_name}
        assert command['format'] == archive_format
        original = folder / source_name / '代码/payload.bin'
        assert identity(original.stat()) == request['identities'][theme]
        with original.open('rb') as stream:
            assert hashlib.file_digest(stream, 'sha256').digest() == content_digest
        target = folder / target_name
        info = target.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
        assert (info.st_uid, info.st_gid) == (policy['ownerUid'], policy['ownerGid'])
        assert [info.st_dev, info.st_ino] == receipt['newIdentity'][:2]
        with target.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
        assert info.st_size == packing['archiveBytes'] == receipt['archiveBytes']
        assert digest == packing['archiveSha256'] == receipt['archiveSha256']
        with tarfile.open(target) as archive:
            assert set(archive.getnames()) == {'.', '空目录', '代码', '代码/payload.bin'}
            assert all(archive.getmember(name).isdir() for name in ('.', '空目录', '代码'))
            member = archive.getmember('代码/payload.bin')
            assert member.isfile() and member.size == SIZE and member.mode == 0o640
            with archive.extractfile(member) as stream:
                assert hashlib.file_digest(stream, 'sha256').digest() == content_digest
        expected_dirs.update((source_name, source_name + '/空目录', source_name + '/代码'))
        expected_files.update((source_name + '/代码/payload.bin', target_name))
        reports.append({'theme': theme, 'format': archive_format, 'archiveBytes': info.st_size, 'archiveSha256': digest})
    dirs, files = set(), set()
    for parent, directories, members in os.walk(folder, followlinks=False):
        for name in directories + members:
            path = Path(parent) / name
            info = path.lstat()
            assert info.st_dev == before.st_dev and info.st_uid in (0, policy['ownerUid']) and not stat.S_ISLNK(info.st_mode)
            relative = str(path.relative_to(folder))
            if stat.S_ISDIR(info.st_mode):
                dirs.add(relative)
            else:
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                files.add(relative)
    assert dirs == expected_dirs and files == expected_files
    shutil.rmtree(folder)
    print(json.dumps({'realBrowserPackingIndependentProof': 'passed', 'sourceBytes': SIZE,
        'archives': reports, 'exactOwnedFixtureRemoved': True, 'immutableRecordsRetained': True}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'packingBrowserFixtureFailed': True, 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
