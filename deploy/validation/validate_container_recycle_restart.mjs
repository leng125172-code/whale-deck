// Real installed UI across one fixed service fault; no injected session or retry.
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
  assert.ok(['worker-crash', 'agent-restart'].includes(f.kind))
  assert.match(f.containerId, /^[a-f0-9]{64}$/); assert.match(f.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = f.evidence
  assert.match(evidence, /\/deploy\/artifacts\/container-recycle-restart-[a-f0-9]{32}$/)
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
  phase = 'waiting-exact-cut-and-service-recovery'
  const settled = await waitSignal(evidence, 'settled.json', 330_000)
  assert.equal(settled.id, initial.id); assert.equal(settled.executionJobId, pending.executionJobId); assert.equal(settled.state, 'Failed')
  phase = 'original-interrupted-result'
  const response = await context.request.get(base + endpoint + '/' + initial.id)
  assert.equal(response.status(), 200); const result = await response.json()
  assert.equal(result.id, initial.id); assert.equal(result.executionJobId, pending.executionJobId)
  assert.equal(result.state, 'Failed'); assert.equal(result.resultCode, 'CONTAINER_RECYCLE_INTERRUPTED')
  assert.equal(result.receipt.state, 'Aborted'); assert.equal(result.confirmation, null)
  const current = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(current.subject, profile.subject); assert.equal(current.isAdministrator, true)
  await expect(dialog.getByRole('status')).toHaveText('文件回收未完成', { timeout: 20_000 })
  await page.screenshot({ path: join(evidence, 'interrupted-original-task.png') })
  await dialog.getByRole('button', { name: '关闭', exact: true }).click()
  await page.getByRole('dialog', { name: /容器详情/ }).locator('.el-drawer__close-btn').click()
  const listing = page.waitForResponse(r => isJobPageResponse(r, base))
  await page.goto(base + '/jobs'); const jobs = await (await listing).json(), index = jobs.findIndex(v => v.id === result.executionJobId)
  assert.ok(index >= 0)
  await page.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
  const detail = page.getByRole('dialog', { name: '任务详情', exact: true })
  await expect(detail).toContainText(result.executionJobId)
  await expect(detail.getByRole('region', { name: '回收安全结果', exact: true })).toContainText('任务未完成')
  await expect(detail.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
  await page.screenshot({ path: join(evidence, 'original-failed-job.png') })
  await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 0); assert.equal(ownerLogoutPosts, 0)
  phase = 'normal-owner-logout'; const logout = await logoutFromUserMenu(page, context, base)
  await writeSignal(evidence, 'browser.json', { revision, kind: f.kind, subject: profile.subject, result, registrations, confirmations, cancellations, originalOwnerStillAdministrator: true, logout, actualUi: true })
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRecycleRestart: 'passed', kind: f.kind }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_restart.mjs:'))?.trim()
  if (activePage && evidence && phase !== 'startup' && !phase.includes('login'))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRecycleRestart: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
