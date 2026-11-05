import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertRestoreAdvance,
  assertRestoreIntent,
  assertRestoreSource,
  confirmRestore,
  listRestoreSources,
  queueRestore,
} from '@/services/containerRecycleRestores'
import type {
  RecycleRestoreIntent,
  RecycleRestoreSource,
} from '@/services/containerRecycleRestores'
import {
  clearRecycleRestoreHints,
  recycleRestoreHint,
  reconcileRecycleRestoreHints,
  saveRecycleRestoreHint,
} from '@/services/containerRecycleRestoreHints'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
  sourceId = 'cc12cc12-cc12-4c12-cc12-cc12cc12cc12',
  sourceOp = 'dc12dc12-dc12-4c12-dc12-dc12dc12dc12'
function source(): RecycleRestoreSource {
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
function ready(): RecycleRestoreIntent {
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
    resultCode: 'CONTAINER_RECYCLE_RESTORE_READY',
    version: 1,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: {
      planHash: 'private-restore-plan',
      expiresAtUtc: expiry,
      warning: '预检不恢复文件',
    },
  }
}
function completed(unknown = false): RecycleRestoreIntent {
  const v = ready()
  v.state = unknown ? 'Unknown' : 'Succeeded'
  v.executionJobId = execution
  v.confirmation = null
  v.version = 3
  v.resultCode = unknown
    ? 'CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN'
    : 'CONTAINER_RECYCLE_RESTORE_COMPLETED'
  v.historyVerified = true
  v.historyDigest = v.sourceHistoryDigest
  v.receipt = {
    id: execution,
    sourceOperationId: sourceOp,
    fingerprint: 'D'.repeat(64),
    targetProof: 'C'.repeat(64),
    state: unknown ? 'Unknown' : 'Completed',
    bindingCurrent: !unknown,
    replayed: false,
    historyVerified: true,
    historyDigest: v.sourceHistoryDigest,
    historyAvailable: true,
    sourceState: unknown ? null : 'Restored',
    sourceRetained: unknown ? null : false,
    sourceRestored: unknown ? null : true,
    restoredByThisOperation: unknown ? null : true,
  }
  return v
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  clearRecycleRestoreHints()
})
it('accepts exact source summary and each own outcome without borrowing original job', () => {
  expect(assertRestoreSource(source(), cid)).toEqual(source())
  assertRestoreIntent(ready(), cid, id, sourceId, source())
  assertRestoreIntent(completed())
  assertRestoreIntent(completed(true))
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
  expect(() => assertRestoreIntent(v, cid, id, sourceId, source())).toThrow(/恢复|来源/)
})
it.each(['sourceRetained', 'sourceRestored', 'restoredByThisOperation'] as const)(
  'does not turn Unknown missing fact into false %s',
  (field) => {
    const v = completed(true)
    v.receipt![field] = false
    expect(() => assertRestoreIntent(v)).toThrow(/恢复|来源/)
  },
)
it.each(['fingerprint', 'targetProof', 'sourceHistoryDigest'] as const)(
  'rejects hash coercion %s',
  (field) => {
    const v = completed()
    if (field === 'sourceHistoryDigest') Object.assign(v, { sourceHistoryDigest: ['E'.repeat(64)] })
    else Object.assign(v.receipt!, { [field]: ['D'.repeat(64)] })
    expect(() => assertRestoreIntent(v)).toThrow(/恢复|来源/)
  },
)
it('holds null/no-claim facts for canceled restoration', () => {
  const v = ready()
  v.state = 'Canceled'
  v.executionJobId = execution
  v.confirmation = null
  v.resultCode = 'CONTAINER_RECYCLE_RESTORE_CANCELED'
  v.receipt = {
    ...completed().receipt!,
    state: 'Aborted',
    bindingCurrent: false,
    historyVerified: false,
    historyDigest: '',
    historyAvailable: false,
    sourceState: null,
    sourceRetained: null,
    sourceRestored: null,
    restoredByThisOperation: false,
  }
  assertRestoreIntent(v)
  v.receipt.restoredByThisOperation = true
  expect(() => assertRestoreIntent(v)).toThrow(/恢复|来源/)
})
it('does not accept rollback, source changes or attribution changes after a receipt', () => {
  const a = completed(true)
  const b = structuredClone(a)
  b.version++
  b.state = 'Acknowledged'
  assertRestoreAdvance(a, b)
  b.receipt!.sourceState = 'Restored'
  expect(() => assertRestoreAdvance(a, b)).toThrow(/恢复|来源/)
  const c = ready()
  const d = structuredClone(c)
  d.version--
  expect(() => assertRestoreAdvance(c, d)).toThrow(/恢复|来源/)
})
it('registration sends only original intent id and opaque key', async () => {
  const v = ready()
  vi.mocked(apiRequest).mockResolvedValue(v)
  await queueRestore(cid, sourceId, id, new AbortController().signal)
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
  await confirmRestore(v, new AbortController().signal)
  expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
    planHash: v.confirmation!.planHash,
    confirmed: true,
    version: 1,
  })
})
it('source pagination refuses duplicate source ids and invented cursors', async () => {
  vi.mocked(apiRequest).mockResolvedValue({ items: [source(), source()], next: null })
  await expect(listRestoreSources(cid, new AbortController().signal)).rejects.toThrow(/恢复|来源/)
  vi.mocked(apiRequest).mockResolvedValue({ items: [source()], next: sourceId })
  await expect(listRestoreSources(cid, new AbortController().signal)).rejects.toThrow(/恢复|来源/)
})
it('browser hints persist only opaque original refs and disappear across accounts', () => {
  saveRecycleRestoreHint({
    subject: 'one',
    containerId: cid,
    key: id,
    id: null,
    sourceIntentId: sourceId,
  })
  expect(recycleRestoreHint('one', cid)?.sourceIntentId).toBe(sourceId)
  expect(sessionStorage.getItem('whaledeck-container-recycle-restore-hints-v1')).not.toContain(
    'private-restore-plan',
  )
  reconcileRecycleRestoreHints('two', true)
  expect(recycleRestoreHint('one', cid)).toBeNull()
})

it.each(['originalUid', 'originalGid', 'originalMode'] as const)(
  'binds planned source identity %s to selection',
  (field) => {
    const v = ready()
    v[field]!++
    expect(() => assertRestoreIntent(v, cid, id, sourceId, source())).toThrow(/来源身份/)
  },
)
