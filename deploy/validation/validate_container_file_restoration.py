#!/usr/bin/env python3
"""Frozen attribute engine on one owned Docker container and real HDD.

No installed helper, RPC, SQL or UI claim. Preserve uncertain sandbox content;
cleanup requires exact fixture, inode, byte and private-history proofs.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned_fixture
from validate_container_file_staging import load

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0; os.umask(0o077)
    host = Path(__file__).resolve().parents[1] / 'host'
    names = ('whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py',
        'whaledeck-container-upload.py', 'whaledeck-file-backups.py', 'whaledeck-container-backups.py',
        'whaledeck-container-publish.py', 'whaledeck-container-history.py', 'whaledeck-container-restore.py')
    frozen = {name: (host / name).read_bytes() for name in names}
    hashes = {name: hashlib.sha256(data).hexdigest() for name, data in frozen.items()}
    evidence = host.parent / 'artifacts' / ('container-attributes-native-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    def save(name, value):
        assert name in ('baseline.json', 'verified.json', 'journals.json')
        path = evidence / name; path.write_text(json.dumps(value)); path.chmod(0o600)
    bundle = Path(tempfile.mkdtemp(prefix='wdv-container-attributes-', dir='/var/lib'))
    for name, data in frozen.items(): path = bundle / name; path.write_bytes(data); path.chmod(0o750)
    reader = load(bundle / names[0], 'native_attributes_reader')
    files = reader.trusted_module(names[1], 'native_attributes_files')
    docker = reader.trusted_module(names[2], 'native_attributes_docker')
    uploads = load(bundle / names[3], 'native_attributes_uploads')
    backups = load(bundle / names[4], 'native_attributes_hdd')
    captures = load(bundle / names[5], 'native_attributes_captures')
    publishing = load(bundle / names[6], 'native_attributes_publish')
    history = load(bundle / names[7], 'native_attributes_history')
    restoring = load(bundle / names[8], 'native_attributes_restore')
    production = backups.FileBackups(files); configuration = production.configuration()
    PHASE = 'attested-physical-hdd-private-sandbox'
    with production.target() as (_, parent):
        namespace = 'wdv-container-attributes-' + uuid.uuid4().hex
        os.mkdir(namespace, 0o700, dir_fd=parent); os.fsync(parent)
        target = Path(configuration['mountPoint']) / configuration['targetRelative'] / namespace
        identity = target.stat().st_dev, target.stat().st_ino
    policy = bundle / 'policy.json'; policy.write_text(json.dumps({**configuration,
        'targetRelative': configuration['targetRelative'] + '/' + namespace})); policy.chmod(0o600)
    backups.CONFIGURATION = str(policy)
    state = bundle / 'state'; state.mkdir(mode=0o700)
    upload = uploads.ContainerUploads(reader, files, state)
    storage = backups.FileBackups(files, namespace='container-files-v1')
    capture = captures.ContainerBackups(upload, storage)
    catalog = history.ContainerHistory(upload, storage)
    restoration = restoring.ContainerRestorations(upload, catalog); upload.restorations = restoration
    publisher = publishing.ContainerPublisher(upload, capture)
    completed = False; histories = {}; operations = []
    save('baseline.json', {'sourceHashes': hashes, 'bundle': str(bundle), 'hddSandbox': str(target)})
    try:
        with owned_fixture.fixture() as owned:
            identifier, folder = owned['identifier'], owned['folder']; output = folder / 'original.bin'
            save('baseline.json', {'sourceHashes': hashes, 'bundle': str(bundle), 'hddSandbox': str(target),
                'containerId': identifier, 'sourceDirectory': str(folder)})
            desired = {'uid': 1001, 'gid': 2002, 'mode': 0o640, 'modifiedNs': 1700000000123456789}
            os.chown(output, desired['uid'], desired['gid']); output.chmod(desired['mode'])
            os.utime(output, ns=(desired['modifiedNs'], desired['modifiedNs']))
            old = output.read_bytes(); old_sha = hashlib.sha256(old).hexdigest().upper()
            owned['proofs']['original.bin'] = owned_fixture.common.fingerprint(output), old_sha
            def location():
                actual = docker.api('GET', '/containers/' + identifier + '/json')
                current = docker.target(actual, identifier, docker.registry())
                return {'containerId': identifier, 'instanceId': current['instanceId'], 'path': 'work/original.bin'}
            def stage(session, path, content, source=None):
                parent = reader.inspect({**session.request, 'path': path.rpartition('/')[0]}, session, files)
                try: version = reader.inspect({**session.request, 'path': path}, session, files)['version']
                except FileNotFoundError: version = 'ABSENT'
                request = {**session.request, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': 'B' * 64,
                    'expectedVersion': version, 'expectedParentVersion': parent['version'],
                    'sizeBytes': len(content), 'sha256': hashlib.sha256(content).hexdigest().upper()}
                if source is not None: request['restoreSource'] = source
                with tempfile.TemporaryFile() as stream:
                    for offset in range(0, len(content), reader.CHUNK):
                        stream.write((json.dumps({'type': 'chunk', 'offset': offset,
                            'data': base64.b64encode(content[offset:offset + reader.CHUNK]).decode()}) + '\n').encode())
                    stream.write(b'{"type":"commit"}\n'); stream.seek(0)
                    assert upload.stage(request, session, files.InputLines(stream.fileno()))['state'] == 'Staged'
                operations.append(request['operationId'])
                return {key: request[key] for key in ('containerId', 'instanceId', 'operationId', 'owner')}
            PHASE = 'capture-real-historical-owner-mode-nanosecond-time'
            original_location = location()
            with reader.ContainerRoot(original_location, files, docker) as session:
                scope = stage(session, 'work/original.bin', b'private candidate capture placeholder')
                result = capture.capture(scope, session); assert result['sha256'] == old_sha
                histories[scope['operationId']] = (scope, old_sha, len(old))
                entries = catalog.listing({**original_location, 'after': '', 'take': 50, 'expectedVersion': ''}, session)['entries']
                entry = next(item for item in entries if item['captureId'] == scope['operationId'])
                source = {'captureId': entry['captureId'], 'captureHash': entry['captureHash'], 'path': entry['path']}
            current_bytes = hashlib.shake_256(b'owned-attribute-current-target').digest(len(old)); current_sha = hashlib.sha256(current_bytes).hexdigest().upper()
            output.write_bytes(current_bytes); os.chown(output, 1000, 1000); output.chmod(0o600)
            current_identity = files.publication_identity(output.stat())
            owned['proofs']['original.bin'] = owned_fixture.common.fingerprint(output), current_sha
            PHASE = 'owned-container-restart-and-restore-old-capture-with-real-attributes'
            owned_fixture.common.command(['docker', 'stop', '--time', '1', identifier]); owned_fixture.common.command(['docker', 'start', identifier])
            current_location = location(); assert current_location['instanceId'] != original_location['instanceId']
            with reader.ContainerRoot(current_location, files, docker) as session:
                restored = stage(session, 'work/original.bin', old, source)
                result = publisher.publish(restored, session); assert result['state'] == 'Published' and result['previousRetained']
                assert output.read_bytes() == old and restoration.matches(output.stat(), desired)
                saved = json.loads((state / (restored['operationId'] + '.json')).read_bytes())
                assert saved['schemaVersion'] == 4 and saved['restorationAttributes'] == desired
                assert files.publication_identity(output.stat()) == saved['publication']['pendingProof']
                private = folder / publishing.PRIVATE / (restored['operationId'] + '.upload')
                assert files.publication_identity(private.stat()) == current_identity and private.read_bytes() == current_bytes
                owned['private'] = publishing.PRIVATE
                owned['privateProofs'][private.name] = owned_fixture.common.fingerprint(private), current_sha
                owned['proofs']['original.bin'] = owned_fixture.common.fingerprint(output), old_sha
                histories[restored['operationId']] = (restored, current_sha, len(current_bytes))
                PHASE = 'other-path-new-file-keeps-historical-attributes-and-source-unchanged'
                new = stage(session, 'work/restored.bin', old, source)
                result = publisher.publish(new, session); assert result['state'] == 'Published' and not result['previousRetained']
                new_output = folder / 'restored.bin'; assert new_output.read_bytes() == old and restoration.matches(new_output.stat(), desired)
                owned['proofs']['restored.bin'] = owned_fixture.common.fingerprint(new_output), old_sha
                assert restoration.matches(output.stat(), desired) and output.read_bytes() == old
                PHASE = 'bad-capture-hash-refuses-without-source-or-target-change'
                try: stage(session, 'work/rejected.bin', old, {**source, 'captureHash': 'F' * 64})
                except reader.Rejected as error: assert str(error) == 'CONTAINER_HISTORY_CAPTURE_CHANGED'
                else: raise AssertionError('BAD_CAPTURE_HASH_ACCEPTED')
                assert not (folder / 'rejected.bin').exists()
                assert all((host / name).read_bytes() == data for name, data in frozen.items())
                save('journals.json', {operation: json.loads((state / (operation + '.json')).read_bytes()) for operation in operations})
                save('verified.json', {'sourceHashes': hashes, 'fullBytesEach': len(old), 'actualOwnedRestart': True,
                    'overwriteAndOtherPathCreate': True, 'historicalUidGidModeAndNanosecondTime': desired,
                    'oldInodeAndPhysicalHddFullBytes': True, 'badCaptureHashRejected': True, 'installedHelperRpcSqlApiOrUi': False})
        completed = True
    finally:
        assert target.resolve(strict=True) == target and (target.stat().st_dev, target.stat().st_ino) == identity
        directory = target / 'container-files-v1'
        if completed:
            assert {path.name for path in directory.iterdir()} == {operation + suffix for operation in histories for suffix in ('.json', '.data')}
            for operation, (scope, sha, size) in histories.items():
                data, metadata = directory / (operation + '.data'), directory / (operation + '.json')
                for path in (data, metadata):
                    info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                saved = json.loads(metadata.read_bytes())
                assert all(saved[key] == scope[key] for key in ('containerId', 'instanceId', 'operationId', 'owner'))
                assert saved['state'] == 'Verified' and saved['sha256'] == sha and data.stat().st_size == size and owned_fixture.common.file_digest(data) == sha
                data.unlink(); metadata.unlink()
            directory.rmdir(); assert not any(target.iterdir()); target.rmdir(); shutil.rmtree(bundle)
        print(json.dumps({'nativeContainerAttributes': 'passed' if completed else 'unverified', 'installedHelperRpcSqlApiOrUi': False,
            'ownedHddAndBundleRemoved': completed, 'uncertainSandboxPreserved': not completed, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'nativeContainerAttributes': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}), file=sys.stderr)
        raise SystemExit(1) from None
