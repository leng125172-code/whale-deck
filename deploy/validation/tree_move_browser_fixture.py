#!/usr/bin/env python3
"""Root-owned, exact-scope real UI fixture and independent move observer.

No identity credentials, fake API responses or arbitrary paths. A failed check
preserves all objects; permanent Root receipts and SQL records are never removed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import uuid

BASE = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
SOURCE = '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789'


def payload(marker):
    seed = hashlib.sha256(marker.encode()).digest()
    return (seed * (2 * 1024 * 1024 // len(seed) + 1))[:2 * 1024 * 1024 + 7]


def regular(path, maximum):
    info = path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
    assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= maximum
    return json.loads(path.read_bytes())


def file_identity(info):
    return [info.st_dev, info.st_ino, info.st_uid, info.st_gid, info.st_size,
            info.st_mtime_ns, stat.S_IMODE(info.st_mode), info.st_nlink]


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(16385))
    if request == {'action': 'create'}:
        marker = 'wd-browser-move-' + uuid.uuid4().hex
        folder = BASE / marker
        folder.mkdir(mode=0o700)
        identities = {}
        for theme in ('light', 'dark'):
            source = folder / (SOURCE + '-' + theme)
            source.mkdir(mode=0o750)
            (source / '空目录').mkdir(mode=0o750)
            (source / '代码').mkdir(mode=0o750)
            data = source / '代码/payload.bin'
            data.write_bytes(payload(marker))
            data.chmod(0o640)
            identities[theme] = file_identity(data.stat())
        info = folder.stat()
        print(json.dumps({'marker': marker, 'device': info.st_dev, 'inode': info.st_ino,
            'source': SOURCE, 'bytes': len(payload(marker)), 'identities': identities}, ensure_ascii=False))
        return
    assert set(request) == {'action', 'marker', 'device', 'inode', 'identities', 'moves'}
    assert request['action'] == 'finish'
    marker = request['marker']
    assert re.fullmatch(r'wd-browser-move-[a-f0-9]{32}', marker)
    folder = BASE / marker
    initial = folder.lstat()
    assert folder.resolve() == folder and stat.S_ISDIR(initial.st_mode)
    assert (initial.st_dev, initial.st_ino, initial.st_uid) == (request['device'], request['inode'], 0)
    assert set(request['identities']) == {'light', 'dark'}
    moves = request['moves']
    assert isinstance(moves, list) and len(moves) == 2
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    policy = next(x for x in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if x['id'] == ROOT)
    assert policy['path'] == str(BASE)
    expected_dirs = {'.whaledeck-files'}
    expected_files = set()
    names = set()
    for move in moves:
        assert set(move) == {'theme', 'operationId'}
        theme = move['theme']
        assert theme in ('light', 'dark') and theme not in names
        names.add(theme)
        operation = uuid.UUID(move['operationId']).hex
        receipt_path = Path('/var/lib/whaledeck-files/tree-moves') / (operation + '.json')
        receipt = regular(receipt_path, 32768)
        assert receipt['operationId'] == operation and receipt['state'] == 'Completed'
        assert all(receipt[key] is True for key in ('destinationPublished', 'sourceRemoved', 'sourceRetained'))
        command = receipt['plan']['copyPlan']['command']
        source_name, target_name = SOURCE + '-' + theme, 'moved-' + theme
        assert command['source']['rootId'] == ROOT
        assert command['source']['path'] == marker + '/' + source_name
        assert re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion'])
        assert command['target'] == {'rootId': ROOT, 'path': marker + '/' + target_name}
        assert not os.path.lexists(folder / source_name)
        target = folder / target_name
        retained_name = '.whaledeck-files/' + operation + '.tree-move'
        retained = folder / retained_name
        target_info, retained_info = target.stat(), retained.stat()
        assert receipt['retainedIdentity'] == [retained_info.st_dev, retained_info.st_ino]
        # The published destination is independently observed through its
        # child-copy receipt, not inferred from a successful HTTP response.
        child = regular(Path('/var/lib/whaledeck-files/tree-move-copies') / (operation + '.json'), 32768)
        assert child['state'] == 'Completed' and child['newIdentity'] == [target_info.st_dev, target_info.st_ino]
        before = request['identities'][theme]
        assert isinstance(before, list) and len(before) == 8
        original = retained / '代码/payload.bin'
        assert file_identity(original.stat()) == before
        assert original.read_bytes() == payload(marker)
        current = target / '代码/payload.bin'
        info = current.stat()
        assert current.read_bytes() == payload(marker)
        assert (info.st_uid, info.st_gid) == (policy['ownerUid'], policy['ownerGid']) and info.st_nlink == 1
        assert [info.st_size, info.st_mtime_ns, stat.S_IMODE(info.st_mode)] == [before[4], before[5], before[6]]
        assert [info.st_dev, info.st_ino] != before[:2]
        assert not list((target / '空目录').iterdir()) and not list((retained / '空目录').iterdir())
        manifest = regular(receipt_path.with_name(operation + '.manifest.json'), 16 * 1024**2)
        encoded = json.dumps(manifest, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()
        assert hashlib.sha256(b'whaledeck-tree-move-v1\n' + encoded).hexdigest().upper() == receipt['manifestDigest']
        assert manifest['manifestSha256'] == receipt['plan']['copyPlan']['manifestSha256']
        for name in (target_name, retained_name):
            expected_dirs.update((name, name + '/空目录', name + '/代码'))
            expected_files.add(name + '/代码/payload.bin')
    dirs, files = set(), set()
    for parent, directories, members in os.walk(folder, followlinks=False):
        for name in directories + members:
            path = Path(parent) / name
            info = path.lstat()
            assert info.st_dev == initial.st_dev and info.st_uid in (0, policy['ownerUid'])
            assert not stat.S_ISLNK(info.st_mode)
            relative = str(path.relative_to(folder))
            if stat.S_ISDIR(info.st_mode):
                dirs.add(relative)
            else:
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                files.add(relative)
    assert dirs == expected_dirs and files == expected_files
    shutil.rmtree(folder)
    print(json.dumps({'realBrowserMovesIndependentBytesMetadataReceipts': 'passed',
        'moves': 2, 'binaryBytesEach': len(payload(marker)), 'exactOwnedFixtureRemoved': True,
        'permanentRootReceiptsRetained': True, 'sameDevicePrivateOriginalsNotHddBackup': True}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'treeMoveBrowserFixture': 'failed-preserved', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
