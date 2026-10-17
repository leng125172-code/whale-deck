import { describe, expect, it } from 'vitest'
import {
  assertRecoveryIntent,
  assertRecoveryPage,
  recoveryKinds,
  recoveryKind,
  recoveryResult,
  recoveryState,
  recoveryTerminal,
} from '@/services/fileRecovery'
import type { RecoveryCandidate, RecoveryIntent } from '@/services/fileRecovery'

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
  it('keeps all five workflow kinds and does not turn unknown or planning into success', () => {
    expect(recoveryKinds).toHaveLength(5)
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
