import { describe, expect, it } from 'vitest'
import {
  assertPurgingIntent,
  canPurgeCapture,
  canPurgeRoot,
  purgingFollows,
  purgingScopes,
  samePurgingCommand,
  samePurgingRequest,
  validPurgingRequest,
} from '@/services/fileTreePurgings'
import type {
  PurgingScope,
  TreePurgingIntent,
  TreePurgingRequest,
} from '@/services/fileTreePurgings'
import { validRecoveryPurge } from '@/services/fileRecovery'
import type { RecoveryPurge } from '@/services/fileRecovery'
import type { FileRoot } from '@/services/fileManagement'
import type { TreeCapture } from '@/services/fileTreeRecyclings'

const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
const executionId = 'bfbe6c41-eaa6-4118-8e1b-e975b40c7dfa'
const captureId = '129f14ed-3254-4868-9885-acad522acefa'
const readyExpiry = '2099-01-01T00:05:00.000Z'
const maximumBytes = 16 * 1024 ** 3
const maximumArchiveBytes = 17 * 1024 ** 3
const grants = [
  'files-list',
  'files-download',
  'files-mutate',
  'files-history',
  'files-archive',
  'files-tree-purge',
]
function root(): FileRoot {
  return { id: 'test.tree', name: 'test', actions: [...grants] }
}
function capture(kind: 'Trash' | 'Overwrite' = 'Trash'): TreeCapture {
  return {
    id: captureId,
    intentId: id,
    source: {
      source: { rootId: 'test.tree', path: '历史/<img src=x>/目录' },
      expectedVersion: 'A'.repeat(64),
    },
    manifestSha256: 'B'.repeat(64),
    entryCount: 10000,
    totalBytes: maximumBytes,
    archiveBytes: maximumArchiveBytes,
    archiveSha256: 'C'.repeat(64),
    createdAtUtc: '2026-10-20T01:00:00Z',
    kind,
    contentSha256: kind === 'Overwrite' ? 'D'.repeat(64) : null,
    archiveState: 'Retained',
    originalsState: 'Retained',
    cleanupActive: false,
  }
}
function ready(
  scope: PurgingScope = 'All',
  kind: 'Trash' | 'Overwrite' = 'Trash',
): TreePurgingIntent {
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: { captureId, source: capture().source.source, captureKind: kind, scope },
    entryCount: 10000,
    totalBytes: maximumBytes,
    archiveBytes: maximumArchiveBytes,
    readyExpiresAtUtc: readyExpiry,
    resultCode: null,
    version: 3,
    deletionStarted: false,
    archiveRemoved: false,
    originalsRemoved: false,
    confirmation: { planHash: 'fixture-plan', expiresAtUtc: readyExpiry, warning: '尚未删除' },
  }
}
function executed(scope: PurgingScope = 'All', state = 'Unknown'): TreePurgingIntent {
  const complete = state === 'Succeeded'
  return {
    ...ready(scope),
    executionJobId: executionId,
    state,
    version: 4,
    deletionStarted: complete ? true : null,
    archiveRemoved: scope === 'Originals' ? false : complete ? true : null,
    originalsRemoved: scope === 'Archive' ? false : complete ? true : null,
    confirmation: null,
  }
}
function facts(row: TreePurgingIntent = executed()): RecoveryPurge {
  return {
    captureId: row.command.captureId,
    captureKind: row.command.captureKind,
    scope: row.command.scope,
    version: row.version,
    state: row.state,
    deletionStarted: row.deletionStarted,
    archiveRemoved: row.archiveRemoved,
    originalsRemoved: row.originalsRemoved,
  }
}
function invalid(value: unknown) {
  expect(() => assertPurgingIntent(value as TreePurgingIntent)).toThrow(/无法核实/)
}

