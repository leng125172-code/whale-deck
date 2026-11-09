import { sha256 } from '@noble/hashes/sha2.js'
import { apiRequest } from './apiClient'
import { validDirectoryId } from './containerDirectories'
import { containerFileBase } from './containerFiles'

export const retirementExecutionJob = 'containers.files.recycle-archive-retirement'
export const retirementPlanningJob = 'containers.files.recycle-archive-retirement-plan'
export const retirementJob = (value: string) =>
  value === retirementExecutionJob || value === retirementPlanningJob
export const retirementTerminal = (state: string) =>
  ['Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged'].includes(state)
const states = [
  'Queued',
  'Planning',
  'Ready',
  'Pending',
  'Reserving',
  'Applying',
  'Succeeded',
  'Failed',
  'Canceled',
  'Expired',
  'Unknown',
  'Acknowledged',
]
const codePattern = /^CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_[A-Z_]{1,28}$/
const date = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
const integer = (value: unknown, min: number, max: number): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max
const exact = (value: unknown, keys: string) =>
  !!value &&
  typeof value === 'object' &&
  !Array.isArray(value) &&
  Object.keys(value).sort().join(' ') === keys.split(' ').sort().join(' ')

interface RetirementFacts {
  state: string
  reservationRecorded: boolean
  applyStarted: boolean
  publishBarrier: boolean
  retirementApplied: boolean | null
}
export interface RetirementIntent extends RetirementFacts {
  id: string
  planningJobId: string
  executionJobId: string | null
  archiveIntentId: string
  retainDays: number
  version: number
  readyExpiresAtUtc: string | null
  notBeforeUnixMs: number | null
  archiveBytes: number | null
  retirementEligible: boolean | null
  rootState: string | null
  resultCode: string | null
  confirmation: { token: string; expiresAtUtc: string } | null
}
export interface RetirementJobResult extends RetirementFacts {
  schemaVersion: number
  intentId: string
  jobId: string
  archiveIntentId: string
  containerId: string
  retainDays: number
  code: string
  notBeforeUnixMs: number | null
  reservationStarted: boolean
  receiptState: string | null
}

