#!/usr/bin/env python3
"""Opt-in real archive user workflow; retain source, budget, HDD and all audit."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, save

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = 'c23526c5-fc43-4944-9f27-75ac540f6df0'


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 3
    assert os.environ.get('WHALEDECK_INSTALLED_ARCHIVE_BUSINESS_PROBE') == '1'
    revision, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True)
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir()
    assert not (evidence / 'state.json').exists(), 'FRESH_EVIDENCE_REQUIRED'
    os.umask(0o077)
    sources = {}
    for name in ('validate_workspace_archive_business_installed.py', 'validate_workspace_archive_business_installed.mjs',
                 'installed_development_lifecycle.mjs', 'inspect_development_workspace_lifecycle.py',
                 'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs'):
        raw = (Path(__file__).parent / name).read_bytes()
        sources[name] = hashlib.sha256(raw).hexdigest()
        (evidence / ('source-' + name)).write_bytes(raw)
    save(evidence, 'source-sha256.json', sources)

    def physical():
        return json.loads(subprocess.check_output(['python3', '-B', str(Path(__file__).with_name(
            'inspect_development_workspace_lifecycle.py')), WORKSPACE], timeout=30))

    before = physical()
    assert before['marker']['state'] == 'Active'
    save(evidence, 'physical-before.json', before)
    client, cleaned = requests.Session(), []
    client.trust_env = False
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        with account(client, 'administrator', group, cleaned) as identity:
            result = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs'))], input=json.dumps(
                dict(revision=revision, evidence=str(evidence), account=identity)),
                capture_output=True, text=True, timeout=900)
            save(evidence, 'browser-process.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
        save(evidence, 'identity-cleanup.json', dict(removed=cleaned))
        assert cleaned == ['administrator']
        state = json.loads((evidence / 'state.json').read_bytes())
        assert result.returncode == 0 and state['passed'] and state['lifecycle']['passed'], 'INSTALLED_ARCHIVE_BUSINESS_FAILED'
        assert sources == {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in sources}
        after = physical()
        save(evidence, 'physical-after.json', after)
        assert dict(before, marker=None) == dict(after, marker=None)
        assert after['marker']['state'] == 'Active' and after['marker']['epoch'] == before['marker']['epoch'] + 2
        save(evidence, 'accepted.json', dict(passed=True, revision=revision, realIdentityUiArchive=True,
            canceledPlan=True, captureAndIndependentVerify=True, temporaryAccountsRemoved=1,
            payloadAndQuotaPreserved=True, retainsHddCapture=True,
            scope='Persistent Plan/confirmation/Capture/Verify/cancel; no source removal or budget release'))
        print(json.dumps(dict(passed=True, evidence=str(evidence), workspaceId=WORKSPACE)))
    finally:
        client.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps(dict(passed=False, failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1)
