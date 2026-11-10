// Actual installed BFF/UI/Worker/Agent and kernel quota. Responses are never mocked.
import assert from 'node:assert/strict'
import { createHash, randomUUID } from 'node:crypto'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
import { writeFile } from 'node:fs/promises'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

const execute = promisify(execFile)
const inspect = fileURLToPath(new URL('./inspect_development_workspace_lifecycle.py', import.meta.url))
async function physical(id) {
  const result = await execute('/usr/bin/python3', ['-B', inspect, id], { timeout: 30_000, maxBuffer: 16 * 1024 })
  return JSON.parse(result.stdout)
}

export async function installedLifecycle({ page, context, base, api, id, route, state, save, headers, poll, evidence, manageOnly = false }) {
  const prefix = `${api}/${id}`, endpoint = `${prefix}/lifecycle`
  state.lifecycle ??= {}
  const receipt = state.lifecycle
  async function current() {
    const response = await context.request.get(base + prefix)
    assert.equal(response.status(), 200)
    return response.json()
  }
  if (!receipt.expectedEpochs) {
    // Repeated installed acceptance must advance the actual monotonic epoch,
    // including retained spaces already stopped/resumed by an earlier batch.
    const baseline = receipt.before ? (receipt.before.marker?.epoch ?? 1) : (await current()).lifecycleVersion
    assert.ok(Number.isSafeInteger(baseline) && baseline >= 1)
    receipt.expectedEpochs = { stopped: baseline + 1, resumed: baseline + 2 }
    await save()
  }
  async function transition(action, expectedEpoch, target) {
    if (receipt[action]?.completedAtUtc) return
    const selected = await current()
    if (!receipt[action]?.operationId && receipt[action]?.key) {
      const replay = await context.request.post(base + endpoint, {
        headers: { ...await headers(context), 'Idempotency-Key': receipt[action].key }, data: receipt[action].command,
      })
      assert.equal(replay.status(), 202)
      Object.assign(receipt[action], await replay.json())
      await save()
    }
    if (!receipt[action]?.operationId) {
      assert.equal(selected.lifecycleState, action === 'stop' ? 'Active' : 'Stopped')
      // Reports can already be downloadable while raw-artifact retirement is
      // still pending. Wait for the real idle gate, never edit the database.
      const deadline = Date.now() + 180_000
      while (true) {
        const response = await context.request.get(`${base}${endpoint}/plan?action=${action}`)
        if (response.status() === 200) break
        assert.equal(response.status(), 409)
        assert.ok(['WORKSPACE_LIFECYCLE_NOT_IDLE', 'WORKSPACE_LIFECYCLE_GIT_DRAINING'].includes((await response.json()).code))
        assert.ok(Date.now() < deadline)
        await new Promise(resolve => setTimeout(resolve, 3000))
      }
      if (action === 'stop' && !receipt.before) {
        receipt.before = await physical(id)
        await save()
      }
      await page.goto(`${base}${route}/${id}/environment`)
      await page.getByRole('button', { name: action === 'stop' ? '核对停用计划' : '核对恢复计划', exact: true }).click()
      const button = page.getByRole('button', { name: action === 'stop' ? '确认停用空间' : '确认恢复空间', exact: true })
      await expect(button).toBeVisible()
      await expect(button).toBeDisabled()
      const checkbox = page.locator('label.el-checkbox').filter({ hasText: '我已核对计划，确认保留源码与配额' })
      await checkbox.click()
      const matches = url => url.origin === base && url.pathname === endpoint
      const observe = async route => {
        const request = route.request()
        assert.equal(request.method(), 'POST')
        receipt[action] = { key: request.headers()['idempotency-key'], command: request.postDataJSON() }
        assert.equal(receipt[action].command.action, action)
        assert.match(receipt[action].key, /^[a-f0-9-]{36}$/)
        // Persist only public plan/key fields before forwarding the genuine UI
        // request. No response is mocked and no credentials enter evidence.
        await save()
        await route.continue()
      }
      await page.route(matches, observe)
      try {
        const posted = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
        await button.click()
        const response = await posted
        assert.equal(response.status(), 202)
        Object.assign(receipt[action], await response.json())
      } finally {
        await page.unroute(matches, observe)
      }
      await save()
    }
    const operation = receipt[action]
    assert.ok(operation?.operationId)
    const finished = await poll(context, `${endpoint}/${operation.operationId}`, async value => {
      if (!value.completedAtUtc && value.code === 'WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED') {
        const response = await context.request.post(`${base}${endpoint}/${operation.operationId}/authorize`, { headers: await headers(context) })
        assert.ok([202, 409].includes(response.status()))
        receipt.reauthorizations = (receipt.reauthorizations ?? 0) + 1
        await save()
      }
      return !!value.completedAtUtc
    })
    assert.equal(finished.state, 'Succeeded')
    assert.equal(finished.phase, target)
    Object.assign(operation, finished)
    const updated = await current()
    assert.equal(updated.lifecycleState, target)
    assert.equal(updated.lifecycleVersion, expectedEpoch)
    await page.goto(`${base}${route}/${id}/environment`)
    await expect(page.locator('.workspace-lifecycle').getByRole('status')).toHaveText(target === 'Stopped' ? '已停用' : '可用')
    await page.screenshot({ path: join(evidence, `lifecycle-${id}-${action}-320.png`) })
    const replay = await context.request.post(base + endpoint, {
      headers: { ...await headers(context), 'Idempotency-Key': operation.key }, data: operation.command,
    })
    assert.equal(replay.status(), 202)
    assert.equal((await replay.json()).operationId, operation.operationId)
    await save()
  }
  await transition('stop', receipt.expectedEpochs.stopped, 'Stopped')
  assert.ok(receipt.before)
  receipt.stopped ??= await physical(id)
  assert.deepEqual({ ...receipt.stopped, marker: null }, { ...receipt.before, marker: null })
  assert.equal(receipt.stopped.marker.state, 'Stopped')
  assert.equal(receipt.stopped.marker.epoch, receipt.expectedEpochs.stopped)
  assert.equal(receipt.stopped.marker.operationId, receipt.stop.operationId)
  await save()
  if (!manageOnly && !receipt.resume?.completedAtUtc) {
    const key = randomUUID()
    const denied = await context.request.post(`${base}${prefix}/runs`, {
      headers: { ...await headers(context), 'Idempotency-Key': key }, data: { runtime: 'node', task: 'test' },
    })
    assert.equal(denied.status(), 409)
    assert.equal((await denied.json()).code, 'WORKSPACE_LIFECYCLE_INACTIVE')
    const source = state.imports.at(-1)
    const sourceInput = source.view.archive
    const deniedSource = await context.request.put(`${base}${prefix}/source-imports/${randomUUID()}`, {
      headers: await headers(context), data: sourceInput,
    })
    assert.equal(deniedSource.status(), 409)
    const sourceReplay = await context.request.put(`${base}${prefix}/source-imports/${source.id}`, {
      headers: await headers(context), data: sourceInput,
    })
    const sourceReplayView = await sourceReplay.json()
    receipt.sourceReplay = { status: sourceReplay.status(), view: sourceReplayView }
    await save()
    // Begin returns AcceptedAtAction for both new intents and exact replays.
    assert.equal(sourceReplay.status(), 202)
    assert.equal(sourceReplayView.state, 'Published')
    const run = state.runs[0]
    const replay = await context.request.post(`${base}${prefix}/runs`, {
      headers: { ...await headers(context), 'Idempotency-Key': run.key }, data: { runtime: run.runtime, task: run.task },
    })
    assert.equal(replay.status(), 202)
    assert.equal((await replay.json()).runId, run.id)
    const untrusted = { action: 'resume', planSha256: 'a'.repeat(64) }
    assert.equal((await context.request.post(base + endpoint, {
      headers: { Origin: base, 'Idempotency-Key': randomUUID() }, data: untrusted,
    })).status(), 400)
    assert.equal((await context.request.post(base + endpoint, {
      headers: { ...await headers(context), Origin: 'http://untrusted.invalid', 'Idempotency-Key': randomUUID() }, data: untrusted,
    })).status(), 403)
    await page.goto(`${base}${route}/${id}/runs`)
    await expect(page.getByText('空间已停用或正在切换状态，恢复后可提交新任务。')).toBeVisible()
    await expect(page.getByRole('button', { name: '提交运行', exact: true })).toHaveCount(0)
    receipt.stoppedAdmissionsDenied = true
    receipt.originalReadOnlyReplaysPassed = true
    receipt.csrfOriginDenied = true
    await save()
  }
  await transition('resume', receipt.expectedEpochs.resumed, 'Active')
  receipt.resumed ??= await physical(id)
  assert.deepEqual({ ...receipt.resumed, marker: null }, { ...receipt.before, marker: null })
  assert.equal(receipt.resumed.marker.state, 'Active')
  assert.equal(receipt.resumed.marker.epoch, receipt.expectedEpochs.resumed)
  assert.equal(receipt.resumed.marker.operationId, receipt.resume.operationId)
  if (manageOnly) {
    receipt.passed = true
    await save()
    return
  }
  await page.goto(`${base}${route}/${id}/runs`)
  await expect(page.getByRole('button', { name: '提交运行', exact: true })).toBeEnabled()
  if (!receipt.resumedRun) {
    await page.locator('.workspace-actions .el-select').click()
    await page.getByRole('option', { name: 'Node 测试', exact: true }).click()
    const posted = page.waitForResponse(r => new URL(r.url()).pathname === `${prefix}/runs` && r.request().method() === 'POST')
    await page.getByRole('button', { name: '提交运行', exact: true }).click()
    const response = await posted
    assert.equal(response.status(), 202)
    const run = await response.json()
    receipt.resumedRun = { id: run.runId, runtime: 'node', task: 'test', key: response.request().headers()['idempotency-key'] }
    state.runs.push(receipt.resumedRun)
    await save()
  }
  const run = await poll(context, `${prefix}/runs/${receipt.resumedRun.id}`,
    value => !!value.completedAtUtc && ['Available', 'ArtifactAttention'].includes(value.reportStatus))
  assert.equal(run.state, 'Succeeded')
  assert.equal(run.reportStatus, 'Available')
  receipt.resumedRun.result = run
  for (const format of ['json', 'markdown']) {
    const response = await context.request.get(`${base}${prefix}/runs/${run.runId}/report/${format}`)
    assert.equal(response.status(), 200)
    const bytes = await response.body()
    assert.equal(createHash('sha256').update(bytes).digest('hex'), response.headers()['x-content-sha256'])
    if (format === 'json') {
      const report = JSON.parse(bytes)
      assert.equal(report.outcome, 'Succeeded')
      assert.equal(report.exitCode, 0)
      const tests = report.steps.flatMap(step => step.tests)
      assert.equal(tests.length, 1)
      assert.equal(tests[0].integrity, 'Matched')
      assert.deepEqual(tests[0].result.counts, { passed: 2, failed: 0, errors: 0, skipped: 0, other: 0, total: 2 })
    }
    await writeFile(join(evidence, `lifecycle-resumed-node-report.${format === 'json' ? 'json' : 'md'}`), bytes, { mode: 0o600 })
  }
  receipt.passed = true
  await save()
}
