#!/usr/bin/env python3
"""Explicit real-identity installed layout check; credentials stay in memory/stdin."""
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, save

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = 'c23526c5-fc43-4944-9f27-75ac540f6df0'


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 3
    assert os.environ.get('WHALEDECK_INSTALLED_LAYOUT_PROBE') == '1'
    revision, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True)
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir()
    assert not any(evidence.iterdir()), 'FRESH_EMPTY_EVIDENCE_REQUIRED'
    os.umask(0o077)
    sources = {}
    for name in ('validate_development_layout_installed.py', 'validate_development_layout_installed.mjs',
                 'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs',
                 'validate_platform_maintenance_browser.py', 'run_with_ephemeral_admin.py',
                 'inspect_development_workspace_lifecycle.py'):
        raw = (Path(__file__).parent / name).read_bytes()
        sources[name] = hashlib.sha256(raw).hexdigest()
        target = evidence / ('source-' + name)
        target.write_bytes(raw)
        target.chmod(0o600)
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
            with subprocess.Popen(['node', str(Path(__file__).with_suffix('.mjs'))],
                    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                    text=True, start_new_session=True) as process:
                try:
                    stdout, stderr = process.communicate(json.dumps(dict(revision=revision,
                        evidence=str(evidence), account=identity)), timeout=300)
                except subprocess.TimeoutExpired:
                    # Only the private process group created above, never a
                    # global browser-name match or another user's session.
                    os.killpg(process.pid, signal.SIGTERM)
                    try:
                        process.communicate(timeout=10)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.communicate()
                    raise
                result = subprocess.CompletedProcess(process.args, process.returncode, stdout, stderr)
            # Keep only bounded summaries and redact the exact ephemeral inputs,
            # even if a future dependency unexpectedly emits them.
            def redacted(text):
                for secret in (identity['password'], identity['username'], values['Authentication__Authentik__ApiToken']):
                    text = text.replace(secret, '[redacted]')
                return text[:8192]
            save(evidence, 'browser-process.json', dict(returncode=result.returncode,
                stdout=redacted(result.stdout), stderr=redacted(result.stderr)))
        save(evidence, 'identity-cleanup.json', dict(removed=cleaned))
        assert cleaned == ['administrator']
        assert result.returncode == 0, 'INSTALLED_LAYOUT_FAILED'
        state = json.loads((evidence / 'state.json').read_bytes())
        assert state['passed'] and len(state['observations']) == 8
        expected_cases = {(width, scheme) for width in (320, 390, 768, 1440) for scheme in ('light', 'dark')}
        assert {(value['viewportWidth'], value['scheme']) for value in state['observations']} == expected_cases
        assert all(value['passed'] for value in state['observations'])
        assert state['workspaceWrites'] == 0 and state['logout']['normalUiLogout']
        after = physical()
        save(evidence, 'physical-after.json', after)
        assert before == after
        assert sources == {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in sources}
        screenshots = list(evidence.glob('installed-*.png'))
        assert {path.name for path in screenshots} == {f'installed-{width}-{scheme}.png' for width, scheme in expected_cases}
        assert all(path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') for path in screenshots)
        save(evidence, 'accepted.json', dict(passed=True, revision=revision, realIdentity=True,
            interceptedResponses=False, layoutCases=8, screenshots=8, temporaryAccountsRemoved=1,
            workspaceWrites=0, payloadQuotaAndEpochPreserved=True,
            scope='Installed environment description layout only; no archive, retirement or complete main-task acceptance'))
        print(json.dumps(dict(passed=True, evidence=str(evidence), layoutCases=8, realIdentity=True)))
    finally:
        client.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps(dict(passed=False, failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1)
