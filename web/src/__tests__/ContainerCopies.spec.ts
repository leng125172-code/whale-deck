import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertCopyAdvance,
  assertCopyIntent,
  canCopyContainerEntry,
  cancelCopy,
  confirmCopy,
  copyState,
  copyTerminal,
  currentCopyInstance,
  defaultCopyTarget,
  getCopy,
  queueCopy,
  validCopyCommand,
} from '@/services/containerCopies'
import type { ContainerCopyIntent } from '@/services/containerCopies'
import {
  clearCopyHints,
  copyHint,
  reconcileCopyHints,
  saveCopyHint,
} from '@/services/containerCopyHints'
import type { FileEntry } from '@/services/fileManagement'

vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-28T01:02:03.123456789Z',
  name: 'ordinary',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const id = 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
  execution = 'bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
const command = {
  source: { containerId: target.containerId, instanceId: target.instanceId, path: 'work/源.txt' },
  targetPath: 'work/副本.txt',
  expectedVersion: 'A'.repeat(64),
}
function ready(): ContainerCopyIntent {
  const expiry = new Date(Date.now() + 300000).toISOString()
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    command: structuredClone(command),
    state: 'Ready',
    kind: 'File',
    entryCount: 1,
    totalBytes: 3,
    readyExpiresAtUtc: expiry,
    resultCode: 'CONTAINER_COPY_READY',
    version: 2,
    publishedVersion: null,
    privateMayExist: false,
    confirmation: {
      planHash: 'opaque-confirmation-plan',
      expiresAtUtc: expiry,
      warning: '尚未复制，目标不能存在',
    },
  }
}
function state(value: string): ContainerCopyIntent {
  const row = ready()
  row.state = value
  row.confirmation = null
  row.version = 3
  row.resultCode = 'CONTAINER_COPY_TEST_RESULT'
  if (['Queued', 'Planning'].includes(value)) {
    row.kind = null
    row.entryCount = null
    row.totalBytes = null
    row.readyExpiresAtUtc = null
    row.resultCode = null
  }
  if (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(value))
    row.executionJobId = execution
  row.privateMayExist = value === 'Unknown' || value === 'Acknowledged'
  if (['Succeeded', 'Superseded'].includes(value)) row.publishedVersion = 'F'.repeat(64)
  return row
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  sessionStorage.clear()
})

