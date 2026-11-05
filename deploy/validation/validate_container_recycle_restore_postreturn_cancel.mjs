// Installed Authentik/Vue only. No routed responses, injected sessions or native writes.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, page, evidence
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base)
  const recycle = `/api/v1/containers/${fixture.containerId}/files/recycles`
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recycle-restores`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const mutations = [], headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === recycle || path.startsWith(recycle + '/') || path === endpoint || path.startsWith(endpoint + '/'))) {
      mutations.push(path); headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key'] && !!h[csrf.headerName.toLowerCase()]))
    }
    assert.ok(!(request.method() === 'PUT' && path.includes('/files/')))
  })
  async function get(path) { const response = await context.request.get(base + path); assert.equal(response.status(), 200); return response.json() }
  async function coordinate(name, value) {
    await writeSignal(evidence, name + '-completed.json', { subject: profile.subject, result: value })
    assert.equal((await waitSignal(evidence, name + '-accepted.json', 60_000)).id, value.id)
  }
  phase = 'page-create-original-recycle-source'
  let files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '回收 recycle-rpc.bin', exact: true }).click()
  const sourceDialog = page.getByRole('dialog', { name: '回收容器普通文件', exact: true })
  const sourceRegistration = page.waitForResponse(r => new URL(r.url()).pathname === recycle && r.request().method() === 'POST')
  await sourceDialog.getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  const sourceResponse = await sourceRegistration; assert.equal(sourceResponse.status(), 202)
  const sourceInitial = await sourceResponse.json()
  await expect(sourceDialog.getByText('预检完成，尚未移除文件', { exact: true })).toBeVisible({ timeout: 60_000 })
  const sourceReady = await get(recycle + '/' + sourceInitial.id)
  assert.equal(sourceReady.state, 'Ready'); assert.equal(sourceReady.sha256, fixture.original.sha256)
  await sourceDialog.getByRole('button', { name: '确认文件回收', exact: true }).click()
  await expect(sourceDialog.getByText('文件回收已完成', { exact: true })).toBeVisible({ timeout: 60_000 })
  const source = await get(recycle + '/' + sourceInitial.id)
  assert.equal(source.state, 'Succeeded'); assert.equal(source.receipt.sourceRetained, true)
  await coordinate('source', { ...source, name: 'trash' })
  await closePermissionDetails(page, sourceDialog)
  phase = 'restore-ready'
  files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '恢复已回收文件', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '恢复已回收的容器文件', exact: true })
  await dialog.locator('.restore-source-row').filter({ hasText: source.id }).getByRole('button', { name: '选择此来源', exact: true }).click()
  const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成独立恢复预检', exact: true }).click()
  const response = await registration; assert.equal(response.status(), 202)
  assert.deepEqual(response.request().postDataJSON(), { sourceIntentId: source.id })
  const initial = await response.json(), key = (await response.request().allHeaders())['idempotency-key']
  await expect(dialog.getByText('恢复预检完成，尚未写入', { exact: true })).toBeVisible({ timeout: 60_000 })
  const ready = await get(endpoint + '/' + initial.id)
  assert.equal(ready.state, 'Ready'); assert.equal(ready.executionJobId, null)
  assert.equal(ready.sourceIntentId, source.id); assert.equal(ready.sourceOperationId, source.executionJobId)
  await writeSignal(evidence, 'armed-intent.json', { id: ready.id, subject: profile.subject, ready })
  assert.equal((await waitSignal(evidence, 'observer-armed.json', 30_000)).id, ready.id)
  const confirm = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + ready.id + '/confirm' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '确认文件恢复', exact: true }).click()
  const confirmation = await confirm; assert.equal(confirmation.status(), 202)
  const pending = await confirmation.json(); assert.ok(pending.executionJobId)
  const cut = await waitSignal(evidence, 'cut-held.json', 115_000)
  assert.equal(cut.id, ready.id); assert.equal(cut.executionJobId, pending.executionJobId)
  phase = 'cancel-after-native-return'
  await expect(dialog.getByRole('button', { name: '请求取消文件恢复', exact: true })).toBeInViewport({ ratio: 1 })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  await page.screenshot({ path: join(evidence, 'held-before-cancel.png') })
  await dialog.getByRole('button', { name: '请求取消文件恢复', exact: true }).click()
  await writeSignal(evidence, 'cancel-request-began.json', { id: ready.id })
  const cancellation = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + ready.id + '/cancel' && r.request().method() === 'POST')
  await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  const canceled = await cancellation; assert.equal(canceled.status(), 202)
  await writeSignal(evidence, 'cancel-response.json', { id: ready.id, status: canceled.status() })
  await expect(dialog.getByText('恢复已取消', { exact: true })).toBeVisible({ timeout: 60_000 })
  const result = await get(endpoint + '/' + ready.id)
  assert.equal(result.state, 'Succeeded'); assert.equal(result.executionJobId, pending.executionJobId)
  assert.equal(result.receipt.state, 'Completed'); assert.equal(result.receipt.sourceState, 'Restored')
  assert.equal(result.receipt.sourceRetained, false); assert.equal(result.receipt.restoredByThisOperation, true)
  await coordinate('cancel', result)
  await page.screenshot({ path: join(evidence, 'canceled-result.png') }); await closePermissionDetails(page, dialog)
  phase = 'original-job-summary'; await page.setViewportSize({ width: 1440, height: 1050 })
  const listing = page.waitForResponse(r => isJobPageResponse(r, base)); await page.goto(base + '/jobs')
  const listed = await (await listing).json(), position = listed.findIndex(j => j.id === result.executionJobId)
  assert.ok(position >= 0); const job = await get('/api/v1/jobs/' + result.executionJobId), safe = JSON.parse(job.resultJson)
  assert.equal(Object.keys(safe).length, 17); assert.equal(safe.state, 'Succeeded'); assert.equal(safe.receiptState, 'Completed')
  assert.equal(safe.sourceIntentId, source.id); assert.equal(safe.sourceOperationId, source.executionJobId)
  assert.equal(safe.sourceRetained, false); assert.equal(safe.sourceRestored, true); assert.equal(safe.restoredByThisOperation, true)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button', { name: '详情', exact: true }).click()
  const detail = page.getByRole('dialog', { name: '任务详情', exact: true }), summary = detail.getByRole('region', { name: '独立恢复安全结果', exact: true })
  await expect(summary).toContainText('文件恢复已完成'); await expect(summary).toContainText(source.id)
  await expect(summary).toContainText(source.executionJobId); await page.screenshot({ path: join(evidence, 'canceled-job.png') })
  await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  assert.equal(mutations.filter(p => p === recycle).length, 1); assert.equal(mutations.filter(p => p === endpoint).length, 1)
  assert.equal(mutations.filter(p => p.endsWith('/confirm')).length, 2); assert.equal(mutations.filter(p => p.endsWith('/cancel')).length, 1)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase = 'normal-dual-logout'; await logoutFromUserMenu(page, context, base)
  phase = 'visible-relogin'; const again = await loginVisible(page, context, base); assert.equal(again.subject, profile.subject)
  for (const path of [endpoint + '/' + result.id, endpoint + '/by-key/' + key, recycle + '/' + source.id])
    assert.equal((await context.request.get(base + path)).status(), 404)
  phase = 'second-dual-logout'; await logoutFromUserMenu(page, context, base)
  await writeSignal(evidence, 'browser.json', { revision, containerId: fixture.containerId, subject: profile.subject, source, ready, result, key, job, mutations, normalDualLogouts: 2, oldReferencesDenied: 3, actualUi: true })
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRestorePostreturnCancelBrowser: 'passed' }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_restore_postreturn_cancel.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-relogin'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRestorePostreturnCancelBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
