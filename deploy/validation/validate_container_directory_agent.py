#!/usr/bin/env python3
"""Installed signed UDS + fixed root mkdir helper on one strictly owned fixture.

No deployment, production container lifecycle, human identity or database write.
The exact created directory must be proved empty and bound to its Root inode
before rmdir. Mismatching resources are preserved by the original fixture.
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
from validate_container_file_write_agent import trusted


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 3; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; probe = Path(sys.argv[2])
    assert probe.parent.parent == repo / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', probe.parent.name) and probe.name == 'TerminalProbe'
    trusted(probe); trusted(probe.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=probe.parent, check=True, capture_output=True, timeout=20)
    modules = ('whaledeck-container-directory-transport.py', 'whaledeck-container-directories.py', 'whaledeck-container-files.py',
        'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-privileged')
    digests = {}
    for module in modules:
        path = Path('/usr/local/libexec') / module; trusted(path)
        frozen = owned.common.command(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + module])
        assert path.read_bytes() == frozen; digests[module] = hashlib.sha256(frozen).hexdigest()
    state = Path('/var/lib/whaledeck-container-directories'); trusted(state, True)
    assert stat.S_IMODE(state.stat().st_mode) == 0o700
    evidence = repo / 'deploy/artifacts' / ('container-directory-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    core_before = owned.common.core_snapshot()
    with owned.fixture() as fixture:
        value = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        instance = str(value['State']['Pid']) + ':' + value['State']['StartedAt']
        parameters = {'revision': revision, 'containerId': fixture['identifier'], 'instanceId': instance,
            'coreId': core_before['/whaledeck-api'][0]}
        (evidence / 'baseline.json').write_text(json.dumps({**parameters, 'ownedSource': str(fixture['folder']), 'sourceSha256': digests}))
        result = subprocess.run([str(probe), 'container-directories'], input=json.dumps(parameters), text=True, capture_output=True, timeout=180)
        assert len(result.stdout.encode()) <= 16384 and len(result.stderr.encode()) <= 16384
        (evidence / 'rpc.stdout').write_text(result.stdout); (evidence / 'rpc.stderr').write_text(result.stderr)
        if result.returncode:
            # Only bounded structured phase/status, never raw credentials or
            # an arbitrary exception/log message from a privileged component.
            for line in result.stderr.splitlines():
                try:
                    item = json.loads(line)
                    if item.get('terminalProbe') == 'failed': print(json.dumps({k: item.get(k) for k in ('phase', 'rpcStatus', 'fileCode')}), flush=True)
                except ValueError: pass
        assert result.returncode == 0, 'installed directory protocol probe failed'
        rpc = json.loads(result.stdout); assert rpc['containerDirectoryProbe'] == 'passed'
        assert (rpc['revision'], rpc['containerId'], rpc['instanceId']) == (revision, fixture['identifier'], instance)
        records = {}
        for field, expected_state, path in (('completedOperation', 'Completed', 'work/新建目录'), ('canceledOperation', 'Aborted', 'work/取消目录')):
            operation = rpc[field]; assert re.fullmatch('[a-f0-9]{32}', operation)
            receipt = state / (operation + '.json'); info = trusted(receipt)
            assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 16384
            record = json.loads(receipt.read_bytes()); plan = record['plan']
            assert record['state'] == expected_state and record['operationId'] == operation and record['owner'] == 'B' * 64
            assert plan['schemaVersion'] == 1
            assert plan['command'] == {'containerId': fixture['identifier'], 'instanceId': instance, 'path': path, 'mode': '0750'}
            assert plan['uid'] == 1000 and plan['gid'] == 1000
            unsigned = {k: v for k, v in plan.items() if k != 'fingerprint'}
            assert plan['fingerprint'] == hashlib.sha256(b'whaledeck-container-directory-plan-v1\n' + json.dumps(unsigned,
                sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()
            (evidence / (field + '.json')).write_bytes(receipt.read_bytes()); records[field] = record
        output = fixture['folder'] / '新建目录'; info = output.lstat(); record = records['completedOperation']
        assert stat.S_ISDIR(info.st_mode) and output.resolve(strict=True) == output
        assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) == (1000, 1000, 0o750)
        assert [info.st_dev, info.st_ino] == record['pendingIdentity'] and record['version'] == rpc['version']
        assert not list(output.iterdir())
        canceled = fixture['folder'] / '取消目录'; assert not canceled.exists() and not canceled.is_symlink()
        private = fixture['folder'] / '.whaledeck-container-directories-v1'; private_info = trusted(private, True)
        assert stat.S_IMODE(private_info.st_mode) == 0o700 and not list(private.iterdir())
        assert [private_info.st_dev, private_info.st_ino] == record['privateIdentity']
        # No glob/recursive deletion. Only our exact verified empty objects.
        assert (output.lstat().st_dev, output.lstat().st_ino) == tuple(record['pendingIdentity']); output.rmdir(); private.rmdir()
    assert owned.common.core_snapshot() == core_before
    active = subprocess.run(['systemctl', 'list-units', '--all', '--no-legend', 'whaledeck-container-directories-*'], capture_output=True, text=True, timeout=10)
    assert active.returncode == 0 and not active.stdout.strip()
    verified = {'revision': revision, 'containerId': parameters['containerId'], 'ownedSource': str(fixture['folder']),
        'completedOperation': rpc['completedOperation'], 'canceledOperation': rpc['canceledOperation'],
        'installedSignedUdsAndFixedHelper': True, 'humanBffOrUi': False, 'rootRecordsRetained': True,
        'coreUnchanged': True, 'ownedFixtureRemoved': True, 'activeTransientUnits': 0}
    (evidence / 'verified.json').write_text(json.dumps(verified))
    for path in evidence.iterdir(): path.chmod(0o600)
    print(json.dumps({'evidence': str(evidence), 'installedDirectoryProtocol': 'passed', 'humanBffOrUi': False}), flush=True)


if __name__ == '__main__': main()
