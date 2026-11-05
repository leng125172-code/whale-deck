// Real installed UI and two isolated temporary identities; no session injection.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal, waitSignal, openPermissionFiles, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let browser, activePage, evidence, phase = 'startup'
try {
  const revision = process.argv[2], f = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(f.revision, revision)
  assert.match(f.containerId, /^[a-f0-9]{64}$/); assert.match(f.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = f.evidence
  assert.match(evidence, /\/deploy\/artifacts\/container-recycle-instance-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080', endpoint = `/api/v1/containers/${f.containerId}/files/recycles`
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage(); activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-owner-login'
  const profile = await loginVisible(page, context, base)
  let registrations = 0, confirmations = 0, cancellations = 0, ownerLogoutPosts = 0, logoutMarkerWrite
  page.on('request', r => {
    const u = new URL(r.url()); if (u.origin !== base || r.method() !== 'POST') return
    if (u.pathname === endpoint) registrations++
    if (u.pathname.startsWith(endpoint + '/') && u.pathname.endsWith('/confirm')) confirmations++
    if (u.pathname.startsWith(endpoint + '/') && u.pathname.endsWith('/cancel')) cancellations++
    if (u.pathname === '/api/v1/auth/logout') { ownerLogoutPosts++; if (!logoutMarkerWrite && r.isNavigationRequest()) logoutMarkerWrite = writeSignal(evidence, 'logout-request-began.json', { actualNavigationPost: true, path: u.pathname }) }
  })
  phase = 'visible-owner-preflight'
  const files = await openPermissionFiles(page, f)
  await files.getByRole('button', { name: '回收 recycle-rpc.bin', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '回收容器普通文件', exact: true })
  const accepted = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  const queued = await accepted; assert.equal(queued.status(), 202); const initial = await queued.json()
  await expect(dialog.getByText('预检完成，尚未移除文件', { exact: true })).toBeVisible({ timeout: 130_000 })
  await writeSignal(evidence, 'armed-intent.json', { id: initial.id, subject: profile.subject })
  await waitSignal(evidence, 'observer-armed.json')
  const confirmed = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + initial.id + '/confirm' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '确认文件回收', exact: true }).click()
  const confirmation = await confirmed; assert.equal(confirmation.status(), 202); const pending = await confirmation.json()
  assert.ok(pending.executionJobId)
  phase = 'waiting-bound-owned-instance-change'
  const cut = await waitSignal(evidence, 'cut-held.json', 115_000)
  assert.equal(cut.id, initial.id); assert.equal(cut.executionJobId, pending.executionJobId)
  phase = 'normal-owner-logout-after-owned-instance-change'
  await page.screenshot({ path: join(evidence, 'new-instance-before-logout.png') })
  if (await dialog.isVisible()) await dialog.getByRole('button', { name: '关闭', exact: true }).click()
  const outer = page.getByRole('dialog', { name: /容器详情/ }); if (await outer.isVisible()) await outer.locator('.el-drawer__close-btn').click()
  const ownerLogout = await logoutFromUserMenu(page, context, base)
  assert.ok(logoutMarkerWrite); await logoutMarkerWrite
  const anonymous = await context.request.get(base + endpoint + '/' + initial.id); assert.equal(anonymous.status(), 401)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-hints-v1'))).toBe(null)
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 0); assert.equal(ownerLogoutPosts, 1)
  await context.close(); await waitSignal(evidence, 'unknown-ready.json', 80_000)
  const recovery = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  recovery.setDefaultTimeout(20_000)
  const adminPage = await recovery.newPage(); activePage = adminPage; phase = 'normal-recovery-login'
  const admin = await loginVisible(adminPage, recovery, base, '/files', true, { username: process.env.RECOVERY_USERNAME, password: process.env.RECOVERY_PASSWORD })
  assert.notEqual(admin.subject, profile.subject)
  let managementWrites = 0, acknowledgements = 0
  recovery.on('request', r => {
    const u = new URL(r.url()); if (u.origin === base && ['POST', 'PATCH', 'PUT', 'DELETE'].includes(r.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')) {
      managementWrites++
      if (r.method() === 'POST' && u.pathname === '/api/v1/files/container-recycles/recovery/' + initial.id + '/acknowledge') acknowledgements++
    }
  })
  phase = 'different-admin-unknown-result'
  const old = await recovery.request.get(base + endpoint + '/' + initial.id); assert.equal(old.status(), 404)
  const recoveryPath = base + '/api/v1/files/container-recycles/recovery/' + initial.id
  const response = await recovery.request.get(recoveryPath); assert.equal(response.status(), 200); const unknown = await response.json()
  assert.equal(unknown.id, initial.id); assert.equal(unknown.executionJobId, pending.executionJobId); assert.equal(unknown.state, 'Unknown')
  assert.equal(unknown.receipt.state, 'Unknown'); assert.equal(unknown.receipt.sourceRetained, null); assert.equal(unknown.receipt.sourceRestored, null)
  await writeSignal(evidence, 'unknown-value.json', unknown); await waitSignal(evidence, 'unknown-proved.json')
  async function showJob(state, title, screenshot) {
    const listing = adminPage.waitForResponse(r => isJobPageResponse(r, base))
    await adminPage.goto(base + '/jobs'); const jobs = await (await listing).json(), index = jobs.findIndex(v => v.id === pending.executionJobId)
    assert.ok(index >= 0); assert.equal(jobs[index].state, state)
    await adminPage.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
    const detail = adminPage.getByRole('dialog', { name: '任务详情', exact: true })
    await expect(detail).toContainText(pending.executionJobId)
    const result = detail.getByRole('region', { name: '回收安全结果', exact: true })
    await expect(result).toContainText(title); await expect(result).toContainText('未证明')
    await expect(detail.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
    await adminPage.screenshot({ path: join(evidence, screenshot) })
    await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  }
  await showJob('Running', '结果未知，仍需核实原任务', 'original-unknown-job.png')
  await adminPage.goto(base + '/files'); await adminPage.getByRole('button', { name: '容器文件回收异常', exact: true }).click()
  const recoveryDialog = adminPage.getByRole('dialog', { name: '容器文件回收异常处置', exact: true })
  const row = recoveryDialog.locator('.recycle-recovery__row').filter({ hasText: initial.id })
  await expect(row).toHaveCount(1); await expect(row).toContainText('结果未知，保护已保留'); await expect(row).toContainText('尚未证明'); await expect(row).toContainText('已验证留存')
  await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await expect(recoveryDialog.getByRole('button', { name: '暂不处置', exact: true })).toBeInViewport({ ratio: 1 })
  await expect(recoveryDialog.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeInViewport({ ratio: 1 })
  await adminPage.screenshot({ path: join(evidence, 'recovery-confirm.png') })
  await recoveryDialog.getByRole('button', { name: '暂不处置', exact: true }).click()
  assert.equal(acknowledgements, 0); assert.equal(managementWrites, 0)
  await writeSignal(evidence, 'recovery-canceled.json', { id: initial.id, subject: admin.subject, acknowledgements })
  await waitSignal(evidence, 'acknowledge.json')
  await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
  const acknowledged = adminPage.waitForResponse(r => new URL(r.url()).pathname === '/api/v1/files/container-recycles/recovery/' + initial.id + '/acknowledge' && r.request().method() === 'POST')
  await recoveryDialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click(); assert.equal((await acknowledged).status(), 202)
  await expect(recoveryDialog.getByRole('status')).toHaveText('已处置历史未知结果并释放保护；不代表文件回收成功，不会重复修改或自动恢复回收，不删除原对象。')
  await expect(row).toHaveCount(0)
  await adminPage.screenshot({ path: join(evidence, 'acknowledged.png') })
  const final = await recovery.request.get(recoveryPath); assert.equal(final.status(), 200); const result = await final.json()
  assert.equal(result.state, 'Acknowledged'); assert.equal(result.executionJobId, pending.executionJobId)
  assert.equal(result.receipt.state, 'Unknown'); assert.equal(result.receipt.sourceRetained, null); assert.equal(result.receipt.sourceRestored, null)
  await recoveryDialog.getByRole('button', { name: '关闭', exact: true }).click()
  await showJob('Failed', '管理员已明确处置未知结果', 'original-acknowledged-job.png')
  assert.equal(acknowledgements, 1); assert.equal(managementWrites, 1)
  phase = 'normal-recovery-logout'; const recoveryLogout = await logoutFromUserMenu(adminPage, recovery, base)
  await writeSignal(evidence, 'browser.json', { revision, subject: profile.subject, recoverySubject: admin.subject, result, registrations, confirmations, cancellations, acknowledgements, managementWrites, originalSessionReadStatus: anonymous.status(), differentAdminOldIdStatus: old.status(), hintsCleared: true, cancelledDecisionPreserved: true, logoutReceipts: [ownerLogout, recoveryLogout], actualUi: true })
  await recovery.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRecycleInstance: 'passed' }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_instance.mjs:'))?.trim()
  if (activePage && evidence && phase !== 'startup' && !phase.includes('login'))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRecycleInstance: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
