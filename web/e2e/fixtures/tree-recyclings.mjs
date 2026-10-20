import { installFileFixture } from './files.mjs'

// UI contract fixtures only, never a workstation/Agent/HDD assertion.
export async function installTreeRecyclingFixture(page, options = {}) {
  const files = await installFileFixture(page, { ...options, mutations: true, directoryName: options.directoryName || '资料' })
  const root = { id: 'whaledeck.files.shared', name: '共享文件', actions: ['files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive', 'files-tree-copy', 'files-tree-move', 'files-tree-pack', 'files-tree-unpack', 'files-tree-trash'] }
  if (options.noGrant) root.actions = root.actions.filter(value => value !== 'files-tree-trash')
  await page.route('**/api/v1/files/roots', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([root]) }))
  const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974', execution = '129f14ed-3254-4868-9885-acad522acefa'
  const state = { writes: [], reads: [], captureReads: [], key: '', holdPlan: !!options.holdPlan, holdRecycling: !!options.holdRecycling, lostCreate: false, lostConfirm: false, missing: false, status: 200, captureBad: false }
  const row = { id, planningJobId: id, executionJobId: null, state: 'Queued', command: null, entryCount: null, totalBytes: null, readyExpiresAtUtc: null, resultCode: null, version: 0,
    captureId: null, archiveBytes: null, archiveSha256: null, captureVerified: false, captureMayBeRetained: false, sourceMayHaveChanged: false, sourceRemoved: false, originalsRetained: false, confirmation: null }
  const records = [
    { id: execution, intentId: id, source: { source: { rootId: root.id, path: '很长的中文目录/<img src=x onerror=alert(1)>/abcdefghijklmnopqrstuvwxyz-0123456789' }, expectedVersion: 'A'.repeat(64) }, manifestSha256: 'C'.repeat(64), entryCount: 10000, totalBytes: 1073741831, archiveBytes: 1073756160, archiveSha256: 'D'.repeat(64), createdAtUtc: '2026-10-20T01:00:00Z', archiveState: 'Retained', originalsState: 'Retained', cleanupActive: false },
    { id: '12ca4fcb-8a2c-4867-9950-8e0cab11400a', intentId: 'eb3131c3-d685-422d-8e1b-139a335b22a7', source: { source: { rootId: root.id, path: '另一份目录' }, expectedVersion: 'B'.repeat(64) }, manifestSha256: 'E'.repeat(64), entryCount: 3, totalBytes: 4096, archiveBytes: 1234, archiveSha256: 'F'.repeat(64), createdAtUtc: '2026-10-20T02:00:00Z', archiveState: 'Retained', originalsState: 'Retained', cleanupActive: false },
  ]
  await page.route('**/api/v1/files/tree-recyclings**', async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname.replace('/api/v1/files/tree-recyclings', '')
    const reply = (value = row, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (request.method() !== 'GET') state.writes.push({ path, body: request.postDataJSON(), headers: request.headers() })
    else state.reads.push(path)
    if (state.status !== 200) return reply({ status: state.status, code: 'AUTH_FORBIDDEN', title: 'Fixture access rejected' }, state.status)
    if (path === '/captures') {
      state.captureReads.push(Object.fromEntries(url.searchParams))
      const filtered = records.filter(item => item.source.source.rootId === url.searchParams.get('rootId') && (!url.searchParams.get('path') || item.source.source.path === url.searchParams.get('path')))
      const start = url.searchParams.get('after') ? filtered.findIndex(item => item.id === url.searchParams.get('after')) + 1 : 0
      const items = structuredClone(filtered.slice(start, start + 1))
      if (state.captureBad && items[0]) items[0].archiveSha256 = 'invalid-proof'
      return reply({ items, next: filtered.length > start + 1 ? items[0].id : null })
    }
    if (state.missing && request.method() === 'GET') return reply({ title: 'not found', status: 404 }, 404)
    if (path === '' && request.method() === 'POST') {
      state.key = request.headers()['idempotency-key']; row.command = request.postDataJSON()
      if (options.loseCreateResponse && !state.lostCreate) { state.lostCreate = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution; row.state = options.unknown ? 'Unknown' : 'RecyclingQueued'; row.confirmation = null; row.version++
      row.captureMayBeRetained = null; row.sourceMayHaveChanged = null; row.sourceRemoved = null; row.originalsRetained = null
      if (options.loseConfirmResponse && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return reply(row, 202)
    }
    if (path.endsWith('/cancel')) { if (!['Unknown', 'Succeeded'].includes(row.state)) { row.state = 'Canceled'; row.captureMayBeRetained = false; row.sourceMayHaveChanged = false; row.sourceRemoved = false; row.originalsRetained = false } row.confirmation = null; row.version++; return reply(row, 202) }
    if (path.endsWith('/acknowledge')) { row.state = 'Acknowledged'; row.version++; return reply({ id, state: row.state }, 202) }
    if (request.method() === 'GET' && row.command) {
      if (path.startsWith('/by-key/') && path !== '/by-key/' + state.key) return reply({ status: 404 }, 404)
      if (['Queued', 'Planning'].includes(row.state)) {
        if (options.protectedPlan) { row.state = 'Failed'; row.resultCode = 'FILES_TREE_PROTECTED'; row.version++ }
        else if (state.holdPlan) row.state = 'Planning'
        else { row.state = 'Ready'; row.entryCount = 4; row.totalBytes = 1073741831; row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString(); row.version++
          row.confirmation = { planHash: 'ui-tree-plan-not-an-auth-token', expiresAtUtc: row.readyExpiresAtUtc, warning: '完整清单已生成，尚未回收。确认后先验证归档，再保留原件。' } }
      } else if (row.state === 'RecyclingQueued' && !state.holdRecycling) {
        row.state = 'Succeeded'; row.captureVerified = true; row.captureMayBeRetained = true; row.sourceMayHaveChanged = true; row.sourceRemoved = true; row.originalsRetained = true;
        row.captureId = execution; row.archiveBytes = 1073756160; row.archiveSha256 = 'F'.repeat(64); row.resultCode = 'FILES_TREE_RECYCLE_COMPLETED'; row.version++
      }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })
  return { files, state, row, root, records }
}
