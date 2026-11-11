import test from 'node:test'
import assert from 'node:assert/strict'
import { beginTakeoverAttempt, abortTakeoverAfterRelease } from './retirement_takeover_attempt.mjs'

const restored = { passed: true, originalAuthorityRestored: true, originalGroupRestored: true }
const old = () => ({ takeover: { attempt: 1, deniedKey: 'old-key', denial: { denialWindowUnchanged: true },
  restoration: { originalAuthorityRestored: true }, childExit: { code: 0 } },
  administrator: { subject: 'same-second-subject', logout: { normalUiLogout: true, bffStatus: 401 } } })

test('regression: new browser attempt advances the pointer used by the finalizer', () => {
  const legacy = old(); legacy.takeover ??= { attempt: 2 }
  assert.equal(legacy.takeover.attempt, 1) // The original defect.
  const recovery = old(); const current = beginTakeoverAttempt(recovery, 2, restored)
  assert.equal(current.attempt, 2)
  assert.notEqual(current.deniedKey, 'old-key')
  assert.equal(recovery.takeoverHistory[0].attempt, 1)
})

test('old denial, restoration and B logout are deep copies rather than aliases', () => {
  const recovery = old(); const original = structuredClone(recovery)
  const current = beginTakeoverAttempt(recovery, 2, restored)
  current.denial = { denialWindowUnchanged: false }; recovery.administrator.logout = { bffStatus: 200 }
  assert.deepEqual(recovery.takeoverHistory[0], original.takeover)
  assert.deepEqual(recovery.administratorHistory[0], { attempt: 1, proof: original.administrator })
  assert.equal(recovery.administrator.subject, original.administrator.subject)
})

test('missing or incomplete original-group restoration blocks a new window', () => {
  for (const invalid of [undefined, { ...restored, passed: false }, { ...restored, originalGroupRestored: false },
    { ...restored, originalAuthorityRestored: false }]) {
    const recovery = old(); const before = structuredClone(recovery)
    assert.throws(() => beginTakeoverAttempt(recovery, 2, invalid))
    assert.deepEqual(recovery, before)
  }
})

test('completed, repeated and older windows cannot repeat revocation', () => {
  const completed = old(); completed.takeover.passed = true
  assert.throws(() => beginTakeoverAttempt(completed, 2, restored))
  for (const attempt of [0, 1, 5, 1.5]) assert.throws(() => beginTakeoverAttempt(old(), attempt, restored))
})

test('browser failures before any revocation may leave a harmless gap in window IDs', () => {
  assert.equal(beginTakeoverAttempt({}, 2).attempt, 2)
  assert.equal(beginTakeoverAttempt(old(), 3, restored).attempt, 3)
})

test('a duplicate history entry is rejected before replacing evidence', () => {
  const recovery = old(); recovery.takeoverHistory = [structuredClone(recovery.takeover)]
  const before = structuredClone(recovery)
  assert.throws(() => beginTakeoverAttempt(recovery, 2, restored)); assert.deepEqual(recovery, before)
})

test('local abort is opt-in, follows FD exit, precedes product POST, and occurs once', async () => {
  const keys = ['WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_RETRY_PROBE', 'WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_PROBE']
  const saved = keys.map(key => process.env[key]); for (const key of keys) process.env[key] = '1'
  try {
    let saves = 0
    const recovery = { takeover: { attempt: 1 }, release: { released: true, holderExit: { code: 0 } }, openUnlinked: { links: 0 } }
    const args = { record: { recovery }, takeoverRetryProbe: true, authorityAttempt: 1,
      retirement: { executeTaskId: 'old-task', retirementId: 'same-intent' }, save: async () => { saves++ } }
    await assert.rejects(abortTakeoverAfterRelease(args), /QA_EXPLICIT_TAKEOVER_ABORT_AFTER_RELEASE/)
    assert.equal(recovery.takeoverRetryAbort.taskId, 'old-task'); assert.equal(saves, 1)
    await abortTakeoverAfterRelease({ ...args, authorityAttempt: 2 }); assert.equal(saves, 1)
    const posted = { ...recovery, takeoverRetryAbort: undefined, key: 'already-posted' }
    await assert.rejects(abortTakeoverAfterRelease({ ...args, record: { recovery: posted } }))
    assert.equal(posted.takeoverRetryAbort, undefined)
  } finally { keys.forEach((key, i) => { if (saved[i] === undefined) delete process.env[key]; else process.env[key] = saved[i] }) }
})
