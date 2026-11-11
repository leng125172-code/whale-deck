import test from 'node:test'
import assert from 'node:assert/strict'
import { takeoverKind, requireRevokedOwnerIdentity, requireRevocationReceipt } from './takeover_revocation_state.mjs'

test('active non-admin and disabled identity have distinct HTTP requirements', () => {
  requireRevokedOwnerIdentity('group-removal', 200, { subject: 'owner', isAdministrator: false }, 'owner')
  requireRevokedOwnerIdentity('account-disable', 403, undefined, 'owner')
})

test('missing authentication or stale admin profile cannot count as active non-admin', () => {
  for (const status of [401, 403]) assert.throws(() => requireRevokedOwnerIdentity('group-removal', status, undefined, 'owner'))
  assert.throws(() => requireRevokedOwnerIdentity('group-removal', 200, { subject: 'owner', isAdministrator: true }, 'owner'))
  assert.throws(() => requireRevokedOwnerIdentity('group-removal', 200, { subject: 'other', isAdministrator: false }, 'owner'))
})

test('disabled test rejects cached success and an expired/missing-session 401', () => {
  for (const status of [200, 401]) assert.throws(() => requireRevokedOwnerIdentity('account-disable', status, undefined, 'owner'))
})

test('disabled receipt must preserve groups and prove inactive through B success', () => {
  const value = { passed: true, kind: 'account-disable', originalAccountStillDisabled: true, originalGroupsUnchanged: true }
  requireRevocationReceipt('account-disable', value)
  for (const key of ['originalAccountStillDisabled', 'originalGroupsUnchanged'])
    assert.throws(() => requireRevocationReceipt('account-disable', { ...value, [key]: false }))
  assert.throws(() => requireRevocationReceipt('group-removal', value))
  assert.throws(() => requireRevocationReceipt('account-disable', { passed: true, originalGroupStillAbsent: true }))
})

test('legacy group receipt remains valid; unsupported mutations are rejected', () => {
  requireRevocationReceipt('group-removal', { passed: true, originalGroupStillAbsent: true })
  assert.throws(() => requireRevocationReceipt('group-removal', { passed: true, originalGroupStillAbsent: false }))
  assert.equal(takeoverKind(), 'group-removal')
  assert.throws(() => takeoverKind('delete-account'))
})
