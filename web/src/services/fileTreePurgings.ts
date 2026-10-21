import type { FileRoot } from '@/services/fileManagement'
import { treeUuid, validTreeLocation } from '@/services/fileTreeCopies'
import type { TreeLocation } from '@/services/fileTreeCopies'
import { assertCapturePage } from '@/services/fileTreeRecyclings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'
import { validRecoveryPurge } from '@/services/fileRecovery'

export { treeTerminal, treeUuid } from '@/services/fileTreeCopies'
export {
  recoveryPurgeScope as purgingScope,
  recoveryPurgeState as purgingState,
  recoveryDeletionFact as purgingFact,
} from '@/services/fileRecovery'
export const purgingScopes = ['Archive', 'Originals', 'All'] as const
export type PurgingScope = (typeof purgingScopes)[number]
export interface TreePurgingRequest {
  captureId: string
  scope: PurgingScope
}
export interface TreePurgingCommand extends TreePurgingRequest {
  source: TreeLocation
  captureKind: 'Trash' | 'Overwrite'
}
export interface TreePurgingIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  state: string
  command: TreePurgingCommand
  entryCount: number | null
  totalBytes: number | null
  archiveBytes: number | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  deletionStarted: boolean | null
  archiveRemoved: boolean | null
  originalsRemoved: boolean | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
const uuid = (value: unknown): value is string =>
  typeof value === 'string' &&
  treeUuid.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000'
