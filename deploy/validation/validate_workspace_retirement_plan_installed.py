#!/usr/bin/env python3
"""Explicit root QA: installed readonly retirement RPC after real UI archive.

Only the fixed existing QA workspace is stopped/archived/resumed through the
product. Retirement requests are hard-coded plan-only by the C# client. Never
delete source, release quota/budget, write SQL or replace installed code/keys.
"""
import collections
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import validate_workspace_archive_faults_installed as guard
from validate_build_provenance import local_http, verify

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / 'deploy/validation/DevelopmentRetirementRpcProbe'
DOTNET = Path('/home/user/.local/share/whaledeck-toolchains/dotnet-10.0.402/dotnet')
JOURNAL = Path('/var/lib/whaledeck-development-retirement-operations')


def save(directory, name, value):
    with (directory / name).open('x') as stream:
        json.dump(value, stream, indent=2)


def failed_probe_is_settled(process):
    """Only the fixed client's sanitized failure can permit UI recovery.

    Its cleanup list is installed before the first task RPC. An empty list
    therefore means failure before dispatch; timeout/unknown output proves
    nothing and must keep the space stopped until the original task settles.
    """
    try:
        failure = json.loads(process['stderr'])
        cleanup = failure['cleanup']
        return (type(process['returncode']) is int and process['returncode'] == 1
            and failure['passed'] is False and isinstance(cleanup, list)
            and all(item['settled'] is True and item['Status'] in
                ('Succeeded', 'Rejected', 'Stopped', 'Interrupted') for item in cleanup))
    except (KeyError, TypeError, ValueError):
        return False


def host_request(value, task):
    return dict(schemaVersion=1, taskId=task, phase='plan', retirementId=value['retirementId'],
        retirementPlanSha256='', prerequisite=dict(schemaVersion=1, storage=dict(schemaVersion=1, **value['storage']),
            expectedEpoch=value['expectedEpoch'], archiveId=value['archiveId'],
            archivePlanSha256=value['archivePlanSha256'], captureReceiptSha256=value['captureReceiptSha256']))


