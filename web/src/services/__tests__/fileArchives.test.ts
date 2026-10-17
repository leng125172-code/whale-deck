import { describe, expect, it } from 'vitest'
import {
  archiveAvailability,
  archiveCaptureState,
  archiveDate,
  archiveEligible,
  archiveLabel,
  archiveVersionReady,
} from '../fileArchives'
import type { FileVersion } from '../fileArchives'
import type { FileEntry } from '../fileManagement'

const entry: FileEntry = {
  name: '资料.txt',
  kind: 'File',
  sizeBytes: 7,
  modifiedAtUnixMs: 1760000000000,
  version: 'A'.repeat(64),
  mode: '0640',
  isProtected: false,
}
const version: FileVersion = {
  id: '11111111-1111-1111-1111-111111111111',
  rootId: 'test.files',
  path: '资料.txt',
  sourceVersion: 'A'.repeat(64),
  sizeBytes: 7,
  sha256: 'B'.repeat(64),
  capturedAtUtc: '2026-10-17T00:00:00Z',
  captureKind: 'Trash',
  captureState: 'Trashed',
  availability: 'Present',
  differentSourceDevice: false,
}
describe('archive display and action boundaries', () => {
  it('only offers ordinary unprotected bounded current files', () => {
    expect(archiveEligible(entry)).toBe(true)
    for (const changed of [
      { ...entry, kind: 'Directory' },
      { ...entry, isProtected: true },
      { ...entry, sizeBytes: -1 },
      { ...entry, sizeBytes: 1073741825 },
      { ...entry, version: 'bad' },
    ])
      expect(archiveEligible(changed)).toBe(false)
  })
  it('never represents pending, missing or corrupt copies as restorable', () => {
    expect(archiveVersionReady(version)).toBe(true)
    for (const changed of [
      { ...version, captureState: 'Pending' },
      { ...version, availability: 'Missing' },
      { ...version, availability: 'Invalid' },
      { ...version, id: '00000000-0000-0000-0000-000000000000' },
      { ...version, path: '../escape' },
      { ...version, sha256: 'bad' },
    ])
      expect(archiveVersionReady(changed)).toBe(false)
  })
  it('labels only the selected HDD copy and does not imply a fresh content check', () => {
    expect(archiveLabel('purge')).toBe('清理此 HDD 副本')
    expect(archiveAvailability('Present')).toBe('副本存在')
    expect(archiveAvailability('garbage')).toBe('未知状态')
    expect(archiveCaptureState('Pending')).toBe('回收结果待核实')
    expect(archiveDate('bad')).toBe('时间未知')
  })
})
