import type { FileRoot } from '@/services/fileManagement'
import type { TreeCopyIntent, TreeLocation } from '@/services/fileTreeCopies'
import { treeUuid, validTreeLocation } from '@/services/fileTreeCopies'
import { assertCapturePage } from '@/services/fileTreeRecyclings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'

export { treeUuid, treeTerminal } from '@/services/fileTreeCopies'
// The HTTP request never contains the historical owner, version or root proof.
export interface TreeRestoringRequest {
  captureId: string
  target: TreeLocation
}
export interface TreeRestoringCommand extends TreeRestoringRequest {
  source: TreeLocation
  captureKind: 'Trash' | 'Overwrite'
}
export interface TreeRestoringIntent extends Omit<TreeCopyIntent, 'command'> {
  command: TreeRestoringCommand
  archiveBytes: number | null
  archiveSha256: string | null
}
const hash = /^[A-F0-9]{64}$/
const uuid = (id: string) => treeUuid.test(id) && id !== '00000000-0000-0000-0000-000000000000'
const date = (value: string | null) =>
  typeof value === 'string' && Number.isFinite(Date.parse(value))
const privateKeys = [
  'captureOwner',
  'ownerHash',
  'loginBinding',
  'planJson',
  'commandJson',
  'requestHash',
]
const privateMaterial = (value: object) =>
  Object.keys(value).some((key) => privateKeys.includes(key))
export function validRestoringRequest(value: TreeRestoringRequest) {
  return (
    !!value &&
    typeof value.captureId === 'string' &&
    uuid(value.captureId) &&
    validTreeLocation(value.target)
  )
}
export function sameRestoringRequest(left: TreeRestoringRequest, right: TreeRestoringRequest) {
  return (
    left.captureId === right.captureId &&
    left.target.rootId === right.target.rootId &&
    left.target.path === right.target.path
  )
}
export function sameRestoringCommand(left: TreeRestoringCommand, right: TreeRestoringCommand) {
  return (
    sameRestoringRequest(left, right) &&
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path &&
    left.captureKind === right.captureKind
  )
}
export function canReceiveRestoring(root: FileRoot) {
  return ['files-upload', 'files-mutate', 'files-archive', 'files-tree-restore'].every((action) =>
    root.actions.includes(action),
  )
}
export function canRestoreCapture(root: FileRoot | undefined, capture: TreeCapture) {
  if (
    !root ||
    !uuid(capture.id) ||
    !['files-list', 'files-download', 'files-history', 'files-tree-restore'].every((action) =>
      root.actions.includes(action),
    )
  )
    return false
  try {
    assertCapturePage({ items: [capture], next: null }, root.id)
    return true
  } catch {
    return false
  }
}
export function restoringState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证历史归档与完整清单',
        Ready: '计划已生成，尚未恢复',
        RestoringQueued: '等待恢复',
        Restoring: '正在恢复并校验新目录',
        Unknown: '结果未知，保留锁与证据',
        Succeeded: '原目录恢复任务已完成',
        Failed: '恢复未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已接受未知历史结果，不代表成功',
      } as Record<string, string>
    )[state] || '无法核实的恢复状态'
  )
}
export function restoringResult(code: string) {
  return (
    (
      {
        FILES_TREE_RESTORE_COMPLETED: '原历史归档已恢复至新目录并完成校验。',
        FILES_TREE_READY_FOR_CONFIRMATION: '完整归档与清单已验证，尚未开始恢复。',
        FILES_CAPTURE_NOT_FOUND: '未找到原捕获记录，不会自动重新选择归档。',
        FILES_TREE_BACKUP_NOT_FOUND: '历史归档当前不可用；没有开始恢复，请核实存储。',
        FILES_TREE_BACKUP_INCOMPLETE: '历史归档未完成验证，不允许恢复。',
        FILES_CAPTURE_PROOF_CHANGED: '历史捕获证明已变化，不会继续执行。',
        FILES_VERSION_CONFLICT: '目标已存在或原计划条件已变化；不会覆盖，请核实目标。',
        FILES_TREE_PROTECTED: '归档包含受保护成员，计划已拒绝。',
        FILES_RESULT_UNKNOWN: '结果无法核实；保留锁与现场，不自动重新恢复。',
        FILES_RECEIPT_UNAVAILABLE: '尚未取得原回执，只查询原任务，不重复执行。',
        FILES_PLAN_EXPIRED: '原计划已过期，不会自动执行。',
        FILES_CANCELED: '已取消或被取消栅栏阻止，以原任务回执为准。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已接受未知历史结果，不代表恢复成功；证据保留。',
      } as Record<string, string>
    )[code] || code
  )
}
export function assertRestoringIntent(value: TreeRestoringIntent, expectedId?: string) {
  const command = value?.command
  const hasPlan = value?.entryCount !== null || value?.totalBytes !== null
  const executed = [
    'RestoringQueued',
    'Restoring',
    'Unknown',
    'Succeeded',
    'Acknowledged',
  ].includes(value?.state)
  if (
    !value ||
    privateMaterial(value) ||
    !uuid(value.id) ||
    value.planningJobId !== value.id ||
    (expectedId && value.id !== expectedId) ||
    (value.executionJobId !== null &&
      (!uuid(value.executionJobId) || value.executionJobId === value.id)) ||
    ![
      'Queued',
      'Planning',
      'Ready',
      'RestoringQueued',
      'Restoring',
      'Unknown',
      'Succeeded',
      'Failed',
      'Canceled',
      'Expired',
      'Acknowledged',
    ].includes(value.state) ||
    !command ||
    privateMaterial(command) ||
    !validRestoringRequest(command) ||
    !validTreeLocation(command.source) ||
    !['Trash', 'Overwrite'].includes(command.captureKind) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    typeof value.partialRetained !== 'boolean' ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    (hasPlan
      ? !Number.isSafeInteger(value.entryCount) ||
        value.entryCount! < 1 ||
        value.entryCount! > 10000 ||
        !Number.isSafeInteger(value.totalBytes) ||
        value.totalBytes! < 0 ||
        value.totalBytes! > 16 * 1024 ** 3 ||
        !Number.isSafeInteger(value.archiveBytes) ||
        value.archiveBytes! < 1 ||
        value.archiveBytes! > 17 * 1024 ** 3 ||
        !hash.test(value.archiveSha256 || '') ||
        !date(value.readyExpiresAtUtc)
      : value.archiveBytes !== null ||
        value.archiveSha256 !== null ||
        value.readyExpiresAtUtc !== null) ||
    ((executed || value.state === 'Ready') && !hasPlan) ||
    (executed && !value.executionJobId) ||
    (value.state === 'Succeeded'
      ? value.partialRetained || !hash.test(value.publishedVersion || '')
      : value.publishedVersion !== null) ||
    (value.confirmation !== null &&
      (value.state !== 'Ready' ||
        value.executionJobId !== null ||
        typeof value.confirmation.planHash !== 'string' ||
        !value.confirmation.planHash ||
        value.confirmation.planHash.length > 4096 ||
        !date(value.confirmation.expiresAtUtc) ||
        typeof value.confirmation.warning !== 'string'))
  )
    throw new Error('目录恢复响应或历史归档证明无法核实；保留原引用，不重新提交')
  return value
}
