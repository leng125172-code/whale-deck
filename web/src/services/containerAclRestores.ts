import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import { validDirectoryId } from './containerDirectories'
import { assertAclIntent, sameAclEntries } from './containerAcls'
import { assertContainerAclHistorySnapshot } from './containerAclHistory'
import type { ContainerAclIntent } from './containerAcls'
import type { ContainerAclHistorySnapshot } from './containerAclHistory'

export interface ContainerAclRestore {
  intent: ContainerAclIntent
  source: ContainerAclHistorySnapshot
}
export interface ContainerAclRestoreRequest {
  location: ContainerAclIntent['command']['location']
  captureId: string
  expectedHistoryDigest: string
  expectedVersion: string
}
export function restoreSourceKey(source: ContainerAclHistorySnapshot) {
  const e = source.entry
  return JSON.stringify([
    source.location.containerId,
    source.location.instanceId,
    source.location.path,
    e.captureId,
    e.historyDigest,
    e.createdAtUnixMs,
    e.sourceInstanceId,
    e.changedAclType,
    e.kind,
    e.originalMode,
    e.accessEntryCount,
    e.defaultEntryCount,
    ...[source.accessAcl, source.defaultAcl].map((entries) =>
      entries.map((v) => [v.tag, v.id, v.permissions]),
    ),
  ])
}
export function assertAclRestore(
  value: ContainerAclRestore,
  containerId: string,
  id?: string,
  expected?: ContainerAclHistorySnapshot,
  version?: string,
) {
  if (!value || Object.keys(value).length !== 2 || !('intent' in value) || !('source' in value))
    throw new Error('ACL历史恢复任务无法核实')
  const intent = assertAclIntent(value.intent, containerId, id)
  const c = intent.command
  const source = assertContainerAclHistorySnapshot(
    value.source,
    c.location,
    c.location.path,
    expected?.entry ?? value.source?.entry,
  )
  if (
    (expected && restoreSourceKey(source) !== restoreSourceKey(expected)) ||
    c.aclType !== source.entry.changedAclType ||
    (intent.kind !== null && intent.kind !== source.entry.kind) ||
    !sameAclEntries(c.entries, c.aclType === 'access' ? source.accessAcl : source.defaultAcl) ||
    (version !== undefined && c.expectedVersion !== version)
  )
    throw new Error('ACL恢复来源或目标已变化，不能确认')
  return value
}
export async function queueAclRestore(
  request: ContainerAclRestoreRequest,
  key: string,
  source: ContainerAclHistorySnapshot,
  signal: AbortSignal,
) {
  if (
    !validDirectoryId(key) ||
    request.captureId !== source.entry.captureId ||
    request.expectedHistoryDigest !== source.entry.historyDigest ||
    request.location.containerId !== source.location.containerId ||
    request.location.instanceId !== source.location.instanceId ||
    request.location.path !== source.location.path ||
    !/^[A-F0-9]{64}$/.test(request.expectedVersion)
  )
    throw new Error('ACL恢复登记无效')
  assertContainerAclHistorySnapshot(source, request.location, request.location.path, source.entry)
  const value = await apiRequest<ContainerAclRestore>(
    `${containerFileBase(request.location.containerId)}/acls/restores`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      signal,
      body: JSON.stringify({
        instanceId: request.location.instanceId,
        path: request.location.path,
        captureId: request.captureId,
        expectedHistoryDigest: request.expectedHistoryDigest,
        expectedVersion: request.expectedVersion,
      }),
    },
  )
  const result = assertAclRestore(
    value,
    request.location.containerId,
    undefined,
    source,
    request.expectedVersion,
  )
  if (result.intent.executionJobId !== null || result.intent.receipt !== null)
    throw new Error('恢复预检登记返回了执行状态，请仅查询原任务；未自动确认。')
  return result
}
export async function getAclRestore(
  containerId: string,
  reference: string,
  signal: AbortSignal,
  byKey = false,
) {
  if (!/^[a-f0-9]{64}$/.test(containerId) || !validDirectoryId(reference))
    throw new Error('ACL恢复引用无效')
  const value = await apiRequest<ContainerAclRestore>(
    `${containerFileBase(containerId)}/acls/restores/${byKey ? 'by-key/' : ''}${reference}`,
    { signal },
  )
  return assertAclRestore(value, containerId, byKey ? undefined : reference)
}
