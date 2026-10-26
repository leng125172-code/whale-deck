#!/usr/bin/env python3
"""Owned real container + physically attested HDD capture; no installed writes.

Freeze the candidate and copy the current policy into a private sandbox. Only
synthetic source files and exact owned HDD objects are touched. The production
backup policy, existing histories and all core services remain unchanged.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import uuid

import validate_container_file_bff_extended as owned_fixture
from validate_container_file_staging import load


def main():
    if os.geteuid() != 0: raise RuntimeError('isolated Linux root required')
    host = Path(__file__).resolve().parents[1] / 'host'
    names = ('whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py',
             'whaledeck-container-upload.py', 'whaledeck-file-backups.py', 'whaledeck-container-backups.py')
    frozen = {name: (host / name).read_bytes() for name in names}
    hashes = {name: hashlib.sha256(value).hexdigest() for name, value in frozen.items()}
    with tempfile.TemporaryDirectory(prefix='wdv-container-capture-', dir='/var/lib') as temporary:
        bundle = Path(temporary)
        for name, content in frozen.items():
            path = bundle / name; path.write_bytes(content); path.chmod(0o750)
        reader = load(bundle / names[0], 'native_capture_reader')
        files = reader.trusted_module(names[1], 'native_capture_files')
        docker = reader.trusted_module(names[2], 'native_capture_docker')
        uploads = load(bundle / names[3], 'native_capture_uploads')
        backups = load(bundle / names[4], 'native_capture_hdd')
        container_backups = load(bundle / names[5], 'native_capture_engine')
        production = backups.FileBackups(files)
        configuration = production.configuration()
        # Attest the actual existing mount before creating even our private
        # namespace, without installing or rebinding any policy.
        with production.target() as (_, parent):
            private_name = 'wdv-container-capture-' + uuid.uuid4().hex
            os.mkdir(private_name, 0o700, dir_fd=parent); os.fsync(parent)
            target = Path(configuration['mountPoint']) / configuration['targetRelative'] / private_name
            owned_target = target.stat()
        policy = bundle / 'policy.json'
        policy.write_text(json.dumps({**configuration,
            'targetRelative': configuration['targetRelative'] + '/' + private_name}))
        policy.chmod(0o600)
        backups.CONFIGURATION = str(policy)
        state = bundle / 'state'; state.mkdir(mode=0o700)
        upload = uploads.ContainerUploads(reader, files, state)
        storage = backups.FileBackups(files, namespace='container-files-v1')
        engine = container_backups.ContainerBackups(upload, storage)
        operation, expected, binding = None, None, None
        completed = False
        try:
            with owned_fixture.fixture() as owned:
                identifier = owned['identifier']
                inspected = docker.api('GET', '/containers/' + identifier + '/json')
                target_info = docker.target(inspected, identifier, docker.registry())
                location = {'containerId': identifier, 'instanceId': target_info['instanceId'], 'path': 'work/中文.bin'}
                with reader.ContainerRoot(location, files, docker) as session:
                    original = reader.inspect(location, session, files)
                    parent = reader.inspect({**location, 'path': 'work'}, session, files)
                    replacement = hashlib.shake_256(b'new-container-capture-payload').digest(owned_fixture.SIZE)
                    operation = uuid.uuid4().hex
                    request = {**location, 'operationId': operation, 'owner': 'B' * 64,
                        'expectedVersion': original['version'], 'expectedParentVersion': parent['version'],
                        'sizeBytes': len(replacement), 'sha256': hashlib.sha256(replacement).hexdigest().upper()}
                    binding = {key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')}
                    with tempfile.TemporaryFile() as stream:
                        for offset in range(0, len(replacement), reader.CHUNK):
                            stream.write((json.dumps({'type': 'chunk', 'offset': offset,
                                'data': base64.b64encode(replacement[offset:offset + reader.CHUNK]).decode()}) + '\n').encode())
                        stream.write(b'{"type":"commit"}\n'); stream.seek(0)
                        assert upload.stage(request, session, files.InputLines(stream.fileno()))['state'] == 'Staged'
                    result = engine.capture(binding, session)
                    expected = owned['proofs']['中文.bin'][1]
                    capture_data = target / 'container-files-v1' / (operation + '.data')
                    assert capture_data.read_bytes() == (owned['folder'] / '中文.bin').read_bytes()
                    assert owned_fixture.file_digest(capture_data) == expected == result['sha256']
                    assert result['sizeBytes'] == owned_fixture.SIZE and result['sourceVersion'] == original['version']
                    assert result['differentSourceDevice'] == (capture_data.stat().st_dev != (owned['folder'] / '中文.bin').stat().st_dev)
                    assert engine.capture(binding, session) == result
                    metadata = json.loads((target / 'container-files-v1' / (operation + '.json')).read_bytes())
                    assert metadata['schemaVersion'] == 2 and metadata['resourceType'] == 'ContainerFile' and 'rootId' not in metadata
                    assert metadata['containerId'] == identifier and metadata['instanceId'] == target_info['instanceId']
                    assert reader.inspect(location, session, files)['version'] == original['version']
                    owned_fixture.verify_payload(owned['folder'], (owned['folder'].stat().st_dev, owned['folder'].stat().st_ino), owned['proofs'])
                    wrong_policy = json.loads(policy.read_bytes())
                    wrong_policy['filesystemUuid'] = '00000000-0000-0000-0000-000000000001' if configuration['filesystemUuid'] != '00000000-0000-0000-0000-000000000001' else '00000000-0000-0000-0000-000000000002'
                    policy.write_text(json.dumps(wrong_policy))
                    try:
                        engine.capture(binding, session)
                    except files.Rejected as error:
                        assert str(error) == 'FILES_BACKUP_DEVICE_MISMATCH'
                    else: raise AssertionError('wrong HDD UUID accepted')
                    policy.write_text(json.dumps({**configuration, 'targetRelative': configuration['targetRelative'] + '/' + private_name}))
                    assert upload.discard(binding, session)['state'] == 'Discarded'
                    assert capture_data.read_bytes() == (owned['folder'] / '中文.bin').read_bytes()
                    docker.api('POST', '/containers/' + identifier + '/stop?t=1')
                    try: engine.capture(binding, session)
                    except reader.Rejected: pass
                    else: raise AssertionError('stopped instance accepted')
                completed = True
            assert all((host / name).read_bytes() == value for name, value in frozen.items())
            print(json.dumps({'nativeContainerHddCapture': 'passed', 'fullBytes': owned_fixture.SIZE,
                'sha256': expected, 'sourceDifferentDevice': result['differentSourceDevice'],
                'originalAndParentVersionsPreserved': True, 'replayUuidStoppedInstanceAndDiscard': 'passed',
                'installedHelperRpcJobsOrUi': False, 'sourceSha256': hashes}), flush=True)
        finally:
            # Failure alone never authorizes broad deletion. Verify the exact
            # private directory and complete content before cleaning our data.
            assert target.resolve(strict=True) == target
            current = target.stat()
            assert (current.st_dev, current.st_ino, current.st_uid, stat.S_IMODE(current.st_mode)) == \
                   (owned_target.st_dev, owned_target.st_ino, 0, 0o700)
            namespace = target / 'container-files-v1'
            if completed and namespace.is_dir() and not namespace.is_symlink():
                assert set(item.name for item in namespace.iterdir()) == {operation + '.data', operation + '.json'}
                data = namespace / (operation + '.data'); info = data.lstat()
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600
                assert info.st_size == owned_fixture.SIZE and owned_fixture.file_digest(data) == expected
                metadata_path = namespace / (operation + '.json'); meta_info = metadata_path.lstat()
                assert stat.S_ISREG(meta_info.st_mode) and meta_info.st_nlink == 1 and meta_info.st_uid == 0
                meta = json.loads(metadata_path.read_bytes())
                assert all(meta[key] == binding[key] for key in ('containerId', 'instanceId', 'operationId', 'owner'))
                assert meta['resourceType'] == 'ContainerFile' and meta['state'] == 'Verified' and meta['sha256'] == expected
                data.unlink(); metadata_path.unlink(); namespace.rmdir()
            if not any(target.iterdir()):
                target.rmdir()
                print(json.dumps({'ownedHddCaptureRemoved': True}), flush=True)
            else:
                print(json.dumps({'ownedHddCaptureRemoved': False, 'preservedForInspection': True}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'nativeContainerHddCapture': 'failed', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
