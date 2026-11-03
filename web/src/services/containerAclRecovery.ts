import { apiRequest } from './apiClient'
import { aclRecoveryBase } from './containerAcls'
import type { ContainerAclIntent } from './containerAcls'

export interface ContainerAclRestoreSourceSummary {
  captureId: string
  changedAclType: 'access' | 'default'
  kind: 'File' | 'Directory'
  createdAtUnixMs: number
  originalMode: number
  accessEntryCount: number
  defaultEntryCount: number
}
const fields = [
  'captureId',
  'changedAclType',
  'kind',
  'createdAtUnixMs',
  'originalMode',
  'accessEntryCount',
  'defaultEntryCount',
] as const
function integer(value: unknown, min: number, max: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max
}
export function assertAclRecoverySource(
  value: unknown,
  row: ContainerAclIntent,
): asserts value is ContainerAclRestoreSourceSummary | null {
  if (value === null) return
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new Error('历史ACL恢复来源摘要无法核实')
  const source = value as Record<string, unknown>
  if (
    Object.keys(source).length !== fields.length ||
    !Object.keys(source).every((key) => fields.includes(key as (typeof fields)[number])) ||
    typeof source.captureId !== 'string' ||
    !/^[a-f0-9]{32}$/.test(source.captureId) ||
    source.captureId === '0'.repeat(32) ||
    source.captureId === row.id.replace(/-/g, '') ||
    source.captureId === row.executionJobId?.replace(/-/g, '') ||
    source.changedAclType !== row.command.aclType ||
    !['access', 'default'].includes(source.changedAclType as string) ||
    !['File', 'Directory'].includes(source.kind as string) ||
    (row.kind !== null && source.kind !== row.kind) ||
    !integer(source.createdAtUnixMs, 0, 253402300799999) ||
    !integer(source.originalMode, 0, 511) ||
    !integer(source.accessEntryCount, 3, 64) ||
    !integer(source.defaultEntryCount, 0, 64) ||
    source.defaultEntryCount === 1 ||
    source.defaultEntryCount === 2 ||
    (source.changedAclType === 'access' ? source.accessEntryCount : source.defaultEntryCount) !==
      row.command.entries.length ||
    (source.changedAclType === 'access' &&
      row.targetMode !== null &&
      source.originalMode !== row.targetMode) ||
    (source.kind === 'File' &&
      (source.defaultEntryCount !== 0 || source.changedAclType !== 'access'))
  )
    throw new Error('历史ACL恢复来源摘要无法核实')
}
export function assertSameAclRecoverySource(
  expected: ContainerAclRestoreSourceSummary | null,
  observed: ContainerAclRestoreSourceSummary | null,
) {
  if (
    expected === null
      ? observed !== null
      : observed === null || fields.some((key) => expected[key] !== observed[key])
  )
    throw new Error('历史ACL恢复来源摘要已变化，请核对原任务；不能推断处置成功')
}
export async function getAclRecoverySource(row: ContainerAclIntent, signal: AbortSignal) {
  const source = await apiRequest<unknown>(`${aclRecoveryBase}/${row.id}/restore-source`, {
    signal,
  })
  assertAclRecoverySource(source, row)
  return source === null ? null : Object.freeze({ ...source })
}
/** At most four reads in flight; a failed source must never be rendered as an ordinary ACL. */
export async function loadAclRecoverySources(rows: ContainerAclIntent[], signal: AbortSignal) {
  if (rows.length > 50 || new Set(rows.map((row) => row.id)).size !== rows.length)
    throw new Error('异常ACL修改任务清单无法核实')
  const sources: Record<string, ContainerAclRestoreSourceSummary | null> = {}
  let next = 0
  const local = new AbortController()
  const abort = () => local.abort()
  signal.addEventListener('abort', abort, { once: true })
  if (signal.aborted) local.abort()
  try {
    await Promise.all(
      Array.from({ length: Math.min(4, rows.length) }, async () => {
        while (next < rows.length) {
          local.signal.throwIfAborted()
          const row = rows[next++]!
          sources[row.id] = await getAclRecoverySource(row, local.signal)
        }
      }),
    )
    local.signal.throwIfAborted()
    return sources
  } catch (reason) {
    local.abort()
    throw reason
  } finally {
    signal.removeEventListener('abort', abort)
  }
}
