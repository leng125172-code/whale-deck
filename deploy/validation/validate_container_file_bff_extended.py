#!/usr/bin/env python3
"""Real Cookie/BFF/SQL/UDS acceptance with exclusively owned synthetic files.

Run with run_with_ephemeral_admin.py and the expected installed runtime SHA.
No production writes, permission-cache edits, Docker Exec, or fault injection.
"""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import socket
import stat
import subprocess
import sys
import time
from urllib.parse import urlsplit
import uuid

from validate_container_file_reads import verify_apps_directory

if os.name == 'posix':
    import pwd

APPS = Path('/data/WhaleDeck/apps')
IMAGE = 'sha256:884e0b7f42f303641b0f4e78755aea647521dca0b89ca3be7992ad78b5c07434'
SIZE = 2 * 1024**2 + 7
CANCEL_SIZE = 64 * 1024**2 + 7
PHASE = 'startup'
CORE_NAMES = ('whaledeck-api', 'whaledeck-worker', 'whaledeck-gateway',
    'database-platform-postgres', 'database-platform-mariadb', 'database-platform-mongodb',
    'database-platform-sqlserver', 'database-platform-valkey', 'database-platform-valkey72',
    'database-platform-authentik-server', 'database-platform-authentik-worker')


def command(arguments):
    return subprocess.run(arguments, check=True, capture_output=True, timeout=30).stdout


def fingerprint(path):
    value = path.lstat()
    return (value.st_dev, value.st_ino, value.st_mode, value.st_uid, value.st_gid,
            value.st_nlink, value.st_size, value.st_mtime_ns, value.st_ctime_ns)


def file_digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def verify_payload(folder, folder_identity, proofs):
    assert folder.parent == APPS and re.fullmatch('wdv-container-bff-large-[a-f0-9]{16}', folder.name)
    info = folder.lstat()
    assert stat.S_ISDIR(info.st_mode) and folder.resolve(strict=True) == folder
    assert (info.st_dev, info.st_ino) == folder_identity and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o750
    assert {entry.name for entry in folder.iterdir()} == set(proofs)
    for name, (identity, digest) in proofs.items():
        leaf = folder / name
        assert leaf.parent == folder and leaf.name == name and name in ('中文.bin', 'cancel.bin')
        assert fingerprint(leaf) == identity and stat.S_ISREG(leaf.lstat().st_mode) and leaf.lstat().st_nlink == 1
        assert file_digest(leaf) == digest


def verify_container(value, identifier, name, folder):
    assert re.fullmatch('[a-f0-9]{64}', identifier)
    assert value['Id'] == identifier and value['Name'] == '/' + name and value['Image'] == IMAGE
    assert value['Config']['Labels'].get('io.whaledeck.validation') == name
    assert value['HostConfig']['Binds'] == [str(folder) + ':/work:ro']
    assert value['HostConfig']['NetworkMode'] == 'none' and value['HostConfig']['ReadonlyRootfs'] is True
    mounts = value['Mounts']
    assert len(mounts) == 1 and mounts[0]['Type'] == 'bind' and mounts[0]['Source'] == str(folder)
    assert mounts[0]['Destination'] == '/work' and mounts[0]['RW'] is False


def core_snapshot():
    values = json.loads(command(['docker', 'inspect', *CORE_NAMES]))
    assert {value['Name'].lstrip('/') for value in values} == set(CORE_NAMES)
    return {value['Name']: (value['Id'], value['Image'], value['State']['Status'], value['State']['StartedAt']) for value in values}


