// A bounded freeze creates a deterministic window for genuine product Cancel.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function armRetirementStop({ workspace, record, state, evidence, save, page, context, base, api }) {
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_STOP_PROBE, '1')
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE, undefined)
  assert.equal(workspace.id, state.workspace.id)
  assert.equal(workspace.ownerSubject, state.ownerSubject)
  assert.equal(workspace.slug, state.slug)
  record.recovery ??= {}
  assert.equal(record.recovery.stop, undefined, 'NEVER_REPEAT_QA_STOP_WINDOW')
  const child = spawn('/usr/bin/python3', ['-B', fileURLToPath(new URL('./pause_qa_retirement_execution.py', import.meta.url))],
    { stdio: ['pipe', 'pipe', 'pipe'] })
  let spawnError, stderr = ''
  child.on('error', error => { spawnError = error })
  const exited = new Promise(resolve => child.once('close', (code, signal) => resolve({ code, signal })))
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]()
  child.stderr.on('data', bytes => { stderr = (stderr + bytes).slice(-8192) })
  async function next() {
    let timer
    try {
      const item = await Promise.race([lines.next(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('QA_STOP_CONTROLLER_DEADLINE')), 100_000)
      })])
      assert.equal(spawnError, undefined)
      assert.equal(item.done, false, 'QA_STOP_CONTROLLER_EXITED: '+stderr)
      return JSON.parse(item.value)
    } finally { clearTimeout(timer) }
  }
  let closed = false, submitted = false
  async function close() {
    if (closed) return
    closed = true; child.stdin.end()
    let timer
    try {
      const result = await Promise.race([exited, new Promise(resolve => {
        timer = setTimeout(() => { child.kill('SIGTERM'); resolve({ timedOut: true }) }, 100_000)
      })])
      assert.equal(result.timedOut, undefined)
      assert.equal(result.code, 0, stderr)
      return result
    } finally { clearTimeout(timer) }
  }
  try {
    child.stdin.write(JSON.stringify({ evidence, workspaceId: workspace.id, ownerSubject: state.ownerSubject,
      retirementId: record.retirement.retirementId, attempt: state.browserAttempts })+'\n')
    const armed = await next(); assert.equal(armed.armed, true)
    record.recovery.stopArmed = armed
    await save()
    return { close, async cancel(taskId) {
      assert.equal(submitted, false); submitted = true
      child.stdin.write(JSON.stringify({ taskId })+'\n')
      const frozen = await next()
      assert.equal(frozen.frozen, true); assert.equal(frozen.before.taskId, taskId)
      record.recovery.stopFrozen = frozen
      await save()
      try {
        const endpoint = `${api}/${workspace.id}/retirements/${record.retirement.retirementId}`
        const card = page.getByRole('region', { name: '开发空间退役' })
        await card.getByRole('button', { name: '刷新退役状态', exact: true }).click()
        const button = card.getByRole('button', { name: '取消退役', exact: true })
        await expect(button).toBeEnabled({ timeout: 5000 })
        const response = page.waitForResponse(r => new URL(r.url()).pathname === endpoint+'/cancel' && r.request().method() === 'POST', { timeout: 8000 })
        await button.click({ timeout: 5000 })
        const accepted = await response
        assert.equal(accepted.status(), 202)
        const view = await accepted.json()
        assert.equal(view.executeTaskId, taskId)
        assert.equal(view.cancelRequested, true)
        record.recovery.stopUiCancellation = { status: accepted.status(), taskId, cancelRequested: true }
        await save()
      } catch (error) {
        // EOF thaws immediately if the UI failed; no direct stop fallback.
        child.stdin.end()
        throw error
      }
      child.stdin.write('verify\n')
      const result = await next()
      assert.equal(result.stopped, true)
      assert.equal(result.receipt.taskId, taskId)
      record.recovery.stop = result
      record.recovery.stop.controllerExit = await close()
      await save()
    } }
  } catch (error) {
    child.stdin.end(); await exited
    throw error
  }
}
