// Real installed UI/API/Worker/Agent only. The dedicated workspace is retained
// for product lifecycle acceptance; no business/audit rows are deleted by SQL.
import assert from 'node:assert/strict'
import { createHash, randomUUID } from 'node:crypto'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { installedLifecycle } from './installed_development_lifecycle.mjs'
import { installedRetirementBusiness } from './installed_workspace_retirement.mjs'

const base = 'http://127.0.0.1:8080', route = '/development/workspaces', api = '/api/v1' + route
let browser, page, evidence, phase = 'startup', state
async function save() {
  state.phase = phase
  await writeFile(join(evidence, 'state.pending.json'), JSON.stringify(state, null, 2), { mode: 0o600 })
  await rename(join(evidence, 'state.pending.json'), join(evidence, 'state.json'))
}
async function poll(context, path, predicate, timeout = 240_000) {
  const deadline = Date.now() + timeout
  let value
  while (Date.now() < deadline) {
    const response = await context.request.get(base + path)
    if (response.status() === 429) {
      const seconds = Number(response.headers()['retry-after'] ?? '60')
      const retryAfter = Number.isFinite(seconds) ? Math.max(1, Math.min(60, seconds)) : 60
      state.rateLimits ??= []
      state.rateLimits.push({ path, retryAfter })
      await save()
      await new Promise(resolve => setTimeout(resolve, retryAfter * 1000))
      continue
    }
    assert.equal(response.status(), 200)
    value = await response.json()
    if (await predicate(value)) return value
    await new Promise(resolve => setTimeout(resolve, 3000))
  }
  throw new Error('INSTALLED_RUNTIME_POLL_DEADLINE')
}
async function headers(context, contentType = 'application/json') {
  const response = await context.request.get(base + '/api/v1/auth/csrf')
  assert.equal(response.status(), 200)
  const value = await response.json()
  assert.equal(typeof value.headerName, 'string')
  assert.equal(typeof value.token, 'string')
  return { Origin: base, 'Content-Type': contentType, [value.headerName]: value.token }
}
try {
  const input = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  if (input.retirementProbe) assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_BUSINESS_PROBE, '1')
  evidence = input.evidence
  try { state = JSON.parse(await readFile(join(evidence, 'state.json'), 'utf8')) }
  catch (error) { if (error.code !== 'ENOENT') throw error; state = { slug: input.slug, imports: [], runs: [] } }
  assert.equal(state.slug, input.slug)
  state.browserAttempts = (state.browserAttempts ?? 0) + 1
  const browserAttempt = state.browserAttempts
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000)
  page = await context.newPage()
  phase = 'actual-admin-login'
  const profile = await loginVisible(page, context, base, route, true, input.accounts[0])
  assert.equal(typeof profile.subject, 'string')
  assert.ok(profile.subject.length > 0)
  state.ownerSubject ??= profile.subject
  assert.equal(state.ownerSubject, profile.subject)
  await save()
  const build = await (await context.request.get(base + '/api/v1/system/status')).json()
  assert.equal(build.build.sourceRevision, input.revision)
  const catalog = await (await context.request.get(base + api + '/catalog')).json()
  assert.ok(catalog.storageRootIds.includes('nvme-main'))
  assert.ok(catalog.runtimes.some(r => r.key === 'node' && r.version === '24.15.0'))
  assert.ok(catalog.runtimes.some(r => r.key === 'dotnet' && r.version === '10.0.402'))
  const invalid = randomUUID()
  assert.equal((await context.request.post(`${base}${api}/${invalid}/runner-preflight`, {
    headers: { Origin: base, 'Content-Type': 'application/json' }, data: {},
  })).status(), 400)
  const wrongOrigin = await headers(context)
  wrongOrigin.Origin = 'https://invalid.example'
  assert.equal((await context.request.post(`${base}${api}/${invalid}/runner-preflight`, { headers: wrongOrigin, data: {} })).status(), 403)
  if (!state.retirementStarted) {
  phase = 'register-workspace'
  if (!state.workspace) {
    const listed = await (await context.request.get(base + api + '?take=100')).json()
    const replay = listed.items.filter(row => row.slug === input.slug && row.ownerSubject === profile.subject)
    assert.ok(replay.length <= 1)
    if (replay.length) state.workspace = replay[0]
    else {
      await page.getByRole('link', { name: '登记开发空间', exact: true }).click()
      await page.getByRole('textbox', { name: '空间标识' }).fill(input.slug)
      state.registrationCheckboxes = await page.locator('.workspace-page label.el-checkbox').evaluateAll(labels =>
        labels.map(label => ({ text: label.textContent, ariaLabel: label.querySelector('input')?.getAttribute('aria-label') })))
      await save()
      for (const runtime of ['node', 'dotnet']) {
        const label = page.locator('.workspace-page label.el-checkbox').filter({ hasText: runtime })
        await expect(label).toHaveCount(1)
        await label.click()
        await expect(label.locator('input[type="checkbox"]')).toBeChecked()
      }
      for (const [name, value] of [['CPU（毫核）', '1000'], ['内存（MiB）', '2048'], ['磁盘（MiB）', '8192']]) {
        await page.getByRole('spinbutton', { name, exact: true }).fill(value)
        await page.getByRole('spinbutton', { name, exact: true }).press('Tab')
      }
      const posted = page.waitForResponse(r => new URL(r.url()).pathname === api && r.request().method() === 'POST')
      await page.getByRole('button', { name: '登记空间', exact: true }).click()
      const response = await posted
      assert.equal(response.status(), 201)
      state.workspace = (await response.json()).workspace
    }
    await save()
  }
  const id = state.workspace.id
  await page.goto(`${base}${route}/${id}/environment`)
  await expect(page.getByRole('heading', { name: input.slug, exact: true })).toBeVisible()
  let workspace = await (await context.request.get(`${base}${api}/${id}`)).json()
  if (workspace.state !== 'StorageAllocated') {
    phase = 'signed-storage-allocation'
    await page.getByRole('button', { name: '核对存储计划', exact: true }).click()
    await expect(page.getByRole('button', { name: '确认分配存储', exact: true })).toBeVisible()
    await page.getByRole('button', { name: '确认分配存储', exact: true }).click()
    workspace = await poll(context, `${api}/${id}`, value => value.state === 'StorageAllocated')
  }
  state.workspace = workspace
  await save()
  await page.reload()
  if (!state.lifecycle?.stop?.operationId) {
    await page.getByRole('button', { name: '核验 Runner 存储', exact: true }).click()
    await expect(page.getByText('Runner 存储和配额核验通过。', { exact: true })).toBeVisible()
    await page.screenshot({ path: join(evidence, 'allocated.png') })
  } else {
    // A repair may resume with the workspace legitimately stopped. The prior
    // preflight and real runs remain evidence; do not require new admission.
    assert.equal(workspace.state, 'StorageAllocated')
    assert.ok(['Stopping', 'Stopped', 'Resuming', 'Active'].includes(workspace.lifecycleState))
  }
  phase = 'real-source-upload'
  const raw = await readFile(input.archive), sha = createHash('sha256').update(raw).digest('hex')
  assert.equal(sha, input.archiveSha256)
  if (!state.imports.length) { state.imports.push({ id: randomUUID(), browserAttempt }); await save() }
  let sourceId = state.imports.at(-1).id, sourcePath = `${api}/${id}/source-imports/${sourceId}`
  let source = await context.request.get(base + sourcePath)
  if (source.ok()) {
    const prior = await source.json()
    if (prior.state !== 'Published' && state.imports.at(-1).browserAttempt !== browserAttempt) {
      // Mutating an existing import is intentionally bound to its original
      // login. A fresh real login can cancel it as the owner, not impersonate
      // the old session. Preserve that denial and the original intent.
      const denied = await context.request.put(`${base}${sourcePath}/chunks/0`, {
        headers: await headers(context, 'application/octet-stream'), data: raw.subarray(0, 1024**2),
      })
      assert.equal(denied.status(), 403)
      const unchanged = await (await context.request.get(base + sourcePath)).json()
      assert.equal(unchanged.receivedBytes, prior.receivedBytes)
      state.imports.at(-1).crossSessionWriteDenied = true
      const canceled = await context.request.post(base + sourcePath + '/cancel', { headers: await headers(context) })
      assert.ok(canceled.ok())
      const terminal = await poll(context, sourcePath, value => ['Canceled', 'Failed'].includes(value.state))
      assert.equal(terminal.state, 'Canceled')
      state.imports.at(-1).view = terminal
      state.imports.push({ id: randomUUID(), browserAttempt })
      await save()
      sourceId = state.imports.at(-1).id
      sourcePath = `${api}/${id}/source-imports/${sourceId}`
      source = await context.request.get(base + sourcePath)
    }
  }
  if (source.status() === 404) {
    source = await context.request.put(base + sourcePath, { headers: await headers(context),
      data: { format: 'tar', archiveBytes: raw.length, archiveSha256: sha } })
    assert.ok(source.ok())
  } else assert.equal(source.status(), 200)
  let imported = await source.json()
  if (imported.state === 'Uploading') {
    for (let offset = 0; offset < raw.length; offset += 1024**2) {
      const response = await context.request.put(`${base}${sourcePath}/chunks/${offset}`, {
        headers: await headers(context, 'application/octet-stream'), data: raw.subarray(offset, offset + 1024**2),
      })
      assert.ok(response.ok())
    }
  }
  imported = await poll(context, sourcePath, value => ['Staged', 'Published', 'Failed', 'Canceled'].includes(value.state))
  assert.ok(['Staged', 'Published'].includes(imported.state))
  if (imported.state === 'Staged') {
    phase = 'visible-source-confirmation'
    await page.goto(`${base}${route}/${id}/source`)
    const confirmation = page.locator('label.el-checkbox').filter({ hasText: '我已核对暂存计划，确认发布到该空间' })
    await expect(confirmation).toHaveCount(1)
    await confirmation.click()
    await expect(confirmation.locator('input[type="checkbox"]')).toBeChecked()
    const posted = page.waitForResponse(r => new URL(r.url()).pathname === sourcePath + '/publish' && r.request().method() === 'POST')
    await page.getByRole('button', { name: '确认发布源码', exact: true }).click()
    assert.ok((await posted).ok())
    imported = await poll(context, sourcePath, value => ['Published', 'Failed'].includes(value.state))
  }
  assert.equal(imported.state, 'Published')
  state.imports.at(-1).view = imported
  await save()
  await page.setViewportSize({ width: 320, height: 800 })
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.goto(`${base}${route}/${id}/source`)
  await expect(page.getByRole('heading', { name: input.slug, exact: true })).toBeVisible()
  await expect(page.getByText(sourceId, { exact: true }).first()).toBeVisible()
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  await page.screenshot({ path: join(evidence, 'source-published-320.png') })
  for (const [runtime, task, label] of [['node', 'test', 'Node 测试'], ['dotnet', 'build', '.NET 构建'], ['dotnet', 'test', '.NET 测试']]) {
    phase = runtime + '-' + task + '-actual-task'
    let saved = state.runs.filter(r => r.runtime === runtime && r.task === task).at(-1)
    let run = saved ? await (await context.request.get(`${base}${api}/${id}/runs/${saved.id}`)).json() : null
    if (!run || (run.completedAtUtc && run.state !== 'Succeeded')) {
      await page.goto(`${base}${route}/${id}/runs`)
      await page.locator('.workspace-actions .el-select').click()
      await page.getByRole('option', { name: label, exact: true }).click()
      const posted = page.waitForResponse(r => new URL(r.url()).pathname === `${api}/${id}/runs` && r.request().method() === 'POST')
      await page.getByRole('button', { name: '提交运行', exact: true }).click()
      const response = await posted
      assert.equal(response.status(), 202)
      run = await response.json()
      saved = { id: run.runId, runtime, task, key: response.request().headers()['idempotency-key'] }
      state.runs.push(saved)
      await save()
    }
    run = await poll(context, `${api}/${id}/runs/${saved.id}`, value => !!value.completedAtUtc)
    saved.result = run
    await save()
    assert.equal(run.state, 'Succeeded')
    run = await poll(context, `${api}/${id}/runs/${saved.id}`, value => ['Available', 'ArtifactAttention'].includes(value.reportStatus))
    saved.result = run
    await save()
    assert.equal(run.reportStatus, 'Available')
    for (const format of ['json', 'markdown']) {
      const response = await context.request.get(`${base}${api}/${id}/runs/${saved.id}/report/${format}`)
      assert.equal(response.status(), 200)
      const bytes = await response.body()
      assert.equal(createHash('sha256').update(bytes).digest('hex'), response.headers()['x-content-sha256'])
      if (format === 'json') {
        const report = JSON.parse(bytes)
        assert.equal(report.outcome, 'Succeeded')
        assert.equal(report.exitCode, 0)
        if (task === 'test') {
          const tests = report.steps.flatMap(step => step.tests)
          assert.equal(tests.length, 1)
          assert.equal(tests[0].integrity, 'Matched')
          assert.equal(tests[0].result.state, 'Parsed')
          assert.deepEqual(tests[0].result.counts, { passed: 2, failed: 0, errors: 0, skipped: 0, other: 0, total: 2 })
        }
      }
      await writeFile(join(evidence, `${runtime}-${task}-report.${format === 'json' ? 'json' : 'md'}`), bytes, { mode: 0o600 })
    }
    await page.goto(`${base}${route}/${id}/reports/${saved.id}`)
    await expect(page.getByRole('heading', { name: '运行报告', exact: true })).toBeVisible()
    await expect(page.getByText('运行结论', { exact: true })).toBeVisible()
    await page.screenshot({ path: join(evidence, `${runtime}-${task}-report.png`) })
  }
  phase = 'actual-idle-lifecycle-preserves-code-and-quota'
  await installedLifecycle({ page, context, base, api, id, route, state, save, headers, poll, evidence })
  phase = 'actual-administrator-manages-deleted-owner-workspaces'
  state.retainedLifecycle ??= {}
  for (const retainedId of input.retirementProbe ? [] : ['c23526c5-fc43-4944-9f27-75ac540f6df0', 'df31e4e1-8a7e-4ffe-9bef-246ce9b6f218']) {
    state.retainedLifecycle[retainedId] ??= {}
    const retained = await (await context.request.get(`${base}${api}/${retainedId}`)).json()
    assert.notEqual(retained.ownerSubject, state.ownerSubject)
    await installedLifecycle({ page, context, base, api, id: retainedId, route,
      state: { lifecycle: state.retainedLifecycle[retainedId] }, save, headers, poll, evidence,
      manageOnly: true })
  }
  }
  const id = state.workspace.id
  if (input.retirementProbe) {
    assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_BUSINESS_PROBE, '1')
    phase = 'real-archive-retirement-budget-reuse'
    state.retirementStarted = true
    state.legacyLifecycleSkipped = true
    await save()
    await installedRetirementBusiness({ page, context, base, api, id, route, state, save, headers, poll, evidence })
  }
  phase = 'ordinary-owner-boundary'
  const other = await browser.newContext({ viewport: { width: 320, height: 800 } }), ordinary = await other.newPage()
  await loginVisible(ordinary, other, base, '/', false, input.accounts[1])
  assert.equal((await other.request.get(`${base}${api}/${id}`)).status(), 404)
  assert.equal((await other.request.get(`${base}${api}/${id}/runs/${state.runs[0].id}/report/json`)).status(), 403)
  assert.equal((await other.request.get(`${base}${api}/${id}/lifecycle/plan?action=stop`)).status(), 404)
  assert.equal((await other.request.get(`${base}${api}/${id}/lifecycle/${state.lifecycle.stop.operationId}`)).status(), 404)
  if (input.retirementProbe) {
    assert.equal((await other.request.get(`${base}${api}/${id}/retirements`)).status(), 403)
    if (process.env.WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE === '1') {
      const recovered = state.retirementBusiness.retirement
      assert.equal((await other.request.get(`${base}${api}/${id}/retirements/${recovered.retirementId}/attempts`)).status(), 403)
      const denied = await other.request.post(`${base}${api}/${id}/retirements/${recovered.retirementId}/recover`, {
        headers: { ...await headers(other), 'Idempotency-Key': randomUUID() },
        data: state.retirementBusiness.recovery.command,
      })
      assert.equal(denied.status(), 403)
      state.retirementRecoveryOrdinaryDenied = true
    }
    state.retirementOrdinaryDenied = true
  }
  const ordinaryLogout = await logoutFromUserMenu(ordinary, other, base)
  await other.close()
  phase = 'owner-normal-logout'
  const ownerLogout = await logoutFromUserMenu(page, context, base)
  state.passed = true
  state.retainedForProductDecommission = !input.retirementProbe
  state.logouts = { owner: ownerLogout, ordinary: ordinaryLogout }
  await save()
  await browser.close()
  browser = undefined
  console.log(JSON.stringify({ installedRuntime: 'passed', workspaceId: id, retainedForProductDecommission: state.retainedForProductDecommission }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => ['validate_development_installed_runtime.mjs:', 'installed_development_lifecycle.mjs:', 'installed_workspace_retirement.mjs:', 'installed_workspace_retirement_recovery.mjs:'].some(file => line.includes(file)))?.trim()
  if (state && evidence) {
    state.failureHistory ??= state.lastFailure ? [state.lastFailure] : []
    state.lastFailure = { phase, failureType: error?.name, frame,
      ...(typeof error?.actual === 'number' ? { actual: error.actual } : {}),
      ...(typeof error?.expected === 'number' ? { expected: error.expected } : {}),
    }
    state.failureHistory.push(state.lastFailure)
    await save().catch(() => {})
  }
  if (page && evidence && page.url().startsWith(base + route))
    await page.screenshot({ path: join(evidence, `attempt-${state.browserAttempts}-failed-page.png`) }).catch(() => {})
  console.error(JSON.stringify({ installedRuntime: 'failed', phase, failureType: error?.name }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
