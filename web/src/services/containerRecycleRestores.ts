import { apiRequest } from './apiClient'
import { containerFileBase } from './containerFiles'
import type { ContainerFileTarget } from './containerFiles'
import type { ContainerUploadLocation } from './containerUploads'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'
import { validRecycleId } from './containerRecycles'

export interface RecycleRestoreSource {
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
export interface RecycleRestoreReceipt {
  id: string
  sourceOperationId: string
  fingerprint: string
  targetProof: string
  state: 'Completed' | 'Aborted' | 'Unknown'
  bindingCurrent: boolean
  replayed: boolean
  historyVerified: boolean
  historyDigest: string
  historyAvailable: boolean
  sourceState: string | null
  sourceRetained: boolean | null
  sourceRestored: boolean | null
  restoredByThisOperation: boolean | null
}
export interface RecycleRestoreIntent {
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
  historyVerified: boolean
  historyDigest: string | null
  receipt: RecycleRestoreReceipt | null
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
  'Applying',
  'Unknown',
  'Succeeded',
  'Superseded',
  'Canceled',
  'Failed',
  'Expired',
  'Acknowledged',
]
export const restoreTerminal = (state: string) =>
  ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Expired', 'Acknowledged'].includes(state)
export function restoreState(state: string) {
  return (
    (
      {
        Queued: '等待恢复预检',
        Planning: '正在核验恢复来源',
        Ready: '恢复预检完成，尚未写入',
        Pending: '等待原inode恢复',
        Applying: '正在恢复原inode',
        Unknown: '恢复结果未知，保护已保留',
        Succeeded: '本次文件恢复已完成',
        Superseded: '曾完成恢复，当前绑定已变化',
        Canceled: '恢复已取消',
        Failed: '文件恢复未完成',
        Expired: '恢复预检已过期',
        Acknowledged: '已处置未知恢复历史，不代表恢复成功',
      } as Record<string, string>
    )[state] ?? '无法核实的恢复状态'
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
export function assertRestoreSource(v: RecycleRestoreSource, cid: string) {
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
function receipt(v: RecycleRestoreIntent) {
  const r = v.receipt
  if (
    !r ||
    !exact(
      r,
      'id sourceOperationId fingerprint targetProof state bindingCurrent replayed historyVerified historyDigest historyAvailable sourceState sourceRetained sourceRestored restoredByThisOperation',
    ) ||
    r.id !== v.executionJobId ||
    r.sourceOperationId !== v.sourceOperationId ||
    typeof r.fingerprint !== 'string' ||
    !hash.test(r.fingerprint) ||
    typeof r.targetProof !== 'string' ||
    !hash.test(r.targetProof) ||
    !['Completed', 'Aborted', 'Unknown'].includes(r.state) ||
    ![r.bindingCurrent, r.replayed, r.historyVerified, r.historyAvailable].every(
      (x) => typeof x === 'boolean',
    ) ||
    r.historyVerified !== v.historyVerified ||
    r.historyDigest !== (v.historyDigest ?? '') ||
    (r.historyAvailable && !r.historyVerified) ||
    (r.bindingCurrent && !(r.state === 'Completed' && r.historyAvailable && r.historyVerified)) ||
    ![null, 'Trashed', 'Restored', 'Aborted', 'Unknown'].includes(r.sourceState)
  )
    return false
  if (r.state === 'Completed')
    return (
      r.historyVerified &&
      r.sourceState === 'Restored' &&
      r.sourceRetained === false &&
      r.sourceRestored === true &&
      r.restoredByThisOperation === true
    )
  if (r.state === 'Unknown')
    return (
      r.historyVerified &&
      r.sourceRetained === null &&
      r.sourceRestored === null &&
      r.restoredByThisOperation === null
    )
  return (
    r.restoredByThisOperation === false &&
    (r.historyVerified
      ? r.sourceState === 'Trashed' && r.sourceRetained === true && r.sourceRestored === false
      : r.sourceState === null && r.sourceRetained === null && r.sourceRestored === null)
  )
}
export function assertRestoreIntent(
  v: RecycleRestoreIntent,
  cid?: string,
  id?: string,
  sourceId?: string,
  source?: RecycleRestoreSource,
) {
  if (
    !v ||
    !exact(
      v,
      'id planningJobId executionJobId sourceIntentId sourceOperationId sourceHistoryDigest location state originalUid originalGid originalMode executorUid executorGid sizeBytes sha256 attributeCount readyExpiresAtUtc resultCode version historyVerified historyDigest receipt confirmation',
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
    typeof v.historyVerified !== 'boolean' ||
    (v.historyVerified ? v.historyDigest !== v.sourceHistoryDigest : v.historyDigest !== null) ||
    (v.resultCode !== null &&
      (typeof v.resultCode !== 'string' ||
        !/^CONTAINER_RECYCLE_RESTORE_[A-Z_]{1,48}$/.test(v.resultCode)))
  )
    throw new Error('独立恢复任务的身份或摘要无法核实')
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
    throw new Error('恢复任务替换了原来源')
  const planned = v.readyExpiresAtUtc !== null,
    values = [v.originalUid, v.originalGid, v.executorUid, v.executorGid]
  if (
    source &&
    planned &&
    (v.originalUid !== source.originalUid ||
      v.originalGid !== source.originalGid ||
      v.originalMode !== source.originalMode)
  )
    throw new Error('恢复预检替换了原来源身份')
  if (
    planned
      ? !date(v.readyExpiresAtUtc) ||
        !values.every(validRecycleId) ||
        !Number.isSafeInteger(v.originalMode) ||
        v.originalMode! < 32768 ||
        v.originalMode! > 33279
      : [...values, v.originalMode].some((x) => x !== null)
  )
    throw new Error('恢复预检身份无法核实')
  if (
    (['Queued', 'Planning'].includes(v.state) && (planned || v.executionJobId !== null)) ||
    ([
      'Ready',
      'Pending',
      'Applying',
      'Unknown',
      'Succeeded',
      'Superseded',
      'Acknowledged',
    ].includes(v.state) &&
      !planned) ||
    (['Pending', 'Applying', 'Unknown', 'Succeeded', 'Superseded', 'Acknowledged'].includes(
      v.state,
    ) &&
      v.executionJobId === null) ||
    (['Ready', 'Expired'].includes(v.state) && v.executionJobId !== null) ||
    (v.receipt !== null && !receipt(v)) ||
    ((restoreTerminal(v.state) || v.state === 'Unknown') && v.resultCode === null) ||
    (['Succeeded', 'Superseded', 'Acknowledged'].includes(v.state) && v.receipt === null) ||
    (v.state === 'Succeeded' && !(v.receipt?.state === 'Completed' && v.receipt.bindingCurrent)) ||
    (v.state === 'Superseded' &&
      !(v.receipt?.state === 'Completed' && !v.receipt.bindingCurrent)) ||
    (v.state === 'Unknown' && v.receipt !== null && v.receipt.state !== 'Unknown') ||
    (v.executionJobId !== null &&
      ['Canceled', 'Failed'].includes(v.state) &&
      v.receipt?.state !== 'Aborted')
  )
    throw new Error('恢复结果或可空事实无法核实')
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
    throw new Error('独立恢复确认无法核实')
  return v
}
export function assertRestoreAdvance(a: RecycleRestoreIntent | null, b: RecycleRestoreIntent) {
  assertRestoreIntent(b)
  if (!a) return
  const expired = a.state === 'Ready' && b.state === 'Expired' && b.executionJobId === null
  const norm = (v: RecycleRestoreIntent) =>
    JSON.stringify({ ...v, ...(expired ? { state: 'Expired', confirmation: null } : {}) })
  const origin = (v: RecycleRestoreIntent) =>
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
  const plan = (v: RecycleRestoreIntent) =>
    JSON.stringify([
      v.originalUid,
      v.originalGid,
      v.originalMode,
      v.executorUid,
      v.executorGid,
      v.readyExpiresAtUtc,
    ])
  const fact = (r: RecycleRestoreReceipt) =>
    JSON.stringify([
      r.id,
      r.sourceOperationId,
      r.fingerprint,
      r.targetProof,
      r.state,
      r.historyVerified,
      r.historyDigest,
      r.sourceState,
      r.sourceRetained,
      r.sourceRestored,
      r.restoredByThisOperation,
    ])
  if (
    origin(a) !== origin(b) ||
    b.version < a.version ||
    (a.executionJobId !== null && a.executionJobId !== b.executionJobId) ||
    (a.readyExpiresAtUtc !== null && plan(a) !== plan(b)) ||
    (restoreTerminal(a.state) && norm(a) !== norm(b)) ||
    (a.state === 'Unknown' && !['Unknown', 'Acknowledged'].includes(b.state)) ||
    (a.state === 'Applying' && b.state === 'Pending') ||
    (a.receipt && (!b.receipt || fact(a.receipt) !== fact(b.receipt))) ||
    (a.historyVerified && !b.historyVerified) ||
    (a.historyDigest !== null && a.historyDigest !== b.historyDigest) ||
    (a.state === 'Ready' &&
      b.state === 'Ready' &&
      JSON.stringify(a.confirmation) !== JSON.stringify(b.confirmation)) ||
    (a.version === b.version && norm(a) !== norm(b))
  )
    throw new Error('原恢复任务的事实或版本发生变化')
}
export async function listRestoreSources(
  cid: string,
  signal: AbortSignal,
  after: string | null = null,
) {
  if (after !== null && !validDirectoryId(after)) throw new Error('恢复来源游标无效')
  const p = await apiRequest<{ items: RecycleRestoreSource[]; next: string | null }>(
    `${containerFileBase(cid)}/recycle-restores/sources?take=25${after ? `&after=${after}` : ''}`,
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
    throw new Error('恢复来源分页无法核实')
  p.items.forEach((v) => assertRestoreSource(v, cid))
  return p
}
export async function queueRestore(
  cid: string,
  sourceId: string,
  key: string,
  signal: AbortSignal,
) {
  if (!validDirectoryId(sourceId) || !validDirectoryId(key))
    throw new Error('原回收引用或登记键无效')
  return assertRestoreIntent(
    await apiRequest<RecycleRestoreIntent>(`${containerFileBase(cid)}/recycle-restores`, {
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
export async function getRestore(cid: string, id: string, signal: AbortSignal, byKey = false) {
  if (!validDirectoryId(id)) throw new Error('独立恢复引用无效')
  return assertRestoreIntent(
    await apiRequest<RecycleRestoreIntent>(
      `${containerFileBase(cid)}/recycle-restores/${byKey ? 'by-key/' : ''}${id}`,
      { signal },
    ),
    cid,
    byKey ? undefined : id,
  )
}
export async function confirmRestore(row: RecycleRestoreIntent, signal: AbortSignal) {
  assertRestoreIntent(row)
  if (
    row.state !== 'Ready' ||
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now()
  )
    throw new Error('恢复预检未核实或已过期')
  return assertRestoreIntent(
    await apiRequest<RecycleRestoreIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-restores/${row.id}/confirm`,
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
export async function cancelRestore(row: RecycleRestoreIntent, signal: AbortSignal) {
  assertRestoreIntent(row)
  return assertRestoreIntent(
    await apiRequest<RecycleRestoreIntent>(
      `${containerFileBase(row.location.containerId)}/recycle-restores/${row.id}/cancel`,
      { method: 'POST', headers: { 'Idempotency-Key': row.id }, signal },
    ),
    row.location.containerId,
    row.id,
    row.sourceIntentId,
  )
}
export const restoreRecoveryBase = 'files/container-recycle-restores/recovery'
export function currentRestoreInstance(row: RecycleRestoreIntent, target: ContainerFileTarget) {
  return (
    row.location.containerId === target.containerId && row.location.instanceId === target.instanceId
  )
}
