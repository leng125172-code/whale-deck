import { apiRequest, createIdempotencyKey } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'

export interface DirectoryCommand {
  location: ContainerUploadLocation
  mode: string
}
export interface DirectoryPlan {
  planHash: string
  expiresAtUtc: string
  command: DirectoryCommand
  warning: string
}
export interface DirectoryRegistration {
  planHash: string
  confirmed: true
  idempotencyKey: string
}
export interface DirectoryIntent {
  id: string
  jobId: string
  command: DirectoryCommand
  state: string
  expiresAtUtc: string
  resultCode: string | null
  version: number
  publishedVersion: string | null
}
const states = [
  'Pending',
  'Applying',
  'Unknown',
  'Succeeded',
  'Superseded',
  'Canceled',
  'Failed',
  'Acknowledged',
]
const uuid = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/
export function validDirectoryId(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    uuid.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
  )
}
function sameCommand(a: DirectoryCommand, b: DirectoryCommand) {
  return (
    a.location.containerId === b.location.containerId &&
    a.location.instanceId === b.location.instanceId &&
    a.location.path === b.location.path &&
    a.mode === b.mode
  )
}
export function directoryTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Acknowledged'].includes(state)
}
export function directoryState(state: string) {
  return (
    (
      {
        Pending: '等待执行',
        Applying: '正在创建目录',
        Unknown: '结果未知，保护已保留',
        Succeeded: '目录已创建',
        Superseded: '目录曾创建，当前目标已变化',
        Canceled: '已取消',
        Failed: '未完成',
        Acknowledged: '已处置历史未知结果，不代表创建成功',
      } as Record<string, string>
    )[state] ?? '无法核实的状态'
  )
}
function commandValid(value: DirectoryCommand) {
  return (
    !!value &&
    Object.keys(value).length === 2 &&
    validContainerUploadLocation(value.location) &&
    /^0[0-7]{3}$/.test(value.mode)
  )
}
export function assertDirectoryIntent(
  value: DirectoryIntent,
  command?: DirectoryCommand,
  id?: string,
) {
  if (
    !value ||
    !validDirectoryId(value.id) ||
    value.jobId !== value.id ||
    !commandValid(value.command) ||
    !states.includes(value.state) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
    (id && value.id !== id) ||
    (command && !sameCommand(value.command, command)) ||
    (['Succeeded', 'Superseded'].includes(value.state)
      ? typeof value.publishedVersion !== 'string' || !/^[A-F0-9]{64}$/.test(value.publishedVersion)
      : value.publishedVersion !== null) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_DIRECTORY_[A-Z_]+$/.test(value.resultCode)))
  )
    throw new Error('目录任务或目标无法核实')
  return value
}
export async function planDirectory(
  target: ContainerFileTarget,
  path: string,
  mode: string,
  signal: AbortSignal,
) {
  const command = {
    location: { containerId: target.containerId, instanceId: target.instanceId, path },
    mode,
  }
  if (!commandValid(command)) throw new Error('目录路径或权限无效')
  const plan = await apiRequest<DirectoryPlan>(
    `${containerFileBase(target.containerId)}/directories/plan`,
    { method: 'POST', body: JSON.stringify({ instanceId: target.instanceId, path, mode }), signal },
  )
  if (
    !plan ||
    typeof plan.planHash !== 'string' ||
    !plan.planHash ||
    plan.planHash.length > 65536 ||
    !Number.isFinite(Date.parse(plan.expiresAtUtc)) ||
    !commandValid(plan.command) ||
    !sameCommand(plan.command, command) ||
    typeof plan.warning !== 'string' ||
    plan.warning.length > 2048
  )
    throw new Error('目录计划无法核实')
  return plan
}
export function directoryRegistration(plan: DirectoryPlan): DirectoryRegistration {
  return { planHash: plan.planHash, confirmed: true, idempotencyKey: createIdempotencyKey() }
}
export async function submitDirectory(
  target: ContainerFileTarget,
  registration: DirectoryRegistration,
  signal: AbortSignal,
) {
  return apiRequest<DirectoryIntent>(`${containerFileBase(target.containerId)}/directories`, {
    method: 'POST',
    headers: { 'Idempotency-Key': registration.idempotencyKey },
    body: JSON.stringify(registration),
    signal,
  })
}
export async function resolveDirectory(
  target: ContainerFileTarget,
  registration: DirectoryRegistration,
  signal: AbortSignal,
) {
  return apiRequest<DirectoryIntent | null>(
    `${containerFileBase(target.containerId)}/directories/resolve`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': registration.idempotencyKey },
      body: JSON.stringify(registration),
      signal,
    },
  )
}
export function getDirectory(target: ContainerFileTarget, id: string, signal: AbortSignal) {
  if (!uuid.test(id)) throw new Error('目录任务编号无效')
  return apiRequest<DirectoryIntent>(`${containerFileBase(target.containerId)}/directories/${id}`, {
    signal,
  })
}
export function cancelDirectory(target: ContainerFileTarget, id: string, signal: AbortSignal) {
  if (!uuid.test(id)) throw new Error('目录任务编号无效')
  return apiRequest(`${containerFileBase(target.containerId)}/directories/${id}/cancel`, {
    method: 'POST',
    headers: { 'Idempotency-Key': id },
    signal,
  })
}
export const directoryRecoveryBase = 'files/container-directories/recovery'
