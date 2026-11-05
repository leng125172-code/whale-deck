import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertRecycleIntent,
  assertRecycleAdvance,
  queueRecycle,
  canRecycleContainerEntry,
} from '@/services/containerRecycles'
import type { ContainerRecycleIntent, RecycleReceipt } from '@/services/containerRecycles'
import {
  clearRecycleHints,
  recycleHint,
  saveRecycleHint,
  reconcileRecycleHints,
} from '@/services/containerRecycleHints'
import type { FileEntry } from '@/services/fileManagement'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
  cid = 'a'.repeat(64)
function ready(): ContainerRecycleIntent {
  const expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: {
      location: { containerId: cid, instanceId: '777:2026-11-05T01:02:03Z', path: 'work/file.bin' },
      expectedVersion: 'A'.repeat(64),
    },
    state: 'Ready',
    originalUid: 1000,
    originalGid: 1001,
    originalMode: 33184,
    executorUid: 5100,
    executorGid: 5101,
    sizeBytes: 7,
    sha256: 'B'.repeat(64),
    attributeCount: 1,
    readyExpiresAtUtc: expiry,
    resultCode: 'CONTAINER_RECYCLE_READY',
    version: 1,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: {
      planHash: 'private-confirmation',
      expiresAtUtc: expiry,
      warning: '尚未移除文件',
    },
  }
}
function outcome(state: 'Unknown' | 'Succeeded' | 'Superseded'): ContainerRecycleIntent {
  const v = ready()
  v.state = state
  v.executionJobId = execution
  v.confirmation = null
  v.version = 3
  v.resultCode = 'CONTAINER_RECYCLE_RESULT_UNKNOWN'
  v.historyMayExist = true
  v.historyVerified = state !== 'Unknown'
  v.historyDigest = v.historyVerified ? 'C'.repeat(64) : null
  v.receipt = {
    id: execution,
    containerId: cid,
    instanceId: v.command.location.instanceId,
    fingerprint: 'D'.repeat(64),
    targetProof: 'E'.repeat(64),
    state: state === 'Unknown' ? 'Unknown' : state === 'Succeeded' ? 'Trashed' : 'Restored',
    bindingCurrent: state !== 'Unknown',
    replayed: false,
    historyMayExist: true,
    historyVerified: v.historyVerified,
    historyDigest: v.historyDigest ?? '',
    historyAvailable: state !== 'Unknown',
    sourceRetained: state === 'Unknown' ? null : state === 'Succeeded',
    sourceRestored: state === 'Unknown' ? null : state === 'Superseded',
  }
  return v
}
beforeEach(() => {
  vi.clearAllMocks()
  clearRecycleHints()
})
it('sends only original location/version and stores no confirmation in navigation hints', async () => {
  const v = ready()
  vi.mocked(apiRequest).mockResolvedValue(v)
  await queueRecycle(v.command, id, new AbortController().signal)
  const call = vi.mocked(apiRequest).mock.calls[0]!
  expect(call[0]).toContain('/recycles')
  expect(JSON.parse(call[1]!.body as string)).toEqual({
    instanceId: v.command.location.instanceId,
    path: v.command.location.path,
    expectedVersion: v.command.expectedVersion,
  })
  saveRecycleHint({ subject: 'admin', containerId: cid, key: id, id: null })
  expect(JSON.stringify(sessionStorage)).not.toContain(v.confirmation!.planHash)
  reconcileRecycleHints('other', true)
  expect(recycleHint('other', cid)).toBeNull()
})
it.each(['originalMode', 'sizeBytes', 'attributeCount', 'sha256', 'receipt', 'confirmation'])(
  'rejects missing %s rather than defaulting',
  (field) => {
    const v = ready()
    delete (v as unknown as Record<string, unknown>)[field]
    expect(() => assertRecycleIntent(v)).toThrow(/无法核实|旧状态|不同原计划/)
  },
)
it.each([
  'id',
  'containerId',
  'instanceId',
  'fingerprint',
  'targetProof',
  'sourceRetained',
  'sourceRestored',
  'historyVerified',
  'historyDigest',
])('rejects corrupt Unknown receipt %s', (field) => {
  const v = outcome('Unknown')
  ;(v.receipt as unknown as Record<string, unknown>)[field] = field.startsWith('source')
    ? false
    : 'wrong'
  expect(() => assertRecycleIntent(v)).toThrow(/无法核实|旧状态|不同原计划/)
})
it('does not label restored history as successful recycling', () => {
  const v = outcome('Superseded')
  expect(() => assertRecycleIntent(v)).not.toThrow()
  v.state = 'Succeeded'
  expect(() => assertRecycleIntent(v)).toThrow(/无法核实|旧状态|不同原计划/)
})
it('acknowledgement preserves Unknown facts and allows only observation refresh', () => {
  const v = outcome('Unknown')
  v.historyVerified = true
  v.historyDigest = 'C'.repeat(64)
  Object.assign(v.receipt!, {
    historyVerified: true,
    historyDigest: v.historyDigest,
    historyAvailable: true,
  })
  const n = structuredClone(v)
  n.version++
  n.state = 'Acknowledged'
  n.resultCode = 'CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED'
  n.receipt!.historyAvailable = false
  n.receipt!.replayed = true
  expect(() => assertRecycleAdvance(v, n)).not.toThrow()
  n.receipt!.sourceRetained = false
  expect(() => assertRecycleAdvance(v, n)).toThrow(/无法核实|旧状态|不同原计划/)
})
it.each(['command', 'version', 'execution', 'metadata', 'receipt'])(
  'refuses a changed or stale original %s',
  (field) => {
    const v = outcome('Succeeded'),
      n = structuredClone(v)
    if (field === 'command') n.command.expectedVersion = 'F'.repeat(64)
    if (field === 'version') n.version--
    if (field === 'execution') n.executionJobId = id
    if (field === 'metadata') n.sizeBytes = 8
    if (field === 'receipt') (n.receipt as RecycleReceipt).sourceRetained = false
    expect(() => assertRecycleAdvance(v, n)).toThrow(/无法核实|旧状态|不同原计划/)
  },
)
it('allows only unprotected ordinary bounded files', () => {
  const entry = {
    kind: 'File',
    isProtected: false,
    mode: '0640',
    version: 'A'.repeat(64),
    sizeBytes: 7,
  } as FileEntry
  expect(canRecycleContainerEntry(entry)).toBe(true)
  for (const changed of [
    { kind: 'Directory' },
    { isProtected: true },
    { sizeBytes: 1073741825 },
    { mode: '4640' },
  ])
    expect(canRecycleContainerEntry({ ...entry, ...changed } as FileEntry)).toBe(false)
})
