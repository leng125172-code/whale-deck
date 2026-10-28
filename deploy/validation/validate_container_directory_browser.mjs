// Installed UI only: no response interception, injected session, token/trace export.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase = 'startup', browser
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  const base = process.env.WHALEDECK_URL ?? 'http://192.168.100.13:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.port, '8080')
  assert.ok(['127.0.0.1', '192.168.100.13', '192.168.22.19'].includes(origin.hostname))
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage()
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
  const cookie = (await context.cookies(base)).map(value => value.name + '=' + value.value).join('; ')
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/directories`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const writeHeaders = { Origin: origin.origin, [csrf.headerName]: csrf.token }
  const forbiddenPlan = { instanceId: fixture.instanceId, path: 'work/禁止创建', mode: '0750' }
  assert.equal((await context.request.post(base + endpoint + '/plan', { headers: { Origin: origin.origin }, data: forbiddenPlan })).status(), 400)
  assert.equal((await context.request.post(base + endpoint + '/plan', { headers: { ...writeHeaders, Origin: 'http://example.invalid' }, data: forbiddenPlan })).status(), 403)
  let registrations = 0, plans = 0, bodyRequests = 0
  const headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === endpoint) {
      registrations++
      headerChecks.push(request.allHeaders().then(headers => headers.origin === origin.origin && !!headers['idempotency-key']))
    }
    if (request.method() === 'POST' && path === endpoint + '/plan') plans++
    if (request.method() === 'PUT' && path.includes('/files/')) bodyRequests++
  })
  async function open() {
    await page.getByRole('textbox', { name: '搜索容器', exact: true }).fill(fixture.containerName)
    const card = page.locator('.container-card').filter({ hasText: fixture.containerName })
    await expect(card).toHaveCount(1)
    await card.getByRole('button', { name: '详情', exact: true }).click()
    await page.getByRole('button', { name: '容器文件', exact: true }).click()
    const files = page.getByRole('region', { name: '容器文件', exact: true })
    await files.getByRole('textbox', { name: '容器内相对目录', exact: true }).fill('work')
    await files.getByRole('button', { name: '前往', exact: true }).click()
    await expect(files.getByText('original.bin', { exact: true })).toBeVisible()
    return files
  }
  let files = await open()
  const directories = []
  for (const [index, view] of [
    { width: 1440, colorScheme: 'light', mode: '0750' }, { width: 1440, colorScheme: 'dark', mode: '0700' },
    { width: 320, colorScheme: 'light', mode: '0755' }, { width: 320, colorScheme: 'dark', mode: '0750' },
  ].entries()) {
    phase = `visible-${view.width}-${view.colorScheme}-directory-confirmation`
    await page.setViewportSize({ width: view.width, height: 1050 }); await page.emulateMedia({ colorScheme: view.colorScheme })
    const panel = files.getByRole('region', { name: '容器新建目录', exact: true })
    const name = `网页新目录-${index}-${view.colorScheme}`, path = 'work/' + name
    async function plan() {
      await panel.getByRole('button', { name: '新建目录', exact: true }).click()
      const dialog = page.getByRole('dialog', { name: '新建容器目录', exact: true })
      await dialog.getByLabel('目录名称', { exact: true }).fill(name)
      await dialog.getByRole('combobox', { name: '目录权限', exact: true }).click()
      await page.getByRole('option', { name: new RegExp('^' + view.mode) }).click()
      await dialog.getByRole('button', { name: '生成目录计划', exact: true }).click()
      await expect(dialog.getByText('/' + path, { exact: true })).toBeVisible()
      return dialog
    }
    let dialog = await plan()
    await dialog.getByRole('button', { name: '取消', exact: true }).click(); await expect(dialog).toBeHidden()
    assert.equal(registrations, index); assert.equal(bodyRequests, 0)
    dialog = await plan()
    const accepted = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === endpoint && response.status() === 202)
    await dialog.getByRole('button', { name: '确认创建目录', exact: true }).click()
    const acceptedResponse = await accepted
    const intent = await acceptedResponse.json(); assert.equal(intent.id, intent.jobId)
    // An exact HTTP retry after a real visible registration is read-back of
    // that intent, even when the Worker has already created the directory.
    const originalRegistration = acceptedResponse.request().postDataJSON()
    const replay = await context.request.post(base + endpoint, { headers: { ...writeHeaders, 'Idempotency-Key': originalRegistration.idempotencyKey }, data: originalRegistration })
    assert.equal(replay.status(), 202)
    const repeated = await replay.json()
    assert.equal(repeated.id, intent.id); assert.equal(repeated.expiresAtUtc, intent.expiresAtUtc)
    assert.deepEqual(intent.command, { location: { containerId: fixture.containerId, instanceId: fixture.instanceId, path }, mode: view.mode })
    await dialog.getByRole('button', { name: '关闭', exact: true }).click(); await expect(dialog).toBeHidden()
    // Reload at registration, rather than wait for a success UI and call it
    // recovery. Restoring the local hint only queries this original intent.
    await page.reload(); files = await open()
    const restored = files.getByRole('region', { name: '容器新建目录', exact: true })
    await expect(restored.getByRole('status')).toContainText('目录已创建', { timeout: 90_000 })
    const response = await context.request.get(base + endpoint + '/' + intent.id)
    assert.equal(response.status(), 200)
    const completed = await response.json(); assert.equal(completed.state, 'Succeeded')
    assert.equal(completed.command.location.path, path); assert.match(completed.publishedVersion, /^[A-F0-9]{64}$/)
    await expect(files.getByText(name, { exact: true })).toBeVisible()
    assert.equal(registrations, index + 1); assert.equal(bodyRequests, 0); assert.equal(plans, (index + 1) * 2)
    assert.ok((await Promise.all(headerChecks)).every(Boolean))
    assert.ok(await files.evaluate(element => element.scrollWidth <= element.clientWidth + 1))
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1))
    await files.screenshot({ path: join(fixture.evidence, `${view.width}-${view.colorScheme}-directory.png`) })
    directories.push({ id: completed.id, path, mode: view.mode, publishedVersion: completed.publishedVersion })
    await writeFile(join(fixture.evidence, 'results.json'), JSON.stringify({ revision, directories, subject: profile.subject, registrations, fullSsoLogout: false }), { mode: 0o600 })
  }
  phase = 'normal-two-page-logout-invalidates-original-session'
  const other = await context.newPage(); await other.setViewportSize({ width: 1440, height: 1000 }); await other.goto(base + '/')
  await other.getByRole('button', { name: '打开用户菜单', exact: true }).click()
  await other.getByRole('menuitem', { name: '退出登录', exact: true }).click()
  await expect(other).toHaveURL(base + '/login', { timeout: 30_000 })
  await expect(page).toHaveURL(base + '/login?returnUrl=/containers', { timeout: 30_000 })
  await expect(page.getByRole('region', { name: '容器新建目录', exact: true })).toHaveCount(0)
  assert.equal((await context.request.get(base + '/api/v1/auth/me')).status(), 401)
  assert.equal((await context.request.get(base + '/api/v1/auth/me', { headers: { Cookie: cookie } })).status(), 401)
  const identity = new URL(base); identity.port = '8081'; identity.pathname = '/api/v3/core/users/me/'
  assert.ok([401, 403].includes((await context.request.get(identity.href)).status()))
  await writeFile(join(fixture.evidence, 'results.json'), JSON.stringify({ revision, directories, subject: profile.subject, registrations, exactHttpRetries: 4, csrfAndOriginDenials: 2, fullSsoLogout: true }), { mode: 0o600 })
  await context.close()
  console.log(JSON.stringify({ installedContainerDirectoryBrowser: 'passed', revision, directories: 4, fullSsoLogout: true }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_directory_browser\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  console.error(JSON.stringify({ installedContainerDirectoryBrowser: 'failed', phase, failureType: error?.constructor?.name ?? 'Error', locations }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
