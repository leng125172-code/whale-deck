// Real BFF/identity/SQL/Worker/Agent. Only a genuine Begin response is dropped.
import assert from 'node:assert/strict'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { installedLifecycle } from './installed_development_lifecycle.mjs'

const base = 'http://127.0.0.1:8080', route = '/development/workspaces', api = '/api/v1' + route
const id = 'c23526c5-fc43-4944-9f27-75ac540f6df0', endpoint = `${api}/${id}/archives`
let browser, context, page, evidence, phase = 'startup', state = {}
const contexts = []
const origins = new Map()
async function save() {
  state.phase = phase
  await writeFile(join(evidence, 'state.pending.json'), JSON.stringify(state, null, 2), { mode: 0o600 })
  await rename(join(evidence, 'state.pending.json'), join(evidence, 'state.json'))
}
async function control(name, timeout = 180_000) {
  const deadline = Date.now() + timeout
  while (Date.now() < deadline) {
    try { if (name !== 'recovery-worker-running.json') { await readFile(join(evidence, 'controller-abort.json')); throw new Error('ARCHIVE_CONTROLLER_ABORTED') } }
    catch (error) { if (error.code !== 'ENOENT') throw error }
    try { return JSON.parse(await readFile(join(evidence, name), 'utf8')) }
    catch (error) { if (error.code !== 'ENOENT') throw error }
    await new Promise(resolve => setTimeout(resolve, 80))
  }
  throw new Error('ARCHIVE_CONTROL_DEADLINE')
}
async function headers(selected = context) {
  const response = await selected.request.get(base + '/api/v1/auth/csrf')
  assert.equal(response.status(), 200)
  const csrf = await response.json()
  return { Origin: base, 'Content-Type': 'application/json', [csrf.headerName]: csrf.token }
}
async function poll(selected, path, predicate) {
  const deadline = Date.now() + 300_000
  while (Date.now() < deadline) {
    const response = await selected.request.get(base + path)
    if (response.status() === 429) {
      const value = Number(response.headers()['retry-after'] ?? 60)
      await new Promise(resolve => setTimeout(resolve, Math.min(60, Math.max(1, Number.isFinite(value) ? value : 60)) * 1000))
      continue
    }
    assert.equal(response.status(), 200)
    const value = await response.json()
    if (await predicate(value)) return value
    await new Promise(resolve => setTimeout(resolve, 3000))
  }
  throw new Error('ARCHIVE_FAULT_OBSERVATION_DEADLINE')
}
const cardFor = selected => selected.getByRole('region', { name: '开发空间 HDD 归档' })
async function refresh(selected = page) {
  const card = cardFor(selected)
  await card.getByRole('button', { name: '刷新归档状态', exact: true }).click()
  return card
}
async function fence() {
  const response = await context.request.get(`${base}${api}/${id}/lifecycle/plan?action=resume`)
  assert.equal(response.status(), 409)
  assert.equal((await response.json()).code, 'WORKSPACE_ARCHIVE_NOT_SETTLED')
}
async function begin(selected, selectedContext, kind, lostReply = false) {
  const card = cardFor(selected)
  let intent
  let complete, reject
  const observed = new Promise((resolve, fail) => { complete = resolve; reject = fail })
  const handled = observed.then(value => ({ value }), error => ({ error }))
  const matches = url => url.origin === base && url.pathname === endpoint
  const intercept = async routed => {
    try {
      const request = routed.request()
      if (request.method() !== 'POST') { await routed.continue(); return }
      assert.equal(request.method(), 'POST')
      assert.equal(intent, undefined)
      intent = { kind, key: request.headers()['idempotency-key'], command: request.postDataJSON() }
      origins.set(intent.key, selectedContext)
      assert.match(intent.key, /^[a-f0-9-]{36}$/)
      assert.equal(intent.command.expectedEpoch, state.lifecycle.expectedEpochs.stopped)
      state.archives.push(intent); await save()
      if (lostReply) {
        // Forward the genuine request, require an actual server acceptance,
        // then discard that response. No API response is fabricated.
        const upstream = await routed.fetch({ maxRetries: 0 })
        assert.equal(upstream.status(), 202)
        const actual = await upstream.json()
        intent.transportFault = { kind: 'server-accepted-response-dropped', serverArchiveId: actual.archiveId }
        await save(); await upstream.dispose(); await routed.abort('connectionreset')
      } else await routed.continue()
      complete(true)
    } catch (error) { reject(error); await routed.abort().catch(() => {}) }
  }
  await selected.route(matches, intercept)
  try {
    const posted = lostReply ? null : selected.waitForResponse(r =>
      new URL(r.url()).pathname === endpoint && r.request().method() === 'POST').then(
        response => ({ response }), error => ({ error }))
    await card.getByRole('button', { name: '核对 HDD 归档计划', exact: true }).click()
    const proof = await handled
    if (proof.error) throw proof.error
    if (posted) {
      const result = await posted
      if (result.error) throw result.error
      assert.equal(result.response.status(), 202)
      Object.assign(intent, await result.response.json())
    } else {
      // The product's automatic current lookup, rather than another Begin,
      // must recover the original intent after losing the response.
      const current = await poll(selectedContext, endpoint, value =>
        value?.archiveId === intent.transportFault.serverArchiveId)
      Object.assign(intent, current)
      await expect(card.getByText(intent.archiveId, { exact: true })).toBeVisible({ timeout: 15_000 })
    }
    await save()
  } finally { await selected.unroute(matches, intercept) }
  return intent
}
async function ready(intent, selected = page, selectedContext = context) {
  Object.assign(intent, await poll(selectedContext, endpoint + '/' + intent.archiveId, value => value.awaitingConfirmation))
  await save(); const card = await refresh(selected)
  await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible()
  await expect(card.getByRole('button', { name: '确认 HDD 归档', exact: true })).toBeDisabled()
  await fence()
  return card
}
async function cancel(intent) {
  await page.goto(`${base}${route}/${id}/environment`)
  const card = await refresh()
  await expect(card.getByText(intent.archiveId, { exact: true })).toBeVisible()
  const posted = page.waitForResponse(r => new URL(r.url()).pathname === `${endpoint}/${intent.archiveId}/cancel`)
  await card.getByRole('button', { name: '取消归档', exact: true }).click()
  assert.equal((await posted).status(), 202)
  Object.assign(intent, await poll(context, endpoint + '/' + intent.archiveId, value => !!value.completedAtUtc))
  assert.equal(intent.state, 'Canceled')
  assert.equal(intent.captureJson, null); assert.equal(intent.verifyJson, null)
  await save()
}
async function victim(input, kind) {
  const selectedContext = await browser.newContext({ viewport: { width: 320, height: 900 }, reducedMotion: 'reduce', colorScheme: 'dark' })
  contexts.push(selectedContext); selectedContext.setDefaultTimeout(30_000)
  const selected = await selectedContext.newPage()
  const profile = await loginVisible(selected, selectedContext, base, route, true, input.accounts[kind])
  await selected.goto(`${base}${route}/${id}/environment`)
  await expect(cardFor(selected).getByRole('button', { name: '核对 HDD 归档计划', exact: true })).toBeEnabled()
  const oldHeaders = await headers(selectedContext)
  await writeSignal(evidence, kind + '-ready.json', { subject: profile.subject })
  await control(kind + '-worker-held.json')
  const intent = await begin(selected, selectedContext, kind)
  await writeSignal(evidence, kind + '-queued.json', { archiveId: intent.archiveId, key: intent.key, command: intent.command })
  const revoked = await control(kind + '-revocation-observed.json')
  assert.equal(revoked.archiveId, intent.archiveId)
  const denied = []
  for (const action of ['get', 'confirm', 'authorize', 'cancel']) {
    const response = action === 'get'
      ? await selectedContext.request.get(`${base}${endpoint}/${intent.archiveId}`)
      : await selectedContext.request.post(`${base}${endpoint}/${intent.archiveId}/${action}`, {
        headers: oldHeaders, data: action === 'confirm' ? { planSha256: 'a'.repeat(64) } : undefined })
    assert.ok([401, 403].includes(response.status()))
    denied.push({ action, status: response.status() })
  }
  await refresh(selected)
  await expect(cardFor(selected).getByText('登录或权限已变化，已停止自动刷新。请重新登录或恢复权限后刷新归档状态。', { exact: true })).toBeVisible()
  await expect(cardFor(selected).getByText(intent.archiveId, { exact: true })).toHaveCount(0)
  let automaticReads = 0
  const observe = request => { if (new URL(request.url()).pathname.startsWith(endpoint)) automaticReads++ }
  selected.on('request', observe)
  await selected.waitForTimeout(6500)
  selected.off('request', observe); assert.equal(automaticReads, 0)
  intent.revokedActorDenied = denied; intent.uiPollingStopped = true; await save()
  await page.goto(`${base}${route}/${id}/environment`)
  const card = await refresh()
  await expect(card.getByRole('button', { name: '重新授权归档', exact: true })).toBeVisible()
  const reauthorized = page.waitForResponse(r => new URL(r.url()).pathname === `${endpoint}/${intent.archiveId}/authorize`)
  await card.getByRole('button', { name: '重新授权归档', exact: true }).click()
  assert.equal((await reauthorized).status(), 202)
  await ready(intent)
  await writeSignal(evidence, kind + '-reauthorized.json', { archiveId: intent.archiveId })
  const confirmed = await control(kind + '-same-task.json')
  assert.equal(confirmed.sameTaskAndCanonicalRequest, true)
  intent.sameTaskAndCanonicalRequest = true; await save()
  if (kind === 'group-removal') {
    const confirmed = page.waitForResponse(r => new URL(r.url()).pathname === `${endpoint}/${intent.archiveId}/confirm`)
    await card.locator('label.el-checkbox').click()
    await card.getByRole('button', { name: '确认 HDD 归档', exact: true }).click()
    assert.equal((await confirmed).status(), 202)
    await page.reload()
    Object.assign(intent, await poll(context, endpoint + '/' + intent.archiveId, value => !!value.completedAtUtc))
    assert.equal(intent.state, 'Succeeded'); assert.equal(intent.phase, 'Archived')
    const capture = JSON.parse(intent.captureJson), verify = JSON.parse(intent.verifyJson)
    assert.equal(capture.operationId, intent.archiveId); assert.deepEqual(capture, verify)
    await refresh()
    await expect(cardFor(page).getByText(verify.receiptSha256, { exact: true })).toBeVisible()
    await page.screenshot({ path: join(evidence, 'reauthorized-archive-320.png'), fullPage: true })
    await save()
  } else await cancel(intent)
  await selectedContext.close(); contexts.splice(contexts.indexOf(selectedContext), 1)
}
try {
  assert.equal(process.env.WHALEDECK_INSTALLED_ARCHIVE_FAULTS_PROBE, '1')
  const input = JSON.parse(await readFile('/dev/stdin', 'utf8')); evidence = input.evidence
  state = { workspaceId: id, revision: input.revision, lifecycle: {}, archives: [] }; await save()
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome-stable', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 900 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000); page = await context.newPage()
  phase = 'real-recovery-admin-login'
  const profile = await loginVisible(page, context, base, route, true, input.accounts.recovery)
  state.recoverySubject = profile.subject
  const system = await context.request.get(base + '/api/v1/system/status')
  assert.equal(system.status(), 200); assert.equal((await system.json()).build.sourceRevision, input.revision)
  await installedLifecycle({ page, context, base, api, id, route, state, save, headers, poll, evidence, manageOnly: true,
    afterStopped: async () => {
      phase = 'actual-begin-response-loss'
      const first = await begin(page, context, 'lost-response-and-worker-restart', true)
      await ready(first)
      await writeSignal(evidence, 'restart-ready.json', { archiveId: first.archiveId })
      await control('worker-restarted.json')
      await page.reload(); await ready(first)
      first.workerRestartRecovered = true; await save(); await cancel(first)
      for (const kind of ['group-removal', 'account-disable']) { phase = kind; await save(); await victim(input, kind) }
      return { passed: true, lostResponseRecovered: true, workerRestartRecovered: true, revokedIdentities: 2 }
    } })
  phase = 'real-recovery-admin-logout'
  state.logout = await logoutFromUserMenu(page, context, base, null)
  state.passed = true; await save()
  console.log(JSON.stringify({ passed: true, realArchiveFaultsAccepted: 3 }))
} catch (error) {
  state.passed = false; state.failureType = error.name
  if (evidence) { await save(); await writeSignal(evidence, 'browser-failed.json', { failureType: error.name }) }
  try {
    // The controller first restores the same Worker. Recovery touches only
    // this run's recorded intents and uses the still-authorized admin.
    await control('recovery-worker-running.json', 120_000)
    for (const intent of state.archives ?? []) {
      if (!intent.archiveId) {
        const originalContext = origins.get(intent.key)
        assert.ok(originalContext)
        const replay = await originalContext.request.post(base + endpoint, { headers: { ...await headers(originalContext), 'Idempotency-Key': intent.key }, data: intent.command })
        assert.equal(replay.status(), 202); Object.assign(intent, await replay.json()); await save()
      }
      const current = await context.request.get(`${base}${endpoint}/${intent.archiveId}`)
      assert.equal(current.status(), 200)
      if (!(await current.json()).completedAtUtc) {
        const response = await context.request.post(`${base}${endpoint}/${intent.archiveId}/cancel`, { headers: await headers() })
        assert.equal(response.status(), 202)
        intent.recovery = await poll(context, endpoint + '/' + intent.archiveId, value => !!value.completedAtUtc)
      }
    }
    const selected = await context.request.get(`${base}${api}/${id}`); assert.equal(selected.status(), 200)
    const current = await selected.json(); state.recovery = { lifecycle: {} }
    if (current.lifecycleState === 'Stopped') {
      assert.equal(current.lifecycleVersion, state.lifecycle.expectedEpochs.stopped)
      await installedLifecycle({ page, context, base, api, id, route, state: state.recovery, save, headers, poll, evidence, manageOnly: true, resumeOnly: true })
    } else assert.equal(current.lifecycleState, 'Active')
    state.recovery.active = true
    state.recovery.logout = await logoutFromUserMenu(page, context, base, null)
  } catch (recoveryError) { state.recovery ??= {}; state.recovery.failureType = recoveryError.name }
  if (evidence) await save()
  console.error(JSON.stringify({ passed: false, phase, failureType: error.name })); process.exitCode = 1
} finally { if (browser) await browser.close() }
