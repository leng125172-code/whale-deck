import { describe, expect, it } from 'vitest'
import {
  assertContainerAclHistoryPage,
  assertContainerAclHistorySnapshot,
  validContainerAclHistoryEntry,
} from '@/services/containerAclHistory'

const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-11-03T01:02:03.123456789Z',
  name: 'owned',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const path = 'work/中文.bin'
const location = { containerId: target.containerId, instanceId: target.instanceId, path }
const entry = {
  captureId: 'a'.repeat(32),
  historyDigest: 'B'.repeat(64),
  createdAtUnixMs: 0,
  sourceInstanceId: '666:2026-11-02T01:02:03Z',
  changedAclType: 'access',
  kind: 'File',
  originalMode: 0,
  accessEntryCount: 5,
  defaultEntryCount: 0,
} as const
const accessAcl = [
  { tag: 'user_obj', id: null, permissions: 0 },
  { tag: 'user', id: 0, permissions: 0 },
  { tag: 'group_obj', id: null, permissions: 0 },
  { tag: 'mask', id: null, permissions: 0 },
  { tag: 'other', id: null, permissions: 0 },
]
const page = {
  location,
  version: 'C'.repeat(64),
  entries: [entry],
  nextAfter: '',
  scannedCount: 1,
  invalidCount: 0,
  complete: true,
}
const snapshot = { location, entry, accessAcl, defaultAcl: [] }
describe('ACL history validates metadata, scope and explicit zero values', () => {
  it('keeps root ID, permissions, mode and epoch zero with empty default ACL', () => {
    expect(validContainerAclHistoryEntry(entry, target, path)).toBe(true)
    expect(assertContainerAclHistoryPage(page, target, path)).toBe(page)
    expect(assertContainerAclHistorySnapshot(snapshot, target, path, entry)).toBe(snapshot)
  })
  it.each(Object.keys(entry))(
    'requires summary field %s even when its legitimate value is zero',
    (field) => {
      const changed: Record<string, unknown> = { ...entry }
      delete changed[field]
      expect(validContainerAclHistoryEntry(changed, target, path)).toBe(false)
    },
  )
  it.each([
    { originalMode: false },
    { originalMode: '0' },
    { originalMode: 512 },
    { originalMode: 0.5 },
    { createdAtUnixMs: false },
    { createdAtUnixMs: 253402300800000 },
    { accessEntryCount: 2 },
    { defaultEntryCount: 1 },
    { defaultEntryCount: 3 },
    { changedAclType: ['access'] },
    { kind: ['File'] },
    { captureId: '0'.repeat(32) },
    { captureId: 'a'.repeat(32) + '\n' },
    { historyDigest: 'b'.repeat(64) },
    { sourceInstanceId: '666:bad' },
    { owner: 'secret' },
    { plan: {} },
  ])('rejects malformed/private summary %j', (changed) => {
    expect(validContainerAclHistoryEntry({ ...entry, ...changed }, target, path)).toBe(false)
  })
  it.each([
    { location: { ...location, containerId: 'b'.repeat(64) } },
    { location: { ...location, instanceId: '778:2026-11-03T01:02:03Z' } },
    { location: { ...location, path: 'other' } },
    { entries: [entry, entry], scannedCount: 2 },
    { scannedCount: 201 },
    { invalidCount: 1 },
    { scannedCount: false },
    { complete: false },
    { nextAfter: 'b'.repeat(32) },
    { version: 'C'.repeat(64) + '\n' },
    { privatePlan: {} },
  ])('rejects foreign/stale/budget/duplicate page %j', (changed) => {
    expect(() => assertContainerAclHistoryPage({ ...page, ...changed }, target, path)).toThrow(
      /ACL历史/,
    )
  })
  it('binds continuation to an exclusive cursor and its frozen inventory version', () => {
    const partial = { ...page, nextAfter: 'b'.repeat(32), complete: false }
    expect(assertContainerAclHistoryPage(partial, target, path, '9'.repeat(32), page.version)).toBe(
      partial,
    )
    expect(() =>
      assertContainerAclHistoryPage(partial, target, path, entry.captureId, page.version),
    ).toThrow(/ACL历史/)
    expect(() => assertContainerAclHistoryPage(partial, target, path, '', 'D'.repeat(64))).toThrow(
      /ACL历史/,
    )
  })
  it.each([
    'captureId',
    'historyDigest',
    'sourceInstanceId',
    'createdAtUnixMs',
    'changedAclType',
    'kind',
    'originalMode',
    'accessEntryCount',
    'defaultEntryCount',
  ])('requires the selected summary field %s to match', (field) => {
    const changed = { ...entry, [field]: field === 'createdAtUnixMs' ? 1 : 'other' }
    expect(() =>
      assertContainerAclHistorySnapshot({ ...snapshot, entry: changed }, target, path, entry),
    ).toThrow(/ACL历史/)
  })
  it('does not allow missing root ID/zero permissions or inconsistent mode/ACLs', () => {
    for (const field of ['id', 'permissions']) {
      const changed = structuredClone(snapshot) as unknown as {
        accessAcl: Record<string, unknown>[]
      }
      delete changed.accessAcl[1]![field]
      expect(() => assertContainerAclHistorySnapshot(changed, target, path, entry)).toThrow(
        /ACL历史/,
      )
    }
    const changed = structuredClone(snapshot)
    changed.accessAcl[0]!.permissions = 6
    expect(() => assertContainerAclHistorySnapshot(changed, target, path, entry)).toThrow(/ACL历史/)
    expect(() =>
      assertContainerAclHistorySnapshot({ ...snapshot, owner: 'private' }, target, path, entry),
    ).toThrow(/ACL历史/)
  })
})
