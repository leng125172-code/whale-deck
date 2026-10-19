import { describe, expect, it } from 'vitest'
import {
  assertPackingIntent,
  canPackDirectory,
  canReceivePacking,
  packingDownloadUrl,
  packingState,
  samePackingCommand,
  validPackingCommand,
} from '@/services/fileTreePackings'
import type { TreePackingIntent } from '@/services/fileTreePackings'

function ready(): TreePackingIntent {
  const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: {
      source: { rootId: 'test.tree', path: 'source' },
      expectedVersion: 'A'.repeat(64),
      target: { rootId: 'test.tree', path: '备份.tar.gz' },
      format: 'tar.gz',
    },
    entryCount: 10000,
    totalBytes: 16 * 1024 ** 3,
    readyExpiresAtUtc: new Date().toISOString(),
    resultCode: null,
    version: 3,
    publishedVersion: null,
    partialRetained: false,
    confirmation: {
      planHash: 'test-plan',
      expiresAtUtc: new Date().toISOString(),
      warning: '尚未压缩',
    },
    archiveBytes: null,
    archiveSha256: null,
  }
}
function completed(): TreePackingIntent {
  return {
    ...ready(),
    state: 'Succeeded',
    confirmation: null,
    executionJobId: '129f14ed-3254-4868-9885-acad522acefa',
    publishedVersion: 'F'.repeat(64),
    archiveBytes: 17 * 1024 ** 3,
    archiveSha256: 'B'.repeat(64),
  }
}
describe('directory packing contract and native streaming presentation', () => {
  it('never treats a plan or Unknown as a downloadable artifact', () => {
    expect(packingState('Ready')).toContain('尚未压缩')
    expect(packingState('PackingQueued')).toBe('等待压缩')
    for (const state of ['Ready', 'Unknown', 'Acknowledged', 'Canceled', 'Failed'])
      expect(() =>
        packingDownloadUrl({
          ...ready(),
          state,
          confirmation: state === 'Ready' ? ready().confirmation : null,
        }),
      ).toThrow('归档尚未完成')
  })
  it('requires independent source and target packing policies', () => {
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: ['files-list', 'files-download', 'files-upload', 'files-mutate', 'files-tree-copy'],
    }
    const entry = {
      name: 'source',
      kind: 'Directory',
      isProtected: false,
      version: 'A'.repeat(64),
      sizeBytes: 0,
      modifiedAtUnixMs: 0,
      mode: '0755',
    }
    expect(canPackDirectory(root, entry)).toBe(false)
    expect(canReceivePacking(root)).toBe(false)
    root.actions.push('files-archive', 'files-tree-pack')
    expect(canPackDirectory(root, entry)).toBe(true)
    expect(canReceivePacking(root)).toBe(true)
    expect(canPackDirectory(root, { ...entry, isProtected: true })).toBe(false)
    expect(canPackDirectory(root, { ...entry, kind: 'File' })).toBe(false)
  })
  it('binds literal paths, extension, format and non-overlap without case folding Linux names', () => {
    const command = ready().command
    expect(validPackingCommand(command)).toBe(true)
    for (const path of [
      '../a.tar.gz',
      '/a.tar.gz',
      'source/a.tar.gz',
      'source',
      'a.tar',
      'a.TAR.GZ',
    ])
      expect(validPackingCommand({ ...command, target: { ...command.target, path } })).toBe(false)
    expect(validPackingCommand({ ...command, format: 'zip' as 'tar' })).toBe(false)
    expect(samePackingCommand(command, { ...command, format: 'tar' })).toBe(false)
    expect(samePackingCommand(command, { ...command, expectedVersion: 'B'.repeat(64) })).toBe(false)
  })
  it('validates complete results up to 17GiB, never widening the old general download endpoint', () => {
    const row = completed()
    expect(assertPackingIntent(row, row.id)).toBe(row)
    const url = packingDownloadUrl(row)
    expect(url).toContain(
      `/files/tree-packings/${row.id}/download?expectedVersion=${row.publishedVersion}`,
    )
    expect(url).not.toContain('planHash')
    for (const invalid of [
      { ...row, archiveBytes: 0 },
      { ...row, archiveBytes: 17 * 1024 ** 3 + 1 },
      { ...row, archiveBytes: Number.MAX_SAFE_INTEGER + 1 },
      { ...row, archiveSha256: null },
      { ...row, publishedVersion: null },
      { ...row, executionJobId: null },
      { ...row, partialRetained: true },
      { ...row, entryCount: null },
      { ...ready(), archiveBytes: 7 },
      { ...ready(), state: 'Copying', confirmation: null },
      { ...row, command: { ...row.command, format: 'tar' as const } },
    ])
      expect(() => assertPackingIntent(invalid)).toThrow(/无法核实/)
  })
})
