import { installFileFixture } from './files.mjs'

// UI protocol fixture only. Not evidence of SQL/Worker/Agent or host mutations.
export async function installMutationFixture(page, options = {}) {
  const files = await installFileFixture(page, { ...options, mutations: true })
  const state = { writes: [], reads: 0, accepted: false, hold: options.mutationHold || false, planStatus: 200, status: 200, unregisteredExpired: false }
  let plan, committed = false
  const id = 'f940e457-5d45-45fc-a27f-de65b4ee1df0'
  const intent = { id, jobId: id, command: null, sizeBytes: 0, state: 'Pending', expiresAtUtc: new Date(Date.now() + 900000).toISOString(), version: 0, resultCode: null, publishedVersion: null, sourceRetained: false }
  await page.route(/\/api\/v1\/files\/operations(?:\/|$)/, async route => {
    const request = route.request(), path = new URL(request.url()).pathname.replace('/api/v1/files/operations', '')
    const reply = (json, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(json) })
    if (request.method() !== 'GET') state.writes.push({ path, headers: request.headers(), body: request.postDataJSON() })
    if (path === '/plan') {
      plan = request.postDataJSON()
      return reply(state.planStatus === 200 ? { planHash: 'ui-signed-mutation-plan', command: plan, sizeBytes: plan.kind === 'mkdir' ? 0 : 3, expiresAtUtc: new Date(Date.now() + 300000).toISOString(), warning: '不覆盖目标；未知结果不自动重做。私有保留不是HDD备份。' } : { status: state.planStatus, code: 'AGENT_VERSION_CONFLICT' }, state.planStatus)
    }
    if (path === '/resolve') return reply(state.accepted ? intent : null)
    if (path === '') {
      if (!state.accepted) Object.assign(intent, { command: plan, sizeBytes: plan.kind === 'mkdir' ? 0 : 3 })
      if (options.registrationNeverAccepted) return reply({ status: 409, code: 'OPERATION_CONFLICT' }, 409)
      const prior = state.accepted; state.accepted = true
      if (options.mutationLoseResponse && !prior) return route.abort('failed')
      return reply(intent, 202)
    }
    if (path === `/${id}/cancel`) { intent.state = 'Canceled'; return reply({ jobId: id, state: 'CancelRequested' }, 202) }
    if (path === `/${id}/acknowledge`) { intent.state = 'Acknowledged'; intent.version++; return reply({ jobId: id, state: 'Acknowledged' }, 202) }
    if (path === `/${id}`) {
      state.reads++
      if (state.status !== 200) return reply({ status: state.status, code: 'AUTH_FORBIDDEN' }, state.status)
      if (intent.state === 'Pending' && !state.hold) intent.state = options.mutationUnknown ? 'Unknown' : 'Succeeded'
      if (intent.state === 'Succeeded' && !committed) {
        committed = true; intent.publishedVersion = 'E'.repeat(64); intent.sourceRetained = intent.command.kind === 'move'
        if (intent.command.kind === 'chmod') files.entry.mode = intent.command.mode
        else {
          files.state.extraEntries.push({ ...files.entry, name: intent.command.target.path, kind: intent.command.kind === 'mkdir' ? 'Directory' : 'File', mode: intent.command.mode || files.entry.mode })
          files.state.hideEntry = intent.command.kind === 'move'
        }
      }
      return reply(intent)
    }
    return reply({ status: 404 }, 404)
  })
  return { ...files, mutation: state, operation: intent }
}
