import { createHash } from 'node:crypto'
import { Buffer } from 'node:buffer'
import { installTerminalFixture } from './terminal.mjs'

// UI protocol fixture only. Does not prove PostgreSQL, Worker or host file I/O.
export async function installFileFixture(page, options = {}) {
  await installTerminalFixture(page, options)
  const state = { writes: [], rootStatus: 200, entryStatus: 200, reads: 0, bodyCount: 0, accepted: false, hold: options.hold || false, unknown: options.unknown || false }
  const root = { id: 'whaledeck.files.shared', name: '共享文件', actions: ['files-list', 'files-stat', 'files-upload', 'files-download'] }
  const entry = { name: '一个很长但应该省略并能显示完整提示的文件名称-abcdefghijklmnopqrstuvwxyz-0123456789.txt', kind: 'File', sizeBytes: 3, modifiedAtUnixMs: 1792150000000, version: 'A'.repeat(64), mode: '0600', isProtected: false }
  const folder = { ...entry, name: '资料', kind: 'Directory', sizeBytes: 0 }
  const intent = { id: '7d4fec0a-01b7-494a-9b55-e96e265503a1', jobId: '7d4fec0a-01b7-494a-9b55-e96e265503a1', rootId: root.id, path: '', sizeBytes: 0, sha256: '', state: 'AwaitingUpload', expiresAtUtc: new Date(Date.now() + 900000).toISOString(), version: 0, resultCode: null, publishedVersion: null, previousRetained: false }
  let metadata
  await page.route('**/api/v1/files/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname.replace('/api/v1/files/', '')
    const reply = (json, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(json) })
    if (request.method() !== 'GET') state.writes.push({ path, method: request.method(), headers: request.headers(), body: request.method() === 'PUT' ? null : request.postDataJSON() })
    if (path === 'roots') return reply(state.rootStatus === 200 ? [root] : { status: state.rootStatus }, state.rootStatus)
    if (path === 'entries') {
      if (state.entryStatus !== 200) return reply({ status: state.entryStatus, code: 'AUTH_FORBIDDEN', traceId: 'file-fixture-trace' }, state.entryStatus)
      return reply({ entries: url.searchParams.get('path') ? [] : [folder, entry, { ...entry, name: '.env', isProtected: true }, ...(intent.state === 'Succeeded' ? [{ ...entry, name: intent.path, sizeBytes: intent.sizeBytes }] : [])], nextCursor: '', version: 'F'.repeat(64) })
    }
    if (path === 'entry') return reply({ status: 404 }, 404)
    if (path === 'uploads/plan') { metadata = request.postDataJSON(); return reply({ planHash: 'ui-fixture-signed-plan', expiresAtUtc: new Date(Date.now() + 300000).toISOString(), overwrites: false, warning: '先暂存校验，再由任务发布。' }) }
    if (path === 'uploads') {
      Object.assign(intent, { path: metadata.path, sizeBytes: metadata.sizeBytes, sha256: metadata.sha256 })
      const prior = state.accepted; state.accepted = true
      if (options.loseCreateResponse && !prior) return route.abort('failed')
      return reply(intent, 202)
    }
    if (path.endsWith('/content')) {
      state.bodyCount++; const body = request.postDataBuffer()
      if (!body || createHash('sha256').update(body).digest('hex').toUpperCase() !== intent.sha256 || body.length !== intent.sizeBytes) return reply({ status: 400 }, 400)
      intent.state = 'Ready'; return reply(intent, 202)
    }
    if (path.endsWith('/cancel')) { if (intent.state !== 'Unknown') intent.state = 'Canceled'; return reply({ jobId: intent.id, state: 'CancelRequested' }, 202) }
    if (path.startsWith('uploads/')) {
      state.reads++
      if (intent.state === 'Ready' && !state.hold) intent.state = state.unknown ? 'Unknown' : state.reads > 1 ? 'Succeeded' : 'Publishing'
      else if (intent.state === 'Publishing' && !state.hold) intent.state = 'Succeeded'
      return reply(intent)
    }
    if (path === 'download') return route.fulfill({ status: 200, headers: { 'Content-Type': 'application/octet-stream', 'Content-Length': '3' }, body: Buffer.from([1, 2, 3]) })
    return reply({ status: 404 }, 404)
  })
  return { state, entry, intent }
}
