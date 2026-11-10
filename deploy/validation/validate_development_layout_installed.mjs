// Opt-in read-only installed UI check. No intercepted responses or session export.
import assert from 'node:assert/strict'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

const base = 'http://127.0.0.1:8080'
const id = 'c23526c5-fc43-4944-9f27-75ac540f6df0'
const route = '/development/workspaces/' + id + '/environment'
let browser, context, page, evidence, phase = 'startup'
const state = { workspaceId: id, observations: [], workspaceWrites: 0 }
async function save() {
  state.phase = phase
  await writeFile(join(evidence, 'state.pending.json'), JSON.stringify(state, null, 2), { mode: 0o600 })
  await rename(join(evidence, 'state.pending.json'), join(evidence, 'state.json'))
}
try {
  assert.equal(process.env.WHALEDECK_INSTALLED_LAYOUT_PROBE, '1')
  const input = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  evidence = input.evidence
  state.revision = input.revision
  await save()
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome-stable', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 900 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000)
  page = await context.newPage()
  context.on('request', request => {
    const url = new URL(request.url())
    if (url.origin === base && url.pathname.startsWith('/api/v1/development/workspaces') && request.method() !== 'GET') state.workspaceWrites++
  })
  phase = 'real-admin-login'
  const profile = await loginVisible(page, context, base, route, true, input.account)
  assert.equal(typeof profile.subject, 'string')
  for (const [path, component] of [['/api/v1/system/status', 'WhaleDeck.Api'], ['/build-info.json', 'WhaleDeck.Gateway']]) {
    const response = await context.request.get(base + path)
    assert.equal(response.status(), 200)
    const result = await response.json(), build = path.endsWith('/status') ? result.build : result
    assert.equal(build.sourceRevision, input.revision)
    assert.equal(build.component, component)
  }
  const response = await context.request.get(base + '/api/v1/development/workspaces/' + id)
  assert.equal(response.status(), 200)
  const workspace = await response.json()
  assert.equal(workspace.id, id)
  assert.equal(workspace.state, 'StorageAllocated')
  assert.equal(workspace.lifecycleState, 'Active')
  assert.deepEqual(workspace.environment.runtimes.map(pin => pin.key).sort(), ['dotnet', 'node'])
  for (const pin of workspace.environment.runtimes) {
    assert.ok(typeof pin.version === 'string' && pin.version.length > 0 && pin.version.length <= 64)
    assert.match(pin.image, /@sha256:[a-f0-9]{64}$/)
  }
  state.lifecycleVersion = workspace.lifecycleVersion
  phase = 'installed-layout'
  const list = page.locator('.workspace-page dl')
  await expect(list).toHaveCount(1)
  for (const width of [320, 390, 768, 1440]) for (const scheme of ['light', 'dark']) {
    phase = 'installed-layout-' + width + '-' + scheme
    await page.setViewportSize({ width, height: 900 })
    await page.emulateMedia({ colorScheme: scheme })
    await expect.poll(() => page.locator('html').evaluate(element => element.classList.contains('dark'))).toBe(scheme === 'dark')
    for (const pin of workspace.environment.runtimes) await expect(list.getByText(pin.image, { exact: true })).toBeVisible()
    const geometry = await list.evaluate((element, pins) => {
      const box = element.getBoundingClientRect()
      const values = [...element.querySelectorAll('dd')].map(value => {
        const r = value.getBoundingClientRect()
        return { text: value.innerText.trim().replace(/\s+/g, ' '), textContent: value.textContent.trim(), x: r.x, y: r.y, width: r.width, height: r.height }
      })
      const runtime = pins.map(pin => values.find(value => value.text.includes(pin.image)))
      return { x: box.x, width: box.width, values, runtime, overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth }
    }, workspace.environment.runtimes)
    assert.ok(geometry.runtime.length === 2 && geometry.runtime.every(Boolean))
    const versionsVisible = workspace.environment.runtimes.every((pin, index) =>
      geometry.runtime[index].text.startsWith(pin.key + ' ' + pin.version + ' '))
    const formerTextContentCheck = workspace.environment.runtimes.every((pin, index) =>
      geometry.runtime[index].textContent.replace(/\s+/g, ' ').startsWith(pin.key + ' ' + pin.version + ' '))
    const valuesAligned = geometry.values.every(value => value.x >= geometry.x - 1 && value.x + value.width <= geometry.x + geometry.width + 1 && value.width >= 120)
    const runtimeAligned = Math.abs(geometry.runtime[0].x - geometry.runtime[1].x) < 1
    const allFullWidth = width > 600 || geometry.values.every(value => Math.abs(value.x - geometry.x) < 1 && value.width >= geometry.width - 1)
    const passed = !geometry.overflow && versionsVisible && valuesAligned && runtimeAligned && allFullWidth
    state.observations.push({ viewportWidth: width, scheme, passed, versionsVisible, formerTextContentCheck, valuesAligned, runtimeAligned, allFullWidth, ...geometry })
    await page.screenshot({ path: join(evidence, 'installed-' + width + '-' + scheme + '.png'), fullPage: true })
    await save()
    assert.equal(passed, true)
  }
  phase = 'real-ui-logout'
  state.logout = await logoutFromUserMenu(page, context, base, null)
  await expect(page.locator('.workspace-page')).toHaveCount(0)
  assert.equal(state.workspaceWrites, 0)
  state.passed = true
  await save()
  console.log(JSON.stringify({ passed: true, cases: state.observations.length, workspaceWrites: state.workspaceWrites, realIdentity: true }))
} catch (error) {
  state.passed = false
  state.failureType = error.name
  const location = error.stack?.match(/validate_development_layout_installed\.mjs:(\d+):(\d+)/)
  if (location) state.failureLocation = { line: Number(location[1]), column: Number(location[2]) }
  if (context && page && phase !== 'startup' && phase !== 'real-admin-login' && phase !== 'real-ui-logout') {
    try { state.failureLogout = await logoutFromUserMenu(page, context, base, null) }
    catch (logoutError) { state.failureLogout = { passed: false, failureType: logoutError.name } }
  }
  if (evidence) await save()
  // Playwright error messages can contain fill arguments. Never emit them.
  console.error(JSON.stringify({ passed: false, phase, failureType: error.name }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
