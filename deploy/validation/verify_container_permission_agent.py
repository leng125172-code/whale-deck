#!/usr/bin/env python3
"""Read-only second-process Root/HDD/identity/cleanup verification.

Full source bytes were checked by the first gate before exact fixture removal;
this verifier does not claim to reread files that no longer exist.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted


def digest(domain, value):
    return hashlib.sha256(domain + json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2
    evidence = Path(sys.argv[1]); repo = Path(__file__).resolve().parents[2]
    assert evidence.parent == repo / 'deploy/artifacts' and re.fullmatch('container-permission-agent-[a-f0-9]{32}', evidence.name)
    trusted(evidence, True); baseline = json.loads((evidence / 'baseline.json').read_bytes())
    verified = json.loads((evidence / 'verified.json').read_bytes()); after = json.loads((evidence / 'after.json').read_bytes())
    rpc = json.loads((evidence / 'rpc.stdout').read_bytes()); parameters = baseline['parameters']
    assert verified['revision'] == rpc['revision'] == parameters['revision'] and rpc['containerPermissionProbe'] == 'passed'
    assert verified['humanBffOrUi'] is False and verified['installedSignedUdsAndFixedHelper'] is True
    assert verified['operations'] == 6 and verified['completed'] == 5 and verified['aborted'] == 1 and verified['metadataHistories'] == 5
    reader = load(Path('/usr/local/libexec/whaledeck-container-files.py'), 'permission_second_reader')
    files = reader.trusted_module('whaledeck-files.py', 'permission_second_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'permission_second_hdd').FileBackups(files)
    records = {}; operations = set()
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        directory = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1'; trusted(directory, True)
        for item in rpc['receipts']:
            name, operation = item['name'], item['operationId']; assert re.fullmatch('[a-f0-9]{32}', operation) and operation not in operations; operations.add(operation)
            path = Path('/var/lib/whaledeck-container-permissions') / (operation + '.json'); trusted(path)
            raw = path.read_bytes(); assert raw == (evidence / (name + '.root.json')).read_bytes()
            record = json.loads(raw); records[name] = record; plan = record['plan']; command = plan['command']
            assert record['operationId'] == operation and record['owner'] == 'B'*64
            assert command['containerId'] == parameters['containerId'] and command['instanceId'] == parameters['instanceId']
            assert plan['fingerprint'] == digest(b'whaledeck-container-permission-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})
            # Public source version is a context-bound nine-field identity.
            expected = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([command['containerId'], command['instanceId'], command['path'], *plan['sourceIdentity']],
                ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
            assert command['expectedVersion'] == expected
            response = json.loads(item['result']); assert response['operationId'] == operation and response['fingerprint'] == plan['fingerprint']
            history = directory / (operation + '.json')
            if name == 'file-cancel':
                assert record['state'] == 'Aborted' and record['modeConfirmed'] is False and not record['historyMayExist'] and not record['historyVerified']
                assert record['historyDigest'] == record['version'] == '' and record['actualIdentity'] is None and not history.exists()
            else:
                assert record['state'] == 'Completed' and record['modeConfirmed'] is True and record['historyMayExist'] and record['historyVerified']
                trusted(history); assert stat.S_IMODE(history.stat().st_mode) == 0o600 and history.read_bytes() == (evidence / (name + '.hdd.json')).read_bytes()
                descriptor = os.open(history, os.O_RDONLY | os.O_NOFOLLOW)
                try: storage.attest(configuration, descriptor, read_only=True)
                finally: os.close(descriptor)
                saved = json.loads(history.read_bytes())
                assert saved['resourceType'] == 'ContainerPermissionMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
                assert saved['plan'] == plan and saved['operationId'] == operation and saved['owner'] == record['owner']
                assert saved['historyDigest'] == record['historyDigest'] == response['historyDigest'] == digest(b'whaledeck-container-permission-history-v1\n', {k: v for k, v in saved.items() if k != 'historyDigest'})
                original, actual = plan['sourceIdentity'], record['actualIdentity']
                assert actual[:2] == original[:2] and actual[3:8] == original[3:8] and (actual[2] & 0o777) == int(command['mode'], 8)
    assert records['file-mode']['plan']['sourceIdentity'] == baseline['original']
    assert records['file-noop']['plan']['sourceIdentity'] == records['file-mode']['actualIdentity'] == records['file-noop']['actualIdentity']
    assert records['file-restore']['plan']['sourceIdentity'] == records['file-noop']['actualIdentity']
    assert records['file-cancel']['plan']['sourceIdentity'] == records['file-restore']['actualIdentity'] == after['file']
    assert after['file'][:8] == baseline['original'][:8]
    assert records['directory-mode']['plan']['sourceIdentity'] == baseline['directory']
    assert records['directory-restore']['plan']['sourceIdentity'] == records['directory-mode']['actualIdentity']
    assert after['directory'] == records['directory-restore']['actualIdentity'] and after['directory'][:8] == baseline['directory'][:8]
    assert after['child'] == baseline['child']
    assert not Path(baseline['source']).exists() and verified['ownedSource'] == baseline['source']
    missing = subprocess.run(['docker', 'inspect', parameters['containerId']], capture_output=True, timeout=10)
    assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    assert {name: list(value) for name, value in owned.common.core_snapshot().items()} == baseline['coreBefore']
    units = subprocess.run(['systemctl', 'list-units', '--all', '--no-legend', 'whaledeck-container-permissions-*'], capture_output=True, text=True, timeout=10)
    assert units.returncode == 0 and not units.stdout.strip()
    print(json.dumps({'independentReadOnlyPermissionProof': 'passed', 'rootRecords': len(records), 'physicalHddMetadata': 5,
        'originalInodeIdentityChain': True, 'exactCleanup': True, 'coreUnchanged': True, 'sourceBytesReReadAfterRemoval': False}), flush=True)


if __name__ == '__main__': main()
