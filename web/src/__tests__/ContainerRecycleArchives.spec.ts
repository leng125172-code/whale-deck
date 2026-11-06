import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertArchiveAdvance,
  assertArchiveIntent,
  assertArchiveSource,
  confirmArchive,
  listArchiveSources,
  queueArchive,
} from '@/services/containerRecycleArchives'
import type {
  RecycleArchiveIntent,
  RecycleArchiveSource,
} from '@/services/containerRecycleArchives'
import {
  clearRecycleArchiveHints,
  recycleArchiveHint,
  reconcileRecycleArchiveHints,
  saveRecycleArchiveHint,
} from '@/services/containerRecycleArchiveHints'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
  sourceId = 'cc12cc12-cc12-4c12-cc12-cc12cc12cc12',
  sourceOp = 'dc12dc12-dc12-4c12-dc12-dc12dc12dc12'
function source(): RecycleArchiveSource {
  return {
    sourceIntentId: sourceId,
    purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
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
function ready(): RecycleArchiveIntent {
  const s = source(),
    expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    sourceIntentId: sourceId,
    purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
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
    resultCode: 'CONTAINER_RECYCLE_ARCHIVE_READY',
    version: 1,
    reservationRecorded: false,
    archiveCopied: null,
    receipt: null,
    confirmation: {
      planHash: 'private-archive-plan',
      expiresAtUtc: expiry,
      warning: '预检不清理文件',
    },
  }
}
function completed(unknown = false): RecycleArchiveIntent {
  const v = ready()
  v.state = unknown ? 'Unknown' : 'Succeeded'
  v.executionJobId = execution
  v.confirmation = null
  v.version = 3
  v.resultCode = unknown
    ? 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN'
    : 'CONTAINER_RECYCLE_ARCHIVE_COMPLETED'
  v.reservationRecorded = true
  v.archiveCopied = unknown ? null : true
  v.receipt = {
    id: execution,
    purgeOperationId: 'ec12ec12-ec12-4c12-ec12-ec12ec12ec12',
    sourceOperationId: sourceOp,
    fingerprint: 'D'.repeat(64),
    state: unknown ? 'Unknown' : 'Completed',
    sourceFenced: true,
    ownedFence: true,
    archiveCopied: unknown ? null : true,
    bindingCurrent: unknown ? null : true,
    publishedIdentity: unknown
      ? null
      : {
          device: '18446744073709551615',
          inode: '9007199254740993',
          mode: 33184,
          ownerUid: 1000,
          ownerGid: 1001,
          linkCount: '1',
          sizeBytes: 7,
          modifiedAtUnixNs: '1700000000123456789',
        },
    publishedChangedAtUnixNs: unknown ? null : '1700000000987654321',
  }

  return v
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  clearRecycleArchiveHints()
})
it('accepts exact source summary and each own outcome without borrowing original job', () => {
  expect(assertArchiveSource(source(), cid)).toEqual(source())
  assertArchiveIntent(ready(), cid, id, sourceId, source())
  assertArchiveIntent(completed())
  assertArchiveIntent(completed(true))
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
  expect(() => assertArchiveIntent(v, cid, id, sourceId, source())).toThrow(/归档|来源/)
})
it('does not turn Unknown into a false copy fact', () => {
  const v = completed(true)
  v.archiveCopied = false
  expect(() => assertArchiveIntent(v)).toThrow(/归档|来源/)
  const r = completed(true)
  r.receipt!.archiveCopied = false
  expect(() => assertArchiveIntent(r)).toThrow(/归档|来源/)
})
it.each(['fingerprint', 'sourceHistoryDigest'] as const)('rejects hash coercion %s', (field) => {
  const v = completed()
  if (field === 'sourceHistoryDigest') Object.assign(v, { sourceHistoryDigest: ['E'.repeat(64)] })
  else Object.assign(v.receipt!, { [field]: ['D'.repeat(64)] })
  expect(() => assertArchiveIntent(v)).toThrow(/归档|来源/)
})
it('holds explicit false only for proven aborted cancellation', () => {
  const v = completed()
  v.state = 'Canceled'
  v.archiveCopied = false
  v.receipt = {
    ...v.receipt!,
    state: 'Aborted',
    sourceFenced: false,
    ownedFence: false,
    archiveCopied: false,
  }
  v.receipt!.bindingCurrent = null
  v.receipt!.publishedIdentity = null
  v.receipt!.publishedChangedAtUnixNs = null
  assertArchiveIntent(v)
  v.receipt.archiveCopied = true
  expect(() => assertArchiveIntent(v)).toThrow(/归档|来源/)
})
it('does not accept rollback, source changes or attribution changes after a receipt', () => {
  const a = completed(true)
  const b = structuredClone(a)
  b.version++
  b.state = 'Acknowledged'
  assertArchiveAdvance(a, b)
  b.receipt!.archiveCopied = true
  expect(() => assertArchiveAdvance(a, b)).toThrow(/归档|来源/)
  const c = ready()
  const d = structuredClone(c)
  d.version--
  expect(() => assertArchiveAdvance(c, d)).toThrow(/归档|来源/)
})
it('registration sends only original intent id and opaque key', async () => {
  const v = ready()
  vi.mocked(apiRequest).mockResolvedValue(v)
  await queueArchive(cid, sourceId, id, new AbortController().signal)
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
  await confirmArchive(v, new AbortController().signal)
  expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
    planHash: v.confirmation!.planHash,
    confirmed: true,
    version: 1,
  })
})
it('source pagination refuses duplicate source ids and invented cursors', async () => {
  vi.mocked(apiRequest).mockResolvedValue({ items: [source(), source()], next: null })
  await expect(listArchiveSources(cid, new AbortController().signal)).rejects.toThrow(/归档|来源/)
  vi.mocked(apiRequest).mockResolvedValue({ items: [source()], next: sourceId })
  await expect(listArchiveSources(cid, new AbortController().signal)).rejects.toThrow(/归档|来源/)
})
it('browser hints persist only opaque original refs and disappear across accounts', () => {
  saveRecycleArchiveHint({
    subject: 'one',
    containerId: cid,
    key: id,
    id: null,
    sourceIntentId: sourceId,
  })
  expect(recycleArchiveHint('one', cid)?.sourceIntentId).toBe(sourceId)
  expect(sessionStorage.getItem('whaledeck-container-recycle-archive-hints-v1')).not.toContain(
    'private-archive-plan',
  )
  reconcileRecycleArchiveHints('two', true)
  expect(recycleArchiveHint('one', cid)).toBeNull()
})

