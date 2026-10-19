import type { FileEntry, FileRoot } from '@/services/fileManagement'
import { treeUuid, validTreeLocation } from '@/services/fileTreeCopies'
import type { TreeLocation } from '@/services/fileTreeCopies'

export { treeUuid, treeTerminal } from '@/services/fileTreeCopies'
export interface TreeRecyclingCommand {
  source: TreeLocation
  expectedVersion: string
}
export interface TreeRecyclingIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  state: string
  command: TreeRecyclingCommand
  entryCount: number | null
  totalBytes: number | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  captureId: string | null
  archiveBytes: number | null
  archiveSha256: string | null
  captureVerified: boolean
  captureMayBeRetained: boolean | null
  sourceMayHaveChanged: boolean | null
  sourceRemoved: boolean | null
  originalsRetained: boolean | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
export interface TreeCapture {
  id: string
  intentId: string
  source: TreeRecyclingCommand
  manifestSha256: string
  entryCount: number
  totalBytes: number
  archiveBytes: number
  archiveSha256: string
  createdAtUtc: string
}
export interface TreeCapturePage {
  items: TreeCapture[]
  next: string | null
}
const hash = /^[A-F0-9]{64}$/
const states = [
  'Queued',
  'Planning',
  'Ready',
  'RecyclingQueued',
  'Recycling',
  'Unknown',
  'Succeeded',
  'Failed',
  'Canceled',
  'Expired',
  'Acknowledged',
]
export function canRecycleRoot(root: FileRoot | undefined) {
  return [
    'files-list',
    'files-download',
    'files-mutate',
    'files-history',
    'files-archive',
    'files-tree-trash',
  ].every((action) => root?.actions.includes(action))
}
export function canRecycleDirectory(root: FileRoot | undefined, entry: FileEntry) {
  return (
    entry.kind === 'Directory' &&
    !entry.isProtected &&
    hash.test(entry.version) &&
    canRecycleRoot(root)
  )
}
export function validRecyclingCommand(value: TreeRecyclingCommand) {
  return !!value && validTreeLocation(value.source) && hash.test(value.expectedVersion)
}
export function sameRecyclingCommand(left: TreeRecyclingCommand, right: TreeRecyclingCommand) {
  return (
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path &&
    left.expectedVersion === right.expectedVersion
  )
}
export function recyclingState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证完整目录',
        Ready: '计划已生成，尚未回收',
        RecyclingQueued: '等待回收',
        Recycling: '正在验证 HDD 归档并保留原件',
        Unknown: '结果未知，保留路径锁与证据',
        Succeeded: '原目录回收任务已完成',
        Failed: '回收未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已接受未知历史结果，不代表成功',
      } as Record<string, string>
    )[state] || state
  )
}
export function recyclingResult(code: string) {
  return (
    (
      {
        FILES_TREE_RECYCLE_COMPLETED: '归档已核验，原件已私有保留，原目录路径已移除。',
        FILES_RESULT_UNKNOWN: '结果无法核实，保留锁与现场，不自动重新回收。',
        FILES_RECEIPT_UNAVAILABLE: '未取得原执行回执，副作用未知；只查询原任务，不重做。',
        FILES_TREE_READY_FOR_CONFIRMATION: '完整清单已生成，尚未开始回收。',
        FILES_TREE_CHANGED: '目录内容已变化，原计划不会自动重做。',
        FILES_TREE_PROTECTED: '目录包含受保护成员，计划已拒绝。',
        FILES_PLAN_EXPIRED: '原计划已过期，不会自动执行。',
        FILES_CANCELED: '已取消或被取消栅栏阻止，以原任务回执为准。',
        FILES_UNKNOWN_ACKNOWLEDGED: '未知历史结果已接受，不代表回收成功；证据保留。',
      } as Record<string, string>
    )[code] || code
  )
}
export function assertRecyclingIntent(value: TreeRecyclingIntent, expectedId?: string) {
  const hasPlan = value?.entryCount !== null || value?.totalBytes !== null
  if (
    !value ||
    !treeUuid.test(value.id) ||
    value.planningJobId !== value.id ||
    (expectedId && expectedId !== value.id) ||
    (value.executionJobId !== null &&
      (!treeUuid.test(value.executionJobId) || value.executionJobId === value.id)) ||
    !states.includes(value.state) ||
    !validRecyclingCommand(value.command) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    typeof value.captureVerified !== 'boolean' ||
    [
      value.captureMayBeRetained,
      value.sourceMayHaveChanged,
      value.sourceRemoved,
      value.originalsRetained,
    ].some((flag) => flag !== null && typeof flag !== 'boolean') ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    (hasPlan &&
      (!Number.isSafeInteger(value.entryCount) ||
        value.entryCount! < 1 ||
        value.entryCount! > 10000 ||
        !Number.isSafeInteger(value.totalBytes) ||
        value.totalBytes! < 0 ||
        value.totalBytes! > 16 * 1024 ** 3)) ||
    (['Ready', 'RecyclingQueued', 'Recycling', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      value.state,
    ) &&
      (!hasPlan ||
        !value.readyExpiresAtUtc ||
        !Number.isFinite(Date.parse(value.readyExpiresAtUtc)))) ||
    (['RecyclingQueued', 'Recycling', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      value.state,
    ) &&
      !value.executionJobId) ||
    (value.captureVerified
      ? value.captureId !== value.executionJobId ||
        !value.captureId ||
        !Number.isSafeInteger(value.archiveBytes) ||
        value.archiveBytes! < 1 ||
        value.archiveBytes! > 17 * 1024 ** 3 ||
        !hash.test(value.archiveSha256 || '') ||
        value.captureMayBeRetained !== true
      : value.captureId !== null || value.archiveBytes !== null || value.archiveSha256 !== null) ||
    (value.sourceMayHaveChanged === true && !value.captureVerified) ||
    (value.state === 'Succeeded' &&
      !(
        value.captureVerified &&
        value.sourceMayHaveChanged === true &&
        value.sourceRemoved === true &&
        value.originalsRetained === true
      )) ||
    (value.state !== 'Succeeded' &&
      (value.sourceRemoved === true || value.originalsRetained === true)) ||
    (value.confirmation !== null &&
      (value.state !== 'Ready' ||
        typeof value.confirmation.planHash !== 'string' ||
        !value.confirmation.planHash ||
        value.confirmation.planHash.length > 4096 ||
        !Number.isFinite(Date.parse(value.confirmation.expiresAtUtc)) ||
        typeof value.confirmation.warning !== 'string'))
  )
    throw new Error('目录回收响应无法核实；保留原引用，不重新提交')
  return value
}
export function assertCapturePage(value: TreeCapturePage, rootId: string, path = '') {
  if (
    !value ||
    !Array.isArray(value.items) ||
    value.items.length > 25 ||
    (value.next !== null &&
      (!treeUuid.test(value.next) || value.next !== value.items[value.items.length - 1]?.id))
  )
    throw new Error('目录捕获分页无法核实')
  const seen = new Set<string>()
  for (const row of value.items) {
    if (
      !row ||
      !treeUuid.test(row.id) ||
      !treeUuid.test(row.intentId) ||
      row.id === row.intentId ||
      seen.has(row.id) ||
      !validRecyclingCommand(row.source) ||
      row.source.source.rootId !== rootId ||
      (path && row.source.source.path !== path) ||
      !hash.test(row.manifestSha256) ||
      !Number.isSafeInteger(row.entryCount) ||
      row.entryCount < 1 ||
      row.entryCount > 10000 ||
      !Number.isSafeInteger(row.totalBytes) ||
      row.totalBytes < 0 ||
      row.totalBytes > 16 * 1024 ** 3 ||
      !Number.isSafeInteger(row.archiveBytes) ||
      row.archiveBytes < 1 ||
      row.archiveBytes > 17 * 1024 ** 3 ||
      !hash.test(row.archiveSha256) ||
      !Number.isFinite(Date.parse(row.createdAtUtc))
    )
      throw new Error('目录捕获证明或作用域无法核实')
    seen.add(row.id)
  }
  return value
}
