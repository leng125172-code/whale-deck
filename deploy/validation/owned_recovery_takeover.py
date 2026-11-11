"""Keep only a declared generated owner's group revoked during B's recovery.

Original proof/plan/time stay immutable while the current workflow is allowed
to advance under the second actor. Restore only the original group, including
EOF/failure; never write business SQL or export credentials/login bindings.
"""
import json
import os
from pathlib import Path
import sys
import uuid

import requests
from owned_recovery_authority import OwnedRecoveryAuthority, check_binding, checkpoint
from retirement_administrator_proof import observe_administrator_recovery
from run_with_ephemeral_admin import container_environment, require
from validate_workspace_archive_faults_installed import sql


def immutable_evidence(retirement, task):
    assert str(uuid.UUID(retirement)) == retirement and str(uuid.UUID(task)) == task
    rows = sql("SELECT json_build_object('workspaceOwner',w.owner_subject,'jobActor',j.actor_subject,'oldTask',s.id,'oldStepSha256',encode(sha256(convert_to(to_jsonb(s)::text,'utf8')),'hex'),'planSha256',encode(sha256(convert_to(r.plan_json::text,'utf8')),'hex'),'snapshotSha256',encode(sha256(convert_to(r.snapshot_json::text,'utf8')),'hex'),'archiveId',r.archive_id,'expectedEpoch',r.expected_epoch,'confirmedAtUtc',r.confirmed_at_utc,'planExpiresAtUtc',r.plan_expires_at_utc) FROM development_workspace_retirement_steps s JOIN development_workspace_retirement_workflows r ON r.id=s.retirement_id JOIN development_workspaces w ON w.id=r.workspace_id JOIN operation_jobs j ON j.id=r.id WHERE r.id='"+retirement+"' AND s.id='"+task+"';")
    assert len(rows) == 1
    return rows[0]


def require_takeover_window(before, after, owner, authority_still_revoked, administrator_proof):
    assert authority_still_revoked is True
    assert before == after and before['workspaceOwner'] == before['jobActor'] == owner
    assert administrator_proof['passed'] is True
    assert len(administrator_proof['rows']) == 1
    row = administrator_proof['rows'][0]
    assert row['workspaceOwner'] == row['jobActor'] == owner
    assert row['recoveryActor'] == row['executionActor'] and row['recoveryActor'] != owner
    assert row['previousTaskId'] == before['oldTask']


def main():
    assert os.geteuid() == 0 and __debug__ and len(sys.argv) == 1
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_PROBE') == '1'
    assert os.environ.get('WHALEDECK_INSTALLED_RETIREMENT_ADMINISTRATOR_PROBE') == '1'
    raw = sys.stdin.readline(8193); assert len(raw) <= 8192 and raw.endswith('\n')
    payload = json.loads(raw)
    assert set(payload) == {'evidence', 'username', 'subject', 'workspaceId', 'retirementId', 'attempt'}
    evidence = Path(payload['evidence']).resolve(strict=True)
    assert evidence.is_relative_to(Path(__file__).resolve().parents[2]/'deploy/artifacts') and evidence.is_dir()
    state = json.loads((evidence/'state.json').read_bytes()); declaration = json.loads((evidence/'declaration.json').read_bytes())
    before = checkpoint(payload['retirementId']); check_binding(payload, state, declaration, before)
    original = immutable_evidence(payload['retirementId'], before['executeStepId'])
    attempt = payload['attempt']; assert type(attempt) is int and 1 <= attempt <= 4
    os.umask(0o077)
    def save(suffix, value):
        target = evidence/f'takeover-{attempt}-{suffix}.json'; pending = target.with_suffix('.pending')
        with pending.open('x') as stream:
            json.dump(value, stream, indent=2); stream.flush(); os.fsync(stream.fileno())
        os.link(pending, target); pending.unlink()
    save('before', dict(checkpoint=before, immutable=original))
    values = container_environment()
    with requests.Session() as client:
        client.trust_env = False
        client.headers['Authorization'] = 'Bearer '+require(values, 'Authentication__Authentik__ApiToken')
        action = OwnedRecoveryAuthority(client, payload['username'], payload['subject'],
            require(values, 'Authentication__Authentik__AdministratorGroupId'), kind='group-removal')
        try:
            save('intent', dict(subject=payload['subject'], kind='group-removal', onlyOriginalGroupRestorable=True))
            action.remove()
            save('removed', dict(passed=True, ownerSubject=payload['subject'], onlyOwnedGroupRemoved=True))
            print(json.dumps(dict(removed=True)), flush=True)
            command = sys.stdin.readline(32)
            if command == '':
                return
            assert command == 'check\n' and action.current() == action.revoked
            denied = checkpoint(payload['retirementId']); assert before == denied
            save('denial-window', dict(passed=True, before=before, after=denied))
            print(json.dumps(dict(denialWindowUnchanged=True)), flush=True)
            command = sys.stdin.readline(32)
            if command == '':
                return
            assert command == 'finish\n' and action.current() == action.revoked
            state = json.loads((evidence/'state.json').read_bytes())
            actor_proof = observe_administrator_recovery(state, sql)
            after = immutable_evidence(payload['retirementId'], before['executeStepId'])
            require_takeover_window(original, after, payload['subject'], action.current() == action.revoked, actor_proof)
            save('while-revoked', dict(passed=True, originalGroupStillAbsent=True, before=original, after=after,
                administratorProof=actor_proof))
            print(json.dumps(dict(takeoverVerified=True, originalGroupStillAbsent=True)), flush=True)
        finally:
            action.restore()
            save('restored', dict(passed=True, originalAuthorityRestored=True, originalGroupRestored=True))
            print(json.dumps(dict(restored=True, originalAuthorityRestored=True)), flush=True)


if __name__ == '__main__':
    main()
