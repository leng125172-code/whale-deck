import { apiRequest } from './apiClient'
import { containerFileBase, containerFileQuery } from './containerFiles'
import { validContainerUploadLocation } from './containerUploads'
import { aclMode, validAclEntries } from './containerAcls'
import type { ContainerAclEntry } from './containerAcls'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'

export interface ContainerRecursiveAclHistorySummary {
  captureId: string
  historyDigest: string
  createdAtUnixMs: number
  sourceInstanceId: string
  changedAclType: 'access' | 'default'
  manifestDigest: string
  entryCount: number
  fileCount: number
  directoryCount: number
  changeCount: number
}
export interface ContainerRecursiveAclHistoryPage {
  location: ContainerUploadLocation
  version: string
  entries: ContainerRecursiveAclHistorySummary[]
  nextAfter: string
  scannedCount: number
  invalidCount: number
  complete: boolean
}
export interface ContainerRecursiveAclHistoryEntry {
  relativePath: string
  kind: 'File' | 'Directory'
  originalMode: number
  accessAcl: ContainerAclEntry[]
  defaultAcl: ContainerAclEntry[]
}
export interface ContainerRecursiveAclHistorySnapshot {
  location: ContainerUploadLocation
  summary: ContainerRecursiveAclHistorySummary
  offset: number
  entries: ContainerRecursiveAclHistoryEntry[]
  complete: boolean
  nextOffset: number | null
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
// Native JSON and protobuf remain 128KiB. HTTP JSON may escape a path character
// into six ASCII bytes; keep this projection bounded without dropping legal names.
function withinBudget(value: unknown) {
  return new TextEncoder().encode(JSON.stringify(value)).length <= 1024 * 1024
}
export function validContainerRecursiveAclHistorySummary(
  value: unknown,
  target: HistoryTarget,
  path: string,
): value is ContainerRecursiveAclHistorySummary {
  return (
    exact(value, [
      'captureId',
      'historyDigest',
      'createdAtUnixMs',
      'sourceInstanceId',
      'changedAclType',
      'manifestDigest',
      'entryCount',
      'fileCount',
      'directoryCount',
      'changeCount',
    ]) &&
    capture(value.captureId) &&
    typeof value.historyDigest === 'string' &&
    hash.test(value.historyDigest) &&
    typeof value.manifestDigest === 'string' &&
    hash.test(value.manifestDigest) &&
    integer(value.createdAtUnixMs, 0, Number.MAX_SAFE_INTEGER) &&
    validContainerUploadLocation({
      containerId: target.containerId,
      instanceId: value.sourceInstanceId,
      path,
    }) &&
    (value.changedAclType === 'access' || value.changedAclType === 'default') &&
    integer(value.entryCount, 1, 10000) &&
    integer(value.fileCount, 0, 10000) &&
    integer(value.directoryCount, 1, 10000) &&
    integer(value.changeCount, 0, value.entryCount) &&
    value.entryCount === value.fileCount + value.directoryCount &&
    (value.changedAclType !== 'default' || value.changeCount <= value.directoryCount)
  )
}
export function sameContainerRecursiveAclHistorySummary(
  value: ContainerRecursiveAclHistorySummary,
  expected: ContainerRecursiveAclHistorySummary,
) {
  return Object.entries(expected).every(
    ([key, item]) => value[key as keyof ContainerRecursiveAclHistorySummary] === item,
  )
}
export function assertContainerRecursiveAclHistoryPage(
  value: unknown,
  target: HistoryTarget,
  path: string,
  after = '',
  version = '',
  take = 25,
): ContainerRecursiveAclHistoryPage {
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
    (version !== '' && value.version !== version) ||
    typeof value.nextAfter !== 'string' ||
    (value.nextAfter !== '' && (!capture(value.nextAfter) || value.nextAfter <= after)) ||
    typeof value.complete !== 'boolean' ||
    value.complete !== (value.nextAfter === '') ||
    !integer(value.scannedCount, 0, 200) ||
    !integer(value.invalidCount, 0, value.scannedCount) ||
    !Array.isArray(value.entries) ||
    value.entries.length > take ||
    value.entries.length + value.invalidCount > value.scannedCount ||
    !withinBudget(value)
  )
    throw new Error('递归ACL历史分页无法核实，请重新读取')
  let previous = after
  for (const entry of value.entries) {
    if (
      !validContainerRecursiveAclHistorySummary(entry, target, path) ||
      entry.captureId <= previous ||
      (value.nextAfter !== '' && entry.captureId > value.nextAfter)
    )
      throw new Error('递归ACL历史记录无法核实')
    previous = entry.captureId
  }
  return value as unknown as ContainerRecursiveAclHistoryPage
}
export function recursiveHistoryEntryOrder(left: string, right: string) {
  const depth = (value: string) => (value === '' ? 0 : value.split('/').length)
  const diff = depth(right) - depth(left)
  if (diff) return diff
  const a = new TextEncoder().encode(left),
    b = new TextEncoder().encode(right)
  for (let i = 0; i < Math.min(a.length, b.length); i++) if (a[i] !== b[i]) return a[i]! - b[i]!
  return a.length - b.length
}
export function assertContainerRecursiveAclHistorySnapshot(
  value: unknown,
  target: HistoryTarget,
  path: string,
  summary: ContainerRecursiveAclHistorySummary,
  offset: number,
  take = 25,
): ContainerRecursiveAclHistorySnapshot {
  if (
    !validContainerRecursiveAclHistorySummary(summary, target, path) ||
    !integer(offset, 0, 9999) ||
    !exact(value, ['location', 'summary', 'offset', 'entries', 'complete', 'nextOffset']) ||
    !location(value.location, target, path) ||
    !validContainerRecursiveAclHistorySummary(value.summary, target, path) ||
    !sameContainerRecursiveAclHistorySummary(value.summary, summary) ||
    !integer(value.offset, 0, 9999) ||
    value.offset !== offset ||
    !Array.isArray(value.entries) ||
    value.entries.length < 1 ||
    value.entries.length > take ||
    typeof value.complete !== 'boolean' ||
    offset + value.entries.length > summary.entryCount ||
    value.complete !== (offset + value.entries.length === summary.entryCount) ||
    (value.complete
      ? value.nextOffset !== null
      : !integer(value.nextOffset, 1, 9999) ||
        value.nextOffset !== offset + value.entries.length) ||
    !withinBudget(value)
  )
    throw new Error('递归ACL逐项前像无法核实，请重新读取')
  let previous: string | null = null
  for (let index = 0; index < value.entries.length; index++) {
    const entry = value.entries[index]
    if (
      !exact(entry, ['relativePath', 'kind', 'originalMode', 'accessAcl', 'defaultAcl']) ||
      typeof entry.relativePath !== 'string' ||
      !validContainerUploadLocation({
        containerId: target.containerId,
        instanceId: target.instanceId,
        path: path + (entry.relativePath ? '/' + entry.relativePath : ''),
      }) ||
      (entry.relativePath !== '' && entry.relativePath.split('/').length > 32) ||
      (entry.kind !== 'File' && entry.kind !== 'Directory') ||
      !integer(entry.originalMode, 0, 511) ||
      !validAclEntries(entry.accessAcl) ||
      !validAclEntries(entry.defaultAcl, true) ||
      aclMode(entry.accessAcl) !== entry.originalMode ||
      (entry.kind === 'File' && entry.defaultAcl.length !== 0) ||
      (entry.relativePath === '') !== (offset + index + 1 === summary.entryCount) ||
      (entry.relativePath === '' && entry.kind !== 'Directory') ||
      (previous !== null && recursiveHistoryEntryOrder(previous, entry.relativePath) >= 0)
    )
      throw new Error('递归ACL逐项前像无法核实')
    previous = entry.relativePath
  }
  return value as unknown as ContainerRecursiveAclHistorySnapshot
}
export async function listContainerRecursiveAclHistory(
  target: HistoryTarget,
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
    throw new Error('递归ACL历史范围无效')
  const response = await apiRequest<unknown>(
    `${containerFileBase(target.containerId)}/recursive-acls/history?${containerFileQuery(target, path, { after, take: '25', expectedVersion: version })}`,
    { signal },
  )
  return assertContainerRecursiveAclHistoryPage(response, target, path, after, version)
}
export async function readContainerRecursiveAclHistory(
  target: HistoryTarget,
  path: string,
  summary: ContainerRecursiveAclHistorySummary,
  offset: number,
  signal: AbortSignal,
) {
  if (
    !validContainerRecursiveAclHistorySummary(summary, target, path) ||
    !integer(offset, 0, summary.entryCount - 1)
  )
    throw new Error('递归ACL历史选择无效')
  const response = await apiRequest<unknown>(
    `${containerFileBase(target.containerId)}/recursive-acls/history/${summary.captureId}?${containerFileQuery(target, path, { expectedHistoryDigest: summary.historyDigest, offset: String(offset), take: '25' })}`,
    { signal },
  )
  return assertContainerRecursiveAclHistorySnapshot(response, target, path, summary, offset)
}
