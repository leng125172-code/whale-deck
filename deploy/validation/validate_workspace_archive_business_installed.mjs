// Real installed UI/BFF/PostgreSQL/Worker/Agent/HDD; no response substitutions.
import assert from 'node:assert/strict'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { installedLifecycle } from './installed_development_lifecycle.mjs'

const base = 'http://127.0.0.1:8080', route = '/development/workspaces', api = '/api/v1' + route
const id = 'c23526c5-fc43-4944-9f27-75ac540f6df0', endpoint = `${api}/${id}/archives`
let browser, context, page, evidence, state = {}, phase = 'startup'
async function save() {
  state.phase = phase
  await writeFile(join(evidence, 'state.pending.json'), JSON.stringify(state, null, 2), { mode: 0o600 })
  await rename(join(evidence, 'state.pending.json'), join(evidence, 'state.json'))
}
async function headers(context) {
  const response = await context.request.get(base + '/api/v1/auth/csrf')
  assert.equal(response.status(), 200)
  const csrf = await response.json()
  return { Origin: base, 'Content-Type': 'application/json', [csrf.headerName]: csrf.token }
}
async function poll(context, path, predicate) {
  const deadline = Date.now() + 300_000
  while (Date.now() < deadline) {
    const response = await context.request.get(base + path)
    if (response.status() === 429) {
      const seconds = Number(response.headers()['retry-after'] ?? 60)
      await new Promise(resolve => setTimeout(resolve, Math.min(60, Math.max(1, Number.isFinite(seconds) ? seconds : 60)) * 1000))
      continue
    }
    assert.equal(response.status(), 200)
    const value = await response.json()
    if (await predicate(value)) return value
    await new Promise(resolve => setTimeout(resolve, 3000))
  }
  throw new Error('INSTALLED_ARCHIVE_BUSINESS_DEADLINE')
}
async function resumeFence() {
  const result = await context.request.get(`${base}${api}/${id}/lifecycle/plan?action=resume`)
  assert.equal(result.status(), 409)
  assert.equal((await result.json()).code, 'WORKSPACE_ARCHIVE_NOT_SETTLED')
}
async function begin() {
  const card = page.getByRole('region', { name: '开发空间 HDD 归档' })
  const response = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await card.getByRole('button', { name: '核对 HDD 归档计划', exact: true }).click()
  const accepted = await response
  assert.equal(accepted.status(), 202)
  const key = accepted.request().headers()['idempotency-key'], command = accepted.request().postDataJSON()
  assert.match(key, /^[a-f0-9-]{36}$/)
  const view = await accepted.json()
  state.archives.push({ key, command, ...view })
  await save()
  const plan = await poll(context, endpoint + '/' + view.archiveId, value => {
    assert.equal(value.workspaceId, id)
    assert.equal(value.archiveId, view.archiveId)
    assert.equal(value.retainsSource, true); assert.equal(value.reservesBudget, true)
    assert.equal(value.completedAtUtc, null)
    return value.awaitingConfirmation
  })
  Object.assign(state.archives.at(-1), plan)
  await save()
  const replay = await context.request.post(base + endpoint, { headers: { ...await headers(context), 'Idempotency-Key': key }, data: command })
  assert.equal(replay.status(), 202); assert.equal((await replay.json()).archiveId, view.archiveId)
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible()
  await expect(card.getByRole('button', { name: '确认 HDD 归档', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeDisabled()
  await resumeFence()
  return { card, archiveId: view.archiveId }
}
try {
  assert.equal(process.env.WHALEDECK_INSTALLED_ARCHIVE_BUSINESS_PROBE, '1')
  const input = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  evidence = input.evidence
  state = { workspaceId: id, revision: input.revision, lifecycle: {}, archives: [] }
  await save()
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome-stable', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 900 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000)
  page = await context.newPage()
  phase = 'real-admin-login'
  const profile = await loginVisible(page, context, base, route, true, input.account)
  assert.equal(typeof profile.subject, 'string')
  const statusResponse = await context.request.get(base + '/api/v1/system/status')
  assert.equal(statusResponse.status(), 200)
  assert.equal((await statusResponse.json()).build.sourceRevision, input.revision)
  phase = 'real-stop-archive-resume'
  await installedLifecycle({ page, context, base, api, id, route, state, save, headers, poll, evidence, manageOnly: true,
    afterStopped: async () => {
      phase = 'persistent-plan-cancel'
      const first = await begin()
      const canceledResponse = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + first.archiveId + '/cancel')
      await first.card.getByRole('button', { name: '取消归档', exact: true }).click()
      assert.equal((await canceledResponse).status(), 202)
      const canceled = await poll(context, endpoint + '/' + first.archiveId, value => !!value.completedAtUtc)
      assert.equal(canceled.state, 'Canceled')
      assert.equal(canceled.captureJson, null); assert.equal(canceled.verifyJson, null)
      Object.assign(state.archives[0], canceled); await save()
      await first.card.getByRole('button', { name: '刷新归档状态' }).click()
      phase = 'persistent-plan-confirm-capture-verify'
      const second = await begin()
      const confirmation = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/' + second.archiveId + '/confirm')
      await second.card.locator('label.el-checkbox').click()
      await second.card.getByRole('button', { name: '确认 HDD 归档', exact: true }).click()
      const confirmed = await confirmation
      assert.equal(confirmed.status(), 202)
      state.archives[1].confirmation = confirmed.request().postDataJSON()
      await save()
      await page.reload()
      const archived = await poll(context, endpoint + '/' + second.archiveId, value => {
        if (!value.completedAtUtc) assert.equal(value.cancelRequested, false)
        return !!value.completedAtUtc
      })
      assert.equal(archived.state, 'Succeeded'); assert.equal(archived.phase, 'Archived')
      assert.equal(archived.retainsSource, true); assert.equal(archived.reservesBudget, true)
      const capture = JSON.parse(archived.captureJson), verify = JSON.parse(archived.verifyJson)
      assert.equal(capture.operationId, second.archiveId)
      assert.equal(verify.operationId, second.archiveId)
      assert.equal(capture.receiptSha256, verify.receiptSha256)
      assert.equal(capture.archiveSha256, verify.archiveSha256)
      assert.equal(capture.archiveIndexSha256, verify.archiveIndexSha256)
      assert.ok(Number.isSafeInteger(verify.archiveBytes) && verify.archiveBytes > 0)
      Object.assign(state.archives[1], archived); await save()
      const card = page.getByRole('region', { name: '开发空间 HDD 归档' })
      await card.getByRole('button', { name: '刷新归档状态' }).click()
      await expect(card.getByText('HDD 副本已独立校验；原源码与资源预算继续保留。')).toBeVisible()
      await expect(card.getByText(verify.receiptSha256, { exact: true })).toBeVisible()
      for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
        await page.setViewportSize({ width, height: 900 }); await page.emulateMedia({ colorScheme })
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
        await page.screenshot({ path: join(evidence, `archive-${width}-${colorScheme}.png`), fullPage: true })
      }
      return { passed: true, canceledPlan: first.archiveId, verifiedArchive: second.archiveId }
    } })
  phase = 'real-ui-logout'
  state.logout = await logoutFromUserMenu(page, context, base, null)
  assert.equal(state.lifecycle.stoppedCheck.passed, true)
  state.passed = true
  await save()
  console.log(JSON.stringify({ passed: true, workspaceId: id, realIdentityPersistentArchive: true }))
} catch (error) {
  state.failureType = error.name; state.passed = false
  // Recover only this run's known operations via the supported product API.
  // Never guess a task ID, delete a source/HDD copy, clear SQL, or release quota.
  if (context && page) {
    try {
      for (const archive of state.archives ?? []) {
        const original = await context.request.get(`${base}${endpoint}/${archive.archiveId}`)
        assert.equal(original.status(), 200)
        if (!(await original.json()).completedAtUtc) {
          const stopped = await context.request.post(`${base}${endpoint}/${archive.archiveId}/cancel`, { headers: await headers(context) })
          assert.equal(stopped.status(), 202)
          archive.recovery = await poll(context, `${endpoint}/${archive.archiveId}`, value => !!value.completedAtUtc)
        }
      }
      const stop = state.lifecycle?.stop
      assert.ok(stop?.operationId)
      if (!stop.completedAtUtc) Object.assign(stop, await poll(context, `${api}/${id}/lifecycle/${stop.operationId}`, value => !!value.completedAtUtc))
      const selected = await context.request.get(`${base}${api}/${id}`)
      assert.equal(selected.status(), 200)
      const current = await selected.json()
      state.recovery = { lifecycle: {} }
      if (current.lifecycleState === 'Stopped') {
        assert.equal(stop.state, 'Succeeded')
        assert.equal(current.lifecycleVersion, state.lifecycle.expectedEpochs.stopped)
        await installedLifecycle({ page, context, base, api, id, route, state: state.recovery,
          save, headers, poll, evidence, manageOnly: true, resumeOnly: true })
      } else assert.equal(current.lifecycleState, 'Active')
      state.recovery.active = true
      state.recovery.logout = await logoutFromUserMenu(page, context, base, null)
    } catch (recoveryError) {
      state.recovery ??= {}; state.recovery.failureType = recoveryError.name
    }
  }
  if (evidence) await save()
  console.error(JSON.stringify({ passed: false, phase, failureType: error.name }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
