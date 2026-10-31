import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerOwnershipCommand {
  location: ContainerUploadLocation
  uid: number
  gid: number
  expectedVersion: string
}
export interface ContainerOwnershipIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerOwnershipCommand
  state: string
  kind: 'File' | 'Directory' | null
  originalUid: number | null
  originalGid: number | null
  executorUid: number | null
  executorGid: number | null
  noChange: boolean | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  publishedVersion: string | null
  ownerConfirmed: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
export function validOwnershipId(value: unknown): value is number {
  return (
    typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value < 4294967295
  )
}
export function parseOwnershipId(value: string): number {
  if (!/^(0|[1-9][0-9]{0,9})$/.test(value)) return Number.NaN
  const id = Number(value)
  return validOwnershipId(id) ? id : Number.NaN
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
export function ownershipTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function ownershipState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验对象和HDD元数据',
        Ready: '预检已完成，尚未修改所有者',
        Pending: '等待所有者修改',
        Applying: '正在修改所有者',
        Unknown: '结果未知，保护已保留',
        Succeeded: '所有者修改已完成',
        Superseded: '曾完成所有者修改，当前对象已变化',
        Canceled: '已取消',
        Failed: '所有者修改未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置未知历史，不代表所有者修改成功',
      } as Record<string, string>
    )[state] ?? '无法核实的所有者修改状态'
  )
}
export function ownershipResult(code: string | null) {
  return (
    (
      {
        CONTAINER_OWNERSHIP_PLAN_ACCEPTED: '预检任务已登记，尚未修改所有者。',
        CONTAINER_OWNERSHIP_READY: '预检已完成，请核对原所有者与目标所有者后确认。',
        CONTAINER_OWNERSHIP_ACCEPTED: '确认已登记，等待执行。',
        CONTAINER_OWNERSHIP_COMPLETED: '数字所有者修改已完成，HDD元数据已验证留存。',
        CONTAINER_OWNERSHIP_CANCELED: '任务已取消。',
        CONTAINER_OWNERSHIP_PLAN_EXPIRED: '预检计划已过期，请重新选择对象并预检。',
        CONTAINER_OWNERSHIP_VERSION_CONFLICT: '对象在预检后发生变化，请重新读取后预检。',
        CONTAINER_OWNERSHIP_SOURCE_UNSUPPORTED: '该对象类型、链接或特殊所有者暂不支持此操作。',
        CONTAINER_OWNERSHIP_EXTENDED_METADATA_REQUIRED:
          '该对象带有ACL或安全属性，当前所有者入口无法安全修改。',
        CONTAINER_OWNERSHIP_PROTECTED: '该对象受保护或位于只读区域，无法修改所有者。',
        CONTAINER_OWNERSHIP_TARGET_CHANGED: '原容器实例或对象已变化，请核实原任务。',
        CONTAINER_OWNERSHIP_ATTRIBUTES_LIMIT: '对象属性超出本入口的安全检查范围。',
        CONTAINER_OWNERSHIP_RESULT_UNKNOWN: '无法确认最终结果，保护已保留，请由管理员核实。',
        CONTAINER_OWNERSHIP_UNKNOWN_ACKNOWLEDGED: '已确认历史不确定性并释放保护，不代表修改成功。',
        CONTAINER_OWNERSHIP_INTERRUPTED: '执行被中断，请核对原任务后再决定下一步。',
      } as Record<string, string>
    )[code ?? ''] ?? '操作结果已记录，可查看任务诊断代码。'
  )
}
export function validOwnershipCommand(value: ContainerOwnershipCommand) {
  return (
    !!value &&
    Object.keys(value).length === 4 &&
    validContainerUploadLocation(value.location) &&
    validOwnershipId(value.uid) &&
    validOwnershipId(value.gid) &&
    typeof value.expectedVersion === 'string' &&
    hash.test(value.expectedVersion)
  )
}
export function sameOwnershipCommand(a: ContainerOwnershipCommand, b: ContainerOwnershipCommand) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.uid === b.uid &&
    a.gid === b.gid &&
    a.expectedVersion === b.expectedVersion
  )
}
function date(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
}
export function assertOwnershipIntent(
  value: ContainerOwnershipIntent,
  containerId?: string,
  id?: string,
  command?: ContainerOwnershipCommand,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    !validOwnershipCommand(value.command) ||
    (containerId && value.command.location.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !sameOwnershipCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.ownerConfirmed !== null && typeof value.ownerConfirmed !== 'boolean') ||
    typeof value.historyMayExist !== 'boolean' ||
    typeof value.historyVerified !== 'boolean' ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    ((ownershipTerminal(value.state) || value.state === 'Unknown') && value.resultCode === null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_OWNERSHIP_[A-Z_]+$/.test(value.resultCode)))
  )
    throw new Error('所有者修改任务的身份或状态无法核实')
  const planned = value.kind !== null
  if (
    planned
      ? !['File', 'Directory'].includes(value.kind!) ||
        !date(value.readyExpiresAtUtc) ||
        !validOwnershipId(value.originalUid) ||
        !validOwnershipId(value.originalGid) ||
        !validOwnershipId(value.executorUid) ||
        !validOwnershipId(value.executorGid) ||
        typeof value.noChange !== 'boolean' ||
        value.noChange !==
          (value.originalUid === value.command.uid && value.originalGid === value.command.gid)
      : value.originalUid !== null ||
        value.originalGid !== null ||
        value.executorUid !== null ||
        value.executorGid !== null ||
        value.noChange !== null ||
        value.readyExpiresAtUtc !== null
  )
    throw new Error('所有者预检摘要无法核实')
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
    (value.ownerConfirmed === true && !value.historyVerified) ||
    (value.executionJobId === null &&
      (value.ownerConfirmed !== null ||
        value.historyMayExist ||
        value.historyVerified ||
        value.historyDigest !== null)) ||
    (value.executionJobId !== null &&
      ['Failed', 'Canceled'].includes(value.state) &&
      value.ownerConfirmed !== false) ||
    (['Succeeded', 'Superseded'].includes(value.state)
      ? typeof value.publishedVersion !== 'string' ||
        !hash.test(value.publishedVersion) ||
        value.ownerConfirmed !== true ||
        !value.historyVerified
      : value.publishedVersion !== null)
  )
    throw new Error('所有者结果或HDD历史事实无法核实')
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
    throw new Error('所有者修改确认计划无法核实')
  return value
}
export function assertOwnershipAdvance(
  previous: ContainerOwnershipIntent | null,
  value: ContainerOwnershipIntent,
) {
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const snapshot = (v: ContainerOwnershipIntent) =>
    JSON.stringify([
      v.planningJobId,
      v.executionJobId,
      virtualExpiry ? null : v.state,
      v.kind,
      v.originalUid,
      v.originalGid,
      v.executorUid,
      v.executorGid,
      v.noChange,
      v.readyExpiresAtUtc,
      v.resultCode,
      v.publishedVersion,
      v.ownerConfirmed,
      v.historyMayExist,
      v.historyVerified,
      v.historyDigest,
      virtualExpiry ? null : v.confirmation?.planHash,
      virtualExpiry ? null : v.confirmation?.expiresAtUtc,
      virtualExpiry ? null : v.confirmation?.warning,
    ])
  if (
    value.id !== previous.id ||
    !sameOwnershipCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (ownershipTerminal(previous.state) && value.state !== previous.state) ||
    (previous.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(value.state)) ||
    (previous.state === 'Applying' && value.state === 'Pending') ||
    (previous.state === 'Ready' &&
      value.state === 'Ready' &&
      (value.confirmation?.planHash !== previous.confirmation?.planHash ||
        value.confirmation?.expiresAtUtc !== previous.confirmation?.expiresAtUtc ||
        value.confirmation?.warning !== previous.confirmation?.warning)) ||
    (previous.executionJobId !== null && value.executionJobId !== previous.executionJobId) ||
    (previous.kind !== null &&
      (value.kind !== previous.kind ||
        value.originalUid !== previous.originalUid ||
        value.originalGid !== previous.originalGid ||
        value.executorUid !== previous.executorUid ||
        value.executorGid !== previous.executorGid ||
        value.noChange !== previous.noChange ||
        value.readyExpiresAtUtc !== previous.readyExpiresAtUtc)) ||
    (previous.ownerConfirmed !== null && value.ownerConfirmed !== previous.ownerConfirmed) ||
    (previous.historyMayExist && !value.historyMayExist) ||
    (previous.historyVerified && !value.historyVerified) ||
    (previous.historyDigest !== null && value.historyDigest !== previous.historyDigest) ||
    (value.version === previous.version && snapshot(value) !== snapshot(previous))
  )
    throw new Error('所有者修改任务返回了旧状态或不同的原计划')
}
export function canOwnershipContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    ['File', 'Directory'].includes(entry.kind) &&
    /^0[0-7]{3}$/.test(entry.mode) &&
    hash.test(entry.version)
  )
}
export async function queueOwnership(
  command: ContainerOwnershipCommand,
  key: string,
  signal: AbortSignal,
) {
  if (!validOwnershipCommand(command) || !validDirectoryId(key))
    throw new Error('所有者对象、数字UID/GID或登记键无效')
  const value = await apiRequest<ContainerOwnershipIntent>(
    `${containerFileBase(command.location.containerId)}/ownerships`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.location.instanceId,
        path: command.location.path,
        uid: command.uid,
        gid: command.gid,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  return assertOwnershipIntent(value, command.location.containerId, undefined, command)
}
export async function getOwnership(
  containerId: string,
  id: string,
  signal: AbortSignal,
  byKey = false,
) {
  if (!validDirectoryId(id)) throw new Error('所有者修改任务引用无效')
  return assertOwnershipIntent(
    await apiRequest<ContainerOwnershipIntent>(
      `${containerFileBase(containerId)}/ownerships/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmOwnership(row: ContainerOwnershipIntent, signal: AbortSignal) {
  assertOwnershipIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('所有者修改计划已过期或尚未核实')
  const value = await apiRequest<ContainerOwnershipIntent>(
    `${containerFileBase(row.command.location.containerId)}/ownerships/${row.id}/confirm`,
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
  return assertOwnershipIntent(value, row.command.location.containerId, row.id, row.command)
}
export async function cancelOwnership(row: ContainerOwnershipIntent, signal: AbortSignal) {
  assertOwnershipIntent(row)
  return assertOwnershipIntent(
    await apiRequest<ContainerOwnershipIntent>(
      `${containerFileBase(row.command.location.containerId)}/ownerships/${row.id}/cancel`,
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
export const ownershipRecoveryBase = 'files/container-ownerships/recovery'
// The live target is used only to refuse confirming a stale instance, never to rewrite an old command.
export function currentOwnershipInstance(
  row: ContainerOwnershipIntent,
  target: ContainerFileTarget,
) {
  return (
    row.command.location.containerId === target.containerId &&
    row.command.location.instanceId === target.instanceId
  )
}
