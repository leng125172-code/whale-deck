import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerAclCommand {
  location: ContainerUploadLocation
  aclType: 'access' | 'default'
  entries: ContainerAclEntry[]
  expectedVersion: string
}
export interface ContainerAclIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerAclCommand
  state: string
  kind: 'File' | 'Directory' | null
  originalUid: number | null
  originalGid: number | null
  executorUid: number | null
  executorGid: number | null
  originalAccessAcl: ContainerAclEntry[] | null
  originalDefaultAcl: ContainerAclEntry[] | null
  targetMode: number | null
  receipt: ContainerAclReceipt | null
  noChange: boolean | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  publishedVersion: string | null
  aclConfirmed: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
export type AclTag = 'user_obj' | 'user' | 'group_obj' | 'group' | 'mask' | 'other'
export interface ContainerAclEntry {
  tag: AclTag
  id: number | null
  permissions: number
}
export interface ContainerAclReceipt {
  id: string
  containerId: string
  instanceId: string
  fingerprint: string
  targetProof: string
  state: 'Completed' | 'Aborted' | 'Unknown'
  version: string
  bindingCurrent: boolean
  replayed: boolean
  kind: 'File' | 'Directory'
  aclConfirmed: boolean | null
  noChange: boolean
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string
  aclType: 'access' | 'default'
  targetMode: number
}
export function validAclId(value: unknown): value is number {
  return (
    typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value < 4294967295
  )
}
export function parseAclId(value: string): number {
  if (!/^(0|[1-9][0-9]{0,9})$/.test(value)) return Number.NaN
  const id = Number(value)
  return validAclId(id) ? id : Number.NaN
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
export function aclTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function aclState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验对象和HDD元数据',
        Ready: '预检已完成，尚未修改ACL',
        Pending: '等待ACL修改',
        Applying: '正在修改ACL',
        Unknown: '结果未知，保护已保留',
        Succeeded: 'ACL修改已完成',
        Superseded: '曾完成ACL修改，当前对象已变化',
        Canceled: '已取消',
        Failed: 'ACL修改未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置未知历史，不代表ACL修改成功',
      } as Record<string, string>
    )[state] ?? '无法核实的ACL修改状态'
  )
}
export function aclResult(code: string | null) {
  return (
    (
      {
        CONTAINER_ACL_PLAN_ACCEPTED: '预检任务已登记，尚未修改ACL。',
        CONTAINER_ACL_READY: '预检已完成，请核对原ACL与目标ACL后确认。',
        CONTAINER_ACL_ACCEPTED: '确认已登记，等待执行。',
        CONTAINER_ACL_COMPLETED: 'ACL修改已完成，HDD元数据已验证留存。',
        CONTAINER_ACL_CANCELED: '任务已取消。',
        CONTAINER_ACL_PLAN_EXPIRED: '预检计划已过期，请重新选择对象并预检。',
        CONTAINER_ACL_VERSION_CONFLICT: '对象版本已变化，请重新读取后预检。',
        CONTAINER_ACL_SOURCE_UNSUPPORTED: '该对象类型、链接或特殊ACL暂不支持此操作。',
        CONTAINER_ACL_EXTENDED_METADATA_REQUIRED: '该对象带有不支持的安全属性，无法安全修改ACL。',
        CONTAINER_ACL_UNSUPPORTED: '文件系统或内核不支持ACL；不能当空ACL处理。',
        CONTAINER_ACL_MAPPING_UNPROVEN: '容器身份命名空间或挂载映射尚未安全证明，已拒绝写入。',
        CONTAINER_ACL_DEFAULT_REQUIRES_DIRECTORY: 'default ACL仅支持目录。',
        CONTAINER_ACL_PROTECTED: '该对象受保护或位于只读区域，无法修改ACL。',
        CONTAINER_ACL_TARGET_CHANGED: '原容器实例或对象已变化，请核实原任务。',
        CONTAINER_ACL_ATTRIBUTES_LIMIT: '对象属性超出本入口的安全检查范围。',
        CONTAINER_ACL_RESULT_UNKNOWN: '无法确认最终结果，保护已保留，请由管理员核实。',
        CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED: '已确认历史不确定性并释放保护，不代表修改成功。',
        CONTAINER_ACL_INTERRUPTED: '执行被中断，请核对原任务后再决定下一步。',
      } as Record<string, string>
    )[code ?? ''] ?? '操作结果已记录，可查看任务诊断代码。'
  )
}
const ranks: Record<AclTag, number> = {
  user_obj: 1,
  user: 2,
  group_obj: 4,
  group: 8,
  mask: 16,
  other: 32,
}
export function validAclEntries(value: unknown, empty = false): value is ContainerAclEntry[] {
  if (!Array.isArray(value) || value.length > 64 || (!value.length && !empty)) return false
  if (!value.length) return true
  let prior = [0, -1]
  const seen = new Set<string>()
  let named = false
  for (const item of value) {
    if (
      !item ||
      Object.keys(item).length !== 3 ||
      !Object.prototype.hasOwnProperty.call(ranks, item.tag) ||
      !Number.isInteger(item.permissions) ||
      item.permissions < 0 ||
      item.permissions > 7
    )
      return false
    const rank = ranks[item.tag as AclTag]
    const isNamed = item.tag === 'user' || item.tag === 'group'
    if (isNamed ? !validAclId(item.id) : item.id !== null) return false
    const id = isNamed ? item.id : -1
    if (rank < prior[0]! || (rank === prior[0] && id <= prior[1]!)) return false
    prior = [rank, id]
    seen.add(item.tag)
    named ||= isNamed
  }
  return (
    ['user_obj', 'group_obj', 'other'].every((v) => seen.has(v)) && (!named || seen.has('mask'))
  )
}
export function aclMode(entries: ContainerAclEntry[]) {
  if (!validAclEntries(entries)) return Number.NaN
  return (
    entries.find((v) => v.tag === 'user_obj')!.permissions * 64 +
    (entries.find((v) => v.tag === 'mask') ?? entries.find((v) => v.tag === 'group_obj'))!
      .permissions *
      8 +
    entries.find((v) => v.tag === 'other')!.permissions
  )
}
export function aclBits(value: number) {
  return [value & 4 ? 'r' : '-', value & 2 ? 'w' : '-', value & 1 ? 'x' : '-'].join('')
}
export function aclModeLabel(value: number | null) {
  return value === null ? '待预检' : '0' + value.toString(8).padStart(3, '0')
}
export function aclEntryLabel(entry: ContainerAclEntry) {
  return (
    {
      user_obj: '文件属主',
      user: '指定用户',
      group_obj: '文件属组',
      group: '指定组',
      mask: 'mask',
      other: '其他用户',
    }[entry.tag] + (entry.id === null ? '' : ` ${entry.id}`)
  )
}
export function aclSummary(entries: ContainerAclEntry[] | null) {
  return entries === null
    ? '待预检'
    : entries.length
      ? entries.map((e) => `${aclEntryLabel(e)} ${aclBits(e.permissions)}`).join('；')
      : '空（无默认ACL）'
}
export function sameAclEntries(a: ContainerAclEntry[], b: ContainerAclEntry[]) {
  return (
    a.length === b.length &&
    a.every(
      (e, i) => e.tag === b[i]!.tag && e.id === b[i]!.id && e.permissions === b[i]!.permissions,
    )
  )
}
export function validAclCommand(value: ContainerAclCommand) {
  return (
    !!value &&
    Object.keys(value).length === 4 &&
    validContainerUploadLocation(value.location) &&
    ['access', 'default'].includes(value.aclType) &&
    validAclEntries(value.entries, value.aclType === 'default') &&
    typeof value.expectedVersion === 'string' &&
    hash.test(value.expectedVersion)
  )
}
export function sameAclCommand(a: ContainerAclCommand, b: ContainerAclCommand) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.aclType === b.aclType &&
    sameAclEntries(a.entries, b.entries) &&
    a.expectedVersion === b.expectedVersion
  )
}
function date(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
}
export function assertAclIntent(
  value: ContainerAclIntent,
  containerId?: string,
  id?: string,
  command?: ContainerAclCommand,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    !validAclCommand(value.command) ||
    (containerId && value.command.location.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !sameAclCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.aclConfirmed !== null && typeof value.aclConfirmed !== 'boolean') ||
    typeof value.historyMayExist !== 'boolean' ||
    typeof value.historyVerified !== 'boolean' ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    ((aclTerminal(value.state) || value.state === 'Unknown') && value.resultCode === null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_ACL_[A-Z_]+$/.test(value.resultCode)))
  )
    throw new Error('ACL修改任务的身份或状态无法核实')
  const planned = value.kind !== null
  if (
    planned
      ? !['File', 'Directory'].includes(value.kind!) ||
        !date(value.readyExpiresAtUtc) ||
        !validAclId(value.originalUid) ||
        !validAclId(value.originalGid) ||
        !validAclId(value.executorUid) ||
        !validAclId(value.executorGid) ||
        typeof value.noChange !== 'boolean' ||
        !validAclEntries(value.originalAccessAcl) ||
        !validAclEntries(value.originalDefaultAcl, true) ||
        (value.kind === 'File' &&
          (value.command.aclType === 'default' || value.originalDefaultAcl.length !== 0)) ||
        value.noChange !==
          sameAclEntries(
            value.command.entries,
            value.command.aclType === 'access' ? value.originalAccessAcl : value.originalDefaultAcl,
          ) ||
        value.targetMode !==
          (value.command.aclType === 'access'
            ? aclMode(value.command.entries)
            : aclMode(value.originalAccessAcl))
      : value.originalUid !== null ||
        value.originalGid !== null ||
        value.executorUid !== null ||
        value.executorGid !== null ||
        value.originalAccessAcl !== null ||
        value.originalDefaultAcl !== null ||
        value.targetMode !== null ||
        value.noChange !== null ||
        value.readyExpiresAtUtc !== null
  )
    throw new Error('ACL预检摘要无法核实')
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
    (value.aclConfirmed === true && !value.historyVerified) ||
    (value.executionJobId === null &&
      (value.aclConfirmed !== null ||
        value.historyMayExist ||
        value.historyVerified ||
        value.historyDigest !== null)) ||
    (value.executionJobId !== null &&
      ['Failed', 'Canceled'].includes(value.state) &&
      value.aclConfirmed !== false) ||
    (['Succeeded', 'Superseded'].includes(value.state)
      ? typeof value.publishedVersion !== 'string' ||
        !hash.test(value.publishedVersion) ||
        value.aclConfirmed !== true ||
        !value.historyVerified
      : value.state !== 'Acknowledged' && value.publishedVersion !== null)
  )
    throw new Error('ACL结果或HDD历史事实无法核实')
  const receipt = value.receipt
  if (receipt === null) {
    if (
      value.aclConfirmed !== null ||
      value.historyVerified ||
      value.historyDigest !== null ||
      value.publishedVersion !== null ||
      ['Succeeded', 'Superseded', 'Acknowledged'].includes(value.state) ||
      (value.executionJobId !== null && ['Canceled', 'Failed'].includes(value.state))
    )
      throw new Error('ACL原回执缺失或事实不一致')
  } else {
    if (
      !receipt ||
      receipt.id !== value.executionJobId ||
      receipt.containerId !== value.command.location.containerId ||
      receipt.instanceId !== value.command.location.instanceId ||
      !hash.test(receipt.fingerprint) ||
      !hash.test(receipt.targetProof) ||
      receipt.kind !== value.kind ||
      receipt.aclType !== value.command.aclType ||
      receipt.targetMode !== value.targetMode ||
      receipt.noChange !== value.noChange ||
      !['Completed', 'Aborted', 'Unknown'].includes(receipt.state) ||
      typeof receipt.bindingCurrent !== 'boolean' ||
      typeof receipt.replayed !== 'boolean' ||
      typeof receipt.historyMayExist !== 'boolean' ||
      typeof receipt.historyVerified !== 'boolean' ||
      receipt.aclConfirmed !== value.aclConfirmed ||
      (receipt.historyMayExist && !value.historyMayExist) ||
      receipt.historyVerified !== value.historyVerified ||
      receipt.historyDigest !== (value.historyDigest ?? '') ||
      (receipt.state === 'Completed'
        ? receipt.aclConfirmed !== true ||
          !receipt.historyVerified ||
          !hash.test(receipt.version) ||
          receipt.version !== value.publishedVersion
        : receipt.aclConfirmed !== (receipt.state === 'Aborted' ? false : null) ||
          receipt.version !== '' ||
          receipt.bindingCurrent ||
          value.publishedVersion !== null) ||
      (value.state === 'Succeeded' && (receipt.state !== 'Completed' || !receipt.bindingCurrent)) ||
      (value.state === 'Superseded' && (receipt.state !== 'Completed' || receipt.bindingCurrent)) ||
      (value.state === 'Unknown' && receipt.state !== 'Unknown') ||
      (['Canceled', 'Failed'].includes(value.state) && receipt.state !== 'Aborted') ||
      !['Succeeded', 'Superseded', 'Unknown', 'Acknowledged', 'Canceled', 'Failed'].includes(
        value.state,
      )
    )
      throw new Error('ACL原回执或历史事实无法核实')
  }
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
    throw new Error('ACL修改确认计划无法核实')
  return value
}
export function assertAclAdvance(previous: ContainerAclIntent | null, value: ContainerAclIntent) {
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const snapshot = (v: ContainerAclIntent) =>
    JSON.stringify([
      v.planningJobId,
      v.executionJobId,
      virtualExpiry ? null : v.state,
      v.kind,
      v.originalUid,
      v.originalGid,
      v.executorUid,
      v.executorGid,
      v.originalAccessAcl,
      v.originalDefaultAcl,
      v.targetMode,
      v.receipt,
      v.noChange,
      v.readyExpiresAtUtc,
      v.resultCode,
      v.publishedVersion,
      v.aclConfirmed,
      v.historyMayExist,
      v.historyVerified,
      v.historyDigest,
      virtualExpiry ? null : v.confirmation?.planHash,
      virtualExpiry ? null : v.confirmation?.expiresAtUtc,
      virtualExpiry ? null : v.confirmation?.warning,
    ])
  if (
    value.id !== previous.id ||
    !sameAclCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (aclTerminal(previous.state) && value.state !== previous.state) ||
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
        JSON.stringify(value.originalAccessAcl) !== JSON.stringify(previous.originalAccessAcl) ||
        JSON.stringify(value.originalDefaultAcl) !== JSON.stringify(previous.originalDefaultAcl) ||
        value.targetMode !== previous.targetMode ||
        value.noChange !== previous.noChange ||
        value.readyExpiresAtUtc !== previous.readyExpiresAtUtc)) ||
    (previous.receipt !== null &&
      JSON.stringify(value.receipt) !== JSON.stringify(previous.receipt)) ||
    (previous.aclConfirmed !== null && value.aclConfirmed !== previous.aclConfirmed) ||
    (previous.historyMayExist && !value.historyMayExist) ||
    (previous.historyVerified && !value.historyVerified) ||
    (previous.historyDigest !== null && value.historyDigest !== previous.historyDigest) ||
    (value.version === previous.version && snapshot(value) !== snapshot(previous))
  )
    throw new Error('ACL修改任务返回了旧状态或不同的原计划')
}
export function canAclContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    ['File', 'Directory'].includes(entry.kind) &&
    /^0[0-7]{3}$/.test(entry.mode) &&
    hash.test(entry.version)
  )
}
export async function queueAcl(command: ContainerAclCommand, key: string, signal: AbortSignal) {
  if (!validAclCommand(command) || !validDirectoryId(key))
    throw new Error('ACL对象、ACL条目或登记键无效')
  const value = await apiRequest<ContainerAclIntent>(
    `${containerFileBase(command.location.containerId)}/acls`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.location.instanceId,
        path: command.location.path,
        aclType: command.aclType,
        entries: command.entries,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  assertAclIntent(value, command.location.containerId, undefined, command)
  // A registration POST cannot serve as proof of a freshly confirmed write.
  // A legitimate advanced idempotent replay is reconciled by GET of the original key.
  if (value.executionJobId !== null || value.receipt !== null)
    throw new Error('预检登记返回了执行状态，请仅查询原任务；未自动确认。')
  return value
}
export async function getAcl(containerId: string, id: string, signal: AbortSignal, byKey = false) {
  if (!validDirectoryId(id)) throw new Error('ACL修改任务引用无效')
  return assertAclIntent(
    await apiRequest<ContainerAclIntent>(
      `${containerFileBase(containerId)}/acls/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmAcl(row: ContainerAclIntent, signal: AbortSignal) {
  assertAclIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('ACL修改计划已过期或尚未核实')
  const value = await apiRequest<ContainerAclIntent>(
    `${containerFileBase(row.command.location.containerId)}/acls/${row.id}/confirm`,
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
  return assertAclIntent(value, row.command.location.containerId, row.id, row.command)
}
export async function cancelAcl(row: ContainerAclIntent, signal: AbortSignal) {
  assertAclIntent(row)
  return assertAclIntent(
    await apiRequest<ContainerAclIntent>(
      `${containerFileBase(row.command.location.containerId)}/acls/${row.id}/cancel`,
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
export const aclRecoveryBase = 'files/container-acls/recovery'
// The live target is used only to refuse confirming a stale instance, never to rewrite an old command.
export function currentAclInstance(row: ContainerAclIntent, target: ContainerFileTarget) {
  return (
    row.command.location.containerId === target.containerId &&
    row.command.location.instanceId === target.instanceId
  )
}
