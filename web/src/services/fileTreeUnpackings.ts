import type { FileEntry, FileRoot } from '@/services/fileManagement'
import { assertTreeIntent, validTreeLocation } from '@/services/fileTreeCopies'
import type { TreeCopyCommand, TreeCopyIntent } from '@/services/fileTreeCopies'

export { treeUuid, treeTerminal, validTreeLocation } from '@/services/fileTreeCopies'
export type UnpackingFormat = 'tar' | 'tar.gz'
export interface TreeUnpackingCommand extends TreeCopyCommand {
  format: UnpackingFormat
}
export interface TreeUnpackingIntent extends Omit<TreeCopyIntent, 'command'> {
  command: TreeUnpackingCommand
  archiveBytes: number | null
  archiveSha256: string | null
}
export function unpackingState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证输入归档与完整清单',
        Ready: '计划已生成，尚未解压',
        UnpackingQueued: '等待解压',
        Unpacking: '正在解压并校验',
        Unknown: '结果未知，保留锁与证据',
        Succeeded: '原解压任务已完成',
        Failed: '解压未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已确认未知结果，不代表成功',
      } as Record<string, string>
    )[state] || state
  )
}
export function canUnpackArchive(root: FileRoot | undefined, entry: FileEntry) {
  return (
    entry.kind === 'File' &&
    Number.isSafeInteger(entry.sizeBytes) &&
    entry.sizeBytes > 0 &&
    entry.sizeBytes <= 17 * 1024 ** 3 &&
    !entry.isProtected &&
    /^[A-F0-9]{64}$/.test(entry.version) &&
    ['files-list', 'files-download', 'files-archive', 'files-tree-unpack'].every((action) =>
      root?.actions.includes(action),
    )
  )
}
export function unpackingResult(code: string) {
  return (
    (
      {
        FILES_TREE_PROTECTED: '目录包含受保护成员，整个计划已拒绝；没有开始解压。',
        FILES_TREE_CHANGED: '目录内容已变化，请核实原任务；不会自动重做。',
        FILES_VERSION_CONFLICT: '源归档版本已变化，请核实原任务；不会自动重做。',
        FILES_RESULT_UNKNOWN: '结果无法核实，路径锁和证据保留；不会自动重做。',
        FILES_RECEIPT_UNAVAILABLE: '尚未取得原执行回执，只查询原任务，不重复执行。',
        FILES_PLAN_EXPIRED: '计划已过期，原计划不会自动执行。',
        FILES_CANCELED: '原操作已取消，或被取消栅栏阻止。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已接受未知历史结果，不代表解压成功；证据保留。',
        FILES_TREE_READY_FOR_CONFIRMATION: '完整扫描已通过，等待显式确认。',
        FILES_TREE_UNPACK_COMPLETED: '原任务已解压至新目录并完成校验。',
      } as Record<string, string>
    )[code] || code
  )
}
export function canReceiveUnpacking(root: FileRoot) {
  return ['files-upload', 'files-mutate', 'files-archive', 'files-tree-unpack'].every((action) =>
    root.actions.includes(action),
  )
}
export function validUnpackingCommand(command: TreeUnpackingCommand) {
  if (
    !command ||
    !validTreeLocation(command.source) ||
    !validTreeLocation(command.target) ||
    !/^[A-F0-9]{64}$/.test(command.expectedVersion) ||
    !['tar', 'tar.gz'].includes(command.format)
  )
    return false
  if (command.source.rootId !== command.target.rootId) return true
  const source = command.source.path,
    target = command.target.path
  return source !== target && !target.startsWith(source + '/') && !source.startsWith(target + '/')
}
export function sameUnpackingCommand(left: TreeUnpackingCommand, right: TreeUnpackingCommand) {
  return (
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path &&
    left.target.rootId === right.target.rootId &&
    left.target.path === right.target.path &&
    left.expectedVersion === right.expectedVersion &&
    left.format === right.format
  )
}
export function assertUnpackingIntent(value: TreeUnpackingIntent, id?: string) {
  if (
    !value ||
    ['CopyQueued', 'Copying'].includes(value.state) ||
    !validUnpackingCommand(value.command)
  )
    throw new Error('解压任务响应无法核实；保留原引用，不重新提交')
  assertTreeIntent(
    {
      ...value,
      state:
        value.state === 'UnpackingQueued'
          ? 'CopyQueued'
          : value.state === 'Unpacking'
            ? 'Copying'
            : value.state,
    },
    id,
  )
  const hasPlan = value.entryCount !== null || value.totalBytes !== null
  if (
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    (hasPlan
      ? value.entryCount === null ||
        value.totalBytes === null ||
        !Number.isSafeInteger(value.archiveBytes) ||
        value.archiveBytes! < 1 ||
        value.archiveBytes! > 17 * 1024 ** 3 ||
        !/^[A-F0-9]{64}$/.test(value.archiveSha256 || '')
      : value.archiveBytes !== null || value.archiveSha256 !== null) ||
    (['Ready', 'UnpackingQueued', 'Unpacking', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      value.state,
    ) &&
      !hasPlan) ||
    (['UnpackingQueued', 'Unpacking', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      value.state,
    ) &&
      !value.executionJobId) ||
    (value.state === 'Succeeded'
      ? value.partialRetained || !/^[A-F0-9]{64}$/.test(value.publishedVersion || '')
      : value.publishedVersion !== null)
  )
    throw new Error('解压结果或输入归档证明无法核实；保留原引用，不重新提交')
  return value
}
