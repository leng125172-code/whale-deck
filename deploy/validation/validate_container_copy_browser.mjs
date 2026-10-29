// Installed UI only: no response interception, injected session, token/trace export.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase = 'startup', browser, activePage, evidence
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://192.168.100.13:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.port, '8080')
  assert.ok(['127.0.0.1', '192.168.100.13', '192.168.22.19'].includes(origin.hostname))
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
  const fakeHint = Buffer.from('{"alg":"RS256","typ":"JWT"}').toString('base64url') + '.' + Buffer.from('{"iss":"http://192.168.100.13:8081/application/o/whaledeck-external/","aud":"invalid-validation-client","sub":"fixture"}').toString('base64url') + '.' + Buffer.alloc(256).toString('base64url')
  const identityBase = new URL(base); identityBase.port = '8081'
  assert.equal((await context.request.get(identityBase.origin + '/application/o/whaledeck-external/end-session/?id_token_hint=' + fakeHint)).status(), 400)
  assert.equal((await context.request.get(identityBase.origin + '/api/v3/core/users/me/')).status(), 200)
  const cookie = (await context.cookies(base)).map(value => value.name + '=' + value.value).join('; ')
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/copies`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: origin.origin, [csrf.headerName]: csrf.token }
  const forbidden = { instanceId: fixture.instanceId, sourcePath: 'work/original.bin', targetPath: 'work/禁止复制', expectedVersion: fixture.sourceVersion }
  assert.equal((await context.request.post(base + endpoint, { headers: { Origin: origin.origin, 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 400)
  assert.equal((await context.request.post(base + endpoint, { headers: { ...headers, Origin: 'http://example.invalid', 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 403)
  let registrations = 0, confirmations = 0, bodyRequests = 0
  const headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === endpoint || path.endsWith('/confirm') && path.startsWith(endpoint + '/'))) {
      if (path === endpoint) registrations++; else confirmations++
      headerChecks.push(request.allHeaders().then(h => h.origin === origin.origin && !!h['idempotency-key']))
    }
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
  const copies = [], modal = () => page.getByRole('dialog', { name: '复制容器文件或目录', exact: true })
  for (const [index, view] of [
    { width: 1440, colorScheme: 'light', kind: 'File' }, { width: 1440, colorScheme: 'dark', kind: 'Directory' },
    { width: 320, colorScheme: 'light', kind: 'Directory' }, { width: 320, colorScheme: 'dark', kind: 'File' },
  ].entries()) {
    phase = `visible-${view.width}-${view.colorScheme}-${view.kind}`
    await page.setViewportSize({ width: view.width, height: 1000 }); await page.emulateMedia({ colorScheme: view.colorScheme })
    const source = view.kind === 'File' ? 'original.bin' : 'source-tree', target = `网页副本-${index}`
    await files.getByRole('button', { name: '复制 ' + source, exact: true }).click()
    await modal().getByRole('button', { name: '返回', exact: true }).click()
    assert.equal(registrations, index); assert.equal(confirmations, index)
    await files.getByRole('button', { name: '复制 ' + source, exact: true }).click()
    await modal().getByLabel('复制目标相对路径', { exact: true }).fill('work/' + target)
    const queued = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === endpoint && r.status() === 202)
    await modal().getByRole('button', { name: '生成复制预检', exact: true }).click()
    const response = await queued, intent = await response.json(), original = response.request().postDataJSON()
    const replay = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': (await response.request().allHeaders())['idempotency-key'] }, data: original })
    assert.equal(replay.status(), 202); assert.equal((await replay.json()).id, intent.id)
    await expect(modal().getByText('预检已完成，尚未复制', { exact: true })).toBeVisible({ timeout: 90_000 })
    assert.equal(confirmations, index); assert.equal(bodyRequests, 0)
    assert.equal((await context.request.get(base + endpoint + '/' + intent.id)).status(), 200)
    await page.screenshot({ path: join(evidence, `${view.width}-${view.colorScheme}-copy-plan.png`) })
    const accepted = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === endpoint + '/' + intent.id + '/confirm' && r.status() === 202)
    await modal().getByRole('button', { name: '确认复制', exact: true }).click()
    const execution = await (await accepted).json(); assert.ok(execution.executionJobId); assert.notEqual(execution.executionJobId, intent.id)
    // Reload while a real execution exists; never resend confirmation or create another intent.
    await page.reload(); files = await open()
    await expect(files.getByRole('region', { name: '容器复制任务', exact: true }).getByRole('status')).toHaveText('复制已完成', { timeout: 90_000 })
    const result = await (await context.request.get(base + endpoint + '/' + intent.id)).json()
    assert.equal(result.state, 'Succeeded'); assert.equal(result.executionJobId, execution.executionJobId)
    assert.deepEqual(result.command, { source: { containerId: fixture.containerId, instanceId: fixture.instanceId, path: 'work/' + source }, targetPath: 'work/' + target, expectedVersion: view.kind === 'File' ? fixture.sourceVersion : fixture.treeVersion })
    assert.equal(result.privateMayExist, false); assert.match(result.publishedVersion, /^[A-F0-9]{64}$/)
    await expect(files.getByText(target, { exact: true })).toBeVisible()
    assert.equal(registrations, index + 1); assert.equal(confirmations, index + 1); assert.equal(bodyRequests, 0)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1))
    await files.screenshot({ path: join(evidence, `${view.width}-${view.colorScheme}-copy-complete.png`) })
    copies.push(result)
    await writeFile(join(evidence, 'results.json'), JSON.stringify({ revision, copies, subject: profile.subject, fullSsoLogout: false }), { mode: 0o600 })
  }
  phase = 'ready-plan-explicit-cancel-no-execution'
  await files.getByRole('button', { name: '复制 original.bin', exact: true }).click()
  await modal().getByLabel('复制目标相对路径', { exact: true }).fill('work/取消的副本')
  const queued = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === endpoint && r.status() === 202)
  await modal().getByRole('button', { name: '生成复制预检', exact: true }).click()
  const canceledId = (await (await queued).json()).id
  await expect(modal().getByText('预检已完成，尚未复制', { exact: true })).toBeVisible({ timeout: 90_000 })
  await modal().getByRole('button', { name: '请求取消复制', exact: true }).click()
  await page.getByRole('button', { name: '继续等待', exact: true }).click()
  await modal().getByRole('button', { name: '请求取消复制', exact: true }).click()
  await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect(modal().getByText('已取消', { exact: true })).toBeVisible()
  const canceled = await (await context.request.get(base + endpoint + '/' + canceledId)).json()
  assert.equal(canceled.state, 'Canceled'); assert.equal(canceled.executionJobId, null)
  assert.equal(registrations, 5); assert.equal(confirmations, 4); assert.equal(bodyRequests, 0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  await modal().getByRole('button', { name: '关闭', exact: true }).click()
  phase = 'normal-two-page-logout-invalidates-original-session'
  const other = await context.newPage(); await other.setViewportSize({ width: 1440, height: 1000 }); await other.goto(base + '/')
  let logoutHosts
  other.on('request', request => {
    const url = new URL(request.url())
    if (url.pathname !== '/application/o/whaledeck-external/end-session/') return
    const hint = url.searchParams.get('id_token_hint'); assert.ok(hint)
    const issuer = new URL(JSON.parse(Buffer.from(hint.split('.')[1], 'base64url')).iss)
    logoutHosts = { browserHost: url.host, issuerHost: issuer.host }
  })
  await other.getByRole('button', { name: '打开用户菜单', exact: true }).click()
  await other.getByRole('menuitem', { name: '退出登录', exact: true }).click()
  await expect(other).toHaveURL(base + '/login', { timeout: 30_000 })
  await expect(page).toHaveURL(base + '/login?returnUrl=/containers', { timeout: 30_000 })
  await expect(page.getByRole('region', { name: '容器复制任务', exact: true })).toHaveCount(0)
  assert.equal((await context.request.get(base + '/api/v1/auth/me')).status(), 401)
  assert.equal((await context.request.get(base + '/api/v1/auth/me', { headers: { Cookie: cookie } })).status(), 401)
  const identity = new URL(base); identity.port = '8081'; identity.pathname = '/api/v3/core/users/me/'
  assert.ok([401, 403].includes((await context.request.get(identity.href)).status()))
  assert.ok(logoutHosts); assert.notEqual(logoutHosts.browserHost, logoutHosts.issuerHost)
  await writeFile(join(evidence, 'results.json'), JSON.stringify({ revision, copies, canceled, subject: profile.subject, registrations, confirmations, bodyRequests, exactHttpRetries: 4, csrfAndOriginDenials: 2, forgedLogoutHintRejected: true, logoutHosts, fullSsoLogout: true }), { mode: 0o600 })
  await context.close()
  console.log(JSON.stringify({ installedContainerCopyBrowser: 'passed', revision, copies: 4, fullSsoLogout: true }))
} catch (error) {
  if (activePage && evidence && !activePage.isClosed() && new URL(activePage.url()).pathname === '/containers') await activePage.screenshot({ path: join(evidence, 'failed-copy-page.png') }).catch(() => {})
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_copy_browser\.mjs:([0-9]+):([0-9]+)/g)].map(m => ({ line: Number(m[1]), column: Number(m[2]) }))
  const pages = (browser?.contexts() ?? []).flatMap(context => context.pages()).slice(0, 4).map(page => {
    const url = new URL(page.url()); return { origin: url.origin, path: url.pathname, queryKeys: [...url.searchParams.keys()] }
  })
  console.error(JSON.stringify({ installedContainerCopyBrowser: 'failed', phase, failureType: error?.constructor?.name ?? 'Error', locations, pages }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
