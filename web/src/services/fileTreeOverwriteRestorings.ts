import type { FileEntry, FileRoot } from '@/services/fileManagement'
import { treeUuid, validTreeLocation, treeTerminal } from '@/services/fileTreeCopies'
import type { TreeCopyIntent, TreeLocation } from '@/services/fileTreeCopies'
import { assertCapturePage, captureKind } from '@/services/fileTreeRecyclings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'

export { treeUuid, treeTerminal } from '@/services/fileTreeCopies'
export interface TreeOverwriteRestoringRequest {
  captureId: string
  target: TreeLocation
  targetExpectedVersion: string
}
export interface TreeOverwriteRestoringCommand extends TreeOverwriteRestoringRequest {
  source: TreeLocation
  captureKind: 'Trash' | 'Overwrite'
}
export interface TreeOverwriteRestoringIntent extends Omit<TreeCopyIntent, 'command'> {
  command: TreeOverwriteRestoringCommand
  targetEntryCount: number | null
  targetTotalBytes: number | null
  archiveBytes: number | null
  archiveSha256: string | null
  overwrittenCaptureId: string | null
  backupMayBeRetained: boolean
  backupVerified: boolean
  originalsRetained: boolean
  targetMayHaveChanged: boolean
  backupArchiveBytes: number | null
  backupArchiveSha256: string | null
}
const hash = /^[A-F0-9]{64}$/
const uuid = (value: string) =>
  typeof value === 'string' &&
  treeUuid.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000'
const date = (value: string | null) =>
  typeof value === 'string' && Number.isFinite(Date.parse(value))
const exactKeys = (value: object, keys: string[]) =>
  !!value &&
  Object.keys(value).length === keys.length &&
  Object.keys(value).every((key) => keys.includes(key))
const requestKeys = ['captureId', 'target', 'targetExpectedVersion']
const commandKeys = [...requestKeys, 'source', 'captureKind']
const publicKeys = [
  'id',
  'planningJobId',
  'executionJobId',
  'state',
  'command',
  'entryCount',
  'totalBytes',
  'targetEntryCount',
  'targetTotalBytes',
  'readyExpiresAtUtc',
  'resultCode',
  'version',
  'publishedVersion',
  'partialRetained',
  'confirmation',
  'archiveBytes',
  'archiveSha256',
  'overwrittenCaptureId',
  'backupMayBeRetained',
  'backupVerified',
  'originalsRetained',
  'targetMayHaveChanged',
  'backupArchiveBytes',
  'backupArchiveSha256',
]
const counts = (count: number | null, bytes: number | null) =>
  Number.isSafeInteger(count) &&
  count! >= 1 &&
  count! <= 10000 &&
  Number.isSafeInteger(bytes) &&
  bytes! >= 0 &&
  bytes! <= 16 * 1024 ** 3
