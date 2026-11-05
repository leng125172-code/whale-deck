import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerRecycleCommand {
  location: ContainerUploadLocation
  expectedVersion: string
}
export interface ContainerRecycleIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerRecycleCommand
  state: string
  originalMode: number | null
  sizeBytes: number | null
  sha256: string | null
  attributeCount: number | null
  originalUid: number | null
  originalGid: number | null
  executorUid: number | null
  executorGid: number | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  receipt: RecycleReceipt | null
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
export function validRecycleId(value: unknown): value is number {
  return (
    typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value < 4294967295
  )
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
export function recycleTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function recycleState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验对象和HDD原件',
        Ready: '预检完成，尚未移除文件',
        Pending: '等待文件回收',
        Applying: '正在回收文件',
        Unknown: '结果未知，保护已保留',
        Succeeded: '文件回收已完成',
        Superseded: '曾完成文件回收，当前对象已变化',
        Canceled: '已取消',
        Failed: '文件回收未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置未知历史，不代表文件回收成功',
      } as Record<string, string>
    )[state] ?? '无法核实的文件回收状态'
  )
}
export function recycleResult(code: string | null) {
  return (
    (
      {
        CONTAINER_RECYCLE_PLAN_ACCEPTED: '预检任务已登记，尚未移除文件。',
        CONTAINER_RECYCLE_READY: '预检已完成，请核对原文件与回收范围后确认。',
        CONTAINER_RECYCLE_ACCEPTED: '确认已登记，等待执行。',
        CONTAINER_RECYCLE_ALREADY_RESTORED: '原文件曾恢复，本次不视为新回收成功。',
        CONTAINER_RECYCLE_TRASHED: '文件已回收，HDD原件已验证留存。',
        CONTAINER_RECYCLE_CANCELED: '任务已取消。',
        CONTAINER_RECYCLE_PLAN_EXPIRED: '预检计划已过期，请重新选择对象并预检。',
        CONTAINER_RECYCLE_SOURCE_CHANGED: '对象在预检后发生变化，请重新读取后预检。',
        CONTAINER_RECYCLE_SOURCE_UNSUPPORTED: '该对象类型、链接或特殊文件回收暂不支持此操作。',
        CONTAINER_RECYCLE_EXTENDED_METADATA_REQUIRED:
          '该对象带有ACL或安全属性，当前文件回收入口无法安全修改。',
        CONTAINER_RECYCLE_PROTECTED: '该对象受保护或位于只读区域，无法回收文件。',
        CONTAINER_RECYCLE_TARGET_CHANGED: '原容器实例或对象已变化，请核实原任务。',
        CONTAINER_RECYCLE_ATTRIBUTES_LIMIT: '对象属性超出本入口的安全检查范围。',
        CONTAINER_RECYCLE_RESULT_UNKNOWN: '无法确认最终结果，保护已保留，请由管理员核实。',
        CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED: '已确认历史不确定性并释放保护，不代表修改成功。',
        CONTAINER_RECYCLE_INTERRUPTED: '执行被中断，请核对原任务后再决定下一步。',
      } as Record<string, string>
    )[code ?? ''] ?? '操作结果已记录，可查看任务诊断代码。'
  )
}
export function validRecycleCommand(value: ContainerRecycleCommand) {
  return (
    !!value &&
    Object.keys(value).length === 2 &&
    validContainerUploadLocation(value.location) &&
    typeof value.expectedVersion === 'string' &&
    hash.test(value.expectedVersion)
  )
}
export function sameRecycleCommand(a: ContainerRecycleCommand, b: ContainerRecycleCommand) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.expectedVersion === b.expectedVersion
  )
}
function date(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
}
export interface RecycleReceipt {
  id: string
  containerId: string
  instanceId: string
  fingerprint: string
  targetProof: string
  state: 'Trashed' | 'Restored' | 'Aborted' | 'Unknown'
  bindingCurrent: boolean
  replayed: boolean
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string
  historyAvailable: boolean
  sourceRetained: boolean | null
  sourceRestored: boolean | null
}
const fields =
  'id planningJobId executionJobId command state originalUid originalGid originalMode executorUid executorGid sizeBytes sha256 attributeCount readyExpiresAtUtc resultCode version historyMayExist historyVerified historyDigest receipt confirmation'.split(
    ' ',
  )
