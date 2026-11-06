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
  assert.ok(['group-removal', 'account-disable'].includes(fixture.kind))
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
  let ownerLogoutPosts = 0
  page.on('request', r => { const u = new URL(r.url()); if (r.method() === 'POST' && u.origin === base && u.pathname === '/api/v1/auth/logout') ownerLogoutPosts++ })
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
  const settled = await waitSignal(evidence, 'settled.json', 210_000)
  assert.equal(settled.id, ready.id); assert.equal(settled.executionJobId, pending.executionJobId); assert.equal(settled.state, 'Succeeded')
  phase = 'revoked-original-session'
  const originalIntentStatus = (await context.request.get(base + endpoint + '/' + ready.id)).status()
  assert.ok([401, 403].includes(originalIntentStatus))
  const me = await context.request.get(base + '/api/v1/auth/me'), currentIdentityStatus = me.status()
  let currentSubject = null, currentlyAdministrator = null, renderedRevocationPage
  if (fixture.kind === 'group-removal') {
    assert.equal(originalIntentStatus, 403); assert.equal(me.status(), 200)
    const value = await me.json(); assert.equal(value.subject, profile.subject); assert.equal(value.isAdministrator, false)
    currentSubject = value.subject; currentlyAdministrator = false
  } else assert.ok([401, 403].includes(me.status()))
  await page.reload()
  await expect(page.getByRole('status', { name: '正在启动 WhaleDeck 协同平台', exact: true })).toHaveCount(0)
  if (fixture.kind === 'group-removal') {
    await expect(page).toHaveURL(base + '/forbidden')
    await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible(); renderedRevocationPage = 'forbidden'
  } else {
    await expect(page).toHaveURL(url => url.origin === base && url.pathname === '/login')
    await expect(page.getByRole('heading', { name: '使用工作站账号登录', exact: true })).toBeVisible(); renderedRevocationPage = 'login'
  }
  await expect(page.getByRole('region', { name: '独立文件恢复', exact: true })).toHaveCount(0)
  await expect(page.getByRole('dialog', { name: '恢复已回收的容器文件', exact: true })).toHaveCount(0)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-restore-hints-v1'))).toBe(null)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-hints-v1'))).toBe(null)
  await expect(page.getByRole('region', { name: '容器文件回收任务', exact: true })).toHaveCount(0)
  await page.screenshot({ path: join(evidence, 'authority-denied.png') })
  const registrations = mutations.filter(p => p === endpoint).length, confirmations = mutations.filter(p => p.startsWith(endpoint + '/') && p.endsWith('/confirm')).length, cancellations = mutations.filter(p => p.endsWith('/cancel')).length
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 0); assert.equal(ownerLogoutPosts, 0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  await context.close()

  const recovery = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  recovery.setDefaultTimeout(20_000)
  const adminPage = await recovery.newPage(); page = adminPage; phase = 'visible-recovery-login'
  const admin = await loginVisible(adminPage, recovery, base, '/jobs', true, { username: process.env.RECOVERY_USERNAME, password: process.env.RECOVERY_PASSWORD })
  assert.notEqual(admin.subject, profile.subject)
  let managementWrites = 0
  recovery.on('request', r => {
    const u = new URL(r.url()); if (u.origin === base && ['POST', 'PATCH', 'PUT', 'DELETE'].includes(r.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')) managementWrites++
  })
  phase = 'current-admin-original-result'
  const recoveredResponse = await recovery.request.get(base + '/api/v1/files/container-recycle-restores/recovery/' + ready.id)
  assert.equal(recoveredResponse.status(), 200); const result = await recoveredResponse.json()
  assert.equal(result.id, ready.id); assert.equal(result.executionJobId, pending.executionJobId)
  assert.equal(result.state, 'Succeeded'); assert.equal(result.receipt.state, 'Completed'); assert.equal(result.confirmation, null)
  assert.equal(result.receipt.sourceState, 'Restored'); assert.equal(result.receipt.sourceRetained, false); assert.equal(result.receipt.sourceRestored, true); assert.equal(result.receipt.restoredByThisOperation, true)
  const jobResponse = await recovery.request.get(base + '/api/v1/jobs/' + result.executionJobId); assert.equal(jobResponse.status(), 200); const job = await jobResponse.json(), safe = JSON.parse(job.resultJson)
  assert.equal(Object.keys(safe).length, 17); assert.equal(safe.sourceIntentId, source.id); assert.equal(safe.sourceOperationId, source.executionJobId); assert.equal(safe.restoredByThisOperation, true)
  await coordinate('revocation', result)
  const listing = adminPage.waitForResponse(r => isJobPageResponse(r, base))
  await adminPage.reload(); const jobs = await (await listing).json(), index = jobs.findIndex(v => v.id === result.executionJobId)
  assert.ok(index >= 0)
  await adminPage.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
  const detail = adminPage.getByRole('dialog', { name: '任务详情', exact: true })
  await expect(detail).toContainText(result.executionJobId)
  await expect(detail.getByRole('region', { name: '独立恢复安全结果', exact: true })).toContainText('文件恢复已完成')
  await adminPage.screenshot({ path: join(evidence, 'original-completed-job.png') })
  await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  assert.equal(managementWrites, 0)
  phase = 'current-admin-normal-logout'; const recoveryLogout = await logoutFromUserMenu(adminPage, recovery, base)
  await writeSignal(evidence, 'browser.json', { revision, containerId: fixture.containerId, source, ready, key, job, mutations, kind: fixture.kind, subject: profile.subject, recoverySubject: admin.subject, result, registrations, confirmations, cancellations, ownerLogoutPosts, managementWrites, originalIntentStatus, currentIdentityStatus, currentSubject, currentlyAdministrator, renderedRevocationPage, privatePanelsAbsent: true, hintsCleared: true, recoveryLogout, actualUi: true })
  await recovery.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRestorePostreturnRevocation: 'passed', kind: fixture.kind }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_restore_postreturn_revocation.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-recovery-login'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRestorePostreturnRevocation: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
