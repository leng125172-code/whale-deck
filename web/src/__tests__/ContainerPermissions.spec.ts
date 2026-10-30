import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertPermissionIntent,
  assertPermissionAdvance,
  canPermissionContainerEntry,
  cancelPermission,
  confirmPermission,
  currentPermissionInstance,
  getPermission,
  permissionTerminal,
  queuePermission,
  validPermissionCommand,
} from '@/services/containerPermissions'
import type { ContainerPermissionIntent } from '@/services/containerPermissions'
import {
  clearPermissionHints,
  permissionHint,
  reconcilePermissionHints,
  savePermissionHint,
} from '@/services/containerPermissionHints'
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
  location: { containerId: cid, instanceId: target.instanceId, path: 'work/权限.bin' },
  mode: '0600',
  expectedVersion: 'A'.repeat(64),
}
function ready(): ContainerPermissionIntent {
  const expiresAtUtc = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: structuredClone(command),
    state: 'Ready',
    kind: 'File',
    originalMode: '0640',
    noChange: false,
    readyExpiresAtUtc: expiresAtUtc,
    resultCode: 'CONTAINER_PERMISSION_READY',
    version: 2,
    publishedVersion: null,
    modeConfirmed: null,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    confirmation: {
      planHash: 'opaque-original-confirmation',
      expiresAtUtc,
      warning: '尚未修改权限',
    },
  }
}
function state(value: string) {
  const row = ready()
  row.state = value
  row.confirmation = null
  row.version = 3
  row.resultCode = 'CONTAINER_PERMISSION_TEST_RESULT'
  if (['Queued', 'Planning'].includes(value)) {
    row.kind = null
    row.originalMode = null
    row.noChange = null
    row.readyExpiresAtUtc = null
    row.resultCode = null
  }
  if (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(value))
    row.executionJobId = execution
  if (['Succeeded', 'Superseded'].includes(value)) {
    row.modeConfirmed = true
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
describe('container permission immutable metadata workflow', () => {
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
    expect(assertPermissionIntent(state(value))).toBeDefined(),
  )
  it('distinguishes no change from execution or content backup', () => {
    const row = ready()
    row.command.mode = '0640'
    row.noChange = true
    expect(assertPermissionIntent(row)).toBeDefined()
    row.noChange = false
    expect(() => assertPermissionIntent(row)).toThrow(/权限/)
  })
  it.each(['4755', '2755', '1755', '755', '0999', ' 0640', '0640\n'])(
    'refuses unsupported mode %s',
    (mode) => expect(validPermissionCommand({ ...command, mode })).toBe(false),
  )
  it.each([
    { mode: ['0600'] },
    { mode: { value: '0600' } },
    { expectedVersion: ['A'.repeat(64)] },
    { expectedVersion: 123 },
  ])('rejects coerced JSON values in the signed command: %j', (change) => {
    expect(validPermissionCommand({ ...command, ...change } as unknown as typeof command)).toBe(
      false,
    )
  })
  it('does not apply content upload size limits to permissions', () => {
    const entry = {
      name: 'large',
      kind: 'File',
      isProtected: false,
      mode: '0640',
      version: 'A'.repeat(64),
      sizeBytes: 10 * 1024 ** 3 + 7,
    } as FileEntry
    expect(canPermissionContainerEntry(entry)).toBe(true)
    expect(canPermissionContainerEntry({ ...entry, mode: '4640' })).toBe(false)
    expect(canPermissionContainerEntry({ ...entry, isProtected: true })).toBe(false)
    expect(canPermissionContainerEntry({ ...entry, kind: 'Symlink' })).toBe(false)
  })
  it.each(['mode', 'history', 'digest', 'kind', 'version', 'job', 'command', 'extra-mode'])(
    'rejects malformed completed %s',
    (field) => {
      const row = state('Succeeded')
      if (field === 'mode') row.modeConfirmed = null
      if (field === 'history') row.historyVerified = false
      if (field === 'digest') row.historyDigest = 'invalid'
      if (field === 'kind') row.kind = null
      if (field === 'version') row.publishedVersion = null
      if (field === 'job') row.executionJobId = null
      if (field === 'command') row.command.location.containerId = 'b'.repeat(64)
      if (field === 'extra-mode') Object.assign(row.command, { recursive: true })
      expect(() => assertPermissionIntent(row, cid)).toThrow(/权限/)
    },
  )
  it('unknown stays unknown without inventing mode false, yet preserves verified HDD metadata', () => {
    const row = state('Unknown')
    row.historyMayExist = true
    row.historyVerified = true
    row.historyDigest = 'B'.repeat(64)
    expect(assertPermissionIntent(row).modeConfirmed).toBeNull()
    expect(permissionTerminal(row.state)).toBe(false)
    const ack = { ...row, state: 'Acknowledged', version: 4 }
    expect(() => assertPermissionAdvance(row, ack)).not.toThrow(/权限/)
    expect(() =>
      assertPermissionAdvance(row, { ...ack, historyVerified: false, historyDigest: null }),
    ).toThrow(/权限/)
    expect(() => assertPermissionAdvance(row, { ...ack, historyMayExist: false })).toThrow(/权限/)
  })
  it('known mode and original confirmation cannot be rewritten by a later response', () => {
    const before = state('Succeeded')
    expect(() =>
      assertPermissionAdvance(before, { ...before, version: 4, modeConfirmed: false }),
    ).toThrow(/权限/)
    const row = ready()
    expect(() =>
      assertPermissionAdvance(row, { ...row, version: 3, originalMode: '0660' }),
    ).toThrow(/权限/)
    expect(() => assertPermissionAdvance(row, { ...row, version: 1 })).toThrow(/权限/)
    expect(
      currentPermissionInstance(row, { ...target, instanceId: '888:2026-10-30T01:02:03Z' }),
    ).toBe(false)
  })
  it('confirmed failed execution needs an explicit no-change proof', () => {
    const row = state('Failed')
    row.executionJobId = execution
    expect(() => assertPermissionIntent(row)).toThrow(/权限/)
    row.modeConfirmed = false
    expect(() => assertPermissionIntent(row)).not.toThrow(/权限/)
  })
  it('queue sends only target/mode/version and original canonical key', async () => {
    const queued = state('Queued')
    vi.mocked(apiRequest).mockResolvedValue(queued)
    await queuePermission(command, id, new AbortController().signal)
    const [url, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toBe(`containers/${cid}/files/permissions`)
    expect(JSON.parse(options!.body as string)).toEqual({
      instanceId: target.instanceId,
      path: command.location.path,
      mode: '0600',
      expectedVersion: command.expectedVersion,
    })
    expect(options!.headers).toEqual({ 'Idempotency-Key': id })
  })
  it('lost registration uses by-key lookup instead of a replacement request', async () => {
    vi.mocked(apiRequest).mockResolvedValue(state('Queued'))
    await getPermission(cid, id, new AbortController().signal, true)
    expect(vi.mocked(apiRequest).mock.calls[0]![0]).toContain('/permissions/by-key/' + id)
  })
  it('confirmation and cancel keep the original intent and version', async () => {
    const row = ready()
    vi.mocked(apiRequest).mockResolvedValue(state('Pending'))
    await confirmPermission(row, new AbortController().signal)
    const [url, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toContain('/permissions/' + id + '/confirm')
    expect(JSON.parse(options!.body as string)).toEqual({
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: row.version,
    })
    vi.mocked(apiRequest).mockResolvedValue(state('Canceled'))
    await cancelPermission(row, new AbortController().signal)
    expect(vi.mocked(apiRequest).mock.calls[1]![0]).toContain('/permissions/' + id + '/cancel')
  })
  it('expired plans cannot send a confirmation', async () => {
    const row = ready()
    row.readyExpiresAtUtc = row.confirmation!.expiresAtUtc = new Date(Date.now() - 1).toISOString()
    await expect(confirmPermission(row, new AbortController().signal)).rejects.toThrow(/权限/)
    expect(apiRequest).not.toHaveBeenCalled()
  })
  it('session hints contain only IDs and are removed on logout or demotion', () => {
    const hint = { subject: 'admin', containerId: cid, key: id, id: null }
    savePermissionHint(hint)
    expect(permissionHint('admin', cid)).toEqual(hint)
    const raw = sessionStorage.getItem('whaledeck-container-permission-hints-v1')!
    expect(raw).not.toContain('path')
    expect(raw).not.toContain('planHash')
    reconcilePermissionHints('admin', false)
    expect(permissionHint('admin', cid)).toBeNull()
    savePermissionHint(hint)
    reconcilePermissionHints('other', true)
    expect(permissionHint('admin', cid)).toBeNull()
    savePermissionHint(hint)
    clearPermissionHints()
    expect(permissionHint('admin', cid)).toBeNull()
  })
})
