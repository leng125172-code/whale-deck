#!/usr/bin/env python3
"""Opt-in read-only installed development pages, using two disposable identities.

Credentials travel only through a private stdin pipe. No workspace is created,
allocated, run, imported or published. Missing host prerequisites stay unaccepted.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, command, jobs, protected, runtime, save

ROOT = Path(__file__).resolve().parents[2]


def snapshot():
    count = command('docker', 'exec', '-u', 'postgres', 'database-platform-postgres',
        'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-c',
        'SELECT count(*) FROM development_workspaces;')
    return dict(protected=protected(), runtime=runtime(), jobs=jobs(), workspaceCount=int(count))


def main():
    revision = sys.argv[1]
    assert re.fullmatch('[a-f0-9]{40}', revision)
    os.umask(0o077)
    evidence = ROOT / 'deploy/artifacts' / ('development-installed-ui-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    sources = ['validate_development_installed_browser.py', 'validate_development_installed_browser.mjs',
        'validate_platform_maintenance_browser.py', 'installed_ui_permission_session.mjs',
        'installed_ui_logout.mjs', 'run_with_ephemeral_admin.py']
    fingerprints = {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in sources}
    save(evidence, 'sources.json', fingerprints)
    before = snapshot()
    save(evidence, 'before.json', before)
    cleaned = []
    client = requests.Session()
    client.trust_env = False
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        with contextlib.ExitStack() as stack:
            accounts = [stack.enter_context(account(client, role, group, cleaned)) for role in ('administrator', 'ordinary')]
            result = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs'))],
                input=json.dumps(dict(accounts=accounts, revision=revision, evidence=str(evidence))),
                text=True, capture_output=True, timeout=300)
            # The child emits only fixed summaries. Exception details are excluded.
            save(evidence, 'browser-process.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
            assert result.returncode == 0, 'INSTALLED_DEVELOPMENT_BROWSER_FAILED'
        assert sorted(cleaned) == ['administrator', 'ordinary']
        after = snapshot()
        save(evidence, 'after.json', after)
        assert before == after, 'INSTALLED_STATE_CHANGED'
        assert fingerprints == {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in sources}
        screenshots = list(evidence.glob('*.png'))
        assert len(screenshots) == 6 and all(p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') for p in screenshots)
        save(evidence, 'accepted.json', dict(passed=True, runtimeRevision=revision, actualUi=True,
            accountsRemoved=2, applicationWrites=0, workspaceCountUnchanged=True, coreUnchanged=11,
            screenshots=6, runnerAccepted=False, sourceImportAccepted=False))
        print(json.dumps(dict(passed=True, evidence=str(evidence), screenshots=6, accountsRemoved=2)))
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
        print(json.dumps(dict(installedDevelopmentUi='failed', failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
