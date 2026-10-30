// Actual installed recovery controls and normal Authentik login/logout only.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  for (const id of [fixture.intent, fixture.waitingIntent]) assert.match(id, /^[a-f0-9-]{36}$/)
  const base = process.env.WHALEDECK_URL ?? 'http://192.168.100.13:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.port, '8080')
  assert.ok(['127.0.0.1', '192.168.100.13', '192.168.22.19'].includes(origin.hostname))
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 1100 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-different-administrator-login'
  await page.goto(base + '/login?returnUrl=/files')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === '/files') break
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
  assert.ok(identified && entered); await expect(page).toHaveURL(base + '/files')
  const profile = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(profile.isAdministrator, true); assert.notEqual(profile.subject, fixture.originalSubject)
  const root = '/api/v1/files/container-moves/recovery'
  let confirmations = 0, registrations = 0, bodies = 0
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === root + '/' + fixture.intent + '/acknowledge') confirmations++
    if (request.method() === 'POST' && /\/files\/directories$/.test(path)) registrations++
    if (request.method() === 'PUT' && path.includes('/files/')) bodies++
  })
  phase = 'real-unknown-candidate-and-explicit-confirmation'
  await page.getByRole('button', { name: '容器移动异常', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '容器移动异常处置', exact: true })
  const candidate = dialog.locator('.move-recovery__row').filter({ hasText: fixture.intent })
  await expect(candidate).toHaveCount(1)
  await expect(candidate.getByText('目标发布：已确认', { exact: true })).toBeVisible()
  await expect(candidate.getByText('源路径移除：尚未核实', { exact: true })).toBeVisible()
  await expect(candidate.getByText('原件留存：尚未核实', { exact: true })).toBeVisible()
  const unknown = await (await context.request.get(base + root + '/' + fixture.intent)).json()
  assert.equal(unknown.state, 'Unknown'); assert.equal(unknown.destinationPublished, true)
  assert.equal(unknown.sourceRemoved, null); assert.equal(unknown.sourceRetained, null)
  await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await expect(dialog.getByText(/原任务会以失败结束/)).toBeVisible()
  await dialog.getByRole('button', { name: '暂不处置', exact: true }).click()
  assert.equal(confirmations, 0)
  await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await dialog.screenshot({ path: join(fixture.evidence, 'mobile-dark-unknown-confirmation.png') })
  const acknowledged = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === root + '/' + fixture.intent + '/acknowledge' && response.status() === 202)
  await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  await acknowledged
  await expect(dialog.getByRole('status')).toContainText('不代表移动成功')
  const old = await (await context.request.get(base + root + '/' + fixture.intent)).json()
  assert.equal(old.state, 'Acknowledged'); assert.equal(old.publishedVersion, null)
  assert.equal(old.destinationPublished, true); assert.equal(old.sourceRemoved, null); assert.equal(old.sourceRetained, null)
  phase = 'other-application-proceeds-only-after-the-barrier-is-released'
  let waiting
  for (let count = 0; count < 120; count++) {
    const response = await context.request.get(base + '/api/v1/files/container-directories/recovery/' + fixture.waitingIntent)
    assert.equal(response.status(), 200); waiting = await response.json()
    if (waiting.state === 'Succeeded') break
    assert.ok(['Pending', 'Applying'].includes(waiting.state))
    await page.waitForTimeout(500)
  }
  assert.equal(waiting.state, 'Succeeded')
  assert.equal(confirmations, 1); assert.equal(registrations, 0); assert.equal(bodies, 0)
  assert.ok(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1))
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1))
  await dialog.screenshot({ path: join(fixture.evidence, 'mobile-dark-unknown-acknowledged.png') })
  phase = 'normal-logout-clears-recovery-controls'
  await logoutFromUserMenu(page, context, base, dialog)
  await writeFile(join(fixture.evidence, 'browser-result.json'), JSON.stringify({ revision, confirmations, registrations, bodies,
    subject: profile.subject, destinationPublished: old.destinationPublished, sourceRemoved: old.sourceRemoved, sourceRetained: old.sourceRetained, originalState: old.state, waitingState: waiting.state, fullSsoLogout: true }), { mode: 0o600 })
  await context.close()
  console.log(JSON.stringify({ installedContainerMoveUnknownUi: 'passed', revision, confirmations, fullSsoLogout: true }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_move_unknown\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  console.error(JSON.stringify({ installedContainerMoveUnknownUi: 'failed', phase, failureType: error?.constructor?.name ?? 'Error', locations }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
