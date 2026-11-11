// Evidence windows belong to explicit browser attempts, not product task IDs.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { join } from 'node:path'

export function beginTakeoverAttempt(recovery, attempt, restored) {
  assert.ok(Number.isInteger(attempt) && attempt >= 1 && attempt <= 4)
  const previous = recovery.takeover
  if (previous) {
    assert.notEqual(previous.passed, true, 'NEVER_REPEAT_COMPLETED_TAKEOVER')
    assert.ok(attempt > previous.attempt, 'EXPLICIT_NEW_BROWSER_ATTEMPT_REQUIRED')
    assert.equal(restored?.passed, true)
    assert.equal(restored?.originalAuthorityRestored, true)
    assert.equal(restored?.originalGroupRestored, true)
    recovery.takeoverHistory ??= []
    assert.ok(!recovery.takeoverHistory.some(row => row.attempt === previous.attempt))
    recovery.takeoverHistory.push(structuredClone(previous))
    if (recovery.administrator) {
      recovery.administratorHistory ??= []
      recovery.administratorHistory.push({ attempt: previous.attempt, proof: structuredClone(recovery.administrator) })
      // A new login must still be the same generated second identity.
      recovery.administrator = { subject: recovery.administrator.subject }
    }
  }
  return recovery.takeover = { deniedKey: randomUUID(), attempt }
}

export async function abortTakeoverAfterRelease(args) {
  const recovery = args.record.recovery
  if (!args.takeoverRetryProbe || recovery.takeoverRetryAbort) return
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_RETRY_PROBE, '1')
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_PROBE, '1')
  assert.equal(args.authorityAttempt, 1)
  assert.equal(recovery.takeover.attempt, 1)
  assert.equal(recovery.release?.released, true)
  assert.equal(recovery.release?.holderExit?.code, 0)
  assert.equal(recovery.openUnlinked?.links, 0)
  assert.equal(recovery.key, undefined)
  assert.equal(recovery.accepted, undefined)
  recovery.takeoverRetryAbort = { attempt: 1, point: 'after-fd-release-before-ui-recover',
    taskId: args.retirement.executeTaskId, retirementId: args.retirement.retirementId,
    deliberateLocalAbort: true, noProductResponseMocked: true }
  await args.save()
  if (args.page) await args.page.screenshot({ path: join(args.evidence, 'attempt-1-explicit-local-abort.png'), fullPage: true })
  throw new Error('QA_EXPLICIT_TAKEOVER_ABORT_AFTER_RELEASE')
}
