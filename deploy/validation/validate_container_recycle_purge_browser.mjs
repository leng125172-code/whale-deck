// Installed Authentik/Vue only. No routed responses, injected sessions or native writes.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, page, evidence
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base)
  const recycle = `/api/v1/containers/${fixture.containerId}/files/recycles`
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recycle-purges`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const mutations = [], headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === recycle || path.startsWith(recycle + '/') || path === endpoint || path.startsWith(endpoint + '/'))) {
      mutations.push(path); headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key'] && !!h[csrf.headerName.toLowerCase()]))
    }
    assert.ok(!(request.method() === 'PUT' && path.includes('/files/')))
  })
  async function get(path) { const response = await context.request.get(base + path); assert.equal(response.status(), 200); return response.json() }
  async function coordinate(name, value) {
    await writeSignal(evidence, name + '-completed.json', { subject: profile.subject, result: value })
    assert.equal((await waitSignal(evidence, name + '-accepted.json', 60_000)).id, value.id)
  }
  phase = 'page-create-original-recycle-source'
  let files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '回收 recycle-rpc.bin', exact: true }).click()
  const sourceDialog = page.getByRole('dialog', { name: '回收容器普通文件', exact: true })
  const sourceRegistration = page.waitForResponse(r => new URL(r.url()).pathname === recycle && r.request().method() === 'POST')
  await sourceDialog.getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  const sourceResponse = await sourceRegistration; assert.equal(sourceResponse.status(), 202)
  const sourceInitial = await sourceResponse.json()
  await expect(sourceDialog.getByText('预检完成，尚未移除文件', { exact: true })).toBeVisible({ timeout: 60_000 })
  const sourceReady = await get(recycle + '/' + sourceInitial.id)
  assert.equal(sourceReady.state, 'Ready'); assert.equal(sourceReady.sha256, fixture.original.sha256)
  await sourceDialog.getByRole('button', { name: '确认文件回收', exact: true }).click()
  await expect(sourceDialog.getByText('文件回收已完成', { exact: true })).toBeVisible({ timeout: 60_000 })
  const source = await get(recycle + '/' + sourceInitial.id)
  assert.equal(source.state, 'Succeeded'); assert.equal(source.receipt.sourceRetained, true)
  await coordinate('source', { ...source, name: 'trash' })
  await closePermissionDetails(page, sourceDialog)
  const completed = [], keys = [], jobProofs = []
  phase = 'purge-csrf-origin-refusal'
  for (const variant of ['csrf', 'origin']) {
    const response = await context.request.post(base + endpoint, {
      headers: variant === 'csrf' ? { Origin: base, 'Idempotency-Key': crypto.randomUUID() } : { ...headers, Origin: 'http://unapproved.invalid', 'Idempotency-Key': crypto.randomUUID() },
      data: { sourceIntentId: source.id },
    })
    assert.ok([400, 403].includes(response.status()))
  }
  for (const item of [{ name: 'cancel', theme: 'light' }, { name: 'success', theme: 'dark' }]) {
    phase = 'purge-' + item.name; await page.emulateMedia({ colorScheme: item.theme })
    await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(item.theme === 'dark')
    files = await openPermissionFiles(page, fixture)
    await expect(files.getByText('recycle-rpc.bin', { exact: true })).toHaveCount(0)
    const catalogResponse = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/sources' && r.request().method() === 'GET')
    await files.getByRole('button', { name: '清理留存原件', exact: true }).click()
    const catalog = await catalogResponse; assert.equal(catalog.status(), 200)
    const sourcePage = await catalog.json(); const selected = sourcePage.items.find(r => r.sourceIntentId === source.id)
    assert.ok(selected); assert.equal(selected.sourceOperationId, source.executionJobId)
    assert.equal(selected.historyDigest, source.historyDigest); assert.equal(selected.sha256, fixture.original.sha256)
    const dialog = page.getByRole('dialog', { name: '清理容器留存原件', exact: true })
    await dialog.locator('.purge-source-row').filter({ hasText: source.id }).getByRole('button', { name: '选择此来源', exact: true }).click()
    const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成留存清理预检', exact: true }).click()
    const response = await registration; assert.equal(response.status(), 202)
    assert.deepEqual(response.request().postDataJSON(), { sourceIntentId: source.id })
    const initial = await response.json(), key = (await response.request().allHeaders())['idempotency-key']
    assert.ok(!keys.includes(key)); keys.push(key)
    await expect(dialog.getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible({ timeout: 60_000 })
    const ready = await get(endpoint + '/' + initial.id)
    assert.equal(ready.state, 'Ready'); assert.equal(ready.sourceIntentId, source.id)
    assert.equal(ready.sourceOperationId, source.executionJobId); assert.equal(ready.executionJobId, null)
    assert.equal(ready.sha256, fixture.original.sha256); assert.equal(ready.sourceHistoryDigest, source.historyDigest)
    assert.equal((await get(endpoint + '/by-key/' + key)).id, ready.id)
    const duplicate = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': key }, data: { sourceIntentId: source.id } })
    assert.equal(duplicate.status(), 202); assert.equal((await duplicate.json()).id, ready.id)
    await page.reload(); await expect(page).toHaveURL(base + '/containers'); await openPermissionFiles(page, fixture)
    await page.getByRole('button', { name: '清理留存原件', exact: true }).click()
    await expect(dialog.getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(dialog.getByRole('button', { name: '请求取消留存清理', exact: true })).toBeInViewport({ ratio: 1 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(evidence, item.name + '-ready.png') })
    if (item.name === 'cancel') {
      await dialog.getByRole('button', { name: '请求取消留存清理', exact: true }).click()
      await page.getByRole('button', { name: '继续等待', exact: true }).click()
      assert.equal((await get(endpoint + '/' + ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '请求取消留存清理', exact: true }).click()
      await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
      await expect(dialog.getByText('清理已取消', { exact: true })).toBeVisible({ timeout: 30_000 })
    } else {
      await coordinate('ready-to-purge', { id: ready.id, sourceIntentId: source.id, sourceOperationId: source.executionJobId, version: ready.version })
      await dialog.getByRole('button', { name: '确认留存清理', exact: true }).click()
      const finalConfirm = page.getByRole('dialog', { name: '确认清理留存原件', exact: true })
      await expect(finalConfirm).toContainText('当前尚未提供清理后的归档恢复入口')
      await finalConfirm.getByRole('button', { name: '暂不清理', exact: true }).click()
      assert.equal((await get(endpoint + '/' + ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '确认留存清理', exact: true }).click()
      const finalReady = await get(endpoint + '/' + ready.id); assert.equal(finalReady.state, 'Ready'); assert.equal(finalReady.version, ready.version)
      await coordinate('final-confirm', { id: finalReady.id, sourceIntentId: source.id, sourceOperationId: source.executionJobId, version: finalReady.version })
      await expect(finalConfirm.getByRole('button', { name: '确认删除留存原件', exact: true })).toBeInViewport({ ratio: 1 })
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
      await finalConfirm.getByRole('button', { name: '确认删除留存原件', exact: true }).click()
      await expect(dialog.getByText('本次留存原件已清理', { exact: true })).toBeVisible({ timeout: 60_000 })
    }
    const result = await get(endpoint + '/' + ready.id)
    assert.equal(result.state, item.name === 'cancel' ? 'Canceled' : 'Succeeded'); assert.equal(result.confirmation, null)
    completed.push({ name: item.name, ...result }); await coordinate(item.name, result)
    await page.screenshot({ path: join(evidence, item.name + '-result.png') }); await closePermissionDetails(page, dialog)
  }
  phase = 'actual-job-summaries'; await page.setViewportSize({ width: 1440, height: 1050 })
  for (const [index, id] of [completed[0].planningJobId, completed[1].executionJobId].entries()) {
    const listing = page.waitForResponse(r => isJobPageResponse(r, base)); await page.goto(base + '/jobs')
    const listed = await (await listing).json(); const position = listed.findIndex(j => j.id === id); assert.ok(position >= 0)
    const job = await get('/api/v1/jobs/' + id), safe = JSON.parse(job.resultJson)
    assert.equal(Object.keys(safe).length, 16); assert.equal(safe.sourceIntentId, source.id); assert.equal(safe.sourceOperationId, source.executionJobId)
    assert.equal(safe.state, index === 0 ? 'Ready' : 'Succeeded'); assert.equal(safe.jobId, id)
    await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button', { name: '详情', exact: true }).click()
    const detail = page.getByRole('dialog', { name: '任务详情', exact: true }), summary = detail.getByRole('region', { name: '留存清理安全结果', exact: true })
    await expect(summary).toContainText(index === 0 ? '预检完成，尚未删除留存原件' : '留存原件清理已完成')
    await expect(summary).toContainText(source.id); await expect(summary).toContainText(source.executionJobId)
    await page.screenshot({ path: join(evidence, 'actual-job-' + index + '.png') }); jobProofs.push(job)
    await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  }
  assert.equal((await get(endpoint + '/sources')).items.some(r => r.sourceIntentId === source.id), false)
  assert.equal(mutations.filter(p => p === recycle).length, 1); assert.equal(mutations.filter(p => p === recycle + '/' + source.id + '/confirm').length, 1)
  assert.equal(mutations.filter(p => p === endpoint).length, 2); assert.equal(mutations.filter(p => p.endsWith('/cancel')).length, 1)
  assert.equal(mutations.filter(p => p.startsWith(endpoint + '/') && p.endsWith('/confirm')).length, 1)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase = 'normal-dual-logout'; await logoutFromUserMenu(page, context, base)
  assert.equal((await context.request.get(base + endpoint + '/' + completed[1].id)).status(), 401)
  phase = 'visible-relogin'; const again = await loginVisible(page, context, base); assert.equal(again.subject, profile.subject)
  for (const path of [endpoint + '/' + completed[0].id, endpoint + '/' + completed[1].id, ...keys.map(k => endpoint + '/by-key/' + k), recycle + '/' + source.id])
    assert.equal((await context.request.get(base + path)).status(), 404)
  phase = 'second-dual-logout'; await logoutFromUserMenu(page, context, base)
  await writeSignal(evidence, 'browser.json', { revision, containerId: fixture.containerId, subject: profile.subject, source, completed, keys, jobProofs, mutations, normalDualLogout: true, reloginOriginalReferencesDenied: 5, actualUi: true })
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualRecyclePurgeBrowser: 'passed', cases: 2 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_purge_browser.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-relogin'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualRecyclePurgeBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
