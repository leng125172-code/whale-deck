#!/usr/bin/env python3
"""Installed signed UDS/copy helper against one exact owned Docker fixture.

No human identity, BFF/UI, production container lifecycle or SQL writes.
Permanent Root evidence remains; only fully proved synthetic objects are removed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import validate_container_file_publishing as owned
from validate_container_copy_intents import remove_tree
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 3; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; probe = Path(sys.argv[2])
    assert probe.parent.parent == repo / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', probe.parent.name) and probe.name == 'TerminalProbe'
    trusted(probe); trusted(probe.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=probe.parent, capture_output=True, check=True, timeout=20)
    modules = ('whaledeck-container-copy-transport.py', 'whaledeck-container-copies.py', 'whaledeck-container-copy-scope.py',
        'whaledeck-container-files.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-operations.py',
        'whaledeck-tree-copies.py', 'whaledeck-file-tree.py', 'whaledeck-privileged')
    digests = {}
    for module in modules:
        path = Path('/usr/local/libexec') / module; trusted(path)
        frozen = owned.common.command(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + module])
        assert path.read_bytes() == frozen; digests[module] = hashlib.sha256(frozen).hexdigest()
    state = Path('/var/lib/whaledeck-container-copies'); trusted(state, True); assert stat.S_IMODE(state.stat().st_mode) == 0o700
    reader = load('whaledeck-container-files.py', 'installed_copy_probe_reader')
    evidence = repo / 'deploy/artifacts' / ('container-copy-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    before = owned.common.core_snapshot()
    with owned.fixture() as fixture:
        folder = fixture['folder']; tree = folder / 'source-tree'; tree.mkdir(mode=0o750); (tree / 'empty').mkdir(mode=0o700)
        (tree / 'code.bin').write_bytes(fixture['old']); (tree / 'code.bin').chmod(0o640)
        for child in (tree / 'code.bin', tree / 'empty', tree):
            os.chown(child, 1000, 1000); os.utime(child, ns=(1700000000123456789, 1700000000123456789))
        tree_proof = reader.fingerprint(tree.stat())
        container = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        instance = str(container['State']['Pid']) + ':' + container['State']['StartedAt']
        location = {'containerId': fixture['identifier'], 'instanceId': instance}
        parameters = {'revision': revision, **location, 'coreId': before['/whaledeck-api'][0],
            'sourceVersion': reader.version(location, 'work/original.bin', (folder / 'original.bin').stat()),
            'treeSourceVersion': reader.version(location, 'work/source-tree', tree.stat())}
        (evidence / 'baseline.json').write_text(json.dumps({**parameters, 'ownedSource': str(folder),
            'sourceSha256': digests, 'originalProof': fixture['proofs']['original.bin'], 'treeProof': tree_proof}))
        result = subprocess.run([str(probe), 'container-copies'], input=json.dumps(parameters), text=True, capture_output=True, timeout=330)
        assert len(result.stdout.encode()) <= 16384 and len(result.stderr.encode()) <= 16384
        (evidence / 'rpc.stdout').write_text(result.stdout); (evidence / 'rpc.stderr').write_text(result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    value = json.loads(line)
                    if value.get('terminalProbe') == 'failed': print(json.dumps({k: value.get(k) for k in ('phase', 'rpcStatus', 'fileCode')}), flush=True)
                except ValueError: pass
        assert result.returncode == 0, 'installed copy protocol probe failed'
        rpc = json.loads(result.stdout); assert rpc['containerCopyProbe'] == 'passed' and rpc['revision'] == revision
        assert (rpc['containerId'], rpc['instanceId']) == (fixture['identifier'], instance) and len(rpc['completed']) == 2
        completed = []
        for value in rpc['completed']:
            operation = value['operationId']; assert re.fullmatch('[a-f0-9]{32}', operation)
            path = state / (operation + '.json'); trusted(path); raw = path.read_bytes(); record = json.loads(raw)
            assert record['state'] == 'Completed' and record['operationId'] == operation and record['owner'] == 'B' * 64
            assert record['plan']['kind'] == value['kind'] and record['plan']['fingerprint'] == value['fingerprint'] and record['version'] == value['version']
            command = record['plan']['command']; assert command['containerId'] == fixture['identifier'] and command['instanceId'] == instance
            assert (record['plan']['uid'], record['plan']['gid']) == (1000, 1000)
            output = folder / ('复制文件.bin' if value['kind'] == 'File' else '复制目录'); info = output.lstat()
            assert command['targetPath'] == 'work/' + output.name
            assert list(reader.fingerprint(info)) == record['publicationIdentity'] and record['version'] == reader.version(location, command['targetPath'], info)
            assert (info.st_uid, info.st_gid) == (1000, 1000)
            if value['kind'] == 'File':
                original = (folder / 'original.bin').stat()
                assert stat.S_IMODE(info.st_mode) == 0o640 and info.st_mtime_ns == original.st_mtime_ns and output.read_bytes() == fixture['old']
                assert reader.fingerprint(output.lstat()) == reader.fingerprint(info); output.unlink()
            else:
                for relative in ('', 'empty', 'code.bin'):
                    old, new = (tree / relative).stat(), (output / relative).stat()
                    assert (new.st_uid, new.st_gid, stat.S_IMODE(new.st_mode), new.st_mtime_ns) == (1000, 1000, stat.S_IMODE(old.st_mode), old.st_mtime_ns)
                remove_tree(output, record['publicationIdentity'][:2], {'code.bin': 'File', 'empty': 'Directory'}, reader, fixture['old'])
            (evidence / (value['kind'] + '-root.json')).write_bytes(raw); completed.append(operation)
        operation = rpc['canceledOperation']; assert re.fullmatch('[a-f0-9]{32}', operation)
        canceled_path = state / (operation + '.json'); trusted(canceled_path); canceled = json.loads(canceled_path.read_bytes())
        assert canceled['state'] == 'Aborted' and canceled['plan']['command']['targetPath'] == 'work/取消复制.bin'
        assert not (folder / '取消复制.bin').exists(); (evidence / 'canceled-root.json').write_bytes(canceled_path.read_bytes())
        assert reader.fingerprint(tree.stat()) == tree_proof
        remove_tree(tree, list(tree_proof[:2]), {'code.bin': 'File', 'empty': 'Directory'}, reader, fixture['old'])
        private = folder / '.whaledeck-container-copies-v1'; trusted(private, True)
        assert stat.S_IMODE(private.stat().st_mode) == 0o700 and not list(private.iterdir()); private.rmdir()
    assert owned.common.core_snapshot() == before
    active = subprocess.run(['systemctl', 'list-units', '--all', '--no-legend', 'whaledeck-container-copies-*.service'], capture_output=True, text=True, check=True, timeout=20)
    assert not active.stdout.strip()
    verified = {'revision': revision, 'completedOperations': completed, 'canceledOperation': operation,
        'installedSignedUdsAndFixedHelper': True, 'humanBffOrUi': False, 'rootRecordsRetained': True,
        'coreUnchanged': True, 'ownedFixtureRemoved': True, 'activeTransientUnits': 0}
    (evidence / 'verified.json').write_text(json.dumps(verified))
    for path in evidence.iterdir(): path.chmod(0o600)
    print(json.dumps({'evidence': str(evidence), 'installedCopyProtocol': 'passed', 'humanBffOrUi': False}), flush=True)


if __name__ == '__main__': main()
