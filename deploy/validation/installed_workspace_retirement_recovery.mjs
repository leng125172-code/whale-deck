// Real open-unlinked inode rejection and explicit UI recovery for the exact
// new QA workspace. No source/SQL writes and no host execution/task control.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function holdRetirementSource({ workspace, record, project, state, save }) {
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_RECOVERY_PROBE, '1')
  assert.equal(workspace.id, state.workspace.id)
  assert.equal(workspace.slug, state.slug)
  assert.equal(workspace.ownerSubject, state.ownerSubject)
  const child = spawn('/usr/bin/python3', ['-B', fileURLToPath(new URL('./hold_qa_workspace_source_fd.py', import.meta.url))],
    { stdio: ['pipe', 'pipe', 'pipe'] })
  const exited = new Promise(resolve => child.once('exit', (code, signal) => resolve({ code, signal })))
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]()
  let stderr = ''
  child.stderr.on('data', bytes => { stderr = (stderr + bytes).slice(-8192) })
  async function next() {
    let timer
    try {
      const result = await Promise.race([lines.next(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('QA_HELD_SOURCE_DEADLINE')), 30_000)
      })])
      assert.equal(result.done, false, 'QA_HELD_SOURCE_EXITED: ' + stderr)
      return JSON.parse(result.value)
    } finally { clearTimeout(timer) }
  }
  let closed = false
  async function close() {
    if (closed) return
    closed = true
    child.stdin.end()
    let timer
    try {
      const result = await Promise.race([exited, new Promise(resolve => {
        timer = setTimeout(() => { child.kill('SIGTERM'); resolve({ timedOut: true }) }, 5000)
      })])
      assert.equal(result.timedOut, undefined)
      assert.equal(result.code, 0, stderr)
      return result
    } finally { clearTimeout(timer) }
  }
  try {
    child.stdin.write(JSON.stringify({ workspaceId: workspace.id, slug: workspace.slug,
      ownerSubject: state.ownerSubject, projectId: project }) + '\n')
    record.recovery ??= {}
    record.recovery.held = { ...await next(), holderPid: child.pid }
    assert.equal(record.recovery.held.ready, true)
    await save()
    return { close, async inspect() { child.stdin.write('inspect\n'); return next() },
      async release() { child.stdin.write('release\n'); const result = await next();
        assert.equal(result.released, true); return { ...result, holderExit: await close() } } }
  } catch (error) { await close(); throw error }
}