@contextlib.contextmanager
def fixture(lifetime_seconds=600):
    assert os.geteuid() == 0
    assert lifetime_seconds in (600, 1200)
    verify_apps_directory(APPS, pwd.getpwnam('whaledeck-agent').pw_uid)
    capacity = os.statvfs(APPS)
    assert capacity.f_bavail * capacity.f_frsize > SIZE + CANCEL_SIZE + max(128 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20)
    image = json.loads(command(['docker', 'image', 'inspect', IMAGE]))[0]
    assert image['Id'] == IMAGE and not image['Config'].get('Volumes')
    baseline = core_snapshot()
    name = 'wdv-container-bff-large-' + uuid.uuid4().hex[:16]
    folder = APPS / name
    os.umask(0o077)
    folder.mkdir(mode=0o750); folder.chmod(0o750)
    folder_identity = folder.stat().st_dev, folder.stat().st_ino
    proofs = {}
    identifier = None
    complete = False
    try:
        block = hashlib.shake_256(('bff-fixture:' + name).encode()).digest(65536)
        for leaf_name, size in (('中文.bin', SIZE), ('cancel.bin', CANCEL_SIZE)):
            leaf = folder / leaf_name
            descriptor = os.open(leaf, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o640)
            with os.fdopen(descriptor, 'wb') as stream:
                remaining = size
                while remaining:
                    chunk = block[:min(len(block), remaining)]
                    stream.write(chunk); remaining -= len(chunk)
                stream.flush(); os.fsync(stream.fileno())
            leaf.chmod(0o640)
            proofs[leaf_name] = fingerprint(leaf), file_digest(leaf)
        verify_payload(folder, folder_identity, proofs)
        identifier = command(['docker', 'run', '--detach', '--name', name, '--network', 'none', '--read-only',
            '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', '1000:1000', '--pids-limit', '16',
            '--memory', '32m', '--cpus', '0.25', '--log-driver', 'local', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2',
            '--label', 'io.whaledeck.validation=' + name, '--mount', 'type=bind,src=' + str(folder) + ',dst=/work,readonly',
            '--entrypoint', '/bin/sleep', IMAGE, str(lifetime_seconds)]).decode().strip()
        # Docker --mount is represented in HostConfig.Mounts, not Binds. Keep
        # one canonical representation in the fixture and its cleanup proof.
        owned = json.loads(command(['docker', 'inspect', identifier]))[0]
        verify_mount_container(owned, identifier, name, folder)
        yield {'identifier': identifier, 'folder': folder, 'proofs': proofs}
        verify_payload(folder, folder_identity, proofs)
        assert core_snapshot() == baseline
        complete = True
    finally:
        lookup = subprocess.run(['docker', 'inspect', identifier or name], capture_output=True, timeout=20)
        if lookup.returncode == 0:
            owned = json.loads(lookup.stdout)[0]
            exact_id = owned['Id']
            if identifier is not None: assert exact_id == identifier
            verify_mount_container(owned, exact_id, name, folder)
            command(['docker', 'rm', '--force', exact_id])
            assert subprocess.run(['docker', 'inspect', exact_id], capture_output=True, timeout=10).returncode != 0
        else:
            assert identifier is None, 'owned-container-disappeared'
        cleanup_verified = False
        try:
            verify_payload(folder, folder_identity, proofs)
            assert core_snapshot() == baseline
            cleanup_verified = True
        except (AssertionError, OSError, subprocess.SubprocessError):
            # A body/observer failure is not itself proof of changed files.
            # Clean only when all original independent proofs still hold;
            # otherwise preserve the unverified payload for inspection.
            pass
        if cleanup_verified:
            for leaf_name in proofs: (folder / leaf_name).unlink()
            folder.rmdir()
            assert not os.path.lexists(folder) and core_snapshot() == baseline
        print(json.dumps({'largeFixtureContainerRemoved': True, 'fixtureBodyCompleted': complete,
            'verifiedPayloadRemoved': cleanup_verified, 'unverifiedPayloadPreserved': not cleanup_verified}), flush=True)


def verify_mount_container(value, identifier, name, folder):
    # Normalize only the two Docker configuration encodings, never an arbitrary
    # mount. The observed effective source/destination/RW must still match.
    host = value['HostConfig']
    assert not host.get('Binds')
    mounts = host.get('Mounts')
    assert type(mounts) is list and len(mounts) == 1
    mount = mounts[0]
    assert mount['Type'] == 'bind' and mount['Source'] == str(folder) and mount['Target'] == '/work' and mount['ReadOnly'] is True
    normalized = {**value, 'HostConfig': {**host, 'Binds': [str(folder) + ':/work:ro']}}
    verify_container(normalized, identifier, name, folder)


def quiet_login(base, username, password, administrator=True):
    from validate_oidc import authenticate
    with contextlib.redirect_stdout(io.StringIO()):
        return authenticate(base, username, password, require_administrator=administrator)


