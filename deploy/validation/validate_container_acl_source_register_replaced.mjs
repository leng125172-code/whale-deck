// Valid new history selection, then valid source replacement BEFORE registration; no fake Job.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal, waitSignal, closePermissionDetails } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { selectReplacedSourceRegistration } from './acl_restore_fault_ui.mjs'

let browser, context, page, directory, dialog, loggedOut = false, phase = 'startup'
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8')), revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.equal(fixture.aclSourceFaultVersion, 7); assert.equal(fixture.kind, 'source-register-replaced')
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-acl-source-register-replaced-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080', endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  let registrations = 0, confirmations = 0, cancellations = 0, contentWrites = 0, jobWrites = 0, key
  const events = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === endpoint + '/restores') { registrations++; key = request.headers()['idempotency-key']; events.push('restore-registration') }
    if (request.method() === 'POST' && path.startsWith(endpoint + '/') && path.endsWith('/confirm')) confirmations++
    if (request.method() === 'POST' && path.startsWith(endpoint + '/') && path.endsWith('/cancel')) cancellations++
    if (request.method() === 'PUT' && path.includes('/files/')) contentWrites++
    if (request.method() !== 'GET' && path.startsWith('/api/v1/jobs')) jobWrites++
  })
  // Ignore the preceding ordinary capture's legitimate confirmation.
  phase = 'normal-login'; const profile = await loginVisible(page, context, base)
  phase = 'new-capture-and-visible-history-selection'
  const selected = await selectReplacedSourceRegistration(page, context, base, fixture, profile)
  dialog = selected.dialog; confirmations = 0; cancellations = 0
  assert.equal(registrations, 0)
  await writeSignal(directory, 'selected.json', { source: selected.source, subject: profile.subject })
  const changed = await waitSignal(directory, 'source-replaced.json', 60_000)
  const entry = selected.source.entry
  const historyUrl = base + endpoint + '/history/' + entry.captureId + '?instanceId=' + encodeURIComponent(fixture.instanceId) + '&path=work%2Facl-file.bin&expectedHistoryDigest=' + entry.historyDigest
  phase = 'real-selected-source-validly-replaced'
  const replaced = await context.request.get(historyUrl); assert.equal(replaced.status(), 409)
  const sourceError = await replaced.json(); assert.equal(sourceError.code, 'AGENT_VERSION_CONFLICT')
  const newRead = await context.request.get(historyUrl.replace(entry.historyDigest, changed.replacementDigest)); assert.equal(newRead.status(), 200)
  const replacementSnapshot = await newRead.json(), expectedReplacement = structuredClone(selected.source)
  expectedReplacement.entry.createdAtUnixMs++; expectedReplacement.entry.historyDigest = changed.replacementDigest
  assert.deepEqual(replacementSnapshot, expectedReplacement)
  const queued = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/restores' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成ACL恢复预检', exact: true }).click()
  const rejected = await queued; assert.equal(rejected.status(), 409)
  const problem = await rejected.json(); assert.equal(problem.code, 'OPERATION_CONFLICT')
  assert.match(key, /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/)
  const body = rejected.request().postDataJSON()
  assert.deepEqual(Object.keys(body).sort(), ['instanceId', 'path', 'captureId', 'expectedHistoryDigest', 'expectedVersion'].sort())
  assert.equal(body.instanceId, fixture.instanceId); assert.equal(body.path, 'work/acl-file.bin')
  assert.equal(body.captureId, entry.captureId); assert.equal(body.expectedHistoryDigest, entry.historyDigest)
  const alert = dialog.getByRole('alert').filter({ hasText: 'ACL恢复请求无法核实，请查询原任务' })
  await expect(alert).toContainText('不会自动重新提交或修改历史来源')
  await alert.scrollIntoViewIfNeeded(); await expect(alert).toBeInViewport({ ratio: 1 })
  await expect(dialog.getByRole('button', { name: '确认ACL历史恢复', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: '请求取消ACL修改', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: '重试原预检登记', exact: true })).toBeVisible()
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  await page.screenshot({ path: join(directory, 'registration-rejected-320-dark.png') })
  const byKey = await context.request.get(base + endpoint + '/restores/by-key/' + key); assert.equal(byKey.status(), 404)
  await writeSignal(directory, 'rejected.json', { subject: profile.subject, key, body, registrationStatus: 409, code: problem.code,
    sourceStatus: 409, sourceCode: sourceError.code, replacementReadStatus: 200, replacementSnapshot, byKeyStatus: 404, registrations, confirmations, cancellations, contentWrites, jobWrites })
  const restored = await waitSignal(directory, 'verified-rejection.json', 60_000)
  assert.equal(restored.captureId, entry.captureId); assert.equal(restored.originalRestored, true)
  const reread = await context.request.get(historyUrl); assert.equal(reread.status(), 200); assert.deepEqual(await reread.json(), selected.source)
  phase = 'query-original-missing-registration-only'
  const lookup = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/restores/by-key/' + key && r.request().method() === 'GET')
  await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click(); assert.equal((await lookup).status(), 404)
  const notFound = dialog.getByRole('alert').filter({ hasText: '尚未找到原登记' })
  await expect(notFound).toContainText('不会自动重新提交')
  const themes = []
  for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
    await page.setViewportSize({ width, height: 600 }); await page.emulateMedia({ colorScheme })
    await notFound.scrollIntoViewIfNeeded(); await expect(notFound).toBeInViewport({ ratio: 1 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    const screenshot = `registration-not-found-${width}-${colorScheme}.png`; await page.screenshot({ path: join(directory, screenshot) })
    themes.push({ width, colorScheme, screenshot, originalMissingVisible: true, noHorizontalOverflow: true })
  }
  for (const id of [restored.expectedIntentId, restored.expectedExecutionId]) {
    const job = await context.request.get(base + '/api/v1/jobs/' + id); assert.equal(job.status(), 404)
  }
  const original = await context.request.get(base + endpoint + '/restores/by-key/' + key); assert.equal(original.status(), 404)
  await writeSignal(directory, 'queried.json', { key, sourceReadableAgain: true, byKeyStill404: true, jobStatuses: [404, 404] })
  await waitSignal(directory, 'verified-query.json', 60_000)
  await closePermissionDetails(page, dialog)
  const logout = await logoutFromUserMenu(page, context, base); loggedOut = true
  assert.equal(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-acl-hints-v1')), null)
  assert.equal(registrations, 1); assert.equal(confirmations, 0); assert.equal(cancellations, 0); assert.equal(contentWrites, 0); assert.equal(jobWrites, 0)
  assert.deepEqual(events, ['restore-registration'])
  await writeSignal(directory, 'browser.json', { revision, aclSourceFaultVersion: 7, kind: fixture.kind, subject: profile.subject, key,
    actualUi: true, normalDualLogout: true, logout, registrations, confirmations, cancellations, contentWrites, jobWrites, events,
    themes, originalSourceReadStatus: 409, replacementReadStatus: 200, sourceReadableAgain: true, registrationStatus: 409, code: 'OPERATION_CONFLICT', originalStillMissing: true, hintsCleared: true })
} catch (error) {
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, type: error?.name ?? 'Error' }).catch(() => {})
  if (page && !page.isClosed() && phase !== 'normal-login') await page.screenshot({ path: join(directory, 'failed-gate-page.png') }).catch(() => {})
  console.error(JSON.stringify({ sourceRegisterReplaced: 'failed', phase, type: error?.name ?? 'Error' })); process.exitCode = 1
} finally {
  if (!loggedOut && page && context && !page.isClosed()) await logoutFromUserMenu(page, context, 'http://127.0.0.1:8080', dialog).catch(() => {})
  await context?.close(); await browser?.close()
}
