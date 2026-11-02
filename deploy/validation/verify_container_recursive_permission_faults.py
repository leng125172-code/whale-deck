#!/usr/bin/env python3
"""Read-only second observer of retained installed partial-cancellation evidence.

Source data has already been removed after the live gate's full reads; this
observer does not pretend to re-read deleted bytes. SQL/Root/HDD remain real.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from validate_container_file_staging import load
from validate_container_recursive_permission_browser import snapshot, read_metadata, managed_plan
from validate_container_recursive_permission_faults import PATHS, unchanged_except_modes, inactive_except
from validate_container_recursive_permission_agent import no_active_work, host_identity
from verify_container_recursive_permissions import digest, unsigned


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    assert re.fullmatch('container-recursive-permission-faults-[a-f0-9]{32}',sys.argv[1])
    directory = Path(__file__).resolve().parents[1]/'artifacts'/sys.argv[1]
    info = directory.lstat(); assert stat.S_ISDIR(info.st_mode) and not directory.is_symlink() and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    def saved(name):
        path = directory/name; info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 16*1024**2
        return json.loads(path.read_bytes())
    baseline = saved('baseline.json'); result = saved('verified.json'); partial = saved('partial-sql.json'); final = saved('final-sql.json')
    actual = snapshot(result['id']); assert actual == final
    before = baseline['original']; after = saved('physical-partial.json'); assert after == saved('physical-final.json')
    row = actual['row']; assert row['state'] == 'Acknowledged' and actual['executionJob']['state'] == 'Failed' and not row['publish_barrier']
    original = saved('cancel-browser.json'); recovery = saved('recovery-browser.json')
    assert original['actualUi'] and recovery['actualUi'] and original['normalDualLogout'] and recovery['normalDualLogout']
    assert original['subject'] == row['actor_subject'] != row['acknowledged_by'] == recovery['subject']
    assert partial['row']['state'] == 'Partial' and partial['row']['publish_barrier'] and partial['row']['completed_at_utc'] is None
    record = saved('partial.root.json'); operation = result['operationId']; assert operation == record['operationId']
    late = saved('late-apply.json')
    assert late['operationId'] == operation and late['state'] == 'Partial' and late['replayed']
    assert result['lateReplayWasSignedRpc'] is False
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):
        assert late[key] == record[key] == result['partialCounts'][key]
    plan = managed_plan(json.loads(row['plan_json'])); assert record['plan'] == plan
    assert record['state'] == 'Partial' and record['unknownCount'] == 0 and record['notStartedCount'] > 0
    assert unchanged_except_modes(before,after) == record['changedCount'] == record['completedCount']
    assert sum(record[k] for k in ('completedCount','abortedCount','notStartedCount','unknownCount')) == len(PATHS)
    root = Path('/var/lib/whaledeck-container-recursive-permissions')
    for name in ['partial.root.json', *[p.name for p in directory.glob('*.root.json') if p.name != 'partial.root.json']]:
        path = root/(operation+'.json') if name == 'partial.root.json' else root/(operation+'.entries')/name.replace('.root.json','.json')
        info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
        assert path.read_bytes() == (directory/name).read_bytes()
    files = load('/usr/local/libexec/whaledeck-files.py','recursive_fault_verify_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_fault_verify_hdd').FileBackups(files)
    checked = []
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd = storage.f.open_at(base,'container-recursive-permissions-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            for artifact in directory.glob('*.hdd.json'):
                name = operation+'.json' if artifact.name == 'partial.hdd.json' else operation+'/entries/'+artifact.name.replace('.hdd.json','.json')
                raw = read_metadata(files,storage,configuration,fd,name); assert raw == artifact.read_bytes(); checked.append(artifact.name)
        finally: os.close(fd)
    history = saved('partial.hdd.json')
    assert history['historyDigest'] == row['history_digest'] == digest(b'whaledeck-container-recursive-permission-history-v1\n',unsigned(history,'historyDigest'))
    assert history['plan'] == plan and plan['manifestDigest'] == digest(b'whaledeck-container-recursive-permission-manifest-v1\n',history['entries'])
    outcomes = saved('history-proof.json')['outcomes']
    assert record['outcomeDigest'] == digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)
    assert not Path(baseline['source']).exists() and not Path(baseline['root']).exists()
    assert subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15).returncode != 0
    assert host_identity(baseline['revision']) == baseline['hostBefore']
    no_active_work(); inactive_except()
    for name in ('inflight.png','partial.png','partial-recovery-confirm.png','partial-acknowledged.png'):
        raw = (directory/name).read_bytes(); assert raw.startswith(b'\x89PNG\r\n\x1a\n')
        assert int.from_bytes(raw[16:20],'big') == 320 and int.from_bytes(raw[20:24],'big') == 600
    observed = {'independentReadOnlyVerification':'passed','id':row['id'],'actualSqlAndRootAndHddUnchanged':True,'hddMetadataFiles':len(checked),
        'sourceAndContainerAccuratelyRemoved':True,'deletedSourceBytesReRead':False,'screenshots':4,'productRevision':baseline['revision']}
    (directory/'independent-final.json').write_text(json.dumps(observed))
    print(json.dumps(observed))


if __name__ == '__main__': main()