def download_verified(session, url, location, size, digest):
    with session.get(url, params=location, allow_redirects=False, stream=True, timeout=(10, 45)) as response:
        assert response.status_code == 200 and response.headers['Content-Length'] == str(size)
        assert 'no-store' in response.headers.get('Cache-Control', '') and response.headers.get('X-Content-Type-Options') == 'nosniff'
        assert 'attachment' in response.headers.get('Content-Disposition', '')
        count = 0; captured = hashlib.sha256()
        for chunk in response.iter_content(32768):
            count += len(chunk); assert count <= size; captured.update(chunk)
        assert count == size and captured.hexdigest().upper() == digest


def interrupted_download(url, headers, size):
    parsed = urlsplit(url)
    assert parsed.scheme == 'http' and parsed.hostname in ('127.0.0.1', '192.168.100.13', '192.168.22.19') and not parsed.username
    # Small receive window gives a genuine backpressured incomplete response;
    # do not use a 2MiB file that may finish entirely in loopback buffers.
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as wire:
        wire.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1024)
        wire.settimeout(45); wire.connect((parsed.hostname, parsed.port or 80))
        headers = dict(headers); headers['Host'] = parsed.netloc; headers['Connection'] = 'close'
        packet = 'GET ' + parsed.path + ('?' + parsed.query if parsed.query else '') + ' HTTP/1.1\r\n'
        packet += ''.join(key + ': ' + value + '\r\n' for key, value in headers.items()) + '\r\n'
        wire.sendall(packet.encode('ascii'))
        received = bytearray()
        while b'\r\n\r\n' not in received:
            chunk = wire.recv(2048); assert chunk; received.extend(chunk); assert len(received) <= 16384
        header, body = bytes(received).split(b'\r\n\r\n', 1)
        lines = header.decode('iso-8859-1').split('\r\n')
        assert re.fullmatch(r'HTTP/1\.[01] 200(?: .*)?', lines[0])
        values = {key.lower(): value.strip() for key, value in (line.split(':', 1) for line in lines[1:])}
        assert values['content-length'] == str(size)
        if not body: body = wire.recv(2048)
        assert 0 < len(body) < size
        wire.shutdown(socket.SHUT_RDWR)


def audits(session, base, subject, identifier):
    from validate_metric_continuity import query
    response = session.get(base + '/api/v1/audit', params={'actorSubject': subject, 'take': 100}, timeout=20)
    assert response.status_code == 200
    public = [row for row in response.json() if row['targetId'] == identifier and row['action'] == 'containers.files.download']
    # The administrator audit DTO deliberately excludes DetailJson. Read exact
    # owned event details independently through local PostgreSQL; never expand
    # the product API or silently treat a missing field as an empty object.
    details = query('''SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT id::text AS id,result,detail_json AS detail FROM audit_events
        WHERE actor_subject=:'subject' AND target_id=:'target'
          AND action='containers.files.download' ORDER BY occurred_at_utc,id LIMIT 100
        ) q;''', subject=subject, target=identifier)
    actual = {row['id']: row for row in details}
    assert len(actual) == len(details) == len(public)
    for row in public:
        assert row['id'] in actual and actual[row['id']]['result'] == row['result']
        row['detailJson'] = json.dumps(actual[row['id']]['detail'])
    return public


def wait_incomplete(session, base, subject, identifier, version):
    deadline = time.monotonic() + 20
    while True:
        rows = [row for row in audits(session, base, subject, identifier) if json.loads(row.get('detailJson') or '{}').get('version') == version]
        assert not any(row['result'] == 'Validated' for row in rows), 'interrupted-download-recorded-success'
        if any(row['result'] == 'Incomplete' for row in rows): return
        assert time.monotonic() < deadline, 'interrupted-download-not-finalized'
        time.sleep(0.5)