describe('directory permanent cleanup public contract and component facts', () => {
  it('sends exactly captureId and scope, never the four-field server-resolved command', () => {
    const request: TreePurgingRequest = { captureId, scope: 'All' }
    expect(validPurgingRequest(request)).toBe(true)
    expect(Object.keys(request).sort()).toEqual(['captureId', 'scope'])
    expect(validPurgingRequest(ready().command)).toBe(false)
    for (const value of [
      null,
      [],
      { captureId },
      { scope: 'All' },
      { ...request, hostPath: '/private' },
      { ...request, captureOwner: 'not-authority' },
      { ...request, captureId: '00000000-0000-0000-0000-000000000000' },
      { ...request, captureId: captureId.toUpperCase() },
      { ...request, scope: 'archive' },
      { ...request, scope: 'All\n' },
    ])
      expect(validPurgingRequest(value as TreePurgingRequest)).toBe(false)
    expect(samePurgingRequest(request, { ...request })).toBe(true)
    expect(samePurgingRequest(request, { ...request, captureId: id })).toBe(false)
    expect(samePurgingRequest(request, { ...request, scope: 'Archive' })).toBe(false)
    expect(samePurgingCommand(ready().command, { ...ready().command })).toBe(true)
    for (const change of [
      { captureId: id },
      { captureKind: 'Overwrite' as const },
      { scope: 'Archive' as const },
      { source: { rootId: 'other.root', path: ready().command.source.path } },
      { source: { ...ready().command.source, path: 'different' } },
    ])
      expect(samePurgingCommand(ready().command, { ...ready().command, ...change })).toBe(false)
  })

  it.each(purgingScopes)('accepts scope %s for both signed catalogue kinds', (scope) => {
    for (const kind of ['Trash', 'Overwrite'] as const) {
      expect(validPurgingRequest({ captureId, scope })).toBe(true)
      const row = ready(scope, kind)
      expect(assertPurgingIntent(row)).toBe(row)
      expect(canPurgeCapture(root(), capture(kind), scope)).toBe(true)
    }
  })

  it.each(grants)('requires the independent %s root grant', (missing) => {
    const reduced = { ...root(), actions: grants.filter((grant) => grant !== missing) }
    expect(canPurgeRoot(root())).toBe(true)
    expect(canPurgeRoot(reduced)).toBe(false)
    for (const scope of purgingScopes)
      expect(canPurgeCapture(reduced, capture(), scope)).toBe(false)
  })

  it.each([
    ['Retained', 'Retained', true, true, true],
    ['Retained', 'Purged', true, false, false],
    ['Retained', 'Uncertain', true, true, true],
    ['Purged', 'Retained', false, true, false],
    ['Purged', 'Purged', false, false, false],
    ['Purged', 'Uncertain', false, true, false],
    ['Uncertain', 'Retained', true, true, true],
    ['Uncertain', 'Purged', true, false, false],
    ['Uncertain', 'Uncertain', true, true, true],
  ] as const)(
    'gates %s archive / %s originals separately',
    (archiveState, originalsState, archive, originals, all) => {
      const row = { ...capture(), archiveState, originalsState }
      expect(canPurgeCapture(root(), row, 'Archive')).toBe(archive)
      expect(canPurgeCapture(root(), row, 'Originals')).toBe(originals)
      expect(canPurgeCapture(root(), row, 'All')).toBe(all)
      expect(canPurgeCapture(root(), row)).toBe(archive || originals)
      for (const scope of purgingScopes)
        expect(canPurgeCapture(root(), { ...row, cleanupActive: true }, scope)).toBe(false)
      expect(canPurgeCapture(root(), { ...row, cleanupActive: true })).toBe(false)
    },
  )

  it('rejects missing roots, foreign scope, invalid capture proof and private expansions', () => {
    expect(canPurgeRoot(undefined)).toBe(false)
    expect(canPurgeCapture(undefined, capture())).toBe(false)
    expect(canPurgeCapture({ ...root(), id: 'other.root' }, capture())).toBe(false)
    expect(canPurgeCapture(root(), capture(), 'archive' as PurgingScope)).toBe(false)
    for (const change of [
      { archiveSha256: 'invalid' },
      { kind: 'Overwrite' as const, contentSha256: null },
      { archiveState: 'missing' },
      { originalsState: undefined },
      { cleanupActive: 'false' },
      { ownerHash: 'private' },
    ])
      expect(canPurgeCapture(root(), { ...capture(), ...change } as TreeCapture)).toBe(false)
  })

  it('requires the exact fifteen-field intent and exact nested public field sets', () => {
    const row = ready()
    expect(Object.keys(row)).toHaveLength(15)
    expect(Object.keys(row.command)).toHaveLength(4)
    expect(Object.keys(row.command.source)).toHaveLength(2)
    expect(Object.keys(row.confirmation!)).toHaveLength(3)
    for (const field of Object.keys(row)) {
      const missing = { ...row } as unknown as Record<string, unknown>
      delete missing[field]
      invalid(missing)
    }
    for (const field of ['ownerHash', 'loginBinding', 'planJson', 'receiptJson', 'unexpected'])
      invalid({ ...row, [field]: 'private' })
    for (const field of [
      'captureOwner',
      'expectedVersion',
      'catalogueProofHash',
      'manifestSha256',
      'unexpected',
    ])
      invalid({ ...row, command: { ...row.command, [field]: 'private' } })
    invalid({
      ...row,
      command: { ...row.command, source: { ...row.command.source, hostPath: '/private' } },
    })
    invalid({ ...row, confirmation: { ...row.confirmation, privateToken: 'not-public' } })
    for (const field of Object.keys(row.command)) {
      const command = { ...row.command } as Record<string, unknown>
      delete command[field]
      invalid({ ...row, command })
    }
    invalid(null)
    invalid([])
  })

  it.each([
    ['entryCount', 0],
    ['entryCount', 10001],
    ['entryCount', 1.5],
    ['totalBytes', -1],
    ['totalBytes', maximumBytes + 1],
    ['totalBytes', Number.NaN],
    ['archiveBytes', 0],
    ['archiveBytes', maximumArchiveBytes + 1],
    ['archiveBytes', Number.POSITIVE_INFINITY],
  ])('rejects out-of-bound %s=%s', (field, value) => {
    expect(() => assertPurgingIntent({ ...ready(), [field]: value })).toThrow(/目录清理响应/)
  })

  it('accepts independent sixteen/seventeen-GiB maxima and valid zero-byte trees', () => {
    expect(assertPurgingIntent(ready()).totalBytes).toBe(maximumBytes)
    expect(assertPurgingIntent(ready()).archiveBytes).toBe(maximumArchiveBytes)
    expect(
      assertPurgingIntent({ ...ready(), entryCount: 1, totalBytes: 0, archiveBytes: 1 }).totalBytes,
    ).toBe(0)
    for (const field of ['entryCount', 'totalBytes', 'archiveBytes', 'readyExpiresAtUtc'])
      invalid({ ...ready(), [field]: null })
  })

  it.each(purgingScopes)(
    'preserves nullable unknown facts and selected completion for %s',
    (scope) => {
      const unknown = executed(scope)
      expect(assertPurgingIntent(unknown)).toBe(unknown)
      expect(validRecoveryPurge(facts(unknown))).toBe(true)
      expect(unknown.deletionStarted).toBeNull()
      for (const component of ['Archive', 'Originals'] as const) {
        const field = component === 'Archive' ? 'archiveRemoved' : 'originalsRemoved'
        const selected = scope === 'All' || scope === component
        expect(unknown[field]).toBe(selected ? null : false)
        if (selected) invalid({ ...unknown, deletionStarted: true, [field]: false })
        else invalid({ ...unknown, [field]: null })
      }
      invalid({ ...unknown, deletionStarted: false })
      const completed = executed(scope, 'Succeeded')
      expect(assertPurgingIntent(completed)).toBe(completed)
      for (const field of [
        'deletionStarted',
        ...(scope === 'Originals' ? [] : ['archiveRemoved']),
        ...(scope === 'Archive' ? [] : ['originalsRemoved']),
      ]) {
        invalid({ ...completed, [field]: null })
        invalid({ ...completed, [field]: false })
      }
      const positive = {
        ...unknown,
        deletionStarted: true,
        archiveRemoved: scope === 'Originals' ? false : true,
      }
      expect(assertPurgingIntent(positive)).toBe(positive)
    },
  )

  it('does not admit deletion facts for an unexecuted plan or malformed shared recovery facts', () => {
    for (const state of ['Queued', 'Planning', 'Failed', 'Canceled', 'Expired']) {
      const unplanned = {
        ...ready(),
        state,
        entryCount: null,
        totalBytes: null,
        archiveBytes: null,
        readyExpiresAtUtc: null,
        confirmation: null,
      }
      expect(assertPurgingIntent(unplanned)).toBe(unplanned)
      for (const field of ['deletionStarted', 'archiveRemoved', 'originalsRemoved']) {
        invalid({ ...unplanned, [field]: null })
        invalid({ ...unplanned, [field]: true })
      }
    }
    for (const changed of [
      undefined,
      { ...facts(), extra: 'private' },
      { ...facts(), deletionStarted: 'true' },
      { ...facts(), captureKind: 'unknown' },
      { ...facts(), scope: 'archive' },
      { ...facts(), version: -1 },
      { ...facts(), state: 'PurgingDone' },
      { ...facts(), deletionStarted: null, archiveRemoved: true },
    ])
      expect(validRecoveryPurge(changed as RecoveryPurge | undefined)).toBe(false)
    const missing = { ...facts() } as Partial<RecoveryPurge>
    delete missing.archiveRemoved
    expect(validRecoveryPurge(missing as RecoveryPurge)).toBe(false)
  })

  it('rejects rebound identity, versions, executions, result text and confirmation fields', () => {
    const row = ready()
    expect(() => assertPurgingIntent(row, captureId)).toThrow(/无法核实/)
    for (const change of [
      { id: '00000000-0000-0000-0000-000000000000' },
      { id: id.toUpperCase() },
      { planningJobId: captureId },
      { version: -1 },
      { version: 1.5 },
      { version: Number.MAX_SAFE_INTEGER + 1 },
      { executionJobId: executionId },
      { state: 'Unexpected' },
      { resultCode: 'SQL password / private path' },
      { command: { ...row.command, captureId: 'bad' } },
      { command: { ...row.command, captureKind: 'unknown' } },
      { command: { ...row.command, source: { ...row.command.source, path: '../escape' } } },
      { confirmation: null },
      { confirmation: { ...row.confirmation, planHash: '' } },
      { confirmation: { ...row.confirmation, planHash: 'x'.repeat(8193) } },
      { confirmation: { ...row.confirmation, expiresAtUtc: 'invalid' } },
      { confirmation: { ...row.confirmation, expiresAtUtc: '2099-01-01T00:06:00.000Z' } },
      { confirmation: { ...row.confirmation, warning: '' } },
      { confirmation: { ...row.confirmation, warning: 'x'.repeat(2049) } },
    ])
      invalid({ ...row, ...change })
    for (const executionJobId of [null, id, captureId, 'bad', executionId.toUpperCase()])
      invalid({ ...executed(), executionJobId })
    invalid({ ...executed(), confirmation: row.confirmation })
  })

  it('follows unchanged reads and forward execution without treating nullable facts as false', () => {
    const planned = ready(),
      unknown = executed(),
      completed = { ...executed('All', 'Succeeded'), version: 5 }
    expect(purgingFollows(planned, { ...planned })).toBe(true)
    expect(purgingFollows(planned, unknown)).toBe(true)
    expect(purgingFollows(unknown, completed)).toBe(true)
    expect(purgingFollows(completed, { ...completed })).toBe(true)
    expect(
      purgingFollows(unknown, {
        ...unknown,
        version: 5,
        deletionStarted: true,
        originalsRemoved: true,
      }),
    ).toBe(true)
  })

  it('freezes capture, command, plan and established execution while allowing version advancement', () => {
    const previous = executed()
    for (const change of [
      { id: captureId },
      { version: previous.version - 1 },
      { executionJobId: id },
      { command: { ...previous.command, captureId: id } },
      { command: { ...previous.command, scope: 'Archive' as const } },
      { command: { ...previous.command, captureKind: 'Overwrite' as const } },
      { command: { ...previous.command, source: { ...previous.command.source, path: 'changed' } } },
      {
        command: {
          ...previous.command,
          source: { ...previous.command.source, rootId: 'another.root' },
        },
      },
      { entryCount: 9999 },
      { totalBytes: maximumBytes - 1 },
      { archiveBytes: maximumArchiveBytes - 1 },
      { readyExpiresAtUtc: '2099-01-01T00:06:00.000Z' },
    ])
      expect(purgingFollows(previous, { ...previous, version: 5, ...change })).toBe(false)
    for (const field of ['deletionStarted', 'archiveRemoved', 'originalsRemoved'] as const) {
      const established = { ...executed(), deletionStarted: true, [field]: true }
      expect(purgingFollows(established, { ...established, version: 5, [field]: false })).toBe(
        false,
      )
      expect(purgingFollows(established, { ...established, version: 5, [field]: null })).toBe(false)
    }
  })

  it('does not accept same-version state, plan, execution, result, facts or confirmation substitution', () => {
    const previous = ready()
    for (const change of [
      { state: 'Planning' },
      { executionJobId: executionId },
      { entryCount: null },
      { totalBytes: maximumBytes - 1 },
      { archiveBytes: maximumArchiveBytes - 1 },
      { readyExpiresAtUtc: null },
      { resultCode: 'FILES_CANCELED' },
      { deletionStarted: null },
      { archiveRemoved: null },
      { originalsRemoved: null },
      { confirmation: { ...previous.confirmation!, planHash: 'substituted' } },
      { confirmation: { ...previous.confirmation!, expiresAtUtc: '2099-01-01T00:06:00.000Z' } },
      {
        confirmation: { ...previous.confirmation!, warning: 'changed without version advancement' },
      },
      { confirmation: null },
    ])
      expect(purgingFollows(previous, { ...previous, ...change })).toBe(false)
  })
})
