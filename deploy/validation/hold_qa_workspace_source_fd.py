#!/usr/bin/env python3
"""Opt-in read-only FD fault for one exact, newly owned QA allocation.

Never writes source, quota, journals or SQL. The parent owns this process and
closes stdin to release the FD on any failure. Holding only a regular source FD
allows genuine product deletion while the kernel keeps its inode charged.
"""
import hashlib
import importlib.util
import json
import os
import re
import stat
import sys
import uuid

from validate_workspace_archive_faults_installed import sql


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 1
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE') == '1'
    raw = sys.stdin.readline(8193)
    assert len(raw) <= 8192 and raw.endswith('\n')
    value = json.loads(raw)
    assert set(value) == {'workspaceId', 'slug', 'ownerSubject', 'projectId'}
    identifier, slug = value['workspaceId'], value['slug']
    assert str(uuid.UUID(identifier)) == identifier
    assert re.fullmatch('qa-native-[a-f0-9]{24}', slug)
    assert type(value['projectId']) is int and 0 < value['projectId'] < 2**32
    rows = sql("SELECT json_build_object('id',id,'slug',slug,'owner',owner_subject,'state',lifecycle_state) FROM development_workspaces WHERE id='" + identifier + "';")
    assert len(rows) == 1 and rows[0] == dict(id=identifier, slug=slug, owner=value['ownerSubject'], state='Retiring')
    assert sql("SELECT to_json(count(*)) FROM development_workspace_retirement_workflows WHERE workspace_id='" + identifier + "' AND phase='plan' AND plan_json IS NOT NULL AND confirmed_at_utc IS NULL AND finished_at_utc IS NULL AND lease IS NULL;") == [1]
    spec = importlib.util.spec_from_file_location('installed_storage_transport',
        '/usr/local/libexec/whaledeck-development-storage-transport.py')
    loader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loader)
    files, storage, quota = (loader.load(name) for name in ('whaledeck-files.py',
        'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py'))
    driver = storage.WorkspaceStorage(files, quota=quota)
    held = None
    try:
        with driver.locked_root('nvme-main', exclusive=False) as (parent, _):
            proof = driver.read_json(parent, '.quota-' + str(value['projectId']) + '.json', 4096)
            assert proof['workspaceId'] == identifier and proof['projectId'] == value['projectId']
            marker = driver.read_json(parent, '.lifecycle-' + identifier + '.json', 4096)
            assert marker['state'] == 'Stopped' and marker['epoch'] >= 2 and marker['epoch'] % 2 == 0
            with driver.opened(parent, identifier, os.O_RDONLY | os.O_DIRECTORY) as allocation:
                with driver.opened(allocation, 'data', os.O_RDONLY | os.O_DIRECTORY) as payload:
                    quota.verify(payload, proof['projectId'], proof['diskMiB'])
                    with driver.opened(payload, 'work', os.O_RDONLY | os.O_DIRECTORY) as work:
                        # The fixed accessor adds CLOEXEC itself and rejects
                        # callers supplying flags outside its read-only list.
                        held = files.open_at(work, 'sum.mjs', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                        info = os.fstat(held)
                        expected = b'export const add = (a, b) => a + b;\n'
                        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_size == len(expected)
                        assert os.pread(held, len(expected) + 1, 0) == expected
        # No directory FD or allocation lock survives this point.
        def facts():
            info = os.fstat(held)
            with driver.locked_root('nvme-main', exclusive=False) as (parent, _):
                charged = list(quota.read_quota(parent, value['projectId']))
            attempts = sql("SELECT json_build_object('retirementId',r.id,'taskId',s.id,'requestSha256',encode(sha256(convert_to(s.request_json,'utf8')),'hex'),'proofSha256',CASE WHEN s.proof_json IS NOT NULL THEN encode(sha256(convert_to(s.proof_json::text,'utf8')),'hex') END,'dispatchedAtUtc',s.dispatched_at_utc,'settledAtUtc',s.settled_at_utc,'planSha256',encode(sha256(convert_to(r.plan_json::text,'utf8')),'hex'),'confirmedAtUtc',r.confirmed_at_utc,'planExpiresAtUtc',r.plan_expires_at_utc) FROM development_workspace_retirement_workflows r JOIN development_workspace_retirement_steps s ON s.id=r.execute_step_id WHERE r.workspace_id='" + identifier + "';")
            return dict(workspaceId=identifier, projectId=value['projectId'], inode=info.st_ino,
                device=info.st_dev, links=info.st_nlink, bytes=info.st_size,
                sha256=hashlib.sha256(os.pread(held, 4096, 0)).hexdigest(), kernelQuota=charged, attempts=attempts)
        print(json.dumps(dict(ready=True, **facts())), flush=True)
        for command in sys.stdin:
            assert command in ('inspect\n', 'release\n')
            if command == 'release\n':
                before = facts()
                assert before['links'] == 0 and (before['kernelQuota'][2] or before['kernelQuota'][5])
                os.close(held); held = None
                print(json.dumps(dict(released=True, before=before)), flush=True)
                return
            print(json.dumps(facts()), flush=True)
    finally:
        if held is not None:
            os.close(held)


if __name__ == '__main__':
    main()
