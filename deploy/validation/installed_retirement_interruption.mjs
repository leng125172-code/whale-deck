// Explicit process-death probe for the exact newly declared QA execution.
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'

export async function armRetirementInterruption({ workspace, record, state, evidence, save }) {
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_INTERRUPT_PROBE, '1')
  assert.equal(workspace.id, state.workspace.id)
  assert.equal(workspace.ownerSubject, state.ownerSubject)
  assert.equal(workspace.slug, state.slug)
  record.recovery ??= {}
  assert.equal(record.recovery.interruption, undefined, 'NEVER_REPEAT_QA_PROCESS_DEATH')
  const child = spawn('/usr/bin/python3', ['-B', fileURLToPath(new URL('./interrupt_qa_retirement_execution.py', import.meta.url))],
    { stdio: ['pipe', 'pipe', 'pipe'] })
  let spawnError
  child.on('error', error => { spawnError = error })
  const exited = new Promise(resolve => child.once('close', (code, signal) => resolve({ code, signal })))
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]()
  let stderr = ''
  child.stderr.on('data', bytes => { stderr = (stderr + bytes).slice(-8192) })
  async function next() {
    let timer
    try {
      const item = await Promise.race([lines.next(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('QA_INTERRUPTION_CONTROLLER_DEADLINE')), 100_000)
      })])
      assert.equal(spawnError, undefined)
      assert.equal(item.done, false, 'QA_INTERRUPTION_CONTROLLER_EXITED: '+stderr)
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
    record.recovery.interruptionArmed = armed
    await save()
    return { close, async interrupt(taskId) {
      assert.equal(submitted, false); submitted = true
      child.stdin.write(JSON.stringify({ taskId })+'\n')
      const result = await next()
      assert.equal(result.interrupted, true)
      assert.equal(result.before.taskId, taskId)
      record.recovery.interruption = result
      record.recovery.interruption.controllerExit = await close()
      await save()
    } }
  } catch (error) {
    child.stdin.end()
    await exited
    throw error
  }
}
