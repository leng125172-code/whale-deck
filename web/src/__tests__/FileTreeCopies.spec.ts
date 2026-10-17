import { describe, expect, it } from 'vitest'
import {
  assertTreeIntent,
  canCopyDirectory,
  canReceiveDirectory,
  treeState,
  treeTerminal,
  validTreeLocation,
} from '@/services/fileTreeCopies'
import type { TreeCopyIntent } from '@/services/fileTreeCopies'
import type { FileEntry } from '@/services/fileManagement'

function ready(): TreeCopyIntent {
  const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: {
      source: { rootId: 'test.tree', path: 'source' },
      expectedVersion: 'A'.repeat(64),
      target: { rootId: 'test.tree', path: 'target' },
    },
    entryCount: 10000,
    totalBytes: 16 * 1024 ** 3,
    readyExpiresAtUtc: new Date().toISOString(),
    resultCode: null,
    version: 3,
    publishedVersion: null,
    partialRetained: false,
    confirmation: {
      planHash: 'test-only-plan',
      expiresAtUtc: new Date().toISOString(),
      warning: 'not copied yet',
    },
  }
}
describe('directory copy workflow presentation', () => {
  it('keeps scan completion distinct from file publication and unknown distinct from terminal', () => {
    expect(treeState('Ready')).toContain('尚未复制')
    expect(treeTerminal('Ready')).toBe(false)
    expect(treeTerminal('Unknown')).toBe(false)
    expect(treeTerminal('Acknowledged')).toBe(true)
    expect(treeState('Acknowledged')).toContain('不代表成功')
    expect(treeState('Succeeded')).toContain('原复制任务')
  })
  it('requires explicit independent read and write policy, never inferring tree permission from regular mutation', () => {
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: ['files-list', 'files-download', 'files-upload', 'files-mutate'],
    }
    const entry: FileEntry = {
      name: 'source',
      kind: 'Directory',
      sizeBytes: 0,
      modifiedAtUnixMs: 0,
      version: 'A'.repeat(64),
      mode: '0755',
      isProtected: false,
    }
    expect(canCopyDirectory(root, entry)).toBe(false)
    expect(canReceiveDirectory(root)).toBe(false)
    root.actions.push('files-tree-copy')
    expect(canCopyDirectory(root, entry)).toBe(true)
    expect(canReceiveDirectory(root)).toBe(true)
    expect(canCopyDirectory(root, { ...entry, isProtected: true })).toBe(false)
    expect(canCopyDirectory(root, { ...entry, kind: 'File' })).toBe(false)
  })
  it('checks UTF8 bytes, relative components and controls without normalizing valid Linux names', () => {
    expect(validTreeLocation({ rootId: 'test.tree', path: '目录/中文' })).toBe(true)
    for (const path of ['', '/etc', '../a', 'a//b', 'a\\b', 'a\n', 'a\u0085', '中'.repeat(86)])
      expect(validTreeLocation({ rootId: 'test.tree', path })).toBe(false)
    expect(validTreeLocation(null)).toBe(false)
  })
  it('accepts complete contract limits while rejecting overflow, wrong ownership references and unsafe versions', () => {
    const row = ready()
    expect(assertTreeIntent(row, row.id)).toBe(row)
    for (const invalid of [
      { ...row, planningJobId: 'different' },
      { ...row, executionJobId: row.id },
      { ...row, version: Number.MAX_SAFE_INTEGER + 1 },
      { ...row, entryCount: 10001 },
      { ...row, totalBytes: 16 * 1024 ** 3 + 1 },
      { ...row, state: 'Imaginary' },
      { ...row, state: 'Copying' },
    ])
      expect(() => assertTreeIntent(invalid)).toThrow('目录任务响应无法核实')
    expect(() => assertTreeIntent(row, 'fc1cac12-5815-4426-b80e-23db4fd3e975')).toThrow(
      '目录任务响应无法核实',
    )
  })
})
