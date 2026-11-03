import { apiRequest } from './apiClient'
import { containerFileBase, containerFileQuery } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { aclMode, validAclEntries } from './containerAcls'
import type { ContainerAclEntry } from './containerAcls'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'

export interface ContainerAclHistoryEntry {
  captureId: string
  historyDigest: string
  createdAtUnixMs: number
  sourceInstanceId: string
  changedAclType: 'access' | 'default'
  kind: 'File' | 'Directory'
  originalMode: number
  accessEntryCount: number
  defaultEntryCount: number
}
export interface ContainerAclHistoryPage {
  location: ContainerUploadLocation
  version: string
  entries: ContainerAclHistoryEntry[]
  nextAfter: string
  scannedCount: number
  invalidCount: number
  complete: boolean
}
export interface ContainerAclHistorySnapshot {
  location: ContainerUploadLocation
  entry: ContainerAclHistoryEntry
  accessAcl: ContainerAclEntry[]
  defaultAcl: ContainerAclEntry[]
}
type HistoryTarget = Pick<ContainerFileTarget, 'containerId' | 'instanceId'>
const hash = /^[A-F0-9]{64}$/
function capture(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f0-9]{32}$/.test(value) && value !== '0'.repeat(32)
}
function exact(value: unknown, fields: string[]): value is Record<string, unknown> {
  return (
    !!value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === fields.length &&
    Object.keys(value).every((key) => fields.includes(key))
  )
}
function integer(value: unknown, min: number, max: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max
}
function location(value: unknown, target: HistoryTarget, path: string) {
  return (
    exact(value, ['containerId', 'instanceId', 'path']) &&
    value.containerId === target.containerId &&
    value.instanceId === target.instanceId &&
    value.path === path &&
    validContainerUploadLocation(value)
  )
}
export function validContainerAclHistoryEntry(
  value: unknown,
  target: HistoryTarget,
  path: string,
): value is ContainerAclHistoryEntry {
  return (
    exact(value, [
      'captureId',
      'historyDigest',
      'createdAtUnixMs',
      'sourceInstanceId',
      'changedAclType',
      'kind',
      'originalMode',
      'accessEntryCount',
      'defaultEntryCount',
    ]) &&
    capture(value.captureId) &&
    typeof value.historyDigest === 'string' &&
    hash.test(value.historyDigest) &&
    integer(value.createdAtUnixMs, 0, 253402300799999) &&
    validContainerUploadLocation({
      containerId: target.containerId,
      instanceId: value.sourceInstanceId,
      path,
    }) &&
    (value.changedAclType === 'access' || value.changedAclType === 'default') &&
    (value.kind === 'File' || value.kind === 'Directory') &&
    integer(value.originalMode, 0, 511) &&
    integer(value.accessEntryCount, 3, 64) &&
    integer(value.defaultEntryCount, 0, 64) &&
    ![1, 2].includes(value.defaultEntryCount) &&
    (value.kind !== 'File' || (value.changedAclType === 'access' && value.defaultEntryCount === 0))
  )
}
export function assertContainerAclHistoryPage(
  value: unknown,
  target: HistoryTarget,
  path: string,
  after = '',
  version = '',
  take = 25,
): ContainerAclHistoryPage {
  if (
    !exact(value, [
      'location',
      'version',
      'entries',
      'nextAfter',
      'scannedCount',
      'invalidCount',
      'complete',
    ]) ||
    !location(value.location, target, path) ||
    typeof value.version !== 'string' ||
    !hash.test(value.version) ||
    (version !== '' && version !== value.version) ||
    typeof value.nextAfter !== 'string' ||
    (value.nextAfter !== '' && (!capture(value.nextAfter) || value.nextAfter <= after)) ||
    typeof value.complete !== 'boolean' ||
    value.complete !== (value.nextAfter === '') ||
    !integer(value.scannedCount, 0, 200) ||
    !integer(value.invalidCount, 0, value.scannedCount) ||
    !Array.isArray(value.entries) ||
    value.entries.length > take ||
    value.entries.length + value.invalidCount > value.scannedCount ||
    new TextEncoder().encode(JSON.stringify(value)).length > 64 * 1024
  )
    throw new Error('ACL历史分页无法核实，请重新读取')
  let previous = after
  for (const entry of value.entries) {
    if (
      !validContainerAclHistoryEntry(entry, target, path) ||
      entry.captureId <= previous ||
      (value.nextAfter !== '' && entry.captureId > value.nextAfter)
    )
      throw new Error('ACL历史记录无法核实，请重新读取')
    previous = entry.captureId
  }
  return value as unknown as ContainerAclHistoryPage
}
function sameEntry(value: ContainerAclHistoryEntry, expected: ContainerAclHistoryEntry) {
  return Object.entries(expected).every(
    ([key, field]) => value[key as keyof ContainerAclHistoryEntry] === field,
  )
}
export function assertContainerAclHistorySnapshot(
  value: unknown,
  target: HistoryTarget,
  path: string,
  expected: ContainerAclHistoryEntry,
): ContainerAclHistorySnapshot {
  if (
    !validContainerAclHistoryEntry(expected, target, path) ||
    !exact(value, ['location', 'entry', 'accessAcl', 'defaultAcl']) ||
    !location(value.location, target, path) ||
    !validContainerAclHistoryEntry(value.entry, target, path) ||
    !sameEntry(value.entry, expected) ||
    !validAclEntries(value.accessAcl) ||
    !validAclEntries(value.defaultAcl, true) ||
    value.accessAcl.length !== value.entry.accessEntryCount ||
    value.defaultAcl.length !== value.entry.defaultEntryCount ||
    aclMode(value.accessAcl) !== value.entry.originalMode ||
    new TextEncoder().encode(JSON.stringify(value)).length > 24 * 1024
  )
    throw new Error('ACL历史快照无法核实，请重新读取')
  return value as unknown as ContainerAclHistorySnapshot
}
export async function listContainerAclHistory(
  target: ContainerFileTarget,
  path: string,
  after: string,
  version: string,
  signal: AbortSignal,
) {
  if (
    !validContainerUploadLocation({
      containerId: target.containerId,
      instanceId: target.instanceId,
      path,
    }) ||
    (after !== '' && (!capture(after) || !hash.test(version)))
  )
    throw new Error('ACL历史范围无效')
  const response = await apiRequest<unknown>(
    `${containerFileBase(target.containerId)}/acls/history?${containerFileQuery(target, path, {
      after,
      take: '25',
      expectedVersion: version,
    })}`,
    { signal },
  )
  return assertContainerAclHistoryPage(response, target, path, after, version)
}
export async function readContainerAclHistory(
  target: ContainerFileTarget,
  path: string,
  entry: ContainerAclHistoryEntry,
  signal: AbortSignal,
) {
  if (!validContainerAclHistoryEntry(entry, target, path)) throw new Error('ACL历史选择无效')
  const response = await apiRequest<unknown>(
    `${containerFileBase(target.containerId)}/acls/history/${entry.captureId}?${containerFileQuery(
      target,
      path,
      { expectedHistoryDigest: entry.historyDigest },
    )}`,
    { signal },
  )
  return assertContainerAclHistorySnapshot(response, target, path, entry)
}
