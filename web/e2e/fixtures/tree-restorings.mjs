import { installTreeRecyclingFixture } from './tree-recyclings.mjs'

// Browser protocol fixtures only: not real identity/SQL/Agent/filesystem evidence.
export async function installTreeRestoringFixture(page, options = {}) {
  const catalogue = await installTreeRecyclingFixture(page, options)
  const root = { ...catalogue.root, actions: [...catalogue.root.actions, 'files-tree-restore'] }
  if (options.noSourceGrant)
    root.actions = root.actions.filter((action) => action !== 'files-tree-restore')
  if (options.noTargetGrant)
    root.actions = root.actions.filter((action) => action !== 'files-upload')
  await page.route('**/api/v1/files/roots', (route) => route.fulfill({ json: [root] }))
  const id = '4d38a152-4477-4f58-ab07-36cb55eebd62',
    execution = '46e7d92d-4a92-4448-9bdc-e976a8090373'
  const state = {
    writes: [],
    reads: [],
    key: '',
    holdPlan: !!options.holdPlan,
    holdRestoring: !!options.holdRestoring,
    lostCreate: false,
    lostConfirm: false,
    missing: false,
    status: 200,
  }
  const row = {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Queued',
    command: null,
    entryCount: null,
    totalBytes: null,
    archiveBytes: null,
    archiveSha256: null,
    readyExpiresAtUtc: null,
    resultCode: null,
    version: 0,
    publishedVersion: null,
    partialRetained: false,
    confirmation: null,
  }
  await page.route('**/api/v1/files/tree-restorings**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.replace('/api/v1/files/tree-restorings', '')
    const reply = (json = row, status = 200) => route.fulfill({ status, json })
    if (request.method() !== 'GET')
      state.writes.push({ path, body: request.postDataJSON(), headers: request.headers() })
    else state.reads.push(path)
    if (state.status !== 200)
      return reply(
        { status: state.status, code: 'AUTH_FORBIDDEN', title: 'Fixture rejected' },
        state.status,
      )
    if (state.missing && request.method() === 'GET') return reply({ status: 404 }, 404)
    if (path === '' && request.method() === 'POST') {
      const command = request.postDataJSON(),
        capture = catalogue.records.find((item) => item.id === command.captureId)
      if (!capture) return reply({ status: 404 }, 404)
      state.key = request.headers()['idempotency-key']
      row.command = { ...command, source: { ...capture.source.source }, captureKind: 'Trash' }
      if (options.loseCreateResponse && !state.lostCreate) {
        state.lostCreate = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution
      row.state = options.unknown ? 'Unknown' : 'RestoringQueued'
      row.partialRetained = !!options.unknown
      row.confirmation = null
      row.version++
      if (options.loseConfirmResponse && !state.lostConfirm) {
        state.lostConfirm = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }
    if (path.endsWith('/cancel')) {
      if (!['Unknown', 'Succeeded'].includes(row.state)) row.state = 'Canceled'
      row.confirmation = null
      row.version++
      return reply(row, 202)
    }
    if (path.endsWith('/acknowledge')) {
      row.state = 'Acknowledged'
      row.version++
      return reply({ id, state: row.state }, 202)
    }
    if (request.method() === 'GET' && row.command) {
      if (path.startsWith('/by-key/') && path !== '/by-key/' + state.key)
        return reply({ status: 404 }, 404)
      if (['Queued', 'Planning'].includes(row.state)) {
        if (options.protectedPlan) {
          row.state = 'Failed'
          row.resultCode = 'FILES_TREE_PROTECTED'
          row.version++
        } else if (state.holdPlan) row.state = 'Planning'
        else {
          const capture = catalogue.records.find((item) => item.id === row.command.captureId)
          row.state = 'Ready'
          row.entryCount = capture.entryCount
          row.totalBytes = capture.totalBytes
          row.archiveBytes = capture.archiveBytes
          row.archiveSha256 = capture.archiveSha256
          row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
          row.version++
          row.confirmation = {
            planHash: 'fixture-tree-restore-plan',
            expiresAtUtc: row.readyExpiresAtUtc,
            warning: '归档和清单已重新验证，尚未恢复；不会覆盖目标。',
          }
        }
      } else if (row.state === 'RestoringQueued' && !state.holdRestoring) {
        row.state = 'Succeeded'
        row.publishedVersion = 'F'.repeat(64)
        row.resultCode = 'FILES_TREE_RESTORE_COMPLETED'
        row.version++
      }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })
  return { catalogue, state, row, root }
}
