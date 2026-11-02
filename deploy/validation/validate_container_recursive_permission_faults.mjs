// Actual installed controls and normal visible Authentik login; no injected responses or session.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let browser, page, context, fixture, directory, currentId, endpoint, phase = 'startup'
try {
  fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  const revision = process.argv[2], role = process.argv[3]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.ok(['cancel', 'recovery'].includes(role))
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  directory = fixture.evidence
  assert.equal(dirname(directory), fileURLToPath(new URL('../artifacts/', import.meta.url)).replace(/\/$/, ''))
  assert.match(directory.split('/').at(-1), /^container-recursive-permission-faults-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: role === 'cancel' ? 'dark' : 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-login'; const profile = await loginVisible(page, context, base, role === 'recovery' ? '/files' : '/containers')
  endpoint = `${base}/api/v1/containers/${fixture.containerId}/files/recursive-permissions`
  let registrations = 0, confirmations = 0, cancellations = 0, acknowledgements = 0, bodies = 0
  page.on('request', request => {
    const url = request.url()
    if (request.method() === 'POST' && url === endpoint) registrations++
    if (request.method() === 'POST' && url.startsWith(endpoint + '/') && url.endsWith('/confirm')) confirmations++
    if (request.method() === 'POST' && url.startsWith(endpoint + '/') && url.endsWith('/cancel')) cancellations++
    if (request.method() === 'POST' && url.includes('/container-recursive-permissions/recovery/') && url.endsWith('/acknowledge')) acknowledgements++
    if (request.method() === 'PUT' && url.includes('/files/')) bodies++
  })
  if (role === 'cancel') {
    phase = 'actual-preflight'
    const files = await openPermissionFiles(page, fixture)
    await files.getByRole('button', { name: '递归权限 recursive-tree', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '递归修改容器目录权限', exact: true })
    await dialog.getByLabel('文件模式', { exact: true }).fill('0600')
    await dialog.getByLabel('目录模式', { exact: true }).fill('0700')
    const registering = page.waitForResponse(r => r.url() === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
    const registered = await registering; assert.equal(registered.status(), 202)
    currentId = (await registered.json()).id
    await writeSignal(directory, 'registered.json', { id: currentId, subject: profile.subject })
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 45_000 })
    await writeSignal(directory, 'ready.json', { id: currentId, subject: profile.subject })
    await waitSignal(directory, 'confirm.json', 30_000)
    phase = 'actual-confirm-worker-stopped'
    const confirming = page.waitForResponse(r => r.url() === endpoint + '/' + currentId + '/confirm' && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '确认递归权限修改', exact: true }).click()
    const confirmed = await confirming; assert.equal(confirmed.status(), 202)
    const pending = await confirmed.json(); assert.equal(pending.state, 'Pending'); assert.ok(pending.executionJobId)
    await writeSignal(directory, 'confirmed.json', pending)
    await waitSignal(directory, 'action.json', 40_000)
    phase = 'actual-inflight-cancel'
    await dialog.getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText('正在逐项修改权限')
    await expect(dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(directory, 'inflight.png') })
    await dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true }).click()
    await page.getByRole('button', { name: '继续等待', exact: true }).click()
    assert.equal(cancellations, 0)
    await dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true }).click()
    await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
    await writeSignal(directory, 'action-done.json', { cancellations })
    const settled = await waitSignal(directory, 'settled.json', 120_000)
    assert.equal(settled.id, currentId); assert.equal(settled.state, 'Partial')
    phase = 'actual-partial-page'
    await dialog.getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText('部分修改，保护已保留')
    await expect(dialog.getByLabel('实际逐项结果', { exact: true })).toBeVisible()
    await expect(dialog.getByText('部分对象已经修改。实际计数和历史保留，不自动续做或回滚；解除保护须管理员核实并明确处置。', { exact: true })).toBeVisible()
    assert.equal(await dialog.getByRole('button', { name: '确认递归权限修改', exact: true }).count(), 0)
    await dialog.getByRole('link', { name: '前往递归权限修改异常处置', exact: true }).scrollIntoViewIfNeeded()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'partial.png') })
    const response = await context.request.get(endpoint + '/' + currentId); assert.equal(response.status(), 200)
    const result = await response.json(); assert.equal(result.state, 'Partial'); assert.equal(result.receipt.state, 'Partial')
    assert.equal(result.receipt.changedCount, settled.changedCount); assert.equal(result.confirmation, null)
    await closePermissionDetails(page, dialog); await logoutFromUserMenu(page, context, base)
    assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 1); assert.equal(acknowledgements, 0); assert.equal(bodies, 0)
    await writeSignal(directory, 'cancel-browser.json', { subject: profile.subject, result, registrations, confirmations, cancellations, bodies, actualUi: true, normalDualLogout: true })
  } else {
    currentId = fixture.id; assert.notEqual(profile.subject, fixture.originalSubject)
    phase = 'different-admin-recovery'
    await page.getByRole('button', { name: '容器递归权限修改异常', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '容器递归权限修改异常处置', exact: true })
    const row = dialog.locator('.recursive-permission-recovery__row').filter({ hasText: currentId })
    await expect(row).toHaveCount(1); await expect(row).toContainText('部分修改，保护已保留')
    await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await expect(dialog.getByRole('button', { name: '暂不处置', exact: true })).toBeInViewport({ ratio: 1 })
    await expect(dialog.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(directory, 'partial-recovery-confirm.png') })
    await dialog.getByRole('button', { name: '暂不处置', exact: true }).click(); assert.equal(acknowledgements, 0)
    await writeSignal(directory, 'recovery-canceled.json', { id: currentId, subject: profile.subject, acknowledgements })
    await waitSignal(directory, 'acknowledge.json', 40_000)
    await row.getByRole('button', { name: '处置历史结果', exact: true }).click()
    const acknowledging = page.waitForResponse(r => r.url().endsWith('/container-recursive-permissions/recovery/' + currentId + '/acknowledge') && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
    assert.equal((await acknowledging).status(), 202)
    await expect(dialog.getByRole('status')).toHaveText('已处置部分修改或未知历史结果并释放保护；不代表整树修改成功，不会续做或自动恢复权限，不删除原对象。')
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'partial-acknowledged.png') })
    const response = await context.request.get(base + '/api/v1/files/container-recursive-permissions/recovery/' + currentId)
    assert.equal(response.status(), 200); const result = await response.json()
    assert.equal(result.state, 'Acknowledged'); assert.equal(result.receipt.state, 'Partial')
    assert.equal(result.receipt.changedCount, fixture.changedCount)
    await dialog.getByRole('button', { name: '关闭', exact: true }).click()
    await logoutFromUserMenu(page, context, base)
    assert.equal(registrations, 0); assert.equal(confirmations, 0); assert.equal(cancellations, 0); assert.equal(acknowledgements, 1); assert.equal(bodies, 0)
    await writeSignal(directory, 'recovery-browser.json', { subject: profile.subject, result, acknowledgements, actualUi: true, normalDualLogout: true })
  }
  console.log(JSON.stringify({ recursiveFaultBrowser: 'passed', role }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_recursive_permission_faults\.mjs:([0-9]+):([0-9]+)/g)].map(x => ({ line: Number(x[1]), column: Number(x[2]) }))
  if (page && directory && !['startup', 'normal-login'].includes(phase)) await page.screenshot({ path: join(directory, 'failed-' + process.argv[3] + '.png') }).catch(() => {})
  console.error(JSON.stringify({ recursiveFaultBrowser: 'failed', phase, failureType: error?.name, locations }))
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, failureType: error?.name, locations }).catch(() => {})
  if (context && currentId && endpoint && process.argv[3] === 'cancel') {
    try {
      const csrf = await (await context.request.get('http://127.0.0.1:8080/api/v1/auth/csrf')).json()
      const canceled = await context.request.post(endpoint + '/' + currentId + '/cancel', { headers: { Origin: 'http://127.0.0.1:8080', [csrf.headerName]: csrf.token, 'Idempotency-Key': currentId } })
      await writeSignal(directory, 'failure-cancellation.json', { id: currentId, status: canceled.status(), gatePassed: false })
    } catch {}
  }
  process.exitCode = 1
} finally { if (browser) await browser.close() }
