import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertContainerRecursiveAclHistoryPage as pageProof,
  assertContainerRecursiveAclHistorySnapshot as snapshotProof,
  validContainerRecursiveAclHistorySummary as summaryProof,
  recursiveHistoryEntryOrder,
  readContainerRecursiveAclHistory,
  listContainerRecursiveAclHistory,
} from '@/services/containerRecursiveAclHistory'
vi.mock('@/services/apiClient', () => ({ apiRequest: vi.fn<typeof apiRequest>() }))
const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-11-04T01:02:03Z',
  name: 'test',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000:1000',
}
const path = 'work/资料',
  location = { containerId: target.containerId, instanceId: target.instanceId, path }
const summary = {
  captureId: 'a'.repeat(32),
  historyDigest: 'B'.repeat(64),
  createdAtUnixMs: 0,
  sourceInstanceId: '666:2026-11-03T01:02:03Z',
  changedAclType: 'access' as const,
  manifestDigest: 'D'.repeat(64),
  entryCount: 2,
  fileCount: 1,
  directoryCount: 1,
  changeCount: 0,
}
const acl = [
  { tag: 'user_obj', id: null, permissions: 0 },
  { tag: 'user', id: 0, permissions: 0 },
  { tag: 'group_obj', id: null, permissions: 0 },
  { tag: 'mask', id: null, permissions: 0 },
  { tag: 'other', id: null, permissions: 0 },
]
const item = {
  relativePath: 'child.bin',
  kind: 'File',
  originalMode: 0,
  accessAcl: acl,
  defaultAcl: [],
}
const snapshot = { location, summary, offset: 0, entries: [item], complete: false, nextOffset: 1 }
const page = {
  location,
  version: 'C'.repeat(64),
  entries: [summary],
  nextAfter: '',
  scannedCount: 1,
  invalidCount: 0,
  complete: true,
}
beforeEach(() => vi.mocked(apiRequest).mockReset())
describe('recursive ACL history proofs', () => {
  it('preserves zero-change plan, named ID zero, mode zero and independent old instance', () => {
    expect(summaryProof(summary, target, path)).toBe(true)
    expect(pageProof(page, target, path)).toBe(page)
    expect(snapshotProof(snapshot, target, path, summary, 0)).toBe(snapshot)
    const last = {
      ...snapshot,
      offset: 1,
      entries: [{ ...item, relativePath: '', kind: 'Directory' }],
      complete: true,
      nextOffset: null,
    }
    expect(snapshotProof(last, target, path, summary, 1)).toBe(last)
  })
  it.each(Object.keys(summary))('rejects omitted summary field %s', (key) => {
    const value: Record<string, unknown> = { ...summary }
    delete value[key]
    expect(summaryProof(value, target, path)).toBe(false)
  })
  it.each([
    { entryCount: true },
    { changeCount: '0' },
    { directoryCount: 0 },
    { fileCount: 2 },
    { changedAclType: 'default', changeCount: 2 },
    { createdAtUnixMs: Number.MAX_SAFE_INTEGER + 1 },
    { historyDigest: 'bad' },
    { owner: 'private' },
  ])('rejects forged summary %j', (changed) =>
    expect(summaryProof({ ...summary, ...changed }, target, path)).toBe(false),
  )
  it.each([
    { complete: 1 },
    { invalidCount: 1 },
    { nextAfter: 'a'.repeat(32) },
    { scannedCount: 201 },
    { version: 'bad' },
    { entries: [summary, summary] },
    { owner: 'private' },
  ])('rejects inventory proof %j', (changed) =>
    expect(() => pageProof({ ...page, ...changed }, target, path)).toThrow(/历史/),
  )
  it('requires inventory continuity even across an empty incomplete scan', () => {
    const value = { ...page, entries: [], complete: false, nextAfter: '9'.repeat(32) }
    expect(pageProof(value, target, path, '', 'C'.repeat(64))).toBe(value)
    expect(() => pageProof(value, target, path, '9'.repeat(32), 'C'.repeat(64))).toThrow(/历史/)
    expect(() => pageProof(value, target, path, '', 'E'.repeat(64))).toThrow(/历史/)
  })
  it.each([
    { offset: 1 },
    { nextOffset: 0 },
    { nextOffset: null },
    { complete: true },
    { summary: { ...summary, historyDigest: 'E'.repeat(64) } },
    { location: { ...location, path: 'other' } },
    { entries: [] },
    { privatePlan: {} },
  ])('rejects mismatched entry page %j', (changed) =>
    expect(() => snapshotProof({ ...snapshot, ...changed }, target, path, summary, 0)).toThrow(
      /前像/,
    ),
  )
  it.each([
    'missing-id',
    'missing-permissions',
    'file-default',
    'mode',
    'root-early',
    'parent-path',
    'private',
    'depth',
  ])('rejects unverified original ACL/path %s', (change) => {
    const value = structuredClone(snapshot)
    if (change === 'missing-id')
      delete (value.entries[0]!.accessAcl[1] as Partial<(typeof acl)[number]>).id
    if (change === 'missing-permissions')
      delete (value.entries[0]!.accessAcl[0] as Partial<(typeof acl)[number]>).permissions
    if (change === 'file-default') Object.assign(value.entries[0]!, { defaultAcl: acl })
    if (change === 'mode') value.entries[0]!.originalMode = 1
    if (change === 'root-early') value.entries[0]!.relativePath = ''
    if (change === 'parent-path') value.entries[0]!.relativePath = '../private'
    if (change === 'private') Object.assign(value.entries[0]!, { owner: 'private' })
    if (change === 'depth') value.entries[0]!.relativePath = Array(33).fill('a').join('/')
    expect(() => snapshotProof(value, target, path, summary, 0)).toThrow(/前像/)
  })
  it('orders by depth then UTF8, rather than UTF16', () => {
    expect(recursiveHistoryEntryOrder('sub/file', 'a')).toBeLessThan(0)
    expect(recursiveHistoryEntryOrder('\uE000', '\u{10000}')).toBeLessThan(0)
    expect(recursiveHistoryEntryOrder('a', '')).toBeLessThan(0)
  })
  it('sends explicit offset and take with the immutable digest and abort signal', async () => {
    vi.mocked(apiRequest).mockResolvedValue(snapshot)
    const signal = new AbortController().signal
    await readContainerRecursiveAclHistory(target, path, summary, 0, signal)
    const [raw, options] = vi.mocked(apiRequest).mock.calls[0]!,
      query = new URL(String(raw), 'http://fixture').searchParams
    expect(String(raw)).toContain('/recursive-acls/history/' + summary.captureId)
    expect(query.get('expectedHistoryDigest')).toBe(summary.historyDigest)
    expect(query.get('offset')).toBe('0')
    expect(query.get('take')).toBe('25')
    expect(options?.signal).toBe(signal)
  })
  it('rejects out-of-range selections without network access', async () => {
    await expect(
      readContainerRecursiveAclHistory(target, path, summary, 2, new AbortController().signal),
    ).rejects.toThrow(/历史/)
    await expect(
      listContainerRecursiveAclHistory(
        target,
        path,
        'a'.repeat(32),
        '',
        new AbortController().signal,
      ),
    ).rejects.toThrow(/历史/)
    expect(apiRequest).not.toHaveBeenCalled()
  })
})
