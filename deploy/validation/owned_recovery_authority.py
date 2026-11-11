"""Opt-in generated-QA group removal or account disable, restored on EOF.

No password/token crosses argv, stdout or evidence. This is never installed as
a privileged product helper. Only the declared new QA owner can be changed.
"""
import json
import os
from pathlib import Path
import re
import sys
import uuid

import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_workspace_archive_faults_installed import sql


class OwnedRecoveryAuthority:
    def __init__(self, client, username, subject, group, *, kind='group-removal'):
        assert kind in ('group-removal', 'account-disable')
        assert re.fullmatch('whaledeck-platform-ui-[a-f0-9]{32}', username)
        assert str(uuid.UUID(subject)) == subject and group
        self.client, self.username, self.subject, self.group = client, username, subject, group
        response = client.get(AUTHENTIK_API+'core/users/', params={'username': username, 'page_size': 2}, timeout=10)
        assert response.status_code == 200
        found = response.json()
        assert not found.get('pagination', {}).get('next') and len(found['results']) == 1
        self.before = self.facts(found['results'][0])
        identifier = str(self.before['pk'])
        assert re.fullmatch('[1-9][0-9]{0,11}', identifier)
        assert self.before['is_active'] is True and self.before['groups'] == [group]
        self.patch = {'groups': []} if kind == 'group-removal' else {'is_active': False}
        self.original_patch = {'groups': [group]} if kind == 'group-removal' else {'is_active': True}
        self.revoked = dict(self.before, **self.patch)
        self.url = AUTHENTIK_API+'core/users/'+identifier+'/'
        self.changed = False

    def facts(self, value):
        assert value['username'] == self.username and value['uuid'] == self.subject
        assert type(value['is_active']) is bool and type(value['groups']) is list
        return {key: value[key] for key in ('pk', 'uuid', 'is_active', 'groups')}

    def current(self):
        response = self.client.get(self.url, timeout=10)
        assert response.status_code == 200
        return self.facts(response.json())

    def remove(self):
        assert not self.changed and self.current() == self.before
        self.changed = True  # A lost PATCH reply is not permission to retry it.
        response = self.client.patch(self.url, json=self.patch, timeout=10)
        assert response.status_code == 200
        assert self.facts(response.json()) == self.revoked
        assert self.current() == self.revoked

    def restore(self):
        if not self.changed:
            return
        current = self.current()
        assert current in (self.before, self.revoked)
        if current != self.before:
            response = self.client.patch(self.url, json=self.original_patch, timeout=10)
            assert response.status_code == 200 and self.facts(response.json()) == self.before
        assert self.current() == self.before


def check_binding(payload, state, declaration, checkpoint):
    assert re.fullmatch('whaledeck-platform-ui-[a-f0-9]{32}', payload['username'])
    for name in ('workspaceId', 'subject', 'retirementId'):
        assert str(uuid.UUID(payload[name])) == payload[name]
    assert re.fullmatch('qa-native-[a-f0-9]{24}', declaration['slug'])
    assert state['slug'] == declaration['slug'] == checkpoint['slug']
    assert payload['workspaceId'] == state['workspace']['id'] == checkpoint['workspaceId']
    assert payload['workspaceId'] not in declaration['originalWorkspaceIds']
    assert payload['subject'] == state['ownerSubject'] == checkpoint['subject']
    assert payload['retirementId'] == checkpoint['retirementId']
    failed = state['retirementBusiness']['recovery']['failedView']
    assert failed['retirementId'] == payload['retirementId'] and failed['attentionRequired']
    assert failed['executeTaskId'] == checkpoint['executeStepId']
    assert checkpoint['lifecycle'] == 'Retiring' and checkpoint['attention'] is True
    assert checkpoint['lease'] is None and checkpoint['finished'] is None
    assert checkpoint['recoveries'] == checkpoint['completions'] == 0
    assert checkpoint['status'] == 'Rejected' and checkpoint['settled'] is not None


