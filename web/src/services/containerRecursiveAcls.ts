import { apiRequest } from './apiClient'
import { validAclEntries, sameAclEntries } from './containerAcls'
import type { ContainerAclEntry } from './containerAcls'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerRecursiveAclCommand {
  location: ContainerUploadLocation
  aclType: 'access' | 'default'
  fileEntries: ContainerAclEntry[] | null
  directoryEntries: ContainerAclEntry[]
  expectedVersion: string
}
export interface ContainerRecursiveAclReceipt {
  id: string
  containerId: string
  instanceId: string
  fingerprint: string
  targetProof: string
  state: 'Completed' | 'Partial' | 'Aborted' | 'Unknown'
  bindingCurrent: boolean
  replayed: boolean
  completedCount: number
  changedCount: number
  abortedCount: number
  notStartedCount: number
  unknownCount: number
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string
  historyAvailable: boolean
  outcomeDigest: string
  aclType: 'access' | 'default'
}
export interface ContainerRecursiveAclIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerRecursiveAclCommand
  state: string
  rootUid: number | null
  rootGid: number | null
  rootMode: number | null
  executorUid: number | null
  executorGid: number | null
  entryCount: number | null
  fileCount: number | null
  directoryCount: number | null
  changeCount: number | null
  manifestDigest: string | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  historyMayExist: boolean
  historyVerified: boolean
  historyDigest: string | null
  receipt: ContainerRecursiveAclReceipt | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
const hash = /^[A-F0-9]{64}$/
const states = [
  'Queued',
  'Planning',
  'Ready',
  'Pending',
  'Applying',
  'Partial',
  'Unknown',
  'Succeeded',
  'Superseded',
  'Canceled',
  'Failed',
  'Expired',
  'Acknowledged',
]
export function recursiveAclUnresolved(state: string) {
  return state === 'Partial' || state === 'Unknown'
}
export function recursiveAclTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function recursiveAclState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验目录树和HDD元数据',
        Ready: '预检已完成，尚未修改ACL',
        Pending: '等待递归ACL修改',
        Applying: '正在逐项修改ACL',
        Partial: '部分修改，保护已保留',
        Unknown: '结果未知，保护已保留',
        Succeeded: '递归ACL修改已完成',
        Superseded: '曾完成修改，当前目录树已变化',
        Canceled: '已取消',
        Failed: '递归ACL修改未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置历史结果，不代表整树修改成功',
      } as Record<string, string>
    )[state] ?? '无法核实的递归ACL状态'
  )
}
export function recursiveAclResult(code: string | null) {
  return (
    (
      {
        CONTAINER_RECURSIVE_ACL_PLAN_ACCEPTED: '预检已登记，尚未修改ACL。',
        CONTAINER_RECURSIVE_ACL_READY: '核对目录树清单、ACL类型及文件/目录两组目标后再确认。',
        CONTAINER_RECURSIVE_ACL_ACCEPTED: '确认已登记，等待执行。',
        CONTAINER_RECURSIVE_ACL_COMPLETED: '整树ACL修改已完成，HDD元数据已验证留存。',
        CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT:
          '仅部分对象完成修改；实际计数保留，不自动续做或回滚，请核对并明确处置。',
        CONTAINER_RECURSIVE_ACL_RESULT_UNKNOWN:
          '结果未完全证明，保护保留，请查询原任务并由管理员核实。',
        CONTAINER_RECURSIVE_ACL_PARTIAL_ACKNOWLEDGED:
          '已处置部分修改历史并释放保护，原计数和证据保留，不代表整树成功。',
        CONTAINER_RECURSIVE_ACL_UNKNOWN_ACKNOWLEDGED: '已处置未知历史并释放保护，不代表修改成功。',
        CONTAINER_RECURSIVE_ACL_PLAN_EXPIRED: '预检计划已过期，请查询原任务。',
        CONTAINER_RECURSIVE_ACL_CANCELED: '任务已取消，请以原回执的实际计数为准。',
        CONTAINER_RECURSIVE_ACL_VERSION_CONFLICT: '目录树已变化，请重新读取后预检。',
        CONTAINER_RECURSIVE_ACL_TARGET_CHANGED: '原实例或目录树已变化，请核实原任务。',
        CONTAINER_RECURSIVE_ACL_EXTENDED_METADATA_REQUIRED:
          '树中存在不支持的安全属性，当前入口无法安全处理。',
        CONTAINER_RECURSIVE_ACL_MAPPING_UNPROVEN: '身份或挂载映射尚未安全证明，已拒绝写入。',
        CONTAINER_RECURSIVE_ACL_UNSUPPORTED: '文件系统或内核不支持所需ACL操作。',
        CONTAINER_RECURSIVE_ACL_ATTRIBUTES_LIMIT: '属性超过安全预算，已拒绝写入。',
        CONTAINER_RECURSIVE_ACL_TREE_LIMIT: '目录树超过10000个对象的安全预算。',
        CONTAINER_RECURSIVE_ACL_MANIFEST_LIMIT: '目录树清单超过安全预算。',
        CONTAINER_RECURSIVE_ACL_PROTECTED: '目录树包含受保护或只读对象，无法修改。',
        CONTAINER_RECURSIVE_ACL_SOURCE_UNSUPPORTED: '目录树包含链接、挂载边界或不支持的对象。',
      } as Record<string, string>
    )[code ?? ''] ?? '操作结果已记录，可查看原任务诊断代码。'
  )
}
export function validRecursiveAclMode(value: unknown): value is string {
  return typeof value === 'string' && value.length === 4 && /^0[0-7]{3}$/.test(value)
}
export function validRecursiveAclCommand(value: ContainerRecursiveAclCommand) {
  return (
    !!value &&
    Object.keys(value).length === 5 &&
    validContainerUploadLocation(value.location) &&
    (value.aclType === 'access' || value.aclType === 'default') &&
    validAclEntries(value.directoryEntries, value.aclType === 'default') &&
    (value.aclType === 'default'
      ? value.fileEntries === null
      : validAclEntries(value.fileEntries)) &&
    typeof value.expectedVersion === 'string' &&
    value.expectedVersion.length === 64 &&
    hash.test(value.expectedVersion)
  )
}
export function sameRecursiveAclCommand(
  a: ContainerRecursiveAclCommand,
  b: ContainerRecursiveAclCommand,
) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.aclType === b.aclType &&
    (a.fileEntries === null
      ? b.fileEntries === null
      : b.fileEntries !== null && sameAclEntries(a.fileEntries, b.fileEntries)) &&
    sameAclEntries(a.directoryEntries, b.directoryEntries) &&
    a.expectedVersion === b.expectedVersion
  )
}
const uint = (v: unknown): v is number =>
  typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 && v < 4294967295
