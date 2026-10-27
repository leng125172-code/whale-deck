import { describe, expect, it } from 'vitest'
import { assertContainerHistoryPage, validContainerHistoryEntry } from '@/services/containerHistory'

const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-27T01:02:03.123456789Z',
  name: 'owned',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const entry = {
  captureId: 'a'.repeat(32),
  containerId: target.containerId,
  originalInstanceId: '666:2026-10-26T01:02:03.123456789Z',
  path: 'work/中文.txt',
  sourceVersion: 'A'.repeat(64),
  sizeBytes: 2097159,
  sha256: 'B'.repeat(64),
  createdAtUnixMs: 0,
  differentSourceDevice: false,
  availability: 'Present',
  captureHash: 'C'.repeat(64),
}
const page = {
  location: { containerId: target.containerId, instanceId: target.instanceId, path: '' },
  targetProof: 'D'.repeat(64),
  entries: [entry],
  version: 'E'.repeat(64),
  nextCursor: '',
  scanned: 1,
  invalidRecords: 0,
  complete: true,
}
describe('container-only HDD history scope and inventory', () => {
  it('keeps the exact current nanosecond instance while accepting a different original instance', () => {
    expect(assertContainerHistoryPage(page, target, '')).toBe(page)
    expect(page.entries[0]?.originalInstanceId).not.toBe(target.instanceId)
  })
  it.each([
    { captureId: '0'.repeat(32) },
    { captureId: 'a'.repeat(32) + '\n' },
    { containerId: 'A'.repeat(64) },
    { originalInstanceId: '666:2026-10-26T01:02:03Z\n' },
    { path: '../secret' },
    { path: '/absolute' },
    { sizeBytes: 1073741825 },
    { sizeBytes: 0.5 },
    { sha256: 'b'.repeat(64) },
    { availability: 'Complete' },
    { createdAtUnixMs: 253402300800000 },
    { captureHash: 'C'.repeat(64) + '\n' },
    { rootId: 'host-root' },
  ])('refuses unsafe or foreign source metadata %j', (change) =>
    expect(validContainerHistoryEntry({ ...entry, ...change })).toBe(false),
  )
  it.each([
    { location: { ...page.location, instanceId: '778:2026-10-27T01:02:03Z' } },
    { entries: [{ ...entry, containerId: 'b'.repeat(64) }] },
    { entries: [entry, entry], scanned: 2 },
    { scanned: 501 },
    { invalidRecords: 1 },
    { nextCursor: 'b'.repeat(32) },
    { complete: false },
    { sourceHostPath: '/private/path' },
  ])('refuses changed scope, duplicate pages and budgets %j', (change) =>
    expect(() => assertContainerHistoryPage({ ...page, ...change }, target, '')).toThrow(
      /容器历史/,
    ),
  )
  it('binds continuation to its original inventory version and exclusive cursor', () => {
    const after = '9'.repeat(32),
      version = page.version
    expect(assertContainerHistoryPage(page, target, '', after, version)).toBe(page)
    expect(() => assertContainerHistoryPage(page, target, '', entry.captureId, version)).toThrow(
      /容器历史/,
    )
    expect(() => assertContainerHistoryPage(page, target, '', after, 'F'.repeat(64))).toThrow(
      /容器历史/,
    )
  })
  it('does not hide incomplete captures or confuse availability with verified content', () => {
    for (const availability of ['Present', 'Incomplete', 'Missing', 'Invalid'])
      expect(validContainerHistoryEntry({ ...entry, availability })).toBe(true)
    expect(() => assertContainerHistoryPage(page, target, 'work/different.txt')).toThrow(/容器历史/)
  })
})
