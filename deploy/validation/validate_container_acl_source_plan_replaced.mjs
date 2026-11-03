// One exact queued preflight; real UI, Worker and signed history, no response interception.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal, waitSignal, closePermissionDetails, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { prepareReplacedSourcePlan } from './acl_restore_fault_ui.mjs'

let browser, context, page, directory, dialog, loggedOut = false, phase = 'startup'
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8')), revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.equal(fixture.aclSourceFaultVersion, 8); assert.equal(fixture.kind, 'source-plan-replaced')
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-acl-source-plan-replaced-[a-f0-9]{32}$/)
  const base = 'http://127.0.0.1:8080', endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  let registrations = 0, confirmations = 0, cancellations = 0, contentWrites = 0, jobWrites = 0, key, id
  const events = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === endpoint + '/restores') { registrations++; key = request.headers()['idempotency-key']; events.push('restore-registration') }
    if (request.method() === 'POST' && id && path === endpoint + '/' + id + '/confirm') { confirmations++; events.push('restore-confirm') }
    if (request.method() === 'POST' && id && path === endpoint + '/' + id + '/cancel') cancellations++
    if (request.method() === 'PUT' && path.includes('/files/')) contentWrites++
    if (request.method() !== 'GET' && path.startsWith('/api/v1/jobs')) jobWrites++
  })
  phase = 'normal-login'; const profile = await loginVisible(page, context, base)
  const prepared = await prepareReplacedSourcePlan(page, context, base, fixture, profile)
  dialog = prepared.dialog; id = prepared.intent.id
  assert.equal(prepared.intent.state, 'Queued'); assert.equal(prepared.intent.executionJobId, null)
  assert.equal(prepared.intent.confirmation, null)
  await writeSignal(directory, 'registered.json', { id, subject: profile.subject, key, status: 202, original: { intent: prepared.intent, source: prepared.source } })
  const changed = await waitSignal(directory, 'source-replaced.json', 60_000)
  const entry = prepared.source.entry
  const historyUrl = base + endpoint + '/history/' + entry.captureId + '?instanceId=' + encodeURIComponent(fixture.instanceId) + '&path=work%2Facl-file.bin&expectedHistoryDigest=' + entry.historyDigest
  const replaced = await context.request.get(historyUrl); assert.equal(replaced.status(), 409)
  const sourceError = await replaced.json(); assert.equal(sourceError.code, 'AGENT_VERSION_CONFLICT')
  const newRead = await context.request.get(historyUrl.replace(entry.historyDigest, changed.replacementDigest)); assert.equal(newRead.status(), 200)
  const replacementSnapshot = await newRead.json(), expectedReplacement = structuredClone(prepared.source)
  expectedReplacement.entry.createdAtUnixMs++; expectedReplacement.entry.historyDigest = changed.replacementDigest
  assert.deepEqual(replacementSnapshot, expectedReplacement)
  const byKeyRead = await context.request.get(base + endpoint + '/restores/by-key/' + key); assert.equal(byKeyRead.status(), 200)
  const byKey = await byKeyRead.json(); assert.deepEqual(byKey.intent, prepared.intent); assert.deepEqual(byKey.source, prepared.source)
  await writeSignal(directory, 'replaced-observed.json', { id, sourceStatus: 409, sourceCode: sourceError.code, replacementReadStatus: 200, replacementSnapshot, byKeyStatus: 200, byKey, queued: prepared.intent })
  const failed = await waitSignal(directory, 'failed.json', 180_000)
  assert.equal(failed.id, id); assert.equal(failed.executionJobId, null)
  phase = 'source-restored-original-execution-remains-failed'
  const reread = await context.request.get(historyUrl); assert.equal(reread.status(), 200); assert.deepEqual(await reread.json(), prepared.source)
  await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click()
  await expect(dialog.getByRole('status')).toHaveText('ACL修改未完成')
  await dialog.getByRole('status').scrollIntoViewIfNeeded(); await expect(dialog.getByRole('status')).toBeInViewport({ ratio: 1 })
  await page.screenshot({ path: join(directory, 'source-plan-failed-mobile-dark.png') })
  const finalRead = await context.request.get(base + endpoint + '/restores/' + id); assert.equal(finalRead.status(), 200)
  const final = await finalRead.json(); assert.equal(final.intent.state, 'Failed'); assert.equal(final.intent.resultCode, 'CONTAINER_ACL_RESTORE_SOURCE_CHANGED')
  assert.deepEqual(final.source, prepared.source); assert.equal(final.intent.aclConfirmed, null)
  assert.equal(final.intent.executionJobId, null); assert.equal(final.intent.confirmation, null); assert.equal(final.intent.receipt, null)
  await expect(dialog.getByRole('button', { name: '确认ACL历史恢复', exact: true })).toHaveCount(0)
  const jobRead = await context.request.get(base + '/api/v1/jobs/' + id); assert.equal(jobRead.status(), 200)
  const job = await jobRead.json(); assert.equal(job.state, 'Failed'); assert.equal(job.jobType, 'containers.files.acl-plan')
  await writeSignal(directory, 'terminal.json', { id, final, job })
  await waitSignal(directory, 'verified-row.json', 60_000)
  await closePermissionDetails(page, dialog)
  const taskCenter = []
  for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
    phase = 'real-task-center-' + width + '-' + colorScheme
    await page.setViewportSize({ width, height: 600 }); await page.emulateMedia({ colorScheme })
    const listing = page.waitForResponse(r => isJobPageResponse(r, base)); await page.goto(base + '/jobs')
    const listResponse = await listing; assert.equal(listResponse.status(), 200); const jobs = await listResponse.json()
    const position = jobs.findIndex(j => j.id === id); assert.ok(position >= 0)
    await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button', { name: '详情', exact: true }).click()
    const drawer = page.getByRole('dialog', { name: '任务详情', exact: true }), region = drawer.getByRole('region', { name: 'ACL 安全结果', exact: true })
    await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(id)
    await expect(drawer.locator('.el-descriptions .el-tag')).toHaveText('Failed')
    const title = region.getByText('预检失败，未进入 ACL 修改执行', { exact: true }); await expect(title).toBeVisible()
    async function field(label, value) { const node = region.getByText(label, { exact: true }).locator('xpath=following-sibling::dd[1]'); await expect(node).toHaveText(value); return node }
    await field('原意图', id); await field('阶段', '预检'); await field('ACL 类型', 'access 访问权限')
    for (const label of ['目标对象', '目标普通模式', '预检无需变更', 'ACL 已确认', '回执对象绑定有效']) await field(label, '未证明')
    await field('历史回执', '未取得')
    for (const label of ['可能已有历史', '历史已验证', '已记录发布版本']) await field(label, '否')
    await expect(region).not.toContainText('执行失败')
    await field('结果码', 'CONTAINER_ACL_RESTORE_SOURCE_CHANGED')
    for (const value of [fixture.containerId, fixture.instanceId, profile.subject, 'work/acl-file.bin']) assert.ok(!(await region.innerText()).includes(value))
    await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0)
    await expect(drawer.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
    await title.scrollIntoViewIfNeeded(); await expect(title).toBeInViewport({ ratio: 1 })
    const titleScreenshot = `failed-plan-title-${width}-${colorScheme}.png`; await page.screenshot({ path: join(directory, titleScreenshot) })
    const fact = await field('ACL 已确认', '未证明'); await fact.evaluate(el => el.scrollIntoView({ block: 'center', inline: 'nearest' })); await expect(fact).toBeInViewport({ ratio: 1 })
    const box = await drawer.locator('.el-drawer__body').boundingBox(); assert.ok(box)
    for (const b of await region.locator('dd').evaluateAll(elements => elements.map(el => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right } }))) assert.ok(b.left >= box.x && b.right <= box.x + box.width)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    const factsScreenshot = `failed-plan-facts-${width}-${colorScheme}.png`; await page.screenshot({ path: join(directory, factsScreenshot) })
    taskCenter.push({ id: id, width, colorScheme, titleScreenshot, factsScreenshot, unprovedConfirmationVisible: true, noGenericWriteControls: true })
    await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  }
  const logout = await logoutFromUserMenu(page, context, base); loggedOut = true
  assert.equal(registrations, 1); assert.equal(confirmations, 0); assert.equal(cancellations, 0); assert.equal(contentWrites, 0); assert.equal(jobWrites, 0)
  assert.deepEqual(events, ['restore-registration'])
  await writeSignal(directory, 'browser.json', { revision, aclSourceFaultVersion: 8, kind: fixture.kind, id, executionJobId: null,
    subject: profile.subject, actualUi: true, normalDualLogout: true, logout, registrations, confirmations, cancellations, contentWrites, jobWrites, events, taskCenter,
    originalSourceReadStatus: 409, replacementReadStatus: 200, sourceReadableAgain: true, finalState: 'Failed', finalCode: 'CONTAINER_ACL_RESTORE_SOURCE_CHANGED' })
} catch (error) {
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, type: error?.name ?? 'Error' }).catch(() => {})
  if (page && !page.isClosed() && phase !== 'normal-login') await page.screenshot({ path: join(directory, 'failed-gate-page.png') }).catch(() => {})
  console.error(JSON.stringify({ sourcePlanReplaced: 'failed', phase, type: error?.name ?? 'Error' })); process.exitCode = 1
} finally {
  if (!loggedOut && page && context && !page.isClosed()) await logoutFromUserMenu(page, context, 'http://127.0.0.1:8080', dialog).catch(() => {})
  await context?.close(); await browser?.close()
}
