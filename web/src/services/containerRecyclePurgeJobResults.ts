import { sha256 } from '@noble/hashes/sha2.js'
import { recycleExecutionId } from './containerRecycleJobResults'
export interface RecyclePurgeJobResult {
  schemaVersion: number
  intentId: string
  jobId: string
  sourceIntentId: string
  sourceOperationId: string
  stage: string
  state: string
  code: string
  receiptState: string | null
  reservationStarted: boolean
  reservationRecorded: boolean
  applyStarted: boolean
  purgeApplied: boolean | null
  sourceFenced: boolean | null
  ownedFence: boolean | null
  publishBarrier: boolean
}
const keys =
  'schemaVersion intentId jobId sourceIntentId sourceOperationId stage state code receiptState reservationStarted reservationRecorded applyStarted purgeApplied sourceFenced ownedFence publishBarrier'.split(
    ' ',
  )
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000'
export function recyclePurgeExecutionId(id: string) {
  const b = sha256(
    new TextEncoder().encode(
      'whaledeck-container-recycle-purge-execution-id-v1:' + id.replace(/-/g, ''),
    ),
  )
  const h = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((i) => b[i]!.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

function code(r: RecyclePurgeJobResult, execution: boolean) {
  const suffix = r.code.slice('CONTAINER_RECYCLE_PURGE_'.length)
  switch (r.state) {
    case 'Ready':
      return suffix === 'READY'
    case 'Expired':
      return suffix === 'PLAN_EXPIRED'
    case 'Succeeded':
      return suffix === 'COMPLETED'
    case 'Canceled':
      return suffix === 'CANCELED'
    case 'Reserving':
      return suffix === (r.reservationRecorded ? 'RESERVED' : 'RESERVING')
    case 'Applying':
      return suffix === 'APPLYING'
    case 'Unknown':
      return [
        'RESULT_UNKNOWN',
        'OUTPUT_INVALID',
        'BINDING_CHANGED',
        'EXECUTION_MARKER_MISSING',
      ].includes(suffix)
    case 'Acknowledged':
      return suffix === 'UNKNOWN_ACKNOWLEDGED'
    case 'Failed':
      return execution
        ? suffix === 'INTERRUPTED'
        : ![
            'READY',
            'PLAN_EXPIRED',
            'COMPLETED',
            'CANCELED',
            'UNKNOWN_ACKNOWLEDGED',
            'RESERVING',
            'RESERVED',
            'APPLYING',
            'ACCEPTED',
            'PLANNING',
            'PLAN_ACCEPTED',
          ].includes(suffix)
    default:
      return false
  }
}
export function readRecyclePurgeJobResult(
  json: string | null,
  jobId: string,
  jobType: string,
  state: string,
): RecyclePurgeJobResult | null {
  if (
    !json ||
    json.length > 4096 ||
    !['containers.files.recycle-purge', 'containers.files.recycle-purge-plan'].includes(jobType)
  )
    return null
  try {
    const r = JSON.parse(json) as RecyclePurgeJobResult,
      execution = jobType === 'containers.files.recycle-purge'
    if (
      !r ||
      Object.keys(r).length !== 16 ||
      !keys.every((k) => Object.prototype.hasOwnProperty.call(r, k)) ||
      JSON.stringify(r) !== json ||
      r.schemaVersion !== 1 ||
      !guid(r.intentId) ||
      !guid(r.sourceIntentId) ||
      r.intentId === r.sourceIntentId ||
      r.sourceOperationId !== recycleExecutionId(r.sourceIntentId) ||
      r.jobId !== jobId ||
      r.jobId !== (execution ? recyclePurgeExecutionId(r.intentId) : r.intentId) ||
      (execution && r.jobId === r.sourceOperationId) ||
      r.stage !== (execution ? 'execution' : 'planning') ||
      typeof r.code !== 'string' ||
      !/^CONTAINER_RECYCLE_PURGE_[A-Z_]+$/.test(r.code) ||
      r.code.length > 64 ||
      !code(r, execution) ||
      state !==
        (['Ready', 'Succeeded'].includes(r.state)
          ? 'Succeeded'
          : ['Unknown', 'Reserving', 'Applying'].includes(r.state)
            ? 'Running'
            : r.state === 'Canceled'
              ? 'Canceled'
              : 'Failed') ||
      ![r.reservationStarted, r.reservationRecorded, r.applyStarted, r.publishBarrier].every(
        (v) => typeof v === 'boolean',
      ) ||
      ![r.purgeApplied, r.sourceFenced, r.ownedFence].every(
        (v) => v === null || typeof v === 'boolean',
      ) ||
      (r.reservationRecorded && !r.reservationStarted) ||
      (r.applyStarted && !r.reservationRecorded) ||
      (r.ownedFence === true && r.sourceFenced !== true)
    )
      return null
    if (!execution)
      return ['Ready', 'Expired', 'Canceled', 'Failed'].includes(r.state) &&
        !r.reservationStarted &&
        !r.reservationRecorded &&
        !r.applyStarted &&
        !r.publishBarrier &&
        r.receiptState === null &&
        r.purgeApplied === null &&
        r.sourceFenced === null &&
        r.ownedFence === null
        ? r
        : null
    if (
      ![
        'Reserving',
        'Applying',
        'Succeeded',
        'Canceled',
        'Failed',
        'Unknown',
        'Acknowledged',
      ].includes(r.state) ||
      r.publishBarrier !== ['Reserving', 'Applying', 'Unknown'].includes(r.state)
    )
      return null
    if (['Reserving', 'Applying'].includes(r.state))
      return r.reservationStarted &&
        r.receiptState === null &&
        r.purgeApplied === null &&
        r.sourceFenced === null &&
        r.ownedFence === null &&
        (r.state === 'Applying' ? r.applyStarted : !r.applyStarted)
        ? r
        : null
    if (r.receiptState === null)
      return r.state === 'Unknown' &&
        r.purgeApplied === null &&
        r.sourceFenced === null &&
        r.ownedFence === null
        ? r
        : null
    const facts =
      r.receiptState === 'Completed'
        ? r.purgeApplied === true && r.sourceFenced === true && r.ownedFence === true
        : r.receiptState === 'Aborted'
          ? r.purgeApplied === false && r.sourceFenced !== null && r.ownedFence === false
          : r.receiptState === 'Unknown'
            ? r.purgeApplied === null && r.sourceFenced === true && r.ownedFence !== null
            : false
    return facts &&
      (r.state === 'Succeeded'
        ? r.receiptState === 'Completed' && r.applyStarted
        : ['Canceled', 'Failed'].includes(r.state)
          ? r.receiptState === 'Aborted'
          : r.state === 'Unknown'
            ? r.receiptState === 'Unknown'
            : r.state === 'Acknowledged')
      ? r
      : null
  } catch {
    return null
  }
}
