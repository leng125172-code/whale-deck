#!/usr/bin/env python3
"""Exact-owned real browser tree fixture. No credentials or arbitrary paths.

Create returns public identity metadata; finish requires unchanged source,
known completed target receipts, full bytes and metadata, and exact membership.
Any uncertainty preserves the fixture. Permanent root receipts are never deleted.
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
SOURCE = '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789'
ROOT = 'whaledeck.files.shared'


def payload(marker):
    seed = hashlib.sha256(marker.encode()).digest()
    return (seed * (2 * 1024 * 1024 // len(seed) + 1))[:2 * 1024 * 1024 + 7]


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(8193))
    if request == {'action': 'create'}:
        marker = 'wd-browser-tree-' + uuid.uuid4().hex
        folder = BASE / marker
        folder.mkdir(mode=0o700)
        source = folder / SOURCE; source.mkdir(mode=0o750)
        (source / '空目录').mkdir(mode=0o750); (source / '代码').mkdir(mode=0o750)
        data = source / '代码/payload.bin'; data.write_bytes(payload(marker)); data.chmod(0o640)
        info = folder.lstat()
        print(json.dumps({'marker': marker, 'device': info.st_dev, 'inode': info.st_ino,
            'source': SOURCE, 'bytes': data.stat().st_size}, ensure_ascii=False), flush=True)
        return
    assert set(request) == {'action', 'marker', 'device', 'inode', 'copies'} and request['action'] == 'finish'
    marker = request['marker']; assert re.fullmatch(r'wd-browser-tree-[a-f0-9]{32}', marker)
    copies = request['copies']; assert isinstance(copies, list) and 1 <= len(copies) <= 2
    folder = BASE / marker; initial = folder.lstat()
    assert folder.resolve() == folder and (initial.st_dev, initial.st_ino, initial.st_uid) == (request['device'], request['inode'], 0)
    assert stat.S_ISDIR(initial.st_mode) and not stat.S_ISLNK(initial.st_mode)
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
    expected_dirs = {SOURCE, SOURCE + '/空目录', SOURCE + '/代码', '.whaledeck-files'}
    expected_files = {SOURCE + '/代码/payload.bin'}
    original = folder / SOURCE / '代码/payload.bin'; before = original.stat()
    assert original.read_bytes() == payload(marker)
    assert stat.S_IMODE(before.st_mode) == 0o640 and before.st_nlink == 1
    names = set()
    for copy in copies:
        assert set(copy) == {'name', 'operationId'} and re.fullmatch(r'copy-(light|dark)', copy['name'])
        name = copy['name']; assert name not in names; names.add(name)
        operation = uuid.UUID(copy['operationId']).hex
        receipt_file = Path('/var/lib/whaledeck-files/tree-copies') / (operation + '.json')
        receipt_info = receipt_file.lstat()
        assert stat.S_ISREG(receipt_info.st_mode) and receipt_info.st_nlink == 1 and receipt_info.st_uid == 0 and receipt_info.st_size <= 32768
        receipt = json.loads(receipt_file.read_bytes())
        assert receipt['operationId'] == operation and receipt['state'] == 'Completed'
        command = receipt['plan']['command']
        assert set(command['source']) == {'rootId', 'path', 'expectedVersion'}
        assert command['source']['rootId'] == ROOT and command['source']['path'] == marker + '/' + SOURCE
        assert re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion'])
        assert command['target'] == {'rootId': ROOT, 'path': marker + '/' + name}
        target_dir = folder / name; after_dir = target_dir.stat()
        assert receipt['newIdentity'] == [after_dir.st_dev, after_dir.st_ino]
        target = target_dir / '代码/payload.bin'; after = target.stat()
        assert target.read_bytes() == payload(marker) and after.st_uid == after.st_gid == 1000 and after.st_nlink == 1
        assert (after.st_size, after.st_mtime_ns, stat.S_IMODE(after.st_mode)) == (before.st_size, before.st_mtime_ns, stat.S_IMODE(before.st_mode))
        assert (after.st_dev, after.st_ino) != (before.st_dev, before.st_ino)
        assert not list((target_dir / '空目录').iterdir())
        expected_dirs.update((name, name + '/空目录', name + '/代码')); expected_files.add(name + '/代码/payload.bin')
    dirs = set(); files = set()
    for parent, directories, members in os.walk(folder, followlinks=False):
        for name in directories + members:
            path = Path(parent) / name; info = path.lstat()
            assert info.st_dev == initial.st_dev and info.st_uid in (0, 1000) and not stat.S_ISLNK(info.st_mode)
            relative = str(path.relative_to(folder))
            if stat.S_ISDIR(info.st_mode): dirs.add(relative)
            else:
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                files.add(relative)
    assert dirs == expected_dirs and files == expected_files
    shutil.rmtree(folder)
    print(json.dumps({'realBrowserTreeCopiesIndependentBytesMetadataReceipts': 'passed',
        'copies': len(copies), 'binaryBytes': before.st_size, 'exactOwnedFixtureRemoved': True, 'rootReceiptsRetained': True}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print(json.dumps({'treeBrowserFixture': 'failed-preserved', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
