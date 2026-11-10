// Installed identity/UI lifecycle plus a trusted installed Agent plan probe.
// No intercepted responses, new workspace, HDD capture or source retirement.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { spawn } from 'node:child_process'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { installedLifecycle } from './installed_development_lifecycle.mjs'

const base = 'http://127.0.0.1:8080', route = '/development/workspaces', api = '/api/v1' + route
const id = 'c23526c5-fc43-4944-9f27-75ac540f6df0'
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
  const deadline = Date.now() + 240_000
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
  throw new Error('INSTALLED_ARCHIVE_LIFECYCLE_DEADLINE')
}
async function plan(storage, expectedEpoch, expectedStatus) {
  const probe = join(evidence, 'probe', 'DevelopmentArchiveRpcProbe.dll')
  const request = { taskId: randomUUID(), archiveId: randomUUID(), storage: { workspaceId: id,
    rootId: storage.rootId, environmentSha256: storage.environmentSha256, diskMiB: storage.diskMiB },
    expectedEpoch, expectedStatus }
  state.planRequests ??= []
  state.planRequests.push(request)
  await save()
  const result = await new Promise((resolve, reject) => {
    const process = spawn('/home/user/.local/share/whaledeck-toolchains/dotnet-10.0.402/dotnet', [probe, '--installed-plan'], {
      env: { ...globalThis.process.env, WHALEDECK_INSTALLED_ARCHIVE_PLAN_PROBE: '1' }, stdio: ['pipe', 'pipe', 'pipe'] })
    let stdout = '', stderr = ''
    const timeout = setTimeout(() => { process.kill('SIGTERM'); reject(new Error('INSTALLED_ARCHIVE_PROBE_DEADLINE')) }, 210_000)
    for (const [stream, append] of [[process.stdout, value => { stdout += value }], [process.stderr, value => { stderr += value }]]) {
      stream.on('data', bytes => {
        append(bytes.toString())
        if (stdout.length + stderr.length > 32_768) process.kill('SIGTERM')
      })
    }
    process.once('error', error => { clearTimeout(timeout); reject(error) })
    process.once('close', code => { clearTimeout(timeout); resolve({ code, stdout, stderr }) })
    process.stdin.on('error', () => {})
    process.stdin.end(JSON.stringify(request))
  })
  await writeFile(join(evidence, `probe-${request.taskId}.json`), JSON.stringify(result, null, 2), { mode: 0o600 })
  assert.equal(result.code, 0)
  const receipt = JSON.parse(result.stdout)
  assert.equal(receipt.passed, true)
  assert.equal(receipt.Status, expectedStatus)
  return receipt
}
try {
  assert.equal(process.env.WHALEDECK_INSTALLED_ARCHIVE_PLAN_PROBE, '1')
  const input = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  evidence = input.evidence
  state = { workspaceId: id, revision: input.revision, lifecycle: {} }
  await save()
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  context = await browser.newContext({ viewport: { width: 320, height: 900 }, colorScheme: 'dark', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000)
  page = await context.newPage()
  phase = 'real-admin-login'
  const profile = await loginVisible(page, context, base, route, true, input.account)
  assert.equal(typeof profile.subject, 'string')
  const statusResponse = await context.request.get(base + '/api/v1/system/status')
  assert.equal(statusResponse.status(), 200)
  assert.equal((await statusResponse.json()).build.sourceRevision, input.revision)
  const currentResponse = await context.request.get(`${base}${api}/${id}`)
  assert.equal(currentResponse.status(), 200)
  const current = await currentResponse.json()
  assert.equal(current.id, id)
  assert.equal(current.state, 'StorageAllocated')
  assert.equal(current.lifecycleState, 'Active')
  phase = 'active-archive-plan-rejected'
  const selected = input.storage
  state.activePlan = await plan(selected, current.lifecycleVersion + 1, 'Rejected')
  phase = 'real-ui-stop-plan-resume'
  await installedLifecycle({ page, context, base, api, id, route, state, save, headers, poll, evidence, manageOnly: true,
    afterStopped: async stopped => {
      try { return await plan(stopped.storageProof, stopped.marker.epoch, 'Succeeded') }
      catch (error) {
        // A read-only plan failure still uses the product resume path. Keep
        // its original task/evidence and never disguise the failure as passed.
        return { passed: false, failureType: error.name }
      }
    } })
  phase = 'real-ui-logout'
  state.logout = await logoutFromUserMenu(page, context, base, null)
  assert.equal(state.lifecycle.stoppedCheck.passed, true)
  state.passed = true
  state.noHddCapture = true
  await save()
  console.log(JSON.stringify({ passed: true, workspaceId: id, realIdentityLifecycle: true, installedAgentPlan: true }))
} catch (error) {
  state.failureType = error.name
  state.passed = false
  if (context && page) {
    try {
      const endpoint = `${api}/${id}/lifecycle`
      const original = state.lifecycle?.stop
      if (original?.key && !original.completedAtUtc) {
        if (!original.operationId) {
          const replay = await context.request.post(base + endpoint, {
            headers: { ...await headers(context), 'Idempotency-Key': original.key }, data: original.command })
          assert.equal(replay.status(), 202)
          Object.assign(original, await replay.json())
          await save()
        }
        const finished = await poll(context, `${endpoint}/${original.operationId}`, async value => {
          if (!value.completedAtUtc && value.code === 'WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED') {
            const renewed = await context.request.post(`${base}${endpoint}/${original.operationId}/authorize`, { headers: await headers(context) })
            assert.ok([202, 409].includes(renewed.status()))
          }
          return !!value.completedAtUtc
        })
        Object.assign(original, finished)
      }
      const selected = await context.request.get(`${base}${api}/${id}`)
      assert.equal(selected.status(), 200)
      const current = await selected.json()
      if (current.lifecycleState === 'Stopped') {
        assert.ok(original?.operationId && original.completedAtUtc && original.state === 'Succeeded' && original.phase === 'Stopped')
        assert.equal(current.lifecycleVersion, state.lifecycle.expectedEpochs.stopped)
        state.recovery = { lifecycle: {} }
        await installedLifecycle({ page, context, base, api, id, route, state: state.recovery,
          save, headers, poll, evidence, manageOnly: true, resumeOnly: true })
        state.recovery.active = true
      } else {
        assert.equal(current.lifecycleState, 'Active')
        state.recovery = { active: true }
      }
      state.recovery.logout = await logoutFromUserMenu(page, context, base, null)
    } catch (recoveryError) {
      state.recovery ??= {}
      state.recovery.failureType = recoveryError.name
    }
  }
  if (evidence) await save()
  console.error(JSON.stringify({ passed: false, phase, failureType: error.name }))
  process.exitCode = 1
} finally { if (browser) await browser.close() }
