import { describe, expect, it } from 'vitest'
import {
  assertOverwriteIntent,
  assertOverwriteProgress,
  canOverwriteCapture,
  canOverwriteDirectory,
  overwriteState,
  sameOverwriteCommand,
  sameOverwriteRequest,
  validOverwriteRequest,
} from '@/services/fileTreeOverwriteRestorings'
import type {
  TreeOverwriteRestoringIntent,
  TreeOverwriteRestoringRequest,
} from '@/services/fileTreeOverwriteRestorings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'

const planningId = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
const executionId = 'bfbe6c41-eaa6-4118-8e1b-e975b40c7dfa'
const captureId = '129f14ed-3254-4868-9885-acad522acefa'
const sourceVersion = 'A'.repeat(64)
const targetVersion = 'B'.repeat(64)
const sourceArchiveHash = 'C'.repeat(64)
const backupArchiveHash = 'D'.repeat(64)
const publishedVersion = 'E'.repeat(64)
const readyExpiry = '2099-01-01T00:05:00.000Z'
const maximumContentBytes = 16 * 1024 ** 3
const maximumArchiveBytes = 17 * 1024 ** 3

function capture(kind: 'Trash' | 'Overwrite' = 'Trash'): TreeCapture {
  return {
    id: captureId,
    intentId: 'eb3131c3-d685-422d-8e1b-139a335b22a7',
    source: {
      source: { rootId: 'test.tree', path: '历史/目录' },
      expectedVersion: sourceVersion,
    },
    manifestSha256: 'F'.repeat(64),
    entryCount: 10_000,
    totalBytes: maximumContentBytes,
    archiveBytes: maximumArchiveBytes,
    archiveSha256: sourceArchiveHash,
    createdAtUtc: new Date().toISOString(),
    kind,
    contentSha256: kind === 'Overwrite' ? '9'.repeat(64) : null,
    archiveState: 'Retained',
    originalsState: 'Retained',
    cleanupActive: false,
  }
}

function ready(kind: 'Trash' | 'Overwrite' = 'Trash'): TreeOverwriteRestoringIntent {
  const historical = capture(kind)
  return {
    id: planningId,
    planningJobId: planningId,
    executionJobId: null,
    state: 'Ready',
    command: {
      captureId: historical.id,
      source: { ...historical.source.source },
      target: { rootId: 'test.tree', path: '当前/目录' },
      targetExpectedVersion: targetVersion,
      captureKind: kind,
    },
    entryCount: historical.entryCount,
    totalBytes: historical.totalBytes,
    targetEntryCount: 10_000,
    targetTotalBytes: maximumContentBytes,
    archiveBytes: historical.archiveBytes,
    archiveSha256: historical.archiveSha256,
    readyExpiresAtUtc: readyExpiry,
    resultCode: null,
    version: 3,
    publishedVersion: null,
    partialRetained: false,
    confirmation: {
      planHash: 'fixture-plan',
      expiresAtUtc: readyExpiry,
      warning: '尚未覆盖恢复',
    },
    overwrittenCaptureId: null,
    backupMayBeRetained: false,
    backupVerified: false,
    originalsRetained: false,
    targetMayHaveChanged: false,
    backupArchiveBytes: null,
    backupArchiveSha256: null,
  }
}

function executing(
  state: 'OverwriteRestoringQueued' | 'OverwriteRestoring' | 'Unknown' | 'Acknowledged',
): TreeOverwriteRestoringIntent {
  return {
    ...ready(),
    state,
    executionJobId: executionId,
    confirmation: null,
    version: 4,
  }
}

function withVerifiedBackup(
  row: TreeOverwriteRestoringIntent = executing('Unknown'),
): TreeOverwriteRestoringIntent {
  return {
    ...row,
    overwrittenCaptureId: executionId,
    backupMayBeRetained: true,
    backupVerified: true,
    backupArchiveBytes: maximumArchiveBytes,
    backupArchiveSha256: backupArchiveHash,
  }
}

function succeeded(): TreeOverwriteRestoringIntent {
  return {
    ...withVerifiedBackup(executing('Unknown')),
    state: 'Succeeded',
    version: 5,
    publishedVersion,
    originalsRetained: true,
    targetMayHaveChanged: true,
  }
}

