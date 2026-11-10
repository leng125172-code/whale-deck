#!/usr/bin/env python3
"""Read-only, bounded physical facts for the exact installed QA workspace."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 2
    identifier = sys.argv[1]
    assert re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}', identifier)
    spec = importlib.util.spec_from_file_location('installed_storage_transport',
        '/usr/local/libexec/whaledeck-development-storage-transport.py')
    loader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loader)
    files, storage, quota = (loader.load(name) for name in ('whaledeck-files.py',
        'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py'))
    driver = storage.WorkspaceStorage(files, quota=quota)
    with driver.locked_root('nvme-main', exclusive=False) as (parent, _):
        with driver.opened(parent, identifier, os.O_RDONLY | os.O_DIRECTORY) as directory:
            allocation = driver.read_json(directory, 'allocation.json', 4096)
        request = {key: allocation[key] for key in ('schemaVersion', 'rootId', 'workspaceId', 'environmentSha256', 'diskMiB')}
        assert request['workspaceId'] == identifier and request['rootId'] == 'nvme-main'
        with driver.attested_payload(request) as (payload, proof):
            digest, count, total = hashlib.sha256(), 0, 0
            for folder, directories, names, descriptor in os.fwalk('.', dir_fd=payload, follow_symlinks=False):
                for name in sorted(directories + names):
                    info = os.stat(name, dir_fd=descriptor, follow_symlinks=False)
                    assert stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode)
                    relative = (Path(folder) / name).as_posix()
                    digest.update(json.dumps([relative, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
                        info.st_size if stat.S_ISREG(info.st_mode) else 0], separators=(',', ':')).encode())
                    count += 1
                    assert count <= 20000
                    if stat.S_ISREG(info.st_mode):
                        total += info.st_size
                        assert total <= 256 * 1024**2
                        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=descriptor)
                        with os.fdopen(fd, 'rb') as stream:
                            while block := stream.read(128 * 1024):
                                digest.update(block)
                directories.sort()
            quota.verify(payload, proof['projectId'], proof['diskMiB'])
            facts = dict(storageProof=proof, quota=list(quota.read_quota(payload, proof['projectId'])),
                treeSha256=digest.hexdigest(), entries=count, bytes=total)
        try:
            facts['marker'] = driver.read_json(parent, '.lifecycle-' + identifier + '.json', 4096)
        except FileNotFoundError:
            facts['marker'] = None
    print(json.dumps(facts, separators=(',', ':')))


if __name__ == '__main__':
    main()
