#!/usr/bin/env python3
"""Remove only archive-identical owned /tmp tree-move code copies, not data.

Every regular source byte is checked against the retained transfer archive.
Only matching Python bytecode caches may be additional files. Any unknown
member, link, mount, owner or changed source preserves the entire copy.
"""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import tarfile


def main():
    assert os.geteuid() == 0
    request = json.loads(sys.stdin.buffer.readline(8193))
    assert isinstance(request, list) and 1 <= len(request) <= 8
    repository = Path(__file__).resolve().parents[2]
    validated = []; seen = set(); files_total = 0
    for item in request:
        assert set(item) == {'source', 'archive'}
        assert re.fullmatch(r'/tmp/whaledeck-tree-moves-[A-Za-z0-9]{6}', item['source'])
        assert re.fullmatch(r'tree-move-source-[0-9]{8}-v[2-9]\.tar', item['archive'])
        folder = Path(item['source']); assert str(folder) not in seen; seen.add(str(folder))
        identity = folder.lstat()
        assert folder.resolve() == folder and stat.S_ISDIR(identity.st_mode) and identity.st_uid == 1000 and stat.S_IMODE(identity.st_mode) == 0o700
        archive_path = repository / 'deploy/artifacts' / item['archive']; archive_info = archive_path.lstat()
        assert stat.S_ISREG(archive_info.st_mode) and archive_info.st_nlink == 1
        expected = {}; expected_dirs = set()
        with tarfile.open(archive_path, 'r:') as archive:
            for member in archive:
                path = PurePosixPath(member.name)
                assert not path.is_absolute() and '..' not in path.parts and not member.issym() and not member.islnk()
                if member.isdir(): expected_dirs.add(str(path)); continue
                assert member.isfile()
                with archive.extractfile(member) as stream: expected[str(path)] = hashlib.file_digest(stream, 'sha256').digest()
                expected_dirs.update(str(parent) for parent in path.parents if str(parent) != '.')
        assert 'deploy/host/whaledeck-tree-moves.py' in expected and 'deploy/validation/test_tree_moves.py' in expected
        for line in Path('/proc/self/mountinfo').read_text().splitlines():
            assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
        found = set()
        for parent, directories, files in os.walk(folder, followlinks=False):
            for name in directories + files:
                target = Path(parent) / name; info = target.lstat()
                assert info.st_dev == identity.st_dev and info.st_uid in (0, 1000) and not stat.S_ISLNK(info.st_mode)
                if stat.S_ISDIR(info.st_mode):
                    relative_dir = str(target.relative_to(folder)); parsed_dir = PurePosixPath(relative_dir)
                    assert relative_dir in expected_dirs or parsed_dir.name == '__pycache__' and any(name.startswith(str(parsed_dir.parent) + '/') and name.endswith('.py') for name in expected)
                    continue
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                relative = str(target.relative_to(folder)); found.add(relative)
                if relative in expected:
                    with target.open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').digest() == expected[relative]
                else:
                    parsed = PurePosixPath(relative); match = re.fullmatch(r'([A-Za-z0-9_-]+)\.cpython-[0-9]+\.pyc', parsed.name)
                    assert parsed.parent.name == '__pycache__' and match and str(parsed.parent.parent / (match[1] + '.py')) in expected
        assert set(expected) <= found
        validated.append((folder, identity)); files_total += len(found)
    for folder, identity in validated:
        current = folder.lstat(); assert (current.st_dev, current.st_ino, current.st_uid) == (identity.st_dev, identity.st_ino, identity.st_uid)
        shutil.rmtree(folder)
    print(json.dumps({'ownedArchiveIdenticalSourceCopiesRemoved': len(validated), 'verifiedSourceAndCacheFiles': files_total,
        'transferArchivesPreserved': True, 'businessDataAndInstalledRuntimeTouched': False}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print(json.dumps({'validationSourceCleanup': 'failed-preserved', 'failureType': type(error).__name__}))
        raise SystemExit(1) from None