function withoutPlan(state: 'Queued' | 'Planning' | 'Failed' | 'Canceled' | 'Expired') {
  return {
    ...ready(),
    state,
    entryCount: null,
    totalBytes: null,
    targetEntryCount: null,
    targetTotalBytes: null,
    archiveBytes: null,
    archiveSha256: null,
    readyExpiresAtUtc: null,
    confirmation: null,
  } satisfies TreeOverwriteRestoringIntent
}

describe('directory overwrite restoration public contract and monotonic backup proof', () => {
  it('accepts exactly captureId, target and targetExpectedVersion in a queue request', () => {
    const request: TreeOverwriteRestoringRequest = {
      captureId,
      target: { rootId: 'test.tree', path: '当前/目录' },
      targetExpectedVersion: targetVersion,
    }
    expect(validOverwriteRequest(request)).toBe(true)
    expect(
      validOverwriteRequest({
        ...request,
        target: { ...request.target, hostPath: '/not-accepted' },
      } as unknown as TreeOverwriteRestoringRequest),
    ).toBe(false)
    expect(Object.keys(request).sort()).toEqual(['captureId', 'target', 'targetExpectedVersion'])
    expect(sameOverwriteRequest(request, { ...request })).toBe(true)
    expect(
      sameOverwriteRequest(request, { ...request, targetExpectedVersion: sourceVersion }),
    ).toBe(false)
    expect(validOverwriteRequest({ ...request, targetExpectedVersion: 'a'.repeat(64) })).toBe(false)
    expect(validOverwriteRequest({ ...request, targetExpectedVersion: '' })).toBe(false)
    expect(
      validOverwriteRequest({
        ...request,
        source: capture().source.source,
      } as unknown as TreeOverwriteRestoringRequest),
    ).toBe(false)
    expect(
      validOverwriteRequest({ ...request, captureId: '00000000-0000-0000-0000-000000000000' }),
    ).toBe(false)
  })

  it('requires an unprotected current directory and explicit source and target grants', () => {
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
        'files-tree-restore',
        'files-tree-overwrite-restore',
      ],
    }
    const directory = {
      name: '目录',
      kind: 'Directory',
      sizeBytes: maximumContentBytes,
      modifiedAtUnixMs: Date.now(),
      version: targetVersion,
      mode: '0750',
      isProtected: false,
    }
    expect(canOverwriteDirectory(root, directory)).toBe(true)
    expect(canOverwriteCapture(root, capture('Trash'))).toBe(true)
    expect(canOverwriteCapture(root, capture('Overwrite'))).toBe(true)
    for (const action of [
      'files-list',
      'files-download',
      'files-upload',
      'files-mutate',
      'files-archive',
      'files-history',
      'files-tree-restore',
      'files-tree-overwrite-restore',
    ])
      expect(
        canOverwriteDirectory(
          { ...root, actions: root.actions.filter((candidate) => candidate !== action) },
          directory,
        ),
      ).toBe(false)
    for (const action of [
      'files-list',
      'files-download',
      'files-history',
      'files-tree-restore',
      'files-tree-overwrite-restore',
    ])
      expect(
        canOverwriteCapture(
          { ...root, actions: root.actions.filter((candidate) => candidate !== action) },
          capture('Trash'),
        ),
      ).toBe(false)
    expect(canOverwriteDirectory(root, { ...directory, kind: 'File' })).toBe(false)
    expect(canOverwriteDirectory(root, { ...directory, isProtected: true })).toBe(false)
    expect(canOverwriteDirectory(root, { ...directory, version: 'bad' })).toBe(false)
    expect(canOverwriteCapture(root, { ...capture('Overwrite'), contentSha256: null })).toBe(false)
    expect(
      canOverwriteCapture(root, {
        ...capture('Trash'),
        kind: 'Unknown' as 'Trash',
      }),
    ).toBe(false)
  })

  it.each(['Trash', 'Overwrite'] as const)(
    'binds the frozen current target version and %s historical capture kind',
    (kind) => {
      const row = ready(kind)
      expect(assertOverwriteIntent(row)).toBe(row)
      expect(row.command.targetExpectedVersion).toBe(targetVersion)
      expect(row.command.captureKind).toBe(kind)
      expect(sameOverwriteCommand(row.command, { ...row.command })).toBe(true)
      expect(
        sameOverwriteCommand(row.command, {
          ...row.command,
          targetExpectedVersion: sourceVersion,
        }),
      ).toBe(false)
      expect(() =>
        assertOverwriteIntent({
          ...row,
          command: { ...row.command, captureKind: 'Unrecognized' as 'Trash' },
        }),
      ).toThrow(/无法核实/)
    },
  )

  it('keeps planning identity separate from every execution identity', () => {
    expect(assertOverwriteIntent(ready())).toBeTruthy()
    expect(assertOverwriteIntent(executing('OverwriteRestoring'))).toBeTruthy()
    expect(() => assertOverwriteIntent({ ...ready(), planningJobId: executionId })).toThrow(
      /无法核实/,
    )
    expect(() =>
      assertOverwriteIntent({ ...executing('OverwriteRestoring'), executionJobId: planningId }),
    ).toThrow(/无法核实/)
    expect(() =>
      assertOverwriteIntent({ ...executing('OverwriteRestoring'), executionJobId: captureId }),
    ).toThrow(/无法核实/)
  })

  it.each(['Queued', 'Planning', 'Failed', 'Canceled', 'Expired'] as const)(
    'accepts the %s state only without invented plan material',
    (state) => {
      const row = withoutPlan(state)
      expect(assertOverwriteIntent(row).state).toBe(state)
      expect(() => assertOverwriteIntent({ ...row, archiveBytes: 1 })).toThrow(/无法核实/)
    },
  )

  it.each(['OverwriteRestoringQueued', 'OverwriteRestoring', 'Unknown', 'Acknowledged'] as const)(
    'accepts the verified planned %s result state',
    (state) => {
      const row = executing(state)
      expect(assertOverwriteIntent(row).state).toBe(state)
      expect(overwriteState(state)).not.toContain('无法核实')
    },
  )

  it('accepts Ready and a fully verified Succeeded result without conflating them', () => {
    expect(assertOverwriteIntent(ready()).executionJobId).toBeNull()
    const row = succeeded()
    expect(assertOverwriteIntent(row)).toBe(row)
    expect(row.executionJobId).toBe(executionId)
    expect(row.overwrittenCaptureId).toBe(executionId)
    expect(row.publishedVersion).toBe(publishedVersion)
    expect(overwriteState('Ready')).toContain('尚未覆盖')
    expect(overwriteState('Succeeded')).toContain('完成')
  })

  it('accepts exact 10,000-entry, 16-GiB content and 17-GiB archive bounds only', () => {
    expect(assertOverwriteIntent(ready())).toBeTruthy()
    for (const change of [
      { entryCount: 10_001 },
      { entryCount: 0 },
      { totalBytes: maximumContentBytes + 1 },
      { targetEntryCount: 10_001 },
      { targetEntryCount: 0 },
      { targetTotalBytes: maximumContentBytes + 1 },
      { archiveBytes: maximumArchiveBytes + 1 },
      { archiveBytes: 0 },
      { archiveSha256: 'bad' },
    ])
      expect(() => assertOverwriteIntent({ ...ready(), ...change })).toThrow(/无法核实/)
  })

  it('requires verified backup identity, hash, bytes and retained facts to agree', () => {
    expect(assertOverwriteIntent(withVerifiedBackup())).toBeTruthy()
    for (const change of [
      { backupMayBeRetained: false },
      { overwrittenCaptureId: captureId },
      { overwrittenCaptureId: null },
      { backupArchiveBytes: 0 },
      { backupArchiveBytes: maximumArchiveBytes + 1 },
      { backupArchiveSha256: 'bad' },
    ])
      expect(() => assertOverwriteIntent({ ...withVerifiedBackup(), ...change })).toThrow(
        /无法核实/,
      )
    expect(() => assertOverwriteIntent({ ...executing('Unknown'), backupArchiveBytes: 1 })).toThrow(
      /无法核实/,
    )
    expect(() =>
      assertOverwriteIntent({ ...executing('Unknown'), targetMayHaveChanged: true }),
    ).toThrow(/无法核实/)
    expect(() =>
      assertOverwriteIntent({ ...withVerifiedBackup(), originalsRetained: true }),
    ).toThrow(/无法核实/)
  })

  it('rejects private material and any public shape expansion', () => {
    const row = ready()
    for (const changed of [
      { ...row, ownerHash: 'private' },
      { ...row, planJson: '{}' },
      { ...row, command: { ...row.command, captureOwner: 'private' } },
      { ...row, command: { ...row.command, originalBindingHash: 'private' } },
    ])
      expect(() =>
        assertOverwriteIntent(changed as unknown as TreeOverwriteRestoringIntent),
      ).toThrow(/无法核实/)
  })

  it('allows progress only when the frozen command, plan and execution identity stay monotonic', () => {
    const planned = ready()
    const running = executing('OverwriteRestoring')
    const backedUp = withVerifiedBackup({ ...running, state: 'Unknown', version: 5 })
    const completed = { ...succeeded(), version: 6 }
    expect(assertOverwriteProgress(planned, running)).toBe(running)
    expect(assertOverwriteProgress(running, backedUp)).toBe(backedUp)
    expect(assertOverwriteProgress(backedUp, completed)).toBe(completed)
    expect(assertOverwriteProgress(completed, completed)).toBe(completed)
  })

  it.each([
    [
      'command capture',
      (row: TreeOverwriteRestoringIntent) => ({
        ...row,
        command: { ...row.command, captureId: planningId },
      }),
    ],
    [
      'target version',
      (row: TreeOverwriteRestoringIntent) => ({
        ...row,
        command: { ...row.command, targetExpectedVersion: sourceVersion },
      }),
    ],
    ['version rollback', (row: TreeOverwriteRestoringIntent) => ({ ...row, version: 2 })],
    [
      'execution identity',
      (row: TreeOverwriteRestoringIntent) => ({
        ...row,
        executionJobId: 'e581559d-c5bc-4aa0-8a76-b098d35326b4',
      }),
    ],
    ['source count', (row: TreeOverwriteRestoringIntent) => ({ ...row, entryCount: 9_999 })],
    [
      'target bytes',
      (row: TreeOverwriteRestoringIntent) => ({
        ...row,
        targetTotalBytes: maximumContentBytes - 1,
      }),
    ],
    [
      'source archive',
      (row: TreeOverwriteRestoringIntent) => ({
        ...row,
        archiveSha256: '1'.repeat(64),
      }),
    ],
  ] as const)('rejects non-monotonic %s progress', (_label, mutate) => {
    const previous = executing('OverwriteRestoring')
    expect(() => assertOverwriteProgress(previous, mutate({ ...previous, version: 5 }))).toThrow(
      /已改变/,
    )
  })

  it.each([
    ['verified flag', { backupVerified: false }],
    ['capture id', { overwrittenCaptureId: captureId }],
    ['backup bytes', { backupArchiveBytes: maximumArchiveBytes - 1 }],
    ['backup hash', { backupArchiveSha256: '1'.repeat(64) }],
    ['retained flag', { backupMayBeRetained: false }],
    ['originals flag', { originalsRetained: false }],
    ['target-changed flag', { targetMayHaveChanged: false }],
  ])('rejects rollback of monotonic backup %s', (_label, change) => {
    const previous = {
      ...withVerifiedBackup(executing('Unknown')),
      originalsRetained: true,
      targetMayHaveChanged: true,
      version: 5,
    }
    expect(() => assertOverwriteProgress(previous, { ...previous, ...change, version: 6 })).toThrow(
      /无法核实|已改变/,
    )
  })

  it('freezes every terminal result instead of accepting later rewrites', () => {
    const result = succeeded()
    expect(assertOverwriteProgress(result, { ...result })).toEqual(result)
    const command = result.command
    expect(
      assertOverwriteProgress(result, {
        ...result,
        command: {
          targetExpectedVersion: command.targetExpectedVersion,
          captureKind: command.captureKind,
          source: command.source,
          target: command.target,
          captureId: command.captureId,
        },
      }),
    ).toEqual(result)
    expect(() =>
      assertOverwriteProgress(result, { ...result, version: result.version + 1 }),
    ).toThrow(/已改变/)
    expect(() =>
      assertOverwriteProgress(result, { ...result, resultCode: 'FILES_LATE_REWRITE' }),
    ).toThrow(/已改变/)
  })
  it('never substitutes a ready confirmation or accepts a ready row without one', () => {
    const previous = ready()
    expect(() => assertOverwriteIntent({ ...previous, confirmation: null })).toThrow(/无法核实/)
    expect(() =>
      assertOverwriteProgress(previous, {
        ...previous,
        confirmation: { ...previous.confirmation!, planHash: 'changed-plan' },
      }),
    ).toThrow(/已改变/)
  })
})
