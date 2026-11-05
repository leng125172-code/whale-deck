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
  assert.ok(['group-removal', 'account-disable'].includes(f.kind))
  assert.match(f.containerId, /^[a-f0-9]{64}$/); assert.match(f.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = f.evidence
  assert.match(evidence, /\/deploy\/artifacts\/container-recycle-revocation-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080', endpoint = `/api/v1/containers/${f.containerId}/files/recycles`
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage(); activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-owner-login'
  const profile = await loginVisible(page, context, base)
  let registrations = 0, confirmations = 0, cancellations = 0, ownerLogoutPosts = 0
  page.on('request', r => {
    const u = new URL(r.url()); if (u.origin !== base || r.method() !== 'POST') return
    if (u.pathname === endpoint) registrations++
    if (u.pathname.startsWith(endpoint + '/') && u.pathname.endsWith('/confirm')) confirmations++
    if (u.pathname.startsWith(endpoint + '/') && u.pathname.endsWith('/cancel')) cancellations++
    if (u.pathname === '/api/v1/auth/logout') ownerLogoutPosts++
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
  phase = 'waiting-exact-cut-and-owned-revocation'
  const settled = await waitSignal(evidence, 'settled.json', 210_000)
  assert.equal(settled.id, initial.id); assert.equal(settled.executionJobId, pending.executionJobId); assert.equal(settled.state, 'Canceled')
  phase = 'revoked-original-session'
  const originalIntentStatus = (await context.request.get(base + endpoint + '/' + initial.id)).status()
  assert.ok([401, 403].includes(originalIntentStatus))
  const me = await context.request.get(base + '/api/v1/auth/me'), currentIdentityStatus = me.status()
  let currentSubject = null, currentlyAdministrator = null, renderedRevocationPage
  if (f.kind === 'group-removal') {
    assert.equal(originalIntentStatus, 403); assert.equal(me.status(), 200)
    const value = await me.json(); assert.equal(value.subject, profile.subject); assert.equal(value.isAdministrator, false)
    currentSubject = value.subject; currentlyAdministrator = false
  } else assert.ok([401, 403].includes(me.status()))
  await page.reload()
  await expect(page.getByRole('status', { name: '正在启动 WhaleDeck 协同平台', exact: true })).toHaveCount(0)
  if (f.kind === 'group-removal') {
    await expect(page).toHaveURL(base + '/forbidden')
    await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible(); renderedRevocationPage = 'forbidden'
  } else {
    await expect(page).toHaveURL(url => url.origin === base && url.pathname === '/login')
    await expect(page.getByRole('heading', { name: '使用工作站账号登录', exact: true })).toBeVisible(); renderedRevocationPage = 'login'
  }
  await expect(page.getByRole('region', { name: '容器文件回收任务', exact: true })).toHaveCount(0)
  await expect(page.getByRole('dialog', { name: '回收容器普通文件', exact: true })).toHaveCount(0)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-hints-v1'))).toBe(null)
  await page.screenshot({ path: join(evidence, 'authority-denied.png') })
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 0); assert.equal(ownerLogoutPosts, 0)
  await context.close()

  const recovery = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  recovery.setDefaultTimeout(20_000)
  const adminPage = await recovery.newPage(); activePage = adminPage; phase = 'normal-recovery-login'
  const admin = await loginVisible(adminPage, recovery, base, '/jobs', true, { username: process.env.RECOVERY_USERNAME, password: process.env.RECOVERY_PASSWORD })
  assert.notEqual(admin.subject, profile.subject)
  let managementWrites = 0
  recovery.on('request', r => {
    const u = new URL(r.url()); if (u.origin === base && ['POST', 'PATCH', 'PUT', 'DELETE'].includes(r.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')) managementWrites++
  })
  phase = 'current-admin-original-result'
  const response = await recovery.request.get(base + '/api/v1/files/container-recycles/recovery/' + initial.id)
  assert.equal(response.status(), 200); const result = await response.json()
  assert.equal(result.id, initial.id); assert.equal(result.executionJobId, pending.executionJobId)
  assert.equal(result.state, 'Canceled'); assert.equal(result.receipt.state, 'Aborted'); assert.equal(result.confirmation, null)
  const listing = adminPage.waitForResponse(r => isJobPageResponse(r, base))
  await adminPage.reload(); const jobs = await (await listing).json(), index = jobs.findIndex(v => v.id === result.executionJobId)
  assert.ok(index >= 0)
  await adminPage.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
  const detail = adminPage.getByRole('dialog', { name: '任务详情', exact: true })
  await expect(detail).toContainText(result.executionJobId)
  await expect(detail.getByRole('region', { name: '回收安全结果', exact: true })).toContainText('已取消')
  await adminPage.screenshot({ path: join(evidence, 'original-canceled-job.png') })
  await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  assert.equal(managementWrites, 0)
  phase = 'current-admin-normal-logout'; const recoveryLogout = await logoutFromUserMenu(adminPage, recovery, base)
  await writeSignal(evidence, 'browser.json', { revision, kind: f.kind, subject: profile.subject, recoverySubject: admin.subject, result, registrations, confirmations, cancellations, ownerLogoutPosts, managementWrites, originalIntentStatus, currentIdentityStatus, currentSubject, currentlyAdministrator, renderedRevocationPage, privatePanelsAbsent: true, hintsCleared: true, recoveryLogout, actualUi: true })
  await recovery.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRecycleRevocation: 'passed', kind: f.kind }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_revocation.mjs:'))?.trim()
  if (activePage && evidence && phase !== 'startup' && !phase.includes('login'))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRecycleRevocation: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
