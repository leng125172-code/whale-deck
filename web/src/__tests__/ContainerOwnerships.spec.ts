import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertOwnershipIntent,
  assertOwnershipAdvance,
  canOwnershipContainerEntry,
  cancelOwnership,
  confirmOwnership,
  currentOwnershipInstance,
  getOwnership,
  ownershipTerminal,
  queueOwnership,
  validOwnershipCommand,
  validOwnershipId,
  parseOwnershipId,
} from '@/services/containerOwnerships'
import type { ContainerOwnershipIntent } from '@/services/containerOwnerships'
import {
  clearOwnershipHints,
  ownershipHint,
  reconcileOwnershipHints,
  saveOwnershipHint,
} from '@/services/containerOwnershipHints'
import type { FileEntry } from '@/services/fileManagement'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
const target = {
  containerId: cid,
  instanceId: '777:2026-10-30T01:02:03.123456789Z',
  name: 'owned',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const command = {
  location: { containerId: cid, instanceId: target.instanceId, path: 'work/所有者.bin' },
  uid: 2200,
  gid: 3200,
  expectedVersion: 'A'.repeat(64),
}
function ready(): ContainerOwnershipIntent {
  const expiresAtUtc = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: structuredClone(command),
    state: 'Ready',
    kind: 'File',
    originalUid: 1000,
    originalGid: 1001,
    executorUid: 5100,
    executorGid: 5101,
    noChange: false,
    readyExpiresAtUtc: expiresAtUtc,
    resultCode: 'CONTAINER_OWNERSHIP_READY',
    version: 2,
    publishedVersion: null,
    ownerConfirmed: null,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    confirmation: {
      planHash: 'opaque-original-confirmation',
      expiresAtUtc,
      warning: '尚未修改所有者',
    },
  }
}
function state(value: string) {
  const row = ready()
  row.state = value
  row.confirmation = null
  row.version = 3
  row.resultCode = 'CONTAINER_OWNERSHIP_TEST_RESULT'
  if (['Queued', 'Planning'].includes(value)) {
    row.kind = null
    row.originalUid = null
    row.originalGid = null
    row.executorUid = null
    row.executorGid = null
    row.noChange = null
    row.readyExpiresAtUtc = null
    row.resultCode = null
  }
  if (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(value))
    row.executionJobId = execution
  if (['Succeeded', 'Superseded'].includes(value)) {
    row.ownerConfirmed = true
    row.historyMayExist = true
    row.historyVerified = true
    row.historyDigest = 'B'.repeat(64)
    row.publishedVersion = 'C'.repeat(64)
  }
  return row
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  sessionStorage.clear()
})
describe('container ownership immutable metadata workflow', () => {
  it.each([
    'Queued',
    'Planning',
    'Pending',
    'Applying',
    'Unknown',
    'Succeeded',
    'Superseded',
    'Canceled',
    'Failed',
    'Expired',
    'Acknowledged',
  ])('accepts honest %s facts', (value) =>
    expect(assertOwnershipIntent(state(value))).toBeDefined(),
  )
  it('distinguishes no change from execution or content backup', () => {
    const row = ready()
    row.command.uid = 1000
    row.command.gid = 1001
    row.noChange = true
    expect(assertOwnershipIntent(row)).toBeDefined()
    row.noChange = false
    expect(() => assertOwnershipIntent(row)).toThrow(/所有者/)
  })
  it.each([
    null,
    undefined,
    false,
    '0',
    -1,
    0.5,
    4294967295,
    4294967296,
    Number.NaN,
    Number.POSITIVE_INFINITY,
  ])('refuses invalid numeric owner %s', (uid) => {
    expect(validOwnershipCommand({ ...command, uid } as unknown as typeof command)).toBe(false)
    expect(validOwnershipCommand({ ...command, gid: uid } as unknown as typeof command)).toBe(false)
  })
  it.each(['', '00', '-0', '0x10', '1e2', ' 0', '1.0', '0\n', '4294967295', 'Infinity'])(
    'does not coerce noncanonical owner input %s',
    (text) => expect(Number.isNaN(parseOwnershipId(text))).toBe(true),
  )
  it.each([0, 3200, 4294967294])('accepts explicit supported owner %s', (uid) => {
    expect(validOwnershipId(uid)).toBe(true)
    expect(parseOwnershipId(String(uid))).toBe(uid)
    expect(validOwnershipCommand({ ...command, uid, gid: uid })).toBe(true)
  })
  it.each([
    { uid: [2200] },
    { gid: { value: 3200 } },
    { expectedVersion: ['A'.repeat(64)] },
    { expectedVersion: 123 },
  ])('rejects coerced JSON values in the signed command: %j', (change) => {
    expect(validOwnershipCommand({ ...command, ...change } as unknown as typeof command)).toBe(
      false,
    )
  })
  it('does not apply content upload size limits to ownerships', () => {
    const entry = {
      name: 'large',
      kind: 'File',
      isProtected: false,
      mode: '0640',
      version: 'A'.repeat(64),
      sizeBytes: 10 * 1024 ** 3 + 7,
    } as FileEntry
    expect(canOwnershipContainerEntry(entry)).toBe(true)
    expect(canOwnershipContainerEntry({ ...entry, mode: '4640' })).toBe(false)
    expect(canOwnershipContainerEntry({ ...entry, isProtected: true })).toBe(false)
    expect(canOwnershipContainerEntry({ ...entry, kind: 'Symlink' })).toBe(false)
  })
  it.each(['owner', 'history', 'digest', 'kind', 'version', 'job', 'command', 'extra-mode'])(
    'rejects malformed completed %s',
    (field) => {
      const row = state('Succeeded')
      if (field === 'owner') row.ownerConfirmed = null
      if (field === 'history') row.historyVerified = false
      if (field === 'digest') row.historyDigest = 'invalid'
      if (field === 'kind') row.kind = null
      if (field === 'version') row.publishedVersion = null
      if (field === 'job') row.executionJobId = null
      if (field === 'command') row.command.location.containerId = 'b'.repeat(64)
      if (field === 'extra-mode') Object.assign(row.command, { recursive: true })
      expect(() => assertOwnershipIntent(row, cid)).toThrow(/所有者/)
    },
  )
  it('unknown stays unknown without inventing ownership false, yet preserves verified HDD metadata', () => {
    const row = state('Unknown')
    row.historyMayExist = true
    row.historyVerified = true
    row.historyDigest = 'B'.repeat(64)
    expect(assertOwnershipIntent(row).ownerConfirmed).toBeNull()
    expect(ownershipTerminal(row.state)).toBe(false)
    const ack = { ...row, state: 'Acknowledged', version: 4 }
    expect(() => assertOwnershipAdvance(row, ack)).not.toThrow(/所有者/)
    expect(() =>
      assertOwnershipAdvance(row, { ...ack, historyVerified: false, historyDigest: null }),
    ).toThrow(/所有者/)
    expect(() => assertOwnershipAdvance(row, { ...ack, historyMayExist: false })).toThrow(/所有者/)
  })
  it('known ownership and original confirmation cannot be rewritten by a later response', () => {
    const before = state('Succeeded')
    expect(() =>
      assertOwnershipAdvance(before, { ...before, version: 4, ownerConfirmed: false }),
    ).toThrow(/所有者/)
    const row = ready()
    expect(() => assertOwnershipAdvance(row, { ...row, version: 3, originalUid: 1002 })).toThrow(
      /所有者/,
    )
    expect(() => assertOwnershipAdvance(row, { ...row, version: 1 })).toThrow(/所有者/)
    expect(
      currentOwnershipInstance(row, { ...target, instanceId: '888:2026-10-30T01:02:03Z' }),
    ).toBe(false)
  })
  it.each(['planHash', 'expiresAtUtc', 'warning'] as const)(
    'a later Ready version cannot replace original confirmation %s',
    (field) => {
      const row = ready()
      const next = structuredClone(row)
      next.version++
      expect(() => assertOwnershipAdvance(row, next)).not.toThrow()
      next.confirmation![field] =
        field === 'expiresAtUtc'
          ? new Date(Date.parse(row.readyExpiresAtUtc!) + 1000).toISOString()
          : 'replacement'
      if (field === 'expiresAtUtc') next.readyExpiresAtUtc = next.confirmation!.expiresAtUtc
      expect(() => assertOwnershipIntent(next)).not.toThrow()
      expect(() => assertOwnershipAdvance(row, next)).toThrow(/不同的原计划/)
    },
  )
  it('confirmed failed execution needs an explicit no-change proof', () => {
    const row = state('Failed')
    row.executionJobId = execution
    expect(() => assertOwnershipIntent(row)).toThrow(/所有者/)
    row.ownerConfirmed = false
    expect(() => assertOwnershipIntent(row)).not.toThrow(/所有者/)
  })
  it('compares no-change against the inode owner, never the executor identity', () => {
    const row = ready()
    row.command.uid = row.executorUid!
    row.command.gid = row.executorGid!
    row.noChange = true
    expect(() => assertOwnershipIntent(row)).toThrow(/所有者/)
    row.noChange = false
    expect(() => assertOwnershipIntent(row)).not.toThrow()
    expect(() => assertOwnershipAdvance(row, { ...row, version: 3, executorUid: 5102 })).toThrow(
      /所有者/,
    )
  })
  it('queue sends only target/uid/gid/version and original canonical key', async () => {
    const queued = state('Queued')
    vi.mocked(apiRequest).mockResolvedValue(queued)
    await queueOwnership(command, id, new AbortController().signal)
    const [url, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toBe(`containers/${cid}/files/ownerships`)
    expect(JSON.parse(options!.body as string)).toEqual({
      instanceId: target.instanceId,
      path: command.location.path,
      uid: 2200,
      gid: 3200,
      expectedVersion: command.expectedVersion,
    })
    expect(options!.headers).toEqual({ 'Idempotency-Key': id })
  })
  it('lost registration uses by-key lookup instead of a replacement request', async () => {
    vi.mocked(apiRequest).mockResolvedValue(state('Queued'))
    await getOwnership(cid, id, new AbortController().signal, true)
    expect(vi.mocked(apiRequest).mock.calls[0]![0]).toContain('/ownerships/by-key/' + id)
  })
  it('confirmation and cancel keep the original intent and version', async () => {
    const row = ready()
    vi.mocked(apiRequest).mockResolvedValue(state('Pending'))
    await confirmOwnership(row, new AbortController().signal)
    const [url, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toContain('/ownerships/' + id + '/confirm')
    expect(JSON.parse(options!.body as string)).toEqual({
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: row.version,
    })
    vi.mocked(apiRequest).mockResolvedValue(state('Canceled'))
    await cancelOwnership(row, new AbortController().signal)
    expect(vi.mocked(apiRequest).mock.calls[1]![0]).toContain('/ownerships/' + id + '/cancel')
  })
  it('expired plans cannot send a confirmation', async () => {
    const row = ready()
    row.readyExpiresAtUtc = row.confirmation!.expiresAtUtc = new Date(Date.now() - 1).toISOString()
    await expect(confirmOwnership(row, new AbortController().signal)).rejects.toThrow(/所有者/)
    expect(apiRequest).not.toHaveBeenCalled()
  })
  it('session hints contain only IDs and are removed on logout or demotion', () => {
    const hint = { subject: 'admin', containerId: cid, key: id, id: null }
    saveOwnershipHint(hint)
    expect(ownershipHint('admin', cid)).toEqual(hint)
    const raw = sessionStorage.getItem('whaledeck-container-ownership-hints-v1')!
    expect(raw).not.toContain('path')
    expect(raw).not.toContain('planHash')
    reconcileOwnershipHints('admin', false)
    expect(ownershipHint('admin', cid)).toBeNull()
    saveOwnershipHint(hint)
    reconcileOwnershipHints('other', true)
    expect(ownershipHint('admin', cid)).toBeNull()
    saveOwnershipHint(hint)
    clearOwnershipHints()
    expect(ownershipHint('admin', cid)).toBeNull()
  })
})
