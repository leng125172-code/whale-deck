// Installed Authentik/Vue only. No routed responses, injected sessions or native writes.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { registerWaitingDirectory } from './restore_cross_directory.mjs'

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
  const mutations = [], headerChecks = [], retryRequests = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (/^\/api\/v1\/jobs\/[^/]+\/retry/.test(path)) retryRequests.push(path)
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
  const cut = await waitSignal(evidence, 'unknown-ready.json', 115_000)
  assert.equal(cut.id, ready.id); assert.equal(cut.executionJobId, pending.executionJobId)
  const beforeLogout = await get(endpoint + '/' + ready.id)
  assert.equal(beforeLogout.state, 'Unknown'); assert.equal(beforeLogout.receipt.state, 'Unknown')
  assert.equal(beforeLogout.receipt.restoredByThisOperation, null)
  phase = 'normal-owner-logout-after-unknown'
  await page.goto(base + '/jobs')
  const me = await get('/api/v1/auth/me'); assert.equal(me.subject, profile.subject); assert.equal(me.isAdministrator, true)
  const ownerLogout = await logoutFromUserMenu(page, context, base)
  const anonymous = await context.request.get(base + endpoint + '/' + ready.id); assert.equal(anonymous.status(), 401)
  for (const key of ['whaledeck-container-recycle-restore-hints-v1', 'whaledeck-container-recycle-hints-v1'])
    await expect.poll(() => page.evaluate(k => sessionStorage.getItem(k), key)).toBe(null)
  assert.equal(mutations.filter(p => p === recycle).length, 1); assert.equal(mutations.filter(p => p === endpoint).length, 1)
  assert.equal(mutations.filter(p => p.endsWith('/confirm')).length, 2); assert.equal(mutations.filter(p => p.endsWith('/cancel')).length, 0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean)); assert.deepEqual(retryRequests, [])
  await context.close()
  const recovery = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  recovery.setDefaultTimeout(20_000)
  const adminPage = await recovery.newPage(); page = adminPage; phase = 'visible-recovery-login'
  const admin = await loginVisible(adminPage, recovery, base, '/files', true, { username: process.env.RECOVERY_USERNAME, password: process.env.RECOVERY_PASSWORD })
  assert.notEqual(admin.subject, profile.subject)
  const recoveryPath = '/api/v1/files/container-recycle-restores/recovery/' + ready.id
  let managementWrites = 0, acknowledgements = 0
  recovery.on('request', r => {
    const u = new URL(r.url())
    if (u.pathname.startsWith('/api/v1/jobs/') && u.pathname.split('/')[5]?.startsWith('retry')) retryRequests.push(u.pathname)
    if (u.origin === base && ['POST', 'PATCH', 'PUT', 'DELETE'].includes(r.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')) {
      managementWrites++; if (r.method() === 'POST' && u.pathname === recoveryPath + '/acknowledge') acknowledgements++
    }
  })
  phase = 'different-admin-unknown'
  const oldId = await recovery.request.get(base + endpoint + '/' + ready.id); assert.equal(oldId.status(), 404)
  const initialUnknown = await recovery.request.get(base + recoveryPath); assert.equal(initialUnknown.status(), 200); const unknown = await initialUnknown.json()
  assert.equal(unknown.state, 'Unknown'); assert.equal(unknown.executionJobId, pending.executionJobId); assert.equal(unknown.receipt.state, 'Unknown')
  for (const key of ['sourceState', 'sourceRetained', 'sourceRestored', 'restoredByThisOperation']) assert.equal(unknown.receipt[key], null)
  await writeSignal(evidence, 'unknown-value.json', unknown); await waitSignal(evidence, 'unknown-proved.json')
  await adminPage.goto(base + '/containers'); const showWaiting = await registerWaitingDirectory(adminPage, base, fixture, admin)
  async function showJob(state, title, screenshot) {
    const listed = adminPage.waitForResponse(r => isJobPageResponse(r, base)); await adminPage.goto(base + '/jobs')
    const jobs = await (await listed).json(), index = jobs.findIndex(j => j.id === pending.executionJobId); assert.ok(index >= 0); assert.equal(jobs[index].state, state)
    await adminPage.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
    const detail = adminPage.getByRole('dialog', { name: '任务详情', exact: true }), summary = detail.getByRole('region', { name: '独立恢复安全结果', exact: true })
    await expect(summary).toContainText(title); await expect(summary).toContainText('未证明'); await expect(summary).toContainText(source.id)
    await expect(detail.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
    await adminPage.screenshot({ path: join(evidence, screenshot) }); await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  }
  await showJob('Running', '结果未知，仍需核实原任务', 'unknown-job.png')
  await adminPage.goto(base + '/files'); await adminPage.getByRole('button', { name: '容器文件恢复异常', exact: true }).click()
  const dialogRecovery = adminPage.getByRole('dialog', { name: '容器文件恢复异常处置', exact: true })
  const row = dialogRecovery.locator('.restore-recovery__row').filter({ hasText: ready.id })
  await expect(row).toHaveCount(1); await expect(row).toContainText('恢复结果未知，保护已保留'); await expect(row).toContainText('尚未证明本操作归属'); await expect(row).toContainText('曾验证留存')
  await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await expect(dialogRecovery.getByRole('button', { name: '暂不处置', exact: true })).toBeInViewport({ ratio: 1 })
  await expect(dialogRecovery.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeInViewport({ ratio: 1 })
  await adminPage.screenshot({ path: join(evidence, 'unknown-confirm.png') })
  await dialogRecovery.getByRole('button', { name: '暂不处置', exact: true }).click(); assert.equal(acknowledgements, 0); assert.equal(managementWrites, 2)
  await writeSignal(evidence, 'recovery-canceled.json', { id: ready.id, subject: admin.subject, acknowledgements }); await waitSignal(evidence, 'acknowledge.json', 120_000)
  await dialogRecovery.getByRole('button', { name: '刷新异常文件恢复任务', exact: true }).click()
  await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
  const posted = adminPage.waitForResponse(r => new URL(r.url()).pathname === recoveryPath + '/acknowledge' && r.request().method() === 'POST')
  await dialogRecovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click(); assert.equal((await posted).status(), 202)
  await expect(dialogRecovery.getByRole('status')).toHaveText('已处置历史未知结果并释放保护；不代表文件恢复成功，不会重复修改或自动恢复回收，不删除原对象。')
  await expect(row).toHaveCount(0); await adminPage.screenshot({ path: join(evidence, 'acknowledged.png') })
  const finalResponse = await recovery.request.get(base + recoveryPath); assert.equal(finalResponse.status(), 200); const result = await finalResponse.json()
  assert.equal(result.state, 'Acknowledged'); assert.equal(result.executionJobId, pending.executionJobId); assert.equal(result.receipt.state, 'Unknown'); assert.equal(result.receipt.restoredByThisOperation, null)
  await writeSignal(evidence, 'waiting-acknowledged.json', { id: ready.id, version: result.version })
  await dialogRecovery.getByRole('button', { name: '关闭', exact: true }).click(); await showJob('Failed', '管理员已明确处置未知结果', 'acknowledged-job.png')
  const settled = await waitSignal(evidence, 'waiting-finished.json', 120_000); const waitingDirectory = await showWaiting(recovery, settled)
  assert.equal(acknowledgements, 1); assert.equal(managementWrites, 3); assert.deepEqual(retryRequests, [])
  const recoveryLogout = await logoutFromUserMenu(adminPage, recovery, base)
  const privateDirectory = await recovery.request.get(base + waitingDirectory.privateEndpoint); assert.equal(privateDirectory.status(), 401); waitingDirectory.anonymousStatus = privateDirectory.status()
  await writeSignal(evidence, 'browser.json', { revision, subject: profile.subject, recoverySubject: admin.subject, source, ready, result, registrations: 1, confirmations: 1, cancellations: 0, acknowledgements, managementWrites, originalSessionReadStatus: anonymous.status(), differentAdminOldIdStatus: oldId.status(), hintsCleared: true, cancelledDecisionPreserved: true, logoutReceipts: [ownerLogout, recoveryLogout], actualUi: true, unknownHelperExitedBeforeLogout: true, retryRequests, waitingDirectory })
  await recovery.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRestoreCrossDirectory: 'passed' }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_restore_cross_directory.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-recovery-login'].includes(phase)) await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRestoreCrossDirectory: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
