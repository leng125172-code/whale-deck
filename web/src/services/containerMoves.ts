import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import type { FileEntry } from './fileManagement'

export interface ContainerMoveCommand {
  source: ContainerUploadLocation
  targetPath: string
  expectedVersion: string
}
export interface ContainerMoveIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  command: ContainerMoveCommand
  operationKind: 'Move'
  state: string
  kind: 'File' | 'Directory' | null
  entryCount: number | null
  totalBytes: number | null
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  publishedVersion: string | null
  privateMayExist: boolean
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
  destinationPublished: boolean | null
  sourceRemoved: boolean | null
  sourceRetained: boolean | null
}
export function moveFactText(value: boolean | null) {
  return value === true ? '已确认' : value === false ? '未发生' : '尚未核实'
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
export function moveTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(
    state,
  )
}
export function moveState(state: string) {
  return (
    (
      {
        Queued: '等待预检',
        Planning: '正在校验源内容',
        Ready: '预检已完成，尚未移动',
        Pending: '等待移动',
        Applying: '正在移动',
        Unknown: '结果未知，保护已保留',
        Succeeded: '移动已完成',
        Superseded: '曾完成移动，当前目标已变化',
        Canceled: '已取消',
        Failed: '移动未完成',
        Expired: '预检计划已过期',
        Acknowledged: '已处置未知历史，不代表移动成功',
      } as Record<string, string>
    )[state] ?? '无法核实的移动状态'
  )
}
export function validMoveCommand(value: ContainerMoveCommand) {
  if (
    !value ||
    Object.keys(value).length !== 3 ||
    !validContainerUploadLocation(value.source) ||
    !validContainerUploadLocation({ ...value.source, path: value.targetPath }) ||
    !hash.test(value.expectedVersion)
  )
    return false
  const source = value.source.path,
    target = value.targetPath
  return source !== target && !source.startsWith(target + '/') && !target.startsWith(source + '/')
}
export function sameMoveCommand(a: ContainerMoveCommand, b: ContainerMoveCommand) {
  return (
    a.source.containerId === b.source.containerId &&
    a.source.instanceId === b.source.instanceId &&
    a.source.path === b.source.path &&
    a.targetPath === b.targetPath &&
    a.expectedVersion === b.expectedVersion
  )
}
function date(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
}
export function assertMoveIntent(
  value: ContainerMoveIntent,
  containerId?: string,
  id?: string,
  command?: ContainerMoveCommand,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.planningJobId !== value.id ||
    value.operationKind !== 'Move' ||
    ![value.destinationPublished, value.sourceRemoved, value.sourceRetained].every(
      (fact) => fact === null || typeof fact === 'boolean',
    ) ||
    !validMoveCommand(value.command) ||
    (containerId && value.command.source.containerId !== containerId) ||
    (id && value.id !== id) ||
    (command && !sameMoveCommand(value.command, command)) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    typeof value.privateMayExist !== 'boolean' ||
    (value.executionJobId !== null &&
      (!validDirectoryId(value.executionJobId) || value.executionJobId === value.id)) ||
    ((moveTerminal(value.state) || value.state === 'Unknown') && value.resultCode === null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_MOVE_[A-Z_]+$/.test(value.resultCode)))
  )
    throw new Error('移动任务的身份或状态无法核实')
  const planned = value.kind !== null
  if (
    planned
      ? !['File', 'Directory'].includes(value.kind!) ||
        !date(value.readyExpiresAtUtc) ||
        !Number.isSafeInteger(value.entryCount) ||
        value.entryCount! < 1 ||
        value.entryCount! > 10000 ||
        !Number.isSafeInteger(value.totalBytes) ||
        value.totalBytes! < 0 ||
        value.totalBytes! > (value.kind === 'File' ? 1073741824 : 17179869184) ||
        (value.kind === 'File' && value.entryCount !== 1)
      : value.entryCount !== null || value.totalBytes !== null || value.readyExpiresAtUtc !== null
  )
    throw new Error('移动预检摘要无法核实')
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
    (value.state === 'Unknown' && !value.privateMayExist) ||
    (['Succeeded', 'Superseded'].includes(value.state)
      ? typeof value.publishedVersion !== 'string' ||
        !hash.test(value.publishedVersion) ||
        value.privateMayExist
      : value.publishedVersion !== null)
  )
    throw new Error('移动结果不能确认为有效终态')
  const facts = [value.destinationPublished, value.sourceRemoved, value.sourceRetained]
  if (
    (['Succeeded', 'Superseded'].includes(value.state) && !facts.every((fact) => fact === true)) ||
    (value.executionJobId === null && !facts.every((fact) => fact === null)) ||
    (value.executionJobId !== null &&
      ['Canceled', 'Failed'].includes(value.state) &&
      !facts.every((fact) => fact === false))
  )
    throw new Error('移动的发布、源移除或原件保留事实无法核实')
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
    throw new Error('移动确认计划无法核实')
  return value
}
export function assertMoveAdvance(
  previous: ContainerMoveIntent | null,
  value: ContainerMoveIntent,
) {
  if (!previous) return
  const virtualExpiry =
    previous.state === 'Ready' && value.state === 'Expired' && value.executionJobId === null
  const snapshot = (v: ContainerMoveIntent) =>
    JSON.stringify([
      v.planningJobId,
      v.executionJobId,
      virtualExpiry ? null : v.state,
      v.kind,
      v.entryCount,
      v.totalBytes,
      v.readyExpiresAtUtc,
      v.resultCode,
      v.publishedVersion,
      v.privateMayExist,
      v.destinationPublished,
      v.sourceRemoved,
      v.sourceRetained,
      virtualExpiry ? null : v.confirmation?.planHash,
      virtualExpiry ? null : v.confirmation?.expiresAtUtc,
      virtualExpiry ? null : v.confirmation?.warning,
    ])
  if (
    value.id !== previous.id ||
    value.operationKind !== 'Move' ||
    (['destinationPublished', 'sourceRemoved', 'sourceRetained'] as const).some(
      (field) => previous[field] !== null && value[field] !== previous[field],
    ) ||
    !sameMoveCommand(value.command, previous.command) ||
    value.version < previous.version ||
    (moveTerminal(previous.state) && value.state !== previous.state) ||
    (previous.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(value.state)) ||
    (previous.state === 'Applying' && value.state === 'Pending') ||
    (previous.executionJobId !== null && value.executionJobId !== previous.executionJobId) ||
    (previous.kind !== null &&
      (value.kind !== previous.kind ||
        value.entryCount !== previous.entryCount ||
        value.totalBytes !== previous.totalBytes ||
        value.readyExpiresAtUtc !== previous.readyExpiresAtUtc)) ||
    (value.version === previous.version && snapshot(value) !== snapshot(previous))
  )
    throw new Error('移动任务返回了旧状态或不同的原计划')
}
export function canMoveContainerEntry(entry: FileEntry) {
  return (
    !entry.isProtected &&
    ['File', 'Directory'].includes(entry.kind) &&
    /^0[0-7]{3}$/.test(entry.mode) &&
    hash.test(entry.version) &&
    (entry.kind === 'Directory' ||
      (Number.isSafeInteger(entry.sizeBytes) &&
        entry.sizeBytes >= 0 &&
        entry.sizeBytes <= 1073741824))
  )
}
export function defaultMoveTarget(parent: string, name: string) {
  const bytes = new TextEncoder(),
    prefix = parent ? parent + '/' : '',
    budget = Math.min(255, 4096 - bytes.encode(prefix).length)
  if (budget < bytes.encode('-移动').length) return prefix + (name === 'c' ? 'd' : 'c')
  const characters = Array.from(name)
  while (bytes.encode(characters.join('') + '-移动').length > budget) characters.pop()
  const candidate = characters.join('') + '-移动'
  return prefix + (candidate === name ? (name === 'c' ? 'd' : 'c') : candidate)
}
export async function queueMove(command: ContainerMoveCommand, key: string, signal: AbortSignal) {
  if (!validMoveCommand(command) || !validDirectoryId(key))
    throw new Error('移动来源、目标或登记键无效')
  const value = await apiRequest<ContainerMoveIntent>(
    `${containerFileBase(command.source.containerId)}/moves`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: command.source.instanceId,
        sourcePath: command.source.path,
        targetPath: command.targetPath,
        expectedVersion: command.expectedVersion,
      }),
    },
  )
  return assertMoveIntent(value, command.source.containerId, undefined, command)
}
export async function getMove(containerId: string, id: string, signal: AbortSignal, byKey = false) {
  if (!validDirectoryId(id)) throw new Error('移动任务引用无效')
  return assertMoveIntent(
    await apiRequest<ContainerMoveIntent>(
      `${containerFileBase(containerId)}/moves/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    containerId,
    byKey ? undefined : id,
  )
}
export async function confirmMove(row: ContainerMoveIntent, signal: AbortSignal) {
  assertMoveIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('移动计划已过期或尚未核实')
  const value = await apiRequest<ContainerMoveIntent>(
    `${containerFileBase(row.command.source.containerId)}/moves/${row.id}/confirm`,
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
  return assertMoveIntent(value, row.command.source.containerId, row.id, row.command)
}
export async function cancelMove(row: ContainerMoveIntent, signal: AbortSignal) {
  assertMoveIntent(row)
  return assertMoveIntent(
    await apiRequest<ContainerMoveIntent>(
      `${containerFileBase(row.command.source.containerId)}/moves/${row.id}/cancel`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        signal,
      },
    ),
    row.command.source.containerId,
    row.id,
    row.command,
  )
}
export const moveRecoveryBase = 'files/container-moves/recovery'
// The live target is used only to refuse confirming a stale instance, never to rewrite an old command.
export function currentMoveInstance(row: ContainerMoveIntent, target: ContainerFileTarget) {
  return (
    row.command.source.containerId === target.containerId &&
    row.command.source.instanceId === target.instanceId
  )
}
