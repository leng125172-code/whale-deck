#!/usr/bin/env python3
"""Frozen-source native read probe, optionally including the installed helper.

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
import re
import select
import stat
import subprocess
import sys
import tempfile
import time
import uuid

if os.name == 'posix':
    import pwd

REPOSITORY = Path('/data/GitRepos/whale-deck')
APPS = Path('/data/WhaleDeck/apps')
SIZE = 2 * 1024**2 + 7
PHASE = 'startup'
HELPER = '/usr/local/libexec/whaledeck-privileged'


def assert_helper_sandbox(properties, invocation):
    assert properties.get('InvocationID') == invocation and re.fullmatch('[a-f0-9]{32}', invocation)
    for name, expected in {'User': 'root', 'Group': 'root', 'NoNewPrivileges': 'yes', 'ProtectSystem': 'strict',
            'ProtectHome': 'yes', 'PrivateTmp': 'yes', 'PrivateDevices': 'yes', 'ProtectKernelTunables': 'yes',
            'ProtectKernelModules': 'yes', 'ProtectControlGroups': 'yes', 'KillMode': 'control-group',
            'MemoryMax': str(128*1024**2), 'TasksMax': '8', 'LimitNOFILE': '64',
            'LimitFSIZE': str(1024**3), 'ReadWritePaths': '/var/lib/whaledeck-container-files'}.items():
        assert properties.get(name) == expected, 'sandbox-property-' + name
    assert set(properties.get('CapabilityBoundingSet', '').split()) == {'cap_dac_read_search', 'cap_sys_ptrace'}
    assert set(properties.get('RestrictAddressFamilies', '').split()) == {'AF_UNIX'}


def installed_source(revision):
    for name in ('whaledeck-privileged', 'whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py'):
        path = Path('/usr/local/libexec') / name; info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and not info.st_mode & 0o022
        assert path.read_bytes() == git('show', revision + ':deploy/host/' + name)
    state = Path('/var/lib/whaledeck-container-files'); info = state.lstat()
    assert state.resolve(strict=True) == state and stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700


def helper_ready(stream):
    # Read raw bytes, not a blocking buffered readline after select: a partial
    # first line must still obey the same total Ready deadline.
    deadline = time.monotonic() + 10; body = bytearray()
    while len(body) <= 65536:
        remaining = deadline - time.monotonic()
        assert remaining > 0 and select.select([stream], [], [], remaining)[0], 'helper-ready-timeout'
        value = os.read(stream.fileno(), 1)
        assert value, 'helper-ready-eof'
        if value == b'\n':
            opening = json.loads(body)
            assert opening.get('type') == 'driver', 'helper-ready-rejected'
            invocation = opening['invocationId']
            assert type(invocation) is str and re.fullmatch('[a-f0-9]{32}', invocation), 'helper-ready-invalid'
            return invocation
        body.extend(value)
    raise AssertionError('helper-ready-size')


def close_helper(process, command, session, invocation):
    stop_failed = False
    try:
        if process.poll() is None:
            try:
                if invocation is not None:
                    stopped = subprocess.run([*command, 'container-file-transport-stop', session, invocation], capture_output=True, timeout=10)
                    stop_failed = stopped.returncode != 0
                else:
                    # No target body is sent before Ready. Closing the caller
                    # gives the fixed root driver's input alarm a bounded exit.
                    if process.stdin: process.stdin.close()
            except (OSError, subprocess.TimeoutExpired):
                stop_failed = True
            finally:
                try: process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    stop_failed = True
                    process.kill(); process.wait(timeout=5)
    finally:
        for stream in (process.stdin, process.stdout, process.stderr):
            if stream is not None and not stream.closed: stream.close()
    assert not stop_failed, 'helper-cleanup-failed'


def helper_request(action, request):
    assert action in ('list', 'stat', 'download')
    session = uuid.uuid4().hex; unit = 'whaledeck-container-files-' + session + '.service'
    command = ['/usr/sbin/runuser', '-u', 'whaledeck-agent', '--', '/usr/bin/sudo', '-n', HELPER]
    process = subprocess.Popen([*command, 'container-file-transport', action, session], stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'LANG': 'C.UTF-8'})
    invocation = None
    try:
        invocation = helper_ready(process.stdout)
        names = ('InvocationID', 'User', 'Group', 'NoNewPrivileges', 'ProtectSystem', 'ProtectHome', 'PrivateTmp', 'PrivateDevices',
            'ProtectKernelTunables', 'ProtectKernelModules', 'ProtectControlGroups', 'KillMode', 'MemoryMax', 'TasksMax',
            'LimitNOFILE', 'LimitFSIZE', 'ReadWritePaths', 'CapabilityBoundingSet', 'RestrictAddressFamilies')
        show = subprocess.run(['/usr/bin/systemctl', 'show', unit, *['--property='+name for name in names]],
            check=True, capture_output=True, text=True, timeout=5)
        properties = dict(line.split('=', 1) for line in show.stdout.splitlines() if '=' in line)
        assert_helper_sandbox(properties, invocation)
        output, errors_output = process.communicate((json.dumps(request, ensure_ascii=False)+'\n').encode(), timeout=40)
        assert len(output) <= 4*1024**2 and len(errors_output) <= 65536
        packets = [json.loads(line) for line in output.splitlines()]
        errors = [row.get('code') for row in packets if row.get('type') == 'error']
        if process.returncode or errors:
            code = errors[-1] if errors and type(errors[-1]) is str and re.fullmatch('(?:CONTAINER_FILES|FILES|CONTAINER)_[A-Z_]{1,80}', errors[-1]) else 'CONTAINER_FILES_HELPER_FAILED'
            raise RuntimeError(code)
        journal = subprocess.run(['/usr/bin/journalctl', '--unit='+unit, '--no-pager', '--output=cat', '--lines=50'],
            check=True, capture_output=True, text=True, timeout=5).stdout
        assert len(journal.encode()) <= 65536 and not re.search(r'"type"\s*:\s*"(?:chunk|metadata|driver)"', journal)
        return packets
    finally:
        close_helper(process, command, session, invocation)


def agent_probe(path, fixture):
    # A published validator, not an arbitrary caller-supplied executable.
    assert path.name == 'TerminalProbe' and path.parent.parent == REPOSITORY / 'deploy/artifacts'
    assert re.fullmatch(r'terminal-probe\.[A-Za-z0-9]{6}', path.parent.name)
    assert path.resolve(strict=True) == path
    for target in (path.parent, path, path.parent / 'SHA256SUMS'):
        info = target.lstat()
        assert info.st_uid == 0 and not info.st_mode & 0o022
        assert stat.S_ISDIR(info.st_mode) if target == path.parent else stat.S_ISREG(info.st_mode) and info.st_nlink == 1
    subprocess.run(['/usr/bin/sha256sum', '--check', '--strict', 'SHA256SUMS'], cwd=path.parent,
        check=True, capture_output=True, timeout=15)
    result = subprocess.run([str(path), 'container-files'], input=json.dumps(fixture), text=True,
        capture_output=True, timeout=160, env={'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8'})
    assert len(result.stdout.encode()) <= 16384 and len(result.stderr.encode()) <= 16384
    if result.returncode:
        # Only the probe's fixed structured result is retained in private QA
        # evidence; no exception, capabilities, key or binary content is printed.
        try: failure = json.loads(result.stderr)
        except (ValueError, TypeError): failure = {}
        phase = failure.get('phase')
        code = failure.get('rpcStatus')
        assert False, 'agent-probe-' + (phase if type(phase) is str and re.fullmatch('[a-z-]{1,80}', phase) else 'failed') + '-' + (code if type(code) is str and re.fullmatch('[A-Za-z]{1,40}', code) else 'unknown')
    outcome = json.loads(result.stdout)
    assert outcome['installedContainerFileRpc'] == 'passed' and outcome['revision'] == fixture['revision'] and outcome['bytes'] == SIZE
    for name in ('fullByteDigest', 'fourMethods', 'methodAndServiceBinding', 'replayRefused', 'coreSecretSymlinkOldVersionAndInstanceRefused'):
        assert outcome.get(name) is True
    assert outcome.get('productionMutation') is False
    return outcome


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
    assert name in ('baseline.json', 'result.json', 'rpc.json')
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
    assert os.geteuid() == 0 and len(sys.argv) in (2, 3, 4) and re.fullmatch('sha256:[a-f0-9]{64}', sys.argv[1])
    installed = len(sys.argv) >= 3
    if installed: assert sys.argv[2] == '--installed-helper'
    probe_path = None
    if len(sys.argv) == 4:
        assert sys.argv[3].startswith('--agent-probe=')
        probe_path = Path(sys.argv[3].removeprefix('--agent-probe='))
    revision = git('rev-parse', 'HEAD').decode().strip(); assert re.fullmatch('[a-f0-9]{40}', revision)
    assert not git('status', '--porcelain')
    if installed: installed_source(revision)
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
                if installed:
                    PHASE = 'installed-helper-sandbox-and-content'
                    page_packets = helper_request('list', {**request, 'after': '', 'take': 100})
                    assert len(page_packets) == 1 and {row['name'] for row in page_packets[0]['entries']} == {leaf.name, '.env', 'empty', 'link'}
                    inspected_packets = helper_request('stat', location)
                    assert len(inspected_packets) == 1 and inspected_packets[0]['version'] == meta['version']
                    downloaded = helper_request('download', {**location, 'expectedVersion': meta['version']})
                    assert downloaded[0]['type'] == 'metadata' and downloaded[-1]['type'] == 'end'
                    delivered = bytearray()
                    for packet in downloaded[1:-1]:
                        assert packet['type'] == 'chunk' and packet['offset'] == len(delivered)
                        delivered.extend(base64.b64decode(packet['data'], validate=True))
                    assert bytes(delivered) == payload and downloaded[-1]['sha256'] == hashlib.sha256(payload).hexdigest().upper()
                    assert downloaded[-1]['version'] == meta['version'] and downloaded[-1]['sizeBytes'] == SIZE
                    assert engine.fingerprint(leaf.stat()) == before
                if probe_path is not None:
                    PHASE = 'installed-agent-uds'
                    core = docker.api('GET', '/containers/whaledeck-api/json')
                    rpc = agent_probe(probe_path, {'revision': revision, 'containerId': identifier, 'coreId': core['Id'],
                        'instanceId': target['instanceId'], 'version': meta['version'], 'sizeBytes': SIZE,
                        'sha256': hashlib.sha256(payload).hexdigest().upper()})
                    private_record(evidence, 'rpc.json', rpc)
                    assert engine.fingerprint(leaf.stat()) == before and leaf.read_bytes() == payload
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
                'pidExitRefused': True, 'coreProtected': True, 'spoolEmpty': True, 'protectedContainersUnchanged': True,
                'installedHelperValidated': installed, 'installedAgentRpcValidated': probe_path is not None})
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
        'ownedContainerAndPayloadRemoved': complete, 'installedHelperValidated': installed,
        'installedAgentRpcValidated': probe_path is not None, 'businessBffOrUiValidated': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        code = str(error) if re.fullmatch(r'(?:CONTAINER_FILES|FILES|CONTAINER)_[A-Z_]{1,80}', str(error)) else None
        diagnostic = str(error) if re.fullmatch(r'agent-probe-[a-z-]{1,80}-[A-Za-z]{1,40}', str(error)) else None
        print(json.dumps({'containerFileNativeRead': 'failed', 'failureType': type(error).__name__,
            'phase': PHASE, 'code': code, 'probeFailure': diagnostic, 'unverifiedFilesPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
