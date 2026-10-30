// Installed UI only: normal Authentik credentials, real HTTP, no mocked response/session injection.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, evidence, activePage
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/); assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.hostname, '127.0.0.1'); assert.equal(origin.port, '8080')
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage()
  activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-visible-authentik-login'
  await page.goto(base + '/login?returnUrl=/containers')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === '/containers') break
    const identification = page.locator('ak-stage-identification'), secret = page.locator('ak-stage-password'), consent = page.locator('ak-stage-consent')
    const button = /^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if (!identified && await identification.locator('input[name="uidField"]').isVisible()) {
      await identification.locator('input[name="uidField"]').fill(username)
      await identification.getByRole('button', { name: button }).click(); identified = true
    } else if (!entered && await secret.locator('input[name="password"]').isVisible()) {
      await secret.locator('input[name="password"]').fill(password)
      await secret.getByRole('button', { name: button }).click(); entered = true
    } else if (entered && !consented && await consent.isVisible()) {
      await consent.getByRole('button', { name: button }).click(); consented = true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified && entered); await expect(page).toHaveURL(base + '/containers')
  const profile = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(profile.isAdministrator, true)
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/permissions`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: origin.origin, [csrf.headerName]: csrf.token }
  const forbidden = { instanceId: fixture.instanceId, path: 'work/original.bin', mode: '0600', expectedVersion: fixture.sourceVersion }
  assert.equal((await context.request.post(base + endpoint, { headers: { Origin: origin.origin, 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 400)
  assert.equal((await context.request.post(base + endpoint, { headers: { ...headers, Origin: 'http://example.invalid', 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 403)
  let registrations = 0, confirmations = 0, uploads = 0
  const headerChecks = [], completed = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === endpoint || path.startsWith(endpoint + '/') && path.endsWith('/confirm'))) {
      if (path === endpoint) registrations++; else confirmations++
      headerChecks.push(request.allHeaders().then(h => h.origin === origin.origin && !!h['idempotency-key']))
    }
    if (request.method() === 'PUT' && path.includes('/files/')) uploads++
  })
  async function openFiles() {
    await page.getByRole('textbox', { name: '搜索容器', exact: true }).fill(fixture.containerName)
    const card = page.locator('.container-card').filter({ hasText: fixture.containerName }); await expect(card).toHaveCount(1)
    await card.getByRole('button', { name: '详情', exact: true }).click()
    await page.getByRole('button', { name: '容器文件', exact: true }).click()
    const files = page.getByRole('region', { name: '容器文件', exact: true })
    await files.getByRole('textbox', { name: '容器内相对目录', exact: true }).fill('work')
    await files.getByRole('button', { name: '前往', exact: true }).click()
    await expect(files.getByText('original.bin', { exact: true })).toBeVisible(); return files
  }
  async function query(row) {
    const response = await context.request.get(base + endpoint + '/' + row.id); assert.equal(response.status(), 200); return response.json()
  }
  const cases = [
    { name: 'file-mode', leaf: 'original.bin', mode: '0600', width: 1440, theme: 'light' },
    { name: 'file-noop', leaf: 'original.bin', mode: '0600', width: 1440, theme: 'light' },
    { name: 'file-restore', leaf: 'original.bin', mode: '0640', width: 320, theme: 'dark' },
    { name: 'directory-mode', leaf: '权限目录', mode: '0700', width: 1440, theme: 'dark' },
    { name: 'directory-restore', leaf: '权限目录', mode: '0750', width: 320, theme: 'light' },
    { name: 'file-cancel', leaf: 'original.bin', mode: '0600', width: 1440, theme: 'light', cancel: true },
  ]
  for (const item of cases) {
    phase = item.name; await page.setViewportSize({ width: item.width, height: 1050 }); await page.emulateMedia({ colorScheme: item.theme })
    const files = await openFiles()
    await files.getByRole('button', { name: `权限 ${item.leaf}`, exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '修改容器文件或目录权限', exact: true })
    await dialog.getByRole('textbox', { name: '普通权限模式', exact: true }).fill(item.mode)
    const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成权限修改预检', exact: true }).click()
    const response = await registration; assert.equal(response.status(), 202)
    const registered = await response.json(), key = (await response.request().allHeaders())['idempotency-key'], body = response.request().postDataJSON()
    assert.equal(registered.state, 'Queued'); assert.equal(registered.executionJobId, null)
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 45_000 })
    const ready = await query(registered); assert.equal(ready.state, 'Ready'); assert.ok(ready.confirmation)
    assert.equal(ready.noChange, item.name === 'file-noop')
    const duplicate = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': key }, data: body })
    assert.equal(duplicate.status(), 202); assert.equal((await duplicate.json()).id, registered.id)
    // Reload resolves the original intent and never submits or confirms another one.
    await page.reload(); await expect(page).toHaveURL(base + '/containers'); await openFiles()
    await page.getByRole('button', { name: '查看权限修改任务', exact: true }).click()
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 30_000 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth), true)
    await page.screenshot({ path: join(evidence, item.name + '.png') })
    if (item.cancel) {
      await dialog.getByRole('button', { name: '请求取消权限修改', exact: true }).click()
      await page.getByRole('button', { name: '继续等待', exact: true }).click()
      assert.equal((await query(ready)).state, 'Ready')
      await dialog.getByRole('button', { name: '请求取消权限修改', exact: true }).click()
      await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
      await expect(dialog.getByText('已取消', { exact: true })).toBeVisible({ timeout: 30_000 })
      const canceled = await query(ready); assert.equal(canceled.state, 'Canceled'); assert.equal(canceled.executionJobId, null)
      completed.push({ name: item.name, ...canceled })
    } else {
      phase = item.name + ':confirm'
      await dialog.getByRole('button', { name: '确认权限修改', exact: true }).click()
      phase = item.name + ':visible-completion'
      await expect(dialog.getByText('权限修改已完成', { exact: true })).toBeVisible({ timeout: 45_000 })
      phase = item.name + ':http-retry'
      const result = await query(ready); assert.equal(result.state, 'Succeeded'); assert.equal(result.modeConfirmed, true); assert.equal(result.historyVerified, true)
      const retry = await context.request.post(base + endpoint + '/' + ready.id + '/confirm', { headers: { ...headers, 'Idempotency-Key': ready.id },
        data: { planHash: ready.confirmation.planHash, confirmed: true, version: ready.version } })
      assert.equal(retry.status(), 202); assert.equal((await retry.json()).executionJobId, result.executionJobId)
      completed.push({ name: item.name, ...result })
    }
    phase = item.name + ':close'
    await dialog.getByRole('button', { name: '关闭', exact: true }).click()
    await page.getByRole('dialog', { name: new RegExp('容器详情') }).locator('.el-drawer__close-btn').click()
  }
  assert.equal(registrations, 6); assert.equal(confirmations, 5); assert.equal(uploads, 0); assert.ok((await Promise.all(headerChecks)).every(Boolean))
  assert.ok(completed.every(r => r.confirmation === null))
  phase = 'visible-dual-logout'; await logoutFromUserMenu(page, context, base)
  // Persist only public task metadata after tokens were consumed, never cookies/credentials/headers.
  await writeFile(join(evidence, 'browser.json'), JSON.stringify({ revision, containerId: fixture.containerId, subject: profile.subject, completed,
    registrations, confirmations, contentUploads: uploads, actualUi: true, registrationRetries: 6, confirmationRetries: 5, normalDualLogout: true }), { mode: 0o600 })
  await context.close(); await browser.close(); browser = undefined
  phase = 'done'; console.log(JSON.stringify({ permissionBrowser: 'passed', actualUi: true, cases: 6 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_permission_browser.mjs:'))?.trim()
  // Only an already authenticated product page may be captured, never login/credentials.
  if (activePage && evidence && !['startup', 'normal-visible-authentik-login'].includes(phase))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ permissionBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
