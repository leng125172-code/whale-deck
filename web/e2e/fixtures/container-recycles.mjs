import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Isolated UI protocol fixture. Not real Cookie, Agent, PostgreSQL or filesystem evidence.
export async function installContainerRecycleFixture(page, options = {}) {
  const base = options.filePage
    ? await installFileFixture(page, options)
    : await installContainerFilesFixture(page, options)
  const cid = 'a'.repeat(64),
    id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
    execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
  const row = {
    id,
    planningJobId: id,
    executionJobId: null,
    command: null,
    state: 'Queued',
    originalMode: null,
    sizeBytes: null,
    sha256: null,
    attributeCount: null,
    originalUid: null,
    originalGid: null,
    executorUid: null,
    executorGid: null,
    readyExpiresAtUtc: null,
    resultCode: null,
    version: 0,
    receipt: null,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    confirmation: null,
  }
  const state = {
    writes: [],
    reads: [],
    registered: false,
    holdPlan: false,
    holdRecycle: false,
    status: 200,
    canceled: false,
    lostQueue: false,
    lostConfirm: false,
    lostAck: false,
    key: '',
    malformed: false,
    pause: false,
    waiting: false,
    release: () => {},
  }
  function planned() {
    row.state = 'Ready'
    row.originalMode = 33184
    row.sizeBytes = 7
    row.sha256 = 'B'.repeat(64)
    row.attributeCount = 1
    row.originalUid = 1000
    row.originalGid = 1001
    row.executorUid = 5100
    row.executorGid = 5101
    row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
    row.confirmation = {
      planHash: 'isolated-recycle-confirmation',
      expiresAtUtc: row.readyExpiresAtUtc,
      warning:
        '预检不移除文件。确认后再次校验原容器实例、文件版本和属性，保存HDD原件并将原inode移至私有回收区；不彻底删除、不覆盖目标、不处理目录。断线后查询原任务，Unknown保留保护，解除保护不代表回收或恢复成功。恢复须通过独立确认流程。',
    }
    row.resultCode = 'CONTAINER_RECYCLE_READY'
    row.version++
  }
  function receipt(state) {
    const verified = ['Trashed', 'Restored'].includes(state)
    return {
      id: execution,
      containerId: cid,
      instanceId: row.command.location.instanceId,
      fingerprint: 'D'.repeat(64),
      targetProof: 'C'.repeat(64),
      state,
      bindingCurrent: verified,
      replayed: false,
      historyMayExist: state !== 'Aborted',
      historyVerified: verified,
      historyDigest: verified ? 'E'.repeat(64) : '',
      historyAvailable: verified,
      sourceRetained: state === 'Unknown' ? null : state === 'Trashed',
      sourceRestored: state === 'Unknown' ? null : state === 'Restored',
    }
  }
  const response = () => structuredClone(row)
  const getResponse = () => {
    if (['Queued', 'Planning'].includes(row.state)) {
      if (state.holdPlan) {
        if (row.state !== 'Planning') {
          row.state = 'Planning'
          row.version++
        }
      } else planned()
    } else if (['Pending', 'Applying'].includes(row.state)) {
      if (state.canceled) {
        row.state = 'Canceled'
        row.receipt = receipt('Aborted')
        row.resultCode = 'CONTAINER_RECYCLE_CANCELED'
        row.version++
      } else if (options.unknown) {
        row.state = 'Unknown'
        row.historyMayExist = true
        row.receipt = receipt('Unknown')
        row.resultCode = 'CONTAINER_RECYCLE_RESULT_UNKNOWN'
        row.version++
      } else if (state.holdRecycle) {
        if (row.state !== 'Applying') {
          row.state = 'Applying'
          row.version++
        }
      } else {
        row.state = options.superseded ? 'Superseded' : 'Succeeded'
        row.receipt = receipt(options.superseded ? 'Restored' : 'Trashed')
        row.historyMayExist = true
        row.historyVerified = true
        row.historyDigest = 'E'.repeat(64)
        row.resultCode = 'CONTAINER_RECYCLE_TRASHED'
        row.version++
      }
    }
    const result = response()
    if (state.malformed) result.sizeBytes = -1
    return result
  }
  const routeRecycle = async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname.split('/recycles')[1] || ''
    if (request.method() === 'GET') {
      state.reads.push(path)
      if (state.status !== 200)
        return route.fulfill({ status: state.status, json: { status: state.status } })
      if (!state.registered || (path.startsWith('/by-key/') && path !== '/by-key/' + state.key))
        return route.fulfill({ status: 404, json: { status: 404 } })
      if (state.pause) {
        state.waiting = true
        await new Promise((resolve) => {
          state.release = resolve
        })
      }
      try {
        return await route.fulfill({ json: getResponse() })
      } catch {
        return
      }
    }
    const body = request.postDataJSON()
    state.writes.push({ path, body, key: request.headers()['idempotency-key'] })
    if (path === '') {
      state.registered = true
      state.key = request.headers()['idempotency-key']
      row.command = {
        location: { containerId: cid, instanceId: body.instanceId, path: body.path },
        expectedVersion: body.expectedVersion,
      }
      if (options.loseQueue && !state.lostQueue) {
        state.lostQueue = true
        return route.abort('failed')
      }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution
      row.state = 'Pending'
      row.confirmation = null
      row.version++
      row.resultCode = 'CONTAINER_RECYCLE_ACCEPTED'
      if (options.loseConfirm && !state.lostConfirm) {
        state.lostConfirm = true
        return route.abort('failed')
      }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (!row.executionJobId) {
        row.state = 'Canceled'
        row.confirmation = null
        row.resultCode = 'CONTAINER_RECYCLE_CANCELED'
      }
      row.version++
      return route.fulfill({ status: 202, json: response() })
    }
    return route.fulfill({ status: 404, json: { status: 404 } })
  }
  await page.route('**/api/v1/containers/*/files/recycles**', routeRecycle)
  if (options.filePage) {
    row.command = {
      location: {
        containerId: cid,
        instanceId: '777:2026-10-28T01:02:03.123456789Z',
        path: 'work/old.txt',
      },
      expectedVersion: 'A'.repeat(64),
    }
    planned()
    row.executionJobId = execution
    row.confirmation = null
    row.state = 'Unknown'
    row.historyMayExist = true
    row.receipt = receipt('Unknown')
    row.resultCode = 'CONTAINER_RECYCLE_RESULT_UNKNOWN'
    await page.route('**/api/v1/files/container-recycles/recovery**', async (route) => {
      const request = route.request(),
        path = new URL(request.url()).pathname.split('/recovery')[1] || ''
      if (request.method() === 'GET') {
        state.reads.push('recovery' + path)
        if (state.status !== 200)
          return route.fulfill({ status: state.status, json: { status: state.status } })
        return route.fulfill({
          json: path ? response() : row.state === 'Unknown' ? [response()] : [],
        })
      }
      state.writes.push({
        path: 'recovery' + path,
        body: request.postDataJSON(),
        key: request.headers()['idempotency-key'],
      })
      row.state = 'Acknowledged'
      row.version++
      row.resultCode = 'CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED'
      if (options.loseAck && !state.lostAck) {
        state.lostAck = true
        return route.abort('failed')
      }
      return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
    })
  }
  return { base, state, row, cid, id, execution }
}
