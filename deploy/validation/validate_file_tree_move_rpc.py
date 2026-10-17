#!/usr/bin/env python3
"""Frozen installed move RPC + independent original-inode/byte observer.

SDK gets only UDS/key, no Docker socket or data mount. Observer timeouts retain
running containers and scratch/fixture state; they are not restarted or treated
as terminal. Successful cleanup is exact membership, never generic file purge.
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
import traceback
import uuid
from validate_file_tree_rpc import call, checksum, client_failure

DATA = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
PHASE = 'startup'


def observe_owned_containers(containers, marker):
    """A timeout is not completion. Never remove a live or unconfirmed container."""
    live = []
    for identifier in containers:
        assert re.fullmatch('[a-f0-9]{64}', identifier)
        inspected = subprocess.run(['docker', 'inspect', identifier], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=30)
        if inspected.returncode:
            if 'No such object' in inspected.stderr or 'No such container' in inspected.stderr: continue
            raise RuntimeError('owned-container-state-unconfirmed')
        value = json.loads(inspected.stdout)[0]
        assert value['Id'] == identifier and value['Config']['Labels']['io.whaledeck.validation.marker'] == marker
        state = value['State']
        assert type(state['Running']) is bool and type(state['Restarting']) is bool and type(state['Paused']) is bool
        if state['Running'] or state['Restarting'] or state['Paused']:
            live.append(identifier); continue
        assert state['Status'] in ('created', 'exited', 'dead')
        call(['docker', 'rm', identifier])
    return live


def main():
    global PHASE
    assert os.geteuid() == 0 and DATA.resolve() == DATA and DATA.is_dir()
    repo = Path(__file__).resolve().parents[2]; git = ['git', '-c', 'safe.directory=' + str(repo), '-C', str(repo)]
    revision = call(git + ['rev-parse', 'HEAD']); assert re.fullmatch('[a-f0-9]{40}', revision)
    assert not call(git + ['status', '--porcelain']), 'frozen-source-required'
    policy = next(x for x in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if x['id'] == ROOT)
    assert policy['path'] == str(DATA) and {'files-tree-move', 'files-list', 'files-download', 'files-upload', 'files-mutate'} <= set(policy['allowedActions'])
    capacity = shutil.disk_usage(DATA); assert capacity.free >= max(5 * 1024**3, capacity.total // 20 + 3 * 1024**3)
    marker = 'wdv-agent-move-' + uuid.uuid4().hex[:16]; folder = DATA / marker
    assert not os.path.lexists(folder); folder.mkdir(mode=0o700); identity = folder.stat()
    status_path = repo / 'deploy/artifacts/native-status' / ('tree-move-rpc-' + uuid.uuid4().hex + '.json'); status_path.parent.mkdir(parents=True, exist_ok=True)
    status = {'sourceSha': revision, 'fixture': marker, 'runnerPid': os.getpid(), 'state': 'Preparing', 'containers': []}

    def report(**fields):
        status.update(fields); descriptor, temporary = tempfile.mkstemp(prefix='.tree-move-status-', dir=status_path.parent)
        with os.fdopen(descriptor, 'w') as stream: json.dump(status, stream); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, status_path)

    report(); print(json.dumps({'ownedTreeMoveFixture': marker, 'sourceSha': revision, 'statusFile': str(status_path.relative_to(repo))}), flush=True)
    scratch = Path(tempfile.mkdtemp(prefix='tree-move-agent-', dir=repo / 'deploy/artifacts'))
    report(scratch=str(scratch.relative_to(repo)))
    completed = False; observed_live = False; containers = []; acceptance = None
    try:
        source = folder / 'source'; source.mkdir(); (source / '0-empty').mkdir(); (source / '代码').mkdir()
        small = source / '代码/原始.bin'; small.write_bytes(os.urandom(2 * 1024**2 + 7)); small.chmod(0o640)
        cancel = folder / 'cancel-source'; cancel.mkdir(); (cancel / 'data.bin').write_bytes(b'cancel-must-preserve-source')
        protected = folder / 'protected'; protected.mkdir(); (protected / '.env').write_bytes(b'fixture-only-not-a-secret')
        large_source = folder / 'large-source'; large_source.mkdir(); (large_source / '0-empty').mkdir(); large = large_source / 'large.bin'
        with large.open('wb') as stream: stream.truncate(1024**3 + 7); stream.seek(1024**3); stream.write(b'WDVMOVE')
        large.chmod(0o640)
        original = [(path.stat(), checksum(path)) for path in (small, large)]
        PHASE = 'build-client'; report(phase=PHASE, state='Running')
        archive = scratch / 'source.tar'; public_source = scratch / 'public-source'; public_source.mkdir()
        call(git + ['archive', '--format=tar', '--output=' + str(archive), revision])
        with tarfile.open(archive) as package: package.extractall(public_source, filter='data')
        dockerfile = (public_source / 'src/WhaleDeck.Api/Dockerfile').read_text()
        sdk = re.search(r'^ARG DOTNET_SDK_IMAGE=(mcr\.microsoft\.com/dotnet/sdk:10\.0\.[0-9]{3}@sha256:[a-f0-9]{64})$', dockerfile, re.M)[1]
        runtime = re.search(r'^ARG ASPNET_IMAGE=mcr\.microsoft\.com/dotnet/aspnet:(10\.0\.[0-9]+)@sha256:', dockerfile, re.M)[1]
        sdk_id = call(['docker', 'image', 'inspect', '--format', '{{.Id}}', sdk]); assert re.fullmatch('sha256:[a-f0-9]{64}', sdk_id)

        def create(arguments):
            identifier = call(['docker', 'create', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--ulimit', 'core=0:0',
                '--pids-limit', '256', '--memory', '1536m', '--cpus', '2', '--log-driver', 'local', '--log-opt', 'max-size=2m', '--log-opt', 'max-file=2',
                '--label', 'io.whaledeck.validation.marker=' + marker, *arguments])
            assert re.fullmatch('[a-f0-9]{64}', identifier); containers.append(identifier); report(containers=list(containers)); return identifier

        build = create(['--tmpfs', '/tmp:rw,nosuid,nodev,size=256m', '--user', '0:0', '--env', 'DOTNET_CLI_HOME=/scratch/cli',
            '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '--env', 'NUGET_PACKAGES=/scratch/packages', '--mount', 'type=bind,source=' + str(public_source) + ',target=/repo,readonly',
            '--mount', 'type=bind,source=' + str(scratch) + ',target=/scratch', '--workdir', '/repo', sdk_id, 'dotnet', 'publish',
            'deploy/validation/TerminalProbe/TerminalProbe.csproj', '--configuration', 'Release', '--runtime', 'linux-x64', '--self-contained', 'true',
            '--artifacts-path', '/scratch/build', '--output', '/scratch/client', '-p:SourceRevisionId=' + revision, '-p:RuntimeFrameworkVersion=' + runtime])
        call(['docker', 'start', '--attach', build], timeout=600)
        assert call(['docker', 'inspect', '--format', '{{.State.ExitCode}}', build]) == '0'
        PHASE = 'installed-rpc'; report(phase=PHASE)
        client = create(['--network', 'none', '--tmpfs', '/tmp:rw,nosuid,nodev,size=64m', '--user', '0:0', '--group-add', str(Path('/run/whaledeck/agent.sock').stat().st_gid),
            '--env', 'DOTNET_EnableDiagnostics=0', '--env', 'WHALEDECK_TREE_MOVE_PROBE_RELATIVE_PATH=' + marker,
            '--mount', 'type=bind,source=' + str(scratch / 'client') + ',target=/probe,readonly',
            '--mount', 'type=bind,source=/run/whaledeck/agent.sock,target=/run/whaledeck/agent.sock',
            '--mount', 'type=bind,source=/etc/whaledeck/agent-capability.key,target=/etc/whaledeck/agent-capability.key,readonly',
            '--entrypoint', '/probe/TerminalProbe', sdk_id, 'tree-move'])
        execution = subprocess.run(['docker', 'start', '--attach', client], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=1200)
        if execution.returncode or call(['docker', 'inspect', '--format', '{{.State.ExitCode}}', client]) != '0':
            for failure in client_failure(execution.stdout, execution.stderr): print(json.dumps(failure), flush=True)
            raise RuntimeError('installed-client-failed')
        result = json.loads(execution.stdout.splitlines()[-1]); assert result['installedTreeMoveRpc'] == 'passed'
        assert result['smallBytes'] == 2 * 1024**2 + 7 and result['largeBytes'] == 1024**3 + 7
        operations = result['completedOperationIds']; canceled_id = result['canceledOperationId']; assert len(operations) == 2
        assert len(set(operations + [canceled_id])) == 3 and all(re.fullmatch('[a-f0-9]{32}', x) for x in operations + [canceled_id])
        PHASE = 'independent-observer'; report(phase=PHASE, completedOperations=operations, canceledOperation=canceled_id)
        expected_dirs = {'moved', 'moved/0-empty', 'moved/代码', 'large-moved', 'large-moved/0-empty', 'cancel-source', 'protected', '.whaledeck-files'}
        expected_files = {'moved/代码/原始.bin', 'large-moved/large.bin', 'cancel-source/data.bin', 'protected/.env'}
        assert not source.exists() and not large_source.exists() and not (folder / 'canceled').exists() and not (folder / 'protected-move').exists()
        assert (cancel / 'data.bin').read_bytes() == b'cancel-must-preserve-source' and (protected / '.env').read_bytes() == b'fixture-only-not-a-secret'
        for index, (name, relative, empty_dirs) in enumerate((('moved', '代码/原始.bin', ['0-empty', '代码']), ('large-moved', 'large.bin', ['0-empty']))):
            operation = operations[index]; receipt = Path('/var/lib/whaledeck-files/tree-moves') / (operation + '.json'); info = receipt.lstat()
            assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 32768
            record = json.loads(receipt.read_bytes()); assert record['state'] == 'Completed' and record['operationId'] == operation and record['sourceRemoved'] and record['sourceRetained']
            command = record['plan']['copyPlan']['command']; assert command['source']['rootId'] == command['target']['rootId'] == ROOT
            assert command['source']['path'] == marker + '/' + ('source' if index == 0 else 'large-source') and command['target']['path'] == marker + '/' + name
            retained_path = '.whaledeck-files/' + operation + '.tree-move'; retained = folder / retained_path
            assert [retained.stat().st_dev, retained.stat().st_ino] == record['retainedIdentity']
            before, digest = original[index]; kept = (retained / relative).stat(); target = (folder / name / relative).stat()
            assert checksum(retained / relative) == checksum(folder / name / relative) == digest
            assert (kept.st_dev, kept.st_ino, kept.st_uid, kept.st_gid, kept.st_size, kept.st_mtime_ns, stat.S_IMODE(kept.st_mode), kept.st_nlink) == (
                before.st_dev, before.st_ino, before.st_uid, before.st_gid, before.st_size, before.st_mtime_ns, stat.S_IMODE(before.st_mode), 1)
            assert (target.st_size, target.st_mtime_ns, stat.S_IMODE(target.st_mode)) == (before.st_size, before.st_mtime_ns, stat.S_IMODE(before.st_mode))
            assert (target.st_uid, target.st_gid, target.st_nlink) == (policy['ownerUid'], policy['ownerGid'], 1) and (target.st_dev, target.st_ino) != (before.st_dev, before.st_ino)
            for child in empty_dirs:
                assert (retained / child).is_dir() and (folder / name / child).is_dir()
                if child == '0-empty': assert not list((retained / child).iterdir()) and not list((folder / name / child).iterdir())
            manifest_path = receipt.with_name(operation + '.manifest.json'); manifest_info = manifest_path.lstat()
            assert stat.S_ISREG(manifest_info.st_mode) and manifest_info.st_uid == 0 and stat.S_IMODE(manifest_info.st_mode) == 0o600 and manifest_info.st_nlink == 1 and manifest_info.st_size <= 16 * 1024**2
            manifest = json.loads(manifest_path.read_bytes()); encoded = json.dumps(manifest, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()
            assert hashlib.sha256(b'whaledeck-tree-move-v1\n' + encoded).hexdigest().upper() == record['manifestDigest']
            assert manifest['manifestSha256'] == record['plan']['copyPlan']['manifestSha256']
            expected_dirs.add(retained_path); expected_dirs.update(retained_path + '/' + x for x in empty_dirs); expected_files.add(retained_path + '/' + relative)
        canceled_path = Path('/var/lib/whaledeck-files/tree-moves') / (canceled_id + '.json'); canceled = json.loads(canceled_path.read_bytes())
        assert canceled['state'] == 'Aborted' and canceled['operationId'] == canceled_id
        PHASE = 'cleanup-exact-fixture'; report(phase=PHASE)
        current = folder.lstat(); assert folder.resolve() == folder and (current.st_dev, current.st_ino, current.st_uid) == (identity.st_dev, identity.st_ino, 0)
        for line in Path('/proc/self/mountinfo').read_text().splitlines(): assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
        dirs = set(); files = set()
        for parent, names, members in os.walk(folder, followlinks=False):
            for name in names + members:
                path = Path(parent) / name; info = path.lstat(); assert info.st_dev == identity.st_dev and info.st_uid in (0, policy['ownerUid']) and not stat.S_ISLNK(info.st_mode)
                if stat.S_ISDIR(info.st_mode): dirs.add(str(path.relative_to(folder)))
                else:
                    assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                    files.add(str(path.relative_to(folder)))
        assert dirs == expected_dirs and files == expected_files
        shutil.rmtree(folder)
        acceptance = {'installedTreeMoveRpc': 'passed', 'sourceSha': revision, 'smallBytes': result['smallBytes'], 'largeBytes': result['largeBytes'],
            'independentFullBytesOriginalInodesMetadataAndRootManifests': 'passed', 'exactOwnedFixtureRemoved': True,
            'rootReceiptsRetained': True, 'bffWorkerHumanAuditAndUi': 'not-covered'}
        completed = True
    finally:
        observed_live = bool(observe_owned_containers(containers, marker))
        if not observed_live: shutil.rmtree(scratch)
        report(state='AwaitingObservation' if observed_live else 'Passed' if completed else 'Failed', phase=PHASE,
            ownedSdkCleanupConfirmed=not observed_live, fixtureCleanupConfirmed=not folder.exists())
        if folder.exists(): print(json.dumps({'ownedMoveFixturePreserved': marker, 'phase': PHASE, 'statusFile': str(status_path.relative_to(repo))}), flush=True)
        if observed_live: print(json.dumps({'runningOwnedSdkPreserved': True, 'statusFile': str(status_path.relative_to(repo))}), flush=True)
    assert acceptance is not None and not observed_live
    print(json.dumps({**acceptance, 'ownedSdkContainersRemoved': True}))


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'installedTreeMoveRpc': 'not-confirmed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
