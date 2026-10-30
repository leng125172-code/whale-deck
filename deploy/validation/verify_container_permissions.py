#!/usr/bin/env python3
"""Read-only independent proof after owned candidate sources were removed.

No metadata mode writes, container actions, history creation or SQL mutations.
The original parent read all content before cleanup, not this later process.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common


def encode(value): return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    name = sys.argv[1]; assert re.fullmatch('container-permissions-[a-f0-9]{32}', name)
    directory = Path(__file__).resolve().parents[1] / 'artifacts' / name
    assert directory.resolve(strict=True) == directory and directory.stat().st_uid == 0 and stat.S_IMODE(directory.stat().st_mode) == 0o700
    baseline = json.loads((directory / 'baseline.json').read_bytes()); verified = json.loads((directory / 'verified.json').read_bytes())
    results = json.loads((directory / 'results.json').read_bytes()); physical = json.loads((directory / 'physical-final.json').read_bytes())
    assert baseline['revision'] == verified['revision'] and re.fullmatch('[a-f0-9]{40}', baseline['revision'])
    assert baseline['candidateOnly'] and verified['actualAttestedHddMetadata'] and verified['fullBytesAndOriginalInodesAndChildrenVerified']
    assert verified['exactContainerAndSourceAndVolumeCleaned'] and verified['candidateRootReceiptsAndHddHistoryRetained'] and verified['coreUnchanged']
    assert verified['installedHelper'] is verified['signedRpc'] is verified['humanBffOrUi'] is False
    assert len(results) == verified['operations'] == 20 and verified['actualCandidateCrashCuts'] == 8
    assert len({row['operationId'] for row in results}) == 20
    assert sum(row['state'] == 'Completed' for row in results) == 16 and sum(row['state'] == 'Aborted' for row in results) == 4
    content = hashlib.shake_256(('owned-container-permissions:' + baseline['name']).encode()).digest(2 * 1024**2 + 7)
    assert hashlib.sha256(content).hexdigest().upper() == baseline['sha256'] and len(content) == baseline['bytesPerFile']
    files = load('/usr/local/libexec/whaledeck-files.py', 'independent_permission_primitives')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py', 'independent_permission_hdd').FileBackups(files)
    chronology = {}; records = []
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        target = files.open_at(base, 'container-permissions-v1', os.O_RDONLY | os.O_DIRECTORY)
        try:
            info = os.fstat(target)
            assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o700
            storage.attest(configuration, target, read_only=True)
            assert info.st_dev == baseline['sourceDevice'] != baseline['nvmeDevice']
            for row in results:
                operation = row['operationId']; assert re.fullmatch('[a-f0-9]{32}', operation)
                root = directory / 'root-state' / (operation + '.json'); identity = root.lstat()
                assert stat.S_ISREG(identity.st_mode) and identity.st_uid == 0 and identity.st_nlink == 1 and stat.S_IMODE(identity.st_mode) == 0o600
                receipt = json.loads(root.read_bytes()); plan = receipt['plan']
                assert receipt['state'] == row['state'] and receipt['owner'] == 'B'*64 and receipt['operationId'] == operation
                assert receipt['modeConfirmed'] is (row['state'] == 'Completed')
                assert receipt['historyMayExist'] and receipt['historyVerified'] and receipt['historyDigest'] == row['historyDigest']
                assert plan['command']['containerId'] == row['containerId'] == baseline['containerId']
                assert plan['command']['instanceId'] == row['instanceId'] == baseline['instanceId']
                assert row['prefix'] in ('work', 'fast') and row['relative'] in ('权限.bin', '权限目录')
                assert plan['command']['path'] == row['prefix'] + '/' + row['relative'] and plan['sourceIdentity'] == row['before']
                expected_fingerprint = hashlib.sha256(b'whaledeck-container-permission-plan-v1\n' + encode({k: v for k, v in plan.items() if k != 'fingerprint'})).hexdigest().upper()
                assert expected_fingerprint == plan['fingerprint'] == row['fingerprint']
                expected_version = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([baseline['containerId'], baseline['instanceId'], plan['command']['path'], *row['before']], ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
                assert expected_version == plan['command']['expectedVersion']
                previous = chronology.get((row['prefix'], row['relative']), baseline['original'][row['prefix']][row['relative']])
                assert row['before'] == previous
                chronology[(row['prefix'], row['relative'])] = row['after']
                wanted = list(row['before'])
                if row['state'] == 'Completed': wanted[2] = wanted[2] & ~0o777 | int(plan['command']['mode'], 8)
                assert row['after'][:8] == wanted[:8]
                if row['state'] == 'Completed': assert receipt['actualIdentity'] == row['after'] and receipt['version'] == row['version']
                else: assert row['after'] == row['before'] and receipt['actualIdentity'] is None and row['version'] == '' and row['crashPhase'] == 'Prepared'
                saved = storage.read_json(target, operation + '.json')
                assert saved == json.loads((directory / (operation + '.hdd.json')).read_bytes())
                assert saved['resourceType'] == 'ContainerPermissionMetadata' and saved['schemaVersion'] == 1 and saved['operationId'] == operation
                assert saved['owner'] == 'B'*64 and saved['plan'] == plan
                digest = hashlib.sha256(b'whaledeck-container-permission-history-v1\n' + encode({k: v for k, v in saved.items() if k != 'historyDigest'})).hexdigest().upper()
                assert digest == saved['historyDigest'] == receipt['historyDigest']
                records.append({'operationId': operation, 'state': row['state'], 'prefix': row['prefix'], 'kind': row['kind'], 'crashPhase': row['crashPhase'], 'historyDigest': digest})
        finally: os.close(target)
    for prefix in ('work', 'fast'):
        for relative in baseline['original'][prefix]:
            if relative: assert baseline['original'][prefix][relative][:8] == physical[prefix][relative][:8]
        for relative in ('权限.bin', '权限目录'): assert physical[prefix][relative] == chronology[(prefix, relative)]
        for relative in ('权限目录/代码.bin', '权限目录/空目录'): assert physical[prefix][relative] == baseline['original'][prefix][relative]
    source = Path(baseline['source'])
    assert source.parent == Path('/data/WhaleDeck/apps') and source.name == baseline['name'] and not os.path.lexists(source)
    assert re.fullmatch('wdv-container-permissions-[a-f0-9]{32}', source.name)
    for args in (['docker', 'inspect', baseline['containerId']], ['docker', 'volume', 'inspect', baseline['volume']['Name']]):
        absent = subprocess.run(args, capture_output=True, timeout=15); assert absent.returncode != 0 and b'no such' in absent.stderr.lower()
    assert common.core_snapshot() == {key: tuple(value) for key, value in baseline['coreBefore'].items()}
    preflight()
    health = subprocess.check_output(['docker', 'ps', '--format', '{{.Names}}|{{.Status}}'], text=True, timeout=20).splitlines()
    assert len([row for row in health if row.startswith(('whaledeck-', 'database-platform-')) and '(healthy)' in row]) == 11
    proof = {'revision': baseline['revision'], 'operations': records, 'actualCrashCuts': 8, 'completed': 16, 'aborted': 4,
        'originalInodeAndModeChronologyAndHddReceiptsVerified': True, 'childrenUnchanged': True, 'exactFixtureAbsent': True,
        'coreUnchangedHealthy': 11, 'independentReadOnlyCheck': True, 'installedOrBffOrUi': False}
    (directory / 'independent-final.json').write_text(json.dumps(proof))
    print(json.dumps({'permissionCandidateIndependent': 'passed', 'operations': 20, 'actualCrashCuts': 8, 'coreHealthy': 11, 'installedOrBffOrUi': False}), flush=True)


if __name__ == '__main__': main()