export function validOverwriteRequest(value: TreeOverwriteRestoringRequest) {
  return (
    !!value &&
    exactKeys(value, requestKeys) &&
    uuid(value.captureId) &&
    exactKeys(value.target, ['rootId', 'path']) &&
    validTreeLocation(value.target) &&
    hash.test(value.targetExpectedVersion)
  )
}
export function sameOverwriteRequest(
  left: TreeOverwriteRestoringRequest,
  right: TreeOverwriteRestoringRequest,
) {
  return (
    left.captureId === right.captureId &&
    left.target.rootId === right.target.rootId &&
    left.target.path === right.target.path &&
    left.targetExpectedVersion === right.targetExpectedVersion
  )
}
export function sameOverwriteCommand(
  left: TreeOverwriteRestoringCommand,
  right: TreeOverwriteRestoringCommand,
) {
  return (
    sameOverwriteRequest(left, right) &&
    left.source.rootId === right.source.rootId &&
    left.source.path === right.source.path &&
    left.captureKind === right.captureKind
  )
}
export function canOverwriteDirectory(root: FileRoot | undefined, entry: FileEntry) {
  return (
    !!root &&
    entry.kind === 'Directory' &&
    !entry.isProtected &&
    hash.test(entry.version) &&
    [
      'files-list',
      'files-download',
      'files-upload',
      'files-mutate',
      'files-archive',
      'files-history',
      'files-tree-restore',
      'files-tree-overwrite-restore',
    ].every((action) => root.actions.includes(action))
  )
}
export function canReceiveOverwrite(root: FileRoot) {
  return [
    'files-list',
    'files-download',
    'files-upload',
    'files-mutate',
    'files-archive',
    'files-history',
    'files-tree-restore',
    'files-tree-overwrite-restore',
  ].every((action) => root.actions.includes(action))
}
export function overwriteResult(code: string) {
  return (
    (
      {
        FILES_TREE_OVERWRITE_RESTORE_COMPLETED:
          '覆盖前备份曾验证，旧目标原件留存；原覆盖恢复任务已完成。',
        FILES_TREE_READY_FOR_CONFIRMATION: '历史归档与当前目标已扫描，尚未覆盖。',
        FILES_VERSION_CONFLICT: '当前目标版本已变化，原计划不会覆盖；请核实原任务。',
        FILES_RESULT_UNKNOWN: '结果未知，保留锁、备份与原件，不重复覆盖。',
        FILES_RECEIPT_UNAVAILABLE: '原回执尚未取得，只查询原任务，不重复执行。',
        FILES_CAPTURE_PROOF_CHANGED: '历史归档或覆盖前备份证明变化，不继续执行。',
        FILES_UNKNOWN_ACKNOWLEDGED: '已接受未知历史结果，不代表覆盖成功；证据保留。',
        FILES_CANCELED: '已取消或请求被取消栅栏阻止，以原回执为准。',
        FILES_PLAN_EXPIRED: '原计划已过期，不自动执行。',
      } as Record<string, string>
    )[code] || code
  )
}
export function canOverwriteCapture(root: FileRoot | undefined, capture: TreeCapture) {
  if (
    !root ||
    capture.archiveState !== 'Retained' ||
    capture.cleanupActive ||
    ![
      'files-list',
      'files-download',
      'files-history',
      'files-tree-restore',
      'files-tree-overwrite-restore',
    ].every((action) => root.actions.includes(action))
  )
    return false
  try {
    assertCapturePage({ items: [capture], next: null }, root.id)
    return true
  } catch {
    return false
  }
}
export function overwriteState(state: string) {
  return (
    (
      {
        Queued: '等待扫描',
        Planning: '正在核验历史归档与当前目标',
        Ready: '计划已生成，尚未覆盖',
        OverwriteRestoringQueued: '等待覆盖恢复',
        OverwriteRestoring: '正在备份当前目标并覆盖恢复',
        Unknown: '结果未知，保留锁、备份与证据',
        Succeeded: '原覆盖恢复任务已完成',
        Failed: '覆盖恢复未完成',
        Canceled: '已取消',
        Expired: '未确认计划已过期',
        Acknowledged: '已接受未知历史结果，不代表成功',
      } as Record<string, string>
    )[state] || '无法核实的覆盖恢复状态'
  )
}
export function assertOverwriteIntent(value: TreeOverwriteRestoringIntent, expectedId?: string) {
  const command = value?.command
  const hasPlan = value?.entryCount !== null || value?.totalBytes !== null
  const executed = [
    'OverwriteRestoringQueued',
    'OverwriteRestoring',
    'Unknown',
    'Succeeded',
    'Acknowledged',
  ].includes(value?.state)
  if (
    !value ||
    !exactKeys(value, publicKeys) ||
    !uuid(value.id) ||
    value.planningJobId !== value.id ||
    (expectedId && value.id !== expectedId) ||
    (value.executionJobId !== null &&
      (!uuid(value.executionJobId) ||
        value.executionJobId === value.id ||
        value.executionJobId === command?.captureId)) ||
    ![
      'Queued',
      'Planning',
      'Ready',
      'OverwriteRestoringQueued',
      'OverwriteRestoring',
      'Unknown',
      'Succeeded',
      'Failed',
      'Canceled',
      'Expired',
      'Acknowledged',
    ].includes(value.state) ||
    !command ||
    !exactKeys(command, commandKeys) ||
    !validOverwriteRequest({
      captureId: command.captureId,
      target: command.target,
      targetExpectedVersion: command.targetExpectedVersion,
    }) ||
    !validTreeLocation(command.source) ||
    !exactKeys(command.source, ['rootId', 'path']) ||
    !['Trash', 'Overwrite'].includes(command.captureKind) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    [
      value.partialRetained,
      value.backupMayBeRetained,
      value.backupVerified,
      value.originalsRetained,
      value.targetMayHaveChanged,
    ].some((flag) => typeof flag !== 'boolean') ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(value.resultCode))) ||
    (hasPlan
      ? !counts(value.entryCount, value.totalBytes) ||
        !counts(value.targetEntryCount, value.targetTotalBytes) ||
        !Number.isSafeInteger(value.archiveBytes) ||
        value.archiveBytes! < 1 ||
        value.archiveBytes! > 17 * 1024 ** 3 ||
        !hash.test(value.archiveSha256 || '') ||
        !date(value.readyExpiresAtUtc)
      : value.targetEntryCount !== null ||
        value.targetTotalBytes !== null ||
        value.archiveBytes !== null ||
        value.archiveSha256 !== null ||
        value.readyExpiresAtUtc !== null) ||
    ((executed || value.state === 'Ready') && !hasPlan) ||
    (value.state === 'Ready' && value.confirmation === null) ||
    (executed && !value.executionJobId) ||
    (value.backupVerified
      ? !value.executionJobId ||
        value.overwrittenCaptureId !== value.executionJobId ||
        !value.backupMayBeRetained ||
        !Number.isSafeInteger(value.backupArchiveBytes) ||
        value.backupArchiveBytes! < 1 ||
        value.backupArchiveBytes! > 17 * 1024 ** 3 ||
        !hash.test(value.backupArchiveSha256 || '')
      : value.overwrittenCaptureId !== null ||
        value.backupArchiveBytes !== null ||
        value.backupArchiveSha256 !== null) ||
    (value.targetMayHaveChanged && !value.backupVerified) ||
    (value.originalsRetained && !value.targetMayHaveChanged) ||
    (value.state === 'Succeeded'
      ? !hash.test(value.publishedVersion || '') ||
        value.partialRetained ||
        !value.backupVerified ||
        !value.originalsRetained ||
        !value.targetMayHaveChanged
      : value.publishedVersion !== null) ||
    (value.confirmation !== null &&
      (value.state !== 'Ready' ||
        value.executionJobId !== null ||
        !exactKeys(value.confirmation, ['planHash', 'expiresAtUtc', 'warning']) ||
        typeof value.confirmation.planHash !== 'string' ||
        !value.confirmation.planHash ||
        value.confirmation.planHash.length > 4096 ||
        !date(value.confirmation.expiresAtUtc) ||
        value.confirmation.expiresAtUtc !== value.readyExpiresAtUtc ||
        typeof value.confirmation.warning !== 'string'))
  )
    throw new Error('目录覆盖恢复响应或备份证明无法核实；保留原引用，不重新提交')
  return value
}
export function assertOverwriteProgress(
  previous: TreeOverwriteRestoringIntent,
  next: TreeOverwriteRestoringIntent,
) {
  assertOverwriteIntent(previous)
  assertOverwriteIntent(next, previous.id)
  if (
    !sameOverwriteCommand(previous.command, next.command) ||
    next.version < previous.version ||
    (previous.state === 'Ready' &&
      next.state === 'Ready' &&
      previous.confirmation?.planHash !== next.confirmation?.planHash) ||
    (previous.executionJobId !== null && previous.executionJobId !== next.executionJobId) ||
    (previous.entryCount !== null &&
      [
        'entryCount',
        'totalBytes',
        'targetEntryCount',
        'targetTotalBytes',
        'archiveBytes',
        'archiveSha256',
        'readyExpiresAtUtc',
      ].some(
        (key) =>
          previous[key as keyof TreeOverwriteRestoringIntent] !==
          next[key as keyof TreeOverwriteRestoringIntent],
      )) ||
    (previous.backupVerified &&
      (!next.backupVerified ||
        previous.overwrittenCaptureId !== next.overwrittenCaptureId ||
        previous.backupArchiveBytes !== next.backupArchiveBytes ||
        previous.backupArchiveSha256 !== next.backupArchiveSha256)) ||
    ['backupMayBeRetained', 'originalsRetained', 'targetMayHaveChanged'].some(
      (key) =>
        previous[key as keyof TreeOverwriteRestoringIntent] === true &&
        next[key as keyof TreeOverwriteRestoringIntent] !== true,
    ) ||
    (treeTerminal(previous.state) &&
      publicKeys.some(
        (key) =>
          key !== 'command' &&
          previous[key as keyof TreeOverwriteRestoringIntent] !==
            next[key as keyof TreeOverwriteRestoringIntent],
      ))
  )
    throw new Error('原覆盖恢复任务、冻结版本或备份证明已改变；保留引用，不确认、不执行')
  return next
}
export function captureMatchesOverwrite(capture: TreeCapture, value: TreeOverwriteRestoringIntent) {
  return (
    value.command.captureId === capture.id &&
    value.command.captureKind === captureKind(capture) &&
    value.command.source.rootId === capture.source.source.rootId &&
    value.command.source.path === capture.source.source.path &&
    (value.entryCount === null ||
      (value.entryCount === capture.entryCount &&
        value.totalBytes === capture.totalBytes &&
        value.archiveBytes === capture.archiveBytes &&
        value.archiveSha256 === capture.archiveSha256))
  )
}
