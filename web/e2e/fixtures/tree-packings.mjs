import { createHash } from 'node:crypto'
import { installFileFixture } from './files.mjs'

// Explicit UI protocol fixture only. Native SQL and installed UDS/I/O have separate proofs.
export async function installTreePackingFixture(page, options = {}) {
  const payload = Buffer.from('packing-fixture-not-a-real-tar')
  const digest = createHash('sha256').update(payload).digest('hex').toUpperCase()
  const files = await installFileFixture(page, { ...options, mutations: true })
  const root = { id: 'whaledeck.files.shared', name: '共享文件', actions: ['files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-tree-pack', 'files-archive'] }
  await page.route('**/api/v1/files/roots', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([root]) }))
  const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974', execution = '129f14ed-3254-4868-9885-acad522acefa'
  const state = { writes: [], reads: [], key: '', holdPlan: options.holdPlan || false, holdPacking: options.holdPacking || false, lostCreate: false, lostConfirm: false, missing: false, status: 200, downloads: 0 }
  const row = { id, planningJobId: id, executionJobId: null, state: 'Queued', command: null, entryCount: null, totalBytes: null, readyExpiresAtUtc: null, resultCode: null,
    version: 0, publishedVersion: null, partialRetained: false, confirmation: null, archiveBytes: null, archiveSha256: null }
  await page.route('**/api/v1/files/tree-packings**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname.replace('/api/v1/files/tree-packings', '')
    const reply = (value = row, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (request.method() !== 'GET') state.writes.push({ path, body: request.postDataJSON(), headers: request.headers() })
    else state.reads.push(path)
    if (state.status !== 200) return reply({ status: state.status, code: 'AUTH_FORBIDDEN', title: 'Fixture access rejected' }, state.status)
    if (state.missing && request.method() === 'GET') return reply({ title: 'not found', status: 404 }, 404)
    if (path === '' && request.method() === 'POST') {
      state.key = request.headers()['idempotency-key']; row.command = request.postDataJSON()
      if (options.loseCreateResponse && !state.lostCreate) { state.lostCreate = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution; row.state = options.unknown ? 'Unknown' : 'PackingQueued'; row.confirmation = null; row.version++; row.partialRetained = !!options.unknown
      if (options.loseConfirmResponse && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/cancel')) { if (row.state !== 'Unknown' && row.state !== 'Succeeded') row.state = 'Canceled'; row.confirmation = null; row.version++; return reply(row, 202) }
    if (path.endsWith('/acknowledge')) { row.state = 'Acknowledged'; row.version++; return reply({ id, state: row.state }, 202) }
    if (request.method() === 'GET' && row.command) {
      if (path.startsWith('/by-key/') && path !== '/by-key/' + state.key) return reply({ status: 404 }, 404)
      if (row.state === 'Queued' || row.state === 'Planning') {
        if (options.protectedPlan) { row.state = 'Failed'; row.resultCode = 'FILES_TREE_PROTECTED'; row.version++ }
        else if (state.holdPlan) row.state = 'Planning'
        else {
          row.state = 'Ready'; row.entryCount = 4; row.totalBytes = 1073741831; row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString(); row.version++
          row.confirmation = { planHash: 'ui-tree-plan-not-auth-token', expiresAtUtc: row.readyExpiresAtUtc, warning: '完整清单已生成，尚未压缩；源保留，目标必须不存在。' }
        }
      } else if (row.state === 'PackingQueued' && !state.holdPacking) { row.state = 'Succeeded'; row.publishedVersion = 'F'.repeat(64); row.archiveBytes = payload.length; row.archiveSha256 = digest; row.version++ }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })
  await page.context().route('**/api/v1/files/tree-packings/*/download?**', async route => {
    state.downloads++
    const version = new URL(route.request().url()).searchParams.get('expectedVersion')
    if (state.status !== 200 || row.state !== 'Succeeded' || version !== row.publishedVersion)
      return route.fulfill({ status: 403, body: 'denied' })
    if (options.downloadUrl) return route.fulfill({ status: 302, headers: { Location: options.downloadUrl } })
    return route.fulfill({ status: 200, headers: { 'Content-Type': 'application/octet-stream',
      'Content-Disposition': 'attachment; filename="fixture.tar.gz"', 'Content-Length': String(payload.length) }, body: payload })
  })
  return { files, state, row, root, payload, digest }
}
