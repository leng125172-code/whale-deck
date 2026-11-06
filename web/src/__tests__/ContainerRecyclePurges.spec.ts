import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertPurgeAdvance,
  assertPurgeIntent,
  assertPurgeSource,
  confirmPurge,
  listPurgeSources,
  queuePurge,
} from '@/services/containerRecyclePurges'
import type { RecyclePurgeIntent, RecyclePurgeSource } from '@/services/containerRecyclePurges'
import {
  clearRecyclePurgeHints,
  recyclePurgeHint,
  reconcileRecyclePurgeHints,
  saveRecyclePurgeHint,
} from '@/services/containerRecyclePurgeHints'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
  sourceId = 'cc12cc12-cc12-4c12-cc12-cc12cc12cc12',
  sourceOp = 'dc12dc12-dc12-4c12-dc12-dc12dc12dc12'
function source(): RecyclePurgeSource {
  return {
    sourceIntentId: sourceId,
    sourceOperationId: sourceOp,
    location: {
      containerId: cid,
      instanceId: '777:2026-10-22T01:02:03.123456789Z',
      path: 'work/file.bin',
    },
    originalUid: 1000,
    originalGid: 1001,
    originalMode: 33184,
    sizeBytes: 7,
    sha256: 'B'.repeat(64),
    attributeCount: 1,
    historyDigest: 'E'.repeat(64),
    completedAtUtc: '2026-11-05T01:02:03Z',
  }
}
function ready(): RecyclePurgeIntent {
  const s = source(),
    expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    sourceIntentId: sourceId,
    sourceOperationId: sourceOp,
    sourceHistoryDigest: s.historyDigest,
    location: s.location,
    state: 'Ready',
    originalUid: 1000,
    originalGid: 1001,
    originalMode: 33184,
    executorUid: 5100,
    executorGid: 5101,
    sizeBytes: 7,
    sha256: s.sha256,
    attributeCount: 1,
    readyExpiresAtUtc: expiry,
    resultCode: 'CONTAINER_RECYCLE_PURGE_READY',
    version: 1,
    reservationRecorded: false,
    purgeApplied: null,
    receipt: null,
    confirmation: {
      planHash: 'private-purge-plan',
      expiresAtUtc: expiry,
      warning: '预检不清理文件',
    },
  }
}
function completed(unknown = false): RecyclePurgeIntent {
  const v = ready()
  v.state = unknown ? 'Unknown' : 'Succeeded'
  v.executionJobId = execution
  v.confirmation = null
  v.version = 3
  v.resultCode = unknown
    ? 'CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN'
    : 'CONTAINER_RECYCLE_PURGE_COMPLETED'
  v.reservationRecorded = true
  v.purgeApplied = unknown ? null : true
  v.receipt = {
    id: execution,
    sourceOperationId: sourceOp,
    fingerprint: 'D'.repeat(64),
    state: unknown ? 'Unknown' : 'Completed',
    sourceFenced: true,
    ownedFence: true,
    purgeApplied: unknown ? null : true,
  }

  return v
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  clearRecyclePurgeHints()
})
it('accepts exact source summary and each own outcome without borrowing original job', () => {
  expect(assertPurgeSource(source(), cid)).toEqual(source())
  assertPurgeIntent(ready(), cid, id, sourceId, source())
  assertPurgeIntent(completed())
  assertPurgeIntent(completed(true))
})
it.each([
  'sourceIntentId',
  'sourceOperationId',
  'sourceHistoryDigest',
  'location',
  'sizeBytes',
  'sha256',
  'attributeCount',
] as const)('rejects source replacement %s', (field) => {
  const v = ready()
  if (field === 'location') v.location = { ...v.location, path: 'work/other' }
  else if (field === 'sizeBytes') v.sizeBytes++
  else if (field === 'attributeCount') v.attributeCount++
  else v[field] = field.endsWith('Id') ? execution : 'F'.repeat(64)
  expect(() => assertPurgeIntent(v, cid, id, sourceId, source())).toThrow(/清理|来源/)
})
it('does not turn Unknown into a false deletion fact', () => {
  const v = completed(true)
  v.purgeApplied = false
  expect(() => assertPurgeIntent(v)).toThrow(/清理|来源/)
  const r = completed(true)
  r.receipt!.purgeApplied = false
  expect(() => assertPurgeIntent(r)).toThrow(/清理|来源/)
})
it.each(['fingerprint', 'sourceHistoryDigest'] as const)('rejects hash coercion %s', (field) => {
  const v = completed()
  if (field === 'sourceHistoryDigest') Object.assign(v, { sourceHistoryDigest: ['E'.repeat(64)] })
  else Object.assign(v.receipt!, { [field]: ['D'.repeat(64)] })
  expect(() => assertPurgeIntent(v)).toThrow(/清理|来源/)
})
it('holds explicit false only for proven aborted cancellation', () => {
  const v = completed()
  v.state = 'Canceled'
  v.purgeApplied = false
  v.receipt = {
    ...v.receipt!,
    state: 'Aborted',
    sourceFenced: false,
    ownedFence: false,
    purgeApplied: false,
  }
  assertPurgeIntent(v)
  v.receipt.purgeApplied = true
  expect(() => assertPurgeIntent(v)).toThrow(/清理|来源/)
})
it('does not accept rollback, source changes or attribution changes after a receipt', () => {
  const a = completed(true)
  const b = structuredClone(a)
  b.version++
  b.state = 'Acknowledged'
  assertPurgeAdvance(a, b)
  b.receipt!.purgeApplied = true
  expect(() => assertPurgeAdvance(a, b)).toThrow(/清理|来源/)
  const c = ready()
  const d = structuredClone(c)
  d.version--
  expect(() => assertPurgeAdvance(c, d)).toThrow(/清理|来源/)
})
it('registration sends only original intent id and opaque key', async () => {
  const v = ready()
  vi.mocked(apiRequest).mockResolvedValue(v)
  await queuePurge(cid, sourceId, id, new AbortController().signal)
  const [, options] = vi.mocked(apiRequest).mock.calls[0]!
  expect(JSON.parse(options!.body as string)).toEqual({ sourceIntentId: sourceId })
  expect(options!.headers).toEqual({ 'Idempotency-Key': id })
})
it('confirmation is separate, explicit and bound to the returned plan/version', async () => {
  const v = ready()
  vi.mocked(apiRequest).mockResolvedValue({
    ...v,
    state: 'Pending',
    executionJobId: execution,
    confirmation: null,
    version: 2,
  })
  await confirmPurge(v, new AbortController().signal)
  expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
    planHash: v.confirmation!.planHash,
    confirmed: true,
    version: 1,
  })
})
it('source pagination refuses duplicate source ids and invented cursors', async () => {
  vi.mocked(apiRequest).mockResolvedValue({ items: [source(), source()], next: null })
  await expect(listPurgeSources(cid, new AbortController().signal)).rejects.toThrow(/清理|来源/)
  vi.mocked(apiRequest).mockResolvedValue({ items: [source()], next: sourceId })
  await expect(listPurgeSources(cid, new AbortController().signal)).rejects.toThrow(/清理|来源/)
})
it('browser hints persist only opaque original refs and disappear across accounts', () => {
  saveRecyclePurgeHint({
    subject: 'one',
    containerId: cid,
    key: id,
    id: null,
    sourceIntentId: sourceId,
  })
  expect(recyclePurgeHint('one', cid)?.sourceIntentId).toBe(sourceId)
  expect(sessionStorage.getItem('whaledeck-container-recycle-purge-hints-v1')).not.toContain(
    'private-purge-plan',
  )
  reconcileRecyclePurgeHints('two', true)
  expect(recyclePurgeHint('one', cid)).toBeNull()
})

it.each(['originalUid', 'originalGid', 'originalMode'] as const)(
  'binds planned source identity %s to selection',
  (field) => {
    const v = ready()
    v[field]!++
    expect(() => assertPurgeIntent(v, cid, id, sourceId, source())).toThrow(/来源身份/)
  },
)