def main():
    global PHASE
    import requests
    from validate_databases import Api
    from validate_oidc import required_environment
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('[a-f0-9]{40}', sys.argv[1])
    revision = sys.argv[1]
    assert json.loads(command(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == revision
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, profile = quiet_login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    with fixture() as owned:
        identifier = owned['identifier']; endpoint = base + '/api/v1/containers/' + identifier + '/files/'
        def read(action, params=None, status=200, client=session):
            response = client.get(endpoint + action, params=params, allow_redirects=False, timeout=45)
            assert response.status_code == status, 'unexpected-' + action + '-status'
            return response
        PHASE = 'large-target-and-source'
        target = read('target').json(); assert target['containerId'] == identifier and target['imageId'] == IMAGE
        instance = target['instanceId']
        location = {'instanceId': instance, 'path': 'work/中文.bin'}
        metadata = read('entry', location).json(); assert metadata['sizeBytes'] == SIZE and not metadata['isProtected']
        location['expectedVersion'] = metadata['version']
        PHASE = 'complete-2mib-tail-bff-transfer'
        download_verified(session, endpoint + 'download', location, SIZE, owned['proofs']['中文.bin'][1])
        assert any(row['result'] == 'Validated' and json.loads(row['detailJson'])['sizeBytes'] == SIZE for row in audits(session, base, profile['subject'], identifier))

        PHASE = 'ordinary-user-four-read-routes'
        ticket = api.stage_secret(required_environment('VALIDATION_MANAGED_PASSWORD'))
        api.run('create-user', '', {'username': required_environment('VALIDATION_MANAGED_USERNAME'),
            'name': 'Temporary ordinary container files acceptance', 'email': '', 'inputTicket': ticket, 'inputKind': 'password'}, area='identity')
        ordinary, ordinary_profile = quiet_login(base, required_environment('VALIDATION_MANAGED_USERNAME'), required_environment('VALIDATION_MANAGED_PASSWORD'), False)
        assert ordinary_profile['isAdministrator'] is False
        for action, parameters in (('target', None), ('entries', {'instanceId': instance, 'path': 'work', 'take': 100}), ('entry', location), ('download', location)):
            read(action, parameters, 403, ordinary)
        assert read('entry', location).json()['version'] == location['expectedVersion']

        PHASE = 'genuine-backpressured-http-disconnect'
        cancel_location = {'instanceId': instance, 'path': 'work/cancel.bin'}
        cancel_meta = read('entry', cancel_location).json(); assert cancel_meta['sizeBytes'] == CANCEL_SIZE
        cancel_location['expectedVersion'] = cancel_meta['version']
        prepared = session.prepare_request(requests.Request('GET', endpoint + 'download', params=cancel_location))
        interrupted_download(prepared.url, prepared.headers, CANCEL_SIZE)
        wait_incomplete(session, base, profile['subject'], identifier, cancel_meta['version'])
        PHASE = 'post-disconnect-full-download-and-source-unchanged'
        download_verified(session, endpoint + 'download', location, SIZE, owned['proofs']['中文.bin'][1])
        assert read('entry', location).json()['version'] == location['expectedVersion']

        PHASE = 'same-cookie-reference-after-logout'
        # Preserve the original browser Cookie header: a new empty jar alone
        # would prove only anonymous rejection, not server-side invalidation.
        original_cookie = session.prepare_request(requests.Request('GET', endpoint + 'target')).headers['Cookie']
        logged_out = session.post(base + '/api/v1/auth/logout', headers=api.headers, allow_redirects=False, timeout=20)
        assert logged_out.status_code in (302, 303)
        replay = requests.Session(); replay.trust_env = False
        for action, parameters in (('target', None), ('entries', {'instanceId': instance, 'path': 'work', 'take': 100}), ('entry', location), ('download', location)):
            denied = replay.get(endpoint + action, params=parameters, headers={'Cookie': original_cookie}, allow_redirects=False, timeout=20)
            assert denied.status_code == 401
        replay.close(); ordinary.close(); session.close()
    print(json.dumps({'extendedContainerFileBff': 'passed', 'revision': revision, 'completeBytes': SIZE,
        'interruptedDeclaredBytes': CANCEL_SIZE, 'ordinaryFourRoutesRefused': True,
        'originalCookieFourRoutesRefusedAfterLogout': True, 'interruptionIncompleteNotValidated': True,
        'postDisconnectCompleteDownload': True, 'fullSourceDigestAndIdentityUnchanged': True,
        'coreContainersUnchanged': True, 'liveGroupRevocationOrBrowserValidated': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'extendedContainerFileBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
