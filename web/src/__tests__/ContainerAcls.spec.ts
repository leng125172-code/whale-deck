import { beforeEach, describe, it, expect, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  validAclEntries,
  aclMode,
  sameAclCommand,
  assertAclIntent,
  assertAclAdvance,
  queueAcl,
  getAcl,
  confirmAcl,
  cancelAcl,
  parseAclId,
  canAclContainerEntry,
} from '@/services/containerAcls'
import type {
  ContainerAclEntry,
  ContainerAclCommand,
  ContainerAclIntent,
  ContainerAclReceipt,
} from '@/services/containerAcls'
import {
  saveAclHint,
  aclHint,
  reconcileAclHints,
  clearAclHints,
} from '@/services/containerAclHints'
import type { FileEntry } from '@/services/fileManagement'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
const base = (): ContainerAclEntry[] => [
  { tag: 'user_obj', id: null, permissions: 6 },
  { tag: 'group_obj', id: null, permissions: 4 },
  { tag: 'other', id: null, permissions: 0 },
]
const named = (): ContainerAclEntry[] => [
  { tag: 'user_obj', id: null, permissions: 6 },
  { tag: 'user', id: 0, permissions: 0 },
  { tag: 'group_obj', id: null, permissions: 4 },
  { tag: 'mask', id: null, permissions: 6 },
  { tag: 'other', id: null, permissions: 0 },
]
const command = (): ContainerAclCommand => ({
  location: { containerId: cid, instanceId: '777:2026-11-03T01:02:03Z', path: 'work/acl.bin' },
  aclType: 'access',
  entries: named(),
  expectedVersion: 'A'.repeat(64),
})
function ready(): ContainerAclIntent {
  const expires = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: command(),
    state: 'Ready',
    kind: 'File',
    originalUid: 1000,
    originalGid: 1001,
    executorUid: 5100,
    executorGid: 5101,
    originalAccessAcl: base(),
    originalDefaultAcl: [],
    targetMode: 432,
    noChange: false,
    readyExpiresAtUtc: expires,
    resultCode: 'CONTAINER_ACL_READY',
    version: 2,
    publishedVersion: null,
    aclConfirmed: null,
    historyMayExist: false,
    historyVerified: false,
    historyDigest: null,
    receipt: null,
    confirmation: {
      planHash: 'original-confirmation',
      expiresAtUtc: expires,
      warning: '不递归，mask影响普通权限',
    },
  }
}
function state(value: string) {
  const r = ready()
  r.state = value
  r.confirmation = null
  r.version = 3
  r.resultCode = 'CONTAINER_ACL_TEST_RESULT'
  if (['Queued', 'Planning'].includes(value)) {
    r.kind = null
    r.originalUid = r.originalGid = r.executorUid = r.executorGid = null
    r.originalAccessAcl = r.originalDefaultAcl = null
    r.targetMode = null
    r.noChange = null
    r.readyExpiresAtUtc = null
    r.resultCode = null
  }
  if (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(value))
    r.executionJobId = execution
  if (['Succeeded', 'Superseded'].includes(value)) {
    r.aclConfirmed = true
    r.historyMayExist = r.historyVerified = true
    r.historyDigest = 'B'.repeat(64)
    r.publishedVersion = 'C'.repeat(64)
    r.receipt = receipt(r, 'Completed')
  }
  if (['Unknown', 'Acknowledged'].includes(value)) {
    r.historyMayExist = true
    r.receipt = receipt(r, 'Unknown')
  }
  return r
}
function receipt(r: ContainerAclIntent, s: ContainerAclReceipt['state']): ContainerAclReceipt {
  return {
    id: execution,
    containerId: cid,
    instanceId: r.command.location.instanceId,
    fingerprint: 'D'.repeat(64),
    targetProof: 'E'.repeat(64),
    state: s,
    version: s === 'Completed' ? r.publishedVersion! : '',
    bindingCurrent: s === 'Completed' && r.state !== 'Superseded',
    replayed: false,
    kind: r.kind!,
    aclConfirmed: s === 'Unknown' ? null : s === 'Completed',
    noChange: r.noChange!,
    historyMayExist: r.historyMayExist,
    historyVerified: r.historyVerified,
    historyDigest: r.historyDigest ?? '',
    aclType: r.command.aclType,
    targetMode: r.targetMode!,
  }
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  sessionStorage.clear()
})
describe('container ACL canonical target and immutable history', () => {
  it.each([
    'Queued',
    'Planning',
    'Ready',
    'Pending',
    'Applying',
    'Unknown',
    'Succeeded',
    'Superseded',
    'Canceled',
    'Failed',
    'Expired',
    'Acknowledged',
  ])('accepts coherent %s', (s) =>
    expect(() => assertAclIntent(s === 'Ready' ? ready() : state(s))).not.toThrow(),
  )
  it.each(['', '01', '1e2', '-1', '4294967295', '1.0', '0x10', ' 0'])(
    'does not coerce numerical ID %s',
    (v) => expect(Number.isNaN(parseAclId(v))).toBe(true),
  )
  it.each(['0', '4294967294'])('retains explicit ID %s', (v) =>
    expect(parseAclId(v)).toBe(Number(v)),
  )
  it('requires explicit nullable id and permission even when zero', () => {
    const e = named()
    expect(validAclEntries(e)).toBe(true)
    delete (e[1] as Partial<ContainerAclEntry>).id
    expect(validAclEntries(e)).toBe(false)
    expect(
      validAclEntries([
        { tag: 'user_obj', id: null },
        { tag: 'group_obj', id: null, permissions: 4 },
        { tag: 'other', id: null, permissions: 0 },
      ]),
    ).toBe(false)
  })
  it.each(['no-mask', 'duplicate', 'reverse', 'extra', 'sentinel', 'permission', 'too-many'])(
    'rejects unsafe entries %s',
    (cause) => {
      let e = named()
      if (cause === 'no-mask') e = e.filter((e) => e.tag !== 'mask')
      if (cause === 'duplicate') e.splice(2, 0, { ...e[1]! })
      if (cause === 'reverse') e.reverse()
      if (cause === 'extra') Object.assign(e[1]!, { owner: 1 })
      if (cause === 'sentinel') e[1]!.id = 4294967295
      if (cause === 'permission') e[1]!.permissions = 8
      if (cause === 'too-many') e = Array.from({ length: 65 }, () => ({ ...e[0]! }))
      expect(validAclEntries(e)).toBe(false)
    },
  )
  it('uses mask rather than group_obj and distinguishes explicit default clear', () => {
    expect(aclMode(named())).toBe(0o660)
    expect(aclMode(base())).toBe(0o640)
    expect(validAclEntries([])).toBe(false)
    expect(validAclEntries([], true)).toBe(true)
    const r = ready()
    r.kind = 'Directory'
    r.command.aclType = 'default'
    r.command.entries = []
    r.originalDefaultAcl = named()
    r.targetMode = 0o640
    expect(() => assertAclIntent(r)).not.toThrow()
    r.kind = 'File'
    expect(() => assertAclIntent(r)).toThrow(/ACL/)
  })
  it('same command compares every canonical entry by value', () => {
    const a = command(),
      b = structuredClone(a)
    expect(sameAclCommand(a, b)).toBe(true)
    b.entries[1]!.permissions = 1
    expect(sameAclCommand(a, b)).toBe(false)
  })
  it.each(['targetMode', 'noChange', 'originalAccess', 'default', 'kind'])(
    'rejects inconsistent original plan %s',
    (field) => {
      const r = ready()
      if (field === 'targetMode') r.targetMode = 511
      if (field === 'noChange') r.noChange = true
      if (field === 'originalAccess') r.originalAccessAcl = []
      if (field === 'default') r.originalDefaultAcl = base()
      if (field === 'kind') r.kind = null
      expect(() => assertAclIntent(r)).toThrow(/ACL/)
    },
  )
  it.each(['false', 'null', 'type', 'mode', 'history', 'version', 'binding'])(
    'rejects malformed Completed receipt %s',
    (field) => {
      const r = state('Succeeded')
      if (field === 'false') r.receipt!.aclConfirmed = false
      if (field === 'null') r.receipt!.aclConfirmed = null
      if (field === 'type') r.receipt!.aclType = 'default'
      if (field === 'mode') r.receipt!.targetMode = 511
      if (field === 'history') r.receipt!.historyDigest = 'F'.repeat(64)
      if (field === 'version') r.receipt!.version = 'F'.repeat(64)
      if (field === 'binding') r.receipt!.bindingCurrent = false
      expect(() => assertAclIntent(r)).toThrow(/ACL/)
    },
  )
  it('Unknown stays nullable, cannot claim zero change or a successful version', () => {
    const r = state('Unknown')
    expect(() => assertAclIntent(r)).not.toThrow()
    r.aclConfirmed = false
    expect(() => assertAclIntent(r)).toThrow(/ACL/)
    r.aclConfirmed = null
    r.publishedVersion = 'F'.repeat(64)
    expect(() => assertAclIntent(r)).toThrow(/ACL/)
  })
  it('acknowledgement keeps exact original receipt and cannot turn Unknown into another Root fact', () => {
    const a = state('Unknown'),
      b = structuredClone(a)
    b.state = 'Acknowledged'
    b.version++
    expect(() => assertAclAdvance(a, b)).not.toThrow()
    b.receipt!.replayed = true
    expect(() => assertAclAdvance(a, b)).toThrow(/ACL/)
  })
  it.each(['mode', 'entries', 'plan', 'expiry', 'version', 'regression'])(
    'refuses late state or different original authority %s',
    (field) => {
      const a = ready(),
        b = structuredClone(a)
      b.version++
      if (field === 'mode') b.targetMode = 511
      if (field === 'entries') b.command.entries[1]!.permissions = 1
      if (field === 'plan') b.confirmation!.planHash = 'replacement'
      if (field === 'expiry')
        b.readyExpiresAtUtc = b.confirmation!.expiresAtUtc = new Date(
          Date.now() + 600000,
        ).toISOString()
      if (field === 'version') b.version = 1
      if (field === 'regression') {
        a.state = 'Applying'
        a.confirmation = null
        a.executionJobId = execution
        b.state = 'Pending'
        b.confirmation = null
        b.executionJobId = execution
      }
      expect(() => assertAclAdvance(a, b)).toThrow(/ACL/)
    },
  )
  it('virtual Ready expiry does not replace its plan or create execution', () => {
    const a = ready(),
      b = structuredClone(a)
    b.state = 'Expired'
    b.confirmation = null
    expect(() => assertAclAdvance(a, b)).not.toThrow()
  })
  it('queue sends only explicit ACL fields and preserves original registration key', async () => {
    vi.mocked(apiRequest).mockResolvedValue(state('Queued'))
    await queueAcl(command(), id, new AbortController().signal)
    const [url, options] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toBe(`containers/${cid}/files/acls`)
    expect(JSON.parse(options!.body as string)).toEqual({
      instanceId: command().location.instanceId,
      path: command().location.path,
      aclType: 'access',
      entries: named(),
      expectedVersion: command().expectedVersion,
    })
    expect(options!.headers).toEqual({ 'Idempotency-Key': id })
  })
  it('lost registration queries same key rather than another POST', async () => {
    vi.mocked(apiRequest).mockResolvedValue(state('Queued'))
    await getAcl(cid, id, new AbortController().signal, true)
    expect(vi.mocked(apiRequest).mock.calls[0]![0]).toContain('/acls/by-key/' + id)
  })
  it('confirm/cancel stay bound to original intent/version and do not repeat on expiry', async () => {
    const r = ready()
    vi.mocked(apiRequest).mockResolvedValue(state('Pending'))
    await confirmAcl(r, new AbortController().signal)
    expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
      planHash: r.confirmation!.planHash,
      confirmed: true,
      version: 2,
    })
    vi.mocked(apiRequest).mockResolvedValue(state('Canceled'))
    await cancelAcl(r, new AbortController().signal)
    expect(vi.mocked(apiRequest).mock.calls[1]![0]).toContain('/acls/' + id + '/cancel')
    r.confirmation!.expiresAtUtc = r.readyExpiresAtUtc = new Date(Date.now() - 1).toISOString()
    await expect(confirmAcl(r, new AbortController().signal)).rejects.toThrow(/ACL/)
    expect(apiRequest).toHaveBeenCalledTimes(2)
  })
  it('session hints hold only IDs and clear on demotion or another subject', () => {
    const hint = { subject: 'acl-admin', containerId: cid, key: id, id: null }
    saveAclHint(hint)
    expect(aclHint('acl-admin', cid)).toEqual(hint)
    const raw = sessionStorage.getItem('whaledeck-container-acl-hints-v1')!
    expect(raw).not.toContain('entries')
    expect(raw).not.toContain('planHash')
    reconcileAclHints('acl-admin', false)
    expect(aclHint('acl-admin', cid)).toBeNull()
    saveAclHint(hint)
    reconcileAclHints('other', true)
    expect(aclHint('acl-admin', cid)).toBeNull()
    clearAclHints()
  })
  it('protected and special-mode entries cannot open the ACL editor', () => {
    const entry = {
      kind: 'File',
      isProtected: false,
      mode: '0640',
      version: 'A'.repeat(64),
    } as FileEntry
    expect(canAclContainerEntry(entry)).toBe(true)
    expect(canAclContainerEntry({ ...entry, isProtected: true })).toBe(false)
    expect(canAclContainerEntry({ ...entry, mode: '4755' })).toBe(false)
  })
})

it.each(['Pending', 'Applying', 'Unknown', 'Succeeded', 'Acknowledged'])(
  'registration cannot silently accept %s execution evidence; original GET can reconcile it',
  async (value) => {
    const row = state(value)
    vi.mocked(apiRequest).mockResolvedValue(row)
    await expect(queueAcl(command(), id, new AbortController().signal)).rejects.toThrow(
      /登记返回了执行状态/,
    )
    await expect(getAcl(cid, id, new AbortController().signal, true)).resolves.toEqual(row)
    expect(apiRequest).toHaveBeenCalledTimes(2)
  },
)
