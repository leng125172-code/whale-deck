"""Read-only evidence for one deliberate local abort and explicit same-intent retry."""
import hashlib
import json

from owned_recovery_authority import OwnedRecoveryAuthority, checkpoint
from owned_recovery_takeover import immutable_evidence


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def retry_ready(evidence, state, client, accounts, group):
    rec = state['retirementBusiness']['recovery']; abort = rec['takeoverRetryAbort']
    assert state['browserAttempts'] == abort['attempt'] == 1
    assert abort['point'] == 'after-fd-release-before-ui-recover' and abort['deliberateLocalAbort']
    assert rec['release']['released'] and rec['release']['holderExit']['code'] == 0
    assert rec['openUnlinked']['links'] == 0 and 'key' not in rec and 'accepted' not in rec
    original = json.loads((evidence/'takeover-1-before.json').read_bytes())
    assert checkpoint(abort['retirementId']) == original['checkpoint']
    assert immutable_evidence(abort['retirementId'], abort['taskId']) == original['immutable']
    restored = json.loads((evidence/'takeover-1-restored.json').read_bytes())
    assert restored['originalAuthorityRestored'] and restored['originalGroupRestored']
    assert not (evidence/'takeover-1-while-revoked.json').exists()
    assert rec['administrator']['logout']['normalUiLogout'] and rec['administrator']['logout']['bffStatus'] == 401
    assert rec['takeover']['ownerAbortLogout']['normalUiLogout'] and rec['takeover']['ownerAbortLogout']['bffStatus'] == 401
    # Only look up the same generated users. No PATCH, password or new account.
    for credentials, subject in ((accounts[0], state['ownerSubject']), (accounts[2], rec['administrator']['subject'])):
        actor = OwnedRecoveryAuthority(client, credentials['username'], subject, group)
        assert actor.current() == actor.before
    return dict(passed=True, workspaceId=state['workspace']['id'], retirementId=abort['retirementId'],
        previousTaskId=abort['taskId'], sameAccountsRetained=True, originalGroupsConfirmed=True,
        originalCheckpoint=original['checkpoint'], originalImmutable=original['immutable'],
        releaseSha256=digest(rec['release']), heldSha256=digest(rec['held']), openUnlinkedSha256=digest(rec['openUnlinked']),
        runIds=[row['id'] for row in state['runs']], importIds=[row['id'] for row in state['imports']])


def require_retry_success(state, ready):
    rec = state['retirementBusiness']['recovery']; current = rec['takeover']
    assert ready['passed'] and state['browserAttempts'] == 2 and current['attempt'] == 2 and current['passed']
    assert state['workspace']['id'] == ready['workspaceId']
    assert rec['failedView']['retirementId'] == ready['retirementId']
    assert rec['attemptsBefore'][0]['taskId'] == ready['previousTaskId']
    assert rec['attemptsAfter'][0] == rec['attemptsBefore'][0] and len(rec['attemptsAfter']) == 2
    history = rec['takeoverHistory']; assert len(history) == 1 and history[0]['attempt'] == 1
    assert history[0]['restoration']['originalAuthorityRestored'] and history[0]['childExit']['code'] == 0
    assert history[0]['ownerAbortLogout']['normalUiLogout'] and history[0]['ownerAbortLogout']['bffStatus'] == 401
    assert not history[0].get('passed') and history[0]['deniedKey'] != current['deniedKey']
    old_actor = rec['administratorHistory']; assert len(old_actor) == 1 and old_actor[0]['attempt'] == 1
    assert old_actor[0]['proof']['subject'] == rec['administrator']['subject']
    assert old_actor[0]['proof']['logout']['normalUiLogout']
    assert digest(rec['release']) == ready['releaseSha256']
    assert digest(rec['held']) == ready['heldSha256'] and digest(rec['openUnlinked']) == ready['openUnlinkedSha256']
    assert [row['id'] for row in state['runs']] == ready['runIds']
    assert [row['id'] for row in state['imports']] == ready['importIds']
