import { apiUrl } from '@/services/apiClient'
import type { FileEntry, FileRoot } from '@/services/fileManagement'
import { assertTreeIntent, validTreeLocation } from '@/services/fileTreeCopies'
import type { TreeCopyCommand, TreeCopyIntent } from '@/services/fileTreeCopies'

export { treeUuid, treeTerminal, validTreeLocation } from '@/services/fileTreeCopies'
export type PackingFormat = 'tar' | 'tar.gz'
export interface TreePackingCommand extends TreeCopyCommand {
  format: PackingFormat
}
export interface TreePackingIntent extends Omit<TreeCopyIntent, 'command'> {
  command: TreePackingCommand
  archiveBytes: number | null
  archiveSha256: string | null
}
export function packingState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证完整目录',
        Ready: '计划已生成，尚未压缩',
        PackingQueued: '等待压缩',
        Packing: '正在压缩并校验',
        Unknown: '结果未知，保留锁与证据',
        Succeeded: '原压缩任务已完成',
        Failed: '压缩未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已确认未知结果，不代表成功',
      } as Record<string, string>
    )[state] || state
  )
}
export function canPackDirectory(root: FileRoot | undefined, entry: FileEntry) {
  return (
    entry.kind === 'Directory' &&
    !entry.isProtected &&
    /^[A-F0-9]{64}$/.test(entry.version) &&
    ['files-list', 'files-download', 'files-archive', 'files-tree-pack'].every((action) =>
      root?.actions.includes(action),
    )
  )
}
export function packingResult(code: string) {
  return (
    (
      {
        FILES_TREE_PROTECTED: '目录包含受保护成员，整个计划已拒绝；没有开始压缩。',
        FILES_TREE_CHANGED: '目录内容已变化，请核实原任务；不会自动重做。',
        FILES_VERSION_CONFLICT: '源目录版本已变化，请核实原任务；不会自动重做。',
        FILES_RESULT_UNKNOWN: '结果无法核实，路径锁和证据保留；不会自动重做。',
        FILES_RECEIPT_UNAVAILABLE: '尚未取得原执行回执，只查询原任务，不重复执行。',
        FILES_PLAN_EXPIRED: '计划已过期，原计划不会自动执行。',
        FILES_CANCELED: '原操作已取消，或被取消栅栏阻止。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已接受未知历史结果，不代表压缩成功；证据保留。',
        FILES_TREE_READY_FOR_CONFIRMATION: '完整扫描已通过，等待显式确认。',
        FILES_TREE_PACK_COMPLETED: '原任务已完成归档并校验。',
      } as Record<string, string>
    )[code] || code
  )
}
export function canReceivePacking(root: FileRoot) {
  return ['files-upload', 'files-mutate', 'files-archive', 'files-tree-pack'].every((action) =>
    root.actions.includes(action),
  )
}
export function validPackingCommand(command: TreePackingCommand) {
  if (
    !command ||
    !validTreeLocation(command.source) ||
    !validTreeLocation(command.target) ||
    !/^[A-F0-9]{64}$/.test(command.expectedVersion) ||
    !['tar', 'tar.gz'].includes(command.format) ||
    !command.target.path.endsWith('.' + command.format)
  )
    return false
  if (command.source.rootId !== command.target.rootId) return true
  const source = command.source.path,
    target = command.target.path
  return source !== target && !target.startsWith(source + '/') && !source.startsWith(target + '/')
}
export function samePackingCommand(left: TreePackingCommand, right: TreePackingCommand) {
  return (
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path &&
    left.target.rootId === right.target.rootId &&
    left.target.path === right.target.path &&
    left.expectedVersion === right.expectedVersion &&
    left.format === right.format
  )
}
export function assertPackingIntent(value: TreePackingIntent, id?: string) {
  if (
    !value ||
    ['CopyQueued', 'Copying'].includes(value.state) ||
    !validPackingCommand(value.command)
  )
    throw new Error('压缩任务响应无法核实；保留原引用，不重新提交')
  assertTreeIntent(
    {
      ...value,
      state:
        value.state === 'PackingQueued'
          ? 'CopyQueued'
          : value.state === 'Packing'
            ? 'Copying'
            : value.state,
    },
    id,
  )
  if (
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    (value.state === 'Succeeded'
      ? !value.executionJobId ||
        value.entryCount === null ||
        value.totalBytes === null ||
        value.partialRetained ||
        !Number.isSafeInteger(value.archiveBytes) ||
        value.archiveBytes! < 1 ||
        value.archiveBytes! > 17 * 1024 ** 3 ||
        !/^[A-F0-9]{64}$/.test(value.archiveSha256 || '') ||
        !/^[A-F0-9]{64}$/.test(value.publishedVersion || '')
      : value.archiveBytes !== null ||
        value.archiveSha256 !== null ||
        value.publishedVersion !== null)
  )
    throw new Error('归档结果无法核实；保留原引用，不开始下载')
  return value
}
/** Native browser streaming; never materialize a potentially 17 GiB Blob in the page. */
export function packingDownloadUrl(value: TreePackingIntent) {
  assertPackingIntent(value)
  if (value.state !== 'Succeeded') throw new Error('归档尚未完成')
  return apiUrl(
    `files/tree-packings/${value.id}/download?${new URLSearchParams({ expectedVersion: value.publishedVersion! })}`,
  )
}
