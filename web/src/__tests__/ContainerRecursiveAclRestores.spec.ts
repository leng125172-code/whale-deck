import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertRecursiveAclRestoreIntent,
  assertRecursiveAclRestoreAdvance,
  queueRecursiveAclRestore,
  confirmRecursiveAclRestore,
  cancelRecursiveAclRestore,
  getRecursiveAclRestore,
  validRecursiveAclRestoreCommand,
} from '@/services/containerRecursiveAclRestores'
import type { ContainerRecursiveAclRestoreIntent } from '@/services/containerRecursiveAclRestores'
import {
  clearRecursiveAclRestoreHints,
  recursiveAclRestoreHint,
  reconcileRecursiveAclRestoreHints,
  saveRecursiveAclRestoreHint,
} from '@/services/containerRecursiveAclRestoreHints'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
const command = {
  location: { containerId: cid, instanceId: '777:2026-11-03T01:02:03Z', path: 'work/资料' },
  captureId: 'a'.repeat(32),
  expectedHistoryDigest: 'B'.repeat(64),
  expectedVersion: 'A'.repeat(64),
}
function ready(): ContainerRecursiveAclRestoreIntent {
  const expires = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: structuredClone(command),
    state: 'Ready',
    rootUid: 1000,
    rootGid: 1001,
    rootMode: 16872,
    executorUid: 5100,
    executorGid: 5101,
    entryCount: 4,
    fileCount: 2,
    directoryCount: 2,
    changeCount: 2,
    manifestDigest: 'D'.repeat(64),
    restoreSource: {
      captureId: command.captureId,
      historyDigest: command.expectedHistoryDigest,
      createdAtUnixMs: 0,
      sourceInstanceId: '666:2026-11-03T01:02:03Z',
      changedAclType: 'access',
      manifestDigest: 'E'.repeat(64),
      entryCount: 4,
      fileCount: 2,
      directoryCount: 2,
      changeCount: 1,
    },
    readyExpiresAtUtc: expires,
    resultCode: 'CONTAINER_RECURSIVE_ACL_RESTORE_READY',
    version: 2,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: {
      planHash: 'opaque',
      expiresAtUtc: expires,
      warning: '逐项恢复，不改inode所有者',
    },
  }
}
function partial(): ContainerRecursiveAclRestoreIntent {
  const row = ready()
  row.state = 'Partial'
  row.confirmation = null
  row.executionJobId = execution
  row.version = 4
  row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT'
  row.historyMayExist = true
  row.historyVerified = true
  row.historyDigest = 'F'.repeat(64)
  row.receipt = {
    id: execution,
    containerId: cid,
    instanceId: command.location.instanceId,
    fingerprint: '1'.repeat(64),
    targetProof: '2'.repeat(64),
    state: 'Partial',
    aclType: 'access',
    bindingCurrent: false,
    replayed: false,
    completedCount: 1,
    changedCount: 1,
    abortedCount: 0,
    notStartedCount: 3,
    unknownCount: 0,
    historyMayExist: true,
    historyVerified: true,
    historyDigest: row.historyDigest,
    historyAvailable: true,
    outcomeDigest: '3'.repeat(64),
  }
  return row
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  sessionStorage.clear()
})
it('keeps source original count distinct from planned restoration and accepts zero source count', () => {
  const row = ready()
  expect(assertRecursiveAclRestoreIntent(row)).toBe(row)
  row.restoreSource!.changeCount = 0
  expect(() => assertRecursiveAclRestoreIntent(row)).not.toThrow()
})
it.each([
  'captureId',
  'historyDigest',
  'sourceInstanceId',
  'changedAclType',
  'manifestDigest',
  'entryCount',
  'fileCount',
  'directoryCount',
  'changeCount',
  'createdAtUnixMs',
])('rejects changed/missing source %s', (field) => {
  const row = ready()
  delete (row.restoreSource as unknown as Record<string, unknown>)[field]
  expect(() => assertRecursiveAclRestoreIntent(row)).toThrow(/递归ACL/)
})
it.each(['captureId', 'expectedHistoryDigest', 'expectedVersion', 'location'])(
  'requires explicit command %s',
  (field) => {
    const value = structuredClone(command)
    delete (value as unknown as Record<string, unknown>)[field]
    expect(validRecursiveAclRestoreCommand(value)).toBe(false)
  },
)
it('rejects another source despite otherwise valid planning counts', () => {
  const row = ready()
  row.restoreSource!.captureId = 'b'.repeat(32)
  expect(() => assertRecursiveAclRestoreIntent(row)).toThrow(/递归ACL/)
  row.restoreSource!.captureId = command.captureId
  row.restoreSource!.historyDigest = 'C'.repeat(64)
  expect(() => assertRecursiveAclRestoreIntent(row)).toThrow(/递归ACL/)
})
it('source type constrains planned changes and receipt type', () => {
  const row = partial()
  row.restoreSource!.changedAclType = 'default'
  expect(() => assertRecursiveAclRestoreIntent(row)).toThrow(/递归ACL/)
  row.receipt!.aclType = 'default'
  expect(() => assertRecursiveAclRestoreIntent(row)).not.toThrow()
  row.changeCount = 3
  expect(() => assertRecursiveAclRestoreIntent(row)).toThrow(/递归ACL/)
})
it.each(['completedCount', 'changedCount', 'notStartedCount', 'historyDigest', 'outcomeDigest'])(
  'preserves actual Partial fact %s',
  (field) => {
    const previous = partial(),
      next = structuredClone(previous)
    next.version++
    ;(next.receipt as unknown as Record<string, unknown>)[field] = field.endsWith('Digest')
      ? '4'.repeat(64)
      : 0
    expect(() => assertRecursiveAclRestoreAdvance(previous, next)).toThrow(/递归ACL/)
  },
)
it.each(['captureId', 'historyDigest', 'sourceInstanceId', 'changeCount'])(
  'rejects late source replacement %s',
  (field) => {
    const previous = ready(),
      next = structuredClone(previous)
    next.version++
    ;(next.restoreSource as unknown as Record<string, unknown>)[field] =
      field === 'changeCount' ? 2 : 'new'
    expect(() => assertRecursiveAclRestoreAdvance(previous, next)).toThrow(/递归ACL/)
  },
)
it('allows only observation changes on acknowledged Partial without losing source or facts', () => {
  const previous = partial(),
    next = structuredClone(previous)
  next.state = 'Acknowledged'
  next.version++
  next.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_ACKNOWLEDGED'
  next.receipt!.replayed = true
  next.receipt!.historyAvailable = false
  expect(() => assertRecursiveAclRestoreIntent(next)).not.toThrow()
  expect(() => assertRecursiveAclRestoreAdvance(previous, next)).not.toThrow()
})
it('snapshots the 5 HTTP request fields and excludes target ACL/frame data', async () => {
  const input = structuredClone(command),
    row = ready()
  vi.mocked(apiRequest).mockResolvedValue(row)
  const pending = queueRecursiveAclRestore(input, id, new AbortController().signal)
  input.location.path = 'changed'
  input.captureId = 'b'.repeat(32)
  await pending
  const [path, init] = vi.mocked(apiRequest).mock.calls[0]!
  expect(path).toContain('/recursive-acl-restores')
  expect(JSON.parse(init!.body as string)).toEqual({
    instanceId: command.location.instanceId,
    path: command.location.path,
    captureId: command.captureId,
    expectedHistoryDigest: command.expectedHistoryDigest,
    expectedVersion: command.expectedVersion,
  })
})
it('queries exact original by key without queue fallback', async () => {
  vi.mocked(apiRequest).mockResolvedValue(ready())
  await getRecursiveAclRestore(cid, id, new AbortController().signal, true)
  expect(vi.mocked(apiRequest).mock.calls[0]![0]).toContain('/by-key/' + id)
  expect(vi.mocked(apiRequest).mock.calls).toHaveLength(1)
})
it('confirm and cancel bind header to original intent, never selected capture ID', async () => {
  const row = ready()
  vi.mocked(apiRequest).mockResolvedValue(row)
  await confirmRecursiveAclRestore(row, new AbortController().signal)
  await cancelRecursiveAclRestore(row, new AbortController().signal)
  for (const [, init] of vi.mocked(apiRequest).mock.calls)
    expect(init!.headers).toEqual({ 'Idempotency-Key': id })
})
it('expired plans never send confirmation', async () => {
  const row = ready()
  row.readyExpiresAtUtc = row.confirmation!.expiresAtUtc = new Date(Date.now() - 1).toISOString()
  await expect(confirmRecursiveAclRestore(row, new AbortController().signal)).rejects.toThrow(
    /递归ACL/,
  )
  expect(apiRequest).not.toHaveBeenCalled()
})
it('hints retain only four opaque fields and reconcile user/role without sensitive source', () => {
  saveRecursiveAclRestoreHint({ subject: 'admin', containerId: cid, key: id, id: null })
  const raw = sessionStorage.getItem('whaledeck-container-recursive-acl-restore-hints-v1')!
  expect(raw).not.toContain(command.expectedHistoryDigest)
  expect(Object.keys(JSON.parse(raw)[0])).toHaveLength(4)
  expect(recursiveAclRestoreHint('admin', cid)?.key).toBe(id)
  reconcileRecursiveAclRestoreHints('admin', false)
  expect(recursiveAclRestoreHint('admin', cid)).toBeNull()
  clearRecursiveAclRestoreHints()
})
