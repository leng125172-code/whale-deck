#!/usr/bin/env python3
"""Installed signed UDS cleanup with an independent two-checkpoint disk observer.

Only this invocation's root-owned inode-attested fixture and proven test journals
can be removed. Any failure preserves evidence. Not a BFF/SQL/UI acceptance.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import shutil
import stat
import subprocess
import sys
import tarfile
import traceback
import uuid
from validate_tree_backup_engine import DIRECTORY, ROOT, proof
from validate_tree_unpacking_agent import run, validation_only_changes
from validate_tree_restoring_agent import plain_file, identifiers
from validate_tree_overwrite_restoring_agent import attest_retained

REPOSITORY = Path('/data/GitRepos/whale-deck')
STATE = Path('/var/lib/whaledeck-files')
PHASE = 'startup'
HELPERS = ('whaledeck-files.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py',
    'whaledeck-tree-backups.py', 'whaledeck-file-backups.py', 'whaledeck-tree-copies.py',
    'whaledeck-tree-moves.py', 'whaledeck-tree-unpacking.py', 'whaledeck-tree-restoring.py',
    'whaledeck-tree-recycling.py', 'whaledeck-tree-overwrite-restoring.py',
    'whaledeck-tree-purging.py', 'whaledeck-privileged')


def fixture_scope(path):
    assert path.parent == DIRECTORY and re.fullmatch(r'wdv-agent-purge-[a-f0-9]{16}', path.name)
    assert path.resolve() == path and path.is_dir()
    info = path.lstat()
    assert info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700


def preserve_evidence(folder, name, value):
    assert folder.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch(r'purge-evidence-[a-f0-9]{16}', folder.name)
    assert folder.resolve() == folder and folder.is_dir()
    assert folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    assert name in ('baseline.json', 'captures.json', 'archives-purged.json', 'rpc-result.json', 'verified.json')
    raw = json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False).encode()
    assert len(raw) <= 65536
    descriptor = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as stream:
        stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    descriptor = os.open(folder, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try: os.fsync(descriptor)
    finally: os.close(descriptor)
    print(json.dumps({'ownedPurgeEvidence': folder.name + '/' + name, 'sha256': hashlib.sha256(raw).hexdigest().upper()}), flush=True)


def record(path):
    plain_file(path, 32768)
    return json.loads(path.read_bytes())


def tree(path):
    assert path.resolve() == path and path.is_dir()
    return {'': proof(path), **{str(item.relative_to(path)): proof(item) for item in path.rglob('*')}}


def exact_cleanup(fixture, identity, private_proofs, allowed_private):
    fixture_scope(fixture)
    assert proof(fixture)[:2] == identity
    assert {item.name for item in fixture.iterdir()} == {'small-target', 'large-target', '.whaledeck-files'}
    assert set(private_proofs) == set(allowed_private)
    assert all(path.resolve() == path for path in private_proofs)
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]
        assert mount != str(fixture) and not mount.startswith(str(fixture) + '/')
    members = {path: proof(path) for path in fixture.rglob('*')}
    expected_members = {fixture / '.whaledeck-files'}
    expected_members.update(fixture / name / relative for name in ('small-target', 'large-target') for relative in ('', '中文.bin', '空目录'))
    assert set(members) == expected_members
    assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in members.values())
    assert all(value[5] == 1 for value in members.values() if stat.S_ISREG(value[2]))
    for name in ('small-target', 'large-target'):
        assert stat.S_ISDIR(members[fixture / name][2]) and stat.S_ISDIR(members[fixture / name / '空目录'][2])
        assert stat.S_ISREG(members[fixture / name / '中文.bin'][2])
    for path in private_proofs: plain_file(path, 16 * 1024**2)
    assert not any((fixture / '.whaledeck-files').iterdir())
    assert all(proof(path) == value for path, value in {**members, **private_proofs}.items())
    for path, value in members.items():
        if stat.S_ISREG(value[2]):
            assert proof(path) == value
            path.unlink()
    for path in sorted((path for path, value in members.items() if stat.S_ISDIR(value[2])), key=lambda item: len(item.parts), reverse=True):
        assert proof(path)[:2] == members[path][:2]
        path.rmdir()
    fixture.rmdir()
    for path, value in private_proofs.items():
        assert proof(path) == value
        path.unlink()
    assert not fixture.exists() and not any(path.exists() for path in private_proofs)


def read_checkpoint(process):
    assert select.select([process.stdout], [], [], 3000)[0], 'PROBE_OBSERVATION_TIMEOUT'
    line = process.stdout.readline(65537)
    assert line and len(line) <= 65536 and line.endswith('\n'), 'PROBE_OUTPUT_INVALID'
    value = json.loads(line)
    assert isinstance(value, dict)
    return value


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 4
    binary, client_revision, runtime_revision = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (client_revision, runtime_revision))
    source = Path(__file__).resolve().parents[2]
    assert source == REPOSITORY
    git = ['git', '-c', 'safe.directory=' + str(source), '-C', str(source)]
    assert run(git + ['rev-parse', 'HEAD']) == client_revision and not run(git + ['status', '--porcelain'])
    assert validation_only_changes(git, runtime_revision, client_revision)
    assert json.loads(run(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == runtime_revision
    assert binary.parent.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[a-zA-Z0-9]+', binary.parent.name)
    assert binary.name == 'TerminalProbe' and binary.resolve() == binary
    for path in (binary.parent, binary):
        info = path.lstat()
        assert info.st_uid == 0 and not info.st_mode & 0o022
    assert stat.S_ISREG(binary.lstat().st_mode) and binary.lstat().st_nlink == 1
    for name in HELPERS:
        installed = Path('/usr/local/libexec') / name; info = installed.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and not info.st_mode & 0o022
        expected = subprocess.check_output(git + ['show', runtime_revision + ':deploy/host/' + name], stderr=subprocess.PIPE, timeout=30)
        assert hashlib.sha256(installed.read_bytes()).digest() == hashlib.sha256(expected).digest()
    policy = next(row for row in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if row['id'] == ROOT)
    assert policy['path'] == str(DIRECTORY) and {'files-list', 'files-download', 'files-history', 'files-mutate', 'files-archive',
        'files-tree-trash', 'files-tree-overwrite-restore', 'files-tree-purge'} <= set(policy['allowedActions'])
    assert DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    capacity = shutil.disk_usage(DIRECTORY)
    assert capacity.free >= max(12 * 1024**3, capacity.total // 20 + 8 * 1024**3)
    configuration = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
    store = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
    os.umask(0o077)
    fixture = DIRECTORY / ('wdv-agent-purge-' + uuid.uuid4().hex[:16]); fixture.mkdir(mode=0o700)
    fixture_scope(fixture); identity = proof(fixture)[:2]
    evidence = REPOSITORY / 'deploy/artifacts' / ('purge-evidence-' + fixture.name.removeprefix('wdv-agent-purge-'))
    evidence.mkdir(mode=0o700)
    print(json.dumps({'ownedAgentPurgeFixture': fixture.name}), flush=True)
    originals = {}
    for size, length in (('small', 2 * 1024**2 + 7), ('large', 1024**3 + 7)):
        for kind, tail in (('source', b'SRC\0\xff!!'), ('target', b'OLD\0\xfe??')):
            path = fixture / (size + '-' + kind); path.mkdir(mode=0o750); (path / '空目录').mkdir(mode=0o710)
            data = path / '中文.bin'
            with data.open('xb') as stream:
                stream.truncate(length); stream.seek(length - 7); stream.write(tail); stream.flush(); os.fsync(stream.fileno())
            data.chmod(0o640); os.utime(data, ns=(1712345678123456789, 1712345678123456789))
            originals[size + '-' + kind] = (proof(path), proof(data))
    preserve_evidence(evidence, 'baseline.json', {'fixture': fixture.name, 'identity': identity, 'originals': originals,
        'clientRevision': client_revision, 'runtimeRevision': runtime_revision})
    PHASE = 'installed-capture-checkpoint'
    process = subprocess.Popen([str(binary), 'tree-purge'], env={**os.environ, 'WHALEDECK_PURGE_PROBE_RELATIVE_PATH': fixture.name},
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    try:
        first = read_checkpoint(process)
        assert first.get('checkpoint') == 'captures-ready' and len(first['captures']) == 4
        captures = identifiers([row['captureId'] for row in first['captures']], 4)
        assert [row['name'] for row in first['captures']] == ['small-source', 'small-target', 'large-source', 'large-target']
        immutable, retained_proofs, archive_proofs, metadata_before = {}, {}, {}, {}
        devices = []
        for item in first['captures']:
            name, capture, kind = item['name'], item['captureId'], item['kind']
            assert kind == ('Trash' if name.endswith('source') else 'Overwrite')
            expected_tree, expected_data = originals[name]
            origin_folder = STATE / ('tree-recycling' if kind == 'Trash' else 'tree-overwrite-restoring')
            origin = record(origin_folder / (capture + '.json'))
            assert origin['operationId'] == capture and origin['state'] == 'Completed'
            saved = record(store / (capture + '.json')); metadata_before[capture] = saved
            assert saved['operationId'] == capture and saved['state'] == 'Verified' and saved['owner'] == origin['owner']
            assert saved['source']['rootId'] == ROOT and saved['source']['path'] == fixture.name + '/' + name
            assert saved.get('captureKind', 'Trash') == kind and saved['entryCount'] == 3 and saved['totalBytes'] == item['bytes'] == expected_data[6]
            manifest_path = store / (capture + '.manifest.json'); plain_file(manifest_path, 16 * 1024**2)
            assert proof(manifest_path)[-1] == saved['manifestFileSha256']
            manifest = json.loads(manifest_path.read_bytes())
            assert manifest['manifestSha256'] == saved['manifestSha256'] and manifest['entryCount'] == 3 and manifest['totalBytes'] == expected_data[6]
            assert manifest['rootId'] == ROOT and manifest['path'] == saved['source']['path'] and manifest['rootVersion'] == saved['source']['expectedVersion']
            assert {row['path'] for row in manifest['entries']} == {'', '中文.bin', '空目录'}
            member = next(row for row in manifest['entries'] if row['path'] == '中文.bin')
            assert tuple(member['sourceIdentity'][:8]) == expected_data[:8] and member['sha256'] == expected_data[-1]
            retained = fixture / '.whaledeck-files' / (capture + ('.tree-trash' if kind == 'Trash' else '.tree-overwrite-restore'))
            retained_identity = origin['retainedIdentity'] if kind == 'Trash' else origin['plan']['targetIdentity']
            attest_retained(retained, 'source' if kind == 'Trash' else 'target', expected_tree, expected_data, retained_identity)
            retained_proofs[capture] = (retained, tree(retained))
            archive = store / (capture + '.tar.gz'); plain_file(archive)
            actual = proof(archive); archive_proofs[capture] = (archive, actual)
            assert actual[:2] == tuple(saved['archiveIdentity']) and actual[6] == saved['archiveBytes'] and actual[-1] == saved['archiveSha256']
            assert saved['differentSourceDevice'] == (expected_data[0] != actual[0]); devices.append(saved['differentSourceDevice'])
            with tarfile.open(archive, 'r:gz') as reader:
                members = reader.getmembers()
                assert {row.name.rstrip('/') for row in members} == {'.', '中文.bin', '空目录'} and all(row.isfile() or row.isdir() for row in members)
                member = next(row for row in members if row.name == '中文.bin'); assert member.size == expected_data[6]
                with reader.extractfile(member) as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == expected_data[-1]
            for path in (origin_folder / (capture + '.json'), origin_folder / (capture + '.lock'), manifest_path, store / (capture + '.lock')):
                plain_file(path, 16 * 1024**2); immutable[path] = proof(path)
            if kind == 'Trash':
                path = origin_folder / (capture + '.manifest.json'); plain_file(path, 16 * 1024**2)
                assert json.loads(path.read_bytes()) == manifest; immutable[path] = proof(path)
        preserve_evidence(evidence, 'captures.json', first)
        PHASE = 'archive-only-cleanup-checkpoint'
        process.stdin.write('attested-captures\n'); process.stdin.flush()
        second = read_checkpoint(process); assert second.get('checkpoint') == 'archives-purged'
        partial = identifiers(second['completedOperationIds'], 2); identifiers(second['canceledOperationIds'], 2)
        for index, capture in enumerate(captures):
            retained, snapshot = retained_proofs[capture]; assert tree(retained) == snapshot
            archive, snapshot = archive_proofs[capture]
            if index < 2:
                assert not os.path.lexists(archive)
                saved = record(store / (capture + '.json'))
                assert saved['state'] == 'Purged' and saved['archivePurgedBy'] == partial[index] and not saved.get('originalsPurged', False)
            else: assert proof(archive) == snapshot and record(store / (capture + '.json')) == metadata_before[capture]
        assert all(proof(path) == value for path, value in immutable.items())
        preserve_evidence(evidence, 'archives-purged.json', second)
        PHASE = 'originals-and-all-cleanup-checkpoint'
        process.stdin.write('attested-archives\n'); process.stdin.flush()
        final = read_checkpoint(process); assert final.get('installedTreePurgingRpc') == 'passed'
        process.stdin.close()
        assert process.wait(timeout=30) == 0 and not process.stdout.read(65537) and not process.stderr.read(65537)
        preserve_evidence(evidence, 'rpc-result.json', final)
    finally:
        if process.poll() is None:
            process.terminate()
            try: process.wait(timeout=30)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=30)
        for stream in (process.stdin, process.stdout, process.stderr): stream.close()
    PHASE = 'independent-final-tombstones-and-visible-output'
    completed = identifiers(final['completedOperationIds'], 6); canceled = identifiers(final['canceledOperationIds'], 6)
    rejected = identifiers(final['rejectedFingerprintOperationIds'], 6)
    assert len(set(completed + canceled + rejected + captures)) == 22 and completed[:2] == partial
    assert all(proof(path) == value for path, value in immutable.items())
    private_proofs = dict(immutable)
    for index, capture in enumerate(captures):
        assert not os.path.lexists(retained_proofs[capture][0]) and not os.path.lexists(archive_proofs[capture][0])
        path = store / (capture + '.json'); saved = record(path)
        original_operation = completed[index + 2]
        archive_operation = completed[index] if index < 2 else original_operation
        assert saved == {**metadata_before[capture], 'state': 'Purged', 'archivePurgedBy': archive_operation,
            'originalsPurged': True, 'originalsPurgedBy': original_operation}
        private_proofs[path] = proof(path)
    for index, operation in enumerate(completed):
        scope = 'Archive' if index < 2 else 'Originals' if index < 4 else 'All'
        capture = captures[index if index < 2 else index - 2]
        current = record(STATE / 'tree-purging' / (operation + '.json'))
        abort = record(STATE / 'tree-purging' / (canceled[index] + '.json'))
        assert current['operationId'] == operation and current['state'] == 'Completed' and current['deletionStarted']
        assert current['owner'] == abort['owner'] and current['owner'] != metadata_before[capture]['owner'] and current['plan'] == abort['plan']
        assert current['plan']['command']['scope'] == scope and current['plan']['command']['captureId'] == capture
        assert current['plan']['command']['source'] == metadata_before[capture]['source']
        assert bool(current.get('archiveRemoved')) == (scope in ('Archive', 'All'))
        assert bool(current.get('originalsRemoved')) == (scope in ('Originals', 'All'))
        assert abort['state'] == 'Aborted' and not abort.get('deletionStarted', False)
        for identifier in (operation, canceled[index]):
            for suffix in ('.json', '.lock'):
                path = STATE / 'tree-purging' / (identifier + suffix); plain_file(path, 32768); private_proofs[path] = proof(path)
        assert all(not os.path.lexists(STATE / 'tree-purging' / (rejected[index] + suffix)) for suffix in ('.json', '.lock'))
        assert not os.path.lexists(store / (operation + '.purging.tar.gz'))
    for size in ('small', 'large'):
        assert not os.path.lexists(fixture / (size + '-source'))
        output = tree(fixture / (size + '-target')); expected = originals[size + '-source'][1]
        assert set(output) == {'', '中文.bin', '空目录'}
        data = output['中文.bin']; assert data[6] == expected[6] and data[-1] == expected[-1] and data[:2] != expected[:2]
        assert (data[3], data[4], stat.S_IMODE(data[2]), data[7]) == (policy['ownerUid'], policy['ownerGid'], 0o640, expected[7])
    assert not any((fixture / '.whaledeck-files').iterdir())
    allowed_private = set()
    for index, capture in enumerate(captures):
        folder = STATE / ('tree-recycling' if index % 2 == 0 else 'tree-overwrite-restoring')
        allowed_private.update(folder / (capture + suffix) for suffix in ('.json', '.lock'))
        if index % 2 == 0: allowed_private.add(folder / (capture + '.manifest.json'))
        allowed_private.update(store / (capture + suffix) for suffix in ('.json', '.manifest.json', '.lock'))
    allowed_private.update(STATE / 'tree-purging' / (identifier + suffix) for identifier in completed + canceled for suffix in ('.json', '.lock'))
    preserve_evidence(evidence, 'verified.json', {'completeWholeBytes': [2 * 1024**2 + 7, 1024**3 + 7], 'differentSourceDevice': devices,
        'wholeCaptureAndArchiveBeforePurge': True, 'archiveOnlyRetainedOriginals': True, 'visibleOutputUnchanged': True, 'tombstonesVerified': True})
    PHASE = 'exact-new-owned-fixture-cleanup'
    exact_cleanup(fixture, identity, private_proofs, allowed_private)
    print(json.dumps({'installedTreePurgingAcceptance': 'passed', 'runtimeRevision': runtime_revision, 'clientRevision': client_revision,
        'wholeBytes': [2 * 1024**2 + 7, 1024**3 + 7], 'differentSourceDevice': devices, 'captureKinds': ['Trash', 'Overwrite'],
        'scopes': ['Archive', 'Originals', 'All'], 'independentCheckpoints': 2, 'completed': 6, 'aborted': 6,
        'exactNewOwnedPayloadsAndJournalsRemoved': True, 'bffWorkerHumanAuditAndUi': 'not-covered', 'actualInterruption': 'not-covered'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        frames = [frame for frame in traceback.extract_tb(error.__traceback__) if Path(frame.filename).resolve() == Path(__file__).resolve()]
        print(json.dumps({'installedTreePurgingAcceptance': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'failedLine': frames[-1].lineno if frames else None, 'unknownObjectsPreserved': True, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
