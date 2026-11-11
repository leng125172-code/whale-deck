#!/usr/bin/env python3
"""Opt-in real user retirement of two new QA spaces, preserving all history.

The first runs imported Node/.NET tests. The second proves the released budget
can be reused, then retires through the same product workflow. Never delete
business/audit rows, invoke the engine directly, or touch original spaces.
"""
import collections
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import traceback
import uuid

import requests
from run_with_ephemeral_admin import container_environment, require
from validate_platform_maintenance_browser import account, save
from validate_development_installed_runtime import fixture
from validate_workspace_archive_faults_installed import snapshot, sql
from retirement_administrator_proof import observe_administrator_recovery
from takeover_retry_proof import retry_ready, require_retry_success

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ('validate_workspace_retirement_business_installed.py', 'validate_development_installed_runtime.py',
    'validate_development_installed_runtime.mjs', 'installed_workspace_retirement.mjs',
    'inspect_development_workspace_retirement.py', 'installed_development_lifecycle.mjs',
    'inspect_development_workspace_lifecycle.py', 'validate_workspace_archive_faults_installed.py',
    'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs',
    'installed_workspace_retirement_recovery.mjs', 'hold_qa_workspace_source_fd.py',
    'installed_recovery_authority.mjs', 'owned_recovery_authority.py',
    'installed_retirement_interruption.mjs', 'interrupt_qa_retirement_execution.py', 'retirement_deletion_progress.py',
    'installed_retirement_stop.mjs', 'pause_qa_retirement_execution.py',
    'installed_recovery_administrator.mjs', 'retirement_administrator_proof.py',
    'installed_recovery_takeover.mjs', 'owned_recovery_takeover.py',
    'retirement_takeover_attempt.mjs', 'takeover_retry_proof.py', 'takeover_revocation_state.mjs')


