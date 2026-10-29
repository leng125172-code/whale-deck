import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Isolated UI protocol fixture. Not real Cookie, Agent, PostgreSQL or filesystem evidence.
export async function installContainerCopyFixture(page, options = {}) {
  const base = options.filePage ? await installFileFixture(page, options) : await installContainerFilesFixture(page, options)
  const cid = 'a'.repeat(64), id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12', execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
  const row = { id, planningJobId: id, executionJobId: null, command: null, state: 'Queued', kind: null, entryCount: null, totalBytes: null,
    readyExpiresAtUtc: null, resultCode: null, version: 0, publishedVersion: null, privateMayExist: false, confirmation: null }
  const state = { writes: [], reads: [], registered: false, holdPlan: false, holdCopy: false, status: 200, canceled: false, lostQueue: false,
    lostConfirm: false, lostAck: false, key: '', malformed: false, pause: false, waiting: false, release: () => {} }
  function planned() {
    row.state = 'Ready'; row.kind = row.command.source.path.endsWith('资料') ? 'Directory' : 'File'; row.entryCount = row.kind === 'Directory' ? 4 : 1
    row.totalBytes = row.kind === 'Directory' ? 1073741831 : 3; row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
    row.confirmation = { planHash: 'isolated-copy-confirmation', expiresAtUtc: row.readyExpiresAtUtc, warning: '预检未复制数据；目标必须不存在，源保留。' }
    row.resultCode = 'CONTAINER_COPY_READY'; row.version++
  }
  const response = () => structuredClone(row)
  const getResponse = () => {
    if (['Queued', 'Planning'].includes(row.state)) {
      if (state.holdPlan) { if (row.state !== 'Planning') { row.state = 'Planning'; row.version++ } }
      else planned()
    } else if (['Pending', 'Applying'].includes(row.state)) {
      if (state.canceled) { row.state = 'Canceled'; row.resultCode = 'CONTAINER_COPY_CANCELED'; row.version++ }
      else if (options.unknown) { row.state = 'Unknown'; row.privateMayExist = true; row.resultCode = 'CONTAINER_COPY_RESULT_UNKNOWN'; row.version++ }
      else if (state.holdCopy) { if (row.state !== 'Applying') { row.state = 'Applying'; row.version++ } }
      else { row.state = options.superseded ? 'Superseded' : 'Succeeded'; row.publishedVersion = 'F'.repeat(64); row.resultCode = 'CONTAINER_COPY_COMPLETED'; row.version++ }
    }
    const result = response()
    if (state.malformed) result.publishedVersion = null
    return result
  }
  const routeCopy = async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname.split('/copies')[1] || ''
    if (request.method() === 'GET') {
      state.reads.push(path)
      if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
      if (!state.registered || path.startsWith('/by-key/') && path !== '/by-key/' + state.key) return route.fulfill({ status: 404, json: { status: 404 } })
      if (state.pause) { state.waiting = true; await new Promise(resolve => { state.release = resolve }) }
      try { return await route.fulfill({ json: getResponse() }) } catch { return }
    }
    const body = request.postDataJSON()
    state.writes.push({ path, body, key: request.headers()['idempotency-key'] })
    if (path === '') {
      state.registered = true; state.key = request.headers()['idempotency-key']
      row.command = { source: { containerId: cid, instanceId: body.instanceId, path: body.sourcePath }, targetPath: body.targetPath, expectedVersion: body.expectedVersion }
      if (options.loseQueue && !state.lostQueue) { state.lostQueue = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution; row.state = 'Pending'; row.confirmation = null; row.version++; row.resultCode = 'CONTAINER_COPY_ACCEPTED'
      if (options.loseConfirm && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (!row.executionJobId) { row.state = 'Canceled'; row.confirmation = null; row.resultCode = 'CONTAINER_COPY_CANCELED' }
      row.version++; return route.fulfill({ status: 202, json: response() })
    }
    return route.fulfill({ status: 404, json: { status: 404 } })
  }
  await page.route('**/api/v1/containers/*/files/copies**', routeCopy)
  if (options.filePage) {
    row.command = { source: { containerId: cid, instanceId: '777:2026-10-28T01:02:03.123456789Z', path: 'work/old.txt' }, targetPath: 'work/copy.txt', expectedVersion: 'A'.repeat(64) }
    planned(); row.executionJobId = execution; row.confirmation = null; row.state = 'Unknown'; row.privateMayExist = true; row.resultCode = 'CONTAINER_COPY_RESULT_UNKNOWN'
    await page.route('**/api/v1/files/container-copies/recovery**', async route => {
      const request = route.request(), path = new URL(request.url()).pathname.split('/recovery')[1] || ''
      if (request.method() === 'GET') {
        state.reads.push('recovery' + path)
        if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
        return route.fulfill({ json: path ? response() : row.state === 'Unknown' ? [response()] : [] })
      }
      state.writes.push({ path: 'recovery' + path, body: request.postDataJSON(), key: request.headers()['idempotency-key'] })
      row.state = 'Acknowledged'; row.version++; row.resultCode = 'CONTAINER_COPY_UNKNOWN_ACKNOWLEDGED'
      if (options.loseAck && !state.lostAck) { state.lostAck = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
    })
  }
  return { base, state, row, cid, id, execution }
}
