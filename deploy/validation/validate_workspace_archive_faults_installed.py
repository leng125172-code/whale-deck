#!/usr/bin/env python3
"""Opt-in real archive network-loss/restart/revocation proof; retain all data.

Only the fixed QA workspace, three generated identities and exact installed
Worker container are controlled. No SQL writes, signature/key changes or
source/quota removal. All recovery is through the existing product APIs.
"""
import collections
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time
import uuid

import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_platform_maintenance_browser import account, fingerprint, runtime, jobs
from validate_container_directory_restart import Worker, preflight

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = 'c23526c5-fc43-4944-9f27-75ac540f6df0'


class OwnedArchiveIdentityRevocation:
    """One mutation of an exact generated account, with no retry after dispatch."""
    def __init__(self, client, username, subject, group):
        assert re.fullmatch('whaledeck-platform-ui-[a-f0-9]{32}', username)
        assert str(uuid.UUID(subject)) == subject and group
        self.client, self.username, self.subject, self.group = client, username, subject, group
        self.changed = False
        found = client.get(AUTHENTIK_API + 'core/users/', params={'username': username, 'page_size': 2}, timeout=10)
        assert found.status_code == 200
        users = found.json()
        assert not users.get('pagination', {}).get('next') and len(users['results']) == 1
        self.before = self.facts(users['results'][0])
        pk = str(self.before['pk']); assert re.fullmatch('[1-9][0-9]{0,11}', pk)
        assert self.before['is_active'] is True and self.before['groups'] == [group]
        self.url = AUTHENTIK_API + 'core/users/' + pk + '/'

    def facts(self, user):
        assert user['username'] == self.username and user['uuid'] == self.subject
        assert type(user['is_active']) is bool and type(user['groups']) is list
        return {key: user[key] for key in ('pk', 'uuid', 'username', 'is_active', 'groups')}

    def change(self, kind):
        assert kind in ('group-removal', 'account-disable') and not self.changed
        current = self.client.get(self.url, timeout=10)
        assert current.status_code == 200 and self.facts(current.json()) == self.before
        payload = dict(groups=[]) if kind == 'group-removal' else dict(is_active=False)
        self.changed = True
        changed = self.client.patch(self.url, json=payload, timeout=10)
        assert changed.status_code == 200
        expected = dict(self.before, **payload)
        assert self.facts(changed.json()) == expected
        confirmed = self.client.get(self.url, timeout=10)
        assert confirmed.status_code == 200 and self.facts(confirmed.json()) == expected
        return dict(passed=True, subject=self.subject, onlyOwnedIdentityChanged=True,
            groupRemoved=kind == 'group-removal', accountDisabled=kind == 'account-disable')


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('ascii')


def sql(query):
    raw = subprocess.check_output(['docker', 'exec', '-i', '-u', 'postgres',
        'database-platform-postgres', 'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1',
        '-d', 'whaledeck'], input=query.encode(), timeout=30)
    return [json.loads(row) for row in raw.splitlines() if row]


def archive(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    return sql("SELECT json_build_object('intent',to_jsonb(a)-'login_binding'-'snapshot_json',"
        "'job',json_build_object('state',j.state,'phase',j.phase,'code',j.error_code,'completed',j.completed_at_utc),"
        "'steps',(SELECT json_agg(to_jsonb(s) ORDER BY s.phase) FROM development_workspace_archive_steps s WHERE s.archive_id=a.id),"
        "'audits',(SELECT json_agg(action ORDER BY occurred_at_utc) FROM audit_events WHERE job_id=a.id)) "
        "FROM development_workspace_archives a JOIN operation_jobs j ON j.id=a.id WHERE a.id='" + identifier + "';")[0]


def tree(root):
    result = {}
    for path in sorted(root.rglob('*')):
        info = path.lstat()
        assert not path.is_symlink() and (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode))
        row = dict(inode=info.st_ino, device=info.st_dev, uid=info.st_uid, gid=info.st_gid, mode=info.st_mode)
        if stat.S_ISREG(info.st_mode):
            with path.open('rb') as stream:
                row.update(size=info.st_size, mtimeNs=info.st_mtime_ns, sha256=hashlib.file_digest(stream, 'sha256').hexdigest())
        result[str(path.relative_to(root))] = row
        assert len(result) <= 20000
    return result