const date = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
const integer = (value: unknown): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
function keys(value: unknown, fields: readonly string[]): value is Record<string, unknown> {
  return (
    !!value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === fields.length &&
    Object.keys(value).every((key) => fields.includes(key))
  )
}
export function validPurgingRequest(value: TreePurgingRequest) {
  return (
    keys(value, ['captureId', 'scope']) &&
    uuid(value.captureId) &&
    purgingScopes.includes(value.scope)
  )
}
export function samePurgingRequest(left: TreePurgingRequest, right: TreePurgingRequest) {
  return left.captureId === right.captureId && left.scope === right.scope
}
export function samePurgingCommand(left: TreePurgingCommand, right: TreePurgingCommand) {
  return (
    samePurgingRequest(left, right) &&
    left.captureKind === right.captureKind &&
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path
  )
}
export function canPurgeRoot(root: FileRoot | undefined) {
  return (
    !!root &&
    [
      'files-list',
      'files-download',
      'files-mutate',
      'files-history',
      'files-archive',
      'files-tree-purge',
    ].every((action) => root.actions.includes(action))
  )
}
export function canPurgeCapture(
  root: FileRoot | undefined,
  capture: TreeCapture,
  scope?: PurgingScope,
): boolean {
  if (!canPurgeRoot(root) || capture.cleanupActive) return false
  try {
    assertCapturePage({ items: [capture], next: null }, root!.id)
  } catch {
    return false
  }
  if (scope === undefined) return purgingScopes.some((item) => canPurgeCapture(root, capture, item))
  return (
    purgingScopes.includes(scope) &&
    (scope === 'Originals' || capture.archiveState !== 'Purged') &&
    (scope === 'Archive' || capture.originalsState !== 'Purged')
  )
}
export function purgingWarning(scope: PurgingScope) {
  return {
    Archive: '将永久删除HDD归档，不可撤销；原件不在本次范围，也不保证仍可用。',
    Originals: '将永久删除保留原件，不可撤销；若归档已清理，可能删除最后副本。',
    All: '将永久删除归档和保留原件，可能移除全部可恢复副本，不可撤销。',
  }[scope]
}
export function purgingResult(code: string | null) {
  return (
    (
      {
        FILES_TREE_PURGE_COMPLETED: '本次范围内的组件已永久清理，原捕获和审计记录保留。',
        FILES_TREE_READY_FOR_CONFIRMATION: '清理计划已核实，尚未开始删除。',
        FILES_CAPTURE_COMPONENT_UNAVAILABLE:
          '所选组件已有删除证明，或此捕获仍有活动清理；请重新读取记录。',
        FILES_CAPTURE_NOT_FOUND: '未找到原捕获记录，不会替换为其他捕获。',
        FILES_CAPTURE_PROOF_CHANGED: '原捕获证明已变化，不会继续清理。',
        FILES_RESULT_UNKNOWN: '结果未知，保留锁与证据，不自动续删。',
        FILES_RECEIPT_UNAVAILABLE: '未取得原回执，只查询原任务，不重复删除。',
        FILES_TREE_BACKUP_NOT_FOUND: '所需历史组件不可用，未生成清理计划。',
        FILES_TREE_CHANGED: '原件或存储状态已变化，不执行原计划。',
        FILES_PLAN_EXPIRED: '原计划已过期，不会自动执行。',
        FILES_CANCELED: '已取消或被取消栅栏阻止；最终删除事实以原回执为准。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已接受未知历史，不代表副本存在或清理完成。',
      } as Record<string, string>
    )[code || ''] || '请核对原任务和组件事实；不会自动重新提交。'
  )
}
export function assertPurgingIntent(value: TreePurgingIntent, expectedId?: string) {
  const command = value?.command
  const plan =
    value?.entryCount !== null || value?.totalBytes !== null || value?.archiveBytes !== null
  const executed = ['PurgingQueued', 'Purging', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
    value?.state,
  )
  if (
    !keys(value, [
      'id',
      'planningJobId',
      'executionJobId',
      'state',
      'command',
      'entryCount',
      'totalBytes',
      'archiveBytes',
      'readyExpiresAtUtc',
      'resultCode',
      'version',
      'deletionStarted',
      'archiveRemoved',
      'originalsRemoved',
      'confirmation',
    ]) ||
    !uuid(value.id) ||
    value.planningJobId !== value.id ||
    (expectedId !== undefined && expectedId !== value.id) ||
    (value.executionJobId !== null &&
      (!uuid(value.executionJobId) ||
        value.executionJobId === value.id ||
        value.executionJobId === command?.captureId)) ||
    !keys(command, ['captureId', 'source', 'captureKind', 'scope']) ||
    !uuid(command.captureId) ||
    !purgingScopes.includes(command.scope) ||
    !['Trash', 'Overwrite'].includes(command.captureKind) ||
    !keys(command.source, ['rootId', 'path']) ||
    !validTreeLocation(command.source) ||
    !integer(value.version) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    !validRecoveryPurge({
      captureId: command.captureId,
      captureKind: command.captureKind,
      scope: command.scope,
      state: value.state,
      version: value.version,
      deletionStarted: value.deletionStarted,
      archiveRemoved: value.archiveRemoved,
      originalsRemoved: value.originalsRemoved,
    }) ||
    (plan
      ? !integer(value.entryCount) ||
        value.entryCount < 1 ||
        value.entryCount > 10000 ||
        !integer(value.totalBytes) ||
        value.totalBytes > 16 * 1024 ** 3 ||
        !integer(value.archiveBytes) ||
        value.archiveBytes < 1 ||
        value.archiveBytes > 17 * 1024 ** 3 ||
        !date(value.readyExpiresAtUtc)
      : value.entryCount !== null ||
        value.totalBytes !== null ||
        value.archiveBytes !== null ||
        value.readyExpiresAtUtc !== null) ||
    ((executed || value.state === 'Ready') && !plan) ||
    (executed && value.executionJobId === null) ||
    (['Queued', 'Planning', 'Ready'].includes(value.state) && value.executionJobId !== null) ||
    (value.executionJobId === null &&
      [value.deletionStarted, value.archiveRemoved, value.originalsRemoved].some(
        (fact) => fact !== false,
      )) ||
    (value.state === 'Unknown' && value.deletionStarted === false) ||
    (value.state === 'Ready' ? value.confirmation === null : value.confirmation !== null)
  )
    throw new Error('目录清理响应或捕获范围无法核实，保留原引用，不重新提交')
  if (
    value.confirmation !== null &&
    (!keys(value.confirmation, ['planHash', 'expiresAtUtc', 'warning']) ||
      value.executionJobId !== null ||
      typeof value.confirmation.planHash !== 'string' ||
      !value.confirmation.planHash ||
      value.confirmation.planHash.length > 8192 ||
      !date(value.confirmation.expiresAtUtc) ||
      value.confirmation.expiresAtUtc !== value.readyExpiresAtUtc ||
      typeof value.confirmation.warning !== 'string' ||
      !value.confirmation.warning ||
      value.confirmation.warning.length > 2048)
  )
    throw new Error('目录清理确认信息无法核实，保留原引用')
  return value
}
export function purgingFollows(previous: TreePurgingIntent, current: TreePurgingIntent) {
  if (
    previous.id !== current.id ||
    !samePurgingCommand(previous.command, current.command) ||
    current.version < previous.version ||
    (previous.executionJobId !== null && previous.executionJobId !== current.executionJobId) ||
    (previous.entryCount !== null &&
      (previous.entryCount !== current.entryCount ||
        previous.totalBytes !== current.totalBytes ||
        previous.archiveBytes !== current.archiveBytes ||
        previous.readyExpiresAtUtc !== current.readyExpiresAtUtc)) ||
    (['deletionStarted', 'archiveRemoved', 'originalsRemoved'] as const).some(
      (key) => previous[key] === true && current[key] !== true,
    )
  )
    return false
  if (previous.version === current.version) {
    if (
      previous.state !== current.state ||
      previous.executionJobId !== current.executionJobId ||
      previous.entryCount !== current.entryCount ||
      previous.totalBytes !== current.totalBytes ||
      previous.archiveBytes !== current.archiveBytes ||
      previous.readyExpiresAtUtc !== current.readyExpiresAtUtc ||
      previous.resultCode !== current.resultCode ||
      (['deletionStarted', 'archiveRemoved', 'originalsRemoved'] as const).some(
        (key) => previous[key] !== current[key],
      ) ||
      previous.confirmation?.planHash !== current.confirmation?.planHash ||
      previous.confirmation?.expiresAtUtc !== current.confirmation?.expiresAtUtc ||
      previous.confirmation?.warning !== current.confirmation?.warning
    )
      return false
  }
  return true
}
