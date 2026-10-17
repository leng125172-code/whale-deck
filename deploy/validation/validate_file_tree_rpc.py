#!/usr/bin/env python3
"""Installed directory RPC + independent byte/metadata observer in an owned fixture.

Root only. SDK client gets only the UDS and read-only capability key, never data
or Docker socket. Failure preserves owned data; no automatic rollback/cleanup of
uncertain output. Success removes only the fully verified fixture membership.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tarfile
import tempfile
import uuid

ROOT_ID = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
PHASE = 'startup'


def call(arguments, timeout=60):
    return subprocess.run(arguments, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=timeout).stdout.strip()


def checksum(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while value := stream.read(4 * 1024 * 1024): digest.update(value)
    return digest.hexdigest()


def main():
    global PHASE
    assert os.geteuid() == 0 and DATA.is_dir() and not DATA.is_symlink(), 'owned-root-validation-required'
    repo = Path(__file__).resolve().parents[2]
    git = ['git', '-c', 'safe.directory=' + str(repo), '-C', str(repo)]
    revision = call(git + ['rev-parse', 'HEAD']); assert re.fullmatch('[a-f0-9]{40}', revision)
    assert not call(git + ['status', '--porcelain']), 'frozen-source-required'
    policy = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT_ID)
    assert policy['path'] == str(DATA) and 'files-tree-copy' in policy['allowedActions']
    capacity = shutil.disk_usage(DATA)
    assert capacity.free >= max(5 * 1024**3, capacity.total // 20 + 3 * 1024**3), 'large-probe-capacity-required'
    marker = 'wdv-agent-tree-' + uuid.uuid4().hex[:16]; folder = DATA / marker
    assert not os.path.lexists(folder); folder.mkdir(mode=0o700); identity = folder.stat()
    print(json.dumps({'ownedTreeProbeFixture': marker, 'sourceSha': revision}), flush=True)
    source = folder / 'source'; source.mkdir(); (source / '0-empty').mkdir(); (source / '代码').mkdir()
    original = source / '代码/原始.bin'; original.write_bytes(os.urandom(2 * 1024 * 1024 + 7)); original.chmod(0o640)
    protected = folder / 'protected'; protected.mkdir(); (protected / '.env').write_bytes(b'test-only-no-real-secret')
    large_source = folder / 'large-source'; large_source.mkdir(); (large_source / '0-empty').mkdir()
    large = large_source / 'large.bin'
    with large.open('wb') as stream:
        stream.truncate(1024**3 + 7); stream.seek(1024**3); stream.write(b'WDVTREE')
    large.chmod(0o640)
    containers = []; acceptance = None
    try:
        PHASE = 'build-client'
        with tempfile.TemporaryDirectory(prefix='tree-agent-', dir=repo / 'deploy/artifacts') as scratch:
            scratch_path = Path(scratch); archive = scratch_path / 'source.tar'; public_source = scratch_path / 'public-source'; public_source.mkdir()
            call(git + ['archive', '--format=tar', '--output=' + str(archive), revision])
            with tarfile.open(archive) as package: package.extractall(public_source, filter='data')
            dockerfile = (public_source / 'src/WhaleDeck.Api/Dockerfile').read_text()
            sdk = re.search(r'^ARG DOTNET_SDK_IMAGE=(mcr\.microsoft\.com/dotnet/sdk:10\.0\.[0-9]{3}@sha256:[a-f0-9]{64})$', dockerfile, re.M)[1]
            runtime = re.search(r'^ARG ASPNET_IMAGE=mcr\.microsoft\.com/dotnet/aspnet:(10\.0\.[0-9]+)@sha256:', dockerfile, re.M)[1]
            sdk_id = call(['docker', 'image', 'inspect', '--format', '{{.Id}}', sdk]); assert re.fullmatch('sha256:[a-f0-9]{64}', sdk_id)

            def create(arguments):
                identifier = call(['docker', 'create', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
                    '--pids-limit', '256', '--memory', '1536m', '--cpus', '2', '--log-driver', 'local', '--log-opt', 'max-size=2m', '--log-opt', 'max-file=2',
                    '--label', 'io.whaledeck.validation.marker=' + marker, *arguments])
                assert re.fullmatch('[a-f0-9]{64}', identifier); containers.append(identifier); return identifier

            build = create(['--tmpfs', '/tmp:rw,nosuid,nodev,size=256m', '--user', '0:0',
                '--env', 'DOTNET_CLI_HOME=/scratch/cli', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '--env', 'NUGET_PACKAGES=/scratch/packages',
                '--mount', 'type=bind,source=' + str(public_source) + ',target=/repo,readonly', '--mount', 'type=bind,source=' + scratch + ',target=/scratch',
                '--workdir', '/repo', sdk_id, 'dotnet', 'publish', 'deploy/validation/TerminalProbe/TerminalProbe.csproj', '--configuration', 'Release',
                '--runtime', 'linux-x64', '--self-contained', 'true', '--artifacts-path', '/scratch/build', '--output', '/scratch/client',
                '-p:SourceRevisionId=' + revision, '-p:RuntimeFrameworkVersion=' + runtime])
            call(['docker', 'start', '--attach', build], timeout=600)
            assert call(['docker', 'inspect', '--format', '{{.State.ExitCode}}', build]) == '0', 'client-build-failed'
            PHASE = 'installed-rpc'
            client = create(['--network', 'none', '--tmpfs', '/tmp:rw,nosuid,nodev,size=64m', '--user', '0:0',
                '--group-add', str(Path('/run/whaledeck/agent.sock').stat().st_gid), '--env', 'DOTNET_EnableDiagnostics=0',
                '--env', 'WHALEDECK_TREE_PROBE_RELATIVE_PATH=' + marker, '--env', 'WHALEDECK_TREE_PROBE_LARGE=1',
                '--mount', 'type=bind,source=' + str(scratch_path / 'client') + ',target=/probe,readonly',
                '--mount', 'type=bind,source=/run/whaledeck/agent.sock,target=/run/whaledeck/agent.sock',
                '--mount', 'type=bind,source=/etc/whaledeck/agent-capability.key,target=/etc/whaledeck/agent-capability.key,readonly',
                '--entrypoint', '/probe/TerminalProbe', sdk_id, 'tree-copy'])
            execution = subprocess.run(['docker', 'start', '--attach', client], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=600)
            output = execution.stdout
            if execution.returncode != 0 or call(['docker', 'inspect', '--format', '{{.State.ExitCode}}', client]) != '0':
                for line in output.splitlines()[-8:]:
                    try: value = json.loads(line)
                    except ValueError: continue
                    detail = value.get('fileCode'); client_phase = value.get('phase')
                    if detail is not None and not re.fullmatch('FILES_[A-Z_]{1,64}', str(detail)): detail = None
                    if not isinstance(client_phase, str) or not re.fullmatch('[a-z0-9-]{1,80}', client_phase): client_phase = None
                    print(json.dumps({'treeClientFailed': True, 'clientPhase': client_phase, 'fileCode': detail}), flush=True)
                raise RuntimeError('client-rpc-failed')
            result = json.loads(output.splitlines()[-1]); assert result['installedTreeCopyRpc'] == 'passed'
            assert result['smallBytes'] == 2 * 1024**2 + 7 and result['largeBytes'] == 1024**3 + 7
            assert len(result['completedOperationIds']) == 2 and len(result['canceledOperationIds']) == 1
            PHASE = 'independent-observer'
            for before, after in ((original, folder / 'copied/代码/原始.bin'), (large, folder / 'large-copy/large.bin')):
                assert checksum(before) == checksum(after)
                first, last = before.stat(), after.stat()
                assert (last.st_size, last.st_mtime_ns, stat.S_IMODE(last.st_mode)) == (first.st_size, first.st_mtime_ns, stat.S_IMODE(first.st_mode))
                assert (last.st_uid, last.st_gid, last.st_nlink) == (policy['ownerUid'], policy['ownerGid'], 1)
                assert (first.st_dev, first.st_ino) != (last.st_dev, last.st_ino)
            for name in ('copied/0-empty', 'large-copy/0-empty'): assert not list((folder / name).iterdir())
            assert not (folder / 'canceled').exists() and not (folder / 'protected-copy').exists()
            for identifier in result['completedOperationIds'] + result['canceledOperationIds']:
                assert re.fullmatch('[a-f0-9]{32}', identifier)
                receipt = Path('/var/lib/whaledeck-files/tree-copies') / (identifier + '.json'); info = receipt.lstat()
                assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 32768
                record = json.loads(receipt.read_bytes()); assert record['operationId'] == identifier
                assert record['plan']['command']['source']['rootId'] == ROOT_ID and record['plan']['command']['source']['path'].startswith(marker + '/')
                assert record['state'] == ('Completed' if identifier in result['completedOperationIds'] else 'Aborted')

            PHASE = 'cleanup-verified-fixture'
            expected_directories = {'source', 'source/0-empty', 'source/代码', 'copied', 'copied/0-empty', 'copied/代码',
                'protected', 'large-source', 'large-source/0-empty', 'large-copy', 'large-copy/0-empty', '.whaledeck-files'}
            expected_files = {'source/代码/原始.bin', 'copied/代码/原始.bin', 'protected/.env', 'large-source/large.bin', 'large-copy/large.bin'}
            actual_dirs = set(); actual_files = set()
            current = folder.lstat(); assert (current.st_dev, current.st_ino, current.st_uid) == (identity.st_dev, identity.st_ino, 0)
            for line in Path('/proc/self/mountinfo').read_text().splitlines(): assert not line.split()[4].startswith(str(folder) + '/')
            for parent, directories, files in os.walk(folder, followlinks=False):
                for name in directories + files:
                    path = Path(parent) / name; value = path.lstat(); assert value.st_dev == identity.st_dev and not stat.S_ISLNK(value.st_mode)
                    assert value.st_uid in (0, policy['ownerUid'])
                    if stat.S_ISDIR(value.st_mode): actual_dirs.add(str(path.relative_to(folder)))
                    else:
                        assert stat.S_ISREG(value.st_mode) and value.st_nlink == 1; actual_files.add(str(path.relative_to(folder)))
            assert actual_dirs == expected_directories and actual_files == expected_files, 'unexpected-fixture-membership'
            shutil.rmtree(folder)
            acceptance = {'installedTreeCopyRpc': 'passed', 'sourceSha': revision, 'smallBytes': result['smallBytes'], 'largeBytes': result['largeBytes'],
                'independentFullByteMetadataAndRootReceipts': 'passed', 'exactOwnedFixtureRemoved': True, 'rootReceiptsRetained': True,
                'bffWorkerHumanAuditAndUi': 'not-covered'}
    finally:
        for identifier in containers:
            inspected = subprocess.run(['docker', 'inspect', identifier], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=30)
            if inspected.returncode:
                if 'No such object' in inspected.stderr or 'No such container' in inspected.stderr: continue
                raise RuntimeError('owned-sdk-cleanup-unconfirmed')
            value = json.loads(inspected.stdout)[0]
            assert value['Id'] == identifier and value['Config']['Labels']['io.whaledeck.validation.marker'] == marker
            call(['docker', 'rm', '--force', identifier])
        if folder.exists(): print(json.dumps({'ownedFixturePreservedForInvestigation': marker, 'phase': PHASE}))
    assert acceptance is not None
    print(json.dumps({**acceptance, 'ownedSdkContainersRemoved': True}))


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'installedTreeCopyRpc': 'failed', 'phase': PHASE, 'failureType': type(error).__name__}))
        raise SystemExit(1) from None
