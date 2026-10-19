import { describe, expect, it } from 'vitest'
import {
  assertCapturePage,
  assertRecyclingIntent,
  canRecycleDirectory,
  canRecycleRoot,
  recyclingState,
  sameRecyclingCommand,
} from '@/services/fileTreeRecyclings'
import type { TreeCapturePage, TreeRecyclingIntent } from '@/services/fileTreeRecyclings'

const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
const execution = 'bfbe6c41-eaa6-4118-8e1b-e975b40c7dfa'
function ready(): TreeRecyclingIntent {
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: {
      source: { rootId: 'test.tree', path: '资料/<img src=x>.目录' },
      expectedVersion: 'A'.repeat(64),
    },
    entryCount: 10000,
    totalBytes: 16 * 1024 ** 3,
    readyExpiresAtUtc: new Date(Date.now() + 300000).toISOString(),
    resultCode: null,
    version: 3,
    captureId: null,
    archiveBytes: null,
    archiveSha256: null,
    captureVerified: false,
    captureMayBeRetained: false,
    sourceMayHaveChanged: false,
    sourceRemoved: false,
    originalsRetained: false,
    confirmation: {
      planHash: 'fixture-confirmation',
      expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
      warning: '尚未回收',
    },
  }
}
function completed(): TreeRecyclingIntent {
  return {
    ...ready(),
    state: 'Succeeded',
    executionJobId: execution,
    confirmation: null,
    captureVerified: true,
    captureMayBeRetained: true,
    sourceMayHaveChanged: true,
    sourceRemoved: true,
    originalsRetained: true,
    captureId: execution,
    archiveBytes: 17 * 1024 ** 3,
    archiveSha256: 'B'.repeat(64),
  }
}
function captures(): TreeCapturePage {
  return {
    items: [
      {
        id: execution,
        intentId: id,
        source: ready().command,
        manifestSha256: 'C'.repeat(64),
        entryCount: 10000,
        totalBytes: 16 * 1024 ** 3,
        archiveBytes: 17 * 1024 ** 3,
        archiveSha256: 'D'.repeat(64),
        createdAtUtc: new Date().toISOString(),
      },
    ],
    next: null,
  }
}
describe('directory recycling facts and separate full-root capture catalogue', () => {
  it('requires every explicit source grant without assuming a directory fits the file transfer limit', () => {
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: [
        'files-list',
        'files-download',
        'files-mutate',
        'files-history',
        'files-archive',
        'files-tree-trash',
      ],
    }
    const entry = {
      name: '目录',
      kind: 'Directory',
      sizeBytes: 16 * 1024 ** 3,
      modifiedAtUnixMs: Date.now(),
      version: 'A'.repeat(64),
      mode: '0750',
      isProtected: false,
    }
    expect(canRecycleRoot(root)).toBe(true)
    expect(canRecycleDirectory(root, entry)).toBe(true)
    for (const action of root.actions)
      expect(
        canRecycleDirectory(
          { ...root, actions: root.actions.filter((value) => value !== action) },
          entry,
        ),
      ).toBe(false)
    expect(canRecycleDirectory(root, { ...entry, kind: 'File' })).toBe(false)
    expect(canRecycleDirectory(root, { ...entry, isProtected: true })).toBe(false)
    expect(canRecycleDirectory(undefined, entry)).toBe(false)
  })
  it('keeps plan completion separate from capture verification and source removal', () => {
    const row = ready()
    expect(assertRecyclingIntent(row)).toBe(row)
    expect(recyclingState('Ready')).toContain('尚未回收')
    const result = completed()
    expect(assertRecyclingIntent(result)).toBe(result)
    expect(sameRecyclingCommand(ready().command, result.command)).toBe(true)
    expect(
      sameRecyclingCommand(ready().command, { ...result.command, expectedVersion: 'F'.repeat(64) }),
    ).toBe(false)
    expect(() => assertRecyclingIntent(result, execution)).toThrow(/目录回收响应无法核实/)
  })
  it.each([
    'captureVerified',
    'captureMayBeRetained',
    'sourceMayHaveChanged',
    'sourceRemoved',
    'originalsRetained',
  ] as const)('never accepts completed without %s', (field) => {
    const row = completed()
    row[field] = false
    expect(() => assertRecyclingIntent(row)).toThrow(/目录回收响应无法核实/)
  })
  it('preserves null unknown side effects instead of inventing false or success', () => {
    const row = {
      ...ready(),
      state: 'Unknown',
      executionJobId: execution,
      confirmation: null,
      captureMayBeRetained: null,
      sourceMayHaveChanged: null,
      sourceRemoved: null,
      originalsRetained: null,
    }
    expect(assertRecyclingIntent(row).sourceRemoved).toBeNull()
    expect(() => assertRecyclingIntent({ ...row, sourceMayHaveChanged: true })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() => assertRecyclingIntent({ ...row, state: 'Succeeded' })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() =>
      assertRecyclingIntent({ ...row, sourceRemoved: 'false' } as unknown as TreeRecyclingIntent),
    ).toThrow(/目录回收响应无法核实/)
  })
  it.each([0, -1, 17 * 1024 ** 3 + 1, Number.NaN])(
    'rejects invalid verified archive byte proof %s',
    (archiveBytes) => {
      expect(() => assertRecyclingIntent({ ...completed(), archiveBytes })).toThrow(
        /目录回收响应无法核实/,
      )
    },
  )
  it('rejects missing plan, rebound captures and counterfeit input confirmations', () => {
    expect(() => assertRecyclingIntent({ ...ready(), entryCount: null })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() => assertRecyclingIntent({ ...completed(), captureId: id })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() => assertRecyclingIntent({ ...completed(), archiveSha256: 'bad' })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() => assertRecyclingIntent({ ...ready(), archiveBytes: 1 })).toThrow(
      /目录回收响应无法核实/,
    )
    expect(() =>
      assertRecyclingIntent({
        ...ready(),
        confirmation: { ...ready().confirmation!, planHash: {} },
      } as unknown as TreeRecyclingIntent),
    ).toThrow(/目录回收响应无法核实/)
    expect(() => assertRecyclingIntent({ ...completed(), state: 'Acknowledged' })).toThrow(
      /目录回收响应无法核实/,
    )
  })
  it('checks root scope, exact path, duplicate IDs and stable continuation without exposing old owner', () => {
    const page = captures()
    expect(assertCapturePage(page, 'test.tree')).toBe(page)
    expect(assertCapturePage(page, 'test.tree', ready().command.source.path)).toBe(page)
    expect(() => assertCapturePage(page, 'other.root')).toThrow(/目录捕获/)
    expect(() => assertCapturePage(page, 'test.tree', 'other/path')).toThrow(/目录捕获/)
    expect(() =>
      assertCapturePage({ items: [...page.items, ...page.items], next: null }, 'test.tree'),
    ).toThrow(/目录捕获/)
    expect(() => assertCapturePage({ ...page, next: id }, 'test.tree')).toThrow(/目录捕获/)
    expect(assertCapturePage({ ...page, next: execution }, 'test.tree').next).toBe(execution)
    const bad = captures()
    bad.items[0]!.archiveBytes = -1
    expect(() => assertCapturePage(bad, 'test.tree')).toThrow(/目录捕获/)
    expect(() =>
      assertCapturePage(
        { items: Array.from({ length: 26 }, () => page.items[0]!), next: null },
        'test.tree',
      ),
    ).toThrow(/目录捕获/)
  })
})
