// Installed Vue, BFF, PostgreSQL and Authentik only. No route or identity fixtures.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

const base = 'http://127.0.0.1:8080'
const route = '/development/workspaces'
const api = '/api/v1' + route
let browser, phase = 'startup'
try {
  const { accounts, evidence, revision } = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/)
  assert.deepEqual(accounts.map(a => a.role), ['administrator', 'ordinary'])
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const results = []
  for (const account of accounts) {
    const admin = account.role === 'administrator'
    const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
    context.setDefaultTimeout(20_000)
    const page = await context.newPage()
    let applicationWrites = 0
    page.on('request', request => {
      const url = new URL(request.url())
      if (url.origin === base && url.pathname.startsWith('/api/v1/') &&
          !url.pathname.startsWith('/api/v1/auth/') && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) applicationWrites++
    })
    phase = account.role + '-login'
    await loginVisible(page, context, base, route, admin, account)
    await expect(page.getByRole('heading', { name: '开发空间', exact: true })).toBeVisible()
    for (const path of ['/api/v1/system/status', '/build-info.json']) {
      const response = await context.request.get(base + path)
      assert.equal(response.status(), 200)
      const value = await response.json()
      assert.equal((value.build ?? value).sourceRevision, revision)
    }
    phase = account.role + '-catalog'
    const response = await context.request.get(base + api + '/catalog')
    assert.equal(response.status(), admin ? 200 : 403)
    let catalog
    if (admin) {
      catalog = await response.json()
      assert.ok(Array.isArray(catalog.runtimes) && Array.isArray(catalog.storageRootIds))
      await expect(page.getByRole('link', { name: '登记开发空间', exact: true })).toBeVisible()
    } else {
      await expect(page.locator('.workspace-page .el-alert')).toBeVisible()
      await expect(page.getByRole('link', { name: '登记开发空间', exact: true })).toHaveCount(0)
    }
    const list = await context.request.get(base + api)
    assert.equal(list.status(), 200)
    const rows = await list.json()
    assert.ok(Array.isArray(rows.items))
    if (!admin) assert.equal(rows.items.length, 0)
    const scenes = []
    for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
      await page.setViewportSize({ width, height: 800 })
      await page.emulateMedia({ colorScheme })
      await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(colorScheme === 'dark')
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
      const screenshot = `${account.role}-list-${width}.png`
      await page.screenshot({ path: join(evidence, screenshot) })
      scenes.push({ width, colorScheme, screenshot, noHorizontalOverflow: true })
    }
    phase = account.role + '-new'
    await page.goto(base + route + '/new')
    await expect(page.getByRole('heading', { name: '登记开发空间', exact: true })).toBeVisible()
    if (admin) {
      await expect(page.getByRole('button', { name: '登记空间', exact: true })).toBeDisabled()
      if (!catalog.runtimes.length) await expect(page.getByText('管理员尚未登记可用的运行时。', { exact: true })).toBeVisible()
    } else await expect(page.locator('.workspace-page .el-alert')).toBeVisible()
    await page.setViewportSize({ width: 320, height: 800 })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    await page.screenshot({ path: join(evidence, `${account.role}-new-320.png`) })
    phase = account.role + '-logout'
    const logout = await logoutFromUserMenu(page, context, base)
    assert.equal((await context.request.get(base + api + '/catalog')).status(), 401)
    assert.equal(applicationWrites, 0)
    results.push({ role: account.role, catalogStatus: response.status(), catalog: catalog ? {
      runtimeCount: catalog.runtimes.length, storageRootCount: catalog.storageRootIds.length,
    } : null, listStatus: list.status(), visibleWorkspaceCount: rows.items.length, scenes, logout, applicationWrites })
    await context.close()
  }
  await writeFile(join(evidence, 'browser.json'), JSON.stringify({ passed: true, actualUi: true, revision, results }), { mode: 0o600, flag: 'wx' })
  await browser.close()
  browser = undefined
  console.log(JSON.stringify({ installedDevelopmentBrowser: 'passed', applicationWrites: 0 }))
} catch (error) {
  // Never print the browser input, cookies, response bodies or credentials.
  console.error(JSON.stringify({ installedDevelopmentBrowser: 'failed', phase, failureType: error?.name }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
