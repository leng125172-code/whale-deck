// Actual product controls. Private file signals coordinate the fault observer,
// never intercept responses, inject a session, alter timers or send credentials.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, waitSignal, writeSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let browser, page, context, directory, phase = 'startup', action, currentIntent, ownEndpoint
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  const revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.ok(['cancel', 'logout', 'worker-crash', 'agent-restart'].includes(fixture.kind))
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix))
  assert.match(directory.slice(prefix.length), /^container-permission-faults-[a-f0-9]{32}\/(cancel|logout|worker-crash|agent-restart)$/)
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: fixture.kind === 'cancel' || fixture.kind === 'agent-restart' ? 320 : 1440, height: 1100 },
    colorScheme: fixture.kind === 'cancel' || fixture.kind === 'worker-crash' ? 'dark' : 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-login'
  const profile = await loginVisible(page, context, base)
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/permissions`
  ownEndpoint = base + endpoint
  let registrations = 0, confirmations = 0, cancellations = 0, bodies = 0
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === endpoint) registrations++
    if (request.method() === 'POST' && path.endsWith('/confirm') && path.startsWith(endpoint + '/')) confirmations++
    if (request.method() === 'POST' && path.endsWith('/cancel') && path.startsWith(endpoint + '/')) cancellations++
    if (request.method() === 'PUT' && path.includes('/files/')) bodies++
  })
  phase = 'visible-preflight'
  const files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '权限 original.bin', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '修改容器文件或目录权限', exact: true })
  await dialog.getByRole('textbox', { name: '普通权限模式', exact: true }).fill('0600')
  const queued = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  const response = await queued; assert.equal(response.status(), 202)
  const intent = await response.json()
  currentIntent = intent.id
  await writeSignal(directory, 'registered.json', { id: intent.id, subject: profile.subject })
  await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 45_000 })
  await writeSignal(directory, 'ready.json', { id: intent.id, subject: profile.subject })
  await waitSignal(directory, 'confirm.json', 30_000)
  phase = 'visible-confirmation-with-worker-stopped'
  const confirming = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + intent.id + '/confirm' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '确认权限修改', exact: true }).click()
  const accepted = await confirming; assert.equal(accepted.status(), 202)
  const pending = await accepted.json(); assert.equal(pending.state, 'Pending'); assert.ok(pending.executionJobId); assert.equal(pending.confirmation, null)
  await writeSignal(directory, 'confirmed.json', pending)
  action = await waitSignal(directory, 'action.json', 40_000)
  assert.equal(action.kind, fixture.kind)
  phase = 'visible-bound-inflight-task'
  await dialog.getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(dialog.getByRole('status')).toContainText('正在修改权限')
  await page.screenshot({ path: join(directory, 'inflight.png') })
  if (fixture.kind === 'cancel') {
    phase = 'visible-cancel'
    await dialog.getByRole('button', { name: '请求取消权限修改', exact: true }).click()
    await page.getByRole('button', { name: '继续等待', exact: true }).click()
    assert.equal(cancellations, 0)
    await dialog.getByRole('button', { name: '请求取消权限修改', exact: true }).click()
    await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  } else if (fixture.kind === 'logout') {
    phase = 'normal-visible-logout'
    await closePermissionDetails(page, dialog)
    await logoutFromUserMenu(page, context, base)
    await expect(page.getByRole('region', { name: '容器权限修改任务', exact: true })).toHaveCount(0)
  }
  await writeSignal(directory, 'action-done.json', { registrations, confirmations, cancellations, bodies, subject: profile.subject, dualLogout: fixture.kind === 'logout' })
  const final = await waitSignal(directory, 'settled.json')
  assert.equal(final.id, intent.id)
  if (fixture.kind !== 'logout') {
    phase = 'visible-original-terminal'
    await dialog.getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText(final.state === 'Canceled' ? '已取消' : '权限修改未完成')
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'terminal.png') })
    await closePermissionDetails(page, dialog)
    await logoutFromUserMenu(page, context, base)
  }
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, fixture.kind === 'cancel' ? 1 : 0); assert.equal(bodies, 0)
  await writeSignal(directory, 'browser.json', { revision, kind: fixture.kind, id: intent.id, executionJobId: pending.executionJobId,
    subject: profile.subject, registrations, confirmations, cancellations, bodies, finalState: final.state, normalDualLogout: true, actualUi: true })
  await context.close()
  console.log(JSON.stringify({ permissionFaultBrowser: 'passed', kind: fixture.kind }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_permission_faults\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  if (page && directory && phase !== 'normal-login' && phase !== 'startup') await page.screenshot({ path: join(directory, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ permissionFaultBrowser: 'failed', phase, failureType: error?.name, locations }))
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, failureType: error?.name, locations }).catch(() => {})
  // Exact original-login cancellation is failure cleanup, never a passing UI case.
  if (context && currentIntent && ownEndpoint) {
    try {
      const csrfResponse = await context.request.get('http://127.0.0.1:8080/api/v1/auth/csrf')
      if (csrfResponse.status() === 200) {
        const csrf = await csrfResponse.json()
        const canceled = await context.request.post(ownEndpoint + '/' + currentIntent + '/cancel', {
          headers: { Origin: 'http://127.0.0.1:8080', [csrf.headerName]: csrf.token, 'Idempotency-Key': currentIntent },
        })
        await writeSignal(directory, 'failure-cancellation.json', { id: currentIntent, status: canceled.status(), gatePassed: false })
      }
    } catch {}
  }
  process.exitCode = 1
} finally { if (browser) await browser.close() }