const count = (v: unknown): v is number => uint(v) && v <= 10000
const date = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= 64 && Number.isFinite(Date.parse(v))
const digest = (v: unknown): v is string => typeof v === 'string' && v.length === 64 && hash.test(v)
function bad(message: string): never {
  throw new Error(message)
}
export function assertRecursiveAclIntent(
  value: ContainerRecursiveAclIntent,
  containerId?: string,
  id?: string,
  command?: ContainerRecursiveAclCommand,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    !validRecursiveAclCommand(value.command) ||
    (containerId && value.command.location.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !sameRecursiveAclCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    typeof value.historyMayExist !== 'boolean' ||
    typeof value.historyVerified !== 'boolean' ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_RECURSIVE_ACL_[A-Z_]+$/.test(value.resultCode))) ||
    ((recursiveAclTerminal(value.state) || recursiveAclUnresolved(value.state)) &&
      value.resultCode === null)
  )
    bad('递归ACL任务的身份或状态无法核实')
  const planned = value.entryCount !== null
  const summary = [value.rootUid, value.rootGid, value.executorUid, value.executorGid]
  if (
    planned
      ? !summary.every(uint) ||
        !uint(value.rootMode) ||
        value.rootMode > 65535 ||
        (value.rootMode & 0xf000) !== 0x4000 ||
        (value.rootMode & 0xe00) !== 0 ||
        !count(value.entryCount) ||
        value.entryCount < 1 ||
        !count(value.fileCount) ||
        !count(value.directoryCount) ||
        value.directoryCount < 1 ||
        value.fileCount + value.directoryCount !== value.entryCount ||
        !count(value.changeCount) ||
        value.changeCount > value.entryCount ||
        (value.command.aclType === 'default' && value.changeCount > value.directoryCount) ||
        !digest(value.manifestDigest) ||
        !date(value.readyExpiresAtUtc)
      : value.rootMode !== null ||
        summary.some((v) => v !== null) ||
        value.fileCount !== null ||
        value.directoryCount !== null ||
        value.changeCount !== null ||
        value.manifestDigest !== null ||
        value.readyExpiresAtUtc !== null
  )
    bad('递归ACL预检清单无法核实')
  if (
    (['Queued', 'Planning'].includes(value.state) && (planned || value.executionJobId !== null)) ||
    ([
      'Ready',
      'Pending',
      'Applying',
      'Partial',
      'Unknown',
      'Succeeded',
      'Superseded',
      'Acknowledged',
    ].includes(value.state) &&
      !planned) ||
    ([
      'Pending',
      'Applying',
      'Partial',
      'Unknown',
      'Succeeded',
      'Superseded',
      'Acknowledged',
    ].includes(value.state) &&
      value.executionJobId === null) ||
    (['Ready', 'Expired'].includes(value.state) && value.executionJobId !== null) ||
    (value.historyVerified
      ? !value.historyMayExist || !digest(value.historyDigest)
      : value.historyDigest !== null) ||
    (value.executionJobId === null &&
      (value.receipt !== null || value.historyMayExist || value.historyVerified))
  )
    bad('递归ACL阶段或历史事实无法核实')
  const receipt = value.receipt
  if (receipt !== null) {
    if (
      !receipt ||
      receipt.id !== value.executionJobId ||
      receipt.aclType !== value.command.aclType ||
      receipt.containerId !== value.command.location.containerId ||
      receipt.instanceId !== value.command.location.instanceId ||
      !digest(receipt.fingerprint) ||
      !digest(receipt.targetProof) ||
      !['Completed', 'Partial', 'Aborted', 'Unknown'].includes(receipt.state) ||
      ![
        receipt.completedCount,
        receipt.changedCount,
        receipt.abortedCount,
        receipt.notStartedCount,
        receipt.unknownCount,
      ].every(count) ||
      receipt.completedCount +
        receipt.abortedCount +
        receipt.notStartedCount +
        receipt.unknownCount !==
        value.entryCount ||
      receipt.changedCount > receipt.completedCount ||
      receipt.changedCount > value.changeCount! ||
      ![
        receipt.bindingCurrent,
        receipt.replayed,
        receipt.historyMayExist,
        receipt.historyVerified,
        receipt.historyAvailable,
      ].every((v) => typeof v === 'boolean') ||
      (receipt.historyMayExist && !value.historyMayExist) ||
      receipt.historyVerified !== value.historyVerified ||
      receipt.historyDigest !== (value.historyDigest ?? '') ||
      (receipt.historyAvailable && !receipt.historyVerified) ||
      (receipt.bindingCurrent && (receipt.state !== 'Completed' || !receipt.historyAvailable)) ||
      ((['Completed', 'Partial'].includes(receipt.state) || receipt.outcomeDigest !== '') &&
        !digest(receipt.outcomeDigest))
    )
      bad('递归ACL回执或HDD事实无法核实')
    if (
      (receipt.state === 'Completed' &&
        (receipt.completedCount !== value.entryCount ||
          receipt.changedCount !== value.changeCount ||
          !receipt.historyVerified)) ||
      (receipt.state === 'Partial' &&
        (!receipt.changedCount ||
          receipt.completedCount === value.entryCount ||
          receipt.unknownCount !== 0 ||
          !receipt.historyVerified)) ||
      (receipt.state === 'Unknown' && !receipt.unknownCount) ||
      (receipt.state === 'Aborted' && (receipt.changedCount !== 0 || receipt.unknownCount !== 0))
    )
      bad('递归ACL实际计数无法核实')
    if (
      (value.state === 'Succeeded' && (receipt.state !== 'Completed' || !receipt.bindingCurrent)) ||
      (value.state === 'Superseded' && (receipt.state !== 'Completed' || receipt.bindingCurrent)) ||
      (recursiveAclUnresolved(value.state) && value.state !== receipt.state) ||
      (['Canceled', 'Failed'].includes(value.state) && receipt.state !== 'Aborted') ||
      ![
        'Succeeded',
        'Superseded',
        'Partial',
        'Unknown',
        'Canceled',
        'Failed',
        'Acknowledged',
      ].includes(value.state)
    )
      bad('递归ACL结果与原回执矛盾')
  } else if (
    ['Succeeded', 'Superseded', 'Partial', 'Acknowledged'].includes(value.state) ||
    (value.executionJobId !== null && ['Canceled', 'Failed'].includes(value.state)) ||
    value.historyVerified ||
    (value.historyMayExist && value.state !== 'Unknown')
  )
    bad('递归ACL结果缺少原回执')
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
    bad('递归ACL确认计划无法核实')
  return value
}
export function assertRecursiveAclAdvance(
  previous: ContainerRecursiveAclIntent | null,
  value: ContainerRecursiveAclIntent,
) {
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const snapshot = (v: ContainerRecursiveAclIntent) =>
    JSON.stringify({
      ...v,
      state: virtualExpiry ? null : v.state,
      confirmation: virtualExpiry ? null : v.confirmation,
    })
  const summary = (v: ContainerRecursiveAclIntent) =>
    JSON.stringify([
      v.rootUid,
      v.rootGid,
      v.rootMode,
      v.executorUid,
      v.executorGid,
      v.entryCount,
      v.fileCount,
      v.directoryCount,
      v.changeCount,
      v.manifestDigest,
      v.readyExpiresAtUtc,
    ])
  const facts = (r: ContainerRecursiveAclReceipt) =>
    JSON.stringify([
      r.id,
      r.containerId,
      r.instanceId,
      r.fingerprint,
      r.targetProof,
      r.state,
      r.aclType,
      r.completedCount,
      r.changedCount,
      r.abortedCount,
      r.notStartedCount,
      r.unknownCount,
      r.outcomeDigest,
      r.historyVerified,
      r.historyDigest,
    ])
  if (
    value.id !== previous.id ||
    !sameRecursiveAclCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (recursiveAclTerminal(previous.state) && value.state !== previous.state) ||
    (recursiveAclUnresolved(previous.state) &&
      ![previous.state, 'Acknowledged'].includes(value.state)) ||
    (previous.state === 'Applying' && value.state === 'Pending') ||
    (previous.executionJobId !== null && value.executionJobId !== previous.executionJobId) ||
    (previous.entryCount !== null && summary(value) !== summary(previous)) ||
    (previous.state === 'Ready' &&
      value.state === 'Ready' &&
      JSON.stringify(value.confirmation) !== JSON.stringify(previous.confirmation)) ||
    (previous.receipt !== null &&
      (value.receipt === null ||
        facts(previous.receipt) !== facts(value.receipt) ||
        (previous.receipt.historyMayExist && !value.receipt.historyMayExist))) ||
    (previous.historyMayExist && !value.historyMayExist) ||
    (previous.historyVerified && !value.historyVerified) ||
    (previous.historyDigest !== null && value.historyDigest !== previous.historyDigest) ||
    (value.version === previous.version && snapshot(value) !== snapshot(previous))
  )
    bad('递归ACL任务返回了旧状态或不同的原计划/实际事实')
}
export function canRecursiveAclContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    entry.kind === 'Directory' &&
    validRecursiveAclMode(entry.mode) &&
    digest(entry.version)
  )
}
export async function queueRecursiveAcl(
  command: ContainerRecursiveAclCommand,
  key: string,
  signal: AbortSignal,
) {
  if (!validRecursiveAclCommand(command) || !validDirectoryId(key))
    bad('递归ACL目录、目标条目或登记键无效')
  command = cloneRecursiveAclCommand(command)
  const value = await apiRequest<ContainerRecursiveAclIntent>(
    `${containerFileBase(command.location.containerId)}/recursive-acls`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.location.instanceId,
        path: command.location.path,
        aclType: command.aclType,
        fileEntries: command.fileEntries,
        directoryEntries: command.directoryEntries,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  return assertRecursiveAclIntent(value, command.location.containerId, undefined, command)
}
export async function getRecursiveAcl(
  containerId: string,
  id: string,
  signal: AbortSignal,
  byKey = false,
) {
  if (!validDirectoryId(id)) bad('递归ACL任务引用无效')
  return assertRecursiveAclIntent(
    await apiRequest<ContainerRecursiveAclIntent>(
      `${containerFileBase(containerId)}/recursive-acls/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmRecursiveAcl(row: ContainerRecursiveAclIntent, signal: AbortSignal) {
  assertRecursiveAclIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    bad('递归ACL计划已过期或尚未核实')
  return assertRecursiveAclIntent(
    await apiRequest<ContainerRecursiveAclIntent>(
      `${containerFileBase(row.command.location.containerId)}/recursive-acls/${row.id}/confirm`,
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
    ),
    row.command.location.containerId,
    row.id,
    row.command,
  )
}
export async function cancelRecursiveAcl(row: ContainerRecursiveAclIntent, signal: AbortSignal) {
  assertRecursiveAclIntent(row)
  return assertRecursiveAclIntent(
    await apiRequest<ContainerRecursiveAclIntent>(
      `${containerFileBase(row.command.location.containerId)}/recursive-acls/${row.id}/cancel`,
      { method: 'POST', headers: { 'Idempotency-Key': row.id }, signal },
    ),
    row.command.location.containerId,
    row.id,
    row.command,
  )
}
export const recursiveAclRecoveryBase = 'files/container-recursive-acls/recovery'
export function currentRecursiveAclInstance(
  row: ContainerRecursiveAclIntent,
  target: ContainerFileTarget,
) {
  return (
    row.command.location.containerId === target.containerId &&
    row.command.location.instanceId === target.instanceId
  )
}

export function cloneRecursiveAclCommand(
  command: ContainerRecursiveAclCommand,
): ContainerRecursiveAclCommand {
  return {
    location: { ...command.location },
    aclType: command.aclType,
    fileEntries: command.fileEntries?.map((e) => ({ ...e })) ?? null,
    directoryEntries: command.directoryEntries.map((e) => ({ ...e })),
    expectedVersion: command.expectedVersion,
  }
}