export function retirementExecutionId(id: string) {
  const bytes = sha256(
    new TextEncoder().encode(
      'whaledeck-container-recycle-archive-retirement-business-v1:execution:' +
        id.replace(/-/g, ''),
    ),
  )
  const hex = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((index) => bytes[index]!.toString(16).padStart(2, '0'))
    .join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
function facts(row: RetirementFacts, receipt: string | null) {
  return (
    states.includes(row.state) &&
    typeof row.reservationRecorded === 'boolean' &&
    typeof row.applyStarted === 'boolean' &&
    (!row.applyStarted || row.reservationRecorded) &&
    row.publishBarrier === ['Reserving', 'Applying', 'Unknown'].includes(row.state) &&
    (receipt === null
      ? row.retirementApplied === null
      : receipt === 'Completed'
        ? row.applyStarted && row.retirementApplied === true
        : receipt === 'Aborted'
          ? row.retirementApplied === false
          : receipt === 'Unknown' &&
            (row.retirementApplied === null || row.retirementApplied === false)) &&
    (row.state === 'Succeeded'
      ? receipt === 'Completed'
      : row.state === 'Unknown'
        ? receipt === null || receipt === 'Unknown'
        : row.state === 'Acknowledged'
          ? receipt !== null
          : ['Failed', 'Canceled', 'Expired'].includes(row.state)
            ? receipt === null || receipt === 'Aborted'
            : receipt === null && row.applyStarted === (row.state === 'Applying')) &&
    (!['Queued', 'Planning', 'Ready', 'Pending'].includes(row.state) || !row.reservationRecorded)
  )
}

export function assertRetirement(
  row: RetirementIntent,
  id?: string,
  archiveId?: string,
  days?: number,
  recovery = false,
) {
  const invalid = () => new Error('归档退役任务摘要无法核实')
  if (
    !exact(
      row,
      'id planningJobId executionJobId archiveIntentId retainDays state version readyExpiresAtUtc notBeforeUnixMs archiveBytes retirementEligible reservationRecorded applyStarted publishBarrier retirementApplied rootState resultCode confirmation',
    ) ||
    !validDirectoryId(row.id) ||
    row.planningJobId !== row.id ||
    !validDirectoryId(row.archiveIntentId) ||
    row.archiveIntentId === row.id ||
    (id !== undefined && row.id !== id) ||
    (archiveId !== undefined && row.archiveIntentId !== archiveId) ||
    !integer(row.retainDays, 1, 3650) ||
    (days !== undefined && row.retainDays !== days) ||
    !integer(row.version, 0, Number.MAX_SAFE_INTEGER) ||
    (row.executionJobId !== null &&
      (row.executionJobId !== retirementExecutionId(row.id) ||
        row.executionJobId === row.archiveIntentId)) ||
    (row.readyExpiresAtUtc !== null && !date(row.readyExpiresAtUtc)) ||
    (row.notBeforeUnixMs !== null && !integer(row.notBeforeUnixMs, 86400000, 253402300799999)) ||
    (row.archiveBytes !== null && !integer(row.archiveBytes, 0, 1073741824)) ||
    (row.retirementEligible !== null && typeof row.retirementEligible !== 'boolean') ||
    (row.resultCode !== null &&
      (typeof row.resultCode !== 'string' || !codePattern.test(row.resultCode))) ||
    !facts(row, row.rootState)
  )
    throw invalid()
  const hasPlan = row.readyExpiresAtUtc !== null
  if (
    hasPlan !== (row.notBeforeUnixMs !== null) ||
    hasPlan !== (row.archiveBytes !== null) ||
    hasPlan !== (row.retirementEligible !== null) ||
    (row.state === 'Ready' && !hasPlan) ||
    (['Pending', 'Reserving', 'Applying', 'Succeeded', 'Unknown', 'Acknowledged'].includes(
      row.state,
    ) &&
      row.executionJobId === null) ||
    (row.executionJobId !== null &&
      (!hasPlan || ['Queued', 'Planning', 'Ready'].includes(row.state)))
  )
    throw invalid()
  if (
    row.confirmation !== null &&
    (recovery ||
      row.state !== 'Ready' ||
      row.retirementEligible !== true ||
      !exact(row.confirmation, 'token expiresAtUtc') ||
      typeof row.confirmation.token !== 'string' ||
      !/^[A-Za-z0-9+/=]+\.[A-F0-9]{64}$/.test(row.confirmation.token) ||
      row.confirmation.token.length > 4096 ||
      row.confirmation.expiresAtUtc !== row.readyExpiresAtUtc)
  )
    throw invalid()
  return row
}

export function assertRetirementAdvance(previous: RetirementIntent | null, next: RetirementIntent) {
  if (
    previous &&
    (next.id !== previous.id ||
      next.version < previous.version ||
      next.archiveIntentId !== previous.archiveIntentId ||
      next.retainDays !== previous.retainDays ||
      (previous.executionJobId !== null && previous.executionJobId !== next.executionJobId) ||
      (previous.applyStarted && !next.applyStarted) ||
      (previous.reservationRecorded && !next.reservationRecorded) ||
      (retirementTerminal(previous.state) && next.state !== previous.state))
  )
    throw new Error('归档退役任务返回了冲突的历史状态')
}

export function readRetirementJob(
  raw: string | null | undefined,
  id: string,
  type: string,
  jobState: string,
): RetirementJobResult | null {
  if (!raw || raw.length > 2048 || !retirementJob(type)) return null
  try {
    const row = JSON.parse(raw) as RetirementJobResult
    const execution = type === retirementExecutionJob
    if (
      !exact(
        row,
        'schemaVersion intentId jobId archiveIntentId containerId retainDays state code notBeforeUnixMs reservationStarted reservationRecorded applyStarted publishBarrier receiptState retirementApplied',
      ) ||
      row.schemaVersion !== 1 ||
      !validDirectoryId(row.intentId) ||
      !validDirectoryId(row.archiveIntentId) ||
      row.archiveIntentId === row.intentId ||
      row.archiveIntentId === id ||
      row.jobId !== id ||
      row.jobId !== (execution ? retirementExecutionId(row.intentId) : row.intentId) ||
      typeof row.containerId !== 'string' ||
      !/^[a-f0-9]{64}$/.test(row.containerId) ||
      !integer(row.retainDays, 1, 3650) ||
      typeof row.code !== 'string' ||
      !codePattern.test(row.code) ||
      typeof row.reservationStarted !== 'boolean' ||
      (row.reservationRecorded && !row.reservationStarted) ||
      (row.publishBarrier && !row.reservationStarted) ||
      (row.receiptState !== null && !row.reservationStarted) ||
      (row.state === 'Pending' && row.reservationStarted) ||
      (['Canceled', 'Expired', 'Failed'].includes(row.state) &&
        row.receiptState !== (row.reservationStarted ? 'Aborted' : null)) ||
      !facts(row, row.receiptState) ||
      (row.notBeforeUnixMs !== null && !integer(row.notBeforeUnixMs, 86400000, 253402300799999))
    )
      return null
    const expected = ['Queued', 'Pending'].includes(row.state)
      ? 'Queued'
      : ['Ready', 'Succeeded'].includes(row.state)
        ? 'Succeeded'
        : ['Planning', 'Reserving', 'Applying', 'Unknown'].includes(row.state)
          ? 'Running'
          : row.state === 'Canceled'
            ? 'Canceled'
            : 'Failed'
    if (
      expected !== jobState ||
      (execution
        ? row.notBeforeUnixMs === null || ['Queued', 'Planning', 'Ready'].includes(row.state)
        : row.reservationStarted ||
          row.receiptState !== null ||
          !['Queued', 'Planning', 'Ready', 'Canceled', 'Expired', 'Failed'].includes(row.state))
    )
      return null
    return row
  } catch {
    return null
  }
}
const base = (cid: string) => `${containerFileBase(cid)}/recycle-archive-retirements`
function ref(id: string) {
  if (!validDirectoryId(id)) throw new Error('归档退役引用无效')
  return id
}
export async function queueRetirement(
  cid: string,
  archiveIntentId: string,
  retainDays: number,
  key: string,
  signal: AbortSignal,
) {
  ref(archiveIntentId)
  if (!integer(retainDays, 1, 3650)) throw new Error('请输入 1 至 3650 天的整数保留期')
  return assertRetirement(
    await apiRequest<RetirementIntent>(base(cid), {
      method: 'POST',
      headers: { 'Idempotency-Key': ref(key) },
      body: JSON.stringify({ archiveIntentId, retainDays }),
      signal,
    }),
    undefined,
    archiveIntentId,
    retainDays,
  )
}
export async function getRetirement(
  cid: string,
  id: string,
  signal: AbortSignal,
  mode: 'id' | 'key' | 'recovery' = 'id',
) {
  const path =
    mode === 'key' ? `by-key/${ref(id)}` : `${ref(id)}${mode === 'recovery' ? '/recovery' : ''}`
  return assertRetirement(
    await apiRequest<RetirementIntent>(`${base(cid)}/${path}`, { signal }),
    mode === 'key' ? undefined : id,
    undefined,
    undefined,
    mode === 'recovery',
  )
}
export async function confirmRetirement(cid: string, row: RetirementIntent, signal: AbortSignal) {
  assertRetirement(row)
  if (
    !row.confirmation ||
    Date.parse(row.confirmation.expiresAtUtc) <= Date.now() ||
    row.notBeforeUnixMs === null ||
    row.notBeforeUnixMs > Date.now()
  )
    throw new Error('归档退役确认票据不可用或已过期')
  return assertRetirement(
    await apiRequest<RetirementIntent>(`${base(cid)}/${ref(row.id)}/confirm`, {
      method: 'POST',
      headers: { 'Idempotency-Key': row.id },
      body: JSON.stringify({
        planHash: row.confirmation.token,
        confirmed: true,
        version: row.version,
      }),
      signal,
    }),
    row.id,
    row.archiveIntentId,
    row.retainDays,
  )
}
export async function cancelRetirement(cid: string, id: string, signal: AbortSignal) {
  await apiRequest(`${base(cid)}/${ref(id)}/cancel`, {
    method: 'POST',
    headers: { 'Idempotency-Key': id },
    signal,
  })
}
export async function acknowledgeRetirement(
  cid: string,
  row: RetirementIntent,
  signal: AbortSignal,
) {
  assertRetirement(row, undefined, undefined, undefined, true)
  if (row.state !== 'Unknown' || !row.publishBarrier) throw new Error('当前任务没有待处置结果')
  await apiRequest(`${base(cid)}/${ref(row.id)}/recovery/acknowledge`, {
    method: 'POST',
    headers: { 'Idempotency-Key': row.id },
    body: JSON.stringify({ confirmed: true, version: row.version }),
    signal,
  })
}

export const retirementState = (state: string) =>
  (
    ({
      Queued: '已登记',
      Planning: '正在预检',
      Ready: '预检就绪',
      Pending: '已确认，等待执行',
      Reserving: '正在预留',
      Applying: '正在执行',
      Succeeded: '退役完成',
      Failed: '任务失败',
      Canceled: '已取消',
      Expired: '已过期',
      Unknown: '结果未知，保护仍生效',
      Acknowledged: '已明确处置',
    }) as Record<string, string>
  )[state] ?? '状态待核实'
