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
from validate_container_recursive_ownership_browser import snapshot, read_metadata, managed_plan
from validate_container_recursive_ownership_faults import SOURCES, PATHS, unchanged_except_owners, inactive_except, same_operation, require_unknown_counts, require_waiting_deferred, require_agent_restart, require_stopped_tree, require_authority_change, require_logout_authority
from validate_container_directory_browser import snapshot as directory_snapshot, ROOT as DIRECTORY_ROOT
from validate_container_recursive_ownership_agent import host_identity
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership import NAMES
from validate_metric_continuity import query
import validate_container_file_publishing as owned
from verify_container_recursive_permissions import digest, unsigned


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    assert re.fullmatch('container-recursive-ownership-faults-[a-f0-9]{32}',sys.argv[1])
    directory = Path(__file__).resolve().parents[1]/'artifacts'/sys.argv[1]
    info = directory.lstat(); assert stat.S_ISDIR(info.st_mode) and not directory.is_symlink() and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    def saved(name):
        path = directory/name; info = path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 16*1024**2
        return json.loads(path.read_bytes())
    baseline = saved('baseline.json'); result = saved('verified.json'); partial = saved('partial-sql.json'); final = saved('final-sql.json')
    repo=Path(__file__).resolve().parents[2]
    assert set(baseline['sourceSha256']) == set(SOURCES)
    for name,sha in baseline['sourceSha256'].items():
        frozen=subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':deploy/validation/'+name])
        assert hashlib.sha256(frozen).hexdigest()==sha
    actual = snapshot(result['id']); assert actual == final
    before = baseline['original']; after = saved('physical-partial.json'); assert after == saved('physical-final.json')
    row = actual['row']; assert row['state'] == 'Acknowledged' and actual['executionJob']['state'] == 'Failed' and not row['publish_barrier']
    original = saved('cancel-browser.json'); recovery = saved('recovery-browser.json')
    kind = baseline.get('kind','cancel'); assert result.get('kind','cancel') == kind
    barrier_case = kind == 'target-disappearance-barrier'
    revoked = kind in ('group-removal','account-disable')
    logout_case = kind == 'logout'
    unknown_case = kind in ('target-disappearance','target-disappearance-barrier'); expected_state = 'Unknown' if unknown_case else 'Partial'
    assert original['actualUi'] and recovery['actualUi'] and recovery['normalDualLogout']
    if revoked:
        authority = saved('authority-change.json'); require_authority_change(authority,saved('ready.json'),kind)
        assert original['authorityRevoked'] and not original['normalDualLogout'] and original['oldTaskAndHintRemoved']
        assert result['authorityRevoked'] and original['kind'] == kind and original['deniedIntentStatus'] in (401,403)
        assert recovery['actualCountersVerified'] is True
        assert original['cancellations'] == original['bodies'] == 0 and original['registrations'] == original['confirmations'] == 1
        if kind == 'group-removal': assert original['currentIdentityStatus'] == 200 and original['currentIdentityAdministrator'] is False and original['renderedRevocationPage'] == 'forbidden'
        else: assert original['currentIdentityStatus'] in (401,403) and original['renderedRevocationPage'] == 'login'
        assert row['cancel_requested_at_utc'] is None and 0 <= result['productStoppedHelperSeconds'] < 15
        stopped = saved('physical-after-helper-exit.json'); cut_record = saved('cut-root.json')
        require_stopped_tree(saved('physical-cut.json'),stopped,cut_record['finishedThrough'],cut_record['startedThrough'])
        assert stopped == after; same_operation(saved('pre-authority-sql.json')['row'],row)
    else: assert original['normalDualLogout']
    if kind == 'cancel':
        assert original['registrations']==original['confirmations']==original['cancellations']==1 and original['bodies']==0
        assert recovery['actualCountersVerified'] is True
    if logout_case:
        authority = saved('logout-authority.json'); require_logout_authority(authority,original['subject'])
        instance = authority['containerInstance']
        assert instance['containerId'] == baseline['containerId'] and instance['pid'] == baseline['containerPid'] and instance['running'] is True
        assert str(instance['pid'])+':'+instance['startedAt'] == baseline['instanceId'] and not result['targetRemovedDuringOperation']
        assert original['kind'] == kind and original['oldTaskAndHintRemoved'] and original['deniedIntentStatus'] == 401
        assert original['registrations'] == original['confirmations'] == 1 and original['cancellations'] == original['bodies'] == 0
        assert row['cancel_requested_at_utc'] is None and result['normalLogoutWhileIdentityActive'] and not result['authorityRevoked']
        assert recovery['actualCountersVerified'] and 0 <= result['productStoppedHelperSeconds'] < 15
        began = dt.datetime.fromisoformat(original['logoutBeganAtUtc']); ended = dt.datetime.fromisoformat(original['logoutEndedAtUtc'])
        assert began < ended <= dt.datetime.fromisoformat(authority['observedAfterLogoutAtUtc'])
        assert dt.datetime.fromisoformat(saved('ready.json')['permissionExpiresAtUtc']) > ended + dt.timedelta(hours=1)
        stopped = saved('physical-after-helper-exit.json'); cut_record = saved('cut-root.json')
        require_stopped_tree(saved('physical-cut.json'),stopped,cut_record['finishedThrough'],cut_record['startedThrough'])
        assert stopped == after; same_operation(saved('pre-logout-sql.json')['row'],row)
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
    assert [plan['command']['uid'],plan['command']['gid']]==[2200,3200]
    if unknown_case:
        require_unknown_counts(record,len(PATHS))
        assert original['cancellations'] == 0 and original['registrations'] == original['confirmations'] == 1 and original['bodies'] == 0
        removed = saved('target-disappearance.json'); assert removed['containerId'] == baseline['containerId'] and removed['exactOwnedContainerRemoved'] and removed['sourcePreserved']
        assert result['targetRemovedDuringOperation'] and result['rootState'] == 'Unknown'
        assert saved('physical-cut.json') == after and 0 < unchanged_except_owners(before,after) == result['physicalChangedCount'] < len(PATHS)
        same_operation(saved('pre-disappearance-sql.json')['row'],row)
        assert saved('history-proof.json')['unknownAggregateDoesNotMeanNoPhysicalChanges']
    else:
        assert record['state'] == 'Partial' and record['unknownCount'] == 0 and record['notStartedCount'] > 0
        assert unchanged_except_owners(before,after) == record['changedCount'] == record['completedCount']
    assert sum(record[k] for k in ('completedCount','abortedCount','notStartedCount','unknownCount')) == len(PATHS)
    root = Path('/var/lib/whaledeck-container-recursive-ownership')
    for name in ['partial.root.json', *[p.name for p in directory.glob('*.root.json') if p.name != 'partial.root.json']]:
        path = root/(operation+'.json') if name == 'partial.root.json' else root/(operation+'.entries')/name.replace('.root.json','.json')
        info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
        assert path.read_bytes() == (directory/name).read_bytes()
    files = load('/usr/local/libexec/whaledeck-files.py','recursive_fault_verify_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_fault_verify_hdd').FileBackups(files)
    checked = []
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        hdd_device = os.fstat(base).st_dev
        fd = storage.f.open_at(base,'container-recursive-ownership-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            for artifact in directory.glob('*.hdd.json'):
                name = operation+'.json' if artifact.name == 'partial.hdd.json' else operation+'/entries/'+artifact.name.replace('.hdd.json','.json')
                raw = read_metadata(files,storage,configuration,fd,name); assert raw == artifact.read_bytes(); checked.append(artifact.name)
        finally: os.close(fd)
    history = saved('partial.hdd.json')
    assert history['historyDigest'] == row['history_digest'] == digest(b'whaledeck-container-recursive-ownership-history-v1\n',unsigned(history,'historyDigest'))
    assert history['plan'] == plan and plan['manifestDigest'] == digest(b'whaledeck-container-recursive-ownership-manifest-v1\n',history['entries'])
    outcomes = saved('history-proof.json')['outcomes']
    if unknown_case: assert outcomes is None and record['outcomeDigest'] == ''
    else: assert record['outcomeDigest'] == digest(b'whaledeck-recursive-ownership-outcomes-v1\n',outcomes)
    audits = query('''SELECT json_build_object('rows',json_agg(json_build_object('actor',actor_subject,'action',action,'result',result,'detail',detail_json)))
      FROM audit_events WHERE job_id IN (:'id'::uuid,:'execution'::uuid) AND action LIKE 'containers.files.recursive-ownership.%';''', id=row['id'],execution=row['execution_job_id'])['rows']
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
    for name in (*NAMES,'whaledeck-container-recursive-ownership-transport.py','whaledeck-privileged'):
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
    if kind == 'agent-restart':
        assert installed == baseline['installedHelpersBefore']
        restart = saved('agent-restart.json'); after_host = saved('host-after.json')
        assert restart == result['agentRestart'] and restart['before'] == baseline['hostBefore'] and restart['after'] == after_host
        require_agent_restart(baseline['hostBefore'],after_host,baseline['revision']); assert host_identity(baseline['revision']) == after_host
        assert restart['onlyFixedAgentServiceRestarted'] and all(n == 0 for n in restart['otherActiveWork'].values())
        began = dt.datetime.fromisoformat(restart['restartBeganAtUtc']); ended = dt.datetime.fromisoformat(restart['restartEndedAtUtc'])
        samples = restart['maintenanceSamples']; assert len(samples) >= 3 and began < ended
        assert dt.datetime.fromisoformat(samples[0]['atUtc']) < began and dt.datetime.fromisoformat(samples[-1]['atUtc']) > ended
        previous = None
        for sample in samples:
            at = dt.datetime.fromisoformat(sample['atUtc']); assert previous is None or at >= previous; previous = at
            assert sample['httpStatus'] == 200 and sample['state'] == 'healthy' and sample['phase'] == 'Ready' and sample['noStore'] and 0 < sample['bytes'] <= 2048
        cut = saved('cut-root.json'); stopped = saved('physical-after-helper-exit.json')
        require_stopped_tree(saved('physical-cut.json'),stopped,cut['finishedThrough'],cut['startedThrough'])
        assert stopped == after; same_operation(saved('pre-agent-restart-sql.json')['row'],row)
    else: assert host_identity(baseline['revision']) == baseline['hostBefore']
    if barrier_case:
        waiting_baseline = saved('waiting-baseline.json'); waiting = saved('waiting-final-sql.json'); blocked = saved('waiting-blocked.json'); physical = saved('waiting-physical.json')
        assert result['otherWriteDeferredUntilExplicitRelease'] and result['waitingId'] == waiting_baseline['id'] == waiting['row']['id']
        assert waiting_baseline['subject'] == waiting['row']['actor_subject'] == row['acknowledged_by'] == recovery['subject']
        assert len(blocked) == 4
        previous_version = 0; previous_next = dt.datetime.fromisoformat(waiting['row']['created_at_utc'])
        for sample in blocked:
            require_waiting_deferred(sample['sql'],waiting['row']['id'],recovery['subject'])
            assert sample['actualTargetAbsent'] and sample['rootReceiptAbsent'] and sample['sourceTreeAndUnknownUnchanged']
            for field in ('id','container_id','instance_id','actor_subject','owner_hash','request_hash','plan_json','relative_path'):
                assert sample['sql']['row'][field] == waiting['row'][field]
            assert dt.datetime.fromisoformat(sample['atUtc']) < dt.datetime.fromisoformat(row['completed_at_utc'])
            deferred = sample['sql']['row']; next_attempt = dt.datetime.fromisoformat(deferred['next_attempt_at_utc'])
            assert deferred['lease_token'] is None and deferred['lease_expires_at_utc'] is None and deferred['version'] >= previous_version+2 and next_attempt > previous_next
            previous_version = deferred['version']; previous_next = next_attempt
            assert sample['workerRunning'] and [sample['workerId'],sample['workerImage']] == baseline['coreBefore']['/whaledeck-worker'][:2]
            assert sample['workerStartedAt'] == current_core['/whaledeck-worker'][3]
            assert all(n == 0 for n in sample['otherWorkChecks'].values())
        assert previous_version >= 8
        assert directory_snapshot(waiting['row']['id']) == waiting and waiting['row']['state'] == waiting['job']['state'] == 'Succeeded'
        assert waiting['accepted'] == 1 and waiting['pending'] == 0 and not waiting['row']['publish_barrier'] and waiting['row']['lease_token'] is None
        assert dt.datetime.fromisoformat(waiting['row']['root_started_at_utc']) >= dt.datetime.fromisoformat(row['completed_at_utc'])
        operation = waiting['row']['id'].replace('-',''); directory_path = DIRECTORY_ROOT/(operation+'.json'); info = directory_path.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and info.st_size <= 16384
        directory_raw = directory_path.read_bytes()
        assert directory_raw == (directory/'waiting-proof'/(operation+'.root.json')).read_bytes()
        receipt = json.loads(directory_raw); plan = receipt['plan']; managed = json.loads(waiting['row']['plan_json'])
        assert receipt['operationId'] == operation and receipt['state'] == 'Completed' and receipt['owner'] == waiting['row']['owner_hash']
        assert receipt['version'] == waiting['row']['published_version'] and plan['fingerprint'] == managed['Fingerprint']
        assert plan['fingerprint'] == digest(b'whaledeck-container-directory-plan-v1\n',unsigned(plan,'fingerprint'))
        assert physical['inode'][:2] == receipt['pendingIdentity'] and physical['inode'][2:] == [1000,1000,0o750] and physical['privateIdentity'] == receipt['privateIdentity']
        assert physical['originalFileUnchanged'] and physical['actualUiDirectoryObserved'] and recovery['waitingVisibleDirectory'] and recovery['directoryRegistrations'] == 0
        assert recovery['waitingState'] == 'Succeeded' and recovery['waitingId'] == waiting_baseline['id'] and result['waitingActualUi']
        directory_audits = query("SELECT json_build_object('rows',json_agg(json_build_object('actor',actor_subject,'action',action,'detail',detail_json))) FROM audit_events WHERE job_id=:'id'::uuid AND action LIKE 'containers.files.mkdir.%';",id=waiting['row']['id'])['rows']
        expected_hashes = {'pathHash':hashlib.sha256(waiting['row']['relative_path'].encode()).hexdigest().upper(),'instanceHash':hashlib.sha256(waiting['row']['instance_id'].encode()).hexdigest().upper()}
        assert len(directory_audits) >= 2 and len([a for a in directory_audits if a['action'] == 'containers.files.mkdir.accepted']) == 1
        for audit in directory_audits:
            assert audit['actor'] == recovery['subject'] and (json.loads(audit['detail']) if isinstance(audit['detail'],str) else audit['detail']) == expected_hashes
        assert not Path(waiting_baseline['source']).exists() and subprocess.run(['docker','inspect',waiting_baseline['containerId']],capture_output=True,timeout=15).returncode != 0
        (directory/'waiting-independent.json').write_text(json.dumps({'originalIdExecutedOnce':True,'deferredSamples':4,'startedAfterOriginalAcknowledgement':True,'rootAndPhysicalInodeVerified':True,'bothFixturesRemoved':True,'actualDirectoryUi':True,'directoryAuditRows':len(directory_audits),'hashedMetadataAndCurrentAdminVerified':True}))
    no_active_work(); inactive_except()
    screenshots = (('inflight.png','logged-out.png','partial-recovery-confirm.png','partial-acknowledged.png') if logout_case else ('inflight.png','authority-denied.png','partial-recovery-confirm.png','partial-acknowledged.png') if revoked else ('inflight.png','unknown-counts.png','partial-recovery-confirm.png','partial-acknowledged.png') if unknown_case
        else ('inflight.png','partial.png','partial-recovery-confirm.png','partial-acknowledged.png')) + (('partial-counts.png',) if kind in ('cancel','worker-crash','agent-restart','logout','group-removal','account-disable') else ())
    if barrier_case: screenshots += ('waiting-directory.png',)
    for name in screenshots:
        raw = (directory/name).read_bytes(); assert raw.startswith(b'\x89PNG\r\n\x1a\n')
        assert int.from_bytes(raw[16:20],'big') == 320 and int.from_bytes(raw[20:24],'big') == 600
    observed = {'independentReadOnlyVerification':'passed','id':row['id'],'actualSqlAndRootAndHddUnchanged':True,'hddMetadataFiles':len(checked),
        'kind':kind,'sourceAndContainerAccuratelyRemoved':True,'deletedSourceBytesReRead':False,'screenshots':len(screenshots),'auditRows':len(audits),
        'all11Healthy':True,'installedHelpersMatchProductRevision':True,'productRevision':baseline['revision'],
        'sourceDevice':baseline['original'][''][0],'hddDevice':hdd_device,'waitingWriteVerified':barrier_case,'authorityRevoked':revoked,'normalLogoutWhileIdentityActive':logout_case}
    (directory/'independent-final.json').write_text(json.dumps(observed))
    print(json.dumps(observed))


if __name__ == '__main__': main()
