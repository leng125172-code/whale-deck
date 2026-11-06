import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import { validRecycleId } from './containerRecycles'

export interface RecycleArchiveSource {
  sourceIntentId: string
  purgeOperationId: string
  sourceOperationId: string
  location: ContainerUploadLocation
  originalUid: number
  originalGid: number
  originalMode: number
  sizeBytes: number
  sha256: string
  attributeCount: number
  historyDigest: string
  completedAtUtc: string
}
export interface RecycleArchiveReceipt {
  id: string
  purgeOperationId: string
  sourceOperationId: string
  fingerprint: string
  state: 'Completed' | 'Aborted' | 'Unknown'
  sourceFenced: boolean
  ownedFence: boolean
  archiveCopied: boolean | null
  bindingCurrent: boolean | null
  publishedIdentity: {
    device: string
    inode: string
    mode: number
    ownerUid: number
    ownerGid: number
    linkCount: string
    sizeBytes: number
    modifiedAtUnixNs: string
  } | null
  publishedChangedAtUnixNs: string | null
}
export interface RecycleArchiveIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  sourceIntentId: string
  purgeOperationId: string
  sourceOperationId: string
  sourceHistoryDigest: string
  location: ContainerUploadLocation
  state: string
  originalUid: number | null
  originalGid: number | null
  originalMode: number | null
  executorUid: number | null
  executorGid: number | null
  sizeBytes: number
  sha256: string
  attributeCount: number
  readyExpiresAtUtc: string | null
  resultCode: string | null
  version: number
  reservationRecorded: boolean
  archiveCopied: boolean | null
  receipt: RecycleArchiveReceipt | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
const unsigned = (v: unknown, max = 18446744073709551615n): v is string =>
  typeof v === 'string' && /^(0|[1-9][0-9]{0,19})$/.test(v) && BigInt(v) <= max
const signed = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^(0|-?[1-9][0-9]{0,18})$/.test(v) &&
  BigInt(v) >= -9223372036854775808n &&
  BigInt(v) <= 9223372036854775807n
const hash = /^[A-F0-9]{64}$/
const date = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= 64 && Number.isFinite(Date.parse(v))
const exact = (v: object, keys: string) =>
  !!v &&
  Object.keys(v).length === keys.split(' ').length &&
  keys.split(' ').every((k) => Object.prototype.hasOwnProperty.call(v, k))
