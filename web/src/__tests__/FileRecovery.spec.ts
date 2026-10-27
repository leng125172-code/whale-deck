import { describe, expect, it } from 'vitest'
import {
  assertRecoveryIntent,
  assertRecoveryPage,
  recoveryKinds,
  recoveryKind,
  recoveryResult,
  recoveryState,
  recoveryTerminal,
  recoveryPurgeFollows,
  recoveryDeletionFact,
} from '@/services/fileRecovery'
import type { RecoveryCandidate, RecoveryIntent, RecoveryPurge } from '@/services/fileRecovery'

function purge(): RecoveryPurge {
  return {
    captureId: '846516d5-6a48-4ba5-9468-cd61f55728ed',
    captureKind: 'Trash',
    scope: 'All',
    version: 17,
    state: 'Unknown',
    deletionStarted: true,
    archiveRemoved: null,
    originalsRemoved: true,
  }
}

function candidate(): RecoveryCandidate {
  return {
    kind: 'tree-move',
    intentId: 'f28bc837-77ef-43c9-a0c9-617f790df336',
    executionJobId: '3c9c0737-999c-468d-817b-c4f9b6288210',
    version: 17,
    createdAtUtc: new Date().toISOString(),
    leaseExpiresAtUtc: null,
    rootStartRecorded: true,
    integrity: 'Verified',
    source: { rootId: 'files.shared', path: '资料/<img src=x onerror=alert(1)>/source' },
    target: { rootId: 'files.shared', path: '资料/target' },
    hasRetainedObjects: null,
  }
}
function containerCandidate(): RecoveryCandidate {
  const id = 'f28bc837-77ef-43c9-a0c9-617f790df336'
  return {
    kind: 'container-upload',
    intentId: id,
    executionJobId: id,
    version: 17,
    createdAtUtc: new Date().toISOString(),
    leaseExpiresAtUtc: null,
    rootStartRecorded: true,
    integrity: 'Verified',
    source: null,
    target: null,
    hasRetainedObjects: true,
    container: {
      containerId: 'a'.repeat(64),
      instanceId: '2147483647:2026-10-22T01:02:03.123456789Z',
      path: '资料/容器内文件.bin',
    },
  }
}
function ready(): RecoveryIntent {
  const original = candidate(),
    id = 'c47ae017-288b-445f-bae1-ef0ce9baa4dd',
    expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    kind: original.kind,
    originalIntentId: original.intentId,
    originalExecutionJobId: original.executionJobId!,
    originalVersion: 17,
    state: 'Ready',
    disposition: 'AcknowledgeUnknown',
    reasonCode: 'OriginalSessionLost',
    rootState: 'Unknown',
    readyExpiresAtUtc: expiry,
    version: 3,
    resultCode: 'FILES_RECOVERY_READY',
    originalResolution: null,
    confirmation: {
      planHash: 'fixture-not-an-authentication-token',
      expiresAtUtc: expiry,
      warning: '接受不确定性不代表成功',
    },
  }
}
describe('current-administrator recovery presentation boundaries', () => {
  it('does not accept a changed same-version snapshot or erase established positive facts', () => {
    const original = purge()
    expect(recoveryPurgeFollows(original, { ...original })).toBe(true)
    expect(recoveryPurgeFollows(original, { ...original, archiveRemoved: true })).toBe(false)
    expect(recoveryPurgeFollows(original, { ...original, version: 18, archiveRemoved: true })).toBe(
      true,
    )
    expect(
      recoveryPurgeFollows(original, { ...original, version: 18, originalsRemoved: null }),
    ).toBe(false)
    expect(
      recoveryPurgeFollows(original, { ...original, version: 18, captureKind: 'Overwrite' }),
    ).toBe(false)
    expect(recoveryDeletionFact(null)).toContain('不推断未删除')
    expect(recoveryDeletionFact(true)).toBe('已确认删除')
    expect(recoveryDeletionFact(false)).toBe('未执行该项删除')
  })
  it('requires strict purge summary only for signed eleventh-kind candidates and intents', () => {
    const item = { ...candidate(), kind: 'tree-purge' as const, target: null, purge: purge() }
    expect(assertRecoveryPage({ entries: [item], nextCursor: null })).toBeTruthy()
    const intent = { ...ready(), kind: 'tree-purge' as const, purge: purge() }
    expect(assertRecoveryIntent(intent)).toBe(intent)
    for (const change of [
      undefined,
      null,
      { ...purge(), version: 16 },
      { ...purge(), state: 'Succeeded' },
      { ...purge(), archiveRemoved: false },
      { ...purge(), originalsRemoved: true, deletionStarted: null },
      { ...purge(), scope: 'Archive' },
      { ...purge(), captureId: '' },
      { ...purge(), ownerHash: 'private' },
      { ...purge(), CaptureId: 'alias' },
    ]) {
      expect(() =>
        assertRecoveryPage({ entries: [{ ...item, purge: change }], nextCursor: null } as never),
      ).toThrow('异常任务元数据无法核实')
      expect(() => assertRecoveryIntent({ ...intent, purge: change } as never)).toThrow(
        '接管任务响应无法核实',
      )
    }
    expect(() =>
      assertRecoveryPage({ entries: [{ ...candidate(), purge: purge() }], nextCursor: null }),
    ).toThrow('异常任务元数据无法核实')
    expect(() => assertRecoveryIntent({ ...ready(), purge: purge() })).toThrow(
      '接管任务响应无法核实',
    )
    expect(() =>
      assertRecoveryPage({
        entries: [{ ...item, integrity: 'InvalidBinding', source: null }],
        nextCursor: null,
      }),
    ).toThrow('异常任务元数据无法核实')
  })
  it('allows newer immutable original facts after disposition without turning partial deletion into success', () => {
    const base = {
      ...ready(),
      kind: 'tree-purge' as const,
      executionJobId: '11c8ecb3-112e-4d6b-b26c-d63982ca941e',
      state: 'Disposed',
      confirmation: null,
      originalResolution: 'Acknowledged',
      purge: { ...purge(), version: 18, state: 'Acknowledged' },
    }
    expect(assertRecoveryIntent(base)).toBe(base)
    expect(() =>
      assertRecoveryIntent({ ...base, purge: { ...base.purge, state: 'Unknown' } }),
    ).toThrow('接管任务响应无法核实')
    const completed = {
      ...base,
      disposition: 'ReconcileCompleted' as const,
      rootState: 'Completed',
      originalResolution: 'Succeeded',
      purge: { ...base.purge, state: 'Succeeded', archiveRemoved: true },
    }
    expect(assertRecoveryIntent(completed)).toBe(completed)
    for (const scope of ['Archive', 'Originals', 'All'] as const) {
      const p = {
        ...purge(),
        scope,
        deletionStarted: null,
        archiveRemoved: scope === 'Originals' ? false : null,
        originalsRemoved: scope === 'Archive' ? false : null,
      }
      expect(assertRecoveryIntent({ ...ready(), kind: 'tree-purge', purge: p })).toBeTruthy()
    }
  })
  it('requires container upload metadata without pretending it is a host root location', () => {
    const item = containerCandidate()
    expect(assertRecoveryPage({ entries: [item], nextCursor: null }).entries[0]).toBe(item)
    expect(recoveryKind(item.kind)).toBe('容器文件上传')
    for (const changed of [
      { ...item, container: undefined },
      { ...item, source: { rootId: 'files.shared', path: '伪造宿主机路径' } },
      { ...item, target: { rootId: 'files.shared', path: '伪造宿主机路径' } },
      { ...item, executionJobId: '3c9c0737-999c-468d-817b-c4f9b6288210' },
      { ...item, hasRetainedObjects: false },
      { ...item, container: { ...item.container!, containerId: 'A'.repeat(64) } },
      { ...item, container: { ...item.container!, instanceId: '1:2026-10-22T01:02:03Z' } },
      { ...item, container: { ...item.container!, path: '../escape' } },
      { ...item, container: { ...item.container!, ownerHash: 'private' } },
    ])
      expect(() => assertRecoveryPage({ entries: [changed], nextCursor: null } as never)).toThrow(
        '异常任务元数据无法核实',
      )
    expect(() =>
      assertRecoveryPage({
        entries: [{ ...candidate(), container: item.container }],
        nextCursor: null,
      }),
    ).toThrow('异常任务元数据无法核实')
  })
  it('allows container upload acknowledgment only and never reconciles a historical publish as success', () => {
    const original = containerCandidate()
    const row = {
      ...ready(),
      kind: 'container-upload' as const,
      originalIntentId: original.intentId,
      originalExecutionJobId: original.executionJobId!,
      disposition: 'AcknowledgeUnknown' as const,
      rootState: 'Unknown',
    }
    expect(assertRecoveryIntent(row)).toBe(row)
    expect(() =>
      assertRecoveryIntent({
        ...row,
        disposition: 'ReconcileCompleted',
        rootState: 'Completed',
      }),
    ).toThrow('接管任务响应无法核实')
    const disposed = {
      ...row,
      state: 'Disposed',
      executionJobId: '11c8ecb3-112e-4d6b-b26c-d63982ca941e',
      confirmation: null,
      originalResolution: 'Failed',
      resultCode: 'FILES_RECOVERY_DISPOSED',
    }
    expect(assertRecoveryIntent(disposed)).toBe(disposed)
    expect(() => assertRecoveryIntent({ ...disposed, originalResolution: 'Acknowledged' })).toThrow(
      '接管任务响应无法核实',
    )
    expect(() => assertRecoveryIntent({ ...disposed, originalResolution: 'Succeeded' })).toThrow(
      '接管任务响应无法核实',
    )
    expect(() => assertRecoveryIntent({ ...disposed, originalResolution: 'Superseded' })).toThrow(
      '接管任务响应无法核实',
    )
    expect(() =>
      assertRecoveryIntent({ ...disposed, resultCode: 'CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED' }),
    ).toThrow('接管任务响应无法核实')
  })
  it('keeps all twelve workflow kinds and does not turn unknown or planning into success', () => {
    expect(recoveryKinds).toHaveLength(12)
    expect(recoveryKind('tree-restore')).toBe('目录恢复')
    expect(recoveryKind('tree-overwrite-restore')).toBe('目录覆盖恢复')
    expect(recoveryKind('tree-trash')).toBe('目录回收')
    expect(recoveryKind('tree-purge')).toBe('目录永久清理')
    expect(recoveryKind('tree-unpack')).toBe('归档解压')
    expect(recoveryKind('tree-pack')).toBe('目录压缩')
    expect(
      assertRecoveryPage({ entries: [{ ...candidate(), kind: 'tree-pack' }], nextCursor: null })
        .entries[0]?.kind,
    ).toBe('tree-pack')
    for (const kind of recoveryKinds) expect(recoveryKind(kind)).toBeTruthy()
    for (const state of ['Queued', 'Planning', 'Ready', 'DispositionQueued', 'Fencing', 'Unknown'])
      expect(recoveryTerminal(state)).toBe(false)
    expect(recoveryState('Disposed')).toContain('不等于')
    expect(recoveryResult('FILES_RECOVERY_DISPOSITION_MISMATCH')).toContain('完整回执')
    expect(recoveryResult('arbitrary private exception and path')).toBe('')
  })
  it('accepts literal path characters as text and preserves unknown retention rather than guessing false', () => {
    const row = candidate(),
      page = { entries: [row], nextCursor: null }
    expect(assertRecoveryPage(page)).toBe(page)
    expect(row.hasRetainedObjects).toBeNull()
  })
  it.each([
    'loginBinding',
    'ownerHash',
    'originalJson',
    'observationJson',
    'planJson',
    'requestHash',
  ])('rejects accidental private %s material in public responses', (name) => {
    expect(() =>
      assertRecoveryPage({ entries: [{ ...candidate(), [name]: 'not-public' }], nextCursor: null }),
    ).toThrow('元数据')
    expect(() => assertRecoveryIntent({ ...ready(), [name]: 'not-public' })).toThrow('接管')
  })
  it('enforces the page, cursor, identity, version, and invalid-binding limits', () => {
    const row = candidate()
    for (const page of [
      { entries: Array.from({ length: 26 }, () => row), nextCursor: null },
      { entries: [row, row], nextCursor: null },
      { entries: [row], nextCursor: '../escape' },
      { entries: [{ ...row, version: Number.MAX_SAFE_INTEGER + 1 }], nextCursor: null },
      { entries: [{ ...row, integrity: 'InvalidBinding' as const }], nextCursor: null },
      {
        entries: [{ ...row, source: { rootId: 'files.shared', path: '../private' } }],
        nextCursor: null,
      },
    ])
      expect(() => assertRecoveryPage(page)).toThrow(/异常任务/)
    expect(
      assertRecoveryPage({
        entries: [
          {
            ...row,
            integrity: 'InvalidBinding',
            source: null,
            target: null,
            hasRetainedObjects: null,
          },
        ],
        nextCursor: null,
      }),
    ).toBeTruthy()
  })
  it('binds the response to the exact selected original version, kind, and disposition', () => {
    const row = ready(),
      command = {
        kind: row.kind,
        intentId: row.originalIntentId,
        originalVersion: row.originalVersion,
        disposition: row.disposition,
        reasonCode: row.reasonCode,
      }
    expect(assertRecoveryIntent(row, row.id, command)).toBe(row)
    expect(() => assertRecoveryIntent(row, candidate().intentId)).toThrow('接管')
    expect(() => assertRecoveryIntent(row, row.id, { ...command, originalVersion: 18 })).toThrow(
      '接管',
    )
    expect(() =>
      assertRecoveryIntent(row, row.id, { ...command, disposition: 'ReconcileCompleted' }),
    ).toThrow('接管')
  })
  it('cannot cast acceptance of historical uncertainty as a successful original file operation', () => {
    const row = ready(),
      disposed = {
        ...row,
        state: 'Disposed',
        executionJobId: '11c8ecb3-112e-4d6b-b26c-d63982ca941e',
        confirmation: null,
        originalResolution: 'Acknowledged',
      }
    expect(assertRecoveryIntent(disposed)).toBe(disposed)
    expect(() => assertRecoveryIntent({ ...disposed, originalResolution: 'Succeeded' })).toThrow(
      '接管',
    )
    expect(() => assertRecoveryIntent({ ...row, rootState: 'Completed' })).toThrow('接管')
    expect(() =>
      assertRecoveryIntent({ ...disposed, executionJobId: row.originalExecutionJobId }),
    ).toThrow('接管')
    expect(() => assertRecoveryIntent({ ...row, originalResolution: 'Acknowledged' })).toThrow(
      '接管',
    )
  })
  it('requires a separate completed observation before reconciliation', () => {
    const row = ready(),
      completed = {
        ...row,
        state: 'Disposed',
        executionJobId: '11c8ecb3-112e-4d6b-b26c-d63982ca941e',
        confirmation: null,
        disposition: 'ReconcileCompleted' as const,
        rootState: 'Completed',
        originalResolution: 'Superseded',
      }
    expect(assertRecoveryIntent(completed)).toBe(completed)
    expect(() =>
      assertRecoveryIntent({ ...completed, originalResolution: 'Acknowledged' }),
    ).toThrow('接管')
  })
  it('never offers a confirmation token for an execution, changed deadline or arbitrary result text', () => {
    const row = ready()
    expect(() =>
      assertRecoveryIntent({
        ...row,
        state: 'Fencing',
        executionJobId: '11c8ecb3-112e-4d6b-b26c-d63982ca941e',
      }),
    ).toThrow('确认')
    expect(() =>
      assertRecoveryIntent({
        ...row,
        confirmation: { ...row.confirmation!, expiresAtUtc: 'not-a-date' },
      }),
    ).toThrow('确认')
    expect(() =>
      assertRecoveryIntent({ ...row, resultCode: 'SQL password or internal path' }),
    ).toThrow('接管')
  })
})