export async function recoverRetirement(args) {
  const { page, context, base, api, workspace, record, retirement, held, save, headers, poll, evidence } = args
  const endpoint = `${api}/${workspace.id}/retirements/${retirement.retirementId}`
  const recovery = record.recovery
  assert.ok(recovery?.held?.ready, 'NO_REAL_HELD_SOURCE_FAULT')
  assert.equal(retirement.attentionRequired, true)
  assert.equal(retirement.completedAtUtc, null)
  assert.equal(retirement.budgetReleased, false)
  assert.equal(retirement.executionAttempt, 0)
  const old = await (await context.request.get(base + endpoint + '/attempts')).json()
  assert.equal(old.length, 1)
  assert.equal(old[0].attempt, 0)
  assert.equal(old[0].taskId, retirement.executeTaskId)
  assert.equal(old[0].status, 'Rejected')
  assert.equal(old[0].code, 'WORKSPACE_STORAGE_RETIREMENT_QUOTA_STILL_REFERENCED')
  assert.ok(old[0].settledAtUtc && old[0].proofSha256)
  recovery.failedView ??= retirement
  recovery.attemptsBefore ??= old
  assert.deepEqual(recovery.attemptsBefore, old)
  await save()
  if (held) {
    recovery.openUnlinked = await held.inspect()
    assert.equal(recovery.openUnlinked.inode, recovery.held.inode)
    assert.equal(recovery.openUnlinked.links, 0)
    assert.ok(recovery.openUnlinked.kernelQuota[2] || recovery.openUnlinked.kernelQuota[5])
    await save()
    recovery.release = await held.release()
    await save()
  }
  const card = page.getByRole('region', { name: '开发空间退役' })
  await card.getByRole('button', { name: '刷新退役状态', exact: true }).click()
  await expect(card.getByText('恢复会创建新的执行尝试，核对原 HDD 归档并接续原删除日志。原失败任务与证明继续保留。', { exact: true })).toBeVisible()
  const button = card.getByRole('button', { name: '确认接续退役', exact: true })
  await expect(button).toBeDisabled()
  const plan = JSON.parse(retirement.planJson)
  if (!recovery.key) {
    const denied = await context.request.post(base + endpoint + '/recover', {
      headers: { ...await headers(context), 'Idempotency-Key': randomUUID() },
      data: { previousTaskId: retirement.executeTaskId, planSha256: plan.retirementPlanSha256, continueDeletionAndReleaseBudget: false },
    })
    assert.equal(denied.status(), 400)
    recovery.independentConsentRequired = true
    await card.locator('label.el-checkbox').filter({ hasText: '我确认接续删除此空间的剩余源码并释放配额与预算，保留 HDD 归档和原失败历史' }).click()
  }
  const matches = url => url.origin === base && url.pathname === endpoint + '/recover'
  const observer = async route => {
    const request = route.request()
    if (request.method() !== 'POST') { await route.continue(); return }
    recovery.key = request.headers()['idempotency-key']
    recovery.command = request.postDataJSON()
    assert.equal(recovery.command.previousTaskId, retirement.executeTaskId)
    assert.equal(recovery.command.planSha256, plan.retirementPlanSha256)
    assert.equal(recovery.command.continueDeletionAndReleaseBudget, true)
    await save()
    await route.continue()
  }
  if (recovery.key) {
    const replay = await context.request.post(base + endpoint + '/recover', {
      headers: { ...await headers(context), 'Idempotency-Key': recovery.key }, data: recovery.command,
    })
    assert.equal(replay.status(), 202)
    recovery.accepted = await replay.json()
  } else {
    await page.route(matches, observer)
    try {
      const response = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/recover' && r.request().method() === 'POST')
      await button.click()
      const accepted = await response
      assert.equal(accepted.status(), 202)
      recovery.accepted = await accepted.json()
    } finally { await page.unroute(matches, observer) }
  }
  assert.equal(recovery.accepted.executionAttempt, 1)
  assert.notEqual(recovery.accepted.executeTaskId, retirement.executeTaskId)
  assert.equal(recovery.accepted.confirmedAtUtc, retirement.confirmedAtUtc)
  assert.equal(recovery.accepted.planExpiresAtUtc, retirement.planExpiresAtUtc)
  assert.equal(recovery.accepted.planJson, retirement.planJson)
  await save()
  const result = await poll(context, endpoint, value => !!value.completedAtUtc || value.attentionRequired)
  return finishRetirementRecovery(args, result)
}

export async function finishRetirementRecovery(args, result) {
  const { page, context, base, api, workspace, record, save, evidence } = args
  const recovery = record.recovery, retirement = recovery.failedView
  const endpoint = `${api}/${workspace.id}/retirements/${retirement.retirementId}`
  const card = page.getByRole('region', { name: '开发空间退役' })
  assert.equal(recovery.release?.released, true)
  assert.equal(result.executionAttempt, 1)
  assert.equal(result.state, 'Succeeded')
  const response = await context.request.get(base + endpoint + '/attempts')
  assert.equal(response.status(), 200)
  recovery.attemptsAfter = await response.json()
  assert.equal(recovery.attemptsAfter.length, 2)
  assert.deepEqual(recovery.attemptsAfter[0], recovery.attemptsBefore[0])
  assert.equal(recovery.attemptsAfter[1].previousTaskId, retirement.executeTaskId)
  assert.equal(recovery.attemptsAfter[1].status, 'Succeeded')
  assert.equal(recovery.attemptsAfter[1].attempt, 1)
  await card.getByRole('button', { name: '刷新退役状态', exact: true }).click()
  await card.getByRole('button', { name: '查看执行尝试', exact: true }).click()
  const history = card.getByRole('list', { name: '退役执行尝试' })
  await expect(history).toContainText(retirement.executeTaskId)
  await expect(history).toContainText(result.executeTaskId)
  for (const [width, colorScheme] of [[320, 'dark'], [1440, 'light']]) {
    await page.setViewportSize({ width, height: 900 }); await page.emulateMedia({ colorScheme })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    await page.screenshot({ path: join(evidence, `recovery-history-${width}-${colorScheme}.png`), fullPage: true })
  }
  recovery.passed = true
  await save()
  return result
}
