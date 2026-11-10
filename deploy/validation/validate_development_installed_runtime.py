#!/usr/bin/env python3
"""Opt-in real workspace acceptance with credentials kept only in memory/stdin.

The uniquely labelled workspace and immutable jobs/audits are retained for the
product decommission workflow. No SQL deletes or ad-hoc host cleanup are used.
On failure, the live test identities can survive a bounded repair/retry window.
"""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tarfile
import time
import uuid

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, command, jobs, protected, runtime, save

ROOT = Path(__file__).resolve().parents[2]


def fixture(package_feed):
    entries = {
        'sum.mjs': b'export const add = (a, b) => a + b;\n',
        'sum.test.mjs': ('import test from "node:test";\nimport assert from "node:assert/strict";\n'
            'import {add} from "./sum.mjs";\ntest("真实加法",()=>assert.equal(add(2,3),5));\n'
            'test("negative values",()=>assert.equal(add(-2,3),1));\n').encode(),
        'Qa.csproj': ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject>'
            '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
            '<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit>'
            '<RestoreSources>$(MSBuildThisFileDirectory)offline-packages</RestoreSources>'
            '</PropertyGroup><ItemGroup>'
            '<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1"/>'
            '<PackageReference Include="xunit" Version="2.9.3"/>'
            '<PackageReference Include="xunit.runner.visualstudio" Version="3.1.4"/>'
            '</ItemGroup></Project>\n').encode(),
        'ArithmeticTests.cs': ('using Xunit;\npublic class ArithmeticTests {\n'
            '[Fact] public void Adds() => Assert.Equal(5, 2 + 3);\n'
            '[Fact] public void HandlesNegativeValues() { Assert.Equal(1, -2 + 3); '
            'Assert.Equal(1, System.Environment.ProcessorCount); }\n}\n').encode(),
        '说明.txt': '真实工作站专用验收源码。\n'.encode(),
    }
    packages = sorted(package_feed.glob('*.nupkg'))
    assert packages and all(file.is_file() and not file.is_symlink() for file in packages)
    assert sum(file.stat().st_size for file in packages) <= 64 * 1024**2
    for file in packages:
        assert re.fullmatch(r'[A-Za-z0-9_.-]+\.nupkg', file.name)
        entries['offline-packages/' + file.name] = file.read_bytes()
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode='w', format=tarfile.USTAR_FORMAT) as writer:
        for name, body in entries.items():
            # USTAR has no encoding metadata; the host supports UTF-8 paths.
            item = tarfile.TarInfo(name)
            item.size, item.mode, item.mtime = len(body), 0o644, 1700000000
            writer.addfile(item, io.BytesIO(body))
        directory = tarfile.TarInfo('offline-packages')
        directory.type, directory.mode, directory.mtime = tarfile.DIRTYPE, 0o755, 1700000000
        writer.addfile(directory)
    return output.getvalue()


def snapshot():
    return dict(protected=protected(), runtime=runtime(), jobs=jobs())


def main():
    assert os.geteuid() == 0 and __debug__
    revision = sys.argv[1]
    assert re.fullmatch('[a-f0-9]{40}', revision)
    os.umask(0o077)
    evidence = ROOT / 'deploy/artifacts' / ('development-installed-runtime-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    slug = 'qa-native-' + uuid.uuid4().hex[:24]
    # Feed contains only public packages resolved before the run. No NuGet
    # settings, account credentials, prebuilt assemblies or test outputs enter.
    content = fixture(Path(sys.argv[2]).resolve(strict=True))
    archive = evidence / 'source.tar'
    archive.write_bytes(content)
    archive.chmod(0o600)
    save(evidence, 'declaration.json', dict(slug=slug, revision=revision, archiveSha256=hashlib.sha256(content).hexdigest(),
        retention='Dedicated acceptance workspace; awaiting product decommission'))
    client = requests.Session()
    client.trust_env = False
    cleaned = []
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        with contextlib.ExitStack() as stack:
            accounts = [stack.enter_context(account(client, role, group, cleaned)) for role in ('administrator', 'ordinary')]
            accepted_before = None
            for attempt in range(1, 7):
                before = snapshot()
                save(evidence, f'before-{attempt}.json', before)
                sources = {}
                for name in ('validate_development_installed_runtime.py', 'validate_development_installed_runtime.mjs',
                             'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs',
                             'installed_development_lifecycle.mjs', 'inspect_development_workspace_lifecycle.py'):
                    data = (Path(__file__).parent / name).read_bytes()
                    target = evidence / f'attempt-{attempt}-{name}'
                    target.write_bytes(data)
                    target.chmod(0o600)
                    sources[name] = hashlib.sha256(data).hexdigest()
                payload = dict(revision=revision, evidence=str(evidence), slug=slug, accounts=accounts,
                    archive=str(archive), archiveSha256=hashlib.sha256(content).hexdigest())
                result = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs'))],
                    input=json.dumps(payload), text=True, capture_output=True, timeout=900)
                save(evidence, f'process-{attempt}.json', dict(returncode=result.returncode,
                    stdout=result.stdout, stderr=result.stderr, sourceSha256=sources))
                if result.returncode == 0:
                    accepted_before = before
                    break
                save(evidence, 'awaiting-repair.json', dict(attempt=attempt, retainedWorkspace=True,
                    retryFile='retry.json', next='Repair the observed failure, then request retry with the actual runtime revision'))
                print(json.dumps(dict(awaitingRepair=True, evidence=str(evidence), attempt=attempt)), flush=True)
                deadline = time.monotonic() + 1800
                control = evidence / 'retry.json'
                while not control.exists() and time.monotonic() < deadline:
                    time.sleep(1)
                if not control.exists():
                    raise RuntimeError('REPAIR_WINDOW_EXPIRED')
                retry = json.loads(control.read_text())
                control.unlink()
                assert retry.get('action') == 'retry' and re.fullmatch('[a-f0-9]{40}', retry.get('revision', ''))
                revision = retry['revision']
            assert accepted_before is not None, 'INSTALLED_RUNTIME_NOT_ACCEPTED'
        assert sorted(cleaned) == ['administrator', 'ordinary']
        after = snapshot()
        save(evidence, 'after.json', after)
        assert accepted_before['protected'] == after['protected'] and accepted_before['runtime'] == after['runtime']
        state = json.loads((evidence / 'state.json').read_text())
        assert state['passed'] and state['retainedForProductDecommission'] and state['lifecycle']['passed']
        save(evidence, 'accepted.json', dict(passed=True, runtimeRevision=revision, actualUi=True,
            temporaryAccountsRemoved=2, coreUnchanged=11, workspaceId=state['workspace']['id'],
            retainedForProductDecommission=True, idleLifecyclePassed=True, sourceImportId=state['imports'][-1]['id'],
            successfulRuns=[r['id'] for r in state['runs'] if r.get('result', {}).get('state') == 'Succeeded']))
        print(json.dumps(dict(passed=True, evidence=str(evidence), workspaceId=state['workspace']['id'], retainedForProductDecommission=True)))
    except BaseException as error:
        save(evidence, 'failed-wrapper.json', dict(failureType=type(error).__name__, cleanedRoles=cleaned))
        print(json.dumps(dict(passed=False, evidence=str(evidence))), file=sys.stderr)
        raise
    finally:
        client.close()


if __name__ == '__main__':
    try:
        def stopped(signum, frame):
            raise SystemExit(128 + signum)
        signal.signal(signal.SIGTERM, stopped)
        main()
    except Exception as error:
        print(json.dumps(dict(installedRuntime='failed', failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
