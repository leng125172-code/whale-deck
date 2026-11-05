import { sha256 } from '@noble/hashes/sha2.js'
export interface RecycleJobResult {
  schemaVersion: number
  intentId: string
  jobId: string
  stage: string
  state: string
  code: string
  receiptState: string | null
  bindingCurrent: boolean | null
  sourceRetained: boolean | null
  sourceRestored: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  historyAvailable: boolean | null
  publishBarrier: boolean
}
const keys =
  'schemaVersion intentId jobId stage state code receiptState bindingCurrent sourceRetained sourceRestored historyMayExist historyVerified historyAvailable publishBarrier'.split(
    ' ',
  )
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000'
export function recycleExecutionId(id: string) {
  const b = sha256(
    new TextEncoder().encode('whaledeck-container-recycle-execution-id-v1:' + id.replace(/-/g, '')),
  )
  const h = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((i) => b[i]!.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
export function readRecycleJobResult(
  json: string | null,
  jobId: string,
  jobType: string,
  state: string,
): RecycleJobResult | null {
  if (
    !json ||
    json.length > 4096 ||
    !['containers.files.recycle', 'containers.files.recycle-plan'].includes(jobType)
  )
    return null
  try {
    const r = JSON.parse(json) as RecycleJobResult
    const execution = jobType === 'containers.files.recycle'
    const unknown = [
      'CONTAINER_RECYCLE_RESULT_UNKNOWN',
      'CONTAINER_RECYCLE_BINDING_CHANGED',
      'CONTAINER_RECYCLE_OUTPUT_INVALID',
    ].includes(r.code)
    if (
      !r ||
      !keys.every((k) => Object.prototype.hasOwnProperty.call(r, k)) ||
      Object.keys(r).length !== keys.length ||
      JSON.stringify(r) !== json ||
      r.schemaVersion !== 1 ||
      !guid(r.intentId) ||
      r.jobId !== jobId ||
      r.jobId !== (execution ? recycleExecutionId(r.intentId) : r.intentId) ||
      r.stage !== (execution ? 'execution' : 'planning') ||
      typeof r.code !== 'string' ||
      !/^CONTAINER_RECYCLE_[A-Z_]+$/.test(r.code) ||
      r.code.length > 64 ||
      state !==
        (r.state === 'Ready' || r.state === 'Succeeded'
          ? 'Succeeded'
          : r.state === 'Unknown'
            ? 'Running'
            : r.state === 'Canceled'
              ? 'Canceled'
              : 'Failed') ||
      ![r.historyMayExist, r.historyVerified, r.publishBarrier].every(
        (v) => typeof v === 'boolean',
      ) ||
      ![r.bindingCurrent, r.sourceRetained, r.sourceRestored, r.historyAvailable].every(
        (v) => v === null || typeof v === 'boolean',
      ) ||
      (r.historyVerified && !r.historyMayExist) ||
      (r.historyAvailable === true && !r.historyVerified)
    )
      return null
    if (!execution)
      return ['Ready', 'Expired', 'Canceled', 'Failed'].includes(r.state) &&
        r.receiptState === null &&
        [r.bindingCurrent, r.sourceRetained, r.sourceRestored, r.historyAvailable].every(
          (v) => v === null,
        ) &&
        !r.historyMayExist &&
        !r.historyVerified &&
        !r.publishBarrier &&
        (r.state === 'Ready'
          ? r.code === 'CONTAINER_RECYCLE_READY'
          : r.state === 'Expired'
            ? r.code === 'CONTAINER_RECYCLE_PLAN_EXPIRED'
            : r.state === 'Canceled'
              ? r.code === 'CONTAINER_RECYCLE_CANCELED'
              : true)
        ? r
        : null
    if (
      !['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Unknown', 'Acknowledged'].includes(
        r.state,
      ) ||
      (r.state !== 'Unknown' && r.publishBarrier)
    )
      return null
    if (r.receiptState === null)
      return r.state === 'Unknown' &&
        unknown &&
        [r.bindingCurrent, r.sourceRetained, r.sourceRestored, r.historyAvailable].every(
          (v) => v === null,
        ) &&
        !r.historyVerified
        ? r
        : null
    if (
      !['Trashed', 'Restored', 'Aborted', 'Unknown'].includes(r.receiptState) ||
      r.bindingCurrent === null ||
      r.historyAvailable === null ||
      (r.bindingCurrent &&
        (!['Trashed', 'Restored'].includes(r.receiptState) || !r.historyAvailable)) ||
      (['Trashed', 'Restored'].includes(r.receiptState) && !r.historyVerified) ||
      (r.receiptState === 'Unknown'
        ? r.sourceRetained !== null || r.sourceRestored !== null
        : r.sourceRetained !== (r.receiptState === 'Trashed') ||
          r.sourceRestored !== (r.receiptState === 'Restored'))
    )
      return null
    return (
      r.state === 'Succeeded'
        ? r.receiptState === 'Trashed' && r.bindingCurrent && r.code === 'CONTAINER_RECYCLE_TRASHED'
        : r.state === 'Superseded'
          ? r.receiptState === 'Restored'
            ? r.code === 'CONTAINER_RECYCLE_ALREADY_RESTORED'
            : r.receiptState === 'Trashed' &&
              !r.bindingCurrent &&
              r.code === 'CONTAINER_RECYCLE_TARGET_CHANGED'
          : r.state === 'Unknown'
            ? r.receiptState === 'Unknown' && unknown
            : r.state === 'Acknowledged'
              ? r.code === 'CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED'
              : r.state === 'Canceled'
                ? r.receiptState === 'Aborted' && r.code === 'CONTAINER_RECYCLE_CANCELED'
                : r.receiptState === 'Aborted' && r.code === 'CONTAINER_RECYCLE_INTERRUPTED'
    )
      ? r
      : null
  } catch {
    return null
  }
}
