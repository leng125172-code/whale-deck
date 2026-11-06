import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Synthetic UI protocol fixture only. No real Cookie, PG, Authentik, Agent or HDD.
export async function installRecycleArchiveFixture(page, options = {}) {
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
    purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
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
    purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
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
    resultCode: 'CONTAINER_RECYCLE_ARCHIVE_PLAN_ACCEPTED',
    version: 0,
    reservationRecorded: false,
    archiveCopied: null,
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
  function receipt(value) {
    return {
      id: execution,
      purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
      sourceOperationId: sourceOp,
      fingerprint: 'D'.repeat(64),
      state: value,
      sourceFenced: value !== 'Aborted',
      ownedFence: value !== 'Aborted',
      archiveCopied: value === 'Unknown' ? null : value === 'Completed',
      bindingCurrent: value === 'Completed' ? !options.bindingGone : null,
      publishedIdentity:
        value === 'Completed'
          ? {
              device: '18446744073709551615',
              inode: '9007199254740993',
              mode: 33184,
              ownerUid: 1000,
              ownerGid: 1001,
              linkCount: '1',
              sizeBytes: 7,
              modifiedAtUnixNs: '1700000000123456789',
            }
          : null,
      publishedChangedAtUnixNs: value === 'Completed' ? '1700000000987654321' : null,
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
        planHash: 'isolated-archive-confirmation',
        expiresAtUtc: row.readyExpiresAtUtc,
        warning: '预检不删除，清理后保留HDD备份；原件删除不可撤销，当前尚无归档恢复入口。',
      }
      row.resultCode = 'CONTAINER_RECYCLE_ARCHIVE_READY'
      row.version++
    } else if (['Pending', 'Reserving', 'Applying'].includes(row.state)) {
      if (options.hold && !state.canceled) {
        row.state = 'Applying'
        row.reservationRecorded = true
        row.version++
      } else {
        row.state = state.canceled ? 'Canceled' : options.unknown ? 'Unknown' : 'Succeeded'
        row.receipt = receipt(
          state.canceled ? 'Aborted' : options.unknown ? 'Unknown' : 'Completed',
        )
        row.reservationRecorded = !state.canceled
        row.archiveCopied = row.receipt.archiveCopied
        row.resultCode = state.canceled
          ? 'CONTAINER_RECYCLE_ARCHIVE_CANCELED'
          : options.unknown
            ? 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN'
            : 'CONTAINER_RECYCLE_ARCHIVE_COMPLETED'
        row.version++
      }
    }
    return clone()
  }
  await page.route('**/api/v1/containers/*/files/recycle-archives**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.split('/recycle-archives')[1] || ''
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
      row.resultCode = 'CONTAINER_RECYCLE_ARCHIVE_ACCEPTED'
      return route.fulfill({ status: 202, json: clone() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (row.executionJobId === null) {
        row.state = 'Canceled'
        row.confirmation = null
        row.resultCode = 'CONTAINER_RECYCLE_ARCHIVE_CANCELED'
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
  await page.route('**/api/v1/files/container-recycle-archives/recovery**', async (route) => {
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
    row.resultCode = 'CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED'
    return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
  })
  return { base, state, row, source, cid, id, execution }
}
