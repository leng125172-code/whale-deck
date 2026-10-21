#!/usr/bin/env python3
"""Frozen-source native container read probe. Not an installed helper/RPC/BFF test.

Creates one private synthetic application directory and one immutable-image,
non-root/no-network/read-only container. No production mounts or image builds.
Only proven owned test objects are cleaned; failure retains unverified files.
"""
import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import pwd
import re
import stat
import subprocess
import sys
import tempfile
import uuid

REPOSITORY = Path('/data/GitRepos/whale-deck')
APPS = Path('/data/WhaleDeck/apps')
SIZE = 2 * 1024**2 + 7
PHASE = 'startup'


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module; spec.loader.exec_module(module)
    return module


def git(*arguments):
    return subprocess.run(['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY), *arguments], check=True, capture_output=True).stdout


def private_record(folder, name, value):
    assert folder.parent == REPOSITORY / 'deploy/artifacts'
    assert re.fullmatch(r'container-files-native-evidence-[a-f0-9]{16}', folder.name)
    assert folder.resolve() == folder and folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    assert name in ('baseline.json', 'result.json')
    descriptor = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as stream:
        stream.write(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()); stream.flush(); os.fsync(stream.fileno())


def core_snapshot(engine, docker):
    resources = docker.registry()
    rows = docker.api('GET', '/containers/json?all=1')
    assert type(rows) is list and 0 < len(rows) <= 1000
    result = {row['Id']: (row['ImageID'], row['State']) for row in rows if engine.is_core(row, resources)}
    assert result and all(re.fullmatch('[a-f0-9]{64}', identifier) for identifier in result)
    return result


def verify_apps_directory(path, agent_uid):
    assert path == APPS and path.resolve(strict=True) == path and path.is_dir()
    info = path.lstat()
    # The production application root is owned by the trusted Docker-capable
    # Agent account; do not chown a live application directory for a probe.
    assert info.st_uid in (0, agent_uid) and not info.st_mode & 0o022


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('sha256:[a-f0-9]{64}', sys.argv[1])
    revision = git('rev-parse', 'HEAD').decode().strip(); assert re.fullmatch('[a-f0-9]{40}', revision)
    assert not git('status', '--porcelain')
    verify_apps_directory(APPS, pwd.getpwnam('whaledeck-agent').pw_uid)
    capacity = os.statvfs(APPS)
    assert capacity.f_bavail * capacity.f_frsize > SIZE + max(128 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20)
    os.umask(0o077)
    marker = 'wdv-container-files-' + uuid.uuid4().hex[:16]
    folder = APPS / marker; assert not os.path.lexists(folder); folder.mkdir(mode=0o750); folder.chmod(0o750)
    evidence = REPOSITORY / 'deploy/artifacts' / ('container-files-native-evidence-' + marker[-16:])
    assert not evidence.exists(); evidence.mkdir(mode=0o700)
    folder_identity = folder.stat().st_dev, folder.stat().st_ino
    payload = hashlib.shake_256(('container-file-native:' + marker).encode()).digest(SIZE)
    leaf = folder / '中文.bin'; leaf.write_bytes(payload); leaf.chmod(0o640)
    secret = folder / '.env'; secret.write_bytes(b'NON_SECRET_SYNTHETIC_QA_VALUE=not-a-real-credential\n'); secret.chmod(0o600)
    empty = folder / 'empty'; empty.mkdir(mode=0o750); empty.chmod(0o750)
    link = folder / 'link'; link.symlink_to('/etc')
    assert set(folder.iterdir()) == {leaf, secret, empty, link}
    identifier = None; complete = False
    with tempfile.TemporaryDirectory(prefix='wd-native-container-files-') as temporary:
        bundle = Path(temporary)
        for name in ('whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py'):
            path = bundle / name; raw = git('show', revision + ':deploy/host/' + name)
            path.write_bytes(raw); path.chmod(0o750)
        engine = load(bundle / 'whaledeck-container-files.py', 'native_container_files_engine')
        files = engine.trusted_module('whaledeck-files.py', 'native_container_file_primitives')
        docker = engine.trusted_module('whaledeck-container-terminal.py', 'native_container_docker')
        spool = bundle / 'spool'; spool.mkdir(mode=0o700); engine.STATE = spool
        before = engine.fingerprint(leaf.stat())
        other_proofs = {path.name: engine.fingerprint(path.lstat()) for path in (secret, empty, link)}
        protected_before = core_snapshot(engine, docker)
        PHASE = 'image-and-baseline'
        image = docker.api('GET', '/images/' + sys.argv[1] + '/json')
        assert image['Id'] == sys.argv[1] and not (image.get('Config') or {}).get('Volumes')
        private_record(evidence, 'baseline.json', {'revision': revision, 'marker': marker,
            'folderIdentity': folder_identity, 'image': image['Id'], 'source': before,
            'bytes': SIZE, 'sha256': hashlib.sha256(payload).hexdigest().upper(), 'otherProofs': other_proofs,
            'protectedContainers': protected_before})
        try:
            PHASE = 'create-owned-container'
            configuration = {'Image': image['Id'], 'Entrypoint': ['/bin/sleep'], 'Cmd': ['600'], 'User': '1000:1000',
                'Labels': {'io.whaledeck.validation': marker}, 'HostConfig': {'NetworkMode': 'none',
                    'Memory': 32 * 1024**2, 'NanoCpus': 250000000, 'PidsLimit': 16,
                    'ReadonlyRootfs': True, 'CapDrop': ['ALL'], 'SecurityOpt': ['no-new-privileges'],
                    'LogConfig': {'Type': 'local', 'Config': {'max-size': '1m', 'max-file': '2'}},
                    'Binds': [str(folder) + ':/work:ro']}}
            created = docker.api('POST', '/containers/create?name=' + marker, configuration)
            identifier = created['Id']; assert re.fullmatch('[a-f0-9]{64}', identifier)
            docker.api('POST', '/containers/' + identifier + '/start')
            inspected = docker.api('GET', '/containers/' + identifier + '/json')
            target = docker.target(inspected, identifier, docker.registry())
            assert inspected['Config']['Labels'].get('io.whaledeck.validation') == marker
            assert len(inspected['Mounts']) == 1 and inspected['Mounts'][0]['RW'] is False
            request = {'containerId': identifier, 'instanceId': target['instanceId'], 'path': 'work'}
            PHASE = 'pinned-container-root'
            with engine.ContainerRoot(request, files, docker) as session:
                PHASE = 'list-stat-download'
                page = engine.listing({**request, 'after': '', 'take': 100}, session, files)
                assert {row['name'] for row in page['entries']} == {leaf.name, '.env', 'empty', 'link'}
                assert next(row for row in page['entries'] if row['name'] == '.env')['isProtected']
                assert next(row for row in page['entries'] if row['name'] == 'link')['isProtected']
                location = {**request, 'path': 'work/' + leaf.name}
                meta = engine.inspect(location, session, files); assert meta['sizeBytes'] == SIZE
                output = io.BytesIO(); engine.download({**location, 'expectedVersion': meta['version']}, session, files, output)
                packets = [json.loads(raw) for raw in output.getvalue().splitlines()]
                assert packets[0]['type'] == 'metadata' and packets[-1]['type'] == 'end'
                captured = bytearray()
                for row in packets[1:-1]:
                    assert row['type'] == 'chunk' and row['offset'] == len(captured)
                    captured.extend(base64.b64decode(row['data'], validate=True))
                assert bytes(captured) == payload and packets[-1]['sizeBytes'] == SIZE
                assert packets[-1]['sha256'] == hashlib.sha256(payload).hexdigest().upper()
                assert engine.fingerprint(leaf.stat()) == before and not list(spool.iterdir())
                PHASE = 'protected-symlink-version-refusals'
                for path in ('work/.env', 'proc/self/status', 'etc/shadow'):
                    try: engine.inspect({**request, 'path': path}, session, files)
                    except engine.Rejected: pass
                    else: raise AssertionError('protected path accepted')
                try: engine.inspect({**request, 'path': 'work/link/passwd'}, session, files)
                except OSError: pass
                else: raise AssertionError('symlink accepted')
                bad_output = io.BytesIO()
                try: engine.download({**location, 'expectedVersion': 'F' * 64}, session, files, bad_output)
                except engine.Rejected as error: assert str(error) == 'CONTAINER_FILES_VERSION_CONFLICT'
                else: raise AssertionError('stale version accepted')
                assert not bad_output.getvalue()
                PHASE = 'owned-process-exit'
                docker.api('POST', '/containers/' + identifier + '/stop?t=1')
                try: session.current()
                except engine.Rejected as error: assert str(error) == 'CONTAINER_FILES_INSTANCE_CHANGED'
                else: raise AssertionError('exited target accepted')
            core = docker.api('GET', '/containers/whaledeck-api/json')
            PHASE = 'protected-core-refusal'
            core_instance = str(core['State']['Pid']) + ':' + core['State']['StartedAt']
            try:
                with engine.ContainerRoot({'containerId': core['Id'], 'instanceId': core_instance, 'path': ''}, files, docker): pass
            except PermissionError as error: assert str(error) == 'CONTAINER_PROTECTED'
            else: raise AssertionError('core container accepted')
            assert core_snapshot(engine, docker) == protected_before
            private_record(evidence, 'result.json', {'revision': revision, 'marker': marker, 'containerId': identifier,
                'bytes': SIZE, 'sha256': hashlib.sha256(payload).hexdigest().upper(), 'fullContent': True,
                'sourceUnchanged': True, 'protectedAndSymlinkAndOldVersionRefused': True,
                'pidExitRefused': True, 'coreProtected': True, 'spoolEmpty': True, 'protectedContainersUnchanged': True})
            complete = True
        finally:
            if identifier:
                PHASE = 'owned-container-cleanup' if complete else 'cleanup-after-' + PHASE
                owned = docker.api('GET', '/containers/' + identifier + '/json')
                assert owned['Id'] == identifier and owned['Name'] == '/' + marker and owned['Image'] == image['Id']
                assert owned['Config']['Labels'].get('io.whaledeck.validation') == marker
                assert owned['HostConfig']['Binds'] == [str(folder) + ':/work:ro']
                assert len(owned['Mounts']) == 1 and owned['Mounts'][0]['RW'] is False
                docker.api('DELETE', '/containers/' + identifier + '?force=1&v=0')
                try: docker.api('GET', '/containers/' + identifier + '/json')
                except docker.DockerFailure as error: assert error.status == 404
                else: raise AssertionError('owned test container still exists')
        if complete:
            PHASE = 'proven-payload-cleanup'
            assert folder.resolve() == folder and (folder.stat().st_dev, folder.stat().st_ino) == folder_identity
            assert set(folder.iterdir()) == {leaf, secret, empty, link}
            assert engine.fingerprint(leaf.stat()) == before and leaf.read_bytes() == payload
            assert {path.name: engine.fingerprint(path.lstat()) for path in (secret, empty, link)} == other_proofs
            assert secret.read_bytes() == b'NON_SECRET_SYNTHETIC_QA_VALUE=not-a-real-credential\n'
            assert stat.S_ISLNK(link.lstat().st_mode) and os.readlink(link) == '/etc' and not list(empty.iterdir())
            leaf.unlink(); secret.unlink(); link.unlink(); empty.rmdir(); folder.rmdir()
            assert not os.path.lexists(folder)
            assert core_snapshot(engine, docker) == protected_before
    print(json.dumps({'containerFileNativeRead': 'passed', 'revision': revision, 'bytes': SIZE,
        'fullContentAndSourceIdentity': True, 'exitProtectedAndSymlinkRefused': True,
        'ownedContainerAndPayloadRemoved': complete, 'installedRpcBffOrUi': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'containerFileNativeRead': 'failed', 'failureType': type(error).__name__,
            'phase': PHASE, 'unverifiedFilesPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