def plan_probe(directory, label, value, capture):
    save(directory, label + '-request.json', value)
    result = subprocess.run([str(DOTNET), str(CLIENT / 'bin/Release/net10.0/publish/DevelopmentRetirementRpcProbe.dll'),
        '--installed-plan'], input=json.dumps(value), capture_output=True, text=True, timeout=200)
    save(directory, label + '-process.json', dict(returncode=result.returncode, stdout=result.stdout, stderr=result.stderr))
    assert result.returncode == 0, 'INSTALLED_RETIREMENT_PLAN_RPC_FAILED'
    proof = json.loads(result.stdout)
    assert proof['passed'] and proof['installedAgent'] and proof['protocolMajor'] == 1 and proof['protocolMinor'] >= 78
    assert proof['taskId'] == value['taskId'] and proof['Status'] == value['expectedStatus']
    assert proof['signatureRefusals'] == 7 and proof['duplicateStartPreserved'] and proof['lateCancelPreserved']
    assert proof['preCancelledStartStopped']
    if proof['Status'] == 'Succeeded':
        plan = proof['result']
        for name in ('schemaVersion', 'expectedEpoch', 'storageProofSha256', 'sourceVersion', 'sourceManifestSha256',
                'entryCount', 'totalBytes', 'archiveFilesystemUuid', 'hddProofSha256', 'archiveSha256',
                'archiveIndexSha256', 'archiveBytes', 'archiveInode'):
            assert plan[name] == capture[name], name
        assert plan['storage'] == capture['storage']
        assert plan['archiveId'] == value['archiveId'] and plan['archivePlanSha256'] == value['archivePlanSha256']
        assert plan['captureReceiptSha256'] == value['captureReceiptSha256']
        assert plan['retainsSource'] is plan['reservesBudget'] is True and plan['deletionAuthorized'] is False
        body = {key: item for key, item in plan.items() if key != 'retirementPlanSha256'}
        assert hashlib.sha256(guard.canonical(body)).hexdigest() == plan['retirementPlanSha256']
    else:
        assert proof['Code'] == 'WORKSPACE_STORAGE_ARCHIVE_NOT_STOPPED' and proof['result'] is None
    tasks = [value['taskId'], proof['preCancelledTaskId']]
    assert len(set(tasks)) == 2 and all(str(uuid.UUID(task)) == task for task in tasks)
    for index, task in enumerate(tasks):
        request = host_request(value, task)
        target = JOURNAL / task
        assert json.loads((target / 'request.json').read_bytes()) == request
        if index == 0:
            receipt = json.loads((target / 'result.json').read_bytes())
            assert receipt['requestSha256'] == hashlib.sha256(guard.canonical(request)).hexdigest() == proof['requestSha256']
            assert receipt['status'] == proof['Status'] and receipt['result'] == proof['result']
        else:
            assert not (target / 'execution.json').exists() and not (target / 'dispatch.json').exists()
        unit = 'whaledeck-development-retirement-run-' + task.replace('-', '') + '.service'
        props = subprocess.check_output(['systemctl', 'show', unit, '-p', 'MainPID', '-p', 'ControlGroup'], text=True)
        values = dict(line.split('=', 1) for line in props.splitlines() if '=' in line)
        assert values.get('MainPID', '0') == '0'
        if values.get('ControlGroup'):
            processes = Path('/sys/fs/cgroup' + values['ControlGroup']) / 'cgroup.procs'
            assert not processes.exists() or not processes.read_text().strip()
    save(directory, label + '-proof.json', proof)
    return proof, tasks


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 3
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_PLAN_PROBE') == '1'
    revision, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True)
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir() and not evidence.is_symlink()
    assert evidence.is_relative_to(ROOT / 'deploy/artifacts')
    assert DOTNET.is_file() and (CLIENT / 'bin/Release/net10.0/publish/DevelopmentRetirementRpcProbe.dll').is_file()
    os.umask(0o077)
    # Refuse a stale or mixed installation before creating an identity,
    # stopping the QA workspace or dispatching even a readonly plan task.
    save(evidence, 'installed-provenance.json', verify(revision, fetch=local_http))
    business = evidence / 'installed-business'; business.mkdir(mode=0o700)
    sources = {}
    for path in (Path(__file__), CLIENT / 'Program.cs', CLIENT / 'InstalledPlanProbe.cs', CLIENT / 'DevelopmentRetirementRpcProbe.csproj'):
        raw = path.read_bytes(); name = 'source-' + path.name
        with (evidence / name).open('xb') as stream: stream.write(raw)
        sources[str(path.relative_to(ROOT))] = hashlib.sha256(raw).hexdigest()
    publication = {str(path.relative_to(CLIENT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (CLIENT / 'bin/Release/net10.0/publish').iterdir() if path.is_file()}
    save(evidence, 'publication-sha256.json', publication); save(evidence, 'source-sha256.json', sources)
    before = guard.snapshot(); save(evidence, 'before.json', before)
    retirement_before = guard.tree(JOURNAL); save(evidence, 'retirement-journal-before.json', retirement_before)
    assert before['physical'][guard.WORKSPACE]['marker']['state'] == 'Active' and len(before['physical']) == 5
    signal = business / 'retirement-plan-result.json'
    safe_to_resume = True
    def signal_result(proof):
        pending = signal.with_name('retirement-plan-result.pending.json')
        save(business, pending.name, proof)
        pending.rename(signal)
    with (evidence / 'business-process.log').open('x') as log:
        child = subprocess.Popen(['python3', '-B', str(Path(__file__).with_name('validate_workspace_archive_business_installed.py')),
            revision, str(business)], env=dict(os.environ, WHALEDECK_INSTALLED_ARCHIVE_BUSINESS_PROBE='1'), stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 420
            ready = business / 'retirement-plan-ready.json'
            while not ready.exists():
                assert child.poll() is None, 'RETIREMENT_ARCHIVE_BROWSER_EXITED'
                assert time.monotonic() < deadline, 'RETIREMENT_ARCHIVE_HANDOFF_TIMEOUT'
                time.sleep(0.2)
            value = json.loads(ready.read_bytes())
            assert value['workspaceId'] == guard.WORKSPACE and str(uuid.UUID(value['archiveId'])) == value['archiveId']
            stored = guard.archive(value['archiveId']); save(evidence, 'archive-prerequisite.json', stored)
            row = stored['intent']; capture = row['capture_json']
            assert row['workspace_id'] == guard.WORKSPACE and row['finished_at_utc'] and row['lease'] is None
            assert stored['job']['state'] == 'Succeeded' and capture == row['verify_json']
            assert value['expectedEpoch'] == row['expected_epoch'] == capture['expectedEpoch']
            storage = {name: capture['storage'][name] for name in ('workspaceId', 'rootId', 'environmentSha256', 'diskMiB')}
            assert storage['workspaceId'] == guard.WORKSPACE and storage['rootId'] == 'nvme-main' and storage['diskMiB'] == 8192
            request = dict(taskId=str(uuid.uuid4()), retirementId=str(uuid.uuid4()), archiveId=value['archiveId'],
                storage=storage, expectedEpoch=value['expectedEpoch'], archivePlanSha256=capture['planSha256'],
                captureReceiptSha256=capture['receiptSha256'], expectedStatus='Succeeded')
            plan_hdd = guard.tree(Path('/data/DevSpace'))
            save(evidence, 'hdd-before-plan.json', plan_hdd)
            safe_to_resume = False
            proof, tasks = plan_probe(evidence, 'stopped-plan', request, capture)
            safe_to_resume = True
            assert guard.tree(Path('/data/DevSpace')) == plan_hdd, 'READONLY_PLAN_CHANGED_HDD'
            signal_result(dict(proof, safeToResume=True))
        finally:
            if not signal.exists():
                failed_process = evidence / 'stopped-plan-process.json'
                if not safe_to_resume and failed_process.exists():
                    try:
                        process = json.loads(failed_process.read_bytes())
                        safe_to_resume = failed_probe_is_settled(process)
                    except ValueError:
                        safe_to_resume = False
                signal_result(dict(passed=False, safeToResume=safe_to_resume))
            code = child.wait(timeout=1000)
            save(evidence, 'business-exit.json', dict(returncode=code))
        assert code == 0 and json.loads((business / 'accepted.json').read_bytes())['passed']
    request.update(taskId=str(uuid.uuid4()), retirementId=str(uuid.uuid4()), expectedStatus='Rejected')
    _, rejected_tasks = plan_probe(evidence, 'active-plan-refused', request, capture)
    assert guard.tree(Path('/data/DevSpace')) == plan_hdd, 'REJECTED_PLAN_CHANGED_HDD'
    tasks += rejected_tasks
    current = guard.snapshot(); save(evidence, 'after.json', current)
    retirement_after = guard.tree(JOURNAL); save(evidence, 'retirement-journal-after.json', retirement_after)
    for relative, original in retirement_before.items(): assert retirement_after[relative] == original
    original_tasks = {key.split('/')[0] for key in retirement_before}
    assert {path.name for path in JOURNAL.iterdir()} == original_tasks | set(tasks)
    assert current['protected'] == before['protected'] and current['disks'] == before['disks']
    assert current['spaces'] == before['spaces'] and current['runtime'] == before['runtime']
    assert current['jobs']['active'] == current['jobs']['locks'] == 0
    for identifier, original in before['physical'].items():
        actual = current['physical'][identifier]
        if identifier == guard.WORKSPACE:
            assert dict(actual, marker=None) == dict(original, marker=None)
            assert actual['marker']['state'] == 'Active' and actual['marker']['epoch'] == original['marker']['epoch'] + 2
        else: assert actual == original
    for name in ('hdd', 'journal'):
        for relative, original in before[name].items(): assert current[name][relative] == original
    archive_prefix = 'workspace-archives-v1/' + value['archiveId']
    assert set(current['hdd']) - set(before['hdd']) == {archive_prefix,
        archive_prefix + '/request.json', archive_prefix + '/receipt.json', archive_prefix + '/payload.tar.gz'}
    for table, original in before['rows'].items():
        if table != 'development_workspaces': assert collections.Counter(original) <= collections.Counter(current['rows'][table]), table
    hdd = Path('/data/DevSpace/workspace-archives-v1') / value['archiveId']
    receipt = json.loads((hdd / 'receipt.json').read_bytes())
    assert dict(receipt, receiptSha256=hashlib.sha256(guard.canonical(receipt)).hexdigest()) == capture
    payload = hdd / 'payload.tar.gz'; info = payload.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_ino == capture['archiveInode'] and info.st_size == capture['archiveBytes']
    with payload.open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest() == capture['archiveSha256']
    for name, digest in sources.items(): assert hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest
    assert publication == {str(path.relative_to(CLIENT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (CLIENT / 'bin/Release/net10.0/publish').iterdir() if path.is_file()}
    save(evidence, 'accepted.json', dict(passed=True, revision=revision, installedRetirementPlan=True, realIdentityArchive=True,
        trustedQaOnly=True, activePlanRefused=True, prestartCancel=True, taskCount=4, productionWorkspaces=5,
        originalReportsRetained=len(before['rows']['workspace_run_reports']), sourceQuotaBudgetRetained=True,
        retainedNewHddCopies=1, temporaryAccountRemoved=True, activeJobs=0, scope='No source retirement or application budget release'))
    print(json.dumps(dict(passed=True, revision=revision, installedRetirementReadonlyPlan=True, activePlanRefused=True)))


if __name__ == '__main__':
    try: main()
    except Exception as failure:
        print(json.dumps(dict(passed=False, failureType=type(failure).__name__)), file=sys.stderr)
        raise SystemExit(1)
