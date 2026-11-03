// Actual product controls. Private file signals coordinate the fault observer,
// never intercept responses, inject a session, alter timers or send credentials.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, waitSignal, writeSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let logoutReceipt, browser, page, context, directory, phase = 'startup', action, currentIntent, ownEndpoint
let jobReadsOnly = false
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  const revision = process.argv[2]
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.ok(['cancel', 'logout', 'group-removal', 'account-disable', 'worker-crash', 'agent-restart', 'post-acl-cancel', 'post-acl-worker-crash', 'post-acl-group-removal', 'post-acl-account-disable', 'post-acl-agent-restart'].includes(fixture.kind))
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix))
  assert.match(directory.slice(prefix.length), /^container-acl-faults-[a-f0-9]{32}\/(cancel|logout|group-removal|account-disable|worker-crash|agent-restart|post-acl-cancel|post-acl-worker-crash|post-acl-group-removal|post-acl-account-disable|post-acl-agent-restart)$/)
  const behavior = fixture.kind.replace(/^post-acl-/, ''), postAcl = fixture.kind.startsWith('post-acl-')
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: ['cancel', 'group-removal', 'agent-restart'].includes(behavior) ? 320 : 1440, height: 600 },
    colorScheme: ['cancel', 'group-removal', 'agent-restart'].includes(behavior) ? 'dark' : 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-login'
  const profile = await loginVisible(page, context, base)
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`
  ownEndpoint = base + endpoint
  const restoration = fixture.aclRestoreFaultVersion !== undefined
  if (restoration) {
    assert.ok([1, 2, 3, 4].includes(fixture.aclRestoreFaultVersion))
    assert.ok((fixture.aclRestoreFaultVersion === 1 ? ['cancel', 'logout'] : fixture.aclRestoreFaultVersion === 2 ? ['group-removal', 'account-disable'] : fixture.aclRestoreFaultVersion === 3 ? ['worker-crash', 'agent-restart'] : ['post-acl-cancel', 'post-acl-worker-crash']).includes(fixture.kind))
  }
  const registrationEndpoint = restoration ? endpoint + '/restores' : endpoint
  const intentReadPath = registrationEndpoint + '/' // ID appended only after actual registration
  let registrations = 0, confirmations = 0, cancellations = 0, bodies = 0
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path === registrationEndpoint) registrations++
    if (request.method() === 'POST' && currentIntent && path === endpoint + '/' + currentIntent + '/confirm') confirmations++
    if (request.method() === 'POST' && currentIntent && path === endpoint + '/' + currentIntent + '/cancel') cancellations++
    if (request.method() === 'PUT' && path.includes('/files/')) bodies++
  })
  phase = 'visible-preflight'
  let dialog, intent, restoreSource = null
  if (restoration) {
    const prepared = await (await import('./acl_restore_fault_ui.mjs')).prepareRestoreFault(page, context, base, fixture, profile)
    dialog = prepared.dialog; intent = prepared.intent
    restoreSource = prepared.source
  } else {
    const files = await openPermissionFiles(page, fixture)
    await files.getByRole('button', { name: 'ACL acl-file.bin', exact: true }).click()
    dialog = page.getByRole('dialog', { name: '修改容器文件或目录ACL', exact: true })
    async function select(label, value) {
      await dialog.getByRole('combobox', { name: label, exact: true }).press('ArrowDown')
      await page.getByRole('listbox', { name: label, exact: true }).getByRole('option', { name: value, exact: true }).click()
    }
    await select('条目1 权限', '6 · rw-'); await select('条目2 权限', '0 · ---'); await select('条目3 权限', '0 · ---')
    await dialog.getByRole('button', { name: '添加ACL条目', exact: true }).click()
    await dialog.getByLabel('条目4 数值ID', { exact: true }).fill('2201'); await select('条目4 权限', '6 · rw-')
    await select('新增条目类型', 'mask'); await dialog.getByRole('button', { name: '添加ACL条目', exact: true }).click(); await select('条目5 权限', '4 · r--')
    const queued = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成ACL修改预检', exact: true }).click()
    const response = await queued; assert.equal(response.status(), 202)
    intent = await response.json()
  }
  currentIntent = intent.id
  await writeSignal(directory, 'registered.json', { id: intent.id, subject: profile.subject })
  await expect(dialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 45_000 })
  await writeSignal(directory, 'ready.json', { id: intent.id, subject: profile.subject, permissionExpiresAtUtc: profile.permissionExpiresAtUtc })
  await waitSignal(directory, 'confirm.json', 30_000)
  phase = 'visible-confirmation-with-worker-stopped'
  const confirming = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + intent.id + '/confirm' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: restoration ? '确认ACL历史恢复' : '确认ACL修改', exact: true }).click()
  const accepted = await confirming; assert.equal(accepted.status(), 202)
  const pending = await accepted.json(); assert.equal(pending.state, 'Pending'); assert.ok(pending.executionJobId); assert.equal(pending.confirmation, null)
  await writeSignal(directory, 'confirmed.json', pending)
  action = await waitSignal(directory, 'action.json', 40_000)
  assert.equal(action.kind, fixture.kind)
  phase = 'visible-bound-inflight-task'
  await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click()
  await expect(dialog.getByRole('status')).toContainText('正在修改ACL')
  if (restoration) {
    await dialog.getByRole('status').scrollIntoViewIfNeeded()
    await expect(dialog.getByRole('status')).toBeInViewport({ ratio: 1 })
  }
  await expect(dialog.getByRole('button', { name: '请求取消ACL修改', exact: true })).toBeInViewport({ ratio: 1 })
  await page.screenshot({ path: join(directory, 'inflight.png') })
  if (behavior === 'cancel') {
    phase = 'visible-cancel'
    await dialog.getByRole('button', { name: '请求取消ACL修改', exact: true }).click()
    await page.getByRole('button', { name: '继续等待', exact: true }).click()
    assert.equal(cancellations, 0)
    await dialog.getByRole('button', { name: '请求取消ACL修改', exact: true }).click()
    await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  } else if (fixture.kind === 'logout') {
    phase = 'normal-visible-logout'
    await closePermissionDetails(page, dialog)
    logoutReceipt = await logoutFromUserMenu(page, context, base)
    await expect(page.getByRole('region', { name: '容器ACL修改任务', exact: true })).toHaveCount(0)
  }
  await writeSignal(directory, 'action-done.json', { registrations, confirmations, cancellations, bodies, subject: profile.subject, dualLogout: fixture.kind === 'logout' })
  const final = await waitSignal(directory, 'settled.json')
  assert.equal(final.id, intent.id)
  const revoked = ['group-removal', 'account-disable'].includes(behavior)
  let terminalIntent = null, restoreTerminalSource = null, currentIdentityStatus = null, originalIntentStatus = null, hintsCleared = false
  if (revoked) {
    phase = 'original-live-authority-denied'
    const denied = await context.request.get(base + intentReadPath + intent.id); originalIntentStatus = denied.status()
    assert.ok([401, 403].includes(originalIntentStatus))
    const me = await context.request.get(base + '/api/v1/auth/me'); currentIdentityStatus = me.status()
    if (behavior === 'group-removal') {
      assert.equal(me.status(), 200); const identity = await me.json(); assert.equal(identity.isAdministrator, false); assert.equal(identity.subject, profile.subject)
    } else assert.ok([401, 403].includes(me.status()))
    await page.reload()
    await expect(page.getByRole('status', { name: '正在启动 WhaleDeck 协同平台', exact: true })).toHaveCount(0)
    if (behavior === 'group-removal') {
      await expect(page).toHaveURL(base + '/forbidden')
      await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
    } else {
      await expect(page).toHaveURL(url => url.origin === base && url.pathname === '/login')
      await expect(page.getByRole('heading', { name: '使用工作站账号登录', exact: true })).toBeVisible()
    }
    await expect(page.getByRole('region', { name: '容器ACL修改任务', exact: true })).toHaveCount(0)
    await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-acl-hints-v1'))).toBe(null)
    hintsCleared = true
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'authority-denied.png') })
  } else if (fixture.kind !== 'logout') {
    phase = 'visible-original-terminal'
    await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText(postAcl ? 'ACL修改已完成' : final.state === 'Failed' ? 'ACL修改未完成' : '已取消')
    if (restoration) {
      await dialog.getByRole('status').scrollIntoViewIfNeeded()
      await expect(dialog.getByRole('status')).toBeInViewport({ ratio: 1 })
    }
    if (postAcl || ['worker-crash', 'agent-restart'].includes(fixture.kind)) {
      const original = await context.request.get(base + intentReadPath + intent.id); assert.equal(original.status(), 200)
      assert.ok((original.headers()['cache-control'] ?? '').includes('no-store'))
      const value = await original.json()
      if (restoration) {
        assert.deepEqual(value.source, restoreSource); assert.equal(value.intent.id, intent.id)
        terminalIntent = value.intent; restoreTerminalSource = value.source
      } else terminalIntent = value
      assert.equal(terminalIntent.state, postAcl ? 'Succeeded' : 'Failed'); assert.equal(terminalIntent.resultCode, postAcl ? 'CONTAINER_ACL_COMPLETED' : 'CONTAINER_ACL_INTERRUPTED')
      assert.equal(terminalIntent.confirmation, null); assert.equal(terminalIntent.aclConfirmed, postAcl); assert.equal(terminalIntent.historyVerified, true); assert.equal(terminalIntent.receipt.state, postAcl ? 'Completed' : 'Aborted')
    }
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'terminal.png') })
    await closePermissionDetails(page, dialog)
    if (fixture.jobResultsVersion === 1) {
      phase = 'original-task-result-after-agent-restart'
      assert.equal(fixture.kind, 'post-acl-agent-restart')
      const tasks = []
      let jobWrites = 0
      page.on('request', r => { if (new URL(r.url()).pathname.startsWith('/api/v1/jobs') && r.method() !== 'GET') jobWrites++ })
      async function job(id) { const r = await context.request.get(base + '/api/v1/jobs/' + id); assert.equal(r.status(), 200); return r.json() }
      const proof = { intentId: intent.id, plan: await job(intent.id), execution: await job(pending.executionJobId), taskCenter: tasks }
      for (const execution of [false, true]) {
        const id = execution ? pending.executionJobId : intent.id
        const listing = page.waitForResponse(r => new URL(r.url()).pathname === '/api/v1/jobs' && r.request().method() === 'GET')
        await page.goto(base + '/jobs'); const response = await listing; assert.equal(response.status(), 200)
        const jobs = await response.json(), index = jobs.findIndex(j => j.id === id); assert.ok(index >= 0)
        await page.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
        const drawer = page.getByRole('dialog', { name: '任务详情', exact: true }), region = drawer.getByRole('region', { name: 'ACL 安全结果', exact: true })
        await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(id)
        await expect(drawer.locator('.el-descriptions .el-tag')).toHaveText('Succeeded')
        const title = execution ? 'ACL 修改已完成' : '预检完成，尚未修改 ACL'; await expect(region.getByText(title, { exact: true })).toBeVisible()
        await expect(region).toContainText(intent.id)
        async function field(label, value) { await expect(region.getByText(label, { exact: true }).locator('xpath=following-sibling::dd[1]')).toHaveText(value) }
        await field('阶段', execution ? '执行' : '预检'); await field('预检无需变更', '否'); await field('ACL 已确认', execution ? '是' : '未证明')
        await field('目标普通模式', terminalIntent.targetMode.toString(8).padStart(4, '0')); await field('历史回执', execution ? 'Completed' : '未取得'); await field('历史已验证', execution ? '是' : '否')
        for (const secret of [fixture.containerId, fixture.instanceId, 'work/acl-file.bin', profile.subject, terminalIntent.publishedVersion, terminalIntent.historyDigest, terminalIntent.receipt.fingerprint, terminalIntent.receipt.targetProof]) assert.ok(!(await region.innerText()).includes(secret))
        await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0); await expect(drawer.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
        await region.getByText(title, { exact: true }).scrollIntoViewIfNeeded(); await expect(region.getByText(title, { exact: true })).toBeInViewport({ ratio: 1 })
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
        const screenshot = execution ? 'execution-job.png' : 'plan-job.png'; await page.screenshot({ path: join(directory, screenshot) })
        tasks.push({ id, intentId: intent.id, execution, screenshot, noGenericWriteControls: true })
        await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click(); await expect(drawer).not.toBeVisible()
      }
      assert.equal(jobWrites, 0); jobReadsOnly = true; await writeSignal(directory, 'task-results.json', proof)
    }
    logoutReceipt = await logoutFromUserMenu(page, context, base)
  }
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, behavior === 'cancel' ? 1 : 0); assert.equal(bodies, 0)
  await writeSignal(directory, 'browser.json', { revision, kind: fixture.kind, id: intent.id, executionJobId: pending.executionJobId,
    subject: profile.subject, intentReadPath: intentReadPath + intent.id, registrations, confirmations, cancellations, bodies, finalState: final.state, normalDualLogout: !revoked, authorityRevoked: revoked,
    renderedRevocationPage: revoked ? behavior === 'group-removal' ? 'forbidden' : 'login' : null, currentIdentityStatus, originalIntentStatus, hintsCleared,
    terminalIntent, restoreTerminalSource, logout: logoutReceipt, actualUi: true, jobResultsVersion: fixture.jobResultsVersion, jobReadsOnly })
  await context.close()
  console.log(JSON.stringify({ aclFaultBrowser: 'passed', kind: fixture.kind }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_acl_faults\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  if (page && directory && phase !== 'normal-login' && phase !== 'startup') await page.screenshot({ path: join(directory, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ aclFaultBrowser: 'failed', phase, failureType: error?.name, locations }))
  if (directory) await writeSignal(directory, 'browser-failed.json', { phase, failureType: error?.name, locations }).catch(() => {})
  // Exact original-login cancellation is failure cleanup, never a passing UI case.
  if (context && currentIntent && ownEndpoint) {
    try {
      const csrfResponse = await context.request.get('http://127.0.0.1:8080/api/v1/auth/csrf')
      if (csrfResponse.status() === 200) {
        const csrf = await csrfResponse.json()
        const canceled = await context.request.post(ownEndpoint + '/' + currentIntent + '/cancel', {
          headers: { Origin: 'http://127.0.0.1:8080', [csrf.headerName]: csrf.token, 'Idempotency-Key': currentIntent },
        })
        await writeSignal(directory, 'failure-cancellation.json', { id: currentIntent, status: canceled.status(), gatePassed: false })
      }
    } catch {}
  }
  process.exitCode = 1
} finally { if (browser) await browser.close() }
