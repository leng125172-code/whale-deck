#!/usr/bin/env python3
"""Bounded, read-only installed source/claim/kernel/HDD retirement facts."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid


def tree(root):
    assert root.is_dir() and not root.is_symlink()
    result, total = {}, 0
    for path in [root, *sorted(root.rglob('*'))]:
        info = path.lstat()
        assert not path.is_symlink() and (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode))
        row = dict(device=info.st_dev, inode=info.st_ino, mode=info.st_mode, uid=info.st_uid, gid=info.st_gid)
        if stat.S_ISREG(info.st_mode):
            assert info.st_nlink == 1
            total += info.st_size
            assert total <= 256 * 1024**2
            with path.open('rb') as stream:
                row.update(size=info.st_size, mtimeNs=info.st_mtime_ns, sha256=hashlib.file_digest(stream, 'sha256').hexdigest())
        result[str(path.relative_to(root))] = row
        assert len(result) <= 20000
    return result


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) in (5, 6)
    mode, identifier, archive, project = sys.argv[1:5]
    assert mode in ('before', 'after')
    assert all(str(uuid.UUID(value)) == value for value in (identifier, archive))
    assert re.fullmatch('[1-9][0-9]{0,9}', project) and int(project) < 2**32
    spec = importlib.util.spec_from_file_location('installed_storage_transport',
        '/usr/local/libexec/whaledeck-development-storage-transport.py')
    loader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loader)
    files, storage, quota = (loader.load(name) for name in ('whaledeck-files.py',
        'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py'))
    driver = storage.WorkspaceStorage(files, quota=quota)
    with driver.locked_root('nvme-main', exclusive=False) as (parent, _):
        def exists(name):
            try:
                os.stat(name, dir_fd=parent, follow_symlinks=False)
                return True
            except FileNotFoundError:
                return False
        facts = dict(workspaceId=identifier, archiveId=archive, projectId=int(project),
            sourceExists=exists(identifier), claimExists=exists('.quota-' + project + '.json'),
            kernelQuota=list(quota.read_quota(parent, int(project))))
        if mode == 'before':
            assert len(sys.argv) == 5 and facts['sourceExists'] and facts['claimExists']
            proof = driver.read_json(parent, '.quota-' + project + '.json', 4096)
            assert proof['workspaceId'] == identifier and proof['projectId'] == int(project)
            with driver.opened(parent, identifier, os.O_RDONLY | os.O_DIRECTORY) as allocation:
                with driver.opened(allocation, 'data', os.O_RDONLY | os.O_DIRECTORY) as payload:
                    quota.verify(payload, int(project), proof['diskMiB'])
            facts['proof'] = proof
        else:
            assert len(sys.argv) == 6 and str(uuid.UUID(sys.argv[5])) == sys.argv[5]
            assert not facts['sourceExists'] and not facts['claimExists']
            quota.require_unused(parent, int(project))
            assert facts['kernelQuota'] == [0] * 8
            with driver.opened(parent, '.retirement-' + identifier, os.O_RDONLY | os.O_DIRECTORY) as journal:
                prepared = driver.read_json(journal, 'prepared.json', 32 * 1024**2)
                request = prepared['request']
                assert request['retirementId'] == sys.argv[5]
                assert request['prerequisite']['archiveId'] == archive
                assert request['prerequisite']['storage']['workspaceId'] == identifier
                binding = hashlib.sha256(driver.canonical(request)).hexdigest()
                assert prepared['bindingSha256'] == binding
                receipt = driver.read_json(journal, 'receipt.json', 32768)
                assert receipt['workspaceId'] == identifier and receipt['retirementId'] == sys.argv[5]
                assert receipt['projectId'] == int(project)
                assert receipt['requestSha256'] == binding
                driver.read_json(journal, 'quota-released.json', 4096,
                    expected=dict(requestSha256=binding, projectId=int(project)))
                assert all(receipt[name] is True for name in ('sourceRemoved', 'quotaReleased', 'claimReleased', 'retainsHdd'))
                facts['receipt'] = receipt
                try:
                    os.stat('source', dir_fd=journal, follow_symlinks=False)
                except FileNotFoundError:
                    facts['detachedSourceExists'] = False
                else:
                    raise AssertionError('DETACHED_RETIREMENT_SOURCE_REMAINS')
    facts['archive'] = tree(Path('/data/DevSpace/workspace-archives-v1') / archive)
    query = "BEGIN TRANSACTION READ ONLY; SELECT json_build_object('cpuMillis',coalesce(sum(cpu_millis),0),'memoryMiB',coalesce(sum(memory_mi_b),0),'diskMiB',coalesce(sum(disk_mi_b),0)) FROM development_workspaces w WHERE NOT EXISTS(SELECT 1 FROM development_workspace_retirement_completions c WHERE c.workspace_id=w.id); COMMIT;"
    facts['reservedBudget'] = json.loads(subprocess.check_output(['docker', 'exec', '-i', '-u', 'postgres',
        'database-platform-postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck'],
        input=query.encode(), timeout=20))
    print(json.dumps(facts, separators=(',', ':')))


if __name__ == '__main__':
    main()
