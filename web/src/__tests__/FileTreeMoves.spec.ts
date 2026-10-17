import { describe, expect, it } from 'vitest'
import {
  assertTreeMoveIntent,
  canMoveDirectory,
  canReceiveMovedDirectory,
  moveTreeState,
  treeTerminal,
  validTreeLocation,
  moveFact,
  moveResult,
} from '@/services/fileTreeMoves'
import type { TreeMoveIntent } from '@/services/fileTreeMoves'
import type { FileEntry } from '@/services/fileManagement'

function ready(): TreeMoveIntent {
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
    destinationPublished: null,
    sourceRemoved: null,
    sourceRetained: null,
    partialRetained: false,
    confirmation: {
      planHash: 'test-only-plan',
      expiresAtUtc: new Date().toISOString(),
      warning: 'not copied yet',
    },
  }
}
describe('directory move workflow presentation', () => {
  it('explains protected and unknown outcomes without inventing success or rendering arbitrary details', () => {
    expect(moveResult('FILES_TREE_PROTECTED')).toContain('受保护')
    expect(moveResult('FILES_RESULT_UNKNOWN')).toContain('不自动重做')
    expect(() =>
      assertTreeMoveIntent({ ...ready(), resultCode: 'private/path or arbitrary exception' }),
    ).toThrow('目录移动')
  })
  it('preserves unknown facts and rejects missing, contradictory or fabricated success facts', () => {
    const row = ready()
    expect(moveFact(null)).not.toBe(moveFact(false))
    const completed = {
      ...row,
      state: 'Succeeded',
      confirmation: null,
      executionJobId: '129f14ed-3254-4868-9885-acad522acefa',
      publishedVersion: 'F'.repeat(64),
      destinationPublished: true,
      sourceRemoved: true,
      sourceRetained: true,
    }
    expect(assertTreeMoveIntent(completed)).toBe(completed)
    for (const invalid of [
      { ...completed, sourceRetained: false },
      { ...completed, destinationPublished: false },
      { ...completed, sourceRemoved: null },
      { ...completed, publishedVersion: null },
      { ...row, destinationPublished: true },
      { ...row, destinationPublished: undefined as unknown as boolean },
      { ...row, state: 'CopyQueued', confirmation: null },
      {
        ...row,
        state: 'Unknown',
        confirmation: null,
        destinationPublished: true,
        sourceRemoved: false,
        sourceRetained: false,
      },
    ])
      expect(() => assertTreeMoveIntent(invalid)).toThrow('目录移动')
  })
  it('accepts an explicitly partial unknown receipt without claiming source removal or retention completed', () => {
    const row = {
      ...ready(),
      state: 'Unknown',
      confirmation: null,
      executionJobId: '129f14ed-3254-4868-9885-acad522acefa',
      destinationPublished: true,
      sourceRemoved: false,
      sourceRetained: false,
      partialRetained: true,
    }
    expect(assertTreeMoveIntent(row)).toBe(row)
    expect(treeTerminal(row.state)).toBe(false)
    expect(() => assertTreeMoveIntent({ ...row, sourceRetained: true })).toThrow('目录移动')
  })
  it('keeps scan completion distinct from file publication and unknown distinct from terminal', () => {
    expect(moveTreeState('Ready')).toContain('尚未移动')
    expect(treeTerminal('Ready')).toBe(false)
    expect(treeTerminal('Unknown')).toBe(false)
    expect(treeTerminal('Acknowledged')).toBe(true)
    expect(moveTreeState('Acknowledged')).toContain('不代表成功')
    expect(moveTreeState('Succeeded')).toContain('原移动任务')
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
    expect(canMoveDirectory(root, entry)).toBe(false)
    expect(canReceiveMovedDirectory(root)).toBe(false)
    root.actions.push('files-tree-move')
    expect(canMoveDirectory(root, entry)).toBe(true)
    expect(canReceiveMovedDirectory(root)).toBe(true)
    expect(canMoveDirectory(root, { ...entry, isProtected: true })).toBe(false)
    expect(canMoveDirectory(root, { ...entry, kind: 'File' })).toBe(false)
  })
  it('checks UTF8 bytes, relative components and controls without normalizing valid Linux names', () => {
    expect(validTreeLocation({ rootId: 'test.tree', path: '目录/中文' })).toBe(true)
    for (const path of ['', '/etc', '../a', 'a//b', 'a\\b', 'a\n', 'a\u0085', '中'.repeat(86)])
      expect(validTreeLocation({ rootId: 'test.tree', path })).toBe(false)
    expect(validTreeLocation(null)).toBe(false)
  })
  it('accepts complete contract limits while rejecting overflow, wrong ownership references and unsafe versions', () => {
    const row = ready()
    expect(assertTreeMoveIntent(row, row.id)).toBe(row)
    for (const invalid of [
      { ...row, planningJobId: 'different' },
      { ...row, executionJobId: row.id },
      { ...row, version: Number.MAX_SAFE_INTEGER + 1 },
      { ...row, entryCount: 10001 },
      { ...row, totalBytes: 16 * 1024 ** 3 + 1 },
      { ...row, state: 'Imaginary' },
      { ...row, state: 'Moving' },
    ])
      expect(() => assertTreeMoveIntent(invalid)).toThrow('目录任务响应无法核实')
    expect(() => assertTreeMoveIntent(row, 'fc1cac12-5815-4426-b80e-23db4fd3e975')).toThrow(
      '目录任务响应无法核实',
    )
  })
})
