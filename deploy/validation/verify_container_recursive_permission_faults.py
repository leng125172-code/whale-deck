#!/usr/bin/env python3
"""Read-only second observer of retained installed partial-cancellation evidence.

Source data has already been removed after the live gate's full reads; this
observer does not pretend to re-read deleted bytes. SQL/Root/HDD remain real.
"""
import hashlib
import datetime as dt
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from validate_container_file_staging import load
from validate_container_recursive_permission_browser import snapshot, read_metadata, managed_plan
from validate_container_recursive_permission_faults import PATHS, unchanged_except_modes, inactive_except, same_operation, require_unknown_counts
from validate_container_recursive_permission_agent import no_active_work, host_identity
from validate_container_recursive_permissions import NAMES
from validate_metric_continuity import query
import validate_container_file_publishing as owned
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
    kind = baseline.get('kind','cancel'); assert result.get('kind','cancel') == kind
    unknown_case = kind == 'target-disappearance'; expected_state = 'Unknown' if unknown_case else 'Partial'
    assert original['actualUi'] and recovery['actualUi'] and original['normalDualLogout'] and recovery['normalDualLogout']
    assert original['subject'] == row['actor_subject'] != row['acknowledged_by'] == recovery['subject']
    if kind == 'worker-crash':
        assert original['kind'] == kind and original['cancellations'] == 0
        assert original['registrations'] == original['confirmations'] == 1 and original['bodies'] == 0
        crash = saved('worker-crash.json'); lease = saved('natural-lease.json'); crashed = saved('crash-sql.json')['row']
        assert crash == result['workerCrash'] and crash['signal'] == 'SIGKILL' and crash['sameContainerAndImage']
        assert [crash['workerId'],crash['imageId']] == baseline['coreBefore']['/whaledeck-worker'][:2]
        assert crash['oldStartedAt'] != crash['newStartedAt']
        assert lease['originalLeaseToken'] == crashed['lease_token'] and lease['originalLeaseExpiresAtUtc'] == crashed['lease_expires_at_utc'] == crash['originalLeaseExpiresAtUtc']
        assert crashed['state'] == 'Applying' and crashed['publish_barrier'] and crashed['cancel_requested_at_utc'] is None
        same_operation(crashed,row)
        expiry = dt.datetime.fromisoformat(lease['originalLeaseExpiresAtUtc'])
        began = dt.datetime.fromisoformat(lease['waitBeganAtUtc']); ended = dt.datetime.fromisoformat(lease['waitEndedAtUtc'])
        assert ended >= expiry > began and abs((ended-began).total_seconds()-lease['monotonicWaitSeconds']) < 3
        samples = lease['samples']; assert len(samples) >= 3 and result['naturalLeaseSamples'] == len(samples)
        assert dt.datetime.fromisoformat(samples[-1]['atUtc'])-dt.datetime.fromisoformat(samples[0]['atUtc']) > dt.timedelta(seconds=15)
        cut_raw = (directory/'cut-root-original.json').read_bytes(); assert json.loads(cut_raw)['state'] == 'Applying'
        raw = (directory/'after-helper-exit-root.json').read_bytes()
        assert json.loads(raw)['state'] == lease['rootStateAfterHelperExit'] and lease['rootStateAfterHelperExit'] in ('Applying','Partial')
        assert hashlib.sha256(raw).hexdigest() == lease['rootSha256']
        previous = began
        for sample in samples:
            at = dt.datetime.fromisoformat(sample['atUtc']); assert previous <= at < expiry; previous = at
            assert sample['leaseToken'] == lease['originalLeaseToken'] and sample['leaseExpiresAtUtc'] == lease['originalLeaseExpiresAtUtc']
            assert sample['state'] == 'Applying' and sample['barrier'] and sample['sourceFingerprintsUnchanged'] and sample['rootSha256'] == lease['rootSha256']
        assert saved('physical-cut.json') == after and lease['clocksOrSqlModified'] is False
    assert partial['row']['state'] == expected_state and partial['row']['publish_barrier'] and partial['row']['completed_at_utc'] is None
    record = saved('partial.root.json'); operation = result['operationId']; assert operation == record['operationId']
    late = saved('late-apply.json')
    assert late['operationId'] == operation and late['state'] == expected_state and late['replayed']
    assert result['lateReplayWasSignedRpc'] is False
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):
        assert late[key] == record[key] == result['partialCounts'][key]
    plan = managed_plan(json.loads(row['plan_json'])); assert record['plan'] == plan
    if unknown_case:
        require_unknown_counts(record,len(PATHS))
        assert original['cancellations'] == 0 and original['registrations'] == original['confirmations'] == 1 and original['bodies'] == 0
        removed = saved('target-disappearance.json'); assert removed['containerId'] == baseline['containerId'] and removed['exactOwnedContainerRemoved'] and removed['sourcePreserved']
        assert result['targetRemovedDuringOperation'] and result['rootState'] == 'Unknown'
        assert saved('physical-cut.json') == after and 0 < unchanged_except_modes(before,after) == result['physicalChangedCount'] < len(PATHS)
        same_operation(saved('pre-disappearance-sql.json')['row'],row)
        assert saved('history-proof.json')['unknownAggregateDoesNotMeanNoPhysicalChanges']
    else:
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
        hdd_device = os.fstat(base).st_dev
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
    if unknown_case: assert outcomes is None and record['outcomeDigest'] == ''
    else: assert record['outcomeDigest'] == digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)
    audits = query('''SELECT json_build_object('rows',json_agg(json_build_object('actor',actor_subject,'action',action,'result',result,'detail',detail_json)))
      FROM audit_events WHERE job_id IN (:'id'::uuid,:'execution'::uuid) AND action LIKE 'containers.files.recursive-permission.%';''', id=row['id'],execution=row['execution_job_id'])['rows']
    acknowledgement = '.unknown-acknowledged' if unknown_case else '.partial-acknowledged'
    assert audits and len([a for a in audits if a['action'].endswith(acknowledgement)]) == 1
    expected = {'intentId':row['id'],'containerId':row['container_id'],
        'instanceHash':hashlib.sha256(row['instance_id'].encode()).hexdigest().upper(),
        'pathHash':hashlib.sha256(row['relative_path'].encode()).hexdigest().upper()}
    for audit in audits:
        detail = json.loads(audit['detail']) if isinstance(audit['detail'],str) else audit['detail']
        assert detail == expected
        if audit['action'].endswith(acknowledgement):
            assert audit['actor'] == row['acknowledged_by'] and audit['result'] == expected_state
        else: assert audit['actor'] == row['actor_subject']
    (directory/'audit-proof.json').write_text(json.dumps({'rows':len(audits),'differentAdminAcknowledgement':1,'originalSubjectPreserved':True,'onlyRecomputedHashedMetadata':True}))
    repo = Path(__file__).resolve().parents[2]; installed = {}
    for name in (*NAMES,'whaledeck-container-recursive-permission-transport.py','whaledeck-privileged'):
        path = Path('/usr/local/libexec')/name; info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o750
        frozen = subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+name])
        assert path.read_bytes() == frozen; installed[name] = hashlib.sha256(frozen).hexdigest()
    current_core = owned.common.core_snapshot()
    assert current_core.keys() == baseline['coreBefore'].keys()
    actual_containers = json.loads(owned.common.command(['docker','inspect',*owned.common.CORE_NAMES]))
    assert len(actual_containers) == 11 and all(c['State']['Health']['Status'] == 'healthy' for c in actual_containers)
    for name, before_core in baseline['coreBefore'].items():
        observed_core = list(current_core[name])
        if name == '/whaledeck-worker': assert observed_core[:3] == before_core[:3] and observed_core[3] != before_core[3]
        else: assert observed_core == before_core
    if kind == 'worker-crash': assert current_core['/whaledeck-worker'][3] == crash['newStartedAt']
    (directory/'installed-and-core-proof.json').write_text(json.dumps({'installedSha256':installed,'all11Healthy':True,'sameCoreContainersAndImages':True,'onlyWorkerStartedAtChanged':True,'databasePlatformEightUnchanged':True}))
    assert not Path(baseline['source']).exists() and not Path(baseline['root']).exists()
    assert subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15).returncode != 0
    assert host_identity(baseline['revision']) == baseline['hostBefore']
    no_active_work(); inactive_except()
    screenshots = (('inflight.png','unknown-counts.png','partial-recovery-confirm.png','partial-acknowledged.png') if unknown_case
        else ('inflight.png','partial.png','partial-recovery-confirm.png','partial-acknowledged.png')) + (('partial-counts.png',) if kind == 'worker-crash' else ())
    for name in screenshots:
        raw = (directory/name).read_bytes(); assert raw.startswith(b'\x89PNG\r\n\x1a\n')
        assert int.from_bytes(raw[16:20],'big') == 320 and int.from_bytes(raw[20:24],'big') == 600
    observed = {'independentReadOnlyVerification':'passed','id':row['id'],'actualSqlAndRootAndHddUnchanged':True,'hddMetadataFiles':len(checked),
        'kind':kind,'sourceAndContainerAccuratelyRemoved':True,'deletedSourceBytesReRead':False,'screenshots':len(screenshots),'auditRows':len(audits),
        'all11Healthy':True,'installedHelpersMatchProductRevision':True,'productRevision':baseline['revision'],
        'sourceDevice':baseline['original'][''][0],'hddDevice':hdd_device}
    (directory/'independent-final.json').write_text(json.dumps(observed))
    print(json.dumps(observed))


if __name__ == '__main__': main()
