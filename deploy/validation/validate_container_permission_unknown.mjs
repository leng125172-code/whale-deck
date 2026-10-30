// Both original and different administrator use real installed controls and SSO.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, waitSignal, writeSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let browser, page, context, directory, currentIntent, ownEndpoint, phase = 'startup'
try {
  const revision = process.argv[2], mode = process.argv[3], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.ok(['owner', 'recovery'].includes(mode))
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-permission-unknown-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: mode === 'owner' ? 1440 : 320, height: 1100 }, colorScheme: mode === 'owner' ? 'light' : 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-' + mode + '-login'
  const profile = await loginVisible(page, context, base, mode === 'owner' ? '/containers' : '/files')
  if (mode === 'owner') {
    const endpoint = `/api/v1/containers/${fixture.containerId}/files/permissions`
    ownEndpoint = base + endpoint
    let registrations = 0, confirmations = 0, bodies = 0
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path === endpoint) registrations++
      if (request.method() === 'POST' && path.startsWith(endpoint + '/') && path.endsWith('/confirm')) confirmations++
      if (request.method() === 'PUT' && path.includes('/files/')) bodies++
    })
    const files = await openPermissionFiles(page, fixture)
    await files.getByRole('button', { name: '权限 original.bin', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '修改容器文件或目录权限', exact: true })
    await dialog.getByRole('textbox', { name: '普通权限模式', exact: true }).fill('0600')
    const queued = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成权限修改预检', exact: true }).click()
    const response = await queued; assert.equal(response.status(), 202)
    const intent = await response.json()
    currentIntent = intent.id
    await writeSignal(directory, 'owner-registered.json', { id: intent.id, subject: profile.subject })
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 45_000 })
    await writeSignal(directory, 'owner-ready.json', { id: intent.id, subject: profile.subject })
    await waitSignal(directory, 'confirm.json', 30_000)
    const confirmed = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + intent.id + '/confirm' && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '确认权限修改', exact: true }).click()
    const accepted = await confirmed; assert.equal(accepted.status(), 202)
    const pending = await accepted.json(); assert.equal(pending.state, 'Pending'); assert.equal(pending.confirmation, null)
    await writeSignal(directory, 'owner-confirmed.json', pending)
    await waitSignal(directory, 'held.json', 40_000)
    phase = 'original-visible-inflight'
    await dialog.getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText('正在修改权限')
    await page.screenshot({ path: join(directory, 'original-inflight.png') })
    await writeSignal(directory, 'owner-inflight.json', { id: intent.id, actualUi: true })
    await waitSignal(directory, 'logout.json', 30_000)
    phase = 'original-normal-dual-logout'
    await closePermissionDetails(page, dialog)
    await logoutFromUserMenu(page, context, base)
    assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(bodies, 0)
    await writeSignal(directory, 'owner-browser.json', { revision, id: intent.id, executionJobId: pending.executionJobId, subject: profile.subject,
      actualUi: true, registrations, confirmations, bodies, normalDualLogout: true })
  } else {
    assert.notEqual(profile.subject, fixture.originalSubject)
    const root = '/api/v1/files/container-permissions/recovery'
    let confirmations = 0, registrations = 0, bodies = 0
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path === root + '/' + fixture.id + '/acknowledge') confirmations++
      if (request.method() === 'POST' && /\/files\/(permissions|directories)$/.test(path)) registrations++
      if (request.method() === 'PUT' && path.includes('/files/')) bodies++
    })
    phase = 'different-administrator-unknown-page'
    await page.getByRole('button', { name: '容器权限修改异常', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '容器权限修改异常处置', exact: true })
    const candidate = dialog.locator('.permission-recovery__row').filter({ hasText: fixture.id })
    await expect(candidate).toHaveCount(1)
    await expect(candidate).toContainText('结果未知，保护已保留')
    await expect(candidate).toContainText('模式变更：尚未确认')
    await expect(candidate).toContainText('已验证HDD元数据留存')
    await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await expect(dialog.getByText(/原任务会以失败结束/)).toBeVisible()
    await dialog.getByRole('button', { name: '暂不处置', exact: true }).click()
    assert.equal(confirmations, 0)
    await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await page.screenshot({ path: join(directory, 'unknown-confirmation-mobile-dark.png') })
    const acknowledged = page.waitForResponse(r => new URL(r.url()).pathname === root + '/' + fixture.id + '/acknowledge' && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
    assert.equal((await acknowledged).status(), 202)
    await expect(dialog.getByRole('status')).toContainText('不代表权限修改成功')
    const old = await (await context.request.get(base + root + '/' + fixture.id)).json()
    assert.equal(old.state, 'Acknowledged'); assert.equal(old.modeConfirmed, null); assert.equal(old.publishedVersion, null)
    assert.equal(old.historyVerified, true); assert.equal(old.confirmation, null)
    phase = 'other-container-starts-only-after-release'
    let waiting
    for (let n = 0; n < 120; n++) {
      const response = await context.request.get(base + '/api/v1/files/container-directories/recovery/' + fixture.waitingId)
      assert.equal(response.status(), 200); waiting = await response.json()
      if (waiting.state === 'Succeeded') break
      assert.ok(['Pending', 'Applying'].includes(waiting.state))
      await page.waitForTimeout(500)
    }
    assert.equal(waiting.state, 'Succeeded')
    assert.equal(confirmations, 1); assert.equal(registrations, 0); assert.equal(bodies, 0)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true)
    await page.screenshot({ path: join(directory, 'unknown-acknowledged-mobile-dark.png') })
    phase = 'different-administrator-normal-dual-logout'
    await logoutFromUserMenu(page, context, base, dialog)
    await writeSignal(directory, 'recovery-browser.json', { revision, id: fixture.id, subject: profile.subject, actualUi: true,
      confirmations, registrations, bodies, originalState: old.state, waitingState: waiting.state, normalDualLogout: true })
  }
  await context.close()
  console.log(JSON.stringify({ permissionUnknownBrowser: 'passed', mode }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_permission_unknown\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  if (page && directory && !phase.includes('login') && phase !== 'startup') await page.screenshot({ path: join(directory, 'failed-' + process.argv[3] + '-page.png') }).catch(() => {})
  console.error(JSON.stringify({ permissionUnknownBrowser: 'failed', phase, failureType: error?.name, locations }))
  // Best-effort exact original-login cleanup only. Root Unknown is not erased.
  if (context && currentIntent && ownEndpoint) {
    try {
      const response = await context.request.get('http://127.0.0.1:8080/api/v1/auth/csrf')
      if (response.status() === 200) {
        const csrf = await response.json()
        const canceled = await context.request.post(ownEndpoint + '/' + currentIntent + '/cancel', { headers: {
          Origin: 'http://127.0.0.1:8080', [csrf.headerName]: csrf.token, 'Idempotency-Key': currentIntent,
        } })
        await writeSignal(directory, 'owner-failure-cancel.json', { id: currentIntent, status: canceled.status(), gatePassed: false })
      }
    } catch {}
  }
  process.exitCode = 1
} finally { if (browser) await browser.close() }