def checkpoint(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    rows = sql("SELECT json_build_object('workspaceId',w.id,'subject',w.owner_subject,'slug',w.slug,'lifecycle',w.lifecycle_state,'retirementId',r.id,'executeStepId',r.execute_step_id,'attention',r.attention_required,'lease',r.lease,'finished',r.finished_at_utc,'status',s.proof_json->>'status','settled',s.settled_at_utc,'workflowSha256',encode(sha256(convert_to(to_jsonb(r)::text,'utf8')),'hex'),'stepSha256',encode(sha256(convert_to(to_jsonb(s)::text,'utf8')),'hex'),'workspaceSha256',encode(sha256(convert_to(to_jsonb(w)::text,'utf8')),'hex'),'jobSha256',encode(sha256(convert_to((SELECT to_jsonb(j) FROM operation_jobs j WHERE j.id=r.id)::text,'utf8')),'hex'),'recoveries',(SELECT count(*) FROM development_workspace_retirement_recoveries a WHERE a.retirement_id=r.id),'completions',(SELECT count(*) FROM development_workspace_retirement_completions c WHERE c.retirement_id=r.id)) FROM development_workspace_retirement_workflows r JOIN development_workspaces w ON w.id=r.workspace_id JOIN development_workspace_retirement_steps s ON s.id=r.execute_step_id WHERE r.id='"+identifier+"';")
    assert len(rows) == 1
    return rows[0]


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 1
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE') == '1'
    raw = sys.stdin.readline(8193)
    assert len(raw) <= 8192 and raw.endswith('\n')
    payload = json.loads(raw)
    assert set(payload) == {'evidence', 'username', 'subject', 'workspaceId', 'retirementId', 'attempt', 'kind'}
    assert payload['kind'] == os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_KIND', 'group-removal')
    assert payload['kind'] in ('group-removal', 'account-disable')
    evidence = Path(payload['evidence']).resolve(strict=True)
    root = Path(__file__).resolve().parents[2]/'deploy/artifacts'
    assert evidence.is_relative_to(root) and evidence.is_dir()
    state = json.loads((evidence/'state.json').read_bytes())
    declaration = json.loads((evidence/'declaration.json').read_bytes())
    before = checkpoint(payload['retirementId'])
    check_binding(payload, state, declaration, before)
    attempt = payload['attempt']
    assert type(attempt) is int and 1 <= attempt <= 4
    os.umask(0o077)
    def save(suffix, value):
        target = evidence/f'authority-{attempt}-{suffix}.json'
        pending = target.with_suffix('.pending')
        with pending.open('x') as stream:
            json.dump(value, stream, indent=2)
            stream.flush(); os.fsync(stream.fileno())
        os.link(pending, target)  # Atomic publication without replacing evidence.
        pending.unlink()
    save('before', before)
    values = container_environment()
    with requests.Session() as client:
        client.trust_env = False
        client.headers['Authorization'] = 'Bearer '+require(values, 'Authentication__Authentik__ApiToken')
        action = OwnedRecoveryAuthority(client, payload['username'], payload['subject'],
            require(values, 'Authentication__Authentik__AdministratorGroupId'), kind=payload['kind'])
        try:
            save('intent', dict(subject=payload['subject'], kind=payload['kind'], onlyOriginalAuthorityRestorable=True))
            action.remove()
            save('removed', dict(passed=True, subject=payload['subject'], kind=payload['kind'], onlyOwnedIdentityChanged=True))
            print(json.dumps(dict(removed=True, kind=payload['kind'])), flush=True)
            command = sys.stdin.readline(32)
            assert command in ('restore\n', '')
        finally:
            # Stdin EOF covers normal browser failure/exit as well as explicit
            # restoration. Account cleanup in the parent remains a final fence.
            action.restore()
            after = checkpoint(payload['retirementId'])
            save('restored', dict(passed=before == after, originalGroupRestored=True, originalAuthorityRestored=True, kind=payload['kind'],
                before=before, after=after, workflowAndProofAndBudgetUnchanged=before == after))
            print(json.dumps(dict(restored=True, unchanged=before == after, kind=payload['kind'])), flush=True)
            assert before == after, 'REVOKED_RECOVERY_CHANGED_LEDGER'


if __name__ == '__main__':
    main()
