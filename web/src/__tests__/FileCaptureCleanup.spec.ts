import { describe, expect, it } from 'vitest'
import { assertCapturePage } from '@/services/fileTreeRecyclings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'
import { canRestoreCapture } from '@/services/fileTreeRestorings'
import { canOverwriteCapture } from '@/services/fileTreeOverwriteRestorings'

const root = {
  id: 'test.cleanup',
  name: 'Cleanup tests',
  actions: [
    'files-list',
    'files-download',
    'files-history',
    'files-tree-restore',
    'files-tree-overwrite-restore',
  ],
}
function capture(): TreeCapture {
  return {
    id: '129f14ed-3254-4868-9885-acad522acefa',
    intentId: 'eb3131c3-d685-422d-8e1b-139a335b22a7',
    source: { source: { rootId: root.id, path: 'historical' }, expectedVersion: 'A'.repeat(64) },
    manifestSha256: 'B'.repeat(64),
    entryCount: 4,
    totalBytes: 1024,
    archiveBytes: 2048,
    archiveSha256: 'C'.repeat(64),
    createdAtUtc: new Date().toISOString(),
    archiveState: 'Retained',
    originalsState: 'Retained',
    cleanupActive: false,
  }
}
describe('signed capture cleanup presentation boundaries', () => {
  it.each(['Purged', 'Uncertain'] as const)(
    'never offers either restoration for %s archive',
    (archiveState) => {
      const row = { ...capture(), archiveState }
      expect(assertCapturePage({ items: [row], next: null }, root.id).items[0]?.archiveState).toBe(
        archiveState,
      )
      expect(canRestoreCapture(root, row)).toBe(false)
      expect(canOverwriteCapture(root, row)).toBe(false)
    },
  )
  it('keeps recovery unavailable while cleanup is active even without a removal proof', () => {
    const row = { ...capture(), cleanupActive: true }
    expect(canRestoreCapture(root, row)).toBe(false)
    expect(canOverwriteCapture(root, row)).toBe(false)
  })
  it.each(['Purged', 'Uncertain'] as const)(
    'does not confuse %s originals with removal of an intact archive',
    (originalsState) => {
      const row = { ...capture(), originalsState }
      expect(canRestoreCapture(root, row)).toBe(true)
      expect(canOverwriteCapture(root, row)).toBe(true)
    },
  )
  it.each(['archiveState', 'originalsState', 'cleanupActive'])(
    'rejects missing %s rather than inventing retention',
    (field) => {
      const row = capture()
      Reflect.deleteProperty(row, field)
      expect(() => assertCapturePage({ items: [row], next: null }, root.id)).toThrow('捕获')
      expect(canRestoreCapture(root, row)).toBe(false)
      expect(canOverwriteCapture(root, row)).toBe(false)
    },
  )
  it('rejects arbitrary state aliases and string booleans', () => {
    for (const [field, value] of [
      ['archiveState', 'retained'],
      ['originalsState', 'Deleted'],
      ['cleanupActive', 'false'],
    ]) {
      const row = capture()
      Reflect.set(row, field!, value)
      expect(() => assertCapturePage({ items: [row], next: null }, root.id)).toThrow('捕获')
    }
  })
})