it.each(['originalUid', 'originalGid', 'originalMode'] as const)(
  'binds planned source identity %s to selection',
  (field) => {
    const v = ready()
    v[field]!++
    expect(() => assertArchiveIntent(v, cid, id, sourceId, source())).toThrow(/来源身份/)
  },
)

it('keeps uint64 and nanosecond copy identity as exact decimal strings', () => {
  const v = completed()
  assertArchiveIntent(v)
  expect(v.receipt!.publishedIdentity!.inode).toBe('9007199254740993')
  for (const inode of [9007199254740992, '01', '-1', '18446744073709551616']) {
    const bad = structuredClone(v)
    ;(bad.receipt!.publishedIdentity! as unknown as { inode: unknown }).inode = inode
    expect(() => assertArchiveIntent(bad)).toThrow(/归档|来源/)
  }
})
it('keeps historical Completed when current binding is gone and rejects substituted inode', () => {
  const v = completed()
  v.receipt!.bindingCurrent = false
  v.receipt!.ownedFence = false
  assertArchiveIntent(v)
  const bad = structuredClone(v)
  bad.receipt!.publishedIdentity!.inode = '9007199254740994'
  expect(() => assertArchiveAdvance(v, bad)).toThrow(/归档|来源/)
})

it.each(['purgeOperationId', 'sourceOperationId'] as const)(
  'rejects recovery-only source identity collision with %s',
  (field) => {
    const v = ready()
    v.sourceIntentId = v[field]
    expect(() => assertArchiveIntent(v)).toThrow(/归档|来源/)
  },
)