describe('container copy typed intent and immutable progress', () => {
  it.each([
    'Queued',
    'Planning',
    'Pending',
    'Applying',
    'Unknown',
    'Succeeded',
    'Superseded',
    'Canceled',
    'Failed',
    'Expired',
    'Acknowledged',
  ])('accepts honest %s without inventing a copy result', (value) => {
    expect(assertCopyIntent(state(value))).toBeDefined()
  })
  it('distinguishes Unknown, historical acknowledgement and superseded target', () => {
    expect(copyTerminal('Unknown')).toBe(false)
    expect(copyTerminal('Acknowledged')).toBe(true)
    expect(copyState('Acknowledged')).toContain('不代表复制成功')
    expect(copyState('Superseded')).toContain('当前目标已变化')
  })
  it.each([
    'id',
    'job',
    'container',
    'instance',
    'path',
    'target',
    'version',
    'kind',
    'count',
    'bytes',
    'expires',
    'code',
    'private',
    'confirmation',
    'extra-command',
  ])('rejects malformed identity or plan: %s', (field) => {
    const row = ready()
    switch (field) {
      case 'id':
        row.id = 'bad'
        break
      case 'job':
        row.planningJobId = execution
        break
      case 'container':
        row.command.source.containerId = 'b'.repeat(64)
        break
      case 'instance':
        row.command.source.instanceId = 'invalid'
        break
      case 'path':
        row.command.source.path = '../escape'
        break
      case 'target':
        row.command.targetPath = row.command.source.path + '/child'
        break
      case 'version':
        row.version = Number.MAX_SAFE_INTEGER + 1
        break
      case 'kind':
        row.kind = 'Symlink' as 'File'
        break
      case 'count':
        row.entryCount = 2
        break
      case 'bytes':
        row.totalBytes = 1073741825
        break
      case 'expires':
        row.readyExpiresAtUtc = 'invalid'
        break
      case 'code':
        row.resultCode = 'SQL password=hidden'
        break
      case 'private':
        row.privateMayExist = null as unknown as boolean
        break
      case 'confirmation':
        row.confirmation!.expiresAtUtc = new Date(Date.now() + 600000).toISOString()
        break
      case 'extra-command':
        Object.assign(row.command, { ownerUid: 0 })
        break
    }
    expect(() => assertCopyIntent(row, target.containerId)).toThrow(/复制/)
  })
  it.each([
    'missing-execution',
    'same-execution',
    'missing-proof',
    'private-success',
    'missing-code',
    'unknown-clean',
  ])('cannot accept an invalid terminal receipt: %s', (field) => {
    const row = state(field === 'unknown-clean' ? 'Unknown' : 'Succeeded')
    if (field === 'missing-execution') row.executionJobId = null
    if (field === 'same-execution') row.executionJobId = id
    if (field === 'missing-proof') row.publishedVersion = null
    if (field === 'private-success') row.privateMayExist = true
    if (field === 'missing-code') row.resultCode = null
    if (field === 'unknown-clean') row.privateMayExist = false
    expect(() => assertCopyIntent(row)).toThrow(/复制/)
  })
  it('keeps JSON order irrelevant and accepts only the virtual Ready expiry without a version increment', () => {
    const row = ready(),
      reordered = Object.fromEntries(
        Object.entries(row).reverse(),
      ) as unknown as ContainerCopyIntent
    expect(() => assertCopyAdvance(row, reordered)).not.toThrow()
    const expired = { ...row, state: 'Expired', confirmation: null }
    expect(() => assertCopyIntent(expired)).not.toThrow()
    expect(() => assertCopyAdvance(row, expired)).not.toThrow()
    expect(() => assertCopyAdvance(row, { ...expired, privateMayExist: true })).toThrow(/复制/)
  })
  it.each([
    'older',
    'changed-target',
    'execution',
    'manifest-count',
    'same-version-mutation',
    'back-to-pending',
    'unknown-resume',
  ])('rejects stale or substituted progress: %s', (field) => {
    const previous = field === 'unknown-resume' ? state('Unknown') : state('Applying'),
      next = structuredClone(previous)
    next.version++
    if (field === 'older') next.version = previous.version - 1
    if (field === 'changed-target') next.command.targetPath = 'work/changed'
    if (field === 'execution') next.executionJobId = id
    if (field === 'manifest-count') next.totalBytes = 4
    if (field === 'same-version-mutation') {
      next.version = previous.version
      next.resultCode = 'CONTAINER_COPY_CHANGED'
    }
    if (field === 'back-to-pending' || field === 'unknown-resume') next.state = 'Pending'
    expect(() => assertCopyAdvance(previous, next)).toThrow(/复制/)
  })
  it('retains an original instance after restart instead of rewriting the command', () => {
    const row = ready()
    expect(currentCopyInstance(row, target)).toBe(true)
    expect(
      currentCopyInstance(row, { ...target, instanceId: '888:2026-10-28T01:02:03.123456789Z' }),
    ).toBe(false)
    expect(row.command.source.instanceId).toBe(target.instanceId)
  })
})

