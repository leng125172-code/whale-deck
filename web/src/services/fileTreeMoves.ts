import type { FileEntry, FileRoot } from '@/services/fileManagement'
import { assertTreeIntent } from '@/services/fileTreeCopies'
import type { TreeCopyCommand, TreeCopyIntent } from '@/services/fileTreeCopies'

export { treeUuid, treeTerminal, validTreeLocation } from '@/services/fileTreeCopies'
export type TreeMoveCommand = TreeCopyCommand
export interface TreeMoveIntent extends TreeCopyIntent {
  destinationPublished: boolean | null
  sourceRemoved: boolean | null
  sourceRetained: boolean | null
}
export function moveTreeState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证完整目录',
        Ready: '计划已生成，尚未移动',
        MoveQueued: '等待移动',
        Moving: '正在核验、发布目标并保留原件',
        Unknown: '结果未知，保留锁与证据',
        Succeeded: '原移动任务已完成',
        Failed: '移动未完成',
        Canceled: '已取消',
        Expired: '未确认的计划已过期',
        Acknowledged: '已确认未知结果，不代表成功',
      } as Record<string, string>
    )[state] || state
  )
}
export function canMoveDirectory(root: FileRoot | undefined, entry: FileEntry) {
  return (
    entry.kind === 'Directory' &&
    !entry.isProtected &&
    /^[A-F0-9]{64}$/.test(entry.version) &&
    ['files-list', 'files-download', 'files-mutate', 'files-tree-move'].every((action) =>
      root?.actions.includes(action),
    )
  )
}
export function canReceiveMovedDirectory(root: FileRoot) {
  return ['files-upload', 'files-mutate', 'files-tree-move'].every((action) =>
    root.actions.includes(action),
  )
}
export function moveFact(value: boolean | null) {
  return value === null ? '尚无可核实回执' : value ? '原回执确认' : '原回执未确认完成'
}
export function moveResult(code: string) {
  return (
    (
      {
        FILES_TREE_PROTECTED: '目录包含受保护成员，完整计划已拒绝；未开始移动。',
        FILES_TREE_CHANGED: '源目录内容已变化；核实原任务，不自动重做。',
        FILES_VERSION_CONFLICT: '原目录版本已变化；核实原任务，不自动重做。',
        FILES_RESULT_UNKNOWN: '不能核实完整结果；保留路径锁和证据，不自动重做。',
        FILES_RECEIPT_UNAVAILABLE: '尚未取得原执行回执；只查询原任务，不重复执行。',
        FILES_PLAN_EXPIRED: '未确认的计划已过期；原计划不会执行。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已明确接受未知历史结果，不代表移动成功；原证据保留。',
        FILES_CANCELED: '原任务已取消或已被取消栅栏阻止。',
      } as Record<string, string>
    )[code] || code
  )
}
export function assertTreeMoveIntent(value: TreeMoveIntent, id?: string) {
  if (!value || ['CopyQueued', 'Copying'].includes(value.state))
    throw new Error('目录移动响应无法核实；保留原引用，不重新提交')
  assertTreeIntent(
    {
      ...value,
      state:
        value.state === 'MoveQueued'
          ? 'CopyQueued'
          : value.state === 'Moving'
            ? 'Copying'
            : value.state,
    },
    id,
  )
  const facts = [value.destinationPublished, value.sourceRemoved, value.sourceRetained]
  if (
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    facts.some((fact) => fact !== null && typeof fact !== 'boolean') ||
    (facts.some((fact) => fact === null) && !facts.every((fact) => fact === null)) ||
    (value.state === 'Succeeded' &&
      (!facts.every((fact) => fact === true) ||
        value.partialRetained ||
        !/^[A-F0-9]{64}$/.test(value.publishedVersion || ''))) ||
    (value.state !== 'Succeeded' &&
      (value.sourceRemoved === true ||
        value.sourceRetained === true ||
        (value.destinationPublished === true && !value.partialRetained)))
  )
    throw new Error('目录移动结果事实无法核实；保留原引用，不重新提交')
  return value
}
