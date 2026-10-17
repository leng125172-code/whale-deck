import type { FileEntry, FileRoot } from '@/services/fileManagement'

export interface TreeLocation {
  rootId: string
  path: string
}
export interface TreeCopyCommand {
  source: TreeLocation
  expectedVersion: string
  target: TreeLocation
}
export interface TreeCopyIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  state: string
  command: TreeCopyCommand
  entryCount: number | null
  totalBytes: number | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  publishedVersion: string | null
  partialRetained: boolean
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
export const treeUuid = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/
export function treeTerminal(state: string) {
  return ['Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged'].includes(state)
}
export function treeState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在验证完整目录',
        Ready: '计划已生成，尚未复制',
        CopyQueued: '等待复制',
        Copying: '正在复制并校验',
        Unknown: '结果未知，保留锁与证据',
        Succeeded: '原复制任务已完成',
        Failed: '复制未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已确认未知结果，不代表成功',
      } as Record<string, string>
    )[state] || state
  )
}
export function canCopyDirectory(root: FileRoot | undefined, entry: FileEntry) {
  return (
    entry.kind === 'Directory' &&
    !entry.isProtected &&
    /^[A-F0-9]{64}$/.test(entry.version) &&
    ['files-list', 'files-download', 'files-tree-copy'].every((action) =>
      root?.actions.includes(action),
    )
  )
}
export function canReceiveDirectory(root: FileRoot) {
  return ['files-upload', 'files-mutate', 'files-tree-copy'].every((action) =>
    root.actions.includes(action),
  )
}
export function validTreeLocation(value: TreeLocation | null | undefined) {
  return (
    !!value &&
    typeof value.rootId === 'string' &&
    typeof value.path === 'string' &&
    /^[a-z0-9.-]{1,64}$/.test(value.rootId) &&
    new TextEncoder().encode(value.path).length <= 4096 &&
    ![...value.path].some(
      (character) =>
        character === '\\' ||
        character.charCodeAt(0) < 32 ||
        (character.charCodeAt(0) >= 127 && character.charCodeAt(0) <= 159),
    ) &&
    value.path
      .split('/')
      .every(
        (part) =>
          part !== '' &&
          part !== '.' &&
          part !== '..' &&
          new TextEncoder().encode(part).length <= 255,
      )
  )
}
export function assertTreeIntent(value: TreeCopyIntent, id?: string) {
  if (
    !value ||
    !treeUuid.test(value.id) ||
    value.planningJobId !== value.id ||
    (id && id !== value.id) ||
    (value.executionJobId !== null &&
      (!treeUuid.test(value.executionJobId) || value.executionJobId === value.id)) ||
    ![
      'Queued',
      'Planning',
      'Ready',
      'CopyQueued',
      'Copying',
      'Unknown',
      'Succeeded',
      'Failed',
      'Canceled',
      'Expired',
      'Acknowledged',
    ].includes(value.state) ||
    !value.command ||
    !validTreeLocation(value.command.source) ||
    !validTreeLocation(value.command.target) ||
    !/^[A-F0-9]{64}$/.test(value.command.expectedVersion) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    typeof value.partialRetained !== 'boolean' ||
    (value.entryCount !== null &&
      (!Number.isSafeInteger(value.entryCount) ||
        value.entryCount < 1 ||
        value.entryCount > 10000)) ||
    (value.totalBytes !== null &&
      (!Number.isSafeInteger(value.totalBytes) ||
        value.totalBytes < 0 ||
        value.totalBytes > 16 * 1024 ** 3)) ||
    (value.confirmation !== null &&
      (value.state !== 'Ready' ||
        value.executionJobId !== null ||
        !value.confirmation.planHash ||
        value.confirmation.planHash.length > 4096 ||
        !Number.isFinite(Date.parse(value.confirmation.expiresAtUtc))))
  )
    throw new Error('目录任务响应无法核实；保留原引用，不重新提交')
  return value
}
