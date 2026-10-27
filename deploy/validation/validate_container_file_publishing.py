#!/usr/bin/env python3
"""Frozen candidate publication on one owned real container and physical HDD.

Uses an existing immutable image, no network, non-root UID, read-only root and
one owned writable bind. No installed helper/RPC/API/UI or product data writes.
Unverified fixture data is preserved, never recursively deleted from /data.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import signal
import stat
import subprocess
import sys
import tempfile
import time
import uuid

import validate_container_file_bff_extended as common
from validate_container_file_staging import load


def verify_container(value, identifier, name, folder):
    assert re.fullmatch('[a-f0-9]{64}', identifier) and value['Id'] == identifier
    assert value['Name'] == '/' + name and value['Image'] == common.IMAGE
    assert value['Config']['Labels'].get('io.whaledeck.validation') == name and value['Config']['User'] == '1000:1000'
    host = value['HostConfig']
    assert host['NetworkMode'] == 'none' and host['ReadonlyRootfs'] is True and not host.get('Binds')
    assert len(host['Mounts']) == 1
    mount = host['Mounts'][0]
    assert mount['Type'] == 'bind' and mount['Source'] == str(folder) and mount['Target'] == '/work' and mount.get('ReadOnly', False) is False
    assert len(value['Mounts']) == 1
    mount = value['Mounts'][0]
    assert mount['Type'] == 'bind' and mount['Source'] == str(folder) and mount['Destination'] == '/work' and mount['RW'] is True


@contextlib.contextmanager
def fixture(*, restartable_core=()):
    # Explicit opt-in for the installed Worker restart gate. All older callers
    # keep the exact ID/image/status/StartedAt invariant; no resource recreation.
    assert restartable_core in ((), ('whaledeck-worker',))
    common.verify_apps_directory(common.APPS, pwd.getpwnam('whaledeck-agent').pw_uid)
    baseline = common.core_snapshot()
    image = json.loads(common.command(['docker', 'image', 'inspect', common.IMAGE]))[0]
    assert image['Id'] == common.IMAGE and not image['Config'].get('Volumes')
    name = 'wdv-container-publish-' + uuid.uuid4().hex
    folder = common.APPS / name
    folder.mkdir(mode=0o755); folder.chmod(0o755)
    directory = folder.stat(); identifier = None
    old = hashlib.shake_256(('owned-container-original:' + name).encode()).digest(common.SIZE)
    source = folder / 'original.bin'; source.write_bytes(old); os.chown(source, 1000, 1000); source.chmod(0o640)
    owned = {'folder': folder, 'old': old, 'proofs': {'original.bin': (common.fingerprint(source), hashlib.sha256(old).hexdigest().upper())},
        'private': None, 'privateProofs': {}}
    def verify_files():
        current = folder.lstat()
        assert folder.resolve(strict=True) == folder and (current.st_dev, current.st_ino, current.st_uid, stat.S_IMODE(current.st_mode)) == \
            (directory.st_dev, directory.st_ino, 0, 0o755)
        names = set(owned['proofs']) | ({owned['private']} if owned['private'] else set())
        assert {entry.name for entry in folder.iterdir()} == names
        for relative, (identity, digest) in owned['proofs'].items():
            path = folder / relative
            assert path.parent == folder and not path.is_symlink() and common.fingerprint(path) == identity and path.stat().st_nlink == 1
            assert common.file_digest(path) == digest
        if owned['private']:
            private = folder / owned['private']; info = private.lstat()
            assert private.resolve(strict=True) == private and (info.st_uid, stat.S_IMODE(info.st_mode)) == (0, 0o700)
            assert {entry.name for entry in private.iterdir()} == set(owned['privateProofs'])
            for leaf, (identity, digest) in owned['privateProofs'].items():
                path = private / leaf
                assert not path.is_symlink() and common.fingerprint(path) == identity and path.stat().st_nlink == 1 and common.file_digest(path) == digest
        current_core = common.core_snapshot()
        assert current_core.keys() == baseline.keys()
        for name, original in baseline.items():
            observed = current_core[name]
            if name.lstrip('/') in restartable_core:
                assert observed[:3] == original[:3] and observed[2] == 'running'
            else: assert observed == original
    try:
        identifier = common.command(['docker', 'run', '--detach', '--name', name, '--network', 'none', '--read-only',
            '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', '1000:1000', '--pids-limit', '16',
            '--memory', '32m', '--cpus', '0.25', '--log-driver', 'local', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2',
            '--label', 'io.whaledeck.validation=' + name, '--mount', 'type=bind,src=' + str(folder) + ',dst=/work',
            '--entrypoint', '/bin/sleep', common.IMAGE, '600']).decode().strip()
        verify_container(json.loads(common.command(['docker', 'inspect', identifier]))[0], identifier, name, folder)
        owned.update(identifier=identifier)
        yield owned
        verify_files()
    finally:
        lookup = subprocess.run(['docker', 'inspect', identifier or name], capture_output=True, timeout=20)
        if lookup.returncode == 0:
            actual = json.loads(lookup.stdout)[0]; exact = actual['Id']
            if identifier is not None: assert exact == identifier
            verify_container(actual, exact, name, folder)
            common.command(['docker', 'rm', '--force', exact])
            assert subprocess.run(['docker', 'inspect', exact], capture_output=True, timeout=10).returncode != 0
        else: assert identifier is None, 'owned container disappeared'
        verified = False
        try: verify_files(); verified = True
        except (AssertionError, OSError, subprocess.SubprocessError): pass
        if verified:
            for name in owned['proofs']: (folder / name).unlink()
            if owned['private']:
                private = folder / owned['private']
                for name in owned['privateProofs']: (private / name).unlink()
                private.rmdir()
            folder.rmdir()
        print(json.dumps({'ownedPublishContainerRemoved': True, 'verifiedFixtureRemoved': verified,
            'unverifiedFixturePreserved': not verified}), flush=True)


def overlay_check(reader, files, docker, upload, publisher, state, target):
    """An owned writable layer, including one original lower-image file.

    No bind/volume, no Docker Exec. Deleting this exact container removes only
    its disposable layer; proof/content failures never touch any host source.
    """
    baseline = common.core_snapshot()
    name = 'wdv-container-publish-overlay-' + uuid.uuid4().hex
    identifier = None
    def verify(value):
        assert value['Id'] == identifier and value['Name'] == '/' + name and value['Image'] == common.IMAGE
        assert value['Config']['Labels'].get('io.whaledeck.validation') == name and value['Config']['User'] == '1000:1000'
        assert value['HostConfig']['ReadonlyRootfs'] is False and value['HostConfig']['NetworkMode'] == 'none'
        assert not value['Mounts'] and not value['HostConfig'].get('Binds') and not value['HostConfig'].get('Mounts')
    try:
        identifier = common.command(['docker', 'run', '--detach', '--name', name, '--network', 'none',
            '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', '1000:1000', '--pids-limit', '16',
            '--memory', '32m', '--cpus', '0.25', '--log-driver', 'local', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2',
            '--label', 'io.whaledeck.validation=' + name, '--entrypoint', '/bin/sleep', common.IMAGE, '600']).decode().strip()
        actual = docker.api('GET', '/containers/' + identifier + '/json'); verify(actual)
        info = docker.target(actual, identifier, docker.registry())
        location = {'containerId': identifier, 'instanceId': info['instanceId'], 'path': ''}
        with reader.ContainerRoot(location, files, docker) as session:
            selected = None
            for path in ('etc/issue', 'etc/alpine-release', 'etc/debian_version'):
                try: metadata = reader.inspect({**location, 'path': path}, session, files)
                except (OSError, reader.Rejected): continue
                if metadata['kind'] == 'File' and not metadata['isProtected']:
                    selected = path; break
            assert selected is not None, 'fixed image has no approved lower-layer text fixture'
            descriptor = files.open_at(session.root, selected, os.O_RDONLY | os.O_NOFOLLOW)
            try:
                before = files.regular(descriptor); assert before.st_size < 65536
                original = os.read(descriptor, 65536); assert len(original) == before.st_size
            finally: os.close(descriptor)
            replacement = hashlib.shake_256(b'native-lower-layer-replacement').digest(common.SIZE)
            def stage(path, body, absent=False):
                parent_path = path.rpartition('/')[0]
                request = {**location, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': 'B' * 64,
                    'expectedParentVersion': reader.inspect({**location, 'path': parent_path}, session, files)['version'],
                    'expectedVersion': 'ABSENT' if absent else reader.inspect({**location, 'path': path}, session, files)['version'],
                    'sizeBytes': len(body), 'sha256': hashlib.sha256(body).hexdigest().upper()}
                with tempfile.TemporaryFile() as stream:
                    import base64
                    for offset in range(0, len(body), reader.CHUNK):
                        stream.write((json.dumps({'type': 'chunk', 'offset': offset,
                            'data': base64.b64encode(body[offset:offset + reader.CHUNK]).decode()}) + '\n').encode())
                    stream.write(b'{"type":"commit"}\n'); stream.seek(0)
                    upload.stage(request, session, files.InputLines(stream.fileno()))
                return {key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')}
            binding = stage(selected, replacement)
            child = os.fork()
            if child == 0:
                try:
                    save = files.save_receipt
                    def persist(*args):
                        if args[2].get('state') == 'Published': os.kill(os.getpid(), signal.SIGKILL)
                        return save(*args)
                    files.save_receipt = persist
                    publisher.publish(binding, session)
                except BaseException: os._exit(2)
                os._exit(3)
            deadline, status = time.monotonic() + 45, None
            while time.monotonic() < deadline:
                done, code = os.waitpid(child, os.WNOHANG)
                if done: status = code; break
                time.sleep(0.01)
            if status is None:
                os.kill(child, signal.SIGKILL); os.waitpid(child, 0)
                raise RuntimeError('owned overlay publication child exceeded deadline')
            assert os.WIFSIGNALED(status) and os.WTERMSIG(status) == signal.SIGKILL
            assert json.loads((state / (binding['operationId'] + '.json')).read_bytes())['state'] == 'PublishApplying'
            # New real PID/root/namespace session, not a mock or state-only
            # snapshot. Neither observing nor retrying may copy/rename again.
            exchange, copy = files.exchange, publisher.copy
            def forbidden(*_args, **_kwargs): raise AssertionError('recovery attempted another mutation')
            files.exchange, publisher.copy = forbidden, forbidden
            try:
                with reader.ContainerRoot(location, files, docker) as resumed:
                    result = publisher.publish(binding, resumed)
                    assert publisher.inspect(binding, resumed)['bindingCurrent']
            finally: files.exchange, publisher.copy = exchange, copy
            assert result['state'] == 'Published' and result['bindingCurrent'] and result['previousRetained']
            parent_path, _, leaf = selected.rpartition('/')
            parent = files.open_at(session.root, parent_path, os.O_RDONLY | os.O_DIRECTORY)
            try:
                private = files.open_at(parent, '.whaledeck-container-files-v1', os.O_RDONLY | os.O_DIRECTORY)
                try:
                    retained = files.current(private, binding['operationId'] + '.upload')
                    assert files.publication_identity(retained) == files.publication_identity(before)
                    assert files.checksum(private, binding['operationId'] + '.upload') == hashlib.sha256(original).hexdigest().upper()
                finally: os.close(private)
                assert files.checksum(parent, leaf) == hashlib.sha256(replacement).hexdigest().upper()
            finally: os.close(parent)
            hdd = target / 'container-files-v1' / (binding['operationId'] + '.data')
            assert hdd.read_bytes() == original
            body = hashlib.shake_256(b'native-overlay-new-file').digest(common.SIZE)
            created = stage('tmp/whaledeck-owned-validation-new.bin', body, absent=True)
            assert publisher.publish(created, session)['state'] == 'Published'
            descriptor = files.open_at(session.root, 'tmp/whaledeck-owned-validation-new.bin', os.O_RDONLY | os.O_NOFOLLOW)
            try:
                new_info = files.regular(descriptor)
                assert (new_info.st_uid, new_info.st_gid, stat.S_IMODE(new_info.st_mode)) == (1000, 1000, 0o600)
            finally: os.close(descriptor)
            tmp = files.open_at(session.root, 'tmp', os.O_RDONLY | os.O_DIRECTORY)
            try: assert files.checksum(tmp, 'whaledeck-owned-validation-new.bin') == hashlib.sha256(body).hexdigest().upper()
            finally: os.close(tmp)
            assert publisher.publish(binding, session)['bindingCurrent'] and publisher.inspect(created, session)['bindingCurrent']
            return binding, hashlib.sha256(original).hexdigest().upper(), len(original)
    finally:
        if identifier is not None:
            actual = json.loads(common.command(['docker', 'inspect', identifier]))[0]; verify(actual)
            common.command(['docker', 'rm', '--force', identifier])
            assert subprocess.run(['docker', 'inspect', identifier], capture_output=True, timeout=10).returncode != 0
        assert common.core_snapshot() == baseline
        print(json.dumps({'ownedOverlayContainerRemoved': identifier is not None, 'coreSnapshotUnchanged': True}), flush=True)


def main():
    if os.geteuid() != 0: raise RuntimeError('isolated Linux root required')
    host = Path(__file__).resolve().parents[1] / 'host'
    names = ('whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py',
        'whaledeck-container-upload.py', 'whaledeck-file-backups.py', 'whaledeck-container-backups.py', 'whaledeck-container-publish.py')
    frozen = {name: (host / name).read_bytes() for name in names}
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in frozen.items()}
    with tempfile.TemporaryDirectory(prefix='wdv-publish-bundle-', dir='/var/lib') as temporary:
        bundle = Path(temporary)
        for name, content in frozen.items():
            path = bundle / name; path.write_bytes(content); path.chmod(0o750)
        reader = load(bundle / names[0], 'native_publish_reader')
        files = reader.trusted_module(names[1], 'native_publish_files')
        docker = reader.trusted_module(names[2], 'native_publish_docker')
        uploads = load(bundle / names[3], 'native_publish_uploads')
        backups = load(bundle / names[4], 'native_publish_hdd')
        captures = load(bundle / names[5], 'native_publish_captures')
        publishing = load(bundle / names[6], 'native_publish_engine')
        production = backups.FileBackups(files); configuration = production.configuration()
        with production.target() as (_, parent):
            namespace = 'wdv-container-publish-' + uuid.uuid4().hex
            os.mkdir(namespace, 0o700, dir_fd=parent); os.fsync(parent)
            target = Path(configuration['mountPoint']) / configuration['targetRelative'] / namespace
            target_identity = target.stat().st_dev, target.stat().st_ino
        policy = bundle / 'policy.json'
        policy.write_text(json.dumps({**configuration, 'targetRelative': configuration['targetRelative'] + '/' + namespace})); policy.chmod(0o600)
        backups.CONFIGURATION = str(policy)
        state = bundle / 'state'; state.mkdir(mode=0o700)
        upload = uploads.ContainerUploads(reader, files, state)
        capture = captures.ContainerBackups(upload, backups.FileBackups(files, namespace='container-files-v1'))
        publisher = publishing.ContainerPublisher(upload, capture)
        operation, old_digest, binding, completed = None, None, None, False
        histories = {}
        try:
            with fixture() as owned:
                identifier, folder = owned['identifier'], owned['folder']
                actual = docker.api('GET', '/containers/' + identifier + '/json')
                target_info = docker.target(actual, identifier, docker.registry())
                location = {'containerId': identifier, 'instanceId': target_info['instanceId'], 'path': 'work'}
                with reader.ContainerRoot(location, files, docker) as session:
                    def stage(path, content, absent=False):
                        parent_path = path.rpartition('/')[0]
                        parent = reader.inspect({**location, 'path': parent_path}, session, files)
                        version = 'ABSENT' if absent else reader.inspect({**location, 'path': path}, session, files)['version']
                        request = {**location, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': 'B' * 64,
                            'expectedVersion': version, 'expectedParentVersion': parent['version'], 'sizeBytes': len(content),
                            'sha256': hashlib.sha256(content).hexdigest().upper()}
                        with tempfile.TemporaryFile() as stream:
                            import base64
                            for offset in range(0, len(content), reader.CHUNK):
                                stream.write((json.dumps({'type': 'chunk', 'offset': offset,
                                    'data': base64.b64encode(content[offset:offset + reader.CHUNK]).decode()}) + '\n').encode())
                            stream.write(b'{"type":"commit"}\n'); stream.seek(0)
                            assert upload.stage(request, session, files.InputLines(stream.fileno()))['state'] == 'Staged'
                        return {key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')}
                    replacement = hashlib.shake_256(b'native-container-published-overwrite').digest(common.SIZE)
                    old_info = (folder / 'original.bin').stat()
                    binding = stage('work/original.bin', replacement); operation = binding['operationId']
                    result = publisher.publish(binding, session)
                    assert result['state'] == 'Published' and result['bindingCurrent'] and result['previousRetained']
                    output = folder / 'original.bin'
                    assert output.read_bytes() == replacement and output.stat().st_ino != old_info.st_ino
                    assert (output.stat().st_uid, output.stat().st_gid, stat.S_IMODE(output.stat().st_mode)) == (1000, 1000, 0o640)
                    retained = folder / publishing.PRIVATE / (operation + '.upload')
                    assert retained.read_bytes() == owned['old'] and retained.stat().st_ino == old_info.st_ino
                    old_digest = hashlib.sha256(owned['old']).hexdigest().upper()
                    hdd_data = target / 'container-files-v1' / (operation + '.data')
                    assert hdd_data.read_bytes() == owned['old'] and common.file_digest(hdd_data) == old_digest
                    assert hdd_data.stat().st_dev == output.stat().st_dev != (state / (operation + '.data')).stat().st_dev
                    owned['proofs']['original.bin'] = common.fingerprint(output), hashlib.sha256(replacement).hexdigest().upper()
                    owned['private'] = publishing.PRIVATE
                    owned['privateProofs'][retained.name] = common.fingerprint(retained), old_digest
                    created_body = hashlib.shake_256(b'native-container-published-create').digest(common.SIZE)
                    created_binding = stage('work/new.bin', created_body, absent=True)
                    created_result = publisher.publish(created_binding, session)
                    created = folder / 'new.bin'
                    assert created_result['state'] == 'Published' and created_result['bindingCurrent'] and not created_result['previousRetained']
                    assert created_result['backupCapture'] is None and created.read_bytes() == created_body
                    assert (created.stat().st_uid, created.stat().st_gid, stat.S_IMODE(created.stat().st_mode)) == (1000, 1000, 0o600)
                    owned['proofs']['new.bin'] = common.fingerprint(created), hashlib.sha256(created_body).hexdigest().upper()
                    assert publisher.publish(binding, session)['bindingCurrent']
                    assert publisher.inspect(created_binding, session)['bindingCurrent']
                    readonly = stage('tmp/whaledeck-validation-new.bin', b'never publish', absent=True)
                    try: publisher.publish(readonly, session)
                    except reader.Rejected as error: assert str(error) == 'CONTAINER_UPLOAD_PUBLISH_READ_ONLY'
                    else: raise AssertionError('read-only root accepted')
                    assert publisher.discard(readonly, session)['state'] == 'Discarded'
                    try: publisher.publish({**binding, 'owner': 'C' * 64}, session)
                    except reader.Rejected: pass
                    else: raise AssertionError('foreign owner accepted')
                    docker.api('POST', '/containers/' + identifier + '/stop?t=1')
                    try: publisher.inspect(binding, session)
                    except reader.Rejected: pass
                    else: raise AssertionError('stopped instance accepted')
                histories[operation] = (binding, old_digest, common.SIZE)
            overlay_binding, overlay_digest, overlay_size = overlay_check(reader, files, docker, upload, publisher, state, target)
            histories[overlay_binding['operationId']] = (overlay_binding, overlay_digest, overlay_size)
            completed = True
            assert all((host / name).read_bytes() == content for name, content in frozen.items())
            print(json.dumps({'nativeContainerPublication': 'passed', 'overwriteAndCreateFullBytesEach': common.SIZE,
                'spoolDifferentDevice': True, 'oldInodeAndHddFullContentRetained': True, 'kernelOwner1000AndModesPreserved': True,
                'readonlyRootForeignOwnerStoppedInstanceAndReplay': 'passed', 'installedHelperRpcJobsOrUi': False,
                'overlayLowerImageOverwriteAndFullSizeCreate': 'passed',
                'actualOverlaySigkillAndFreshSessionReconciliationWithoutMutation': 'passed',
                'sourceSha256': hashes}), flush=True)
        finally:
            assert target.resolve(strict=True) == target and (target.stat().st_dev, target.stat().st_ino) == target_identity
            directory = target / 'container-files-v1'
            if completed and directory.is_dir() and not directory.is_symlink():
                assert {entry.name for entry in directory.iterdir()} == {operation + suffix for operation in histories for suffix in ('.data', '.json')}
                for operation, (binding, digest, size) in histories.items():
                    data = directory / (operation + '.data'); info = data.lstat()
                    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                    assert info.st_size == size and common.file_digest(data) == digest
                    metadata = directory / (operation + '.json'); info = metadata.lstat()
                    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                    saved = json.loads(metadata.read_bytes())
                    assert all(saved[key] == binding[key] for key in ('operationId', 'owner', 'containerId', 'instanceId'))
                    assert saved['state'] == 'Verified' and saved['resourceType'] == 'ContainerFile' and saved['sha256'] == digest
                    data.unlink(); metadata.unlink()
                directory.rmdir()
            removed = not any(target.iterdir())
            if removed: target.rmdir()
            print(json.dumps({'ownedPublishHddRemoved': removed, 'unverifiedHistoryPreserved': not removed}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'nativeContainerPublication': 'failed', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
