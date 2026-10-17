import { installFileFixture } from './files.mjs'

// UI protocol fixture only; SQL, real identity and installed Root have separate evidence.
export async function installFileRecoveryFixture(page, options = {}) {
  const files = await installFileFixture(page, options)
  const state = { reads: [], writes: [], missing: false, status: 200, key: '', lostCreate: false, lostConfirm: false }
  const originalId = 'f28bc837-77ef-43c9-a0c9-617f790df336', originalExecution = '3c9c0737-999c-468d-817b-c4f9b6288210'
  const id = 'c47ae017-288b-445f-bae1-ef0ce9baa4dd', execution = '11c8ecb3-112e-4d6b-b26c-d63982ca941e'
  const candidate = { kind: 'tree-move', intentId: originalId, executionJobId: originalExecution, version: 17,
    createdAtUtc: new Date().toISOString(), leaseExpiresAtUtc: null, rootStartRecorded: true,
    integrity: options.invalidBinding ? 'InvalidBinding' : 'Verified',
    source: options.invalidBinding ? null : { rootId: 'files.shared', path: options.sourcePath || '很长的中文目录/abcdefghijklmnopqrstuvwxyz-0123456789/source' },
    target: options.invalidBinding ? null : { rootId: 'files.shared', path: '资料/target' }, hasRetainedObjects: null }
  const row = { id, planningJobId: id, executionJobId: null, kind: candidate.kind, originalIntentId: originalId, originalExecutionJobId: originalExecution, originalVersion: 17,
    state: 'Queued', disposition: 'AcknowledgeUnknown', reasonCode: 'OriginalSessionLost', rootState: null, readyExpiresAtUtc: null, version: 0,
    resultCode: null, originalResolution: null, confirmation: null }
  await page.route('**/api/v1/files/recovery**', async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname.replace('/api/v1/files/recovery', '')
    const reply = (value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (request.method() === 'GET') state.reads.push(path)
    else state.writes.push({ path, body: request.postDataJSON(), headers: request.headers() })
    if (state.status !== 200) return reply({ status: state.status }, state.status)
    if (request.method() === 'GET' && state.missing) return reply({ status: 404 }, 404)
    if (request.method() === 'GET' && path === '') return reply({ entries: options.empty ? [] : [candidate], nextCursor: null })
    if (request.method() === 'GET' && path === '/tree-move/' + originalId) return reply(candidate)
    if (path === '/operations' && request.method() === 'POST') {
      state.key = request.headers()['idempotency-key']; const command = request.postDataJSON()
      row.disposition = command.disposition; row.reasonCode = command.reasonCode
      if (options.loseCreateResponse && !state.lostCreate) { state.lostCreate = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/confirm') && request.method() === 'POST') {
      row.executionJobId = execution; row.confirmation = null; row.state = options.unknown ? 'Unknown' : 'DispositionQueued'; row.version++
      if (options.loseConfirmResponse && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/cancel') && request.method() === 'POST') { row.state = 'Canceled'; row.confirmation = null; row.version++; return reply(row, 202) }
    if (request.method() === 'GET' && (path === '/operations/' + id || path === '/operations/by-key/' + state.key)) {
      if (!state.key) return reply({ status: 404 }, 404)
      if (row.state === 'Queued' && !options.holdPlan) {
        row.state = 'Ready'; row.rootState = row.disposition === 'ReconcileCompleted' ? 'Completed' : 'Unknown'
        row.readyExpiresAtUtc = new Date(Date.now() + (options.expiryMs || 300000)).toISOString(); row.version++
        row.confirmation = { planHash: 'ui-recovery-plan-not-auth-token', expiresAtUtc: row.readyExpiresAtUtc, warning: row.disposition === 'ReconcileCompleted' ? '仅按完整可信回执对账，不重做原文件操作。' : '只接受历史不确定性，不代表原文件操作成功；不重做、不删除留存对象。' }
      } else if (row.state === 'DispositionQueued') { row.state = 'Disposed'; row.originalResolution = row.disposition === 'ReconcileCompleted' ? 'Succeeded' : 'Acknowledged'; row.resultCode = 'FILES_RECOVERY_DISPOSED'; row.version++ }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })
  return { files, state, row, candidate }
}
