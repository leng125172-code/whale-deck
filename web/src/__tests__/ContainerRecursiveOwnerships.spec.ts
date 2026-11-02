import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertRecursiveOwnershipIntent,
  assertRecursiveOwnershipAdvance,
  canRecursiveOwnershipContainerEntry,
  cancelRecursiveOwnership,
  confirmRecursiveOwnership,
  currentRecursiveOwnershipInstance,
  getRecursiveOwnership,
  queueRecursiveOwnership,
  recursiveOwnershipTerminal,
  recursiveOwnershipUnresolved,
  validRecursiveOwnershipCommand,
} from '@/services/containerRecursiveOwnerships'
import type {
  ContainerRecursiveOwnershipIntent,
  ContainerRecursiveOwnershipReceipt,
} from '@/services/containerRecursiveOwnerships'
import {
  clearRecursiveOwnershipHints,
  recursiveOwnershipHint,
  reconcileRecursiveOwnershipHints,
  saveRecursiveOwnershipHint,
} from '@/services/containerRecursiveOwnershipHints'
import type { FileEntry } from '@/services/fileManagement'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
const target = {
  containerId: cid,
  instanceId: '777:2026-10-30T01:02:03.123456789Z',
  name: 'tree',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const command = {
  location: { containerId: cid, instanceId: target.instanceId, path: 'work/目录树' },
  uid: 2200,
  gid: 3200,
  expectedVersion: 'A'.repeat(64),
}
function ready(): ContainerRecursiveOwnershipIntent {
  const expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: structuredClone(command),
    state: 'Ready',
    rootUid: 1000,
    rootGid: 1001,
    executorUid: 5100,
    executorGid: 5101,
    entryCount: 4,
    fileCount: 2,
    directoryCount: 2,
    changeCount: 2,
    manifestDigest: 'D'.repeat(64),
    readyExpiresAtUtc: expiry,
    resultCode: 'CONTAINER_RECURSIVE_OWNERSHIP_READY',
    version: 2,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: {
      planHash: 'opaque-original-confirmation',
      expiresAtUtc: expiry,
      warning: '预检不修改所有者',
    },
  }
}
function result(
  kind: ContainerRecursiveOwnershipReceipt['state'],
): ContainerRecursiveOwnershipIntent {
  const row = ready(),
    verified = kind === 'Completed' || kind === 'Partial'
  row.state = kind === 'Completed' ? 'Succeeded' : kind === 'Aborted' ? 'Canceled' : kind
  row.confirmation = null
  row.executionJobId = execution
  row.version = 3
  row.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_RESULT'
  row.historyMayExist = kind !== 'Aborted'
  row.historyVerified = verified
  row.historyDigest = verified ? 'E'.repeat(64) : null
  row.receipt = {
    id: execution,
    containerId: cid,
    instanceId: target.instanceId,
    fingerprint: 'A'.repeat(64),
    targetProof: 'B'.repeat(64),
    state: kind,
    bindingCurrent: kind === 'Completed',
    replayed: false,
    completedCount: kind === 'Completed' ? 4 : kind === 'Partial' ? 1 : 0,
    changedCount: kind === 'Completed' ? 2 : kind === 'Partial' ? 1 : 0,
    abortedCount: 0,
    notStartedCount: kind === 'Aborted' ? 4 : kind === 'Partial' ? 3 : 0,
    unknownCount: kind === 'Unknown' ? 4 : 0,
    historyMayExist: row.historyMayExist,
    historyVerified: verified,
    historyDigest: row.historyDigest ?? '',
    historyAvailable: verified,
    outcomeDigest: verified ? 'F'.repeat(64) : '',
  }
  return row
}
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
})
describe('recursive ownership contract and irreversible facts', () => {
  it.each(['Completed', 'Partial', 'Aborted', 'Unknown'] as const)(
    '%s is a coherent independent receipt',
    (kind) => expect(assertRecursiveOwnershipIntent(result(kind))).toBeTruthy(),
  )
  it.each(['Partial', 'Unknown'])('%s remains protected and nonterminal', (state) => {
    expect(recursiveOwnershipUnresolved(state)).toBe(true)
    expect(recursiveOwnershipTerminal(state)).toBe(false)
  })
  it.each(['', '0', 1.5, -1, 4294967295, Number.NaN, Infinity, null, true])(
    'invalid owner %s cannot acquire a registration',
    (value) => {
      expect(validRecursiveOwnershipCommand({ ...command, uid: value as number })).toBe(false)
      expect(validRecursiveOwnershipCommand({ ...command, gid: value as number })).toBe(false)
    },
  )
  it('requires both explicit owners and retains explicit root', () => {
    expect(validRecursiveOwnershipCommand({ ...command, uid: 0, gid: 0 })).toBe(true)
    const missing = { ...command } as Partial<typeof command>
    delete missing.gid
    expect(validRecursiveOwnershipCommand(missing as typeof command)).toBe(false)
  })
  it('offers only ordinary writable directory entries', () => {
    const entry = {
      kind: 'Directory',
      mode: '0755',
      version: 'A'.repeat(64),
      isProtected: false,
    } as FileEntry
    expect(canRecursiveOwnershipContainerEntry(entry)).toBe(true)
    for (const patch of [
      { kind: 'File' },
      { kind: 'SymbolicLink' },
      { mode: '4755' },
      { isProtected: true },
      { version: 'ABSENT' },
    ])
      expect(canRecursiveOwnershipContainerEntry({ ...entry, ...patch } as FileEntry)).toBe(false)
  })
  it.each([
    'rootUid',
    'rootGid',
    'executorUid',
    'executorGid',
    'fileCount',
    'directoryCount',
    'changeCount',
    'entryCount',
    'manifestDigest',
    'readyExpiresAtUtc',
  ])('missing planned %s fails closed', (key) => {
    const row = ready()
    Object.assign(row, { [key]: null })
    expect(() => assertRecursiveOwnershipIntent(row)).toThrow(/递归所有者/)
  })
  it.each([
    { entryCount: 10001 },
    { fileCount: 4, directoryCount: 0 },
    { changeCount: 5 },
    { rootUid: 4294967295 },
    { manifestDigest: 'bad' },
  ])('invalid plan summary %j is rejected', (patch) =>
    expect(() => assertRecursiveOwnershipIntent({ ...ready(), ...patch })).toThrow(/递归所有者/),
  )
  it.each([
    'id',
    'containerId',
    'instanceId',
    'fingerprint',
    'targetProof',
    'state',
    'completedCount',
    'changedCount',
    'abortedCount',
    'notStartedCount',
    'unknownCount',
    'bindingCurrent',
    'historyMayExist',
    'historyVerified',
    'historyDigest',
    'historyAvailable',
    'outcomeDigest',
  ])('invalid receipt %s is rejected', (key) => {
    const row = result('Completed')
    Object.assign(row.receipt!, { [key]: key.endsWith('Count') ? -1 : 'bad' })
    expect(() => assertRecursiveOwnershipIntent(row)).toThrow(/递归所有者/)
  })
  it.each(['Completed', 'Partial', 'Aborted', 'Unknown'] as const)(
    '%s counters must sum to the sealed tree size',
    (kind) => {
      const row = result(kind)
      row.receipt!.notStartedCount++
      expect(() => assertRecursiveOwnershipIntent(row)).toThrow(/递归所有者/)
    },
  )
  it('actual change count is a subset and cannot exceed planned change', () => {
    const row = result('Partial')
    row.receipt!.changedCount = 2
    expect(() => assertRecursiveOwnershipIntent(row)).toThrow(/递归所有者/)
    row.receipt!.completedCount = 3
    row.receipt!.notStartedCount = 1
    row.receipt!.changedCount = 3
    expect(() => assertRecursiveOwnershipIntent(row)).toThrow(/递归所有者/)
  })
  it('missing Root proof cannot produce success, partial or acknowledgment', () => {
    for (const state of ['Succeeded', 'Superseded', 'Partial', 'Acknowledged'])
      expect(() =>
        assertRecursiveOwnershipIntent({
          ...ready(),
          state,
          confirmation: null,
          executionJobId: execution,
          resultCode: 'CONTAINER_RECURSIVE_OWNERSHIP_RESULT',
        }),
      ).toThrow(/递归所有者/)
  })
  it('metadata availability can worsen without erasing verified history', () => {
    const before = result('Partial'),
      after = structuredClone(before)
    after.state = 'Acknowledged'
    after.version++
    after.receipt!.historyAvailable = false
    after.receipt!.replayed = true
    after.resultCode = 'CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL_ACKNOWLEDGED'
    expect(assertRecursiveOwnershipIntent(after)).toBeTruthy()
    expect(() => assertRecursiveOwnershipAdvance(before, after)).not.toThrow()
    expect(after.receipt!.changedCount).toBe(1)
  })
  it.each([
    'completedCount',
    'changedCount',
    'abortedCount',
    'notStartedCount',
    'unknownCount',
    'outcomeDigest',
    'historyVerified',
    'historyDigest',
    'state',
  ])('acknowledgment cannot change original receipt %s', (key) => {
    const before = result('Partial'),
      after = structuredClone(before)
    after.state = 'Acknowledged'
    after.version++
    Object.assign(after.receipt!, {
      [key]:
        typeof after.receipt![key as keyof ContainerRecursiveOwnershipReceipt] === 'number'
          ? Number(after.receipt![key as keyof ContainerRecursiveOwnershipReceipt]) + 1
          : 'different',
    })
    expect(() => assertRecursiveOwnershipAdvance(before, after)).toThrow(/递归所有者/)
  })
  it.each(['Pending', 'Applying', 'Succeeded', 'Canceled'])(
    'Partial cannot move back to %s',
    (state) => {
      const before = result('Partial'),
        after = structuredClone(before)
      after.state = state
      after.version++
      expect(() => assertRecursiveOwnershipAdvance(before, after)).toThrow(/递归所有者/)
    },
  )
  it('does not accept altered owners, instance, plan token, manifest, execution or stale versions', () => {
    const before = ready()
    for (const change of [
      (r: ContainerRecursiveOwnershipIntent) => (r.command.uid = 2300),
      (r: ContainerRecursiveOwnershipIntent) => (r.command.gid = 3300),
      (r: ContainerRecursiveOwnershipIntent) => (r.manifestDigest = 'B'.repeat(64)),
      (r: ContainerRecursiveOwnershipIntent) => (r.confirmation!.planHash = 'other'),
      (r: ContainerRecursiveOwnershipIntent) => r.version--,
    ]) {
      const row = structuredClone(before)
      change(row)
      expect(() => assertRecursiveOwnershipAdvance(before, row)).toThrow(/递归所有者/)
    }
    expect(
      currentRecursiveOwnershipInstance(before, {
        ...target,
        instanceId: '888:2026-10-30T01:02:03Z',
      }),
    ).toBe(false)
    const active = result('Unknown'),
      changed = structuredClone(active)
    changed.version++
    changed.executionJobId = id
    expect(() => assertRecursiveOwnershipAdvance(active, changed)).toThrow(/递归所有者/)
  })
  it('allows virtual Ready expiry but not replacement at the same version', () => {
    const before = ready(),
      after = structuredClone(before)
    after.state = 'Expired'
    after.confirmation = null
    expect(() => assertRecursiveOwnershipAdvance(before, after)).not.toThrow()
    const changed = structuredClone(before)
    changed.fileCount = 1
    expect(() => assertRecursiveOwnershipAdvance(before, changed)).toThrow(/递归所有者/)
  })
})
describe('original registration and private navigation hints', () => {
  it('posts only two explicit owners and the original version/key', async () => {
    vi.mocked(apiRequest).mockResolvedValue(ready())
    await queueRecursiveOwnership(command, id, new AbortController().signal)
    const [path, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(path).toBe(`containers/${cid}/files/recursive-ownerships`)
    expect(options!.headers).toEqual({ 'Idempotency-Key': id })
    expect(JSON.parse(options!.body as string)).toEqual({
      instanceId: target.instanceId,
      path: command.location.path,
      uid: 2200,
      gid: 3200,
      expectedVersion: 'A'.repeat(64),
    })
  })
  it('by-key read never manufactures a replacement write', async () => {
    vi.mocked(apiRequest).mockResolvedValue(ready())
    await getRecursiveOwnership(cid, id, new AbortController().signal, true)
    expect(vi.mocked(apiRequest).mock.calls[0]![0]).toContain('/by-key/' + id)
    expect(vi.mocked(apiRequest).mock.calls[0]![1]!.method).toBeUndefined()
  })
  it('confirm sends original token and version only; expired plan makes no call', async () => {
    const row = ready(),
      next = { ...row, state: 'Pending', confirmation: null, executionJobId: execution, version: 3 }
    vi.mocked(apiRequest).mockResolvedValue(next)
    await confirmRecursiveOwnership(row, new AbortController().signal)
    expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: 2,
    })
    vi.clearAllMocks()
    row.confirmation!.expiresAtUtc = row.readyExpiresAtUtc = new Date(
      Date.now() - 1000,
    ).toISOString()
    await expect(confirmRecursiveOwnership(row, new AbortController().signal)).rejects.toThrow(
      /递归所有者/,
    )
    expect(apiRequest).not.toHaveBeenCalled()
  })
  it('cancel keeps original idempotency reference', async () => {
    const row = ready()
    vi.mocked(apiRequest).mockResolvedValue({
      ...row,
      state: 'Canceled',
      confirmation: null,
      resultCode: 'CONTAINER_RECURSIVE_OWNERSHIP_CANCELED',
      version: 3,
    })
    await cancelRecursiveOwnership(row, new AbortController().signal)
    expect(vi.mocked(apiRequest).mock.calls[0]![1]!.headers).toEqual({ 'Idempotency-Key': id })
  })
  it('stores only original reference and clears it on subject/role changes', () => {
    const hint = { subject: 'admin', containerId: cid, key: id, id: null }
    saveRecursiveOwnershipHint(hint)
    expect(recursiveOwnershipHint('admin', cid)).toEqual(hint)
    const raw = sessionStorage.getItem('whaledeck-container-recursive-ownership-hints-v1')!
    expect(raw).not.toContain(command.location.path)
    expect(raw).not.toContain('planHash')
    expect(raw).not.toContain('uid')
    reconcileRecursiveOwnershipHints('admin', false)
    expect(recursiveOwnershipHint('admin', cid)).toBeNull()
    saveRecursiveOwnershipHint(hint)
    reconcileRecursiveOwnershipHints('other', true)
    expect(recursiveOwnershipHint('admin', cid)).toBeNull()
    clearRecursiveOwnershipHints()
  })
  it('enforces bounded hints and rejects corrupted storage', () => {
    for (let n = 0; n < 4; n++)
      saveRecursiveOwnershipHint({
        subject: 'admin',
        containerId: n.toString(16).repeat(64),
        key: id,
        id: null,
      })
    expect(() =>
      saveRecursiveOwnershipHint({
        subject: 'admin',
        containerId: 'f'.repeat(64),
        key: id,
        id: null,
      }),
    ).toThrow(/递归所有者/)
    sessionStorage.setItem('whaledeck-container-recursive-ownership-hints-v1', 'x'.repeat(8193))
    expect(recursiveOwnershipHint('admin', cid)).toBeNull()
  })
})
