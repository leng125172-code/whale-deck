import { describe, expect, it } from 'vitest'
import {
  assertRestoringIntent,
  canReceiveRestoring,
  canRestoreCapture,
  sameRestoringCommand,
  sameRestoringRequest,
  validRestoringRequest,
  restoringState,
} from '@/services/fileTreeRestorings'
import type { TreeRestoringIntent } from '@/services/fileTreeRestorings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'

function capture(): TreeCapture {
  return {
    id: '129f14ed-3254-4868-9885-acad522acefa',
    intentId: 'eb3131c3-d685-422d-8e1b-139a335b22a7',
    source: {
      source: { rootId: 'test.tree', path: '历史/<img src=x>' },
      expectedVersion: 'A'.repeat(64),
    },
    manifestSha256: 'C'.repeat(64),
    entryCount: 10000,
    totalBytes: 16 * 1024 ** 3,
    archiveBytes: 17 * 1024 ** 3,
    archiveSha256: 'B'.repeat(64),
    createdAtUtc: new Date().toISOString(),
    archiveState: 'Retained',
    originalsState: 'Retained',
    cleanupActive: false,
  }
}
function ready(): TreeRestoringIntent {
  const item = capture(),
    id = 'fc1cac12-5815-4426-b80e-23db4fd3e974',
    expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: {
      captureId: item.id,
      source: item.source.source,
      target: { ...item.source.source },
      captureKind: 'Trash',
    },
    entryCount: item.entryCount,
    totalBytes: item.totalBytes,
    archiveBytes: item.archiveBytes,
    archiveSha256: item.archiveSha256,
    readyExpiresAtUtc: expiry,
    resultCode: null,
    version: 3,
    publishedVersion: null,
    partialRetained: false,
    confirmation: { planHash: 'fixture-plan', expiresAtUtc: expiry, warning: '尚未恢复' },
  }
}
describe('directory restoring public capture and input proof', () => {
  it('requires independent source and target root grants, not only history access', () => {
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: [
        'files-list',
        'files-download',
        'files-history',
        'files-upload',
        'files-mutate',
        'files-archive',
      ],
    }
    expect(canRestoreCapture(root, capture())).toBe(false)
    expect(canReceiveRestoring(root)).toBe(false)
    root.actions.push('files-tree-restore')
    expect(canRestoreCapture(root, capture())).toBe(true)
    expect(canReceiveRestoring(root)).toBe(true)
    expect(canRestoreCapture(undefined, capture())).toBe(false)
    expect(canRestoreCapture({ ...root, id: 'another.root' }, capture())).toBe(false)
    expect(
      canRestoreCapture(root, { ...capture(), id: '00000000-0000-0000-0000-000000000000' }),
    ).toBe(false)
    expect(canRestoreCapture(root, { ...capture(), archiveSha256: 'bad' })).toBe(false)
    expect(
      canReceiveRestoring({
        ...root,
        actions: root.actions.filter((action) => action !== 'files-upload'),
      }),
    ).toBe(false)
  })
  it('allows the historical source path and sends only capture reference plus target', () => {
    const command = ready().command,
      request = { captureId: command.captureId, target: command.target }
    expect(validRestoringRequest(request)).toBe(true)
    expect(Object.keys(request).sort()).toEqual(['captureId', 'target'])
    expect(sameRestoringRequest(command, request)).toBe(true)
    expect(
      sameRestoringCommand(command, { ...command, source: { ...command.source, path: 'changed' } }),
    ).toBe(false)
    expect(sameRestoringRequest(request, { ...request, captureId: ready().id })).toBe(false)
    for (const path of ['', '../escape', '/absolute', 'a//b', 'a\\b', 'a/../b'])
      expect(validRestoringRequest({ ...request, target: { ...request.target, path } })).toBe(false)
    expect(
      validRestoringRequest({ ...request, captureId: '00000000-0000-0000-0000-000000000000' }),
    ).toBe(false)
  })
  it.each([
    'Ready',
    'RestoringQueued',
    'Restoring',
    'Unknown',
    'Succeeded',
    'Acknowledged',
    'Failed',
    'Canceled',
    'Expired',
  ])('%s retains verified historical archive proof without inventing a source version', (state) => {
    const row = {
      ...ready(),
      state,
      confirmation: state === 'Ready' ? ready().confirmation : null,
      executionJobId: state === 'Ready' ? null : 'e581559d-c5bc-4aa0-8a76-b098d35326b4',
      publishedVersion: state === 'Succeeded' ? 'F'.repeat(64) : null,
      partialRetained: state === 'Unknown',
    }
    expect(assertRestoringIntent(row)).toBe(row)
    expect('expectedVersion' in row.command).toBe(false)
    for (const change of [
      { archiveBytes: null },
      { archiveBytes: 0 },
      { archiveBytes: 17 * 1024 ** 3 + 1 },
      { archiveSha256: 'bad' },
      { totalBytes: null },
      { entryCount: 10001 },
    ])
      expect(() => assertRestoringIntent({ ...row, ...change })).toThrow(/无法核实/)
  })
  it('validates no-plan states and refuses private payloads and foreign states', () => {
    const row = ready()
    for (const state of ['Queued', 'Planning', 'Failed', 'Canceled'])
      expect(
        assertRestoringIntent({
          ...row,
          state,
          confirmation: null,
          entryCount: null,
          totalBytes: null,
          archiveBytes: null,
          archiveSha256: null,
          readyExpiresAtUtc: null,
        }).state,
      ).toBe(state)
    for (const change of [
      { state: 'Unpacking' },
      { state: 'Succeeded' },
      { state: 'Unknown', confirmation: null },
      { publishedVersion: 'F'.repeat(64) },
      { planningJobId: capture().id },
      { executionJobId: row.id },
      { ownerHash: 'private' },
      { command: { ...row.command, captureOwner: 'private' } },
      { command: { ...row.command, captureKind: 'Unrecognized' as 'Trash' } },
    ])
      expect(() => assertRestoringIntent({ ...row, ...change })).toThrow(/无法核实/)
    expect(() => assertRestoringIntent(row, capture().id)).toThrow(/无法核实/)
    expect(restoringState('Unknown')).toContain('未知')
    expect(restoringState('Ready')).toContain('尚未恢复')
  })
  it('accepts a catalogue-verified overwritten backup without changing absent-target semantics', () => {
    const row = ready()
    row.command.captureKind = 'Overwrite'
    expect(assertRestoringIntent(row)).toBe(row)
    expect(
      validRestoringRequest({ captureId: row.command.captureId, target: row.command.target }),
    ).toBe(true)
    expect('targetExpectedVersion' in row.command).toBe(false)
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: ['files-list', 'files-download', 'files-history', 'files-tree-restore'],
    }
    const backup: TreeCapture = { ...capture(), kind: 'Overwrite', contentSha256: 'E'.repeat(64) }
    expect(canRestoreCapture(root, backup)).toBe(true)
    expect(canRestoreCapture(root, { ...backup, contentSha256: null })).toBe(false)
    expect(
      canRestoreCapture(root, { ...capture(), kind: 'Trash', contentSha256: 'E'.repeat(64) }),
    ).toBe(false)
  })
})
