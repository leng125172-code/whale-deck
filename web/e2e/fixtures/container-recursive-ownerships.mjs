import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Explicit isolated UI protocol fixture, never real Authentik/SQL/Root/HDD evidence.
export async function installContainerRecursiveOwnershipFixture(page, options = {}) {
  const base = options.filePage ? await installFileFixture(page, options) : await installContainerFilesFixture(page, options)
  const cid = 'a'.repeat(64), id = 'ad12ad12-ad12-4d12-ad12-ad12ad12ad12', execution = 'bd12bd12-bd12-4d12-bd12-bd12bd12bd12'
  const row = { id, planningJobId: id, executionJobId: null, command: null, state: 'Queued', rootUid: null, rootGid: null, executorUid: null, executorGid: null, entryCount: null, fileCount: null, directoryCount: null, changeCount: null, manifestDigest: null,
    readyExpiresAtUtc: null, resultCode: null, version: 0, historyMayExist: false, historyVerified: false, historyDigest: null, receipt: null, confirmation: null }
  const state = { writes: [], reads: [], registered: false, holdPlan: false, holdRecursive: false, status: 200, canceled: false, lostQueue: false, lostConfirm: false, lostAck: false, key: '', malformed: false, pause: false, waiting: false, release: () => {} }
  function planned() {
    row.state = 'Ready'; row.rootUid = 1000; row.rootGid = 1001; row.executorUid = 5100; row.executorGid = 5101
    row.entryCount = 4; row.fileCount = 2; row.directoryCount = 2; row.changeCount = 2; row.manifestDigest = 'D'.repeat(64)
    row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
    row.confirmation = { planHash: 'isolated-recursive-owner-confirmation', expiresAtUtc: row.readyExpiresAtUtc, warning: '预检未修改所有者；先保留HDD元数据，按显式目标UID/GID逐项修改。Partial/Unknown不自动续做或回滚。' }
    row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_READY'; row.version++
  }
  function receipt(kind) {
    const verified = ['Completed','Partial'].includes(kind)
    row.historyMayExist = kind !== 'Aborted'; row.historyVerified = verified; row.historyDigest = verified ? 'E'.repeat(64) : null
    row.receipt = { id: execution, containerId: cid, instanceId: row.command.location.instanceId, fingerprint: 'A'.repeat(64), targetProof: 'B'.repeat(64), state: kind,
      bindingCurrent: kind === 'Completed' && !options.superseded, replayed: false, completedCount: kind === 'Completed' ? 4 : kind === 'Partial' ? 1 : 0, changedCount: kind === 'Completed' ? 2 : kind === 'Partial' ? 1 : 0,
      abortedCount: 0, notStartedCount: kind === 'Aborted' ? 4 : kind === 'Partial' ? 3 : 0, unknownCount: kind === 'Unknown' ? 4 : 0,
      historyMayExist: row.historyMayExist, historyVerified: verified, historyDigest: row.historyDigest ?? '', historyAvailable: verified, outcomeDigest: verified ? 'F'.repeat(64) : '' }
  }
  const response = () => structuredClone(row)
  const getResponse = () => {
    if (['Queued','Planning'].includes(row.state)) {
      if (state.holdPlan) { if (row.state !== 'Planning') { row.state = 'Planning'; row.version++ } }
      else planned()
    } else if (['Pending','Applying'].includes(row.state)) {
      if (state.canceled) { row.state = 'Canceled'; receipt('Aborted'); row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_CANCELED'; row.version++ }
      else if (options.unresolved) { row.state = options.unresolved; receipt(options.unresolved); row.resultCode = options.unresolved === 'Partial' ? 'CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL_REQUIRES_ACKNOWLEDGEMENT' : 'CONTAINER_RECURSIVE_OWNERSHIP_RESULT_UNKNOWN'; row.version++ }
      else if (state.holdRecursive) { if (row.state !== 'Applying') { row.state = 'Applying'; row.version++ } }
      else { row.state = options.superseded ? 'Superseded' : 'Succeeded'; receipt('Completed'); row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_COMPLETED'; row.version++ }
    }
    const result = response()
    if (state.malformed && result.receipt) result.receipt.changedCount = 999
    return result
  }
  await page.route('**/api/v1/containers/*/files/recursive-ownerships**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname.split('/recursive-ownerships')[1] || ''
    if (request.method() === 'GET') {
      state.reads.push(path)
      if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
      if (!state.registered || path.startsWith('/by-key/') && path !== '/by-key/' + state.key) return route.fulfill({ status: 404, json: { status: 404 } })
      if (state.pause) { state.waiting = true; await new Promise(resolve => { state.release = resolve }) }
      try { return await route.fulfill({ json: getResponse() }) } catch { return }
    }
    const body = request.postDataJSON(); state.writes.push({ path, body, key: request.headers()['idempotency-key'] })
    if (path === '') {
      state.registered = true; state.key = request.headers()['idempotency-key']
      row.command = { location: { containerId: cid, instanceId: body.instanceId, path: body.path }, uid: body.uid, gid: body.gid, expectedVersion: body.expectedVersion }
      if (options.loseQueue && !state.lostQueue) { state.lostQueue = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution; row.state = 'Pending'; row.confirmation = null; row.version++; row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_ACCEPTED'
      if (options.loseConfirm && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (!row.executionJobId) { row.state = 'Canceled'; row.confirmation = null; row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_CANCELED' }
      row.version++; return route.fulfill({ status: 202, json: response() })
    }
    return route.fulfill({ status: 404, json: { status: 404 } })
  })
  if (options.filePage) {
    row.command = { location: { containerId: cid, instanceId: '777:2026-10-28T01:02:03.123456789Z', path: 'work/old-tree' }, uid: 2200, gid: 3200, expectedVersion: 'A'.repeat(64) }
    planned(); row.executionJobId = execution; row.confirmation = null; row.state = options.unresolved ?? 'Unknown'; receipt(row.state)
    row.resultCode = row.state === 'Partial' ? 'CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL_REQUIRES_ACKNOWLEDGEMENT' : 'CONTAINER_RECURSIVE_OWNERSHIP_RESULT_UNKNOWN'
    await page.route('**/api/v1/files/container-recursive-ownerships/recovery**', async route => {
      const request = route.request(), path = new URL(request.url()).pathname.split('/recovery')[1] || ''
      if (request.method() === 'GET') {
        state.reads.push('recovery' + path)
        if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
        return route.fulfill({ json: path ? response() : ['Unknown','Partial'].includes(row.state) ? [response()] : [] })
      }
      state.writes.push({ path: 'recovery' + path, body: request.postDataJSON(), key: request.headers()['idempotency-key'] })
      const partial = row.state === 'Partial'; row.state = 'Acknowledged'; row.version++; row.resultCode = partial ? 'CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL_ACKNOWLEDGED' : 'CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_ACKNOWLEDGED'
      if (options.loseAck && !state.lostAck) { state.lostAck = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
    })
  }
  return { base, state, row, cid, id, execution }
}
