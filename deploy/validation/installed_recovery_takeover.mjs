// Only the new QA owner's group is removed, until B's recovery is verified.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { beginTakeoverAttempt } from './retirement_takeover_attempt.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

export async function finishRevokedOwnerTakeover(args, result) {
  const { page, context, base, api, route, workspace, state, record, save, evidence } = args
  const proof = record.recovery.takeover
  const prefix = `takeover-${proof.attempt}`
  const verified = JSON.parse(await readFile(join(evidence, prefix+'-while-revoked.json'), 'utf8'))
  const restored = JSON.parse(await readFile(join(evidence, prefix+'-restored.json'), 'utf8'))
  assert.equal(verified.passed, true); assert.equal(verified.originalGroupStillAbsent, true)
  assert.equal(restored.originalAuthorityRestored, true)
  assert.equal(result.state, 'Succeeded')
  proof.whileRevoked = { takeoverVerified: true, originalGroupStillAbsent: true }
  proof.restoration = { restored: true, originalAuthorityRestored: true }
  const endpoint = `${api}/${workspace.id}/retirements/${record.recovery.failedView.retirementId}`
  assert.equal((await context.request.get(base+endpoint)).status(), 200)
  const owner = await context.request.get(base+'/api/v1/auth/me'); assert.equal(owner.status(), 200)
  const profile = await owner.json(); assert.equal(profile.subject, state.ownerSubject); assert.equal(profile.isAdministrator, true)
  await page.goto(`${base}${route}/${workspace.id}/environment`)
  proof.passed = true; await save()
  return result
}

export async function withRevokedOwnerTakeover(args, perform) {
  const { page, context, base, api, route, workspace, state, record, retirement, save, headers, evidence, authorityUsername } = args
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_TAKEOVER_PROBE, '1')
  assert.ok(args.recoveryAdministrator)
  record.recovery ??= {}
  record.recovery.failedView ??= retirement
  const previous = record.recovery.takeover
  const restored = previous ? JSON.parse(await readFile(join(evidence, `takeover-${previous.attempt}-restored.json`), 'utf8')) : undefined
  const proof = beginTakeoverAttempt(record.recovery, args.authorityAttempt, restored)
  await save()
  const child = spawn('/usr/bin/python3', ['-B', fileURLToPath(new URL('./owned_recovery_takeover.py', import.meta.url))], { stdio: ['pipe', 'pipe', 'pipe'] })
  let stderr = '', spawnError
  child.on('error', error => { spawnError = error })
  const exited = new Promise(resolve => child.once('close', (code, signal) => resolve({ code, signal })))
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]()
  child.stderr.on('data', bytes => { stderr = (stderr + bytes).slice(-8192) })
  async function next() {
    let timer
    try {
      const row = await Promise.race([lines.next(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('TAKEOVER_AUTHORITY_CHILD_DEADLINE')), 90_000)
      })])
      assert.equal(spawnError, undefined)
      assert.equal(row.done, false, stderr)
      return JSON.parse(row.value)
    } finally { clearTimeout(timer) }
  }
  async function waitExit() {
    let timer
    try {
      return await Promise.race([exited, new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('TAKEOVER_AUTHORITY_RESTORE_DEADLINE')), 90_000)
      })])
    } finally { clearTimeout(timer) }
  }
  try {
    child.stdin.write(JSON.stringify({ evidence, username: authorityUsername, subject: state.ownerSubject,
      workspaceId: workspace.id, retirementId: retirement.retirementId, attempt: args.authorityAttempt })+'\n')
    proof.removal = await next(); assert.equal(proof.removal.removed, true)
    await save()
    const endpoint = `${api}/${workspace.id}/retirements/${retirement.retirementId}`
    const denied = await context.request.post(base+endpoint+'/recover', {
      headers: { ...await headers(context), 'Idempotency-Key': proof.deniedKey },
      data: { previousTaskId: retirement.executeTaskId,
        planSha256: JSON.parse(retirement.planJson).retirementPlanSha256, continueDeletionAndReleaseBudget: true },
    })
    proof.recoveryStatus = denied.status()
    proof.historyStatus = (await context.request.get(base+endpoint+'/attempts')).status()
    await save()
    assert.equal(proof.recoveryStatus, 403); assert.equal(proof.historyStatus, 403)
    const currentOwner = async () => {
      const response = await context.request.get(base+'/api/v1/auth/me')
      assert.equal(response.status(), 200)
      const profile = await response.json()
      assert.equal(profile.subject, state.ownerSubject); assert.equal(profile.isAdministrator, false)
    }
    await currentOwner()
    proof.originalOwnerAuthenticatedWithoutAdministrator = true
    child.stdin.write('check\n')
    proof.denial = await next(); assert.equal(proof.denial.denialWindowUnchanged, true)
    await save()
    // The provider group is still absent throughout B's genuine login/recovery.
    const result = await perform(args)
    assert.equal(result.state, 'Succeeded')
    await currentOwner()
    proof.ownerStillWithoutAdministratorAfterBSuccess = true
    child.stdin.write('finish\n')
    proof.whileRevoked = await next()
    assert.equal(proof.whileRevoked.takeoverVerified, true)
    assert.equal(proof.whileRevoked.originalGroupStillAbsent, true)
    proof.restoration = await next(); assert.equal(proof.restoration.originalAuthorityRestored, true)
    child.stdin.end(); proof.childExit = await waitExit(); assert.equal(proof.childExit.code, 0, stderr)
    return finishRevokedOwnerTakeover(args, result)
  } finally {
    // EOF restores the original group before the Python parent permits retry
    // or generated-account cleanup. Never SIGKILL an authority restorer.
    child.stdin.end()
    proof.childExit = await waitExit()
    // Preserve the successful EOF restoration even when B/UI threw before a
    // normal restore reply. The parent checks the same root-owned receipt.
    const receipt = JSON.parse(await readFile(join(evidence, `takeover-${proof.attempt}-restored.json`), 'utf8'))
    assert.equal(receipt.originalAuthorityRestored, true)
    proof.restoration = { restored: true, originalAuthorityRestored: true }
    if (args.takeoverRetryProbe && record.recovery.takeoverRetryAbort?.attempt === proof.attempt && !proof.passed) {
      const endpoint = `${api}/${workspace.id}/retirements/${retirement.retirementId}`
      assert.equal((await context.request.get(base+endpoint)).status(), 200)
      await page.goto(`${base}${route}/${workspace.id}/environment`)
      proof.ownerAbortLogout = await logoutFromUserMenu(page, context, base)
    }
    await save()
  }
}
