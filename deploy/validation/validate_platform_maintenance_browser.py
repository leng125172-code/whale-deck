#!/usr/bin/env python3
"""Opt-in installed Authentik/Vue checks; plans are cancelled, never executed.

Only two exact ephemeral identities are created/deleted. Passwords use a private
stdin pipe, never argv, files, browser tracing, stdout or a storage-state export.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys
import uuid

import requests
from platform_maintenance_ui_proof import prove_report
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ('validate_platform_maintenance_browser.py', 'validate_platform_maintenance_browser.mjs',
    'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs', 'run_with_ephemeral_admin.py', 'platform_maintenance_ui_proof.py')
RUNTIME = '134fd253bae0c343850e0057f4b18f8f7d55ddeb'


def command(*args, input=None):
    result = subprocess.run(args, input=input, text=True, capture_output=True, timeout=60)
    assert result.returncode == 0, 'PLATFORM_UI_CHECK_COMMAND_FAILED'
    return result.stdout.strip()


def save(evidence, name, value):
    p = evidence / name
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
    p.chmod(0o600)


def fingerprint(path):
    s = path.lstat()
    assert stat.S_ISREG(s.st_mode) and s.st_nlink == 1
    return dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(), uid=s.st_uid,
        gid=s.st_gid, mode=stat.S_IMODE(s.st_mode))


def protected():
    state = Path('/var/lib/whaledeck-agent/platform-maintenance')
    paths = [state / name for name in ('deployment-revisions.json', 'previous-revision', 'previous-revision.legacy')]
    paths += [Path('/data/GitRepos/whale-deck') / name for name in ('.env', '.images.env')]
    paths += [Path('/etc/sudoers.d') / name for name in ('whaledeck-agent', '90-whaledeck-development-user')]
    paths += [Path('/usr/local/libexec') / name for name in
        ('whaledeck-platform-control-recovery.py', 'whaledeck-platform-revisions.py', 'whaledeck-platform-maintenance', 'whaledeck-privileged')]
    assert not (state / 'control-recovery.json').exists()
    return {str(path): fingerprint(path) for path in paths}


def runtime():
    names = [n for n in command('docker', 'ps', '--format', '{{.Names}}').splitlines()
        if n.startswith(('whaledeck-', 'database-platform-'))]
    assert len(names) == 11
    template = '{"id":{{json .Id}},"image":{{json .Image}},"startedAt":{{json .State.StartedAt}},"health":{{json .State.Health.Status}}}'
    rows = [json.loads(x) for x in command('docker', 'inspect', '--format', template, *names).splitlines()]
    assert all(v['health'] == 'healthy' for v in rows)
    return dict(core=sorted((v['id'], v['image'], v['startedAt'], v['health']) for v in rows),
        hosts=command('systemctl', 'show', 'whaledeck-agent.service', 'whaledeck-maintenance.service',
            '-p', 'MainPID', '-p', 'InvocationID', '-p', 'ActiveState').splitlines())


def jobs():
    sql = "SELECT json_build_object('total',(SELECT count(*) FROM operation_jobs),'active',(SELECT count(*) FROM operation_jobs WHERE completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks));"
    value = json.loads(command('docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres',
        'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', input=sql))
    assert value['active'] == value['locks'] == 0
    return value


@contextlib.contextmanager
def account(api, role, group, cleaned):
    name = 'whaledeck-platform-ui-' + uuid.uuid4().hex
    password = 'Wd!' + secrets.token_urlsafe(24) + '9aA'
    identifier = None
    try:
        response = api.post(AUTHENTIK_API + 'core/users/', json=dict(username=name,
            name='Temporary platform UI acceptance', email='', is_active=True,
            groups=[group] if role == 'administrator' else []), timeout=20)
        assert response.status_code == 201
        identifier = str(response.json()['pk'])
        response = api.post(AUTHENTIK_API + f'core/users/{identifier}/set_password/', json={'password': password}, timeout=20)
        assert response.status_code in (200, 204)
        yield dict(role=role, username=name, password=password)
    finally:
        if identifier is not None:
            url = AUTHENTIK_API + f'core/users/{identifier}/'
            response = api.get(url, timeout=20)
            assert response.status_code == 200 and response.json()['username'] == name
            assert api.delete(url, timeout=20).status_code == 204
            assert api.get(url, timeout=20).status_code == 404
            cleaned.append(role)


def main():
    global RUNTIME
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('[a-f0-9]{40}', sys.argv[1])
    RUNTIME = sys.argv[1]
    os.umask(0o077)
    assert command('git', '-C', str(ROOT), 'status', '--porcelain') == ''
    source = command('git', '-C', str(ROOT), 'rev-parse', 'HEAD')
    assert re.fullmatch('[a-f0-9]{40}', source)
    evidence = ROOT / 'deploy/artifacts' / ('platform-maintenance-ui-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    sources = {}
    for name in SOURCES:
        raw = subprocess.check_output(['git', '-C', str(ROOT), 'show', source + ':deploy/validation/' + name])
        assert raw == (ROOT / 'deploy/validation' / name).read_bytes()
        p = evidence / name
        p.write_bytes(raw)
        p.chmod(0o600)
        sources[name] = hashlib.sha256(raw).hexdigest()
    before = dict(protected=protected(), runtime=runtime(), jobs=jobs())
    save(evidence, 'before.json', before)
    client = requests.Session()
    client.trust_env = False
    values = container_environment()
    client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
    group = require(values, 'Authentication__Authentik__AdministratorGroupId')
    cleaned = []
    try:
        with contextlib.ExitStack() as stack:
            accounts = [stack.enter_context(account(client, role, group, cleaned)) for role in ('administrator', 'ordinary')]
            payload = dict(revision=RUNTIME, evidence=str(evidence), accounts=accounts)
            # Only the bounded, sanitized child summary reaches disk. Do not log
            # input, environment, stdout on a parser exception or HTTP bodies.
            result = subprocess.run(['node', str(ROOT / 'deploy/validation/validate_platform_maintenance_browser.mjs'), RUNTIME],
                input=json.dumps(payload), text=True, capture_output=True, timeout=360)
            save(evidence, 'browser-process.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
            assert result.returncode == 0, 'PLATFORM_BROWSER_FAILED'
            value = json.loads((evidence / 'browser.json').read_text())
            prove_report(value, RUNTIME)
        assert sorted(cleaned) == ['administrator', 'ordinary']
        after = dict(protected=protected(), runtime=runtime(), jobs=jobs())
        assert before == after, 'PRODUCT_CHANGED_DURING_READONLY_UI'
        save(evidence, 'after.json', after)
        for name, digest in sources.items():
            assert hashlib.sha256((ROOT / 'deploy/validation' / name).read_bytes()).hexdigest() == digest
        screenshots = list(evidence.glob('*.png'))
        assert len(screenshots) == 11 and all(p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') for p in screenshots)
        save(evidence, 'accepted.json', dict(passed=True, sourceRevision=source, runtimeRevision=RUNTIME,
            accountsRemoved=2, screenshots=len(screenshots), jobsUnchanged=True, coreUnchanged=11,
            ledgerUnchanged=True, actualUi=True, applicationExecutionRequests=0))
        print(json.dumps(dict(passed=True, runtimeRevision=RUNTIME, evidence=str(evidence), accountsRemoved=2, screenshots=len(screenshots))))
    except BaseException:
        save(evidence, 'failed.json', dict(passed=False, cleanedRoles=cleaned))
        print(json.dumps(dict(passed=False, evidence=str(evidence))), file=sys.stderr)
        raise
    finally:
        client.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps(dict(platformMaintenanceUi='failed', failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