describe('copy transport has no automatic mutation recovery', () => {
  it('queues exact metadata with one frozen key; lookup uses GET only', async () => {
    const row = ready()
    vi.mocked(apiRequest).mockResolvedValue(row)
    await queueCopy(command, id, new AbortController().signal)
    await getCopy(target.containerId, id, new AbortController().signal, true)
    const call = vi.mocked(apiRequest).mock.calls[0]!
    expect(call[1]?.headers).toEqual({ 'Idempotency-Key': id })
    expect(JSON.parse(call[1]!.body as string)).toEqual({
      instanceId: target.instanceId,
      sourcePath: command.source.path,
      targetPath: command.targetPath,
      expectedVersion: command.expectedVersion,
    })
    expect(vi.mocked(apiRequest).mock.calls[1]?.[0]).toBe(
      `containers/${target.containerId}/files/copies/by-key/${id}`,
    )
    expect(vi.mocked(apiRequest).mock.calls[1]?.[1]?.method).toBeUndefined()
  })
  it('confirms the exact original plan and version and cancels only the same intent', async () => {
    const row = ready()
    vi.mocked(apiRequest)
      .mockResolvedValueOnce(state('Pending'))
      .mockResolvedValueOnce(state('Canceled'))
    await confirmCopy(row, new AbortController().signal)
    await cancelCopy(row, new AbortController().signal)
    expect(JSON.parse(vi.mocked(apiRequest).mock.calls[0]![1]!.body as string)).toEqual({
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: row.version,
    })
    expect(vi.mocked(apiRequest).mock.calls[1]?.[1]?.headers).toEqual({ 'Idempotency-Key': id })
  })
  it('does not resend a lost response and refuses expired or unknown confirmations', async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error('response lost'))
    await expect(queueCopy(command, id, new AbortController().signal)).rejects.toThrow(
      'response lost',
    )
    expect(apiRequest).toHaveBeenCalledOnce()
    const expired = ready()
    expired.confirmation!.expiresAtUtc = expired.readyExpiresAtUtc = new Date(
      Date.now() - 1,
    ).toISOString()
    await expect(confirmCopy(expired, new AbortController().signal)).rejects.toThrow(/复制/)
    await expect(confirmCopy(state('Unknown'), new AbortController().signal)).rejects.toThrow(
      /复制/,
    )
    expect(apiRequest).toHaveBeenCalledOnce()
  })
  it.each([
    '',
    '../outside',
    '/absolute',
    'work//double',
    'work/源.txt/child',
    'work',
    'work/源.txt',
  ])('rejects invalid or overlapping target before network: %s', (path) => {
    expect(validCopyCommand({ ...command, targetPath: path })).toBe(false)
  })
  it('uses byte-safe target defaults and ordinary file budgets', () => {
    const value: FileEntry = {
      name: 'a',
      kind: 'File',
      sizeBytes: 0,
      modifiedAtUnixMs: 0,
      version: 'A'.repeat(64),
      mode: '0640',
      isProtected: false,
    }
    expect(canCopyContainerEntry(value)).toBe(true)
    expect(canCopyContainerEntry({ ...value, kind: 'Directory', sizeBytes: 1073741825 })).toBe(true)
    for (const bad of [
      { isProtected: true },
      { kind: 'Symlink' },
      { mode: '4755' },
      { sizeBytes: 1073741825 },
    ])
      expect(canCopyContainerEntry({ ...value, ...bad })).toBe(false)
    const name = '长'.repeat(85),
      result = defaultCopyTarget('work', name)
    expect(new TextEncoder().encode(result.split('/').slice(-1)[0]!).length).toBeLessThanOrEqual(
      255,
    )
    expect(
      validCopyCommand({
        ...command,
        source: { ...command.source, path: 'work/' + name },
        targetPath: result,
      }),
    ).toBe(true)
  })
})

describe('opaque copy navigation hints', () => {
  const hint = { subject: 'admin', containerId: target.containerId, key: id, id: null }
  it('stores only original references, never command, source bytes or confirmation', () => {
    saveCopyHint(hint)
    const raw = sessionStorage.getItem('whaledeck-container-copy-hints-v1')!
    expect(Object.keys(JSON.parse(raw)[0]).sort()).toEqual(['containerId', 'id', 'key', 'subject'])
    expect(copyHint('admin', target.containerId)).toEqual(hint)
    expect(() => saveCopyHint({ ...hint, command } as unknown as typeof hint)).toThrow(/复制/)
  })
  it.each([
    ['another', true],
    ['admin', false],
    [undefined, true],
  ] as const)('clears hints on subject/role change: %s', (subject, admin) => {
    saveCopyHint(hint)
    reconcileCopyHints(subject, admin)
    expect(copyHint('admin', target.containerId)).toBeNull()
  })
  it('does not evict unverified references when the four-container budget is full', () => {
    for (const letter of ['a', 'b', 'c', 'd'])
      saveCopyHint({ ...hint, containerId: letter.repeat(64) })
    expect(() => saveCopyHint({ ...hint, containerId: 'e'.repeat(64) })).toThrow(/复制/)
    expect(copyHint('admin', 'a'.repeat(64))).toEqual(hint)
  })
  it.each([
    'bad-json',
    JSON.stringify([{ ...hint, planHash: 'must-not-be-kept' }]),
    JSON.stringify([hint, hint]),
  ])('rejects corrupt/duplicate/expanded storage', (raw) => {
    sessionStorage.setItem('whaledeck-container-copy-hints-v1', raw)
    expect(copyHint('admin', target.containerId)).toBeNull()
  })
  it('clears explicitly without touching unrelated storage', () => {
    sessionStorage.setItem('unrelated', 'keep')
    saveCopyHint(hint)
    clearCopyHints()
    expect(sessionStorage.getItem('unrelated')).toBe('keep')
  })
})
