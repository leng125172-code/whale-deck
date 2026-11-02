// Real installed UI and normal Authentik login. No response/Cookie injection or SQL writes.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, evidence, activePage
process.once('SIGTERM', () => { void (async () => { if (browser) await browser.close().catch(() => {}); process.exit(143) })() })
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/); assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'; assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 800 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage(); activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-visible-authentik-login'; const profile = await loginVisible(page, context, base)
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recursive-permissions`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const forbidden = { instanceId: fixture.instanceId, path: 'work/recursive-tree', fileMode: '0600', directoryMode: '0700', expectedVersion: fixture.sourceVersion }
  assert.equal((await context.request.post(base + endpoint, { headers: { Origin: base, 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 400)
  assert.equal((await context.request.post(base + endpoint, { headers: { ...headers, Origin: 'http://example.invalid', 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 403)
  const missing = { ...forbidden }; delete missing.directoryMode
  const rejected = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: missing })
  assert.equal(rejected.status(), 400); assert.equal((await rejected.json()).code, 'CONTAINER_RECURSIVE_PERMISSION_INPUT_INVALID')
  let registrations = 0, confirmations = 0, uploads = 0
  const headerChecks = [], completed = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === endpoint || path.startsWith(endpoint + '/') && path.endsWith('/confirm'))) {
      if (path === endpoint) registrations++; else confirmations++
      headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key']))
    }
    if (request.method() === 'PUT' && path.includes('/files/')) uploads++
  })
  async function query(id) { const r = await context.request.get(base + endpoint + '/' + id); assert.equal(r.status(), 200); return r.json() }
  const cases = [
    { name: 'complete', file: '0600', directory: '0700', changes: 2, width: 1440, theme: 'light' },
    { name: 'noop', file: '0600', directory: '0700', changes: 0, width: 320, theme: 'dark' },
    { name: 'alternate', file: '0640', directory: '0750', changes: 4, width: 1440, theme: 'dark' },
    { name: 'zero', file: '0000', directory: '0777', changes: 4, width: 320, theme: 'light' },
    { name: 'readable', file: '0640', directory: '0750', changes: 4, width: 320, theme: 'dark' },
    { name: 'cancel', file: '0600', directory: '0700', changes: 4, width: 320, height: 600, theme: 'light', cancel: true },
  ]
  for (const item of cases) {
    phase = item.name; await page.setViewportSize({ width: item.width, height: item.height ?? 800 }); await page.emulateMedia({ colorScheme: item.theme })
    const files = await openPermissionFiles(page, fixture)
    await files.getByRole('button', { name: '递归权限 recursive-tree', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '递归修改容器目录权限', exact: true })
    await dialog.getByLabel('文件模式', { exact: true }).fill(item.file); await dialog.getByLabel('目录模式', { exact: true }).fill(item.directory)
    const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
    const response = await registration; assert.equal(response.status(), 202)
    const registered = await response.json(), key = (await response.request().allHeaders())['idempotency-key'], body = response.request().postDataJSON()
    assert.equal(registered.state, 'Queued'); assert.equal(registered.executionJobId, null)
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 45_000 })
    const ready = await query(registered.id); assert.equal(ready.state, 'Ready'); assert.ok(ready.confirmation)
    assert.deepEqual([ready.command.fileMode, ready.command.directoryMode], [item.file, item.directory])
    assert.deepEqual([ready.entryCount, ready.fileCount, ready.directoryCount, ready.changeCount], [4, 2, 2, item.changes])
    assert.deepEqual([ready.rootUid, ready.rootGid, ready.executorUid, ready.executorGid], [2100, 3100, 1000, 1000])
    const duplicate = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': key }, data: body })
    assert.equal(duplicate.status(), 202); assert.equal((await duplicate.json()).id, registered.id)
    await page.reload(); await expect(page).toHaveURL(base + '/containers'); await openPermissionFiles(page, fixture)
    await page.getByRole('button', { name: '查看递归权限修改任务', exact: true }).click()
    await expect(dialog.getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 30_000 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth), true)
    await expect(dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true })).toBeInViewport({ ratio: 1 })
    await expect(dialog.getByRole('button', { name: '确认递归权限修改', exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(evidence, item.name + '.png') })
    let result
    if (item.cancel) {
      await dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true }).click(); await page.getByRole('button', { name: '继续等待', exact: true }).click()
      assert.equal((await query(ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '请求取消递归权限修改', exact: true }).click(); await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
      await expect(dialog.getByText('已取消', { exact: true })).toBeVisible({ timeout: 30_000 })
      result = await query(ready.id); assert.equal(result.state, 'Canceled'); assert.equal(result.executionJobId, null); assert.equal(result.receipt, null)
    } else {
      await dialog.getByRole('button', { name: '确认递归权限修改', exact: true }).click()
      await expect(dialog.getByText('递归权限修改已完成', { exact: true })).toBeVisible({ timeout: 45_000 })
      result = await query(ready.id); assert.equal(result.state, 'Succeeded'); assert.equal(result.receipt.state, 'Completed')
      assert.deepEqual([result.receipt.completedCount, result.receipt.changedCount, result.receipt.unknownCount], [4, item.changes, 0])
      const retry = await context.request.post(base + endpoint + '/' + ready.id + '/confirm', { headers: { ...headers, 'Idempotency-Key': ready.id }, data: { planHash: ready.confirmation.planHash, confirmed: true, version: ready.version } })
      assert.equal(retry.status(), 202); assert.equal((await retry.json()).executionJobId, result.executionJobId)
    }
    assert.equal(result.confirmation, null); completed.push({ name: item.name, ...result })
    // The next mode change is forbidden until a separate parent reads physical bytes/metadata and SQL/Root/HDD.
    await writeSignal(evidence, item.name + '-result.json', { subject: profile.subject, result: completed.at(-1) })
    const proof = await waitSignal(evidence, item.name + '-verified.json'); assert.equal(proof.id, result.id); assert.equal(proof.physicalAndHistoryVerified, true)
    await closePermissionDetails(page, dialog)
  }
  assert.equal(registrations, 6); assert.equal(confirmations, 5); assert.equal(uploads, 0); assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase = 'visible-dual-logout'; await logoutFromUserMenu(page, context, base)
  await writeFile(join(evidence, 'browser.json'), JSON.stringify({ revision, containerId: fixture.containerId, subject: profile.subject, completed, registrations, confirmations, registrationRetries: 6, confirmationRetries: 5, contentUploads: uploads, actualUi: true, normalDualLogout: true }), { mode: 0o600 })
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ recursivePermissionBrowser: 'passed', actualUi: true, cases: 6 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recursive_permission_browser.mjs:'))?.trim()
  if (activePage && evidence && !['startup', 'normal-visible-authentik-login'].includes(phase)) await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ recursivePermissionBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
