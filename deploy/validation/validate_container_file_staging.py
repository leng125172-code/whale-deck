#!/usr/bin/env python3
"""Candidate staging against one owned real Docker container, without RPC/UI.

Uses the existing immutable-image/non-root/read-only/no-network fixture. Its
source, mount, image, core-resource and exact cleanup proofs remain mandatory.
"""
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import uuid

import validate_container_file_bff_extended as owned_fixture


def load(path, name):
    specification = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(specification)
    sys.modules[name] = module
    specification.loader.exec_module(module)
    return module


def main():
    if os.geteuid() != 0: raise RuntimeError('isolated Linux root required')
    host = Path(__file__).resolve().parents[1] / 'host'
    names = ('whaledeck-container-files.py', 'whaledeck-files.py',
             'whaledeck-container-terminal.py', 'whaledeck-container-upload.py')
    frozen = {name: (host / name).read_bytes() for name in names}
    source_hashes = {name: hashlib.sha256(value).hexdigest() for name, value in frozen.items()}
    payload = hashlib.shake_256(b'container-staging-native-full-content-v1').digest(owned_fixture.SIZE)
    digest = hashlib.sha256(payload).hexdigest().upper()
    with tempfile.TemporaryDirectory(prefix='wd-native-container-stage-') as temporary:
        bundle = Path(temporary)
        for name, content in frozen.items():
            path = bundle / name
            path.write_bytes(content)
            path.chmod(0o750)
        reader = load(bundle / names[0], 'native_staging_container_reads')
        files = reader.trusted_module(names[1], 'native_staging_file_primitives')
        docker = reader.trusted_module(names[2], 'native_staging_docker')
        uploads = load(bundle / names[3], 'native_staging_uploads')
        state = bundle / 'state'
        state.mkdir(mode=0o700)
        engine = uploads.ContainerUploads(reader, files, state)
        with owned_fixture.fixture() as owned:
            identifier = owned['identifier']
            inspected = docker.api('GET', '/containers/' + identifier + '/json')
            target = docker.target(inspected, identifier, docker.registry())
            location = {'containerId': identifier, 'instanceId': target['instanceId'], 'path': 'work'}
            with reader.ContainerRoot(location, files, docker) as session:
                def stage(path, expected):
                    parent = reader.inspect(location, session, files)
                    request = {**location, 'path': path, 'expectedVersion': expected,
                               'expectedParentVersion': parent['version'], 'operationId': uuid.uuid4().hex,
                               'owner': 'B' * 64, 'sizeBytes': len(payload), 'sha256': digest}
                    with tempfile.TemporaryFile() as source:
                        for offset in range(0, len(payload), reader.CHUNK):
                            row = {'type': 'chunk', 'offset': offset,
                                   'data': base64.b64encode(payload[offset:offset + reader.CHUNK]).decode()}
                            source.write((json.dumps(row) + '\n').encode())
                        source.write(b'{"type":"commit"}\n')
                        source.seek(0)
                        result = engine.stage(request, session, files.InputLines(source.fileno()))
                    assert result['state'] == 'Staged' and result['sha256'] == digest
                    captured = state / (request['operationId'] + '.data')
                    assert captured.read_bytes() == payload
                    assert hashlib.sha256(captured.read_bytes()).hexdigest().upper() == digest
                    return request

                source = {**location, 'path': 'work/中文.bin'}
                metadata = reader.inspect(source, session, files)
                overwrite = stage(source['path'], metadata['version'])
                create = stage('work/new-upload.bin', 'ABSENT')
                assert reader.inspect(source, session, files)['version'] == metadata['version']
                assert not (owned['folder'] / 'new-upload.bin').exists()
                assert owned_fixture.file_digest(owned['folder'] / '中文.bin') == owned['proofs']['中文.bin'][1]
                for request in (overwrite, create):
                    reference = {key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')}
                    assert engine.inspect(reference, session)['state'] == 'Staged'
                    try: engine.inspect({**reference, 'owner': 'C' * 64}, session)
                    except reader.Rejected: pass
                    else: raise AssertionError('foreign owner accepted')
                    assert engine.discard(reference, session)['state'] == 'Discarded'
                assert not list(state.glob('*.data')) and not list(state.glob('*.partial'))
                owned_fixture.verify_mount_container(inspected, identifier, owned['folder'].name, owned['folder'])
                docker.api('POST', '/containers/' + identifier + '/stop?t=1')
                try: engine.inspect({key: create[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')}, session)
                except (reader.Rejected, ValueError): pass
                else: raise AssertionError('stopped instance accepted')
            core = docker.api('GET', '/containers/whaledeck-api/json')
            core_request = {'containerId': core['Id'], 'instanceId': str(core['State']['Pid']) + ':' + core['State']['StartedAt'], 'path': ''}
            try:
                with reader.ContainerRoot(core_request, files, docker): pass
            except PermissionError: pass
            else: raise AssertionError('protected core accepted')
        assert all((host / name).read_bytes() == content for name, content in frozen.items())
    print(json.dumps({'nativeContainerStaging': 'passed', 'fullBytes': len(payload), 'sha256': digest,
                      'overwriteAndCreatePrivateOnly': True, 'originalSourcesUnchanged': True,
                      'ownerAndStoppedInstanceAndCoreRefused': True, 'ownedFixtureCleaned': True,
                      'installedHelperOrRpcChanged': False, 'sourceSha256': source_hashes}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'nativeContainerStaging': 'failed', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