const states = [
  'Queued',
  'Planning',
  'Ready',
  'Pending',
  'Reserving',
  'Applying',
  'Unknown',
  'Succeeded',
  'Canceled',
  'Failed',
  'Expired',
  'Acknowledged',
]
export const archiveTerminal = (state: string) =>
  ['Succeeded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(state)
export function archiveState(state: string) {
  return (
    (
      {
        Queued: '等待归档预检',
        Planning: '正在核验清理证明与HDD备份',
        Ready: '归档预检完成，尚未复制',
        Pending: '等待归档任务',
        Reserving: '正在预留归档复制保护',
        Applying: '正在从归档复制恢复',
        Unknown: '归档结果未知，保护已保留',
        Succeeded: '本次归档已复制恢复',
        Canceled: '归档恢复已取消',
        Failed: '归档恢复未完成',
        Expired: '归档预检已过期',
        Acknowledged: '已处置未知归档历史，不代表复制成功',
      } as Record<string, string>
    )[state] ?? '无法核实的归档状态'
  )
}
function metadata(v: { sizeBytes: number; sha256: string; attributeCount: number }) {
  return (
    Number.isSafeInteger(v.sizeBytes) &&
    v.sizeBytes >= 0 &&
    v.sizeBytes <= 1073741824 &&
    typeof v.sha256 === 'string' &&
    hash.test(v.sha256) &&
    Number.isInteger(v.attributeCount) &&
    v.attributeCount >= 0 &&
    v.attributeCount <= 32
  )
}
export function assertArchiveSource(v: RecycleArchiveSource, cid: string) {
  if (
    !v ||
    !exact(
      v,
      'sourceIntentId purgeOperationId sourceOperationId location originalUid originalGid originalMode sizeBytes sha256 attributeCount historyDigest completedAtUtc',
    ) ||
    !validDirectoryId(v.sourceIntentId) ||
    !validDirectoryId(v.sourceOperationId) ||
    !validDirectoryId(v.purgeOperationId) ||
    v.purgeOperationId === v.sourceOperationId ||
    v.sourceIntentId === v.sourceOperationId ||
    v.sourceIntentId === v.purgeOperationId ||
    !validContainerUploadLocation(v.location) ||
    v.location.containerId !== cid ||
    !metadata(v) ||
    !validRecycleId(v.originalUid) ||
    !validRecycleId(v.originalGid) ||
    !Number.isSafeInteger(v.originalMode) ||
    v.originalMode < 32768 ||
    v.originalMode > 33279 ||
    typeof v.historyDigest !== 'string' ||
    !hash.test(v.historyDigest) ||
    !date(v.completedAtUtc)
  )
    throw new Error('成功归档来源摘要无法核实')
  return v
}
function receipt(v: RecycleArchiveIntent) {
  const r = v.receipt
  if (
    !r ||
    !exact(
      r,
      'id sourceOperationId purgeOperationId fingerprint state sourceFenced ownedFence archiveCopied bindingCurrent publishedIdentity publishedChangedAtUnixNs',
    ) ||
    r.id !== v.executionJobId ||
    r.sourceOperationId !== v.sourceOperationId ||
    r.purgeOperationId !== v.purgeOperationId ||
    typeof r.fingerprint !== 'string' ||
    !hash.test(r.fingerprint) ||
    !['Completed', 'Aborted', 'Unknown'].includes(r.state) ||
    typeof r.sourceFenced !== 'boolean' ||
    typeof r.ownedFence !== 'boolean' ||
    (r.ownedFence && !r.sourceFenced) ||
    r.archiveCopied !== v.archiveCopied
  )
    return false
  if (
    ![null, true, false].includes(r.bindingCurrent) ||
    (r.bindingCurrent === true && (r.state !== 'Completed' || !r.ownedFence))
  )
    return false
  if (r.state === 'Completed') {
    const i = r.publishedIdentity
    return (
      r.sourceFenced &&
      r.archiveCopied === true &&
      !!i &&
      exact(i, 'device inode mode ownerUid ownerGid linkCount sizeBytes modifiedAtUnixNs') &&
      unsigned(i.device) &&
      unsigned(i.inode) &&
      i.inode !== '0' &&
      i.linkCount === '1' &&
      i.mode === v.originalMode &&
      i.ownerUid === v.originalUid &&
      i.ownerGid === v.originalGid &&
      i.sizeBytes === v.sizeBytes &&
      signed(i.modifiedAtUnixNs) &&
      unsigned(r.publishedChangedAtUnixNs, 9223372036854775807n)
    )
  }
  if (r.publishedIdentity !== null || r.publishedChangedAtUnixNs !== null) return false
  if (r.state === 'Unknown') return r.sourceFenced && r.archiveCopied === null
  return !r.ownedFence && r.archiveCopied === false
}
export function assertArchiveIntent(
  v: RecycleArchiveIntent,
  cid?: string,
  id?: string,
  sourceId?: string,
  source?: RecycleArchiveSource,
) {
  if (
    !v ||
    !exact(
      v,
      'id planningJobId executionJobId sourceIntentId purgeOperationId sourceOperationId sourceHistoryDigest location state originalUid originalGid originalMode executorUid executorGid sizeBytes sha256 attributeCount readyExpiresAtUtc resultCode version reservationRecorded archiveCopied receipt confirmation',
    ) ||
    !validDirectoryId(v.id) ||
    v.planningJobId !== v.id ||
    !validDirectoryId(v.sourceIntentId) ||
    !validDirectoryId(v.sourceOperationId) ||
    !validDirectoryId(v.purgeOperationId) ||
    v.purgeOperationId === v.sourceOperationId ||
    v.sourceIntentId === v.purgeOperationId ||
    v.sourceIntentId === v.sourceOperationId ||
    v.id === v.sourceIntentId ||
    v.id === v.purgeOperationId ||
    v.id === v.sourceOperationId ||
    typeof v.sourceHistoryDigest !== 'string' ||
    !hash.test(v.sourceHistoryDigest) ||
    !validContainerUploadLocation(v.location) ||
    (cid && v.location.containerId !== cid) ||
    (id && v.id !== id) ||
    (sourceId && v.sourceIntentId !== sourceId) ||
    !metadata(v) ||
    !states.includes(v.state) ||
    !Number.isSafeInteger(v.version) ||
    v.version < 0 ||
    (v.executionJobId !== null &&
      (!validDirectoryId(v.executionJobId) ||
        v.executionJobId === v.id ||
        v.executionJobId === v.sourceOperationId ||
        v.executionJobId === v.purgeOperationId)) ||
    typeof v.reservationRecorded !== 'boolean' ||
    ![null, true, false].includes(v.archiveCopied) ||
    (v.resultCode !== null &&
      (typeof v.resultCode !== 'string' ||
        !/^CONTAINER_RECYCLE_ARCHIVE_[A-Z_]{1,48}$/.test(v.resultCode)))
  )
    throw new Error('归档恢复任务的身份或摘要无法核实')
  if (
    source &&
    (v.sourceIntentId !== source.sourceIntentId ||
      v.sourceOperationId !== source.sourceOperationId ||
      v.purgeOperationId !== source.purgeOperationId ||
      JSON.stringify(v.location) !== JSON.stringify(source.location) ||
      v.sourceHistoryDigest !== source.historyDigest ||
      v.sha256 !== source.sha256 ||
      v.sizeBytes !== source.sizeBytes ||
      v.attributeCount !== source.attributeCount)
  )
    throw new Error('归档任务替换了原来源')
  const planned = v.readyExpiresAtUtc !== null,
    values = [v.originalUid, v.originalGid, v.executorUid, v.executorGid]
  if (
    source &&
    planned &&
    (v.originalUid !== source.originalUid ||
      v.originalGid !== source.originalGid ||
      v.originalMode !== source.originalMode)
  )
    throw new Error('归档预检替换了原来源身份')
  if (
    planned
      ? !date(v.readyExpiresAtUtc) ||
        !values.every(validRecycleId) ||
        !Number.isSafeInteger(v.originalMode) ||
        v.originalMode! < 32768 ||
        v.originalMode! > 33279
      : [...values, v.originalMode].some((x) => x !== null)
  )
    throw new Error('归档预检身份无法核实')
  if (
    (['Queued', 'Planning'].includes(v.state) && (planned || v.executionJobId !== null)) ||
    (['Ready', 'Pending', 'Reserving', 'Applying', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      v.state,
    ) &&
      !planned) ||
    (['Pending', 'Reserving', 'Applying', 'Unknown', 'Succeeded', 'Acknowledged'].includes(
      v.state,
    ) &&
      v.executionJobId === null) ||
    (['Ready', 'Expired'].includes(v.state) && v.executionJobId !== null) ||
    (v.executionJobId === null &&
      (v.reservationRecorded || v.archiveCopied !== null || v.receipt !== null)) ||
    (v.reservationRecorded && v.executionJobId === null) ||
    (v.state === 'Applying' && !v.reservationRecorded) ||
    (v.receipt !== null && !receipt(v)) ||
    ((archiveTerminal(v.state) || v.state === 'Unknown') && v.resultCode === null) ||
    (['Succeeded', 'Acknowledged'].includes(v.state) && v.receipt === null) ||
    (v.state === 'Succeeded' &&
      !(v.receipt?.state === 'Completed' && v.archiveCopied === true && v.reservationRecorded)) ||
    (v.state === 'Unknown' &&
      (v.archiveCopied !== null || (v.receipt !== null && v.receipt.state !== 'Unknown'))) ||
    (v.executionJobId !== null &&
      ['Canceled', 'Failed'].includes(v.state) &&
      v.receipt?.state !== 'Aborted') ||
    (v.receipt === null && v.archiveCopied !== null)
  )
    throw new Error('归档结果或可空事实无法核实')
  const c = v.confirmation
  if (
    v.state === 'Ready'
      ? !c ||
        !exact(c, 'planHash expiresAtUtc warning') ||
        typeof c.planHash !== 'string' ||
        !c.planHash ||
        c.planHash.length > 4096 ||
        !date(c.expiresAtUtc) ||
        c.expiresAtUtc !== v.readyExpiresAtUtc ||
        typeof c.warning !== 'string' ||
        c.warning.length > 2048
      : c !== null
  )
    throw new Error('归档恢复确认无法核实')
  return v
}
export function assertArchiveAdvance(a: RecycleArchiveIntent | null, b: RecycleArchiveIntent) {
  assertArchiveIntent(b)
  if (!a) return
  const expired = a.state === 'Ready' && b.state === 'Expired' && b.executionJobId === null
  const norm = (v: RecycleArchiveIntent) =>
    JSON.stringify({ ...v, ...(expired ? { state: 'Expired', confirmation: null } : {}) })
  const origin = (v: RecycleArchiveIntent) =>
    JSON.stringify([
      v.id,
      v.sourceIntentId,
      v.sourceOperationId,
      v.purgeOperationId,
      v.sourceHistoryDigest,
      v.location,
      v.sizeBytes,
      v.sha256,
      v.attributeCount,
    ])
  const plan = (v: RecycleArchiveIntent) =>
    JSON.stringify([
      v.originalUid,
      v.originalGid,
      v.originalMode,
      v.executorUid,
      v.executorGid,
      v.readyExpiresAtUtc,
    ])
  const fact = (r: RecycleArchiveReceipt) =>
    JSON.stringify([
      r.id,
      r.sourceOperationId,
      r.purgeOperationId,
      r.fingerprint,
      r.state,
      r.archiveCopied,
      r.publishedIdentity,
      r.publishedChangedAtUnixNs,
    ])
  if (
    origin(a) !== origin(b) ||
    b.version < a.version ||
    (a.executionJobId !== null && a.executionJobId !== b.executionJobId) ||
    (a.readyExpiresAtUtc !== null && plan(a) !== plan(b)) ||
    (archiveTerminal(a.state) && norm(a) !== norm(b)) ||
    (a.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(b.state)) ||
    (['Reserving', 'Applying'].includes(a.state) && b.state === 'Pending') ||
    (a.reservationRecorded && !b.reservationRecorded) ||
    (a.archiveCopied !== null && a.archiveCopied !== b.archiveCopied) ||
    (a.receipt && (!b.receipt || fact(a.receipt) !== fact(b.receipt))) ||
    (a.state === 'Ready' &&
      b.state === 'Ready' &&
      JSON.stringify(a.confirmation) !== JSON.stringify(b.confirmation)) ||
    (a.version === b.version && norm(a) !== norm(b))
  )
    throw new Error('原归档任务的事实或版本发生变化')
}
export async function listArchiveSources(
  cid: string,
  signal: AbortSignal,
  after: string | null = null,
) {
  if (after !== null && !validDirectoryId(after)) throw new Error('归档来源游标无效')
  const p = await apiRequest<{ items: RecycleArchiveSource[]; next: string | null }>(
    `${containerFileBase(cid)}/recycle-archives/sources?take=25${after ? `&after=${after}` : ''}`,
    { signal },
  )
  if (
    !p ||
    !exact(p, 'items next') ||
    !Array.isArray(p.items) ||
    p.items.length > 25 ||
    new Set(p.items.map((v) => v.sourceIntentId)).size !== p.items.length ||
    (p.next !== null &&
      (!validDirectoryId(p.next) ||
        p.items.length !== 25 ||
        p.next !== p.items[p.items.length - 1]?.sourceIntentId ||
        p.next === after))
  )
    throw new Error('归档来源分页无法核实')
  p.items.forEach((v) => assertArchiveSource(v, cid))
  return p
}
export async function queueArchive(
  cid: string,
  sourceId: string,
  key: string,
  signal: AbortSignal,
) {
  if (!validDirectoryId(sourceId) || !validDirectoryId(key))
    throw new Error('来源清理引用或登记键无效')
  return assertArchiveIntent(
    await apiRequest<RecycleArchiveIntent>(`${containerFileBase(cid)}/recycle-archives`, {
      method: 'POST',
      headers: { 'Idempotency-Key': key },
      body: JSON.stringify({ sourceIntentId: sourceId }),
      signal,
    }),
    cid,
    undefined,
    sourceId,
  )
}
export async function getArchive(cid: string, id: string, signal: AbortSignal, byKey = false) {
  if (!validDirectoryId(id)) throw new Error('归档恢复引用无效')
  return assertArchiveIntent(
    await apiRequest<RecycleArchiveIntent>(
      `${containerFileBase(cid)}/recycle-archives/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    cid,
    byKey ? undefined : id,
  )
}
export async function confirmArchive(row: RecycleArchiveIntent, signal: AbortSignal) {
  assertArchiveIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('归档预检未核实或已过期')
  return assertArchiveIntent(
    await apiRequest<RecycleArchiveIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-archives/${row.id}/confirm`,
      {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        body: JSON.stringify({
          planHash: row.confirmation.planHash,
          confirmed: true,
          version: row.version,
        }),
        signal,
      },
    ),
    row.location.containerId,
    row.id,
    row.sourceIntentId,
  )
}
export async function cancelArchive(row: RecycleArchiveIntent, signal: AbortSignal) {
  assertArchiveIntent(row)
  return assertArchiveIntent(
    await apiRequest<RecycleArchiveIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-archives/${row.id}/cancel`,
      { method: 'POST', headers: { 'Idempotency-Key': row.id }, signal },
    ),
    row.location.containerId,
    row.id,
    row.sourceIntentId,
  )
}
export const archiveRecoveryBase = 'files/container-recycle-archives/recovery'
export function currentArchiveInstance(row: RecycleArchiveIntent, target: ContainerFileTarget) {
  return (
    row.location.containerId === target.containerId && row.location.instanceId === target.instanceId
  )
}
