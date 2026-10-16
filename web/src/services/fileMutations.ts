import type { FileEntry } from './fileManagement'

export type FileMutationKind = 'mkdir' | 'copy' | 'move' | 'chmod'
export interface FileLocation {
  rootId: string
  path: string
}
export interface FileMutationCommand {
  kind: FileMutationKind
  source: FileLocation | null
  target: FileLocation | null
  expectedVersion: string
  mode: string
}
export interface FileMutationPlan {
  planHash: string
  expiresAtUtc: string
  command: FileMutationCommand
  sizeBytes: number
  warning: string
}
export interface FileMutationIntent {
  id: string
  jobId: string
  command: FileMutationCommand
  sizeBytes: number
  state: string
  expiresAtUtc: string
  resultCode: string | null
  version: number
  publishedVersion: string | null
  sourceRetained: boolean
}
export interface FrozenFileMutation {
  planHash: string
  confirmed: true
  idempotencyKey: string
}
export function mutationLabel(kind: FileMutationKind) {
  return { mkdir: '新建目录', copy: '复制文件', move: '移动文件', chmod: '修改权限' }[kind]
}
export function mutationTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Failed', 'Canceled', 'Acknowledged'].includes(state)
}
export function mutationState(state: string) {
  return (
    (
      {
        Pending: '等待执行',
        Running: '执行中',
        Unknown: '结果未知，等待人工核实',
        Succeeded: '已完成',
        Superseded: '历史已完成，当前目标已变化',
        Failed: '失败',
        Canceled: '已取消',
        Acknowledged: '已人工确认未知结果（不代表成功）',
      } as Record<string, string>
    )[state] || state
  )
}
export function mutationEligible(entry: FileEntry) {
  return entry.kind === 'File' && !entry.isProtected
}
export function mutationTarget(path: string) {
  if (
    !path ||
    new TextEncoder().encode(path).length > 4096 ||
    path.includes('\\') ||
    Array.from(path).some((character) => {
      const code = character.charCodeAt(0)
      return code < 32 || (code >= 127 && code <= 159)
    })
  )
    return false
  return path
    .split('/')
    .every(
      (part) =>
        part !== '' &&
        part !== '.' &&
        part !== '..' &&
        new TextEncoder().encode(part).length <= 255,
    )
}
