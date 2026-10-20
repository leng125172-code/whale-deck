import { installTreeRecyclingFixture } from './tree-recyclings.mjs'

// Browser protocol fixture only: no real identity, SQL, Worker, Agent, backup or disk evidence.
export async function installTreeOverwriteRestoringFixture(page, options = {}) {
  const catalogue = await installTreeRecyclingFixture(page, options)
  const root = {
    ...catalogue.root,
    actions: [...catalogue.root.actions, 'files-tree-restore', 'files-tree-overwrite-restore'],
  }
  if (options.noGrant)
    root.actions = root.actions.filter((action) => action !== 'files-tree-overwrite-restore')
  await page.route('**/api/v1/files/roots', (route) => route.fulfill({ json: [root] }))
  const target = {
    name: '现有目标-abcdefghijklmnopqrstuvwxyz-0123456789',
    kind: 'Directory',
    sizeBytes: 0,
    modifiedAtUnixMs: 1792150000000,
    version: 'B'.repeat(64),
    mode: '0700',
    isProtected: !!options.protectedTarget,
  }
  const entryState = { reads: 0, target }
  await page.route(/\/api\/v1\/files\/entries(?:\?|$)/, (route) => {
    entryState.reads++
    return route.fulfill({
      json: { entries: [target, catalogue.files.entry], nextCursor: '', version: 'F'.repeat(64) },
    })
  })
  catalogue.records[0].kind = options.captureKind || 'Trash'
  catalogue.records[0].contentSha256 = options.captureKind === 'Overwrite' ? 'E'.repeat(64) : null
  const id = '972ace59-c6aa-4cab-a237-8796f8fd81ef',
    execution = '8572fa55-d18e-4ac2-a392-513b15bdc4ef'
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
  // Exactly the 24 public DTO fields; no historical owner, signature or host-path authority.
  const row = {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Queued',
    command: null,
    entryCount: null,
    totalBytes: null,
    targetEntryCount: null,
    targetTotalBytes: null,
    readyExpiresAtUtc: null,
    resultCode: null,
    version: 0,
    publishedVersion: null,
    partialRetained: false,
    confirmation: null,
    archiveBytes: null,
    archiveSha256: null,
    overwrittenCaptureId: null,
    backupMayBeRetained: false,
    backupVerified: false,
    originalsRetained: false,
    targetMayHaveChanged: false,
    backupArchiveBytes: null,
    backupArchiveSha256: null,
  }
  function backup() {
    Object.assign(row, {
      overwrittenCaptureId: execution,
      backupMayBeRetained: true,
      backupVerified: true,
      targetMayHaveChanged: true,
      backupArchiveBytes: 2099200,
      backupArchiveSha256: 'A'.repeat(64),
    })
  }
  await page.route('**/api/v1/files/tree-overwrite-restorings**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.replace('/api/v1/files/tree-overwrite-restorings', '')
    const reply = (json = row, status = 200) => route.fulfill({ status, json })
    const body = request.method() === 'GET' ? null : request.postDataJSON()
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
        Object.keys(body).sort().join(',') !== 'captureId,target,targetExpectedVersion' ||
        Object.keys(body.target || {})
          .sort()
          .join(',') !== 'path,rootId'
      )
        return reply({ status: 400 }, 400)
      const capture = catalogue.records.find((item) => item.id === body.captureId)
      if (!capture) return reply({ status: 404 }, 404)
      if (
        body.target.rootId !== root.id ||
        body.target.path !== target.name ||
        body.targetExpectedVersion !== target.version
      )
        return reply({ status: 409, code: 'FILES_VERSION_CONFLICT' }, 409)
      state.key = request.headers()['idempotency-key']
      if (!/^[a-f0-9-]{36}$/.test(state.key || '')) return reply({ status: 400 }, 400)
      row.command = {
        ...body,
        source: { ...capture.source.source },
        captureKind: capture.kind || 'Trash',
      }
      if (options.loseCreateResponse && !state.lostCreate) {
        state.lostCreate = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }
    if (path === '/' + id + '/confirm' && request.method() === 'POST') {
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
      row.state = options.unknown ? 'Unknown' : 'OverwriteRestoringQueued'
      row.confirmation = null
      row.version++
      if (options.unknown) {
        backup()
        row.partialRetained = true
        row.resultCode = 'FILES_RESULT_UNKNOWN'
      }
      if (options.loseConfirmResponse && !state.lostConfirm) {
        state.lostConfirm = true
        return route.abort('failed')
      }
      return reply(row, 202)
    }
    if (path === '/' + id + '/cancel' && request.method() === 'POST') {
      if (!['Unknown', 'Succeeded'].includes(row.state)) {
        row.state = 'Canceled'
        row.resultCode = 'FILES_CANCELED'
      }
      row.confirmation = null
      row.version++
      return reply(row, 202)
    }
    if (path === '/' + id + '/acknowledge' && request.method() === 'POST') {
      if (row.state !== 'Unknown' || body?.confirmed !== true || body.version !== row.version)
        return reply({ status: 409 }, 409)
      row.state = 'Acknowledged'
      row.resultCode = 'FILES_UNKNOWN_ACKNOWLEDGED'
      row.version++
      return reply({ id, state: row.state }, 202)
    }
    if (
      request.method() === 'GET' &&
      row.command &&
      (path === '/' + id || path === '/by-key/' + state.key)
    ) {
      if (['Queued', 'Planning'].includes(row.state)) {
        if (options.targetChanged || options.protectedPlan) {
          row.state = 'Failed'
          row.resultCode = options.targetChanged ? 'FILES_VERSION_CONFLICT' : 'FILES_TREE_PROTECTED'
          row.version++
        } else if (state.holdPlan) row.state = 'Planning'
        else {
          const capture = catalogue.records.find((item) => item.id === row.command.captureId)
          Object.assign(row, {
            state: 'Ready',
            entryCount: capture.entryCount,
            totalBytes: capture.totalBytes,
            targetEntryCount: 3,
            targetTotalBytes: 2097159,
            archiveBytes: capture.archiveBytes,
            archiveSha256: capture.archiveSha256,
            readyExpiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          })
          row.version++
          row.confirmation = {
            planHash: 'fixture-independent-overwrite-plan',
            expiresAtUtc: row.readyExpiresAtUtc,
            warning: '已核验历史归档与现有目录，尚未覆盖；确认后先备份并验证当前目标。',
          }
        }
      } else if (row.state === 'OverwriteRestoringQueued' && !state.holdRestoring) {
        backup()
        row.state = 'Succeeded'
        row.originalsRetained = true
        row.publishedVersion = 'F'.repeat(64)
        row.resultCode = 'FILES_TREE_OVERWRITE_RESTORE_COMPLETED'
        row.version++
      }
      return reply(row)
    }
    return reply({ status: 404 }, 404)
  })
  return { catalogue, root, row, state, target, entryState }
}
