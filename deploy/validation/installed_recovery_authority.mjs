// One generated QA owner only; the child restores its original group on EOF.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'

export async function installedRecoveryAuthority(args) {
  const { page, context, base, api, route, workspace, state, record, retirement, headers, save, evidence, authorityUsername } = args
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE, '1')
  const proof = record.recovery.authority ??= { deniedKey: randomUUID() }
  if (proof.passed) return
  await save()
  const child = spawn('/usr/bin/python3', ['-B', fileURLToPath(new URL('./owned_recovery_authority.py', import.meta.url))], { stdio: ['pipe', 'pipe', 'pipe'] })
  const exit = new Promise(resolve => child.once('exit', (code, signal) => resolve({ code, signal })))
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]()
  let stderr = ''
  child.stderr.on('data', bytes => { stderr = (stderr + bytes).slice(-8192) })
  async function next() {
    let timer
    try {
      const row = await Promise.race([lines.next(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('RECOVERY_AUTHORITY_CHILD_DEADLINE')), 90_000)
      })])
      assert.equal(row.done, false, stderr)
      return JSON.parse(row.value)
    } finally { clearTimeout(timer) }
  }
  try {
    child.stdin.write(JSON.stringify({ evidence, username: authorityUsername, subject: state.ownerSubject,
      workspaceId: workspace.id, retirementId: retirement.retirementId, attempt: args.authorityAttempt }) + '\n')
    proof.removal = await next()
    assert.equal(proof.removal.removed, true)
    await save()
    const endpoint = `${api}/${workspace.id}/retirements/${retirement.retirementId}`
    const denied = await context.request.post(base + endpoint + '/recover', {
      headers: { ...await headers(context), 'Idempotency-Key': proof.deniedKey },
      data: { previousTaskId: retirement.executeTaskId,
        planSha256: JSON.parse(retirement.planJson).retirementPlanSha256,
        continueDeletionAndReleaseBudget: true },
    })
    proof.recoveryStatus = denied.status()
    proof.historyStatus = (await context.request.get(base + endpoint + '/attempts')).status()
    await save()
    assert.equal(proof.recoveryStatus, 403)
    assert.equal(proof.historyStatus, 403)
    child.stdin.write('restore\n')
    proof.restoration = await next()
    assert.equal(proof.restoration.restored, true)
    assert.equal(proof.restoration.unchanged, true)
    child.stdin.end()
    proof.childExit = await exit
    assert.equal(proof.childExit.code, 0, stderr)
    // An authoritative resource read refreshes current permissions after the
    // original directory group is restored. The old login is never replaced.
    assert.equal((await context.request.get(base + endpoint)).status(), 200)
    await page.goto(`${base}${route}/${workspace.id}/environment`)
    proof.passed = true
    await save()
  } finally {
    child.stdin.end()
    // Parent Python also waits for the restore receipt before any retry or
    // generated-account cleanup. Never kill a child in the middle of restore.
  }
}
