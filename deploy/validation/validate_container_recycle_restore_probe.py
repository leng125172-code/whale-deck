#!/usr/bin/env python3
"""Freeze the probe/proof candidate; read-only refusal against the old installation.

No new container, source capture, restore RPC, key access or installation. A 1.57
installation needs a separate owning gate and physical namespace verification.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_directory_restart import preflight
from validate_container_recycle_agent import baseline, preserved
from validate_container_upload_recovery_agent import trusted
from container_recycle_restore_rpc_proof import capability_refusal, unique_json

SOURCES = ('validate_container_recycle_restore_probe.py', 'container_recycle_restore_agent_io.py', 'test_container_recycle_restore_agent_io.py', 'test_container_recycle_restore_rpc_proof.py', 'test_container_file_recycle_restores.py')


def main(revision, binary_name, probe_revision):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{40}', revision) and re.fullmatch('[a-f0-9]{40}', probe_revision)
    os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'; repo = Path(__file__).resolve().parents[2]
    git = lambda *args: subprocess.check_output(['git', '-c', 'safe.directory='+str(repo), '-C', str(repo), *args])
    assert not git('status', '--porcelain').strip(); sha = git('rev-parse', 'HEAD').decode().strip(); assert sha == probe_revision
    closure = source_closure(repo, sha, seeds=SOURCES)
    sources = {str(Path('deploy/validation')/n): h for n, h in closure.items()}
    tracked = git('ls-files', 'deploy/host/*.py', 'deploy/validation/TerminalProbe/*', 'src/WhaleDeck.Contracts/*', 'src/WhaleDeck.Contracts/Protos/*', 'Directory.*', 'global.json', 'NuGet.config', '.images.env').decode().splitlines()
    tracked += ['deploy/host/whaledeck-privileged', 'deploy/host/install.sh', 'deploy/host/validate.sh', 'deploy/validation/publish-terminal-probe.sh']
    for name in tracked: sources[name] = hashlib.sha256(git('show', sha+':'+name)).hexdigest()
    for name, value in sources.items(): assert hashlib.sha256((repo/name).read_bytes()).hexdigest() == value
    binary = Path(binary_name)
    assert binary.parent.parent == repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    trusted(binary); trusted(binary.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=binary.parent, check=True, capture_output=True)
    preflight(); before = baseline(revision)
    assert before['restoreDatabase']['active'] == 0 and not Path('/var/lib/whaledeck-container-recycle-restores').exists()
    e = repo/'deploy/artifacts'/('container-recycle-restore-probe-'+uuid.uuid4().hex); e.mkdir(mode=0o700)
    save = lambda name, value: (e/name).write_text(json.dumps(value, sort_keys=True, indent=2))
    save('source.json', dict(productRevision=revision, validationRevision=sha, probeRevision=probe_revision, binary=str(binary), sourceSha256=sources))
    save('before.json', before)
    print(json.dumps(dict(recycleRestoreProbe='started', evidence=e.name)), flush=True)
    try:
        tests = {}
        for pattern in ('test_container_file_recycl*.py', 'test_container_recycle*.py', 'test_scope_baseline.py'):
            run = subprocess.run([sys.executable, '-W', 'error::ResourceWarning', '-m', 'unittest', 'discover', '-s', 'deploy/validation', '-p', pattern], cwd=repo, text=True, capture_output=True, timeout=150)
            stem = pattern.replace('*', '_'); (e/(stem+'.stdout')).write_text(run.stdout); (e/(stem+'.stderr')).write_text(run.stderr)
            match = re.search(r'Ran (\d+) tests in [^\n]+\n\nOK\s*$', run.stderr)
            assert run.returncode == 0 and match and 'skipped' not in run.stderr
            tests[pattern] = int(match[1])
        header = dict(revision=revision, probeRevision=probe_revision, containerId='a'*64, instanceId='777:2026-11-06T01:02:03Z', coreId=before['core']['/whaledeck-api'][0])
        assert header['containerId'] != header['coreId']; save('old-host-header.json', header)
        run = subprocess.run([str(binary), 'container-recycle-restores'], input=json.dumps(header)+'\n', text=True, capture_output=True, timeout=20)
        (e/'old-host.stdout').write_text(run.stdout); (e/'old-host.stderr').write_text(run.stderr)
        save('old-host-refusal.json', capability_refusal(run.returncode, run.stdout, unique_json(run.stderr)))
        # A synthetic resource identifier is intentional: refusal occurs before any resource request.
        preflight(); after = baseline(revision); preserved(before, after, ()); save('after.json', after)
        assert not Path('/var/lib/whaledeck-container-recycle-restores').exists()
        typed_equal(before, after)
        frozen = e/'source'; frozen.mkdir(mode=0o700)
        for name, value in sources.items():
            raw = git('show', sha+':'+name); assert hashlib.sha256(raw).hexdigest() == value
            path = frozen/name; path.parent.mkdir(parents=True, exist_ok=True, mode=0o700); path.write_bytes(raw)
        save('probe-binary.json', dict(binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(), assemblySha256=hashlib.sha256((binary.parent/'TerminalProbe.dll').read_bytes()).hexdigest(), contractsSha256=hashlib.sha256((binary.parent/'WhaleDeck.Contracts.dll').read_bytes()).hexdigest(), publishSha256=hashlib.sha256((binary.parent/'SHA256SUMS').read_bytes()).hexdigest()))
        save('verified.json', dict(tests=tests, productionUnchanged=True, oldHostReadOnlyRefusal=True, restoreNamespaceNotInstalled=True, actualRestoreRpc=False, physicalHdd=False, permanentBusiness=False, humanUi=False))
        print(json.dumps(dict(recycleRestoreProbe='passed', evidence=e.name, tests=tests, actualRestoreRpc=False)), flush=True)
    except BaseException as error:
        import traceback
        save('failure.json', dict(type=type(error).__name__, frames=[dict(file=Path(f.filename).name, line=f.lineno, function=f.name) for f in traceback.extract_tb(error.__traceback__)])); raise


if __name__ == '__main__':
    try: assert len(sys.argv) == 4; main(*sys.argv[1:])
    except BaseException as error: print(json.dumps(dict(recycleRestoreProbe='failed', type=type(error).__name__)), flush=True); raise SystemExit(1) from None
