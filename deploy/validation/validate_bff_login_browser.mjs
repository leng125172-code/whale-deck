// Real installed SSO and normal visible controls, without request interception,
// storageState exports, traces, credential output or production-user changes.
import assert from 'node:assert/strict'
import { mkdir, mkdtemp } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase = 'startup'
let browser
let activePage
let otherPage
const flowEvidence = []
let enteredPasswordMatches = false
try {
  const revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/)
  const base = process.env.WHALEDECK_URL ?? 'http://192.168.100.13:8080'
  const parsed = new URL(base)
  assert.equal(parsed.protocol, 'http:')
  assert.ok(['127.0.0.1', '192.168.100.13', '192.168.22.19'].includes(parsed.hostname))
  assert.equal(parsed.port, '8080')
  const username = process.env.VALIDATION_USERNAME
  const password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  // No recorder, browser extensions, user profile or TLS/proxy bypass.
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' })
  context.setDefaultTimeout(15_000)
  const page = await context.newPage()
  activePage = page
  page.on('request', request => {
    if (request.method() !== 'POST' || !new URL(request.url()).pathname.startsWith('/api/v3/flows/executor/')) return
    if (flowEvidence.length >= 20) return
    try {
      const body = request.postDataJSON()
      flowEvidence.push({ passwordSubmitted: typeof body.password === 'string',
        passwordMatches: body.password === password, identificationSubmitted: typeof body.uid_field === 'string',
        identificationMatches: body.uid_field === username })
    } catch { /* No body or diagnostic contents are retained. */ }
  })
  const status = await context.request.get(base + '/api/v1/system/status')
  assert.equal(status.status(), 200)
  assert.equal((await status.json()).build.sourceRevision, revision)

  phase = 'visible-login-button-and-authentik-stages'
  await page.goto(base + '/login?returnUrl=/containers')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false
  let passwordEntered = false
  let consentSubmitted = false
  for (let stage = 0; stage < 80; stage++) {
    const current = new URL(page.url())
    if (current.origin === parsed.origin && current.pathname === '/containers') break
    const identificationStage = page.locator('ak-stage-identification')
    const passwordStage = page.locator('ak-stage-password')
    const consentStage = page.locator('ak-stage-consent')
    const identification = identificationStage.locator('input[name="uidField"]')
    const secret = passwordStage.locator('input[name="password"]')
    const submitName = /^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if (!identified && await identification.isVisible()) {
      await identification.fill(username)
      await identificationStage.getByRole('button', { name: submitName }).click()
      identified = true
    } else if (!passwordEntered && await secret.isVisible()) {
      await secret.fill(password)
      enteredPasswordMatches = await secret.inputValue() === password
      await passwordStage.getByRole('button', { name: submitName }).click()
      passwordEntered = true
    } else if (passwordEntered && !consentSubmitted && await consentStage.isVisible()) {
      consentSubmitted = true
      await consentStage.getByRole('button', { name: submitName }).click()
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified && passwordEntered)
  await expect(page).toHaveURL(base + '/containers')
  await expect(page.getByRole('button', { name: '创建容器', exact: true })).toBeVisible()
  const me = await context.request.get(base + '/api/v1/auth/me')
  assert.equal(me.status(), 200)
  assert.equal((await me.json()).isAdministrator, true)
  const cookie = (await context.cookies(base)).map(value => value.name + '=' + value.value).join('; ')
  assert.ok(cookie.length > 0)

  phase = 'mobile-dark-admin-and-second-page'
  await page.setViewportSize({ width: 320, height: 900 })
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.getByRole('button', { name: '创建容器', exact: true })).toBeVisible()
  const other = await context.newPage()
  otherPage = other
  await other.goto(base + '/')
  await expect(other.getByRole('button', { name: '打开用户菜单', exact: true })).toBeVisible()
  const artifacts = resolve(dirname(fileURLToPath(import.meta.url)), '../artifacts')
  await mkdir(artifacts, { recursive: true, mode: 0o700 })
  const evidence = await mkdtemp(join(artifacts, 'bff-login-browser-'))
  await page.screenshot({ path: join(evidence, 'mobile-dark-authenticated.png') })

  phase = 'normal-menu-logout-clears-both-pages'
  await other.getByRole('button', { name: '打开用户菜单', exact: true }).click()
  await other.getByRole('menuitem', { name: '退出登录', exact: true }).click()
  phase = 'logout-originating-page-url'
  await expect(other).toHaveURL(base + '/login', { timeout: 30_000 })
  phase = 'logout-other-page-return-url'
  await expect(page).toHaveURL(base + '/login?returnUrl=/containers', { timeout: 30_000 })
  phase = 'logout-private-controls-cleared'
  for (const tab of [page, other]) {
    await expect(tab.getByRole('button', { name: '使用用户名和密码登录', exact: true })).toBeVisible()
    await expect(tab.getByRole('button', { name: '创建容器', exact: true })).toHaveCount(0)
    await expect(tab.getByRole('button', { name: '打开用户菜单', exact: true })).toHaveCount(0)
  }
  assert.equal((await context.request.get(base + '/api/v1/auth/me')).status(), 401)
  assert.ok([401, 403].includes((await context.request.get(parsed.origin.replace(':8080', ':8081') + '/api/v3/core/users/me/')).status()))
  assert.equal((await context.request.get(base + '/api/v1/auth/me', { headers: { Cookie: cookie } })).status(), 401)
  await page.screenshot({ path: join(evidence, 'mobile-dark-after-logout.png') })
  await other.screenshot({ path: join(evidence, 'desktop-light-after-logout.png') })
  await context.close()
  console.log(JSON.stringify({ installedBffBrowser: 'passed', revision, normalAuthentikControls: true,
    twoPageLogoutClearsPrivateControls: true, originalCookieRejected: true, authentikAndBffSignedOut: true,
    mobileDarkAndDesktopLight: true, evidence }))
} catch (error) {
  // Playwright call logs can include form values or callback URLs. Keep only
  // the stage and error class, with no message, stack, trace or request dump.
  console.error(JSON.stringify({ installedBffBrowser: 'failed', phase, failureType: error?.constructor?.name ?? 'Error' }))
  if (activePage && !activePage.isClosed()) {
    const location = new URL(activePage.url())
    const inputs = await activePage.locator('input').evaluateAll(nodes => nodes.map(node => ({ name: node.name, type: node.type,
      required: node.required, valid: node.validity.valid, valueMissing: node.validity.valueMissing,
      disabled: node.disabled, hidden: !node.checkVisibility() }))).catch(() => [])
    const buttons = await activePage.getByRole('button').allTextContents().catch(() => [])
    const known = ['继续', 'Continue', 'Log in', '登录', 'Login', 'Authorize', '授权', '允许', 'Allow', '确认', 'Confirm', '使用用户名和密码登录']
    console.error(JSON.stringify({ loginPage: location.pathname === '/login', containerPage: location.pathname === '/containers',
      authentikFlow: location.pathname.startsWith('/if/flow/'), forbiddenPage: location.pathname === '/forbidden', inputs,
      knownButtons: buttons.map(text => text.trim()).filter(text => known.includes(text)), flowEvidence, enteredPasswordMatches }))
    for (const [name, tab] of [['original', activePage], ['other', otherPage]]) {
      if (!tab || tab.isClosed()) continue
      const route = new URL(tab.url())
      const back = route.searchParams.get('returnUrl')
      console.error(JSON.stringify({ page: name, loginPage: route.pathname === '/login',
        authentikFlow: route.pathname.startsWith('/if/flow/'), queryKeys: [...route.searchParams.keys()],
        returnUrl: ['/', '/containers', null].includes(back) ? back : 'withheld' }))
    }
  }
  process.exitCode = 1
} finally {
  if (browser) await browser.close()
}
