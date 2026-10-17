import { randomUUID } from 'node:crypto'
import { installFileFixture } from './files.mjs'

// Browser protocol fixture only. No SQL, Worker, installed Agent, physical HDD,
// OIDC authorization or real filesystem behavior is proven by these replies.
export async function installArchiveFixture(page, options = {}) {
  const files = await installFileFixture(page, { ...options, mutations: true })
  const root = {
    id: 'whaledeck.files.shared',
    name: '共享文件',
    actions: [
      'files-list',
      'files-stat',
      'files-download',
      'files-history',
      ...(options.readOnly ? [] : ['files-upload', 'files-mutate', 'files-archive']),
    ],
  }
  const state = {
    writes: [],
    lists: [],
    registrations: [],
    reads: 0,
    accepted: 0,
    status: 200,
    planStatus: 200,
    historyStatus: 200,
    historyEmpty: false,
    invalid: 0,
    paged: false,
    hold: !!options.hold,
    unknown: !!options.unknown,
    planDelayMs: 0,
    registrationState: null,
  }
  const versions = [
    {
      id: '11111111-1111-4111-8111-111111111111',
      rootId: root.id,
      path: files.entry.name,
      sourceVersion: 'A'.repeat(64),
      sizeBytes: 2097167,
      sha256: 'B'.repeat(64),
      capturedAtUtc: '2026-10-17T04:00:00Z',
      captureKind: 'Trash',
      captureState: 'Trashed',
      availability: 'Present',
      differentSourceDevice: false,
    },
    ...['Missing', 'Invalid', 'Incomplete', 'Purged'].map((availability, index) => ({
      id: `${index + 2}`.repeat(8) + '-1111-4111-8111-111111111111',
      rootId: root.id,
      path: `状态-${availability}.txt`,
      sourceVersion: 'C'.repeat(64),
      sizeBytes: 7,
      sha256: 'D'.repeat(64),
      capturedAtUtc: '2026-10-17T03:00:00Z',
      captureKind: 'Overwrite',
      captureState: availability === 'Purged' ? 'Purged' : 'Available',
      availability,
      differentSourceDevice: true,
    })),
  ]
  const plans = new Map(),
    operations = new Map(),
    applied = new Set()
  const replyFor =
    (route) =>
    (value, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
  await page.route('**/api/v1/files/roots', (route) => replyFor(route)([root]))
  await page.route('**/api/v1/files/entry?**', (route) =>
    options.existingTarget ? replyFor(route)(files.entry) : replyFor(route)({ status: 404 }, 404),
  )
  await page.route(/\/api\/v1\/files\/archives(?:[/?]|$)/, async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname.replace('/api/v1/files/archives', ''),
      reply = replyFor(route)
    if (request.method() !== 'GET')
      state.writes.push({ path, headers: request.headers(), body: request.postDataJSON() })
    if (path === '' && request.method() === 'GET') {
      state.lists.push(Object.fromEntries(url.searchParams))
      if (state.historyStatus !== 200)
        return reply(
          { status: state.historyStatus, code: 'AGENT_VERSION_CONFLICT' },
          state.historyStatus,
        )
      const after = url.searchParams.get('after') || ''
      const pathFilter = url.searchParams.get('path') || '',
        filtered = versions.filter((item) => !pathFilter || item.path === pathFilter)
      return reply({
        entries: state.historyEmpty
          ? []
          : state.paged
            ? after
              ? filtered.slice(1)
              : filtered.slice(0, 1)
            : filtered,
        nextCursor: state.paged && !after ? versions[0].id : '',
        version: 'F'.repeat(64),
        scanned: versions.length,
        invalidRecords: state.invalid,
        complete: !state.paged || !!after,
      })
    }
    if (path === '/plan') {
      if (state.planDelayMs) await new Promise((resolve) => setTimeout(resolve, state.planDelayMs))
      if (state.planStatus !== 200)
        return reply({ status: state.planStatus, code: 'AGENT_VERSION_CONFLICT' }, state.planStatus)
      const key = request.headers()['idempotency-key']
      let plan = [...plans.values()].find((item) => item.idempotencyKey === key)
      if (!plan) {
        const id = randomUUID()
        const command = request.postDataJSON()
        plan = {
          operationId: id,
          idempotencyKey: key,
          planHash: `ui-sealed-${id}`,
          expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          command,
          sizeBytes:
            command.kind === 'trash'
              ? files.entry.sizeBytes
              : versions.find((item) => item.id === command.versionId).sizeBytes,
          warning:
            '完整校验 HDD 副本；恢复覆盖前先备份当前文件。清理仅针对选定 HDD 副本，不删除原文件或审计证据。',
        }
        plans.set(id, plan)
      }
      return reply(plan)
    }
    if (path.startsWith('/registrations/')) {
      const id = path.split('/')[2],
        plan = plans.get(id),
        operation = operations.get(id)
      if (state.status !== 200)
        return reply({ status: state.status, code: 'AUTH_FORBIDDEN' }, state.status)
      if (!plan && !operation) return reply({ status: 404 }, 404)
      if (request.method() === 'POST' && !operation) plan.canceled = true
      const status = operation
        ? 'Registered'
        : plan.canceled
          ? 'Canceled'
          : state.registrationState || 'Issued'
      state.registrations.push({ id, state: status })
      return reply({
        id,
        state: status,
        plan: status === 'Issued' ? plan : null,
        intent: operation || null,
      })
    }
    if (path === '' && request.method() === 'POST') {
      const body = request.postDataJSON(),
        plan = [...plans.values()].find(
          (item) => item.planHash === body.planHash && item.idempotencyKey === body.idempotencyKey,
        )
      if (!plan || plan.canceled) return reply({ status: 409 }, 409)
      if (options.neverAccepted) {
        state.registrationState = 'Expired'
        return reply({ status: 503 }, 503)
      }
      const prior = operations.has(plan.operationId)
      if (!prior) {
        state.accepted++
        operations.set(plan.operationId, {
          id: plan.operationId,
          jobId: plan.operationId,
          command: plan.command,
          sizeBytes: plan.sizeBytes,
          state: 'Pending',
          expiresAtUtc: new Date(Date.now() + 900000).toISOString(),
          version: 0,
          resultCode: null,
          publishedVersion: null,
          sourceRetained: false,
        })
      }
      if (options.loseResponse && !prior) return route.abort('failed')
      return reply(operations.get(plan.operationId), 202)
    }
    const id = path.split('/')[1],
      operation = operations.get(id)
    if (!operation) return reply({ status: 404 }, 404)
    if (path.endsWith('/cancel')) {
      operation.state = 'Canceled'
      operation.version++
      return reply({ jobId: id, state: 'CancelRequested' }, 202)
    }
    if (path.endsWith('/acknowledge')) {
      operation.state = 'Acknowledged'
      operation.version++
      return reply({ jobId: id, state: 'Acknowledged' }, 202)
    }
    if (state.status !== 200)
      return reply({ status: state.status, code: 'AUTH_FORBIDDEN' }, state.status)
    state.reads++
    if (operation.state === 'Pending' && !state.hold)
      operation.state = state.unknown ? 'Unknown' : 'Succeeded'
    if (operation.state === 'Succeeded' && !applied.has(id)) {
      applied.add(id)
      operation.publishedVersion = 'E'.repeat(64)
      operation.sourceRetained =
        operation.command.kind === 'trash' || operation.command.expectedVersion !== 'ABSENT'
      if (operation.command.kind === 'trash') files.state.hideEntry = true
      if (operation.command.kind === 'restore') {
        files.state.hideEntry = false
        versions[0].captureState = 'Restored'
      }
      if (operation.command.kind === 'purge') {
        const version = versions.find((item) => item.id === operation.command.versionId)
        version.captureState = 'Purged'
        version.availability = 'Purged'
      }
    }
    return reply(operation)
  })
  return { ...files, archive: state, versions, plans, operations, root }
}
