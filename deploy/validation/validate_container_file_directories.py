#!/usr/bin/env python3
"""Frozen candidate mkdir engine against one exact owned real Docker container.

No installed helper, RPC, BFF or UI is modified. No production process is
restarted. Child SIGKILL gates stop only the candidate Python engine process.
The existing fixture proves its image/mount/ownership/core-resource cleanup.
Unrecognized objects are preserved; all removal is exact empty-directory rmdir.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import signal
import stat
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load


def kill_at(engine, files, request, session, point):
    ready_read, ready_write = os.pipe(); continue_read, continue_write = os.pipe()
    pid = os.fork()
    if pid == 0:
        os.close(ready_read); os.close(continue_write)
        save, exchange = engine.save, files.exchange
        def pause():
            os.write(ready_write, b'R'); os.read(continue_read, 1)
        def save_at(state, record):
            save(state, record)
            if point == record['state']: pause()
        def exchange_at(*args):
            value = exchange(*args)
            if point == 'after-rename': pause()
            return value
        try:
            engine.save, files.exchange = save_at, exchange_at
            engine.apply(request, session)
            os._exit(3)
        except BaseException: os._exit(4)
    os.close(ready_write); os.close(continue_read)
    try:
        assert select.select([ready_read], [], [], 30)[0], 'candidate child did not reach gate'
        assert os.read(ready_read, 1) == b'R'
        os.kill(pid, signal.SIGKILL)
        assert os.waitpid(pid, 0) == (pid, signal.SIGKILL)
    finally:
        os.close(ready_read); os.close(continue_write)
        try: os.kill(pid, signal.SIGKILL); os.waitpid(pid, 0)
        except (ProcessLookupError, ChildProcessError): pass


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    assert owned.common.command(['git', '-C', str(repository), 'rev-parse', 'HEAD']).decode().strip() == revision
    names = ('whaledeck-container-directories.py', 'whaledeck-container-files.py', 'whaledeck-files.py',
        'whaledeck-container-terminal.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py')
    frozen = {name: owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name]) for name in names}
    for name, content in frozen.items(): assert (repository / 'deploy/host' / name).read_bytes() == content
    assert Path(__file__).read_bytes() == owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/validation/' + Path(__file__).name])
    core_before = owned.common.core_snapshot()
    evidence = repository / 'deploy/artifacts' / ('container-directory-engine-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); state = evidence / 'root-state'; state.mkdir(mode=0o700)
    results = []
    with tempfile.TemporaryDirectory(prefix='wd-native-container-directory-') as temporary:
        bundle = Path(temporary)
        for name, content in frozen.items():
            path = bundle / name; path.write_bytes(content); path.chmod(0o750)
        reader = load(bundle / names[1], 'native_directory_read')
        files = reader.trusted_module(names[2], 'native_directory_files')
        docker = reader.trusted_module(names[3], 'native_directory_docker')
        uploads = load(bundle / names[4], 'native_directory_upload')
        publisher = load(bundle / names[5], 'native_directory_publisher')
        directories = load(bundle / names[0], 'native_directory_engine')
        upload_state = bundle / 'unused-upload-state'; upload_state.mkdir(mode=0o700)
        upload = uploads.ContainerUploads(reader, files, upload_state)
        publication = publisher.ContainerPublisher(upload, type('Backups', (), {'u': upload})())
        engine = directories.ContainerDirectories(upload, publication, state)
        with owned.fixture() as fixture:
            identifier = fixture['identifier']; folder = fixture['folder']
            container = docker.api('GET', '/containers/' + identifier + '/json')
            target = docker.target(container, identifier, docker.registry())
            location = {'containerId': identifier, 'instanceId': target['instanceId'], 'path': 'work'}
            private = folder / engine.PRIVATE; private_identity = None
            with reader.ContainerRoot(location, files, docker) as session:
                for index, point in enumerate(('normal', 'Preparing', 'Prepared', 'Applying', 'after-rename')):
                    leaf = '新建目录-' + str(index)
                    command = {**location, 'path': 'work/' + leaf, 'mode': '0750'}
                    parent_before = reader.fingerprint(folder.stat())
                    plan = engine.plan(command, session)
                    assert reader.fingerprint(folder.stat()) == parent_before
                    assert (plan['uid'], plan['gid']) == (1000, 1000)
                    request = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': plan}
                    if point == 'normal': result = engine.apply(request, session)
                    else:
                        kill_at(engine, files, request, session, point)
                        record_before = json.loads((state / (request['operationId'] + '.json')).read_bytes())
                        expected_phase = 'Applying' if point == 'after-rename' else point
                        assert record_before['state'] == expected_phase
                        result = engine.inspect(request, session)
                    expected = 'Completed' if point in ('normal', 'after-rename') else 'Aborted'
                    assert result['state'] == expected
                    replay = engine.apply(request, session)
                    assert replay['state'] == expected and replay['replayed'] is True
                    output = folder / leaf
                    if expected == 'Completed':
                        info = output.lstat()
                        assert stat.S_ISDIR(info.st_mode) and not output.is_symlink() and not list(output.iterdir())
                        assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) == (1000, 1000, 0o750)
                        assert result['bindingCurrent'] is True and replay['bindingCurrent'] is True
                        assert result['version'] == reader.version(command, command['path'], info) == replay['version']
                        assert [info.st_dev, info.st_ino] == json.loads((state / (request['operationId'] + '.json')).read_bytes())['pendingIdentity']
                        # Prove the exact inode remains empty immediately before
                        # removing this one known synthetic directory.
                        assert reader.fingerprint(output.lstat()) == reader.fingerprint(info) and not list(output.iterdir()); output.rmdir()
                    else: assert not output.exists() and not output.is_symlink()
                    assert engine.inspect(request, session)['state'] == expected
                    if private.exists():
                        info = private.lstat()
                        assert stat.S_ISDIR(info.st_mode) and private.resolve(strict=True) == private
                        assert (info.st_uid, stat.S_IMODE(info.st_mode)) == (0, 0o700) and not list(private.iterdir())
                        identity = (info.st_dev, info.st_ino)
                        if private_identity is None: private_identity = identity
                        else: assert private_identity == identity
                    results.append({'gate': point, 'operationId': request['operationId'], 'state': expected,
                        'planFingerprint': plan['fingerprint'], 'owner': [plan['uid'], plan['gid']], 'replay': replay['state']})
                # Physical mount policy is evaluated against actual Docker
                # configuration: read-only root and the writable bind root.
                for path in ('tmp/not-allowed', 'work'):
                    try: engine.plan({**location, 'path': path, 'mode': '0750'}, session)
                    except reader.Rejected as error: assert str(error) in ('CONTAINER_UPLOAD_PUBLISH_READ_ONLY', 'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT')
                    else: raise AssertionError('read-only or mount-root creation accepted')
                private_info = private.lstat()
                assert private_identity == (private_info.st_dev, private_info.st_ino) and not list(private.iterdir())
                private.rmdir()
            core = docker.api('GET', '/containers/whaledeck-api/json')
            try:
                core_target = {'containerId': core['Id'], 'instanceId': str(core['State']['Pid']) + ':' + core['State']['StartedAt']}
                with reader.ContainerRoot(core_target, files, docker): pass
            except PermissionError as error: assert str(error) == 'CONTAINER_PROTECTED'
            else: raise AssertionError('core container file entry accepted')
    assert owned.common.core_snapshot() == core_before
    result = {'revision': revision, 'sourceSha256': {name: hashlib.sha256(content).hexdigest() for name, content in frozen.items()},
        'engineOnly': True, 'installedOrRpcOrUi': False, 'checks': results, 'coreUnchanged': True,
        'ownedContainerAndSourceRemoved': True, 'rootEvidenceRetained': True}
    output = evidence / 'verified.json'; output.write_text(json.dumps(result)); output.chmod(0o600)
    print(json.dumps({'evidence': str(evidence), 'gates': len(results), 'coreUnchanged': True, 'engineOnly': True}), flush=True)


if __name__ == '__main__': main()
