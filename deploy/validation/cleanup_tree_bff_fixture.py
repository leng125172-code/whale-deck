#!/usr/bin/env python3
"""Precisely clean a preserved, terminal tree BFF acceptance fixture only."""
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import traceback
import uuid


def camel(value):
    return {key[0].lower() + key[1:]: camel(member) if isinstance(member, dict) else member for key, member in value.items()}


def main():
    assert os.geteuid() == 0
    marker = sys.argv[1]; assert re.fullmatch(r'wdv-tree-bff-[a-f0-9]{16}', marker)
    folder = Path('/data/WhaleDeck/files') / marker; identity = folder.lstat()
    assert folder.resolve() == folder and identity.st_uid == 0 and stat.S_ISDIR(identity.st_mode)
    sql = "SELECT json_agg(json_build_object('state',state,'execution',execution_job_id,'command',command_json,'completed',completed_at_utc IS NOT NULL)) FROM file_tree_copy_operations WHERE command_json::text LIKE '%" + marker + "%';"
    rows = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
    assert len(rows) == 4 and all(row['completed'] for row in rows)
    assert sorted(row['state'] for row in rows) == ['Canceled', 'Failed', 'Failed', 'Succeeded']
    for row in rows:
        row['command'] = camel(row['command'])
        assert row['command']['source']['path'].startswith(marker + '/') and row['command']['target']['path'].startswith(marker + '/')
        if row['execution'] is None: continue
        operation = row['execution']; assert re.fullmatch(r'[a-f0-9-]{36}', operation)
        root_operation = uuid.UUID(operation).hex
        path = Path('/var/lib/whaledeck-files/tree-copies') / (root_operation + '.json'); info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and info.st_size <= 32768
        receipt = json.loads(path.read_bytes())
        expected_command = {'source': {**row['command']['source'], 'expectedVersion': row['command']['expectedVersion']}, 'target': row['command']['target']}
        assert receipt['operationId'] == root_operation and receipt['plan']['command'] == expected_command
        assert receipt['state'] == ('Completed' if row['state'] == 'Succeeded' else 'Aborted')
        count = subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', "SELECT count(*) FROM file_path_locks WHERE operation_id='" + operation + "';"], text=True).strip()
        assert count == '0'
    expected_dirs = {'source', 'source/空', 'source/代码', 'copied', 'copied/空', 'copied/代码', 'stale-source', 'protected', '.whaledeck-files'}
    expected_files = {'source/代码/payload.bin', 'copied/代码/payload.bin', 'stale-source/data.bin', 'protected/.env'}
    original = folder / 'source/代码/payload.bin'; copied = folder / 'copied/代码/payload.bin'
    assert original.read_bytes() == copied.read_bytes() and original.stat().st_size == 2097159
    assert (original.stat().st_mtime_ns, stat.S_IMODE(original.stat().st_mode)) == (copied.stat().st_mtime_ns, stat.S_IMODE(copied.stat().st_mode))
    assert copied.stat().st_uid == copied.stat().st_gid == 1000 and original.stat().st_ino != copied.stat().st_ino
    assert (folder / 'stale-source/data.bin').read_bytes() == b'changed-after-plan'
    assert (folder / 'protected/.env').read_bytes() == b'fixture-only-not-a-secret'
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
    dirs = set(); files = set()
    for parent, names, entries in os.walk(folder, followlinks=False):
        for name in names + entries:
            path = Path(parent) / name; info = path.lstat()
            assert info.st_dev == identity.st_dev and info.st_uid in (0, 1000) and not stat.S_ISLNK(info.st_mode)
            relative = str(path.relative_to(folder))
            if stat.S_ISDIR(info.st_mode): dirs.add(relative)
            else:
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                files.add(relative)
    assert dirs == expected_dirs and files == expected_files
    current = folder.lstat(); assert (current.st_dev, current.st_ino) == (identity.st_dev, identity.st_ino)
    shutil.rmtree(folder)
    print(json.dumps({'preservedOwnedTreeFixtureRemoved': True, 'terminalIntentsRootReceiptsAndHumanAuditRetained': True}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'preservedTreeCleanup': 'failed-preserved', 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
