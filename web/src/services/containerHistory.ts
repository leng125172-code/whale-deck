import { apiRequest } from '@/services/apiClient'
import { containerFileBase, containerFileQuery } from '@/services/containerFiles'
import type { ContainerFileTarget } from '@/services/containerFiles'
import type { FilePlan } from '@/services/fileManagement'
import {
  assertContainerUploadIntent,
  assertContainerUploadPlan,
  validContainerUploadLocation,
} from '@/services/containerUploads'
import type {
  ContainerUploadIntent,
  ContainerUploadRegistration,
} from '@/services/containerUploads'

export interface ContainerHistoryEntry {
  captureId: string
  containerId: string
  originalInstanceId: string
  path: string
  sourceVersion: string
  sizeBytes: number
  sha256: string
  createdAtUnixMs: number
  differentSourceDevice: boolean
  availability: 'Present' | 'Incomplete' | 'Missing' | 'Invalid'
  captureHash: string
}
export interface ContainerHistoryPage {
  location: { containerId: string; instanceId: string; path: string }
  targetProof: string
  entries: ContainerHistoryEntry[]
  version: string
  nextCursor: string
  scanned: number
  invalidRecords: number
  complete: boolean
}
const hash = /^[A-F0-9]{64}$/
const capture = /^[a-f0-9]{32}$/
const uuid = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/
function exact(value: unknown, keys: string[]): value is Record<string, unknown> {
  return (
    !!value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === keys.length &&
    Object.keys(value).every((key) => keys.includes(key))
  )
}
function validCapture(value: unknown): value is string {
  return typeof value === 'string' && capture.test(value) && value !== '0'.repeat(32)
}
export function validContainerHistoryEntry(value: unknown): value is ContainerHistoryEntry {
  if (
    !exact(value, [
      'captureId',
      'containerId',
      'originalInstanceId',
      'path',
      'sourceVersion',
      'sizeBytes',
      'sha256',
      'createdAtUnixMs',
      'differentSourceDevice',
      'availability',
      'captureHash',
    ])
  )
    return false
  return (
    validCapture(value.captureId) &&
    validContainerUploadLocation({
      containerId: value.containerId,
      instanceId: value.originalInstanceId,
      path: value.path,
    }) &&
    typeof value.sourceVersion === 'string' &&
    hash.test(value.sourceVersion) &&
    typeof value.sha256 === 'string' &&
    hash.test(value.sha256) &&
    typeof value.captureHash === 'string' &&
    hash.test(value.captureHash) &&
    Number.isSafeInteger(value.sizeBytes) &&
    Number(value.sizeBytes) >= 0 &&
    Number(value.sizeBytes) <= 1073741824 &&
    Number.isSafeInteger(value.createdAtUnixMs) &&
    Number(value.createdAtUnixMs) >= 0 &&
    Number(value.createdAtUnixMs) <= 253402300799999 &&
    typeof value.differentSourceDevice === 'boolean' &&
    typeof value.availability === 'string' &&
    ['Present', 'Incomplete', 'Missing', 'Invalid'].includes(value.availability)
  )
}
export function assertContainerHistoryPage(
  value: unknown,
  target: ContainerFileTarget,
  path: string,
  after = '',
  expectedVersion = '',
): ContainerHistoryPage {
  if (
    !exact(value, [
      'location',
      'targetProof',
      'entries',
      'version',
      'nextCursor',
      'scanned',
      'invalidRecords',
      'complete',
    ]) ||
    !exact(value.location, ['containerId', 'instanceId', 'path']) ||
    value.location.containerId !== target.containerId ||
    value.location.instanceId !== target.instanceId ||
    value.location.path !== path ||
    !validContainerUploadLocation({ ...value.location, path: path || 'history-scope.bin' }) ||
    typeof value.targetProof !== 'string' ||
    !hash.test(value.targetProof) ||
    typeof value.version !== 'string' ||
    !hash.test(value.version) ||
    (expectedVersion !== '' && value.version !== expectedVersion) ||
    typeof value.nextCursor !== 'string' ||
    (value.nextCursor !== '' && (!validCapture(value.nextCursor) || value.nextCursor <= after)) ||
    typeof value.complete !== 'boolean' ||
    value.complete !== (value.nextCursor === '') ||
    !Array.isArray(value.entries) ||
    value.entries.length > 50 ||
    !Number.isSafeInteger(value.scanned) ||
    Number(value.scanned) < 0 ||
    Number(value.scanned) > 500 ||
    !Number.isSafeInteger(value.invalidRecords) ||
    Number(value.invalidRecords) < 0 ||
    value.entries.length + Number(value.invalidRecords) > Number(value.scanned) ||
    new TextEncoder().encode(JSON.stringify(value.entries)).length > 48 * 1024
  )
    throw new Error('容器历史范围或分页无法核实，请刷新')
  let previous = after
  for (const entry of value.entries) {
    if (
      !validContainerHistoryEntry(entry) ||
      entry.containerId !== target.containerId ||
      (path !== '' && entry.path !== path) ||
      entry.captureId <= previous ||
      (value.nextCursor !== '' && entry.captureId > value.nextCursor)
    )
      throw new Error('容器历史记录发生变化，请刷新')
    previous = entry.captureId
  }
  return value as unknown as ContainerHistoryPage
}
export async function listContainerHistory(
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
      path: path || 'history-scope.bin',
    }) ||
    (after !== '' && (!validCapture(after) || !hash.test(version)))
  )
    throw new Error('容器历史范围无效')
  const value = await apiRequest<unknown>(
    `${containerFileBase(target.containerId)}/history?${containerFileQuery(target, path, { after, take: '50', expectedVersion: version })}`,
    { signal },
  )
  return assertContainerHistoryPage(value, target, path, after, version)
}
function source(target: ContainerFileTarget, entry: ContainerHistoryEntry) {
  if (
    !validContainerHistoryEntry(entry) ||
    entry.availability !== 'Present' ||
    entry.containerId !== target.containerId
  )
    throw new Error('该历史版本不可恢复')
}
export function assertContainerRestorePlan(value: unknown, restoreAttributes: boolean): FilePlan {
  if (!restoreAttributes) return assertContainerUploadPlan(value as FilePlan)
  if (
    !exact(value, ['planHash', 'expiresAtUtc', 'overwrites', 'warning', 'restoreAttributes']) ||
    value.restoreAttributes !== true
  )
    throw new Error('恢复计划未确认属性策略，请升级服务或重新核对')
  return assertContainerUploadPlan({
    planHash: value.planHash,
    expiresAtUtc: value.expiresAtUtc,
    overwrites: value.overwrites,
    warning: value.warning,
  } as FilePlan)
}
export async function planContainerRestore(
  target: ContainerFileTarget,
  entry: ContainerHistoryEntry,
  destination: string,
  expectedVersion: string,
  signal: AbortSignal,
  restoreAttributes = false,
) {
  source(target, entry)
  if (
    !validContainerUploadLocation({
      containerId: target.containerId,
      instanceId: target.instanceId,
      path: destination,
    })
  )
    throw new Error('恢复目标路径无效')
  if (expectedVersion !== 'ABSENT' && !hash.test(expectedVersion))
    throw new Error('当前文件版本无法核实')
  return assertContainerRestorePlan(
    await apiRequest<FilePlan>(`${containerFileBase(target.containerId)}/restores/plan`, {
      method: 'POST',
      signal,
      body: JSON.stringify({
        instanceId: target.instanceId,
        path: destination,
        captureId: entry.captureId,
        expectedCaptureHash: entry.captureHash,
        expectedVersion,
        ...(restoreAttributes ? { restoreAttributes: true } : {}),
      }),
    }),
    restoreAttributes,
  )
}
export async function createContainerRestore(
  target: ContainerFileTarget,
  entry: ContainerHistoryEntry,
  destination: string,
  registration: ContainerUploadRegistration,
  signal: AbortSignal,
  restoreAttributes = false,
) {
  source(target, entry)
  return assertContainerUploadIntent(
    await apiRequest<ContainerUploadIntent>(`${containerFileBase(target.containerId)}/restores`, {
      method: 'POST',
      signal,
      headers: { 'Idempotency-Key': registration.idempotencyKey },
      body: JSON.stringify(registration),
    }),
    {
      target,
      path: destination,
      sizeBytes: entry.sizeBytes,
      sha256: entry.sha256,
      restoreAttributes,
    },
  )
}
export async function getContainerRestore(
  target: ContainerFileTarget,
  entry: ContainerHistoryEntry,
  destination: string,
  id: string,
  signal: AbortSignal,
  restoreAttributes = false,
) {
  source(target, entry)
  if (!uuid.test(id) || id === '00000000-0000-0000-0000-000000000000')
    throw new Error('恢复任务编号无效')
  return assertContainerUploadIntent(
    await apiRequest<ContainerUploadIntent>(
      `${containerFileBase(target.containerId)}/restores/${id}`,
      { signal },
    ),
    {
      target,
      path: destination,
      sizeBytes: entry.sizeBytes,
      sha256: entry.sha256,
      restoreAttributes,
    },
  )
}
export async function cancelContainerRestore(
  target: ContainerFileTarget,
  id: string,
  signal: AbortSignal,
) {
  if (!uuid.test(id) || id === '00000000-0000-0000-0000-000000000000')
    throw new Error('恢复任务编号无效')
  await apiRequest(`${containerFileBase(target.containerId)}/restores/${id}/cancel`, {
    method: 'POST',
    signal,
    headers: { 'Idempotency-Key': id },
  })
}
