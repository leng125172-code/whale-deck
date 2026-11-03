// Only a NEW own capture. Actual Authentik/Vue/HTTP; no interception or auth injection.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal, waitSignal, closePermissionDetails } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { prepareReplacedSourceConfirmation } from './acl_restore_fault_ui.mjs'

let browser, context, page, directory, dialog, loggedOut = false, phase = 'startup'
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8')), revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.equal(fixture.aclSourceFaultVersion, 5); assert.equal(fixture.kind, 'source-confirm-replaced')
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-acl-source-confirm-replaced-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080', endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  const events = []; let registrations = 0, confirmations = 0, cancellations = 0, contentWrites = 0, key, id
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === endpoint + '/restores') {
      registrations++; key = request.headers()['idempotency-key']; events.push('restore-registration')
    }
    if (request.method() === 'POST' && id && path === endpoint + '/' + id + '/confirm') { confirmations++; events.push('restore-confirm') }
    if (request.method() === 'POST' && id && path === endpoint + '/' + id + '/cancel') { cancellations++; events.push('restore-cancel') }
    if (request.method() === 'PUT' && path.includes('/files/')) contentWrites++
  })
  phase = 'normal-login'; const profile = await loginVisible(page, context, base)
  phase = 'new-capture-and-visible-history-selection'
  const prepared = await prepareReplacedSourceConfirmation(page, context, base, fixture, profile)
  dialog = prepared.dialog; id = prepared.intent.id
  assert.match(key, /^[a-f0-9-]{36}$/)
  await expect(dialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 45_000 })
  const originalResponse = await context.request.get(base + endpoint + '/restores/' + id)
  assert.equal(originalResponse.status(), 200); const original = await originalResponse.json()
  assert.equal(original.intent.state, 'Ready'); assert.equal(original.intent.executionJobId, null)
  assert.deepEqual(original.source, prepared.source)
  await writeSignal(directory, 'ready.json', { id, subject: profile.subject, key, original })
  const substituted = await waitSignal(directory, 'source-replaced.json', 60_000)
  phase = 'real-selected-source-unreadable'
  const selected = prepared.source.entry
  const missing = await context.request.get(base + endpoint + '/history/' + selected.captureId + '?instanceId=' + encodeURIComponent(fixture.instanceId) + '&path=work%2Facl-file.bin&expectedHistoryDigest=' + selected.historyDigest)
  assert.equal(missing.status(), 409)
  assert.equal((await missing.json()).code, 'AGENT_VERSION_CONFLICT')
  assert.equal(substituted.captureId, selected.captureId); assert.equal(substituted.originalDigest, selected.historyDigest)
  assert.notEqual(substituted.replacementDigest, selected.historyDigest)
  const newRead = await context.request.get(base + endpoint + '/history/' + selected.captureId + '?instanceId=' + encodeURIComponent(fixture.instanceId) + '&path=work%2Facl-file.bin&expectedHistoryDigest=' + substituted.replacementDigest)
  assert.equal(newRead.status(), 200)
  const validReplacement = await newRead.json(), expectedReplacement = structuredClone(prepared.source)
  expectedReplacement.entry.createdAtUnixMs++; expectedReplacement.entry.historyDigest = substituted.replacementDigest
  assert.deepEqual(validReplacement, expectedReplacement)
  const originalKeyResponse = await context.request.get(base + endpoint + '/restores/by-key/' + key)
  assert.equal(originalKeyResponse.status(), 200)
  const originalByKey = await originalKeyResponse.json(); assert.deepEqual(originalByKey, original)
  phase = 'visible-confirmation-rejected'
  const waiting = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + id + '/confirm' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '确认ACL历史恢复', exact: true }).click()
  const rejected = await waiting; assert.equal(rejected.status(), 409)
  const problem = await rejected.json(); assert.equal(problem.code, 'OPERATION_CONFLICT')
  const alert = dialog.getByRole('alert').filter({ hasText: 'ACL恢复请求无法核实，请查询原任务' })
  await expect(alert).toContainText('未自动重试确认')
  await alert.scrollIntoViewIfNeeded(); await expect(alert).toBeInViewport({ ratio: 1 })
  await expect(dialog.getByRole('button', { name: '确认ACL历史恢复', exact: true })).toBeDisabled()
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  await page.screenshot({ path: join(directory, 'source-replaced-confirm-rejected-mobile-dark.png') })
  const unchangedResponse = await context.request.get(base + endpoint + '/restores/' + id)
  assert.equal(unchangedResponse.status(), 200); const unchanged = await unchangedResponse.json(); assert.deepEqual(unchanged, original)
  await writeSignal(directory, 'rejected.json', { id, subject: profile.subject, sourceStatus: missing.status(), sourceCode: 'AGENT_VERSION_CONFLICT', replacementReadStatus: 200, replacementSnapshot: validReplacement, confirmStatus: rejected.status(),
    code: problem.code, original, originalByKey, unchanged, registrations, confirmations, cancellations, contentWrites, events })
  const restored = await waitSignal(directory, 'source-restored.json', 60_000)
  assert.equal(restored.captureId, selected.captureId); assert.equal(restored.originalRestored, true)
  phase = 'real-source-readable-again'
  const reread = await context.request.get(base + endpoint + '/history/' + selected.captureId + '?instanceId=' + encodeURIComponent(fixture.instanceId) + '&path=work%2Facl-file.bin&expectedHistoryDigest=' + selected.historyDigest)
  assert.equal(reread.status(), 200); assert.deepEqual(await reread.json(), prepared.source)
  // Query the original reference, then explicitly cancel. Never retry confirmation.
  await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '请求取消ACL修改', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '请求取消ACL修改', exact: true }).click()
  await page.getByRole('button', { name: '继续等待', exact: true }).click(); assert.equal(cancellations, 0)
  await dialog.getByRole('button', { name: '请求取消ACL修改', exact: true }).click()
  await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect(dialog.getByRole('status')).toHaveText('已取消', { timeout: 45_000 })
  await dialog.getByRole('status').scrollIntoViewIfNeeded(); await expect(dialog.getByRole('status')).toBeInViewport({ ratio: 1 })
  await page.screenshot({ path: join(directory, 'source-replaced-original-canceled-mobile-dark.png') })
  const finalResponse = await context.request.get(base + endpoint + '/restores/' + id); assert.equal(finalResponse.status(), 200)
  const final = await finalResponse.json(); assert.equal(final.intent.state, 'Canceled'); assert.equal(final.intent.executionJobId, null)
  assert.deepEqual(final.source, prepared.source)
  await writeSignal(directory, 'canceled.json', { id, final })
  await waitSignal(directory, 'verified-row.json', 60_000)
  phase = 'normal-dual-logout'; await closePermissionDetails(page, dialog)
  const logout = await logoutFromUserMenu(page, context, base); loggedOut = true
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 1); assert.equal(contentWrites, 0)
  assert.deepEqual(events, ['restore-registration', 'restore-confirm', 'restore-cancel'])
  await writeSignal(directory, 'browser.json', { revision, aclSourceFaultVersion: 5, kind: fixture.kind, id, subject: profile.subject,
    actualUi: true, normalDualLogout: true, logout, registrations, confirmations, cancellations, contentWrites, events,
    originalSourceReadStatus: 409, replacementReadStatus: 200, confirmStatus: 409, code: 'OPERATION_CONFLICT', originalByKeyUnchanged: true, sourceReadableAgain: true,
    uncertainConfirmationDisabled: true, errorEntirelyInViewport: true, noHorizontalOverflow: true })
} catch (error) {
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, type: error?.name ?? 'Error' }).catch(() => {})
  console.error(JSON.stringify({ actualAclSourceReplaced: 'failed', phase, type: error?.name ?? 'Error' })); process.exitCode = 1
} finally {
  if (!loggedOut && page && context && !page.isClosed()) await logoutFromUserMenu(page, context, 'http://127.0.0.1:8080', dialog).catch(() => {})
  await context?.close(); await browser?.close()
}
