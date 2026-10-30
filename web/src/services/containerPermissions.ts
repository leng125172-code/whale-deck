import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerPermissionCommand {
  location: ContainerUploadLocation
  mode: string
  expectedVersion: string
}
export interface ContainerPermissionIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerPermissionCommand
  state: string
  kind: 'File' | 'Directory' | null
  originalMode: string | null
  noChange: boolean | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  publishedVersion: string | null
  modeConfirmed: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
const hash = /^[A-F0-9]{64}$/
const states = [
  'Queued',
  'Planning',
  'Ready',
  'Pending',
  'Applying',
  'Unknown',
  'Succeeded',
  'Superseded',
  'Canceled',
  'Failed',
  'Expired',
  'Acknowledged',
]
export function permissionTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function permissionState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验对象和HDD元数据',
        Ready: '预检已完成，尚未修改权限',
        Pending: '等待权限修改',
        Applying: '正在修改权限',
        Unknown: '结果未知，保护已保留',
        Succeeded: '权限修改已完成',
        Superseded: '曾完成权限修改，当前对象已变化',
        Canceled: '已取消',
        Failed: '权限修改未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置未知历史，不代表权限修改成功',
      } as Record<string, string>
    )[state] ?? '无法核实的权限修改状态'
  )
}
export function permissionResult(code: string | null) {
  return (
    (
      {
        CONTAINER_PERMISSION_PLAN_ACCEPTED: '预检任务已登记，尚未修改权限。',
        CONTAINER_PERMISSION_READY: '预检已完成，请核对原权限与目标权限后确认。',
        CONTAINER_PERMISSION_ACCEPTED: '确认已登记，等待执行。',
        CONTAINER_PERMISSION_COMPLETED: '普通权限修改已完成，HDD元数据已验证留存。',
        CONTAINER_PERMISSION_CANCELED: '任务已取消。',
        CONTAINER_PERMISSION_PLAN_EXPIRED: '预检计划已过期，请重新选择对象并预检。',
        CONTAINER_PERMISSION_VERSION_CONFLICT: '对象在预检后发生变化，请重新读取后预检。',
        CONTAINER_PERMISSION_SOURCE_UNSUPPORTED: '该对象类型、链接或特殊权限暂不支持此操作。',
        CONTAINER_PERMISSION_EXTENDED_METADATA_REQUIRED:
          '该对象带有ACL或安全属性，普通权限入口无法安全修改。',
        CONTAINER_PERMISSION_PROTECTED: '该对象受保护或位于只读区域，无法修改权限。',
        CONTAINER_PERMISSION_TARGET_CHANGED: '原容器实例或对象已变化，请核实原任务。',
        CONTAINER_PERMISSION_ATTRIBUTES_LIMIT: '对象属性超出本入口的安全检查范围。',
        CONTAINER_PERMISSION_RESULT_UNKNOWN: '无法确认最终结果，保护已保留，请由管理员核实。',
        CONTAINER_PERMISSION_UNKNOWN_ACKNOWLEDGED: '已确认历史不确定性并释放保护，不代表修改成功。',
        CONTAINER_PERMISSION_INTERRUPTED: '执行被中断，请核对原任务后再决定下一步。',
      } as Record<string, string>
    )[code ?? ''] ?? '操作结果已记录，可查看任务诊断代码。'
  )
}
export function validPermissionCommand(value: ContainerPermissionCommand) {
  return (
    !!value &&
    Object.keys(value).length === 3 &&
    validContainerUploadLocation(value.location) &&
    typeof value.mode === 'string' &&
    /^0[0-7]{3}$/.test(value.mode) &&
    typeof value.expectedVersion === 'string' &&
    hash.test(value.expectedVersion)
  )
}
export function samePermissionCommand(
  a: ContainerPermissionCommand,
  b: ContainerPermissionCommand,
) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.mode === b.mode &&
    a.expectedVersion === b.expectedVersion
  )
}
function date(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
}
export function assertPermissionIntent(
  value: ContainerPermissionIntent,
  containerId?: string,
  id?: string,
  command?: ContainerPermissionCommand,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    !validPermissionCommand(value.command) ||
    (containerId && value.command.location.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !samePermissionCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.modeConfirmed !== null && typeof value.modeConfirmed !== 'boolean') ||
    typeof value.historyMayExist !== 'boolean' ||
    typeof value.historyVerified !== 'boolean' ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    ((permissionTerminal(value.state) || value.state === 'Unknown') && value.resultCode === null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_PERMISSION_[A-Z_]+$/.test(value.resultCode)))
  )
    throw new Error('权限修改任务的身份或状态无法核实')
  const planned = value.kind !== null
  if (
    planned
      ? !['File', 'Directory'].includes(value.kind!) ||
        !date(value.readyExpiresAtUtc) ||
        typeof value.originalMode !== 'string' ||
        !/^0[0-7]{3}$/.test(value.originalMode) ||
        typeof value.noChange !== 'boolean' ||
        value.noChange !== (value.originalMode === value.command.mode)
      : value.originalMode !== null || value.noChange !== null || value.readyExpiresAtUtc !== null
  )
    throw new Error('权限预检摘要无法核实')
  if (
    (['Queued', 'Planning'].includes(value.state) && (planned || value.executionJobId !== null)) ||
    ([
      'Ready',
      'Pending',
      'Applying',
      'Unknown',
      'Succeeded',
      'Superseded',
      'Acknowledged',
    ].includes(value.state) &&
      !planned) ||
    (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(
      value.state,
    ) &&
      value.executionJobId === null) ||
    (['Ready', 'Expired'].includes(value.state) && value.executionJobId !== null) ||
    (value.historyVerified
      ? !value.historyMayExist ||
        typeof value.historyDigest !== 'string' ||
        !hash.test(value.historyDigest)
      : value.historyDigest !== null) ||
    (value.modeConfirmed === true && !value.historyVerified) ||
    (value.executionJobId === null &&
      (value.modeConfirmed !== null ||
        value.historyMayExist ||
        value.historyVerified ||
        value.historyDigest !== null)) ||
    (value.executionJobId !== null &&
      ['Failed', 'Canceled'].includes(value.state) &&
      value.modeConfirmed !== false) ||
    (['Succeeded', 'Superseded'].includes(value.state)
      ? typeof value.publishedVersion !== 'string' ||
        !hash.test(value.publishedVersion) ||
        value.modeConfirmed !== true ||
        !value.historyVerified
      : value.publishedVersion !== null)
  )
    throw new Error('权限结果或HDD历史事实无法核实')
  const confirmation = value.confirmation
  if (
    value.state === 'Ready'
      ? !confirmation ||
        Object.keys(confirmation).length !== 3 ||
        typeof confirmation.planHash !== 'string' ||
        !confirmation.planHash ||
        confirmation.planHash.length > 4096 ||
        !date(confirmation.expiresAtUtc) ||
        confirmation.expiresAtUtc !== value.readyExpiresAtUtc ||
        typeof confirmation.warning !== 'string' ||
        confirmation.warning.length > 2048
      : confirmation !== null
  )
    throw new Error('权限修改确认计划无法核实')
  return value
}
export function assertPermissionAdvance(
  previous: ContainerPermissionIntent | null,
  value: ContainerPermissionIntent,
) {
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const snapshot = (v: ContainerPermissionIntent) =>
    JSON.stringify([
      v.planningJobId,
      v.executionJobId,
      virtualExpiry ? null : v.state,
      v.kind,
      v.originalMode,
      v.noChange,
      v.readyExpiresAtUtc,
      v.resultCode,
      v.publishedVersion,
      v.modeConfirmed,
      v.historyMayExist,
      v.historyVerified,
      v.historyDigest,
      virtualExpiry ? null : v.confirmation?.planHash,
      virtualExpiry ? null : v.confirmation?.expiresAtUtc,
      virtualExpiry ? null : v.confirmation?.warning,
    ])
  if (
    value.id !== previous.id ||
    !samePermissionCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (permissionTerminal(previous.state) && value.state !== previous.state) ||
    (previous.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(value.state)) ||
    (previous.state === 'Applying' && value.state === 'Pending') ||
    (previous.executionJobId !== null && value.executionJobId !== previous.executionJobId) ||
    (previous.kind !== null &&
      (value.kind !== previous.kind ||
        value.originalMode !== previous.originalMode ||
        value.noChange !== previous.noChange ||
        value.readyExpiresAtUtc !== previous.readyExpiresAtUtc)) ||
    (previous.modeConfirmed !== null && value.modeConfirmed !== previous.modeConfirmed) ||
    (previous.historyMayExist && !value.historyMayExist) ||
    (previous.historyVerified && !value.historyVerified) ||
    (previous.historyDigest !== null && value.historyDigest !== previous.historyDigest) ||
    (value.version === previous.version && snapshot(value) !== snapshot(previous))
  )
    throw new Error('权限修改任务返回了旧状态或不同的原计划')
}
export function canPermissionContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    ['File', 'Directory'].includes(entry.kind) &&
    /^0[0-7]{3}$/.test(entry.mode) &&
    hash.test(entry.version)
  )
}
export async function queuePermission(
  command: ContainerPermissionCommand,
  key: string,
  signal: AbortSignal,
) {
  if (!validPermissionCommand(command) || !validDirectoryId(key))
    throw new Error('权限对象、普通模式或登记键无效')
  const value = await apiRequest<ContainerPermissionIntent>(
    `${containerFileBase(command.location.containerId)}/permissions`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.location.instanceId,
        path: command.location.path,
        mode: command.mode,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  return assertPermissionIntent(value, command.location.containerId, undefined, command)
}
export async function getPermission(
  containerId: string,
  id: string,
  signal: AbortSignal,
  byKey = false,
) {
  if (!validDirectoryId(id)) throw new Error('权限修改任务引用无效')
  return assertPermissionIntent(
    await apiRequest<ContainerPermissionIntent>(
      `${containerFileBase(containerId)}/permissions/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmPermission(row: ContainerPermissionIntent, signal: AbortSignal) {
  assertPermissionIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('权限修改计划已过期或尚未核实')
  const value = await apiRequest<ContainerPermissionIntent>(
    `${containerFileBase(row.command.location.containerId)}/permissions/${row.id}/confirm`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': row.id },
      signal,
      body: JSON.stringify({
        planHash: row.confirmation.planHash,
        confirmed: true,
        version: row.version,
      }),
    },
  )
  return assertPermissionIntent(value, row.command.location.containerId, row.id, row.command)
}
export async function cancelPermission(row: ContainerPermissionIntent, signal: AbortSignal) {
  assertPermissionIntent(row)
  return assertPermissionIntent(
    await apiRequest<ContainerPermissionIntent>(
      `${containerFileBase(row.command.location.containerId)}/permissions/${row.id}/cancel`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        signal,
      },
    ),
    row.command.location.containerId,
    row.id,
    row.command,
  )
}
export const permissionRecoveryBase = 'files/container-permissions/recovery'
// The live target is used only to refuse confirming a stale instance, never to rewrite an old command.
export function currentPermissionInstance(
  row: ContainerPermissionIntent,
  target: ContainerFileTarget,
) {
  return (
    row.command.location.containerId === target.containerId &&
    row.command.location.instanceId === target.instanceId
  )
}
