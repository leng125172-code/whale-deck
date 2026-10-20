#!/usr/bin/env python3
"""Installed signed UDS plus independent complete original/backup/output proof.

Own exact inode-attested fixtures only; any failure preserves all evidence.
No BFF, human authorization, SQL, browser or interrupted-swap acceptance claim.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import traceback
import uuid
from validate_tree_backup_engine import DIRECTORY, ROOT, proof
from validate_tree_unpacking_agent import run
from validate_tree_restoring_agent import plain_file, identifiers
from validate_file_tree_rpc import client_failure

REPOSITORY = Path('/data/GitRepos/whale-deck')
STATE = Path('/var/lib/whaledeck-files')
PHASE = 'startup'
HELPERS = ('whaledeck-files.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py',
    'whaledeck-tree-backups.py', 'whaledeck-file-backups.py', 'whaledeck-tree-unpacking.py',
    'whaledeck-tree-restoring.py', 'whaledeck-tree-recycling.py',
    'whaledeck-tree-overwrite-restoring.py', 'whaledeck-privileged')


def fixture_scope(path):
    assert path.parent == DIRECTORY and re.fullmatch(r'wdv-agent-overwrite-[a-f0-9]{16}', path.name)
    assert path.resolve() == path and path.is_dir() and path.lstat().st_uid == 0
    assert stat.S_IMODE(path.lstat().st_mode) == 0o700


def preserve_evidence(folder, name, value):
    assert folder.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch('overwrite-evidence-[a-f0-9]{16}', folder.name)
    assert name in ('baseline.json', 'rpc-result.json') and folder.resolve() == folder
    metadata = folder.lstat()
    assert metadata.st_uid == 0 and stat.S_IMODE(metadata.st_mode) == 0o700
    raw = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()
    assert len(raw) <= 65536
    descriptor = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        remaining = memoryview(raw)
        while remaining:
            written = os.write(descriptor, remaining)
            assert written > 0
            remaining = remaining[written:]
        os.fsync(descriptor)
    finally: os.close(descriptor)
    directory = os.open(folder, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try: os.fsync(directory)
    finally: os.close(directory)
    print(json.dumps({'ownedOverwriteEvidence': folder.name + '/' + name, 'sha256': hashlib.sha256(raw).hexdigest().upper()}), flush=True)


def attest_retained(retained, kind, original_tree, original_data, retained_identity):
    assert kind in ('source', 'target')
    root = proof(retained)
    assert retained.resolve() == retained and root[:2] == tuple(retained_identity)
    if kind == 'source':
        # Recycling creates a private skeleton and moves the original files into
        # it; only the displaced overwrite target keeps its directory inode.
        assert (root[3], root[4], stat.S_IMODE(root[2])) == (0, 0, 0o700)
    else:
        assert root[:2] == original_tree[:2]
    data = proof(retained / '中文.bin')
    assert data[:8] == original_data[:8] and data[-1] == original_data[-1]
    assert {str(path.relative_to(retained)) for path in retained.rglob('*')} == {'空目录', '中文.bin'}


def exact_cleanup(fixture, identity, expected_children, private_proofs):
    fixture_scope(fixture)
    assert proof(fixture)[:2] == identity and {path.name for path in fixture.iterdir()} == expected_children
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(fixture) and not mount.startswith(str(fixture) + '/')
    members = {path: proof(path) for path in fixture.rglob('*')}
    assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in members.values())
    # No recursive delete, following links, broad globs, or unobserved metadata.
    assert all(proof(path) == value for path, value in {**members, **private_proofs}.items())
    for path, value in members.items():
        if stat.S_ISREG(value[2]): path.unlink()
    for path in sorted((path for path, value in members.items() if stat.S_ISDIR(value[2])), key=lambda item: len(item.parts), reverse=True):
        path.rmdir()
    fixture.rmdir()
    for path in private_proofs: path.unlink()
    assert not fixture.exists() and not any(path.exists() for path in private_proofs)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 4
    binary, client_revision, runtime_revision = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (client_revision, runtime_revision))
    source = Path(__file__).resolve().parents[2]
    assert source == REPOSITORY or (source.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch('overwrite-source-[a-f0-9]{40}', source.name))
    git = ['git', '-c', 'safe.directory=' + str(source), '-C', str(source)]
    assert run(git + ['rev-parse', 'HEAD']) == client_revision and not run(git + ['status', '--porcelain'])
    assert binary.parent.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[a-zA-Z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    assert binary.resolve() == binary
    for path in (binary.parent, binary):
        metadata = path.lstat()
        assert metadata.st_uid == 0 and not metadata.st_mode & 0o022
    assert stat.S_ISREG(binary.lstat().st_mode) and binary.lstat().st_nlink == 1
    assert json.loads(run(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == runtime_revision
    # Client and installed runtime remain separate evidence. Only unchanged
    # wire contracts are required; this never claims API/Worker were deployed.
    assert not run(git + ['diff', '--no-renames', runtime_revision, client_revision, '--', 'src/WhaleDeck.Contracts'])
    for name in HELPERS:
        installed = Path('/usr/local/libexec') / name
        metadata = installed.lstat()
        assert stat.S_ISREG(metadata.st_mode) and metadata.st_uid == 0 and metadata.st_nlink == 1 and not metadata.st_mode & 0o022
        trusted = subprocess.check_output(git + ['show', runtime_revision + ':deploy/host/' + name], stderr=subprocess.PIPE, timeout=30)
        assert hashlib.sha256(installed.read_bytes()).digest() == hashlib.sha256(trusted).digest()
    policy = next(row for row in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if row['id'] == ROOT)
    assert policy['path'] == str(DIRECTORY) and {'files-list', 'files-download', 'files-history', 'files-upload', 'files-mutate', 'files-archive', 'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore'} <= set(policy['allowedActions'])
    assert DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    capacity = shutil.disk_usage(DIRECTORY)
    assert capacity.free >= max(12 * 1024**3, capacity.total // 20 + 8 * 1024**3)
    configuration = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
    store = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
    os.umask(0o077)
    fixture = DIRECTORY / ('wdv-agent-overwrite-' + uuid.uuid4().hex[:16])
    fixture.mkdir(mode=0o700)
    fixture_scope(fixture)
    identity = proof(fixture)[:2]
    evidence = REPOSITORY / 'deploy/artifacts' / ('overwrite-evidence-' + fixture.name.removeprefix('wdv-agent-overwrite-'))
    evidence.mkdir(mode=0o700)
    print(json.dumps({'ownedAgentOverwriteFixture': fixture.name}), flush=True)
    originals = {}
    for size, length in (('small', 2 * 1024**2 + 7), ('large', 1024**3 + 7)):
        for kind, tail in (('source', b'SRC\0\xff!!'), ('target', b'OLD\0\xfe??')):
            tree = fixture / (size + '-' + kind)
            tree.mkdir(mode=0o750)
            (tree / '空目录').mkdir(mode=0o710)
            data = tree / '中文.bin'
            with data.open('xb') as stream:
                stream.truncate(length)
                stream.seek(length - 7)
                stream.write(tail)
                stream.flush()
                os.fsync(stream.fileno())
            data.chmod(0o640)
            os.utime(data, ns=(1712345678123456789, 1712345678123456789))
            originals[(size, kind)] = (proof(tree), proof(data))
    preserve_evidence(evidence, 'baseline.json', {'schemaVersion': 1, 'fixture': fixture.name, 'fixtureIdentity': identity,
        'clientSourceRevision': client_revision, 'runtimeSourceRevision': runtime_revision,
        'originals': {size + '-' + kind: value for (size, kind), value in originals.items()}})
    PHASE = 'installed-signed-uds-fixed-helper'
    result = subprocess.run([str(binary), 'tree-overwrite-restore'], env={**os.environ, 'WHALEDECK_OVERWRITE_PROBE_RELATIVE_PATH': fixture.name},
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=3000)
    rows = [json.loads(line) for line in result.stdout.splitlines() if line.strip()]
    if result.returncode or len(rows) != 1 or rows[0].get('installedTreeOverwriteRestoringRpc') != 'passed':
        for failure in client_failure(result.stdout, result.stderr): print(json.dumps(failure), flush=True)
        raise RuntimeError('installed-probe-failed')
    received = rows[0]
    preserve_evidence(evidence, 'rpc-result.json', received)
    completed = identifiers(received['completedOperationIds'], 2)
    canceled = identifiers(received['canceledOperationIds'], 2)
    captures = identifiers(received['captureIds'], 2)
    rejected = identifiers(received['rejectedFingerprintOperationIds'], 2)
    assert identifiers(received['overwrittenCaptureIds'], 2) == completed
    assert len(set(completed + canceled + captures + rejected)) == 8
    assert received['smallBytes'] == 2 * 1024**2 + 7 and received['largeBytes'] == 1024**3 + 7
    PHASE = 'independent-full-original-displaced-backups-and-output'
    private_proofs = {}
    devices = []
    for index, size in enumerate(('small', 'large')):
        operation, capture, abort = completed[index], captures[index], canceled[index]
        overwrite_path = STATE / 'tree-overwrite-restoring' / (operation + '.json')
        recycle_path = STATE / 'tree-recycling' / (capture + '.json')
        plain_file(overwrite_path, 32768)
        plain_file(recycle_path, 32768)
        overwrite, recycle = json.loads(overwrite_path.read_bytes()), json.loads(recycle_path.read_bytes())
        assert overwrite['state'] == 'Completed' and all(overwrite[key] for key in ('backupStarted', 'backupVerified', 'swapStarted', 'originalsRetained'))
        assert recycle['state'] == 'Completed' and overwrite['owner'] != recycle['owner']
        command = overwrite['plan']['command']
        assert command['captureId'] == capture and command['captureOwner'] == recycle['owner'] and command['captureKind'] == 'Trash'
        assert command['source'] == recycle['plan']['source']
        assert command['target']['rootId'] == ROOT and command['target']['path'] == fixture.name + '/' + size + '-target'
        output = fixture / (size + '-target')
        assert proof(output)[:2] == tuple(overwrite['newIdentity']) and proof(output)[:2] != originals[(size, 'target')][0][:2]
        payload = proof(output / '中文.bin')
        source_payload = originals[(size, 'source')][1]
        assert payload[6] == source_payload[6] and payload[-1] == source_payload[-1] and payload[:2] != source_payload[:2]
        assert (payload[3], payload[4], stat.S_IMODE(payload[2]), payload[7]) == (policy['ownerUid'], policy['ownerGid'], 0o640, source_payload[7])
        assert {str(path.relative_to(output)) for path in output.rglob('*')} == {'空目录', '中文.bin'}
        assert not (fixture / (size + '-source')).exists()
        for kind, identifier, retained_suffix, owner, source_location, capture_kind in (
            ('source', capture, '.tree-trash', recycle['owner'], recycle['plan']['source'], 'Trash'),
            ('target', operation, '.tree-overwrite-restore', overwrite['owner'], command['target'], 'Overwrite')):
            retained = fixture / '.whaledeck-files' / (identifier + retained_suffix)
            original_tree, original_data = originals[(size, kind)]
            retained_identity = recycle['retainedIdentity'] if kind == 'source' else overwrite['plan']['targetIdentity']
            attest_retained(retained, kind, original_tree, original_data, retained_identity)
            capture_path = store / (identifier + '.json')
            plain_file(capture_path, 32768)
            saved = json.loads(capture_path.read_bytes())
            assert saved['state'] == 'Verified' and saved['operationId'] == identifier and saved['owner'] == owner and saved['source'] == source_location
            assert saved.get('captureKind', 'Trash') == capture_kind and saved['entryCount'] == 3 and saved['totalBytes'] == original_data[6]
            assert saved['manifestSha256'] == (recycle['plan']['manifestSha256'] if kind == 'source' else overwrite['plan']['targetManifestSha256'])
            manifest_path = store / (identifier + '.manifest.json')
            plain_file(manifest_path, 16 * 1024**2)
            assert proof(manifest_path)[-1] == saved['manifestFileSha256']
            manifest = json.loads(manifest_path.read_bytes())
            assert manifest['manifestSha256'] == saved['manifestSha256'] and manifest['entryCount'] == 3 and manifest['totalBytes'] == original_data[6]
            assert manifest['rootId'] == ROOT and manifest['path'] == source_location['path'] and manifest['rootVersion'] == source_location['expectedVersion']
            assert {entry['path'] for entry in manifest['entries']} == {'', '空目录', '中文.bin'}
            root_proof = next(entry for entry in manifest['entries'] if entry['path'] == '')
            assert tuple(root_proof['sourceIdentity'][:8]) == original_tree[:8]
            member_proof = next(entry for entry in manifest['entries'] if entry['path'] == '中文.bin')
            assert tuple(member_proof['sourceIdentity'][:8]) == original_data[:8] and member_proof['sha256'] == original_data[-1]
            if kind == 'source':
                retained_manifest = STATE / 'tree-recycling' / (capture + '.manifest.json')
                plain_file(retained_manifest, 16 * 1024**2)
                assert json.loads(retained_manifest.read_bytes()) == manifest
                private_proofs[retained_manifest] = proof(retained_manifest)
            archive = store / (identifier + '.tar.gz')
            plain_file(archive)
            archive_proof = proof(archive)
            assert archive_proof[:2] == tuple(saved['archiveIdentity']) and archive_proof[6] == saved['archiveBytes'] and archive_proof[-1] == saved['archiveSha256']
            if kind == 'target':
                assert saved['archiveBytes'] == overwrite['backupArchiveBytes'] and saved['archiveSha256'] == overwrite['backupArchiveSha256']
            else:
                assert saved['archiveBytes'] == overwrite['plan']['archiveBytes'] and saved['archiveSha256'] == overwrite['plan']['archiveSha256']
            assert saved['differentSourceDevice'] == (original_data[0] != archive_proof[0])
            devices.append(saved['differentSourceDevice'])
            with tarfile.open(archive, 'r:gz') as reader:
                members = reader.getmembers()
                assert {member.name.rstrip('/') for member in members} == {'.', '空目录', '中文.bin'} and all(member.isdir() or member.isfile() for member in members)
                member = next(item for item in members if item.name == '中文.bin')
                assert member.size == original_data[6]
                with reader.extractfile(member) as stream:
                    assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == original_data[-1]
            for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
                path = store / (identifier + suffix)
                plain_file(path, None if suffix == '.tar.gz' else 16 * 1024**2)
                private_proofs[path] = proof(path)
        assert tuple(overwrite['plan']['targetIdentity']) == originals[(size, 'target')][0][:2]
        for identifier, folder, state in ((capture, 'tree-recycling', 'Completed'), (operation, 'tree-overwrite-restoring', 'Completed'), (abort, 'tree-overwrite-restoring', 'Aborted')):
            path = STATE / folder / (identifier + '.json')
            plain_file(path, 32768)
            record = json.loads(path.read_bytes())
            assert record['operationId'] == identifier and record['state'] == state
            if identifier == abort: assert record['owner'] == overwrite['owner'] and record['plan'] == overwrite['plan'] and not record.get('swapStarted', False)
            for suffix in ('.json', '.lock'):
                path = STATE / folder / (identifier + suffix)
                plain_file(path, 32768)
                private_proofs[path] = proof(path)
        # Invalid fingerprint is rejected before creating an operation record.
        assert all(not (STATE / 'tree-overwrite-restoring' / (rejected[index] + suffix)).exists() for suffix in ('.json', '.lock'))
    PHASE = 'exact-owned-cleanup'
    private_children = {identifier + '.tree-trash' for identifier in captures} | {identifier + '.tree-overwrite-restore' for identifier in completed}
    assert {path.name for path in (fixture / '.whaledeck-files').iterdir()} == private_children
    exact_cleanup(fixture, identity, {'small-target', 'large-target', '.whaledeck-files'}, private_proofs)
    print(json.dumps({'installedTreeOverwriteRestoringAcceptance': 'passed', 'runtimeSourceRevision': runtime_revision, 'clientSourceRevision': client_revision,
        'smallWholeBytes': received['smallBytes'], 'largeWholeBytes': received['largeBytes'], 'fullOriginalDisplacedBackupAndOutputVerified': True,
        'signedUdsAndFixedSandboxHelper': True, 'differentSourceDevice': devices, 'exactOwnedPayloadsAndReceiptsRemoved': True,
        'interruptedSwapAndOverwriteCaptureReuse': 'not-covered', 'bffWorkerHumanAuditAndUi': 'not-covered'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        own_frames = [frame for frame in traceback.extract_tb(error.__traceback__) if Path(frame.filename).resolve() == Path(__file__).resolve()]
        print(json.dumps({'installedTreeOverwriteRestoringAcceptance': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'failedLine': own_frames[-1].lineno if own_frames else None, 'unknownObjectsPreserved': True, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