def require_disks_preserved(before, after):
    assert set(before) == set(after)
    for name, original in before.items():
        current = after[name]
        # st_blocks also charges the host filesystem's extent metadata. Keep
        # the exact logical cap/inode/mount/UUID/filesystem capacity, and never
        # allow reservation loss; allocating an extent metadata block is valid.
        assert {k: v for k, v in original.items() if k != 'reservedBytes'} == {
            k: v for k, v in current.items() if k != 'reservedBytes'}, name
        assert type(original['reservedBytes']) is type(current['reservedBytes']) is int
        assert current['reservedBytes'] >= original['reservedBytes'] >= original['bytes'], name


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 4
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_BUSINESS_PROBE') == '1'
    interrupt = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE') == '1'
    delete_progress = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_DELETE_PROGRESS_PROBE') == '1'
    assert not delete_progress or interrupt
    stopped = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_STOP_PROBE') == '1'
    administrator = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_ADMINISTRATOR_PROBE') == '1'
    takeover = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_PROBE') == '1'
    takeover_kind = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_KIND', 'group-removal')
    assert takeover_kind in ('group-removal', 'account-disable')
    retry_probe = os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_RETRY_PROBE') == '1'
    if retry_probe:
        assert takeover
    if takeover:
        assert administrator and not (interrupt or stopped)
    if administrator:
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') != '1'
    assert not (interrupt and stopped)
    if interrupt or stopped:
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') != '1'
    if os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') == '1':
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
        assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_KIND', 'group-removal') in ('group-removal', 'account-disable')
    revision, package_feed, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True), Path(sys.argv[3]).resolve(strict=True)
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir() and not (evidence / 'state.json').exists()
    os.umask(0o077)
    sources = {}
    for name in SOURCES:
        raw = Path(__file__).with_name(name).read_bytes()
        (evidence / ('source-' + name)).write_bytes(raw)
        sources[name] = hashlib.sha256(raw).hexdigest()
    save(evidence, 'source-sha256.json', sources)
    before = snapshot()
    save(evidence, 'before.json', before)
    assert before['jobs']['active'] == before['jobs']['locks'] == 0
    slug = 'qa-native-' + uuid.uuid4().hex[:24]
    content = fixture(package_feed)
    archive = evidence / 'source.tar'
    archive.write_bytes(content)
    save(evidence, 'declaration.json', dict(slug=slug, revision=revision, archiveSha256=hashlib.sha256(content).hexdigest(),
        explicitDeletionScope='Only newly registered exact QA slug and its -reuse companion',
        originalWorkspaceIds=sorted(before['physical']), retainsHddAndReportsAndAudits=True))
    client, cleaned = requests.Session(), []
    client.trust_env = False
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        try:
            with contextlib.ExitStack() as stack:
                roles = ('administrator', 'ordinary', 'administrator') if administrator else ('administrator', 'ordinary')
                accounts = [stack.enter_context(account(client, role, group, cleaned)) for role in roles]
                assert len({entry['username'] for entry in accounts}) == len(accounts)
                for attempt in range(1, 5):
                    sources = {}
                    for name in SOURCES:
                        raw = Path(__file__).with_name(name).read_bytes()
                        (evidence / f'attempt-{attempt}-source-{name}').write_bytes(raw)
                        sources[name] = hashlib.sha256(raw).hexdigest()
                    save(evidence, f'attempt-{attempt}-source-sha256.json', sources)
                    payload = dict(revision=revision, evidence=str(evidence), slug=slug, accounts=accounts,
                        archive=str(archive), archiveSha256=hashlib.sha256(content).hexdigest(), retirementProbe=True,
                        recoveryAdministrator=administrator, revokedOwnerTakeover=takeover, takeoverRetryProbe=retry_probe,
                        takeoverKind=takeover_kind,
                        authorityProbe=os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') == '1', authorityAttempt=attempt,
                        authorityKind=os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_KIND', 'group-removal'))
                    # Browser process belongs to this wrapper; credentials only
                    # cross its private stdin, never argv or evidence.
                    try:
                        result = subprocess.run(['node', str(Path(__file__).with_name('validate_development_installed_runtime.mjs'))],
                            input=json.dumps(payload), text=True, capture_output=True, timeout=1200)
                    finally:
                        for prefix in ('authority', 'takeover'):
                            intent = evidence / f'{prefix}-{attempt}-intent.json'
                            restored = evidence / f'{prefix}-{attempt}-restored.json'
                            if intent.exists():
                                deadline = time.monotonic() + 90
                                while not restored.exists() and time.monotonic() < deadline:
                                    time.sleep(0.2)
                                assert restored.exists(), 'OWNED_AUTHORITY_RESTORE_NOT_CONFIRMED'
                                assert json.loads(restored.read_bytes())['originalGroupRestored']
                                assert json.loads(restored.read_bytes())['originalAuthorityRestored']
                    save(evidence, f'browser-process-{attempt}.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
                    if result.returncode == 0:
                        break
                    if retry_probe and attempt == 1:
                        failed_state = json.loads((evidence/'state.json').read_bytes())
                        save(evidence, 'takeover-retry-ready.json', retry_ready(evidence, failed_state, client, accounts, group))
                    save(evidence, f'awaiting-repair-{attempt}.json', dict(attempt=attempt, retainsExactIntents=True,
                        noAutomaticMutationRetry=True, next='Inspect state/failure; explicitly retry same identities and intent after repair'))
                    print(json.dumps(dict(awaitingRepair=True, evidence=str(evidence), attempt=attempt)), flush=True)
                    deadline, control = time.monotonic() + 1800, evidence / 'retry.json'
                    while not control.exists() and time.monotonic() < deadline:
                        time.sleep(1)
                    assert control.exists(), 'REPAIR_WINDOW_EXPIRED'
                    retry = json.loads(control.read_bytes())
                    assert set(retry) == {'action', 'revision'} and retry['action'] == 'retry' and re.fullmatch('[a-f0-9]{40}', retry['revision']), 'EXACT_RUNTIME_RETRY_REQUIRED'
                    control.rename(evidence / f'retry-consumed-{attempt}.json')
                    revision = retry['revision']
                assert result.returncode == 0, 'INSTALLED_RETIREMENT_BUSINESS_FAILED'
        finally:
            save(evidence, 'identity-cleanup.json', dict(removed=cleaned))
        assert sorted(cleaned) == sorted(roles)
        state = json.loads((evidence / 'state.json').read_bytes())
        if retry_probe:
            ready = json.loads((evidence/'takeover-retry-ready.json').read_bytes())
            require_retry_success(state, ready)
            save(evidence, 'takeover-retry-accepted.json', dict(passed=True, sameAccountsWorkspaceIntent=True,
                noNewRunOrImport=True, originalFdEvidencePreserved=True, oldWindowRetained=True,
                currentEvidenceWindow=2, originalProductFailureTaskPreserved=True, exactlyOneProductRecovery=True))
        assert state['passed'] and state['lifecycle']['passed'] and state['legacyLifecycleSkipped']
        assert state['retirementBusiness']['passed'] and state['budgetReuse']['passed']
        if administrator:
            save(evidence, 'persistent-administrator-recovery.json', observe_administrator_recovery(state, sql))
        if takeover:
            proof = state['retirementBusiness']['recovery']['takeover']
            assert proof['passed'] and proof['recoveryStatus'] == proof['historyStatus'] == 403
            assert proof['denial']['denialWindowUnchanged']
            assert proof['kind'] == takeover_kind and proof['originalOwnerContextAcceptedAfterRestore']
            if takeover_kind == 'account-disable':
                assert proof['originalOwnerIdentityRejectedWhileDisabled'] and proof['ownerStillDisabledAfterBSuccess']
                assert proof['ownerStatusBeforeB'] == proof['ownerStatusAfterB'] == 403
                assert proof['whileRevoked']['originalAccountStillDisabled'] and proof['whileRevoked']['originalGroupsUnchanged']
            else:
                assert proof['originalOwnerAuthenticatedWithoutAdministrator'] and proof['ownerStillWithoutAdministratorAfterBSuccess']
                assert proof['whileRevoked']['originalGroupStillAbsent']
            assert proof['restoration']['originalAuthorityRestored']
        if os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1':
            recovery = state['retirementBusiness']['recovery']
            assert recovery['passed'] and recovery['independentConsentRequired']
            if os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') == '1':
                assert recovery['authority']['passed']
                assert recovery['authority']['kind'] == os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_KIND', 'group-removal')
                assert recovery['authority']['recoveryStatus'] == recovery['authority']['historyStatus'] == 403
                assert recovery['authority']['restoration']['unchanged']
            assert state['retirementRecoveryOrdinaryDenied']
            if stopped:
                fault = recovery['stop']
                assert fault['stopped'] and fault['exactExecutionSettled']
                assert fault['cancellationPresent'] and fault['resultAbsent']
                assert recovery['attemptsBefore'][0]['status'] == 'Stopped'
                assert recovery['failedView']['cancelRequested'] and recovery['stopUiCancellation']['status'] == 202
            elif interrupt:
                fault = recovery['interruption']
                assert fault['interrupted'] and fault['exactExecutionSettled']
                assert fault['cancellationAbsent'] and fault['resultAbsent']
                assert recovery['attemptsBefore'][0]['status'] == 'Interrupted'
                if delete_progress:
                    progress = fault['deletionProgress']
                    assert progress['partialDeletion'] and progress['executionAlreadySettled']
                    assert progress['removedFiles'] > 0 and progress['remainingFiles'] > 0
                    assert progress['everyAbsenceHasExactIntent'] and progress['noStorageLockOrWrites']
            else:
                assert recovery['openUnlinked']['links'] == 0 and recovery['release']['released']
            assert recovery['attemptsBefore'][0] == recovery['attemptsAfter'][0]
            assert recovery['attemptsAfter'][1]['previousTaskId'] == recovery['failedView']['executeTaskId']
            original = recovery['stop']['original'] if stopped else recovery['interruption']['original'] if interrupt else recovery['openUnlinked']['attempts']
            assert len(original) == 1 and original[0]['taskId'] == recovery['failedView']['executeTaskId']
            old_task = str(uuid.UUID(original[0]['taskId']))
            unchanged = sql("SELECT json_build_object('retirementId',r.id,'taskId',s.id,'requestSha256',encode(sha256(convert_to(s.request_json,'utf8')),'hex'),'proofSha256',encode(sha256(convert_to(s.proof_json::text,'utf8')),'hex'),'dispatchedAtUtc',s.dispatched_at_utc,'settledAtUtc',s.settled_at_utc,'planSha256',encode(sha256(convert_to(r.plan_json::text,'utf8')),'hex'),'confirmedAtUtc',r.confirmed_at_utc,'planExpiresAtUtc',r.plan_expires_at_utc) FROM development_workspace_retirement_steps s JOIN development_workspace_retirement_workflows r ON r.id=s.retirement_id WHERE s.id='" + old_task + "';")
            assert unchanged == original
            save(evidence, 'persistent-original-failure-preserved.json', dict(passed=True, before=original, after=unchanged))
        assert state['retirementOrdinaryDenied'] and not state['retainedForProductDecommission']
        ids = [state['workspace']['id'], state['budgetReuse']['workspace']['id']]
        assert len(set(ids)) == 2 and not set(ids) & set(before['physical'])
        after = snapshot()
        save(evidence, 'after.json', after)
        require_disks_preserved(before['disks'], after['disks'])
        for key in ('protected', 'physical', 'runtime'):
            assert before[key] == after[key], key
        for key in ('hdd', 'journal'):
            assert all(after[key].get(name) == value for name, value in before[key].items()), key
        for table, rows in before['rows'].items():
            assert not (collections.Counter(rows) - collections.Counter(after['rows'][table])), table
        old_ids = {space['id'] for space in before['spaces']}
        assert [space for space in after['spaces'] if space['id'] in old_ids] == before['spaces']
        new_spaces = [space for space in after['spaces'] if space['id'] not in old_ids]
        assert {space['id'] for space in new_spaces} == set(ids)
        assert after['jobs']['active'] == after['jobs']['locks'] == 0
        # Narrow public receipt/audit fields only. No login binding, token or
        # encrypted request snapshot is copied out of PostgreSQL.
        for identifier in ids:
            assert str(uuid.UUID(identifier)) == identifier
        query = "SELECT json_build_object('workspaceId',w.id,'slug',w.slug,'state',w.lifecycle_state,'completion',json_build_object('retirementId',c.retirement_id,'executeStepId',c.execute_step_id,'completedAtUtc',c.completed_at_utc),'audits',(SELECT json_agg(a.action ORDER BY a.occurred_at_utc) FROM audit_events a WHERE a.job_id=c.retirement_id)) FROM development_workspaces w JOIN development_workspace_retirement_completions c ON c.workspace_id=w.id WHERE w.id IN (" + ','.join("'" + identifier + "'" for identifier in ids) + ') ORDER BY w.id;'
        receipts = sql(query)
        save(evidence, 'persistent-completions.json', receipts)
        assert len(receipts) == 2 and all(row['state'] == 'Retired' and
            'development.workspace.retirement.budget-released' in row['audits'] for row in receipts)
        for run in state['runs']:
            assert run['result']['state'] == 'Succeeded' and run['result']['reportStatus'] == 'Available'
        assert sources == {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest() for name in SOURCES}
        save(evidence, 'accepted.json', dict(passed=True, revision=revision, realIdentityUiApiWorkerAgent=True,
            workspaceIds=ids, successfulRuns=len(state['runs']), reportsReadableAfterRetirement=True,
            physicalKernelQuotaAndClaimsReleased=True, sameBudgetRegisteredAndRetired=True, originalSpacesPreserved=len(old_ids),
            hddAndAuditsRetained=True, temporaryAccountsRemoved=len(roles), originalResourcesUnchanged=True))
        print(json.dumps(dict(passed=True, evidence=str(evidence), workspaceIds=ids)), flush=True)
    except BaseException as error:
        save(evidence, 'failed-wrapper.json', dict(failureType=type(error).__name__, cleanedRoles=cleaned,
            frames=[dict(file=Path(frame.filename).name, line=frame.lineno) for frame in traceback.extract_tb(error.__traceback__)]))
        raise
    finally:
        client.close()


if __name__ == '__main__':
    try:
        def stopped(signum, frame):
            raise SystemExit(128 + signum)
        signal.signal(signal.SIGTERM, stopped)
        main()
    except Exception as error:
        print(json.dumps(dict(passed=False, failureType=type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
