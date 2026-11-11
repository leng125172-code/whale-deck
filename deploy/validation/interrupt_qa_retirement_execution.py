#!/usr/bin/env python3
"""Opt-in process-death fault for one declared new QA retirement execution.

Never writes business SQL, source, quota, cancellation or journal metadata.
The installed controller checks the complete immutable request and exact live
boot/cgroup inode. Holding only the task flock excludes result commit while
that one execution is killed; storage locks are never acquired here.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import time
import uuid


def identifier(value):
    assert isinstance(value, str) and str(uuid.UUID(value)) == value and int(uuid.UUID(value)) != 0
    return value


def check_scope(payload, state, declaration, row, *, task=None):
    for key in ('workspaceId', 'ownerSubject', 'retirementId'):
        identifier(payload[key])
    assert re.fullmatch('qa-native-[a-f0-9]{24}', declaration['slug'])
    assert state['slug'] == declaration['slug'] == row['slug']
    assert payload['workspaceId'] == state['workspace']['id'] == row['workspaceId']
    assert payload['workspaceId'] not in declaration['originalWorkspaceIds']
    assert payload['ownerSubject'] == state['ownerSubject'] == row['ownerSubject']
    assert payload['retirementId'] == state['retirementBusiness']['retirement']['retirementId'] == row['retirementId']
    assert row['lifecycle'] == 'Retiring' and row['finished'] is None and row['attention'] is False
    assert row['attempt'] == row['recoveries'] == row['completions'] == 0
    plan = json.loads(state['retirementBusiness']['retirement']['planJson'])
    assert row['planSha256'] == plan['retirementPlanSha256']
    if task is None:
        assert row['phase'] == 'plan' and row['confirmed'] is None and row['executeStepId'] is None
    else:
        identifier(task)
        assert row['phase'] == 'execute' and row['confirmed'] is not None
        assert row['executeStepId'] == task and row['settled'] is None
        request = json.loads(row['request'])
        assert request['taskId'] == task and request['phase'] == 'execute'
        assert request['retirementId'] == payload['retirementId']
        assert request['retirementPlanSha256'] == row['planSha256']
        assert request['prerequisite']['storage']['workspaceId'] == payload['workspaceId']
        assert request['prerequisite']['archiveId'] == plan['archiveId']


def interrupt_locked(tasks, value, save, *, progress=None):
    """Return None until execution is live; never cancel or restart a task."""
    with tasks.entry(value) as directory:
        tasks.require_dispatch(directory, value)
        assert tasks.completed(directory, value) is None, 'QA_EXECUTION_ALREADY_COMMITTED'
        assert not tasks.cancelled(directory), 'QA_EXECUTION_ALREADY_CANCELLED'
        proof = tasks.proof(directory)
        if proof is None:
            return None
        tasks.execution.validate(proof)
        assert proof['path'] == '/system.slice/whaledeck-development-retirement-run-' + value['taskId'].replace('-', '') + '.service'
        # A previous boot or already empty group is not a live fault injection.
        assert proof['bootId'] == tasks.execution.boot()
        assert not tasks.execution.settled(proof), 'QA_EXECUTION_ALREADY_GONE'
        digest = hashlib.sha256(tasks.d.canonical(value)).hexdigest()
        before = dict(taskId=value['taskId'], requestSha256=digest, execution=proof,
            resultAbsent=True, cancellationAbsent=True, liveExecution=True)
        if progress is not None:
            before['deletionProgress'] = progress.wait(tasks, value)
            assert not tasks.execution.settled(proof), 'QA_EXECUTION_GONE_BEFORE_PROGRESS_KILL'
        save('before-kill', before)  # Fail closed if evidence cannot be committed.
        assert tasks.execution.settled(proof, stop=True)
        assert tasks.completed(directory, value) is None
        assert not tasks.cancelled(directory) and tasks.proof(directory) == proof
        receipt = tasks.state(directory, value)
        assert receipt['status'] == 'Interrupted' and receipt['taskId'] == value['taskId']
        assert receipt['requestSha256'] == digest
        result = dict(interrupted=True, before=before, receipt=receipt,
            exactExecutionSettled=True, cancellationAbsent=True, resultAbsent=True)
        if progress is not None:
            save('execution-stopped', result)
            result['deletionProgress'] = progress.stopped(tasks, value)
        save('after-kill', result)
        return result


def checkpoint(retirement):
    from validate_workspace_archive_faults_installed import sql
    identifier(retirement)
    rows = sql("SELECT json_build_object('workspaceId',w.id,'ownerSubject',w.owner_subject,'slug',w.slug,'lifecycle',w.lifecycle_state,'retirementId',r.id,'executeStepId',r.execute_step_id,'phase',r.phase,'attention',r.attention_required,'finished',r.finished_at_utc,'confirmed',r.confirmed_at_utc,'attempt',r.execution_attempt,'planSha256',r.plan_json::jsonb->>'retirementPlanSha256','request',s.request_json,'settled',s.settled_at_utc,'recoveries',(SELECT count(*) FROM development_workspace_retirement_recoveries a WHERE a.retirement_id=r.id),'completions',(SELECT count(*) FROM development_workspace_retirement_completions c WHERE c.retirement_id=r.id)) FROM development_workspace_retirement_workflows r JOIN development_workspaces w ON w.id=r.workspace_id LEFT JOIN development_workspace_retirement_steps s ON s.id=r.execute_step_id WHERE r.id='"+retirement+"';")
    assert len(rows) == 1
    return rows[0]


def settled_snapshot(task, status='Interrupted'):
    from validate_workspace_archive_faults_installed import sql
    identifier(task)
    assert status in ('Interrupted', 'Stopped')
    return sql("SELECT json_build_object('retirementId',r.id,'taskId',s.id,'requestSha256',encode(sha256(convert_to(s.request_json,'utf8')),'hex'),'proofSha256',encode(sha256(convert_to(s.proof_json::text,'utf8')),'hex'),'dispatchedAtUtc',s.dispatched_at_utc,'settledAtUtc',s.settled_at_utc,'planSha256',encode(sha256(convert_to(r.plan_json::text,'utf8')),'hex'),'confirmedAtUtc',r.confirmed_at_utc,'planExpiresAtUtc',r.plan_expires_at_utc) FROM development_workspace_retirement_steps s JOIN development_workspace_retirement_workflows r ON r.id=s.retirement_id WHERE s.id='"+task+"' AND s.proof_json->>'status'='"+status+"' AND s.settled_at_utc IS NOT NULL AND r.attention_required AND r.finished_at_utc IS NULL AND r.execution_attempt=0;")


def installed_tasks():
    path = Path('/usr/local/libexec/whaledeck-development-retirement-transport.py')
    info = path.lstat()
    import stat
    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and not info.st_mode & 0o7022
    spec = importlib.util.spec_from_file_location('installed_retirement_transport', path)
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    files, storage, quota, archives, shared, operations, control, tree, codec, plans, execution = (loader.load(name) for name in (
        'whaledeck-files.py', 'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py',
        'whaledeck-development-archives.py', 'whaledeck-development-archive-operations.py',
        'whaledeck-development-retirement-operations.py', 'whaledeck-development-archive-control.py',
        'whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-development-retirement.py',
        'whaledeck-development-retirement-execution.py'))
    driver = storage.WorkspaceStorage(files, quota=quota)
    engine = archives.DevelopmentArchives(driver, tree.FileTreeManifest, codec.TreeArchive,
        archives.HddArchiveStorage(driver, read_only=True))
    return operations.create(shared, driver, plans.DevelopmentRetirementPlans(engine),
        execution.DevelopmentRetirementExecution(engine, archives.PayloadFiles), control.Execution(driver, kind='retirement'))


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 1
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE') == '1'
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
    raw = sys.stdin.readline(8193)
    assert len(raw) <= 8192 and raw.endswith('\n')
    payload = json.loads(raw)
    assert set(payload) == {'evidence', 'workspaceId', 'ownerSubject', 'retirementId', 'attempt'}
    evidence = Path(payload['evidence']).resolve(strict=True)
    assert evidence.is_relative_to(Path(__file__).resolve().parents[2]/'deploy/artifacts') and evidence.is_dir()
    attempt = payload['attempt']
    assert type(attempt) is int and 1 <= attempt <= 4
    state = json.loads((evidence/'state.json').read_bytes())
    declaration = json.loads((evidence/'declaration.json').read_bytes())
    check_scope(payload, state, declaration, checkpoint(payload['retirementId']))
    tasks = installed_tasks()
    progress = None
    if os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_DELETE_PROGRESS_PROBE') == '1':
        from retirement_deletion_progress import InstalledDeletionProgress
        progress = InstalledDeletionProgress()
    os.umask(0o077)
    def save(suffix, value):
        target = evidence/f'interruption-{attempt}-{suffix}.json'
        pending = target.with_suffix('.pending')
        with pending.open('x') as stream:
            json.dump(value, stream, indent=2); stream.flush(); os.fsync(stream.fileno())
        os.link(pending, target); pending.unlink()
    save('armed', dict(workspaceId=payload['workspaceId'], retirementId=payload['retirementId'],
        originalWorkspaceExcluded=True, noStorageOrSqlWrites=True))
    print(json.dumps(dict(armed=True)), flush=True)
    raw = sys.stdin.readline(128)
    assert raw.endswith('\n') and len(raw) < 128
    task = identifier(json.loads(raw)['taskId'])
    row = checkpoint(payload['retirementId'])
    check_scope(payload, state, declaration, row, task=task)
    value = json.loads(row['request']); tasks.validate(value)
    save('binding', {k: v for k, v in row.items() if k != 'request'})
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        try:
            result = interrupt_locked(tasks, value, save, progress=progress)
        except FileNotFoundError:
            result = None
        except tasks.d.files.Rejected as error:
            if str(error) != 'WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY':
                raise
            result = None
        if result is not None:
            deadline = time.monotonic() + 60
            while True:
                original = settled_snapshot(task)
                if original:
                    assert len(original) == 1 and original[0]['proofSha256']
                    result['original'] = original
                    save('settled', result)
                    break
                assert time.monotonic() < deadline, 'QA_INTERRUPTED_STEP_NOT_SETTLED'
                time.sleep(0.5)
            print(json.dumps(result), flush=True)
            return
        time.sleep(0.01)
    raise TimeoutError('QA_EXECUTION_START_NOT_OBSERVED')


if __name__ == '__main__':
    main()
