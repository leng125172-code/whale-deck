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
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-acl-unknown-[a-f0-9]{32}$/)
  const restoration = fixture.aclRestoreFaultVersion !== undefined
  if (restoration) { assert.ok([7, 8].includes(fixture.aclRestoreFaultVersion)); assert.equal(fixture.kind, fixture.aclRestoreFaultVersion === 8 ? 'post-acl-instance-change' : 'post-acl-target-removed'); assert.equal(fixture.postAcl, true); assert.equal(fixture.instanceChanged, fixture.aclRestoreFaultVersion === 8) }
  const sourceUi = fixture.recoverySourceUiVersion !== undefined
  if (sourceUi) { assert.equal(restoration, true); assert.equal(fixture.recoverySourceUiVersion, 1) }
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: mode === 'owner' ? 1440 : 320, height: 600 }, colorScheme: mode === 'owner' ? 'light' : 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-' + mode + '-login'
  const profile = await loginVisible(page, context, base, mode === 'owner' ? '/containers' : '/files')
  phase = 'original-visible-preflight'
  if (mode === 'owner') {
    const endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`
    ownEndpoint = base + endpoint
    const registrationEndpoint = restoration ? endpoint + '/restores' : endpoint
    let registrations = 0, confirmations = 0, bodies = 0
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path === registrationEndpoint) registrations++
      if (request.method() === 'POST' && currentIntent && path === endpoint + '/' + currentIntent + '/confirm') confirmations++
      if (request.method() === 'PUT' && path.includes('/files/')) bodies++
    })
    let dialog, intent, restoreSource
    if (restoration) {
      const { prepareRestoreFault } = await import('./acl_restore_fault_ui.mjs')
      const prepared = await prepareRestoreFault(page, context, base, fixture, profile)
      dialog = prepared.dialog; intent = prepared.intent; restoreSource = prepared.source
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
    await writeSignal(directory, 'owner-registered.json', { id: intent.id, subject: profile.subject })
    await expect(dialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 45_000 })
    await writeSignal(directory, 'owner-ready.json', { id: intent.id, subject: profile.subject, ...(restoration ? { restoreSource, instanceChanged: fixture.instanceChanged } : {}) })
    await waitSignal(directory, 'confirm.json', 30_000)
    const confirmed = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + intent.id + '/confirm' && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: restoration ? '确认ACL历史恢复' : '确认ACL修改', exact: true }).click()
    const accepted = await confirmed; assert.equal(accepted.status(), 202)
    const pending = await accepted.json(); assert.equal(pending.state, 'Pending'); assert.equal(pending.confirmation, null)
    await writeSignal(directory, 'owner-confirmed.json', pending)
    await waitSignal(directory, 'held.json', 40_000)
    phase = 'original-visible-inflight'
    await dialog.getByRole('button', { name: '查询原ACL修改任务', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText('正在修改ACL')
    await page.screenshot({ path: join(directory, 'original-inflight.png') })
    await writeSignal(directory, 'owner-inflight.json', { id: intent.id, actualUi: true })
    await waitSignal(directory, 'logout.json', 30_000)
    phase = 'original-normal-dual-logout'
    await closePermissionDetails(page, dialog)
    const logoutReceipt = await logoutFromUserMenu(page, context, base)
    assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(bodies, 0)
    await writeSignal(directory, 'owner-browser.json', { revision, id: intent.id, executionJobId: pending.executionJobId, subject: profile.subject,
      actualUi: true, registrations, confirmations, bodies, logout: logoutReceipt, normalDualLogout: true, ...(restoration ? { restoreSource, instanceChanged: fixture.instanceChanged } : {}) })
  } else {
    assert.notEqual(profile.subject, fixture.originalSubject)
    const root = '/api/v1/files/container-acls/recovery'
    let confirmations = 0, registrations = 0, bodies = 0
    const sourceReads = []
    let sourceCardText, sourceTimeText, sourceConfirmationText
    const expectedSource = sourceUi ? Object.fromEntries(['captureId','changedAclType','kind','createdAtUnixMs','originalMode','accessEntryCount','defaultEntryCount'].map(key => [key, fixture.restoreSource.entry[key]])) : null
    if (sourceUi) page.on('response', response => {
      if (new URL(response.url()).pathname === root + '/' + fixture.id + '/restore-source' && response.request().method() === 'GET')
        sourceReads.push(response.json().then(source => ({ status: response.status(), noStore: (response.headers()['cache-control'] ?? '').includes('no-store'), source })).catch(() => ({ invalidResponse: true })))
    })
    let jobReadsOnly = false
    async function inspectJob(stage) {
      assert.equal(fixture.postAcl, true)
      async function readJob(id) { const r = await context.request.get(base + '/api/v1/jobs/' + id); assert.equal(r.status(), 200); return r.json() }
      const proof = { id: fixture.id, stage, plan: await readJob(fixture.id), execution: await readJob(fixture.executionJobId), readOnly: true }
      assert.equal(proof.execution.id, fixture.executionJobId); assert.equal(proof.execution.state, stage === 'unknown' ? 'Running' : 'Failed')
      const summary = JSON.parse(proof.execution.resultJson); assert.equal(summary.aclConfirmed, null); assert.equal(summary.receiptState, 'Unknown'); assert.equal(summary.hasPublishedVersion, false)
      const tab = await context.newPage(); let writes = 0
      tab.on('request', r => { if (new URL(r.url()).pathname.startsWith('/api/v1/jobs') && r.method() !== 'GET') writes++ })
      try {
        const listing = tab.waitForResponse(r => new URL(r.url()).pathname === '/api/v1/jobs' && r.request().method() === 'GET')
        await tab.goto(base + '/jobs'); const response = await listing; assert.equal(response.status(), 200); const jobs = await response.json(), index = jobs.findIndex(j => j.id === fixture.executionJobId); assert.ok(index >= 0)
        await tab.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button', { name: '详情', exact: true }).click()
        const drawer = tab.getByRole('dialog', { name: '任务详情', exact: true }), region = drawer.getByRole('region', { name: 'ACL 安全结果', exact: true })
        await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(fixture.executionJobId); await expect(drawer.locator('.el-descriptions .el-tag')).toHaveText(stage === 'unknown' ? 'Running' : 'Failed')
        const title = stage === 'unknown' ? '结果未知，仍保留写入保护' : '管理员已明确处置未知结果'; await expect(region.getByText(title, { exact: true })).toBeVisible()
        async function field(label, text) { const value = region.getByText(label, { exact: true }).locator('xpath=following-sibling::dd[1]'); await expect(value).toHaveText(text); return value }
        await field('历史回执', 'Unknown'); await field('历史已验证', '是'); await field('已记录发布版本', '否'); await field('回执对象绑定有效', '否')
        const nullValue = await field('ACL 已确认', '未证明'); await nullValue.evaluate(el => el.scrollIntoView({ block: 'center', inline: 'nearest' })); await expect(nullValue).toBeInViewport({ ratio: 1 }); await tab.screenshot({ path: join(directory, stage + '-job-facts.png') })
        if (stage === 'acknowledged') await expect(region).toContainText('处置仅解除业务保护，不证明 ACL 已恢复或重新执行。')
        await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0); await expect(drawer.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
        for (const secret of [fixture.containerId, fixture.instanceId, 'work/acl-file.bin', fixture.originalSubject, profile.subject]) assert.ok(!(await region.innerText()).includes(secret))
        await region.getByText(title, { exact: true }).scrollIntoViewIfNeeded(); await expect(region.getByText(title, { exact: true })).toBeInViewport({ ratio: 1 }); assert.ok(await tab.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
        await tab.screenshot({ path: join(directory, stage + '-job.png') }); assert.equal(writes, 0)
        proof.taskCenter = { id: fixture.executionJobId, nullConfirmationVisible: true, noGenericWriteControls: true }
        await writeSignal(directory, stage + '-jobs.json', proof)
      } catch (error) {
        await tab.screenshot({ path: join(directory, 'failed-' + stage + '-job.png') }).catch(() => {})
        await writeSignal(directory, stage + '-job-failure.json', { failureType: error?.name, field: 'ACL 已确认', nullFieldBox: await tab.getByRole('region', { name: 'ACL 安全结果', exact: true }).getByText('ACL 已确认', { exact: true }).locator('xpath=following-sibling::dd[1]').boundingBox().catch(() => null) }).catch(() => {})
        throw error
      } finally { await tab.close() }
      jobReadsOnly = true
    }
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path === root + '/' + fixture.id + '/acknowledge') confirmations++
      if (request.method() === 'POST' && /\/files\/(acls|directories)$/.test(path)) registrations++
      if (request.method() === 'PUT' && path.includes('/files/')) bodies++
    })
    phase = 'different-administrator-unknown-page'
    await page.getByRole('button', { name: '容器ACL修改异常', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '容器ACL修改异常处置', exact: true })
    const candidate = dialog.locator('.acl-recovery__row').filter({ hasText: fixture.id })
    await expect(candidate).toHaveCount(1)
    await expect(candidate.getByText(fixture.containerId, { exact: true })).toBeVisible()
    await expect(candidate.getByText('对象：/work/acl-file.bin', { exact: true })).toBeVisible()
    await candidate.getByText(fixture.containerId, { exact: true }).scrollIntoViewIfNeeded()
    await expect(candidate.getByText(fixture.containerId, { exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(directory, 'unknown-target-mobile-dark.png') })
    await expect(candidate).toContainText('结果未知，保护已保留')
    await expect(candidate).toContainText(/ACL变更：\s*尚未确认/)
    await expect(candidate).toContainText('已验证HDD元数据留存')
    if (sourceUi) {
      const card = candidate.getByRole('region', { name: '历史ACL恢复来源', exact: true })
      await expect(card).toHaveCount(1); await expect(card.locator('code')).toHaveText(expectedSource.captureId)
      sourceTimeText = await page.evaluate(ms => new Date(ms).toLocaleString('zh-CN', { hour12: false }), expectedSource.createdAtUnixMs)
      for (const text of [sourceTimeText, '这是登记时保存的历史来源摘要，不证明当前文件状态或恢复成功。']) await expect(card).toContainText(text)
      await card.scrollIntoViewIfNeeded(); await expect(card).toBeInViewport({ ratio: 1 })
      sourceCardText = (await card.innerText()).replace(/\s+/g, ' ')
      for (const text of [expectedSource.changedAclType, expectedSource.kind === 'File' ? '文件' : '目录', '原模式 ' + expectedSource.originalMode.toString(8).padStart(4, '0'), 'access ' + expectedSource.accessEntryCount + ' 项', 'default ' + expectedSource.defaultEntryCount + ' 项']) assert.ok(sourceCardText.includes(text))
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
      await page.screenshot({ path: join(directory, 'unknown-source-mobile-dark.png') })
      assert.equal(sourceReads.length, 1); assert.deepEqual(await sourceReads[0], { status: 200, noStore: true, source: expectedSource })
    }
    await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await expect(dialog.getByText(/原任务会以失败结束/)).toBeVisible()
    await dialog.getByRole('button', { name: '暂不处置', exact: true }).click()
    assert.equal(confirmations, 0)
    const stillUnknown = await context.request.get(base + root + '/' + fixture.id); assert.equal(stillUnknown.status(), 200); assert.equal((await stillUnknown.json()).state, 'Unknown')
    if (restoration) {
      const denied = await context.request.get(base + `/api/v1/containers/${fixture.containerId}/files/acls/restores/${fixture.id}`)
      assert.equal(denied.status(), 404)
      assert.ok((stillUnknown.headers()['cache-control'] ?? '').includes('no-store'))
    }
    if (fixture.postAcl) await inspectJob('unknown')
    await writeSignal(directory, 'recovery-dismissed.json', { id: fixture.id, subject: profile.subject, confirmations: 0, stillUnknown: true })
    await waitSignal(directory, 'allow-ack.json', 40_000)
    await candidate.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await expect(dialog.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeInViewport({ ratio: 1 })
    await expect(dialog.getByRole('button', { name: '暂不处置', exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(directory, 'unknown-confirmation-mobile-dark.png') })
    if (sourceUi) {
      const reference = dialog.locator('.acl-recovery__reference')
      await expect(reference).toContainText(expectedSource.captureId); await expect(reference).toBeInViewport({ ratio: 1 })
      sourceConfirmationText = (await reference.innerText()).replace(/\s+/g, ' ')
      await page.screenshot({ path: join(directory, 'unknown-source-confirmation-mobile-dark.png') })
    }

    const acknowledged = page.waitForResponse(r => new URL(r.url()).pathname === root + '/' + fixture.id + '/acknowledge' && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
    const accepted = await acknowledged; assert.equal(accepted.status(), 202)
    const submitted = accepted.request().postDataJSON(); assert.equal(submitted.confirmed, true); assert.ok(Number.isSafeInteger(submitted.version))
    const ackHeaders = await accepted.request().allHeaders(); assert.equal(ackHeaders.origin, base); assert.equal(ackHeaders['idempotency-key'], fixture.id)
    await expect(dialog.getByRole('status')).toContainText('不代表ACL修改成功')
    const old = await (await context.request.get(base + root + '/' + fixture.id)).json()
    assert.equal(old.state, 'Acknowledged'); assert.equal(old.aclConfirmed, null); assert.equal(old.publishedVersion, null)
    assert.equal(old.historyVerified, true); assert.equal(old.confirmation, null)
    if (sourceUi) {
      assert.equal(sourceReads.length, 3)
      const reads = await Promise.all(sourceReads)
      for (const read of reads) assert.deepEqual(read, { status: 200, noStore: true, source: expectedSource })
      await writeSignal(directory, 'recovery-source-ui.json', { schemaVersion: 1, id: fixture.id, subject: profile.subject, reads,
        cardText: sourceCardText, timeText: sourceTimeText, confirmationText: sourceConfirmationText, cardInViewport: true, confirmationInViewport: true,
        horizontalOverflow: await page.evaluate(() => document.documentElement.scrollWidth > innerWidth) })
    }
    if (fixture.postAcl) await inspectJob('acknowledged')
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
    phase = 'actual-other-container-directory-visible'
    await dialog.getByRole('button', { name: '关闭', exact: true }).click()
    await page.goto(base + '/containers')
    const otherFiles = await openPermissionFiles(page, { containerName: fixture.waitingContainerName })
    const publishedDirectory = otherFiles.getByText('ACL未知保护后的目录', { exact: true })
    await publishedDirectory.scrollIntoViewIfNeeded()
    await expect(publishedDirectory).toBeInViewport({ ratio: 1 })
    await page.screenshot({ path: join(directory, 'waiting-published-mobile-dark.png') })
    await page.getByRole('dialog', { name: /容器详情/ }).locator('.el-drawer__close-btn').click()
    phase = 'different-administrator-normal-dual-logout'
    const logoutReceipt = await logoutFromUserMenu(page, context, base)
    await writeSignal(directory, 'recovery-browser.json', { revision, id: fixture.id, subject: profile.subject, actualUi: true,
      confirmations, registrations, bodies, originalState: old.state, waitingState: waiting.state, waitingVisibleInVue: true, waitingDirectoryInViewport: true, displayedContainerId: fixture.containerId, displayedPath: '/work/acl-file.bin', ackVersion: submitted.version, logout: logoutReceipt, normalDualLogout: true, jobReadsOnly, ...(sourceUi ? { recoverySourceUiVersion: 1 } : {}), ...(restoration ? { ownerRestoreStatus: 404, recoveryNoStore: true, instanceChanged: fixture.instanceChanged } : {}) })
  }
  await context.close()
  console.log(JSON.stringify({ aclUnknownBrowser: 'passed', mode }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_container_acl_unknown\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  if (page && directory && !phase.includes('login') && phase !== 'startup') await page.screenshot({ path: join(directory, 'failed-' + process.argv[3] + '-page.png') }).catch(() => {})
  console.error(JSON.stringify({ aclUnknownBrowser: 'failed', phase, failureType: error?.name, locations }))
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
