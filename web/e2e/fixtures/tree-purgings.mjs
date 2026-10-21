import { installTreeRecyclingFixture } from './tree-recyclings.mjs'

// Browser protocol fixture only. It proves the public BFF/UI contract, never
// real Authentik, PostgreSQL, Worker, Agent, HDD deletion or recovery.
export async function installTreePurgingFixture(page, options = {}) {
  const catalogue = await installTreeRecyclingFixture(page, options)
  const root = {
    ...catalogue.root,
    actions: [...new Set([...catalogue.root.actions, 'files-tree-purge'])],
  }
  if (options.noGrant) root.actions = root.actions.filter((action) => action !== 'files-tree-purge')
  await page.route('**/api/v1/files/roots', (route) => route.fulfill({ json: [root] }))

  const record = catalogue.records[0]
  record.kind = options.captureKind || 'Trash'
  record.contentSha256 = record.kind === 'Overwrite' ? '9'.repeat(64) : null
  record.archiveState = options.archiveState || 'Retained'
  record.originalsState = options.originalsState || 'Retained'
  record.cleanupActive = !!options.cleanupActive

  const id = '730f56df-ce8f-4984-8d55-794b937c38d2'
  const execution = 'eff2f3c0-cfe7-424e-a9c1-fab9a3302920'
  const state = {
    writes: [],
    reads: [],
    key: '',
    lostCreate: false,
    lostConfirm: false,
    missing: false,
    status: 200,
    holdPlan: !!options.holdPlan,
    holdPurging: !!options.holdPurging,
  }
  // Exactly the 15 public DTO fields. No owner, binding, host path or receipt internals.
  const row = {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Queued',
    command: null,
    entryCount: null,
    totalBytes: null,
    archiveBytes: null,
    readyExpiresAtUtc: null,
    resultCode: null,
    version: 0,
    deletionStarted: false,
    archiveRemoved: false,
    originalsRemoved: false,
    confirmation: null,
  }

  function selectedFacts(value, selectedValue, unselectedValue = false) {
    return {
      archiveRemoved: value === 'Archive' || value === 'All' ? selectedValue : unselectedValue,
      originalsRemoved: value === 'Originals' || value === 'All' ? selectedValue : unselectedValue,
    }
  }
  function markCatalogue(value, componentState, active) {
    if (value === 'Archive' || value === 'All') record.archiveState = componentState
    if (value === 'Originals' || value === 'All') record.originalsState = componentState
    record.cleanupActive = active
  }

  await page.route('**/api/v1/files/tree-purgings**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1/files/tree-purgings', '')
    const reply = (json = row, status = 200) => route.fulfill({ status, json })
    const body = request.postData() ? request.postDataJSON() : null
    if (request.method() === 'GET') state.reads.push(path)
    else {
      state.writes.push({ path, body, headers: request.headers() })
      if (request.headers()['x-xsrf-token'] !== 'ui-fixture-only')
        return reply({ status: 403 }, 403)
    }
    if (state.status !== 200)
      return reply(
        { status: state.status, code: 'AUTH_FORBIDDEN', title: 'Fixture access rejected' },
        state.status,
      )
    if (state.missing && request.method() === 'GET') return reply({ status: 404 }, 404)

    if (path === '' && request.method() === 'POST') {
      if (
        !body ||
        Object.keys(body).sort().join(',') !== 'captureId,scope' ||
        body.captureId !== record.id ||
        !['Archive', 'Originals', 'All'].includes(body.scope)
      )
        return reply({ status: 400 }, 400)
      if (
        (body.scope !== 'Originals' && record.archiveState === 'Purged') ||
        (body.scope !== 'Archive' && record.originalsState === 'Purged') ||
        record.cleanupActive
      )
        return reply({ status: 409, code: 'FILES_CAPTURE_COMPONENT_UNAVAILABLE' }, 409)
      state.key = request.headers()['idempotency-key']
      if (!/^[a-f0-9-]{36}$/.test(state.key || '')) return reply({ status: 400 }, 400)
      row.command = {
        captureId: record.id,
        source: { ...record.source.source },
        captureKind: record.kind || 'Trash',
        scope: body.scope,
      }
      if (options.loseCreateResponse && !state.lostCreate) {
        state.lostCreate = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }

    if (path === `/${id}/confirm` && request.method() === 'POST') {
      if (
        row.state !== 'Ready' ||
        !body ||
        Object.keys(body).sort().join(',') !== 'confirmed,planHash,version' ||
        body.confirmed !== true ||
        body.planHash !== row.confirmation?.planHash ||
        body.version !== row.version
      )
        return reply({ status: 409 }, 409)
      row.executionJobId = execution
      row.confirmation = null
      row.version++
      if (options.unknown) {
        row.state = 'Unknown'
        row.resultCode = 'FILES_RESULT_UNKNOWN'
        row.deletionStarted = true
        Object.assign(row, selectedFacts(row.command.scope, null))
        if (row.command.scope === 'Archive' || row.command.scope === 'All')
          row.archiveRemoved = true
        else row.originalsRemoved = true
        markCatalogue(row.command.scope, 'Uncertain', true)
      } else {
        row.state = 'PurgingQueued'
        row.deletionStarted = null
        Object.assign(row, selectedFacts(row.command.scope, null))
        markCatalogue(row.command.scope, 'Uncertain', true)
      }
      if (options.loseConfirmResponse && !state.lostConfirm) {
        state.lostConfirm = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }

    if (path === `/${id}/cancel` && request.method() === 'POST') {
      if (!['Unknown', 'Succeeded', 'Acknowledged'].includes(row.state)) {
        row.state = 'Canceled'
        row.resultCode = 'FILES_CANCELED'
        row.deletionStarted = false
        Object.assign(row, selectedFacts(row.command.scope, false))
        markCatalogue(row.command.scope, 'Retained', false)
      }
      row.confirmation = null
      row.version++
      return reply(row, 202)
    }

    if (path === `/${id}/acknowledge` && request.method() === 'POST') {
      if (row.state !== 'Unknown' || body?.confirmed !== true || body.version !== row.version)
        return reply({ status: 409 }, 409)
      row.state = 'Acknowledged'
      row.resultCode = 'FILES_UNKNOWN_ACKNOWLEDGED'
      row.version++
      record.cleanupActive = false
      return reply({ id, state: row.state }, 202)
    }

    if (
      request.method() === 'GET' &&
      row.command &&
      (path === `/${id}` || path === `/by-key/${state.key}`)
    ) {
      if (['Queued', 'Planning'].includes(row.state)) {
        if (state.holdPlan) row.state = 'Planning'
        else {
          row.state = 'Ready'
          row.entryCount = record.entryCount
          row.totalBytes = record.totalBytes
          row.archiveBytes = record.archiveBytes
          row.readyExpiresAtUtc = new Date(
            Date.now() + (options.expired ? -60_000 : 300_000),
          ).toISOString()
          row.version++
          row.confirmation = {
            planHash: 'fixture-tree-purge-plan',
            expiresAtUtc: row.readyExpiresAtUtc,
            warning: '所选组件已经核实，尚未删除；永久清理不可撤销。',
          }
        }
      } else if (row.state === 'PurgingQueued' && !state.holdPurging) {
        row.state = 'Succeeded'
        row.resultCode = 'FILES_TREE_PURGE_COMPLETED'
        row.deletionStarted = true
        Object.assign(row, selectedFacts(row.command.scope, true))
        row.version++
        markCatalogue(row.command.scope, 'Purged', false)
      }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })

  return { catalogue, root, record, row, state, execution }
}
