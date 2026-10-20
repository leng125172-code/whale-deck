#!/usr/bin/env python3
"""Installed signed UDS/helper + independent full materialization observer.

Only this invocation's inode-proven fixture and six known test receipts may be
removed. Failures preserve evidence; no rollback, production mutation or human
BFF/UI claim. The native client uses the Agent, never Python engine functions.
"""
from decimal import Decimal
import hashlib
import io
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
from validate_file_tree_rpc import client_failure

REPOSITORY = Path('/data/GitRepos/whale-deck')
STATE = Path('/var/lib/whaledeck-files/tree-unpacking')
PHASE = 'startup'


def run(arguments):
    # Read-only attestation must not refresh a user-owned Git index as root.
    return subprocess.check_output(arguments, text=True, stderr=subprocess.PIPE, timeout=30,
        env={**os.environ, 'GIT_OPTIONAL_LOCKS': '0'}).strip()


def validation_only_changes(git, runtime_revision, client_revision):
    # Disable rename detection: src/x -> docs/x must include the src deletion.
    changes = run(git + ['diff', '--no-renames', '--name-only', runtime_revision, client_revision, '--']).splitlines()
    # This single CI workflow controls validation only. It is not consumed by
    # the workstation's local frozen source/image builder. No other workflow,
    # dependency, installer or product/build input is classified as QA-only.
    return all(path.startswith(('deploy/validation/', 'docs/')) or path in ('.project-atlas.json', '.github/workflows/ci.yml') for path in changes)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) in (3, 4)
    binary, client_revision = Path(sys.argv[1]), sys.argv[2]
    revision = sys.argv[3] if len(sys.argv) == 4 else client_revision
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (client_revision, revision))
    assert binary.parent.parent == REPOSITORY / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[a-zA-Z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    assert binary.resolve() == binary and stat.S_ISREG(binary.lstat().st_mode)
    git = ['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY)]
    assert run(git + ['rev-parse', 'HEAD']) == client_revision and not run(git + ['status', '--porcelain'])
    # A validation-only correction need not rebuild unchanged product images.
    # Check the ENTIRE diff with an allowlist, not a partial product path list.
    # Distinct client/runtime revisions stay explicit in the final evidence.
    assert validation_only_changes(git, revision, client_revision)
    assert json.loads(run(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == revision
    for name in ('whaledeck-files.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-tree-unpacking.py', 'whaledeck-privileged'):
        installed = Path('/usr/local/libexec') / name; info = installed.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and not info.st_mode & 0o022
        assert hashlib.sha256(installed.read_bytes()).digest() == hashlib.sha256((REPOSITORY / 'deploy/host' / name).read_bytes()).digest()
    root = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert root['path'] == str(DIRECTORY) and {'files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-archive', 'files-tree-unpack'} <= set(root['allowedActions'])
    assert DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    capacity = shutil.disk_usage(DIRECTORY)
    assert capacity.free >= max(6 * 1024**3, capacity.total // 20 + 4 * 1024**3)
    os.umask(0o077)
    fixture = DIRECTORY / ('wdv-agent-unpack-' + uuid.uuid4().hex[:16]); fixture.mkdir(mode=0o700)
    identity = proof(fixture)[:2]; print(json.dumps({'ownedAgentUnpackingFixture': fixture.name}), flush=True)
    data = bytes(range(256)) * 8192 + b'TAIL\x00\xff!'
    small = fixture / 'small.bin'; small.write_bytes(data)
    large = fixture / 'large.bin'
    with large.open('wb') as stream:
        stream.truncate(1024**3 + 7); stream.seek(1024**3); stream.write(b'TAIL\x00\xff!'); stream.flush(); os.fsync(stream.fileno())

    def archive(path, standard, content, member_name):
        with tarfile.open(path, 'w:gz' if path.name.endswith('.gz') else 'w', format=standard) as writer:
            for name, mode in (('.', 0o750), ('空目录', 0o710), *((('代码', 0o750),) if '/' in member_name else ())):
                entry = tarfile.TarInfo(name); entry.type = tarfile.DIRTYPE; entry.mode = mode
                entry.uid, entry.gid, entry.mtime = 12345, 23456, 1712345678
                if standard == tarfile.PAX_FORMAT: entry.pax_headers['mtime'] = '1712345678.123456789'
                writer.addfile(entry)
            entry = tarfile.TarInfo(member_name); entry.size = content.stat().st_size; entry.mode = 0o6751
            entry.uid, entry.gid, entry.mtime = 12345, 23456, 1712345678
            if standard == tarfile.PAX_FORMAT: entry.pax_headers['mtime'] = '1712345678.123456789'
            with content.open('rb') as stream: writer.addfile(entry, stream)

    inputs = []
    for standard, kind in ((tarfile.PAX_FORMAT, 'pax'), (tarfile.GNU_FORMAT, 'gnu')):
        for suffix in ('tar', 'tar.gz'):
            path = fixture / (kind + '.' + suffix)
            archive(path, standard, small, '代码/' + ('中文' * 35 if kind == 'gnu' else '中文') + '.bin'); inputs.append(path)
    archive(fixture / 'large.tar', tarfile.PAX_FORMAT, large, 'large.bin'); inputs.append(fixture / 'large.tar')
    for name, member in (('protected.tar', 'nested/.env'), ('traversal.tar', '../escaped')):
        path = fixture / name
        with tarfile.open(path, 'w', format=tarfile.PAX_FORMAT) as writer:
            entry = tarfile.TarInfo(member); entry.size = 1; writer.addfile(entry, io.BytesIO(b'x'))
        inputs.append(path)
    originals = {path.name: proof(path) for path in [small, large, *inputs]}
    environment = os.environ.copy(); environment['WHALEDECK_UNPACK_PROBE_RELATIVE_PATH'] = fixture.name
    PHASE = 'installed-signed-grpc-fixed-helper'
    executed = subprocess.run([str(binary), 'tree-unpack'], env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=1800)
    rows = [json.loads(line) for line in executed.stdout.splitlines() if line.strip()]
    if executed.returncode or len(rows) != 1 or rows[0].get('installedTreeUnpackingRpc') != 'passed':
        for failure in client_failure(executed.stdout, executed.stderr): print(json.dumps(failure), flush=True)
        raise RuntimeError('installed-probe-failed')
    result = rows[0]
    assert result['smallBytes'] == len(data) and result['largeBytes'] == 1024**3 + 7
    assert {name: proof(fixture / name) for name in originals} == originals
    completed, canceled = result['completedOperationIds'], result['canceledOperationIds']
    assert len(completed) == 5 and len(canceled) == 1 and len(set(completed + canceled)) == 6
    assert all(isinstance(value, str) and re.fullmatch('[a-f0-9]{32}', value) for value in completed + canceled)
    PHASE = 'independent-full-disk-membership-and-receipts'
    targets = {'pax-tar', 'pax-tar.gz', 'gnu-tar', 'gnu-tar.gz', 'large-output'}
    receipts, output_proofs = [], {}
    for operation in completed + canceled:
        path = STATE / (operation + '.json'); info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 32768
        record = json.loads(path.read_bytes()); assert record['operationId'] == operation
        assert record['state'] == ('Completed' if operation in completed else 'Aborted')
        command = record['plan']['command']; source = Path(command['source']['path']); target = Path(command['target']['path'])
        assert command['source']['rootId'] == ROOT and command['target']['rootId'] == ROOT and source.parent == target.parent == Path(fixture.name)
        assert source.name in {item.name for item in inputs} and originals[source.name][-1] == record['plan']['archiveSha256'] and originals[source.name][6] == record['plan']['archiveBytes']
        receipts.extend((path, STATE / (operation + '.lock')))
        if operation in canceled:
            assert target.name == 'canceled' and not record.get('mayHaveStaged') and not (fixture / target.name).exists(); continue
        assert target.name in targets; output = fixture / target.name
        assert proof(output)[:2] == tuple(record['newIdentity'])
        expected_paths = set()
        with tarfile.open(fixture / source.name) as reader:
            for entry in reader.getmembers():
                relative = '' if entry.name.rstrip('/') in ('', '.') else entry.name.rstrip('/')
                assert entry.isdir() or entry.isfile(); expected_paths.add(relative)
                restored = output / relative; value = restored.lstat()
                assert (stat.S_ISDIR(value.st_mode) if entry.isdir() else stat.S_ISREG(value.st_mode)) and value.st_dev == identity[0]
                assert (value.st_uid, value.st_gid, stat.S_IMODE(value.st_mode), value.st_mtime_ns) == (root.get('ownerUid', 0), root.get('ownerGid', 0), entry.mode & 0o777, int(Decimal(entry.pax_headers.get('mtime', str(entry.mtime))) * 1000000000))
                if entry.isfile():
                    expected = originals['large.bin' if source.name == 'large.tar' else 'small.bin'][-1]
                    assert value.st_nlink == 1 and value.st_size == entry.size and proof(restored)[-1] == expected
                    with reader.extractfile(entry) as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == expected
        found = {''} | {str(path.relative_to(output)) for path in output.rglob('*')}
        assert found == expected_paths
        output_proofs.update({str(path.relative_to(fixture)): proof(path) for path in [output, *output.rglob('*')]})
    assert {name: proof(fixture / name) for name in originals} == originals
    PHASE = 'precise-owned-cleanup'
    assert proof(fixture)[:2] == identity and fixture.resolve() == fixture
    assert {path.name for path in fixture.iterdir()} == set(originals) | targets | {'.whaledeck-files'}
    private = fixture / '.whaledeck-files'; info = private.lstat()
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700 and not any(private.iterdir())
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(fixture) and not mount.startswith(str(fixture) + '/')
    for path in receipts:
        info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
    assert {name: proof(fixture / name) for name in output_proofs} == output_proofs
    # Only known regular files and then exact empty directories, no recursive rm.
    for name, value in output_proofs.items():
        if stat.S_ISREG(value[2]): (fixture / name).unlink()
    for name in sorted((name for name, value in output_proofs.items() if stat.S_ISDIR(value[2])), key=lambda name: (name.count('/'), name), reverse=True):
        (fixture / name).rmdir()
    for name in originals: (fixture / name).unlink()
    for path in receipts: path.unlink()
    private.rmdir(); fixture.rmdir()
    print(json.dumps({'installedTreeUnpackingAcceptance': 'passed', 'sourceRevision': revision, 'clientSourceRevision': client_revision,
        'productDiffEmpty': True, 'smallWholeBytes': len(data), 'largeWholeBytes': 1024**3 + 7,
        'bothFormatsAndPaxGnu': True, 'independentArchiveSourceAndFullOutputProofs': True, 'signedUdsAndRealHelper': True,
        'exactOwnedFixtureAndReceiptsRemoved': True, 'bffWorkerHumanAuditAndUi': 'not-covered'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'installedTreeUnpackingAcceptance': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'unknownObjectsPreserved': True, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
