import type { FileEntry } from './fileManagement'
import { maximumFileBytes } from './fileManagement'
import { mutationTarget } from './fileMutations'
export { mutationState as archiveState, mutationTerminal as archiveTerminal } from './fileMutations'

export type FileArchiveKind = 'trash' | 'restore' | 'purge'
export interface FileVersion {
  id: string
  rootId: string
  path: string
  sourceVersion: string
  sizeBytes: number
  sha256: string
  capturedAtUtc: string
  captureKind: 'Overwrite' | 'Trash'
  captureState: string
  availability: string
  differentSourceDevice: boolean
}
export interface FileHistoryPage {
  entries: FileVersion[]
  nextCursor: string
  version: string
  scanned: number
  invalidRecords: number
  complete: boolean
}
export interface FileArchiveCommand {
  kind: FileArchiveKind
  location: { rootId: string; path: string }
  versionId: string | null
  expectedVersion: string
}
export interface FileArchivePlan {
  planHash: string
  expiresAtUtc: string
  command: FileArchiveCommand
  sizeBytes: number
  warning: string
  operationId: string
  idempotencyKey: string
}
export interface FileArchiveIntent {
  id: string
  jobId: string
  command: FileArchiveCommand
  sizeBytes: number
  state: string
  expiresAtUtc: string
  resultCode: string | null
  version: number
  publishedVersion: string | null
  sourceRetained: boolean
}
export interface FileArchiveRegistration {
  id: string
  state: 'Issued' | 'Registered' | 'Expired' | 'Canceled'
  plan: FileArchivePlan | null
  intent: FileArchiveIntent | null
}
export interface FrozenArchiveRegistration {
  planHash: string
  confirmed: true
  idempotencyKey: string
}
export function archiveLabel(kind: FileArchiveKind) {
  return { trash: '移入回收站', restore: '恢复历史版本', purge: '清理此 HDD 副本' }[kind]
}
export function archiveEligible(entry: FileEntry) {
  return (
    entry.kind === 'File' &&
    !entry.isProtected &&
    Number.isSafeInteger(entry.sizeBytes) &&
    entry.sizeBytes >= 0 &&
    entry.sizeBytes <= maximumFileBytes &&
    /^[A-F0-9]{64}$/.test(entry.version)
  )
}
export function archiveVersionReady(version: FileVersion) {
  return (
    version.availability === 'Present' &&
    ['Available', 'Trashed', 'Restored', 'Canceled'].includes(version.captureState) &&
    /^(?!00000000-0000-0000-0000-000000000000$)[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/.test(
      version.id,
    ) &&
    mutationTarget(version.path) &&
    Number.isSafeInteger(version.sizeBytes) &&
    version.sizeBytes >= 0 &&
    version.sizeBytes <= maximumFileBytes &&
    /^[A-F0-9]{64}$/.test(version.sha256) &&
    /^[A-F0-9]{64}$/.test(version.sourceVersion)
  )
}
export function archiveAvailability(value: string) {
  return (
    (
      {
        Present: '副本存在',
        Missing: '副本缺失',
        Incomplete: '备份未完成',
        Invalid: '副本异常',
        Purged: '已清理',
      } as Record<string, string>
    )[value] || '未知状态'
  )
}
export function archiveCaptureState(value: string) {
  return (
    (
      {
        Available: '历史版本',
        Pending: '回收结果待核实',
        Trashed: '已回收',
        Restored: '已恢复过',
        Canceled: '原操作已取消',
        Purged: '已清理',
      } as Record<string, string>
    )[value] || '未知状态'
  )
}
export function archiveDate(value: string) {
  const time = new Date(value)
  return Number.isNaN(time.valueOf()) ? '时间未知' : time.toLocaleString()
}
