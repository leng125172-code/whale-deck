import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import { validRecycleId } from './containerRecycles'

export interface RecyclePurgeSource {
  sourceIntentId: string
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
export interface RecyclePurgeReceipt {
  id: string
  sourceOperationId: string
  fingerprint: string
  state: 'Completed' | 'Aborted' | 'Unknown'
  sourceFenced: boolean
  ownedFence: boolean
  purgeApplied: boolean | null
}
export interface RecyclePurgeIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  sourceIntentId: string
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
  purgeApplied: boolean | null
  receipt: RecyclePurgeReceipt | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
}
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
export const purgeTerminal = (state: string) =>
  ['Succeeded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(state)
export function purgeState(state: string) {
  return (
    (
      {
        Queued: '等待清理预检',
        Planning: '正在核验留存原件与备份',
        Ready: '清理预检完成，尚未删除',
        Pending: '等待清理任务',
        Reserving: '正在预留原件保护',
        Applying: '正在清理留存原件',
        Unknown: '清理结果未知，保护已保留',
        Succeeded: '本次留存原件已清理',
        Canceled: '清理已取消',
        Failed: '清理未完成',
        Expired: '清理预检已过期',
        Acknowledged: '已处置未知清理历史，不代表清理成功',
      } as Record<string, string>
    )[state] ?? '无法核实的清理状态'
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
export function assertPurgeSource(v: RecyclePurgeSource, cid: string) {
  if (
    !v ||
    !exact(
      v,
      'sourceIntentId sourceOperationId location originalUid originalGid originalMode sizeBytes sha256 attributeCount historyDigest completedAtUtc',
    ) ||
    !validDirectoryId(v.sourceIntentId) ||
    !validDirectoryId(v.sourceOperationId) ||
    v.sourceIntentId === v.sourceOperationId ||
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
    throw new Error('原回收来源摘要无法核实')
  return v
}
function receipt(v: RecyclePurgeIntent) {
  const r = v.receipt
  if (
    !r ||
    !exact(r, 'id sourceOperationId fingerprint state sourceFenced ownedFence purgeApplied') ||
    r.id !== v.executionJobId ||
    r.sourceOperationId !== v.sourceOperationId ||
    typeof r.fingerprint !== 'string' ||
    !hash.test(r.fingerprint) ||
    !['Completed', 'Aborted', 'Unknown'].includes(r.state) ||
    typeof r.sourceFenced !== 'boolean' ||
    typeof r.ownedFence !== 'boolean' ||
    (r.ownedFence && !r.sourceFenced) ||
    r.purgeApplied !== v.purgeApplied
  )
    return false
  if (r.state === 'Completed') return r.sourceFenced && r.ownedFence && r.purgeApplied === true
  if (r.state === 'Unknown') return r.sourceFenced && r.purgeApplied === null
  return !r.ownedFence && r.purgeApplied === false
}
export function assertPurgeIntent(
  v: RecyclePurgeIntent,
  cid?: string,
  id?: string,
  sourceId?: string,
  source?: RecyclePurgeSource,
) {
  if (
    !v ||
    !exact(
      v,
      'id planningJobId executionJobId sourceIntentId sourceOperationId sourceHistoryDigest location state originalUid originalGid originalMode executorUid executorGid sizeBytes sha256 attributeCount readyExpiresAtUtc resultCode version reservationRecorded purgeApplied receipt confirmation',
    ) ||
    !validDirectoryId(v.id) ||
    v.planningJobId !== v.id ||
    !validDirectoryId(v.sourceIntentId) ||
    !validDirectoryId(v.sourceOperationId) ||
    v.id === v.sourceIntentId ||
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
        v.executionJobId === v.sourceOperationId)) ||
    typeof v.reservationRecorded !== 'boolean' ||
    ![null, true, false].includes(v.purgeApplied) ||
    (v.resultCode !== null &&
      (typeof v.resultCode !== 'string' ||
        !/^CONTAINER_RECYCLE_PURGE_[A-Z_]{1,48}$/.test(v.resultCode)))
  )
    throw new Error('留存清理任务的身份或摘要无法核实')
  if (
    source &&
    (v.sourceIntentId !== source.sourceIntentId ||
      v.sourceOperationId !== source.sourceOperationId ||
      JSON.stringify(v.location) !== JSON.stringify(source.location) ||
      v.sourceHistoryDigest !== source.historyDigest ||
      v.sha256 !== source.sha256 ||
      v.sizeBytes !== source.sizeBytes ||
      v.attributeCount !== source.attributeCount)
  )
    throw new Error('清理任务替换了原来源')
  const planned = v.readyExpiresAtUtc !== null,
    values = [v.originalUid, v.originalGid, v.executorUid, v.executorGid]
  if (
    source &&
    planned &&
    (v.originalUid !== source.originalUid ||
      v.originalGid !== source.originalGid ||
      v.originalMode !== source.originalMode)
  )
    throw new Error('清理预检替换了原来源身份')
  if (
    planned
      ? !date(v.readyExpiresAtUtc) ||
        !values.every(validRecycleId) ||
        !Number.isSafeInteger(v.originalMode) ||
        v.originalMode! < 32768 ||
        v.originalMode! > 33279
      : [...values, v.originalMode].some((x) => x !== null)
  )
    throw new Error('清理预检身份无法核实')
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
      (v.reservationRecorded || v.purgeApplied !== null || v.receipt !== null)) ||
    (v.reservationRecorded && v.executionJobId === null) ||
    (v.state === 'Applying' && !v.reservationRecorded) ||
    (v.receipt !== null && !receipt(v)) ||
    ((purgeTerminal(v.state) || v.state === 'Unknown') && v.resultCode === null) ||
    (['Succeeded', 'Acknowledged'].includes(v.state) && v.receipt === null) ||
    (v.state === 'Succeeded' &&
      !(v.receipt?.state === 'Completed' && v.purgeApplied === true && v.reservationRecorded)) ||
    (v.state === 'Unknown' &&
      (v.purgeApplied !== null || (v.receipt !== null && v.receipt.state !== 'Unknown'))) ||
    (v.executionJobId !== null &&
      ['Canceled', 'Failed'].includes(v.state) &&
      v.receipt?.state !== 'Aborted') ||
    (v.receipt === null && v.purgeApplied !== null)
  )
    throw new Error('清理结果或可空事实无法核实')
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
    throw new Error('留存清理确认无法核实')
  return v
}
export function assertPurgeAdvance(a: RecyclePurgeIntent | null, b: RecyclePurgeIntent) {
  assertPurgeIntent(b)
  if (!a) return
  const expired = a.state === 'Ready' && b.state === 'Expired' && b.executionJobId === null
  const norm = (v: RecyclePurgeIntent) =>
    JSON.stringify({ ...v, ...(expired ? { state: 'Expired', confirmation: null } : {}) })
  const origin = (v: RecyclePurgeIntent) =>
    JSON.stringify([
      v.id,
      v.sourceIntentId,
      v.sourceOperationId,
      v.sourceHistoryDigest,
      v.location,
      v.sizeBytes,
      v.sha256,
      v.attributeCount,
    ])
  const plan = (v: RecyclePurgeIntent) =>
    JSON.stringify([
      v.originalUid,
      v.originalGid,
      v.originalMode,
      v.executorUid,
      v.executorGid,
      v.readyExpiresAtUtc,
    ])
  const fact = (r: RecyclePurgeReceipt) =>
    JSON.stringify([r.id, r.sourceOperationId, r.fingerprint, r.state, r.purgeApplied])
  if (
    origin(a) !== origin(b) ||
    b.version < a.version ||
    (a.executionJobId !== null && a.executionJobId !== b.executionJobId) ||
    (a.readyExpiresAtUtc !== null && plan(a) !== plan(b)) ||
    (purgeTerminal(a.state) && norm(a) !== norm(b)) ||
    (a.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(b.state)) ||
    (['Reserving', 'Applying'].includes(a.state) && b.state === 'Pending') ||
    (a.reservationRecorded && !b.reservationRecorded) ||
    (a.purgeApplied !== null && a.purgeApplied !== b.purgeApplied) ||
    (a.receipt && (!b.receipt || fact(a.receipt) !== fact(b.receipt))) ||
    (a.state === 'Ready' &&
      b.state === 'Ready' &&
      JSON.stringify(a.confirmation) !== JSON.stringify(b.confirmation)) ||
    (a.version === b.version && norm(a) !== norm(b))
  )
    throw new Error('原清理任务的事实或版本发生变化')
}
export async function listPurgeSources(
  cid: string,
  signal: AbortSignal,
  after: string | null = null,
) {
  if (after !== null && !validDirectoryId(after)) throw new Error('清理来源游标无效')
  const p = await apiRequest<{ items: RecyclePurgeSource[]; next: string | null }>(
    `${containerFileBase(cid)}/recycle-purges/sources?take=25${after ? `&after=${after}` : ''}`,
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
    throw new Error('清理来源分页无法核实')
  p.items.forEach((v) => assertPurgeSource(v, cid))
  return p
}
export async function queuePurge(cid: string, sourceId: string, key: string, signal: AbortSignal) {
  if (!validDirectoryId(sourceId) || !validDirectoryId(key))
    throw new Error('原回收引用或登记键无效')
  return assertPurgeIntent(
    await apiRequest<RecyclePurgeIntent>(`${containerFileBase(cid)}/recycle-purges`, {
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
export async function getPurge(cid: string, id: string, signal: AbortSignal, byKey = false) {
  if (!validDirectoryId(id)) throw new Error('留存清理引用无效')
  return assertPurgeIntent(
    await apiRequest<RecyclePurgeIntent>(
      `${containerFileBase(cid)}/recycle-purges/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    cid,
    byKey ? undefined : id,
  )
}
export async function confirmPurge(row: RecyclePurgeIntent, signal: AbortSignal) {
  assertPurgeIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('清理预检未核实或已过期')
  return assertPurgeIntent(
    await apiRequest<RecyclePurgeIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-purges/${row.id}/confirm`,
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
export async function cancelPurge(row: RecyclePurgeIntent, signal: AbortSignal) {
  assertPurgeIntent(row)
  return assertPurgeIntent(
    await apiRequest<RecyclePurgeIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-purges/${row.id}/cancel`,
      { method: 'POST', headers: { 'Idempotency-Key': row.id }, signal },
    ),
    row.location.containerId,
    row.id,
    row.sourceIntentId,
  )
}
export const purgeRecoveryBase = 'files/container-recycle-purges/recovery'
export function currentPurgeInstance(row: RecyclePurgeIntent, target: ContainerFileTarget) {
  return (
    row.location.containerId === target.containerId && row.location.instanceId === target.instanceId
  )
}