function exact(value: object, names: string[]) {
  return (
    Object.keys(value).length === names.length &&
    names.every((n) => Object.prototype.hasOwnProperty.call(value, n))
  )
}
function validReceipt(r: RecycleReceipt, v: ContainerRecycleIntent) {
  if (
    !r ||
    !exact(
      r,
      'id containerId instanceId fingerprint targetProof state bindingCurrent replayed historyMayExist historyVerified historyDigest historyAvailable sourceRetained sourceRestored'.split(
        ' ',
      ),
    ) ||
    r.id !== v.executionJobId ||
    r.containerId !== v.command.location.containerId ||
    r.instanceId !== v.command.location.instanceId ||
    !hash.test(r.fingerprint) ||
    !hash.test(r.targetProof) ||
    !['Trashed', 'Restored', 'Aborted', 'Unknown'].includes(r.state) ||
    ![r.bindingCurrent, r.replayed, r.historyMayExist, r.historyVerified, r.historyAvailable].every(
      (x) => typeof x === 'boolean',
    ) ||
    r.historyVerified !== v.historyVerified ||
    r.historyDigest !== (v.historyDigest ?? '') ||
    (r.historyMayExist && !v.historyMayExist) ||
    (r.historyAvailable && !r.historyVerified) ||
    (r.bindingCurrent && (!r.historyAvailable || !['Trashed', 'Restored'].includes(r.state)))
  )
    return false
  return r.state === 'Unknown'
    ? r.sourceRetained === null && r.sourceRestored === null
    : r.sourceRetained === (r.state === 'Trashed') &&
        r.sourceRestored === (r.state === 'Restored') &&
        (!['Trashed', 'Restored'].includes(r.state) || r.historyVerified)
}
export function assertRecycleIntent(
  value: ContainerRecycleIntent,
  containerId?: string,
  id?: string,
  command?: ContainerRecycleCommand,
) {
  if (
    !value ||
    !exact(value, fields) ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    !validRecycleCommand(value.command) ||
    (containerId && value.command.location.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !sameRecycleCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    typeof value.historyMayExist !== 'boolean' ||
    typeof value.historyVerified !== 'boolean' ||
    (value.historyVerified
      ? !value.historyMayExist ||
        typeof value.historyDigest !== 'string' ||
        !hash.test(value.historyDigest)
      : value.historyDigest !== null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        !/^CONTAINER_RECYCLE_[A-Z_]{1,46}$/.test(value.resultCode))) ||
    ((recycleTerminal(value.state) || value.state === 'Unknown') && value.resultCode === null)
  )
    throw new Error('回收任务的身份或状态无法核实')
  const planned = value.readyExpiresAtUtc !== null
  const metadata = [value.originalUid, value.originalGid, value.executorUid, value.executorGid]
  if (
    planned
      ? !date(value.readyExpiresAtUtc) ||
        !metadata.every(validRecycleId) ||
        !Number.isSafeInteger(value.originalMode) ||
        value.originalMode! < 32768 ||
        value.originalMode! > 33279 ||
        !Number.isSafeInteger(value.sizeBytes) ||
        value.sizeBytes! < 0 ||
        value.sizeBytes! > 1073741824 ||
        typeof value.sha256 !== 'string' ||
        !hash.test(value.sha256) ||
        !Number.isInteger(value.attributeCount) ||
        value.attributeCount! < 0 ||
        value.attributeCount! > 32
      : [...metadata, value.originalMode, value.sizeBytes, value.sha256, value.attributeCount].some(
          (x) => x !== null,
        )
  )
    throw new Error('回收预检摘要无法核实')
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
    (value.receipt !== null && !validReceipt(value.receipt, value)) ||
    (value.executionJobId === null &&
      (value.receipt !== null || value.historyMayExist || value.historyVerified)) ||
    (['Succeeded', 'Superseded', 'Acknowledged'].includes(value.state) && value.receipt === null) ||
    (value.state === 'Succeeded' &&
      (value.receipt?.state !== 'Trashed' || !value.receipt.bindingCurrent)) ||
    (value.state === 'Superseded' &&
      !(
        value.receipt?.state === 'Restored' ||
        (value.receipt?.state === 'Trashed' && !value.receipt.bindingCurrent)
      )) ||
    (value.state === 'Unknown' && value.receipt !== null && value.receipt.state !== 'Unknown') ||
    (value.executionJobId !== null &&
      ['Canceled', 'Failed'].includes(value.state) &&
      value.receipt?.state !== 'Aborted')
  )
    throw new Error('回收结果或历史事实无法核实')
  const c = value.confirmation
  if (
    value.state === 'Ready'
      ? !c ||
        !exact(c, ['planHash', 'expiresAtUtc', 'warning']) ||
        typeof c.planHash !== 'string' ||
        !c.planHash ||
        c.planHash.length > 4096 ||
        !date(c.expiresAtUtc) ||
        c.expiresAtUtc !== value.readyExpiresAtUtc ||
        typeof c.warning !== 'string' ||
        c.warning.length > 2048
      : c !== null
  )
    throw new Error('回收确认计划无法核实')
  return value
}
export function assertRecycleAdvance(
  previous: ContainerRecycleIntent | null,
  value: ContainerRecycleIntent,
) {
  assertRecycleIntent(value)
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const normalize = (v: ContainerRecycleIntent) =>
    JSON.stringify({ ...v, ...(virtualExpiry ? { state: 'Expired', confirmation: null } : {}) })
  const plan = (v: ContainerRecycleIntent) =>
    JSON.stringify([
      v.originalUid,
      v.originalGid,
      v.originalMode,
      v.executorUid,
      v.executorGid,
      v.sizeBytes,
      v.sha256,
      v.attributeCount,
      v.readyExpiresAtUtc,
    ])
  const fact = (r: RecycleReceipt) =>
    JSON.stringify([
      r.id,
      r.containerId,
      r.instanceId,
      r.fingerprint,
      r.targetProof,
      r.state,
      r.sourceRetained,
      r.sourceRestored,
      r.historyVerified,
      r.historyDigest,
    ])
  if (
    value.id !== previous.id ||
    !sameRecycleCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (recycleTerminal(previous.state) && normalize(value) !== normalize(previous)) ||
    (previous.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(value.state)) ||
    (previous.state === 'Applying' && value.state === 'Pending') ||
    (previous.executionJobId !== null && value.executionJobId !== previous.executionJobId) ||
    (previous.readyExpiresAtUtc !== null && plan(previous) !== plan(value)) ||
    (previous.state === 'Ready' &&
      value.state === 'Ready' &&
      JSON.stringify(previous.confirmation) !== JSON.stringify(value.confirmation)) ||
    (previous.receipt && (!value.receipt || fact(previous.receipt) !== fact(value.receipt))) ||
    (previous.historyMayExist && !value.historyMayExist) ||
    (previous.historyVerified && !value.historyVerified) ||
    (previous.historyDigest !== null && previous.historyDigest !== value.historyDigest) ||
    (value.version === previous.version && normalize(value) !== normalize(previous))
  )
    throw new Error('回收任务返回了旧状态或不同原计划')
}
export function canRecycleContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    entry.kind === 'File' &&
    /^0[0-7]{3}$/.test(entry.mode) &&
    hash.test(entry.version) &&
    Number.isSafeInteger(entry.sizeBytes) &&
    entry.sizeBytes >= 0 &&
    entry.sizeBytes <= 1073741824
  )
}
export async function queueRecycle(
  command: ContainerRecycleCommand,
  key: string,
  signal: AbortSignal,
) {
  if (!validRecycleCommand(command) || !validDirectoryId(key))
    throw new Error('回收文件、当前版本或登记键无效')
  const value = await apiRequest<ContainerRecycleIntent>(
    `${containerFileBase(command.location.containerId)}/recycles`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.location.instanceId,
        path: command.location.path,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  return assertRecycleIntent(value, command.location.containerId, undefined, command)
}
export async function getRecycle(
  containerId: string,
  id: string,
  signal: AbortSignal,
  byKey = false,
) {
  if (!validDirectoryId(id)) throw new Error('文件回收任务引用无效')
  return assertRecycleIntent(
    await apiRequest<ContainerRecycleIntent>(
      `${containerFileBase(containerId)}/recycles/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmRecycle(row: ContainerRecycleIntent, signal: AbortSignal) {
  assertRecycleIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('文件回收计划已过期或尚未核实')
  const value = await apiRequest<ContainerRecycleIntent>(
    `${containerFileBase(row.command.location.containerId)}/recycles/${row.id}/confirm`,
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
  return assertRecycleIntent(value, row.command.location.containerId, row.id, row.command)
}
export async function cancelRecycle(row: ContainerRecycleIntent, signal: AbortSignal) {
  assertRecycleIntent(row)
  return assertRecycleIntent(
    await apiRequest<ContainerRecycleIntent>(
      `${containerFileBase(row.command.location.containerId)}/recycles/${row.id}/cancel`,
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
export const recycleRecoveryBase = 'files/container-recycles/recovery'
// The live target is used only to refuse confirming a stale instance, never to rewrite an old command.
export function currentRecycleInstance(row: ContainerRecycleIntent, target: ContainerFileTarget) {
  return (
    row.command.location.containerId === target.containerId &&
    row.command.location.instanceId === target.instanceId
  )
}
