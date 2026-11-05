import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Synthetic UI protocol fixture only. No real Cookie, PG, Authentik, Agent or HDD.
export async function installRecycleRestoreFixture(page, options = {}) {
  const base = options.filePage
    ? await installFileFixture(page, options)
    : await installContainerFilesFixture(page, options)
  const cid = 'a'.repeat(64),
    id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
    execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
    sourceId = 'cc12cc12-cc12-4c12-cc12-cc12cc12cc12',
    sourceOp = 'dc12dc12-dc12-4c12-dc12-dc12dc12dc12'
  const source = {
    sourceIntentId: sourceId,
    sourceOperationId: sourceOp,
    location: {
      containerId: cid,
      instanceId: '777:2026-10-22T01:02:03.123456789Z',
      path: 'work/' + '原文件'.repeat(24) + '.bin',
    },
    originalUid: 1000,
    originalGid: 1001,
    originalMode: 33184,
    sizeBytes: 7,
    sha256: 'B'.repeat(64),
    attributeCount: 1,
    historyDigest: 'E'.repeat(64),
    completedAtUtc: '2026-11-05T01:02:03Z',
  }
  const row = {
    id,
    planningJobId: id,
    executionJobId: null,
    sourceIntentId: sourceId,
    sourceOperationId: sourceOp,
    sourceHistoryDigest: source.historyDigest,
    location: source.location,
    state: 'Queued',
    originalUid: null,
    originalGid: null,
    originalMode: null,
    executorUid: null,
    executorGid: null,
    sizeBytes: 7,
    sha256: source.sha256,
    attributeCount: 1,
    readyExpiresAtUtc: null,
    resultCode: 'CONTAINER_RECYCLE_RESTORE_PLAN_ACCEPTED',
    version: 0,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: null,
  }
  const state = {
    writes: [],
    reads: [],
    registered: false,
    key: '',
    status: 200,
    canceled: false,
    lostQueue: false,
    paused: false,
    waiting: false,
    release: () => {},
  }
  const clone = () => JSON.parse(JSON.stringify(row))
  function receipt(state) {
    const verified = state !== 'Aborted'
    return {
      id: execution,
      sourceOperationId: sourceOp,
      fingerprint: 'D'.repeat(64),
      targetProof: 'C'.repeat(64),
      state,
      bindingCurrent: state === 'Completed',
      replayed: false,
      historyVerified: verified,
      historyDigest: verified ? source.historyDigest : '',
      historyAvailable: verified,
      sourceState: state === 'Completed' ? 'Restored' : null,
      sourceRetained: state === 'Completed' ? false : null,
      sourceRestored: state === 'Completed' ? true : null,
      restoredByThisOperation: state === 'Unknown' ? null : state === 'Completed',
    }
  }
  function read() {
    if (['Queued', 'Planning'].includes(row.state)) {
      row.state = 'Ready'
      row.originalUid = 1000
      row.originalGid = 1001
      row.originalMode = 33184
      row.executorUid = 5100
      row.executorGid = 5101
      row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
      row.confirmation = {
        planHash: 'isolated-independent-restore-confirmation',
        expiresAtUtc: row.readyExpiresAtUtc,
        warning: '原回收来源独立恢复，预检不写入，不覆盖现有目标。',
      }
      row.resultCode = 'CONTAINER_RECYCLE_RESTORE_READY'
      row.version++
    } else if (['Pending', 'Applying'].includes(row.state)) {
      if (options.hold && !state.canceled) {
        row.state = 'Applying'
        row.version++
      } else {
        row.state = state.canceled ? 'Canceled' : options.unknown ? 'Unknown' : 'Succeeded'
        row.receipt = receipt(
          state.canceled ? 'Aborted' : options.unknown ? 'Unknown' : 'Completed',
        )
        row.historyVerified = !state.canceled
        row.historyDigest = state.canceled ? null : source.historyDigest
        row.resultCode = state.canceled
          ? 'CONTAINER_RECYCLE_RESTORE_CANCELED'
          : options.unknown
            ? 'CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN'
            : 'CONTAINER_RECYCLE_RESTORE_COMPLETED'
        row.version++
      }
    }
    return clone()
  }
  await page.route('**/api/v1/containers/*/files/recycle-restores**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.split('/recycle-restores')[1] || ''
    if (request.method() === 'GET') {
      state.reads.push(path)
      if (state.status !== 200)
        return route.fulfill({ status: state.status, json: { status: state.status } })
      if (path === '/sources') return route.fulfill({ json: { items: [source], next: null } })
      if (!state.registered) return route.fulfill({ status: 404, json: { status: 404 } })
      if (state.paused) {
        state.waiting = true
        await new Promise((resolve) => {
          state.release = resolve
        })
      }
      try {
        return await route.fulfill({ json: read() })
      } catch {
        return
      }
    }
    const body = request.postDataJSON()
    state.writes.push({ path, body, key: request.headers()['idempotency-key'] })
    if (path === '') {
      state.registered = true
      state.key = request.headers()['idempotency-key']
      if (options.loseQueue && !state.lostQueue) {
        state.lostQueue = true
        return route.abort('failed')
      }
      return route.fulfill({ status: 202, json: clone() })
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution
      row.state = 'Pending'
      row.confirmation = null
      row.version++
      row.resultCode = 'CONTAINER_RECYCLE_RESTORE_ACCEPTED'
      return route.fulfill({ status: 202, json: clone() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (row.executionJobId === null) {
        row.state = 'Canceled'
        row.confirmation = null
        row.resultCode = 'CONTAINER_RECYCLE_RESTORE_CANCELED'
        row.version++
        return route.fulfill({ status: 202, json: clone() })
      }
      return route.fulfill({ status: 202, json: read() })
    }
    return route.fulfill({ status: 400, json: { status: 400 } })
  })
  if (options.filePage) {
    state.registered = true
    read()
    row.state = 'Pending'
    row.executionJobId = execution
    row.confirmation = null
    read()
  }
  await page.route('**/api/v1/files/container-recycle-restores/recovery**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname
    if (request.method() === 'GET')
      return route.fulfill({
        json: path.endsWith('/recovery') ? (row.state === 'Unknown' ? [clone()] : []) : clone(),
      })
    state.writes.push({
      path,
      body: request.postDataJSON(),
      key: request.headers()['idempotency-key'],
    })
    row.state = 'Acknowledged'
    row.version++
    row.resultCode = 'CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED'
    return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
  })
  return { base, state, row, source, cid, id, execution }
}
