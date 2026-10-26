#!/usr/bin/env python3
"""Installed signed-UDS write probe on one owned non-root Docker fixture.

Never installs services. Only exact, independently attested QA content is
removed; a failed or mismatching check preserves its evidence and data.
"""
import fcntl
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

STATE = Path('/var/lib/whaledeck-container-uploads')
HOST = Path('/usr/local/libexec')
REPOSITORY = Path(__file__).resolve().parents[2]
PHASE = 'startup'


def trusted(path, directory=False):
    assert path.resolve(strict=True) == path
    info = path.lstat()
    assert info.st_uid == 0 and not info.st_mode & 0o022
    assert stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode) and info.st_nlink == 1
    return info


def evidence(folder, name, value):
    assert folder.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch('container-write-evidence-[a-f0-9]{32}', folder.name)
    assert stat.S_IMODE(trusted(folder, True).st_mode) == 0o700
    assert name in ('baseline.json', 'rpc.json', 'verified.json')
    data = json.dumps(value, ensure_ascii=False, allow_nan=False).encode()
    assert len(data) <= 32768
    fd = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, 'wb', closefd=False) as output:
            output.write(data); output.flush(); os.fsync(fd)
    finally: os.close(fd)


def cleanup_receipt(directory, operation, expected, body):
    """Delete only this run's quiescent, terminal, fully bound QA receipt."""
    assert re.fullmatch('[a-f0-9]{32}', operation)
    assert set(expected) == {'containerId', 'instanceId', 'operationId', 'owner', 'targetProof', 'state'}
    assert expected['state'] in ('Published', 'Discarded') and expected['owner'] == 'B'*64
    assert stat.S_IMODE(trusted(directory, True).st_mode) == 0o700
    lock_path = directory / (operation + '.lock')
    assert stat.S_IMODE(trusted(lock_path).st_mode) == 0o600 and lock_path.stat().st_size == 0
    descriptor = os.open(lock_path, os.O_RDWR | os.O_NOFOLLOW)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        metadata = directory / (operation + '.json')
        assert stat.S_IMODE(trusted(metadata).st_mode) == 0o600 and metadata.stat().st_size <= 65536
        saved = json.loads(metadata.read_bytes())
        assert saved['schemaVersion'] == 1
        assert all(saved[key] == value for key, value in expected.items())
        assert all(saved['request'][key] == expected[key] for key in ('containerId', 'instanceId', 'operationId', 'owner'))
        assert saved['request']['path'] == ('work/original.bin' if expected['state'] == 'Published' else 'work/canceled.bin')
        assert saved['request']['sha256'] == hashlib.sha256(body).hexdigest().upper() and saved['request']['sizeBytes'] == len(body)
        digest = hashlib.sha256(b'whaledeck-container-upload-stage-v1\n' + json.dumps(saved['request'], sort_keys=True,
            separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()
        assert saved['fingerprint'] == digest
        names = {p.name for p in directory.iterdir() if p.name.startswith(operation + '.')}
        expected_names = {operation + '.json', operation + '.lock'}
        data = directory / (operation + '.data')
        if expected['state'] == 'Published':
            expected_names.add(data.name)
            info = trusted(data)
            assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size == len(body)
            assert [info.st_dev, info.st_ino] == saved['payloadIdentity'] and data.read_bytes() == body
        assert names == expected_names
        # Every check precedes deletion; never glob-remove receipts or payloads.
        if expected['state'] == 'Published': data.unlink()
        metadata.unlink(); lock_path.unlink()
        os.fsync(descriptor)
    finally: os.close(descriptor)


def cleanup_fence(directory, operation, expected):
    """QA-only cleanup after the sole known synthetic sender has terminated."""
    assert re.fullmatch('[a-f0-9]{32}', operation)
    assert set(expected)=={'containerId','instanceId','operationId','owner','targetProof'} and expected['operationId']==operation and expected['owner']=='B'*64
    assert stat.S_IMODE(trusted(directory,True).st_mode)==0o700
    lock=directory/(operation+'.lock');metadata=directory/(operation+'.json')
    assert stat.S_IMODE(trusted(lock).st_mode)==0o600 and lock.stat().st_size==0
    descriptor=os.open(lock,os.O_RDWR|os.O_NOFOLLOW)
    try:
        fcntl.flock(descriptor,fcntl.LOCK_EX|fcntl.LOCK_NB)
        assert stat.S_IMODE(trusted(metadata).st_mode)==0o600 and metadata.stat().st_size<=16384
        saved=json.loads(metadata.read_bytes())
        assert set(saved)==set(expected)|{'schemaVersion','resourceType','state','createdAtUnixMs'}
        assert type(saved['schemaVersion']) is int and saved['schemaVersion']==2 and saved['state']=='Prevented' and saved['resourceType']=='ContainerUploadCancellation'
        assert all(saved[key]==value for key,value in expected.items())
        assert type(saved['createdAtUnixMs']) is int and 0<=saved['createdAtUnixMs']<=253402300799999
        assert {p.name for p in directory.iterdir() if p.name.startswith(operation+'.')}=={lock.name,metadata.name}
        metadata.unlink();lock.unlink()
    finally:os.close(descriptor)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 3
    probe = Path(sys.argv[1]); host_revision = sys.argv[2]
    assert re.fullmatch('[a-f0-9]{40}', host_revision)
    assert probe.name == 'TerminalProbe' and probe.parent.parent == REPOSITORY / 'deploy/artifacts'
    assert re.fullmatch(r'terminal-probe\.[A-Za-z0-9]{6}', probe.parent.name)
    trusted(probe.parent, True); trusted(probe); trusted(probe.parent / 'SHA256SUMS')
    subprocess.run(['sha256sum', '--check', '--strict', 'SHA256SUMS'], cwd=probe.parent, check=True, capture_output=True, timeout=15)
    revision = owned.common.command(['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY), 'rev-parse', 'HEAD']).decode().strip()
    names = ('whaledeck-privileged', 'whaledeck-container-file-write.py', 'whaledeck-container-upload.py', 'whaledeck-container-backups.py',
        'whaledeck-container-publish.py', 'whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-file-backups.py', 'whaledeck-container-terminal.py')
    for name in names:
        trusted(HOST / name)
        expected = owned.common.command(['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY), 'show', revision + ':deploy/host/' + name])
        assert (HOST / name).read_bytes() == expected
    for component in ('agent', 'maintenance'):
        application = 'WhaleDeck.Agent' if component == 'agent' else 'WhaleDeck.MaintenanceHost'
        binary = Path('/opt') / ('whaledeck-' + component) / application
        trusted(binary)
        assert json.loads(owned.common.command([str(binary), '--build-info']))['sourceRevision'] == host_revision
    assert stat.S_IMODE(trusted(STATE, True).st_mode) == 0o700
    folder = REPOSITORY / 'deploy/artifacts' / ('container-write-evidence-' + uuid.uuid4().hex)
    folder.mkdir(mode=0o700)
    first, second, third = uuid.uuid4().hex, uuid.uuid4().hex, uuid.uuid4().hex
    assert not any(p.name.startswith((first + '.', second + '.', third + '.')) for p in STATE.iterdir())
    payload = (bytes(range(251)) * ((owned.common.SIZE + 250) // 251))[:owned.common.SIZE]
    backup_module = load(HOST / 'whaledeck-file-backups.py', 'installed_write_backup_observer')
    file_module = load(HOST / 'whaledeck-files.py', 'installed_write_file_observer')
    storage = backup_module.FileBackups(file_module)
    with storage.target(create_namespace=False): pass
    configuration = storage.configuration()
    history = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'container-files-v1'
    PHASE = 'owned-container-and-independent-baseline'
    with owned.fixture() as fixture:
        container = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        instance = str(container['State']['Pid']) + ':' + container['State']['StartedAt']
        core = json.loads(owned.common.command(['docker', 'inspect', 'whaledeck-api']))[0]
        request = {'revision': revision, 'containerId': fixture['identifier'], 'instanceId': instance,
            'coreId': core['Id'], 'coreInstanceId': str(core['State']['Pid']) + ':' + core['State']['StartedAt'],
            'operationId': first, 'cancelOperationId': second, 'fenceOperationId': third, 'originalSha256': hashlib.sha256(fixture['old']).hexdigest().upper()}
        evidence(folder, 'baseline.json', {**request, 'hostRevision': host_revision, 'ownedFolder': str(fixture['folder'])})
        before = (fixture['folder'] / 'original.bin').stat()
        PHASE = 'installed-signed-uds'
        process = subprocess.run([str(probe), 'container-file-writes'], input=json.dumps(request), text=True, capture_output=True,
            timeout=190, env={'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8'})
        assert len(process.stdout.encode()) <= 32768 and len(process.stderr.encode()) <= 32768
        if process.returncode:
            try: result = json.loads(process.stderr)
            except ValueError: result = {}
            evidence(folder, 'rpc.json', result)
            phase = result.get('phase'); code = result.get('rpcStatus'); own_code = result.get('fileCode')
            print(json.dumps({'installedWriteProbeFailed': True,
                'phase': phase if isinstance(phase, str) and re.fullmatch('[a-z-]{1,80}', phase) else None,
                'rpcStatus': code if isinstance(code, str) and re.fullmatch('[A-Za-z]{1,40}', code) else None,
                'fileCode': own_code if isinstance(own_code, str) and re.fullmatch('(?:CONTAINER_UPLOAD|FILES)_[A-Z_]{1,80}', own_code) else None}), flush=True)
            raise AssertionError('installed UDS probe failed')
        result = json.loads(process.stdout); evidence(folder, 'rpc.json', result)
        assert result['installedContainerFileWriteRpc'] == 'passed' and result['revision'] == revision and result['bytes'] == len(payload)
        assert all(result[key] is True for key in ('fiveMethods', 'bodyMethodReplayAndLegacyRefused', 'completeInstalledReadback', 'hddOverwriteProof', 'explicitStagedDiscard', 'preReceiveFenceAndLateSenderRefused'))
        assert result['humanBffOrUi'] is False and (result['operationId'], result['cancelOperationId']) == (first, second)
        assert result['fenceOperationId']==third
        PHASE = 'independent-full-output-and-history'
        output = fixture['folder'] / 'original.bin'; retained = fixture['folder'] / '.whaledeck-container-files-v1' / (first + '.upload')
        assert output.read_bytes() == payload and (output.stat().st_uid, output.stat().st_gid, stat.S_IMODE(output.stat().st_mode)) == (1000, 1000, 0o640)
        assert retained.read_bytes() == fixture['old'] and retained.stat().st_ino == before.st_ino
        assert not (fixture['folder'] / 'canceled.bin').exists()
        assert stat.S_IMODE(trusted(history, True).st_mode) == 0o700
        archive = history / (first + '.data'); metadata = history / (first + '.json')
        for path in (archive, metadata): assert stat.S_IMODE(trusted(path).st_mode) == 0o600
        assert archive.stat().st_size == len(fixture['old']) and archive.read_bytes() == fixture['old'] and metadata.stat().st_size <= 65536
        capture = json.loads(metadata.read_bytes())
        binding = {'containerId': fixture['identifier'], 'instanceId': instance, 'owner': 'B'*64, 'targetProof': result['targetProof']}
        assert all(capture[key] == value for key, value in {**binding, 'operationId': first, 'schemaVersion': 2, 'resourceType': 'ContainerFile',
            'path': 'work/original.bin', 'state': 'Verified', 'sizeBytes': len(fixture['old']), 'sha256': request['originalSha256'],
            'storageId': 'database-platform.backup.hdd', 'captureKind': 'Overwrite', 'captureState': 'Available'}.items())
        fixture['proofs']['original.bin'] = owned.common.fingerprint(output), hashlib.sha256(payload).hexdigest().upper()
        fixture['private'] = '.whaledeck-container-files-v1'
        fixture['privateProofs'][retained.name] = owned.common.fingerprint(retained), request['originalSha256']
        evidence(folder, 'verified.json', {'fullOutputAndRetainedAndHddOriginal': True, 'bytes': len(payload), 'hostRevision': host_revision, 'sourceRevision': revision})
        PHASE = 'precise-owned-receipt-and-history-cleanup'
        cleanup_receipt(STATE, first, {**binding, 'operationId': first, 'state': 'Published'}, payload)
        cleanup_receipt(STATE, second, {**binding, 'operationId': second, 'state': 'Discarded'}, payload)
        cleanup_fence(STATE, third, {**binding, 'operationId':third})
        archive.unlink(); metadata.unlink()
    print(json.dumps({'installedContainerFileWriter': 'passed', 'hostRevision': host_revision, 'probeRevision': revision, 'bytes': len(payload),
        'fullUdsAndIndependentFilesystemProof': True, 'ownedFixtureReceiptAndHistoryCleaned': True, 'preReceiveFenceAndLateSenderRefused': True, 'humanBffOrUi': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerFileWriter': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'failureLocations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
