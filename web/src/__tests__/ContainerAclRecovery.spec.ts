import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertAclRecoverySource,
  assertSameAclRecoverySource,
  getAclRecoverySource,
  loadAclRecoverySources,
} from '@/services/containerAclRecovery'
import type { ContainerAclIntent } from '@/services/containerAcls'
vi.mock('@/services/apiClient', () => ({ apiRequest: vi.fn<typeof apiRequest>() }))
const row = (index = 1) =>
  ({
    id: `ac12ac12-ac12-4c12-ac12-${index.toString().padStart(12, '0')}`,
    executionJobId: 'b'.repeat(32),
    kind: 'File',
    targetMode: 432,
    command: {
      aclType: 'access',
      entries: [
        { tag: 'user_obj', id: null, permissions: 6 },
        { tag: 'user', id: 0, permissions: 0 },
        { tag: 'group_obj', id: null, permissions: 4 },
        { tag: 'mask', id: null, permissions: 6 },
        { tag: 'other', id: null, permissions: 0 },
      ],
    },
  }) as ContainerAclIntent
const source = () => ({
  captureId: 'd'.repeat(32),
  changedAclType: 'access',
  kind: 'File',
  createdAtUnixMs: 0,
  originalMode: 432,
  accessEntryCount: 5,
  defaultEntryCount: 0,
})
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
})
it('distinguishes explicit ordinary null from unavailable or missing source', async () => {
  expect(() => assertAclRecoverySource(null, row())).not.toThrow()
  expect(() => assertAclRecoverySource(undefined, row())).toThrow(/来源摘要/)
  vi.mocked(apiRequest).mockRejectedValue(new Error('unavailable'))
  await expect(getAclRecoverySource(row(), new AbortController().signal)).rejects.toThrow(
    'unavailable',
  )
})
it.each([
  'missing',
  'extra',
  'capture',
  'same-intent',
  'same-execution',
  'acl-type',
  'kind',
  'epoch',
  'mode',
  'access-count',
  'default-count',
  'file-default',
  'target-count',
  'target-mode',
  'array',
])('rejects malformed or unrelated summary: %s', (field) => {
  const value: Record<string, unknown> = source()
  if (field === 'missing') delete value.captureId
  if (field === 'extra') value.accessAcl = []
  if (field === 'capture') value.captureId = 'D'.repeat(32)
  if (field === 'same-intent') value.captureId = row().id.replace(/-/g, '')
  if (field === 'same-execution') value.captureId = 'b'.repeat(32)
  if (field === 'acl-type') value.changedAclType = 'default'
  if (field === 'kind') value.kind = 'Directory'
  if (field === 'epoch') value.createdAtUnixMs = '0'
  if (field === 'mode') value.originalMode = 512
  if (field === 'access-count') value.accessEntryCount = 2
  if (field === 'default-count') value.defaultEntryCount = 2
  if (field === 'file-default') value.defaultEntryCount = 3
  if (field === 'target-count') value.accessEntryCount = 4
  if (field === 'target-mode') value.originalMode = 416
  expect(() =>
    assertAclRecoverySource(field === 'array' ? Object.values(value) : value, row()),
  ).toThrow(/来源摘要/)
})
it('accepts a directory default clear with valid zero values', () => {
  const r = row()
  r.kind = 'Directory'
  r.command.aclType = 'default'
  r.command.entries = []
  expect(() =>
    assertAclRecoverySource(
      { ...source(), kind: 'Directory', changedAclType: 'default', originalMode: 0 },
      r,
    ),
  ).not.toThrow()
})
it.each([
  'captureId',
  'changedAclType',
  'kind',
  'createdAtUnixMs',
  'originalMode',
  'accessEntryCount',
  'defaultEntryCount',
])('compares immutable source field %s', (field) => {
  const original = source(),
    observed = { ...original, [field]: 'changed' }
  expect(() => assertSameAclRecoverySource(original as never, observed as never)).toThrow(/已变化/)
})
it('never accepts a restore source appearing or disappearing during acknowledgement', () => {
  expect(() => assertSameAclRecoverySource(null, source() as never)).toThrow(/已变化/)
  expect(() => assertSameAclRecoverySource(source() as never, null)).toThrow(/已变化/)
})
it('limits fifty source reads to four in flight and returns only a complete map', async () => {
  let active = 0,
    peak = 0
  vi.mocked(apiRequest).mockImplementation(async () => {
    peak = Math.max(peak, ++active)
    await new Promise((resolve) => setTimeout(resolve, 1))
    active--
    return source() as never
  })
  const result = await loadAclRecoverySources(
    Array.from({ length: 50 }, (_, n) => row(n + 1)),
    new AbortController().signal,
  )
  expect(Object.keys(result)).toHaveLength(50)
  expect(peak).toBe(4)
  expect(apiRequest).toHaveBeenCalledTimes(50)
})
it('aborts sibling reads on failure without publishing a partial ordinary list', async () => {
  const signals: AbortSignal[] = []
  vi.mocked(apiRequest).mockImplementation(async (_url, options) => {
    signals.push(options!.signal as AbortSignal)
    if (signals.length === 1) throw new Error('bad source')
    await new Promise<void>((_, reject) =>
      options!.signal!.addEventListener('abort', () => reject(new Error('aborted')), {
        once: true,
      }),
    )
    return null as never
  })
  await expect(
    loadAclRecoverySources(
      Array.from({ length: 10 }, (_, n) => row(n + 1)),
      new AbortController().signal,
    ),
  ).rejects.toThrow('bad source')
  expect(apiRequest).toHaveBeenCalledTimes(4)
  expect(signals.every((signal) => signal.aborted)).toBe(true)
})
it('does no reads after logout or for oversized or duplicate lists', async () => {
  const stop = new AbortController()
  stop.abort()
  await expect(loadAclRecoverySources([row()], stop.signal)).rejects.toThrow(/abort/i)
  await expect(
    loadAclRecoverySources(
      Array.from({ length: 51 }, (_, n) => row(n)),
      new AbortController().signal,
    ),
  ).rejects.toThrow(/清单无法核实/)
  await expect(
    loadAclRecoverySources([row(), row()], new AbortController().signal),
  ).rejects.toThrow(/清单无法核实/)
  expect(apiRequest).not.toHaveBeenCalled()
})
