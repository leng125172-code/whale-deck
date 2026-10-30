#!/usr/bin/env python3
"""Installed signed permission RPC on exact owned HDD fixture; no human business/UI.

Prove original inode/content/owner/mtime and children, verify attested HDD
metadata and permanent Root receipts, then clean only those exact objects.
A failed proof preserves the original fixture for inspection.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted
from validate_container_directory_restart import preflight

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 3; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; probe = Path(sys.argv[2])
    assert probe.parent.parent == repo / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', probe.parent.name) and probe.name == 'TerminalProbe'
    trusted(probe); trusted(probe.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=probe.parent, check=True, capture_output=True, timeout=20)
    names = ('whaledeck-container-permissions.py', 'whaledeck-container-permission-transport.py', 'whaledeck-container-directories.py',
        'whaledeck-container-files.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-backups.py', 'whaledeck-privileged')
    digests = {}
    for name in names:
        path = Path('/usr/local/libexec') / name; trusted(path)
        frozen = owned.common.command(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + name])
        assert path.read_bytes() == frozen; digests[name] = hashlib.sha256(frozen).hexdigest()
    state = Path('/var/lib/whaledeck-container-permissions'); trusted(state, True); assert stat.S_IMODE(state.stat().st_mode) == 0o700
    preflight(); before = owned.common.core_snapshot(); assert len(before) == 11
    evidence = repo / 'deploy/artifacts' / ('container-permission-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    reader = load(Path('/usr/local/libexec/whaledeck-container-files.py'), 'installed_permission_reader')
    files = reader.trusted_module('whaledeck-files.py', 'installed_permission_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'installed_permission_storage').FileBackups(files)
    with owned.fixture() as fixture:
        PHASE = 'owned-fixture'
        source = fixture['folder'] / 'original.bin'; original = list(reader.fingerprint(source.stat())); content = source.read_bytes()
        directory = fixture['folder'] / 'permission-folder'; directory.mkdir(mode=0o750); directory.chmod(0o750); os.chown(directory, 2100, 3100)
        child = directory / 'child.bin'; child.write_bytes(content); os.chown(child, 2200, 3200); child.chmod(0o640)
        child_before = list(reader.fingerprint(child.stat())); directory_before = list(reader.fingerprint(directory.stat()))
        inspected = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        location = {'containerId': fixture['identifier'], 'instanceId': str(inspected['State']['Pid']) + ':' + inspected['State']['StartedAt']}
        parameters = {'revision': revision, **location, 'coreId': before['/whaledeck-api'][0],
            'fileVersion': reader.version(location, 'work/original.bin', source.stat()),
            'directoryVersion': reader.version(location, 'work/permission-folder', directory.stat())}
        (evidence / 'baseline.json').write_text(json.dumps({'parameters': parameters, 'source': str(fixture['folder']), 'original': original,
            'directory': directory_before, 'child': child_before, 'sha256': hashlib.sha256(content).hexdigest().upper(),
            'bytes': len(content), 'coreBefore': before, 'installedSourceSha256': digests}))
        PHASE = 'signed-uds-helper'
        result = subprocess.run([str(probe), 'container-permissions'], input=json.dumps(parameters), text=True, capture_output=True, timeout=210)
        assert len(result.stdout.encode()) <= 65536 and len(result.stderr.encode()) <= 16384
        (evidence / 'rpc.stdout').write_text(result.stdout); (evidence / 'rpc.stderr').write_text(result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    item = json.loads(line)
                    if item.get('terminalProbe') == 'failed': print(json.dumps({k: item.get(k) for k in ('phase', 'rpcStatus', 'fileCode')}), flush=True)
                except ValueError: pass
        assert result.returncode == 0, 'INSTALLED_PERMISSION_RPC_FAILED'
        rpc = json.loads(result.stdout); assert rpc['containerPermissionProbe'] == 'passed'
        assert (rpc['revision'], rpc['containerId'], rpc['instanceId']) == (revision, location['containerId'], location['instanceId'])
        assert len(rpc['receipts']) == 6; records = {}
        PHASE = 'root-and-physical-hdd-proof'
        with storage.target(create_namespace=False, read_only=True) as (configuration, base):
            target = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1'; trusted(target, True)
            assert stat.S_IMODE(target.stat().st_mode) == 0o700
            for item in rpc['receipts']:
                name, operation = item['name'], item['operationId']; assert re.fullmatch('[a-f0-9]{32}', operation)
                path = state / (operation + '.json'); trusted(path)
                assert stat.S_IMODE(path.stat().st_mode) == 0o600 and path.stat().st_size <= 16384
                record = json.loads(path.read_bytes()); records[name] = record; plan = record['plan']; response = json.loads(item['result'])
                assert record['schemaVersion'] == 1 and record['operationId'] == operation and record['owner'] == 'B'*64
                assert plan['command']['containerId'] == location['containerId'] and plan['command']['instanceId'] == location['instanceId']
                assert plan['uid'] == plan['gid'] == 1000
                expected_path = 'work/permission-folder' if name.startswith('directory-') else 'work/original.bin'
                assert plan['command']['path'] == expected_path and plan['kind'] == ('Directory' if name.startswith('directory-') else 'File')
                expected_mode = {'file-mode': '0600', 'file-noop': '0600', 'file-restore': '0640', 'directory-mode': '0700', 'directory-restore': '0750', 'file-cancel': '0600'}[name]
                assert plan['command']['mode'] == expected_mode and plan['noChange'] == (name == 'file-noop')
                unsigned = {k: v for k, v in plan.items() if k != 'fingerprint'}
                assert plan['fingerprint'] == hashlib.sha256(b'whaledeck-container-permission-plan-v1\n' + json.dumps(unsigned,
                    ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
                assert response['operationId'] == operation and response['fingerprint'] == plan['fingerprint'] and response['targetProof'] == plan['targetProof']
                history = target / (operation + '.json')
                if name == 'file-cancel':
                    assert record['state'] == 'Aborted' and record['modeConfirmed'] is False and record['historyMayExist'] is False and record['historyVerified'] is False
                    assert record['historyDigest'] == record['version'] == '' and record['actualIdentity'] is None and not history.exists()
                else:
                    trusted(history)
                    descriptor = os.open(history, os.O_RDONLY | os.O_NOFOLLOW)
                    try: storage.attest(configuration, descriptor, read_only=True)
                    finally: os.close(descriptor)
                    saved = json.loads(history.read_bytes()); assert stat.S_IMODE(history.stat().st_mode) == 0o600
                    assert set(saved) == {'schemaVersion', 'resourceType', 'storageId', 'operationId', 'owner', 'plan', 'createdAtUnixMs', 'historyDigest'}
                    assert saved['schemaVersion'] == 1 and saved['resourceType'] == 'ContainerPermissionMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
                    assert saved['operationId'] == operation and saved['owner'] == record['owner'] and saved['plan'] == plan
                    digest = hashlib.sha256(b'whaledeck-container-permission-history-v1\n' + json.dumps({k: v for k, v in saved.items() if k != 'historyDigest'},
                        sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
                    assert saved['historyDigest'] == record['historyDigest'] == response['historyDigest'] == digest
                    assert record['state'] == 'Completed' and record['modeConfirmed'] is True and record['historyMayExist'] is True and record['historyVerified'] is True
                    assert record['version'] == response['version']
                    (evidence / (name + '.hdd.json')).write_bytes(history.read_bytes())
                (evidence / (name + '.root.json')).write_bytes(path.read_bytes())
        PHASE = 'original-inode-bytes-and-children'
        final_file = list(reader.fingerprint(source.stat())); final_directory = list(reader.fingerprint(directory.stat()))
        assert final_file[:8] == original[:8] and final_directory[:8] == directory_before[:8]
        assert final_file == records['file-restore']['actualIdentity'] and final_directory == records['directory-restore']['actualIdentity']
        assert source.read_bytes() == child.read_bytes() == content and list(reader.fingerprint(child.stat())) == child_before
        assert records['file-noop']['actualIdentity'] == records['file-mode']['actualIdentity']
        assert {p.name for p in directory.iterdir()} == {'child.bin'}
        (evidence / 'after.json').write_text(json.dumps({'file': final_file, 'directory': final_directory, 'child': child_before, 'rootRecords': list(records)}))
        # Current identity was independently established above; only this fixture's
        # expected proof is advanced for its exact normal cleanup routine.
        fixture['proofs']['original.bin'] = (owned.common.fingerprint(source), hashlib.sha256(content).hexdigest().upper())
        assert list(reader.fingerprint(child.stat())) == child_before; child.unlink()
        assert list(reader.fingerprint(directory.stat()))[:5] == final_directory[:5] and not list(directory.iterdir()); directory.rmdir()
    assert owned.common.core_snapshot() == before
    units = subprocess.run(['systemctl', 'list-units', '--all', '--no-legend', 'whaledeck-container-permissions-*'], text=True, capture_output=True, timeout=10)
    assert units.returncode == 0 and not units.stdout.strip()
    assert not fixture['folder'].exists()
    missing = subprocess.run(['docker', 'inspect', location['containerId']], capture_output=True, timeout=10)
    assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    verified = {'revision': revision, **location, 'ownedSource': str(fixture['folder']), 'installedSignedUdsAndFixedHelper': True,
        'humanBffOrUi': False, 'operations': 6, 'completed': 5, 'aborted': 1, 'metadataHistories': 5,
        'fullBytesAndOriginalInodesOwnersMtime': True, 'childrenUnchanged': True, 'coreUnchanged': True, 'ownedFixtureRemoved': True,
        'rootAndHddRecordsRetained': True, 'activeTransientUnits': 0}
    (evidence / 'verified.json').write_text(json.dumps(verified))
    for path in evidence.iterdir(): path.chmod(0o600)
    PHASE = 'done'; print(json.dumps({'evidence': str(evidence), 'installedPermissionProtocol': 'passed', 'humanBffOrUi': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'installedPermissionProtocol': 'failed', 'phase': PHASE, 'failureType': type(error).__name__}), flush=True)
        raise
