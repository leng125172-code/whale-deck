#!/usr/bin/env python3
"""Explicit installed plan-only acceptance; credentials stay in memory/stdin."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, save

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = 'c23526c5-fc43-4944-9f27-75ac540f6df0'


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 3
    assert os.environ.get('WHALEDECK_INSTALLED_ARCHIVE_PLAN_PROBE') == '1'
    revision, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True)
    fault = os.environ.get('WHALEDECK_INSTALLED_ARCHIVE_QA_FAULT')
    assert fault in (None, 'after-stopped-plan')
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir()
    assert not (evidence/'state.json').exists(), 'FRESH_EVIDENCE_REQUIRED'
    os.umask(0o077)
    publication = Path(__file__).parent/'DevelopmentArchiveRpcProbe/bin/Release/net10.0/publish'
    def hashes(directory):
        result = {}
        for path in directory.rglob('*'):
            assert not path.is_symlink()
            if path.is_file():
                assert path.stat().st_nlink == 1
                result[str(path.relative_to(directory))] = hashlib.sha256(path.read_bytes()).hexdigest()
        return result
    original = hashes(publication)
    assert 'DevelopmentArchiveRpcProbe.dll' in original
    frozen = evidence/'probe'
    shutil.copytree(publication, frozen, symlinks=True)
    for path in [frozen, *frozen.rglob('*')]:
        assert not path.is_symlink()
        path.chmod(0o700 if path.is_dir() else 0o600)
    assert hashes(frozen) == hashes(publication) == original
    save(evidence, 'probe-sha256.json', original)
    sources = {}
    for name in ('validate_development_archive_installed.py', 'validate_development_archive_installed.mjs',
                 'installed_development_lifecycle.mjs', 'inspect_development_workspace_lifecycle.py',
                 'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs',
                 'DevelopmentArchiveRpcProbe/Program.cs', 'DevelopmentArchiveRpcProbe/InstalledPlanProbe.cs'):
        raw = (Path(__file__).parent/name).read_bytes()
        sources[name] = hashlib.sha256(raw).hexdigest()
        target = evidence/('source-'+name.replace('/', '-'))
        target.write_bytes(raw)
        target.chmod(0o600)
    save(evidence, 'source-sha256.json', sources)
    raw = subprocess.check_output(['python3', '-B', str(Path(__file__).with_name(
        'inspect_development_workspace_lifecycle.py')), WORKSPACE], timeout=30)
    physical = json.loads(raw)
    assert physical['marker']['state'] == 'Active'
    save(evidence, 'physical-before.json', physical)
    client, cleaned = requests.Session(), []
    client.trust_env = False
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        with account(client, 'administrator', group, cleaned) as identity:
            result = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs'))], input=json.dumps(
                dict(revision=revision, evidence=str(evidence), account=identity, storage=physical['storageProof'],
                     faultAfterStopped=fault is not None)),
                capture_output=True, text=True, timeout=720)
            save(evidence, 'browser-process.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
        save(evidence, 'identity-cleanup.json', dict(removed=cleaned))
        assert cleaned == ['administrator']
        state = json.loads((evidence/'state.json').read_bytes())
        if fault is None:
            assert result.returncode == 0 and state['passed'] and state['lifecycle']['passed'], 'INSTALLED_ARCHIVE_ACCEPTANCE_FAILED'
        else:
            assert result.returncode == 1 and state['passed'] is False and state['faultInjected'] == fault
            assert state['stoppedPlan']['passed'] and state['recovery']['active'] and state['recovery']['lifecycle']['passed']
            assert state['recovery']['logout']['normalUiLogout']
        assert hashes(frozen) == original
        assert sources == {name: hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in sources}
        after = json.loads(subprocess.check_output(['python3', '-B', str(Path(__file__).with_name(
            'inspect_development_workspace_lifecycle.py')), WORKSPACE], timeout=30))
        save(evidence, 'physical-after.json', after)
        assert dict(physical, marker=None) == dict(after, marker=None)
        assert after['marker']['state'] == 'Active' and after['marker']['epoch'] == physical['marker']['epoch'] + 2
        save(evidence, 'accepted.json', dict(passed=True, revision=revision, installedAgentPlan=True,
            realIdentityUiLifecycle=True, temporaryAccountsRemoved=1, payloadAndQuotaPreserved=True,
            controlledQaFailure=fault,
            scope='Plan-only trusted RPC; no HDD capture, persistent archive business or retirement'))
        print(json.dumps(dict(passed=True, evidence=str(evidence), workspaceId=WORKSPACE)))
    finally:
        client.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps(dict(passed=False, failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1)
