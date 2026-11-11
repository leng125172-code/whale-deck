// Preserve the distinction between an active non-admin and an inactive user.
import assert from 'node:assert/strict'

export function takeoverKind(value = 'group-removal') {
  assert.ok(['group-removal', 'account-disable'].includes(value))
  return value
}

export function requireRevokedOwnerIdentity(kind, status, profile, subject) {
  takeoverKind(kind)
  if (kind === 'account-disable') {
    assert.equal(status, 403, 'DISABLED_OWNER_MUST_BE_REJECTED_BY_IDENTITY_SOURCE')
  } else {
    assert.equal(status, 200)
    assert.equal(profile.subject, subject)
    assert.equal(profile.isAdministrator, false)
  }
}

export function requireRevocationReceipt(kind, value) {
  takeoverKind(kind)
  assert.equal(value.passed ?? value.takeoverVerified, true)
  if (value.kind !== undefined) assert.equal(value.kind, kind)
  if (kind === 'account-disable') {
    assert.equal(value.originalAccountStillDisabled, true)
    assert.equal(value.originalGroupsUnchanged, true)
  } else assert.equal(value.originalGroupStillAbsent, true)
}
