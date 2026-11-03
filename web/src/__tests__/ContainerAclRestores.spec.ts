import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import { assertAclRestore, queueAclRestore, getAclRestore } from '@/services/containerAclRestores'
import type { ContainerAclRestore } from '@/services/containerAclRestores'
import { aclHint, saveAclHint, clearAclHints } from '@/services/containerAclHints'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const cid = 'a'.repeat(64),
  id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12'
function row(): ContainerAclRestore {
  const location = {
    containerId: cid,
    instanceId: '777:2026-11-03T01:02:03Z',
    path: 'work/acl.bin',
  }
  const entries = [
    { tag: 'user_obj' as const, id: null, permissions: 0 },
    { tag: 'group_obj' as const, id: null, permissions: 0 },
    { tag: 'other' as const, id: null, permissions: 0 },
  ]
  return {
    source: {
      location,
      entry: {
        captureId: 'b'.repeat(32),
        historyDigest: 'C'.repeat(64),
        createdAtUnixMs: 0,
        sourceInstanceId: '666:2026-11-02T01:02:03Z',
        changedAclType: 'access',
        kind: 'File',
        originalMode: 0,
        accessEntryCount: 3,
        defaultEntryCount: 0,
      },
      accessAcl: entries,
      defaultAcl: [],
    },
    intent: {
      id,
      planningJobId: id,
      executionJobId: null,
      command: { location, aclType: 'access', entries, expectedVersion: 'A'.repeat(64) },
      state: 'Queued',
      kind: null,
      originalUid: null,
      originalGid: null,
      executorUid: null,
      executorGid: null,
      originalAccessAcl: null,
      originalDefaultAcl: null,
      targetMode: null,
      receipt: null,
      noChange: null,
      readyExpiresAtUtc: null,
      resultCode: null,
      version: 0,
      publishedVersion: null,
      aclConfirmed: null,
      historyMayExist: false,
      historyVerified: false,
      historyDigest: null,
      confirmation: null,
    },
  }
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  clearAclHints()
})
describe('server-authoritative ACL historical restoration', () => {
  it('keeps explicit zero source and refuses a different selected capture', () => {
    const value = row()
    expect(assertAclRestore(value, cid)).toBe(value)
    expect(() =>
      assertAclRestore(value, cid, id, {
        ...value.source,
        entry: { ...value.source.entry, captureId: 'd'.repeat(32) },
      }),
    ).toThrow('ACL历史快照')
  })
  it.each(['intent', 'source'])('rejects missing wrapper %s', (field) => {
    const value: Record<string, unknown> = { ...row() }
    delete value[field]
    expect(() => assertAclRestore(value as unknown as ContainerAclRestore, cid)).toThrow(
      'ACL历史恢复',
    )
  })
  it.each([
    'container',
    'instance',
    'path',
    'entries',
    'type',
    'digest',
    'private',
    'mode',
    'version',
  ])('rejects changed %s', (field) => {
    const original = row(),
      value = structuredClone(original)
    if (field === 'container') value.source.location.containerId = 'b'.repeat(64)
    if (field === 'instance') value.source.location.instanceId = '778:2026-11-03T01:02:03Z'
    if (field === 'path') value.source.location.path = 'other'
    if (field === 'entries') value.source.accessAcl[0]!.permissions = 1
    if (field === 'type') value.intent.command.aclType = 'default'
    if (field === 'digest') value.source.entry.historyDigest = 'D'.repeat(64)
    if (field === 'private') Object.assign(value.source, { owner: 'private' })
    if (field === 'mode') value.source.entry.originalMode = 1
    if (field === 'version') value.intent.command.expectedVersion = 'D'.repeat(64)
    expect(() =>
      assertAclRestore(value, cid, id, original.source, original.intent.command.expectedVersion),
    ).toThrow(/ACL/)
  })
  it('preserves only the selected empty default list for a directory', () => {
    const value = row()
    value.source.entry.kind = 'Directory'
    value.source.entry.changedAclType = 'default'
    value.intent.command.aclType = 'default'
    value.intent.command.entries = []
    expect(assertAclRestore(value, cid).intent.command.entries).toEqual([])
  })
  it('submits only source selection and current version, never ACL entries', async () => {
    const value = row()
    vi.mocked(apiRequest).mockResolvedValue(value)
    const signal = new AbortController().signal
    await queueAclRestore(
      {
        location: value.source.location,
        captureId: value.source.entry.captureId,
        expectedHistoryDigest: value.source.entry.historyDigest,
        expectedVersion: value.intent.command.expectedVersion,
      },
      id,
      value.source,
      signal,
    )
    const [url, request] = vi.mocked(apiRequest).mock.calls[0]!
    expect(url).toBe(`containers/${cid}/files/acls/restores`)
    expect(request?.headers).toEqual({ 'Idempotency-Key': id })
    expect(Object.keys(JSON.parse(request!.body as string))).toEqual([
      'instanceId',
      'path',
      'captureId',
      'expectedHistoryDigest',
      'expectedVersion',
    ])
  })
  it('requires original GET before accepting an advanced registration replay', async () => {
    const value = row(),
      intent = value.intent
    Object.assign(intent, {
      state: 'Pending',
      kind: 'File',
      originalUid: 1000,
      originalGid: 1000,
      executorUid: 1000,
      executorGid: 1000,
      originalAccessAcl: [
        { tag: 'user_obj', id: null, permissions: 6 },
        { tag: 'group_obj', id: null, permissions: 4 },
        { tag: 'other', id: null, permissions: 0 },
      ],
      originalDefaultAcl: [],
      targetMode: 0,
      noChange: false,
      readyExpiresAtUtc: new Date(Date.now() + 300000).toISOString(),
      executionJobId: 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12',
      version: 1,
      resultCode: 'CONTAINER_ACL_ACCEPTED',
    })
    vi.mocked(apiRequest).mockResolvedValue(value)
    const request = {
      location: value.source.location,
      captureId: value.source.entry.captureId,
      expectedHistoryDigest: value.source.entry.historyDigest,
      expectedVersion: intent.command.expectedVersion,
    }
    await expect(
      queueAclRestore(request, id, value.source, new AbortController().signal),
    ).rejects.toThrow('仅查询原任务')
    await expect(getAclRestore(cid, id, new AbortController().signal)).resolves.toBe(value)
  })
  it('queries the exact original reference without registration or confirmation', async () => {
    const value = row()
    vi.mocked(apiRequest).mockResolvedValue(value)
    await getAclRestore(cid, id, new AbortController().signal, true)
    expect(vi.mocked(apiRequest).mock.calls[0]![0]).toBe(
      `containers/${cid}/files/acls/restores/by-key/${id}`,
    )
    expect(vi.mocked(apiRequest).mock.calls[0]![1]?.method).toBeUndefined()
  })
  it('stores only a restore mode and original reference, accepts old ordinary hints', () => {
    saveAclHint({ subject: 'subject', containerId: cid, key: id, id: null, restore: true })
    expect(aclHint('subject', cid)?.restore).toBe(true)
    const text = sessionStorage.getItem('whaledeck-container-acl-hints-v1')!
    expect(text).not.toContain('capture')
    expect(text).not.toContain('Acl')
    saveAclHint({ subject: 'subject', containerId: cid, key: id, id: null })
    expect(aclHint('subject', cid)?.restore).toBeUndefined()
  })
})