def snapshot():
    spaces = sql('SELECT to_json(id) FROM development_workspaces ORDER BY id;')
    physical = {identifier: json.loads(subprocess.check_output(['python3', '-B',
        str(ROOT / 'deploy/validation/inspect_development_workspace_lifecycle.py'), identifier], timeout=30)) for identifier in spaces}
    tables = sql("SELECT to_json(table_name) FROM information_schema.tables WHERE table_schema='public' AND "
        "(table_name LIKE 'development_%' OR table_name IN ('audit_events','workspace_run_reports','operation_jobs','operation_job_events'));")
    rows = {}
    for name in tables:
        assert re.fullmatch('[a-z_]+', name)
        rows[name] = sql("SELECT to_json(encode(sha256(convert_to(to_jsonb(t)::text,'utf8')),'hex')) FROM " + name + ' t;')
    protected = ['/data/GitRepos/whale-deck/.env', '/data/GitRepos/whale-deck/.images.env',
        '/etc/sudoers.d/90-whaledeck-development-user', '/etc/whaledeck/development-storage.json',
        '/etc/whaledeck/development-workspaces.env', '/var/lib/whaledeck-development-disks/layout.json']
    layout = json.loads(Path(protected[-1]).read_bytes())
    disks = {}
    for key, target, limit in (('runtime', '/dataNvme/DevSpace', 120000000000), ('archive', '/data/DevSpace', 300000000000)):
        planned = layout['plan'][key]; image = Path(planned['image']).stat()
        assert planned['target'] == target and planned['sizeBytes'] == image.st_size == limit
        assert image.st_blocks * 512 >= limit and image.st_ino == layout['images'][key]['inode']
        mount = json.loads(subprocess.check_output(['findmnt', '-J', '-T', target, '-o',
            'TARGET,SOURCE,FSTYPE,OPTIONS,UUID'], timeout=10))['filesystems'][0]
        assert mount['uuid'] == layout['images'][key]['uuid'] and mount['fstype'] == 'ext4'
        if key == 'runtime':
            assert 'prjquota' in mount['options'].split(',')
        capacity = os.statvfs(target); total = capacity.f_blocks * capacity.f_frsize
        assert total <= limit
        disks[key] = dict(bytes=limit, reservedBytes=image.st_blocks * 512, inode=image.st_ino, filesystemBytes=total, mount=mount)
    return dict(rows=rows, physical=physical,
        spaces=sql("SELECT to_jsonb(t)-'lifecycle_state'-'lifecycle_version' FROM development_workspaces t ORDER BY id;"),
        protected={name: fingerprint(Path(name)) for name in protected}, disks=disks,
        hdd=tree(Path('/data/DevSpace')), journal=tree(Path('/var/lib/whaledeck-development-archive-operations')),
        runtime=json.loads(json.dumps(runtime())), jobs=jobs())


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 3
    assert os.environ.get('WHALEDECK_INSTALLED_ARCHIVE_FAULTS_PROBE') == '1'
    revision, evidence = sys.argv[1], Path(sys.argv[2]).resolve(strict=True)
    assert re.fullmatch('[a-f0-9]{40}', revision) and evidence.is_dir() and not (evidence / 'state.json').exists()
    os.umask(0o077)
    def save(name, value):
        target = evidence / name
        with target.open('x') as stream:
            json.dump(value, stream, indent=2)
    def signal(name, value):
        pending = evidence / (name + '.pending')
        with pending.open('x') as stream:
            json.dump(value, stream)
        pending.rename(evidence / name)
    before = snapshot(); preflight()
    assert before['physical'][WORKSPACE]['marker']['state'] == 'Active'
    save('before.json', before)
    worker, process = Worker(), None
    browser_unit = 'whaledeck-qa-archive-faults-' + uuid.uuid4().hex + '.service'
    assert worker.original['Config']['Labels']['org.opencontainers.image.revision'] == revision
    worker_id = worker.id
    sources = {}
    for name in ('validate_workspace_archive_faults_installed.py', 'validate_workspace_archive_faults_installed.mjs',
                 'installed_development_lifecycle.mjs', 'inspect_development_workspace_lifecycle.py',
                 'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs',
                 'validate_container_directory_restart.py', 'validate_platform_maintenance_browser.py',
                 'run_with_ephemeral_admin.py'):
        raw = (Path(__file__).parent / name).read_bytes()
        (evidence / ('source-' + name)).write_bytes(raw)
        sources[name] = hashlib.sha256(raw).hexdigest()
    save('source-sha256.json', sources)
    def wait(name, timeout=180):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if (evidence / 'browser-failed.json').exists():
                raise RuntimeError('ARCHIVE_BROWSER_FAILED')
            if (evidence / name).exists():
                raw = (evidence / name).read_bytes(); assert len(raw) <= 32768
                return json.loads(raw)
            assert process.poll() is None, 'ARCHIVE_BROWSER_EXITED'
            time.sleep(0.08)
        raise RuntimeError('ARCHIVE_CONTROLLER_DEADLINE')
    def settled_before_dispatch(identifier):
        current = archive(identifier)
        row = current['intent']; assert row['workspace_id'] == WORKSPACE
        assert row['lease'] is None and row['finished_at_utc'] is None
        assert not row['cancel_requested'] and len(current['steps']) == 1
        step = current['steps'][0]
        assert step['phase'] == 'plan' and step['dispatched_at_utc'] is None
        assert step['proof_json'] is step['settled_at_utc'] is None
        return current
    client, cleaned, identities = requests.Session(), [], {}
    client.trust_env = False
    error = None
    try:
        values = container_environment()
        client.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
        group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        with contextlib.ExitStack() as stack:
            for role in ('recovery', 'group-removal', 'account-disable'):
                identities[role] = stack.enter_context(account(client, 'administrator', group, cleaned))
            stdout = stack.enter_context((evidence / 'browser.stdout').open('x'))
            stderr = stack.enter_context((evidence / 'browser.stderr').open('x'))
            # A transient cgroup owns only this browser tree. Stopping it on a
            # failed recovery cannot select an unrelated Chrome or task.
            node = str(Path(shutil.which('node')).resolve(strict=True))
            save('browser-unit.json', dict(unit=browser_unit, executable=node))
            process = subprocess.Popen(['systemd-run', '--quiet', '--pipe', '--wait', '--collect',
                '--unit=' + browser_unit, '--property=Type=exec', '--property=KillMode=control-group',
                '--property=RuntimeMaxSec=1200', '--setenv=WHALEDECK_INSTALLED_ARCHIVE_FAULTS_PROBE=1',
                node, str(Path(__file__).with_suffix('.mjs'))],
                stdin=subprocess.PIPE, stdout=stdout, stderr=stderr, text=True)
            process.stdin.write(json.dumps(dict(revision=revision, evidence=str(evidence), accounts=identities)))
            process.stdin.close()
            try:
                initial = wait('restart-ready.json'); identifier = initial['archiveId']
                first = archive(identifier); assert first['intent']['awaiting_confirmation'] and first['intent']['lease'] is None
                assert len(first['steps']) == 1 and first['steps'][0]['settled_at_utc']
                preflight([identifier]); started = worker.current()['State']['StartedAt']
                worker.stop(); save('worker-stopped-waiting.json', archive(identifier)); worker.start()
                second = archive(identifier); assert first == second
                assert worker.current()['State']['StartedAt'] != started
                save('worker-restart-proof.json', dict(passed=True, containerId=worker_id, before=first, after=second))
                signal('worker-restarted.json', dict(passed=True))
                for kind in ('group-removal', 'account-disable'):
                    actor = wait(kind + '-ready.json')
                    assert str(uuid.UUID(actor['subject'])) == actor['subject']
                    preflight(); worker.stop(); signal(kind + '-worker-held.json', dict(passed=True))
                    queued = wait(kind + '-queued.json'); identifier = queued['archiveId']
                    original = settled_before_dispatch(identifier)
                    assert original['intent']['execution_subject'] == actor['subject']
                    revocation = OwnedArchiveIdentityRevocation(client, identities[kind]['username'], actor['subject'], group)
                    save(kind + '-identity-change.json', revocation.change(kind))
                    worker.start()
                    deadline = time.monotonic() + 100
                    while True:
                        paused = settled_before_dispatch(identifier)
                        if paused['intent']['authorization_required']:
                            break
                        assert time.monotonic() < deadline, 'CURRENT_REVOCATION_NOT_OBSERVED'
                        time.sleep(1)
                    assert paused['steps'] == original['steps']
                    assert paused['job']['code'] == 'WORKSPACE_ARCHIVE_AUTHORIZATION_REQUIRED'
                    assert not (Path('/data/DevSpace/workspace-archives-v1') / identifier).exists()
                    save(kind + '-predispatch-proof.json', dict(passed=True, before=original, paused=paused))
                    signal(kind + '-revocation-observed.json', dict(archiveId=identifier))
                    reauthorized = wait(kind + '-reauthorized.json'); assert reauthorized['archiveId'] == identifier
                    resumed = archive(identifier); assert resumed['intent']['awaiting_confirmation']
                    assert resumed['steps'][0]['id'] == original['steps'][0]['id']
                    assert resumed['steps'][0]['request_json'] == original['steps'][0]['request_json']
                    assert resumed['steps'][0]['proof_json']['status'] == 'Succeeded'
                    assert len(resumed['steps']) == 1 and not resumed['intent']['authorization_required']
                    assert not (Path('/data/DevSpace/workspace-archives-v1') / identifier).exists()
                    save(kind + '-same-task-proof.json', dict(passed=True, original=original, resumed=resumed))
                    signal(kind + '-same-task.json', dict(sameTaskAndCanonicalRequest=True))
                assert process.wait(timeout=300) == 0, 'INSTALLED_ARCHIVE_FAULTS_FAILED'
            except BaseException as failure:
                error = failure
                save('controller-failure.json', dict(failureType=type(failure).__name__))
            finally:
                worker.start()
                if error is not None:
                    signal('controller-abort.json', dict(failureType=type(error).__name__))
                    signal('recovery-worker-running.json', dict(passed=True))
                    try:
                        process.wait(timeout=240)
                    except subprocess.TimeoutExpired:
                        subprocess.run(['systemctl', 'stop', browser_unit], check=True, timeout=30)
                        process.wait(timeout=30)
            if error is not None:
                raise error
        save('identity-cleanup.json', dict(removed=cleaned))
        assert cleaned == ['administrator'] * 3
        state = json.loads((evidence / 'state.json').read_bytes())
        assert state['passed'] and state['lifecycle']['passed'] and state['logout']['bffStatus'] == 401
        assert len(state['archives']) == 3
        browser_processes = Path('/sys/fs/cgroup/system.slice') / browser_unit / 'cgroup.procs'
        assert not browser_processes.exists() or not browser_processes.read_text().strip()
        save('browser-cgroup-cleanup.json', dict(passed=True, unit=browser_unit, remainingProcesses=0))
        current = snapshot(); save('after.json', current)
        assert current['protected'] == before['protected'] and current['disks'] == before['disks']
        assert current['spaces'] == before['spaces']
        for identifier, original in before['physical'].items():
            actual = current['physical'][identifier]
            if identifier == WORKSPACE:
                assert dict(actual, marker=None) == dict(original, marker=None)
                assert actual['marker']['state'] == 'Active' and actual['marker']['epoch'] == original['marker']['epoch'] + 2
            else:
                assert actual == original
        for name in ('hdd', 'journal'):
            for relative, original in before[name].items():
                assert current[name][relative] == original, 'ORIGINAL_' + name.upper() + '_CHANGED'
        for table, original in before['rows'].items():
            if table != 'development_workspaces':
                assert collections.Counter(original) <= collections.Counter(current['rows'][table]), table
        worker_after = worker.current()
        original_cores = {row[0]: row for row in before['runtime']['core']}
        for row in current['runtime']['core']:
            original = original_cores[row[0]]
            assert row[:2] == original[:2] and row[3] == original[3] == 'healthy'
            assert row[2] != original[2] if row[0] == worker_id else row == original
        assert current['runtime']['hosts'] == before['runtime']['hosts']
        receipts = []
        for index, view in enumerate(state['archives']):
            identifier = view['archiveId']; proof = archive(identifier); receipts.append(proof)
            assert proof['intent']['finished_at_utc'] and proof['intent']['lease'] is None
            assert proof['job']['state'] == view['state'] == ('Succeeded' if index == 1 else 'Canceled')
            phases = {step['phase']: step for step in proof['steps']}
            assert len(phases) == len(proof['steps']) and set(phases) == ({'plan', 'capture', 'verify'} if index == 1 else {'plan'})
            for step in phases.values():
                result = step['proof_json']
                assert step['dispatched_at_utc'] and step['settled_at_utc'] and result['status'] == 'Succeeded'
                assert result['taskId'] == step['id'] and hashlib.sha256(step['request_json'].encode()).hexdigest() == result['requestSha256']
                unit = 'whaledeck-development-archive-run-' + step['id'].replace('-', '') + '.service'
                properties = subprocess.check_output(['systemctl', 'show', unit, '-p', 'MainPID', '-p', 'ControlGroup'], text=True)
                values = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
                assert values.get('MainPID', '0') == '0'
                if values.get('ControlGroup'):
                    processes = Path('/sys/fs/cgroup' + values['ControlGroup']) / 'cgroup.procs'
                    assert not processes.exists() or not processes.read_text().strip()
            if index == 0:
                assert view['transportFault']['kind'] == 'server-accepted-response-dropped' and view['workerRestartRecovered']
                assert view['transportFault']['serverArchiveId'] == identifier
            else:
                assert view['sameTaskAndCanonicalRequest'] and view['uiPollingStopped']
                assert len(view['revokedActorDenied']) == 4 and all(row['status'] in (401, 403) for row in view['revokedActorDenied'])
                assert 'development.workspace.archive.authorization-required' in proof['audits']
                assert 'development.workspace.archive.reauthorize' in proof['audits']
                assert proof['intent']['execution_subject'] == state['recoverySubject']
            hdd = Path('/data/DevSpace/workspace-archives-v1') / identifier
            if index != 1:
                assert proof['intent']['capture_json'] is proof['intent']['verify_json'] is None and not hdd.exists()
            else:
                capture, verify = proof['intent']['capture_json'], proof['intent']['verify_json']
                assert capture == verify == json.loads(view['verifyJson'])
                receipt = json.loads((hdd / 'receipt.json').read_bytes())
                assert dict(receipt, receiptSha256=hashlib.sha256(canonical(receipt)).hexdigest()) == capture
                payload = hdd / 'payload.tar.gz'; info = payload.lstat()
                assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == info.st_gid == 0
                assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_ino == capture['archiveInode'] and info.st_size == capture['archiveBytes']
                with payload.open('rb') as stream:
                    assert hashlib.file_digest(stream, 'sha256').hexdigest() == capture['archiveSha256']
        assert sources == {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in sources}
        save('independent-archive-proofs.json', receipts)
        save('accepted.json', dict(passed=True, revision=revision, realIdentityUi=True, droppedRealBeginReply=True,
            sameTaskAfterWorkerRestart=True, currentGroupRemovalAndAccountDisable=True, currentAdminReauthorization=True,
            temporaryAccountsRemoved=3, sourceQuotaBudgetRetained=True, activeJobs=0, retainedNewHddCopies=1,
            limitation='Predispatch revocation and waiting-plan Worker restart only; in-flight dispatch/lease-loss cuts still pending'))
        print(json.dumps(dict(passed=True, evidence=str(evidence), realFaultCases=3, temporaryAccountsRemoved=3)))
    finally:
        try:
            worker.start()
        finally:
            if not (evidence / 'identity-cleanup.json').exists():
                save('identity-cleanup.json', dict(removed=cleaned))
            client.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as failure:
        print(json.dumps(dict(passed=False, failureType=type(failure).__name__)), file=sys.stderr)
        raise SystemExit(1)
