// Explicit opt-in, new uniquely labelled QA spaces only. All writes go through
// the installed BFF; observers only forward genuine UI requests. No SQL writes.
import assert from 'node:assert/strict'
import { createHash, randomUUID } from 'node:crypto'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { installedLifecycle } from './installed_development_lifecycle.mjs'
import { holdRetirementSource, recoverRetirement, finishRetirementRecovery } from './installed_workspace_retirement_recovery.mjs'
import { armRetirementInterruption } from './installed_retirement_interruption.mjs'
import { armRetirementStop } from './installed_retirement_stop.mjs'

const execute = promisify(execFile)
const inspector = fileURLToPath(new URL('./inspect_development_workspace_retirement.py', import.meta.url))

export async function installedRetirementBusiness(args) {
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_BUSINESS_PROBE, '1')
  const { page, context, base, api, id, route, state, save, headers, poll, evidence } = args
  assert.match(state.slug, /^qa-native-[a-f0-9]{24}$/)
  assert.equal(state.workspace.id, id)
  async function get(path) {
    const response = await context.request.get(base + path)
    assert.equal(response.status(), 200)
    return response.json()
  }
  async function inspect(mode, workspaceId, archiveId, projectId, retirementId) {
    const result = await execute('/usr/bin/python3', ['-B', inspector, mode, workspaceId, archiveId, String(projectId), ...(retirementId ? [retirementId] : [])],
      { timeout: 30_000, maxBuffer: 2 * 1024**2 })
    return JSON.parse(result.stdout)
  }
  async function genuineBegin(card, endpoint, label, intent) {
    if (intent.key) {
      const replay = await context.request.post(base + endpoint, {
        headers: { ...await headers(context), 'Idempotency-Key': intent.key }, data: intent.command,
      })
      assert.equal(replay.status(), 202)
      Object.assign(intent, await replay.json())
      await save()
      return
    }
    const matches = url => url.origin === base && url.pathname === endpoint
    const observer = async route => {
      const request = route.request()
      if (request.method() !== 'POST') { await route.continue(); return }
      assert.equal(request.method(), 'POST')
      intent.key = request.headers()['idempotency-key']
      assert.match(intent.key, /^[a-f0-9-]{36}$/)
      intent.command = request.postDataJSON()
      await save()
      await route.continue()
    }
    await page.route(matches, observer)
    try {
      const response = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
      await card.getByRole('button', { name: label, exact: true }).click()
      const accepted = await response
      assert.equal(accepted.status(), 202)
      Object.assign(intent, await accepted.json())
      await save()
    } finally { await page.unroute(matches, observer) }
  }
  async function retire(workspace, record) {
    assert.equal(workspace.ownerSubject, state.ownerSubject)
    assert.ok(workspace.slug === state.slug || workspace.slug === state.slug + '-reuse')
    const prefix = `${api}/${workspace.id}`
    if (!record.completedAtUtc) {
      await installedLifecycle({ ...args, id: workspace.id, state: record, manageOnly: true, stopOnly: true })
      await page.goto(`${base}${route}/${workspace.id}/environment`)
      const archiveCard = page.getByRole('region', { name: '开发空间 HDD 归档' })
      record.archive ??= {}
      if (!record.archive.completedAtUtc) {
        if (!record.archive.key) await expect(archiveCard.getByRole('button', { name: '核对 HDD 归档计划', exact: true })).toBeEnabled()
        await genuineBegin(archiveCard, prefix + '/archives', '核对 HDD 归档计划', record.archive)
        let archive = await poll(context, prefix + '/archives/' + record.archive.archiveId, value => value.awaitingConfirmation || !!value.completedAtUtc)
        if (!archive.completedAtUtc) {
          assert.equal(archive.awaitingConfirmation, true)
          await archiveCard.getByRole('button', { name: '刷新归档状态' }).click()
          await expect(archiveCard.getByRole('button', { name: '确认 HDD 归档', exact: true })).toBeDisabled()
          await archiveCard.locator('label.el-checkbox').click()
          const response = page.waitForResponse(r => new URL(r.url()).pathname === prefix + '/archives/' + archive.archiveId + '/confirm')
          await archiveCard.getByRole('button', { name: '确认 HDD 归档', exact: true }).click()
          assert.equal((await response).status(), 202)
          archive = await poll(context, prefix + '/archives/' + archive.archiveId, value => !!value.completedAtUtc)
        }
        assert.equal(archive.state, 'Succeeded')
        assert.equal(archive.phase, 'Archived')
        assert.equal(archive.retainsSource, true)
        assert.equal(archive.reservesBudget, true)
        Object.assign(record.archive, archive)
        await save()
      }
      const captured = JSON.parse(record.archive.captureJson), verified = JSON.parse(record.archive.verifyJson)
      assert.equal(captured.receiptSha256, verified.receiptSha256)
      assert.equal(captured.archiveSha256, verified.archiveSha256)
      const project = record.lifecycle.stopped.storageProof.projectId
      record.physicalBefore ??= await inspect('before', workspace.id, record.archive.archiveId, project)
      await save()
      await page.reload()
      const card = page.getByRole('region', { name: '开发空间退役' })
      record.retirement ??= {}
      if (!record.retirement.retirementId)
        await genuineBegin(card, prefix + '/retirements', '核对空间退役计划', record.retirement)
      let retirement = await poll(context, prefix + '/retirements/' + record.retirement.retirementId, value => value.awaitingConfirmation || value.attentionRequired || value.phase === 'Execute' || !!value.completedAtUtc)
      Object.assign(record.retirement, retirement)
      await save()
      const recoveryProbe = process.env.WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE === '1' && workspace.id === state.workspace.id
      const interruptProbe = recoveryProbe && process.env.WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE === '1'
      const stopProbe = recoveryProbe && process.env.WHALEDECK_INSTALLED_RETIREMENT_STOP_PROBE === '1'
      assert.ok(!stopProbe || !interruptProbe)
      const stopping = stopProbe && retirement.awaitingConfirmation
        ? await armRetirementStop({ ...args, workspace, record }) : undefined
      const interruption = interruptProbe && retirement.awaitingConfirmation
        ? await armRetirementInterruption({ ...args, workspace, record }) : undefined
      const held = recoveryProbe && !interruptProbe && !stopProbe && retirement.awaitingConfirmation
        ? await holdRetirementSource({ ...args, workspace, record, project }) : undefined
      try {
      if (retirement.awaitingConfirmation) {
        await card.getByRole('button', { name: '刷新退役状态' }).click()
        const confirm = card.getByRole('button', { name: '确认删除源码并退役空间', exact: true })
        await expect(confirm).toBeDisabled()
        const plan = JSON.parse(retirement.planJson)
        assert.equal(plan.archiveId, record.archive.archiveId)
        assert.equal(plan.expectedEpoch, record.lifecycle.expectedEpochs.stopped)
        assert.equal(plan.deletionAuthorized, false)
        const denied = await context.request.post(`${base}${prefix}/retirements/${retirement.retirementId}/confirm`, {
          headers: await headers(context), data: { planSha256: plan.retirementPlanSha256, deleteSourceAndReleaseBudget: false },
        })
        assert.equal(denied.status(), 400)
        record.independentDeletionConsentRequired = true
        await card.locator('label.el-checkbox').click()
        const posted = page.waitForResponse(r => new URL(r.url()).pathname === `${prefix}/retirements/${retirement.retirementId}/confirm`)
        await confirm.click()
        const response = await posted
        record.confirmation = response.request().postDataJSON()
        await save()
        assert.equal(record.confirmation.deleteSourceAndReleaseBudget, true)
        assert.equal(record.confirmation.planSha256, plan.retirementPlanSha256)
        assert.equal(response.status(), 202)
        if (interruption) {
          const accepted = await response.json()
          await interruption.interrupt(accepted.executeTaskId)
        }
        if (stopping) {
          const accepted = await response.json()
          await stopping.cancel(accepted.executeTaskId)
        }
      }
      retirement = await poll(context, prefix + '/retirements/' + retirement.retirementId, value => !!value.completedAtUtc || value.attentionRequired)
      if (recoveryProbe && retirement.attentionRequired)
        retirement = await recoverRetirement({ ...args, workspace, record, retirement, held })
      if (recoveryProbe && retirement.completedAtUtc && record.recovery && !record.recovery.passed)
        retirement = await finishRetirementRecovery({ ...args, workspace, record }, retirement)
      } finally { await held?.close(); await interruption?.close(); await stopping?.close() }
      Object.assign(record.retirement, retirement)
      await save()
      assert.equal(retirement.state, 'Succeeded')
      assert.equal(retirement.phase, 'Retired')
      assert.equal(retirement.sourceRetired, true)
      assert.equal(retirement.budgetReleased, true)
      assert.equal(retirement.retainsHdd, true)
      const executed = JSON.parse(retirement.executionJson)
      assert.equal(executed.workspaceId, workspace.id)
      assert.equal(executed.retirementId, retirement.retirementId)
      for (const flag of ['sourceRemoved', 'quotaReleased', 'claimReleased', 'retainsHdd']) assert.equal(executed[flag], true)
      record.physicalAfter = await inspect('after', workspace.id, record.archive.archiveId, project, retirement.retirementId)
      assert.deepEqual(record.physicalAfter.archive, record.physicalBefore.archive)
      assert.deepEqual(record.physicalAfter.kernelQuota, Array(8).fill(0))
      assert.equal(record.physicalAfter.sourceExists, false)
      assert.equal(record.physicalAfter.claimExists, false)
      for (const key of ['cpuMillis', 'memoryMiB', 'diskMiB'])
        assert.equal(record.physicalBefore.reservedBudget[key] - record.physicalAfter.reservedBudget[key], workspace.budget[key])
      record.completedAtUtc = retirement.completedAtUtc
      await save()
    }
    const selected = await get(prefix)
    assert.equal(selected.lifecycleState, 'Retired')
    await page.goto(`${base}${route}/${workspace.id}/environment`)
    await expect(page.getByText('空间已退役，保留 HDD 归档、历史任务和报告。', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '核验 Runner 存储', exact: true })).toHaveCount(0)
    for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
      await page.setViewportSize({ width, height: 900 }); await page.emulateMedia({ colorScheme })
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
      await page.screenshot({ path: join(evidence, `retired-${workspace.id}-${width}-${colorScheme}.png`), fullPage: true })
    }
  }
  state.retirementBusiness ??= {}
  const record = state.retirementBusiness
  await retire(state.workspace, record)
  record.reportDownloadsAfterRetirement ??= []
  for (const run of state.runs) for (const format of ['json', 'markdown']) {
    const response = await context.request.get(`${base}${api}/${id}/runs/${run.id}/report/${format}`)
    assert.equal(response.status(), 200)
    const bytes = await response.body(), sha256 = createHash('sha256').update(bytes).digest('hex')
    assert.equal(sha256, response.headers()['x-content-sha256'])
    if (format === 'json') assert.equal(JSON.parse(bytes).outcome, 'Succeeded')
    record.reportDownloadsAfterRetirement.push({ runId: run.id, format, sha256, bytes: bytes.length })
  }
  await save()
  state.budgetReuse ??= { key: randomUUID(), command: { slug: state.slug + '-reuse', storageRootId: state.workspace.storageRootId,
    runtimes: ['node', 'dotnet'], budget: state.workspace.budget } }
  const reuse = state.budgetReuse
  await save()
  if (!reuse.workspace) {
    const response = await context.request.post(base + api, { headers: { ...await headers(context), 'Idempotency-Key': reuse.key }, data: reuse.command })
    assert.ok([200, 201].includes(response.status()))
    reuse.workspace = (await response.json()).workspace
    assert.deepEqual(reuse.workspace.budget, state.workspace.budget)
    await save()
  }
  if (!reuse.retirement?.completedAtUtc) {
    let workspace = await get(`${api}/${reuse.workspace.id}`)
    if (workspace.state !== 'StorageAllocated') {
      await page.goto(`${base}${route}/${workspace.id}/environment`)
      await page.getByRole('button', { name: '核对存储计划', exact: true }).click()
      await page.getByRole('button', { name: '确认分配存储', exact: true }).click()
      workspace = await poll(context, `${api}/${workspace.id}`, value => value.state === 'StorageAllocated')
    }
    reuse.workspace = workspace
    reuse.retirement ??= {}
    await save()
    // Even the budget-reuse allocation is closed through the same product
    // archive/explicit-delete workflow. Its history and HDD copy are retained.
    await retire(workspace, reuse.retirement)
  }
  assert.deepEqual(reuse.retirement.physicalBefore.reservedBudget, record.physicalBefore.reservedBudget)
  assert.deepEqual(reuse.retirement.physicalAfter.reservedBudget, record.physicalAfter.reservedBudget)
  record.passed = true
  reuse.passed = true
  await save()
}
