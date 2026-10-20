#!/usr/bin/env python3
"""Actual installed RPC/helper and independent full disk/archive observer.

Only new inode-proven test fixtures and their exact terminal receipts/captures
are removed. Failures preserve evidence. No production payload, BFF/SQL human
permission or WebUI acceptance is claimed by this low-level probe.
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
import uuid
from validate_tree_backup_engine import DIRECTORY, ROOT, proof
from validate_tree_unpacking_agent import run, validation_only_changes
from validate_file_tree_rpc import client_failure

REPOSITORY = Path('/data/GitRepos/whale-deck')
STATE = Path('/var/lib/whaledeck-files')
PHASE = 'startup'


def plain_file(path, limit=None):
    value = path.lstat()
    assert stat.S_ISREG(value.st_mode) and value.st_uid == 0 and value.st_nlink == 1 and stat.S_IMODE(value.st_mode) == 0o600
    assert limit is None or value.st_size <= limit


def identifiers(value, count):
    assert isinstance(value, list) and len(value) == count and len(set(value)) == count
    assert all(isinstance(item, str) and re.fullmatch('[a-f0-9]{32}', item) and uuid.UUID(item).int for item in value)
    return value


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) in (3, 4)
    binary, client_revision = Path(sys.argv[1]), sys.argv[2]
    revision = sys.argv[3] if len(sys.argv) == 4 else client_revision
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (revision, client_revision))
    assert binary.parent.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[a-zA-Z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    assert binary.resolve() == binary and stat.S_ISREG(binary.lstat().st_mode)
    git = ['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY)]
    assert run(git + ['rev-parse', 'HEAD']) == client_revision and not run(git + ['status', '--porcelain'])
    assert validation_only_changes(git, revision, client_revision)
    assert json.loads(run(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == revision
    for name in ('whaledeck-files.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-tree-backups.py', 'whaledeck-file-backups.py', 'whaledeck-tree-unpacking.py', 'whaledeck-tree-restoring.py', 'whaledeck-tree-recycling.py', 'whaledeck-privileged'):
        installed = Path('/usr/local/libexec') / name; info = installed.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and not info.st_mode & 0o022
        assert hashlib.sha256(installed.read_bytes()).digest() == hashlib.sha256((REPOSITORY / 'deploy/host' / name).read_bytes()).digest()
    policy = next(row for row in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if row['id'] == ROOT)
    assert policy['path'] == str(DIRECTORY) and {'files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash', 'files-tree-restore'} <= set(policy['allowedActions'])
    assert DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    capacity = shutil.disk_usage(DIRECTORY)
    assert capacity.free >= max(10 * 1024**3, capacity.total // 20 + 6 * 1024**3)
    configuration = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
    store = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
    os.umask(0o077)
    fixture = DIRECTORY / ('wdv-agent-restore-' + uuid.uuid4().hex[:16]); fixture.mkdir(mode=0o700)
    identity = proof(fixture)[:2]
    print(json.dumps({'ownedAgentRestoringFixture': fixture.name}), flush=True)
    originals = {}
    for name, size in (('small', 2 * 1024**2 + 7), ('large', 1024**3 + 7)):
        source = fixture / name; source.mkdir(mode=0o750); (source / '空目录').mkdir(mode=0o710)
        path = source / '中文.bin'
        with path.open('wb') as stream:
            stream.truncate(size); stream.seek(size - 7); stream.write(b'TAIL\x00\xff!'); stream.flush(); os.fsync(stream.fileno())
        path.chmod(0o640); os.utime(path, ns=(1712345678123456789, 1712345678123456789))
        originals[name] = proof(path)
    environment = {**os.environ, 'WHALEDECK_RESTORE_PROBE_RELATIVE_PATH': fixture.name}
    PHASE = 'installed-signed-uds-fixed-helper'
    executed = subprocess.run([str(binary), 'tree-restore'], env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=2400)
    rows = [json.loads(line) for line in executed.stdout.splitlines() if line.strip()]
    if executed.returncode or len(rows) != 1 or rows[0].get('installedTreeRestoringRpc') != 'passed':
        for failure in client_failure(executed.stdout, executed.stderr): print(json.dumps(failure), flush=True)
        raise RuntimeError('installed-probe-failed')
    result = rows[0]
    completed = identifiers(result['completedOperationIds'], 2); canceled = identifiers(result['canceledOperationIds'], 2); captures = identifiers(result['captureIds'], 2)
    assert len(set(completed + canceled + captures)) == 6
    assert result['smallBytes'] == 2 * 1024**2 + 7 and result['largeBytes'] == 1024**3 + 7
    PHASE = 'independent-full-original-capture-and-restored-disk'
    owned_records = {}; archive_proofs = {}; captured_devices = []
    for index, name in enumerate(('small', 'large')):
        capture, operation, abort = captures[index], completed[index], canceled[index]
        capture_path = STATE / 'tree-recycling' / (capture + '.json'); plain_file(capture_path, 32768)
        recycle = json.loads(capture_path.read_bytes())
        assert recycle['operationId'] == capture and recycle['state'] == 'Completed' and recycle['plan']['source']['path'] == fixture.name + '/' + name
        retained = fixture / '.whaledeck-files' / (capture + '.tree-trash')
        assert retained.resolve() == retained and proof(retained)[:2] == tuple(recycle['retainedIdentity'])
        assert {str(path.relative_to(retained)) for path in retained.rglob('*')} == {'空目录', '中文.bin'}
        retained_proof = proof(retained / '中文.bin')
        assert retained_proof[:8] == originals[name][:8] and retained_proof[-1] == originals[name][-1]
        capture_record = store / (capture + '.json'); plain_file(capture_record, 32768)
        saved = json.loads(capture_record.read_bytes())
        assert saved['state'] == 'Verified' and saved['source'] == recycle['plan']['source'] and saved['owner'] == recycle['owner']
        assert saved['manifestSha256'] == recycle['plan']['manifestSha256'] and saved['entryCount'] == 3 and saved['totalBytes'] == originals[name][6]
        captured_devices.append(saved['differentSourceDevice'])
        archive = store / (capture + '.tar.gz'); plain_file(archive)
        archive_proof = proof(archive)
        assert archive_proof[:2] == tuple(saved['archiveIdentity']) and archive_proof[6] == saved['archiveBytes'] and archive_proof[-1] == saved['archiveSha256']
        output = fixture / ('small' if name == 'small' else 'large-restored')
        assert output.is_dir() and output.resolve() == output and {str(path.relative_to(output)) for path in output.rglob('*')} == {'空目录', '中文.bin'}
        actual = proof(output / '中文.bin')
        assert actual[6] == originals[name][6] and actual[-1] == originals[name][-1] and actual[:2] != originals[name][:2]
        assert (actual[3], actual[4], stat.S_IMODE(actual[2]), actual[7]) == (policy['ownerUid'], policy['ownerGid'], 0o640, originals[name][7])
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers(); assert {member.name.rstrip('/') for member in members} == {'.', '空目录', '中文.bin'}
            assert all(member.isdir() or member.isfile() for member in members)
            member = next(item for item in members if item.name == '中文.bin'); assert member.size == originals[name][6]
            with reader.extractfile(member) as content: assert hashlib.file_digest(content, 'sha256').hexdigest().upper() == originals[name][-1]
        for identifier in (operation, abort):
            path = STATE / 'tree-restoring' / (identifier + '.json'); plain_file(path, 32768); record = json.loads(path.read_bytes())
            assert record['operationId'] == identifier and record['state'] == ('Completed' if identifier == operation else 'Aborted')
            command = record['plan']['command']
            assert command['source'] == saved['source'] and command['captureId'] == capture and command['captureOwner'] == saved['owner']
            assert record['owner'] != saved['owner'] and command['captureKind'] == 'Trash' and command['manifestSha256'] == saved['manifestSha256']
            assert command['target'] == {'rootId': ROOT, 'path': str(output.relative_to(DIRECTORY))}
            assert record['plan']['archiveSha256'] == archive_proof[-1] and record['plan']['archiveBytes'] == archive_proof[6]
            if identifier == operation: assert proof(output)[:2] == tuple(record['newIdentity'])
            for suffix in ('.json', '.lock'):
                private = path.with_suffix(suffix); plain_file(private, 32768); owned_records[private] = proof(private)
        for suffix in ('.json', '.lock'):
            private = capture_path.with_suffix(suffix); plain_file(private, 32768); owned_records[private] = proof(private)
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            private = store / (capture + suffix); plain_file(private, None if suffix == '.tar.gz' else 16 * 1024**2); archive_proofs[private] = proof(private)
    PHASE = 'precise-owned-cleanup'
    assert not (fixture / 'large').exists()
    assert {path.name for path in fixture.iterdir()} == {'small', 'large-restored', '.whaledeck-files'} and proof(fixture)[:2] == identity
    assert {path.name for path in (fixture / '.whaledeck-files').iterdir()} == {capture + '.tree-trash' for capture in captures}
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(fixture) and not mount.startswith(str(fixture) + '/')
    fixture_proofs = {path: proof(path) for path in fixture.rglob('*')}
    assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in fixture_proofs.values())
    # Re-observe every exact object before unlink; no glob deletion or recursive rm.
    assert all(proof(path) == value for path, value in {**owned_records, **archive_proofs, **fixture_proofs}.items())
    for path, value in fixture_proofs.items():
        if stat.S_ISREG(value[2]): path.unlink()
    for path in sorted((path for path, value in fixture_proofs.items() if stat.S_ISDIR(value[2])), key=lambda path: len(path.parts), reverse=True): path.rmdir()
    fixture.rmdir()
    for path in [*owned_records, *archive_proofs]: path.unlink()
    assert not fixture.exists() and not any(path.exists() for path in [*owned_records, *archive_proofs])
    print(json.dumps({'installedTreeRestoringAcceptance': 'passed', 'sourceRevision': revision, 'clientSourceRevision': client_revision,
        'smallWholeBytes': result['smallBytes'], 'largeWholeBytes': result['largeBytes'], 'fullArchiveOriginalAndNewInodeOutputVerified': True,
        'signedUdsAndFixedSandboxHelper': True, 'differentSourceDevice': captured_devices,
        'exactOwnedSyntheticPayloadsAndReceiptsRemoved': True, 'bffWorkerHumanAuditAndUi': 'not-covered'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'installedTreeRestoringAcceptance': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'unknownObjectsPreserved': True, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
