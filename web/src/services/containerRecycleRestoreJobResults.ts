import { sha256 } from '@noble/hashes/sha2.js'
import { recycleExecutionId } from './containerRecycleJobResults'
export interface RecycleRestoreJobResult {
  schemaVersion: number
  intentId: string
  jobId: string
  sourceIntentId: string
  sourceOperationId: string
  stage: string
  state: string
  code: string
  receiptState: string | null
  sourceState: string | null
  bindingCurrent: boolean | null
  sourceRetained: boolean | null
  sourceRestored: boolean | null
  restoredByThisOperation: boolean | null
  historyVerified: boolean
  historyAvailable: boolean | null
  publishBarrier: boolean
}
const keys =
  'schemaVersion intentId jobId sourceIntentId sourceOperationId stage state code receiptState sourceState bindingCurrent sourceRetained sourceRestored restoredByThisOperation historyVerified historyAvailable publishBarrier'.split(
    ' ',
  )
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000'
export function recycleRestoreExecutionId(id: string) {
  const b = sha256(
    new TextEncoder().encode(
      'whaledeck-container-recycle-restore-execution-id-v1:' + id.replace(/-/g, ''),
    ),
  )
  const h = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((i) => b[i]!.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
function code(r: RecycleRestoreJobResult, execution: boolean) {
  const s = r.code.slice('CONTAINER_RECYCLE_RESTORE_'.length)
  return r.state === 'Ready'
    ? s === 'READY'
    : r.state === 'Expired'
      ? s === 'PLAN_EXPIRED'
      : r.state === 'Succeeded'
        ? s === 'COMPLETED'
        : r.state === 'Superseded'
          ? s === 'TARGET_CHANGED'
          : r.state === 'Canceled'
            ? s === 'CANCELED'
            : r.state === 'Unknown'
              ? ['RESULT_UNKNOWN', 'OUTPUT_INVALID', 'BINDING_CHANGED'].includes(s)
              : r.state === 'Acknowledged'
                ? s === 'UNKNOWN_ACKNOWLEDGED'
                : r.state === 'Failed'
                  ? execution
                    ? s === 'INTERRUPTED'
                    : ![
                        'READY',
                        'PLAN_EXPIRED',
                        'COMPLETED',
                        'CANCELED',
                        'UNKNOWN_ACKNOWLEDGED',
                      ].includes(s)
                  : false
}
export function readRecycleRestoreJobResult(
  json: string | null,
  jobId: string,
  jobType: string,
  state: string,
): RecycleRestoreJobResult | null {
  if (
    !json ||
    json.length > 4096 ||
    !['containers.files.recycle-restore', 'containers.files.recycle-restore-plan'].includes(jobType)
  )
    return null
  try {
    const r = JSON.parse(json) as RecycleRestoreJobResult,
      execution = jobType === 'containers.files.recycle-restore'
    if (
      !r ||
      Object.keys(r).length !== 17 ||
      !keys.every((k) => Object.prototype.hasOwnProperty.call(r, k)) ||
      JSON.stringify(r) !== json ||
      r.schemaVersion !== 1 ||
      !guid(r.intentId) ||
      !guid(r.sourceIntentId) ||
      r.sourceIntentId === r.intentId ||
      r.sourceOperationId !== recycleExecutionId(r.sourceIntentId) ||
      r.jobId !== jobId ||
      r.jobId !== (execution ? recycleRestoreExecutionId(r.intentId) : r.intentId) ||
      (execution && r.jobId === r.sourceOperationId) ||
      r.stage !== (execution ? 'execution' : 'planning') ||
      typeof r.code !== 'string' ||
      !/^CONTAINER_RECYCLE_RESTORE_[A-Z_]+$/.test(r.code) ||
      r.code.length > 64 ||
      !code(r, execution) ||
      state !==
        (r.state === 'Ready' || r.state === 'Succeeded'
          ? 'Succeeded'
          : r.state === 'Unknown'
            ? 'Running'
            : r.state === 'Canceled'
              ? 'Canceled'
              : 'Failed') ||
      ![r.historyVerified, r.publishBarrier].every((v) => typeof v === 'boolean') ||
      ![
        r.bindingCurrent,
        r.sourceRetained,
        r.sourceRestored,
        r.restoredByThisOperation,
        r.historyAvailable,
      ].every((v) => v === null || typeof v === 'boolean') ||
      (r.historyAvailable === true && !r.historyVerified) ||
      ![null, 'Trashed', 'Restored', 'Aborted', 'Unknown'].includes(r.sourceState)
    )
      return null
    if (!execution)
      return ['Ready', 'Expired', 'Canceled', 'Failed'].includes(r.state) &&
        r.receiptState === null &&
        r.sourceState === null &&
        [
          r.bindingCurrent,
          r.sourceRetained,
          r.sourceRestored,
          r.restoredByThisOperation,
          r.historyAvailable,
        ].every((v) => v === null) &&
        !r.historyVerified &&
        !r.publishBarrier
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
        r.sourceState === null &&
        [
          r.bindingCurrent,
          r.sourceRetained,
          r.sourceRestored,
          r.restoredByThisOperation,
          r.historyAvailable,
        ].every((v) => v === null) &&
        !r.historyVerified
        ? r
        : null
    if (
      !['Completed', 'Aborted', 'Unknown'].includes(r.receiptState) ||
      r.bindingCurrent === null ||
      r.historyAvailable === null ||
      (r.bindingCurrent &&
        !(r.receiptState === 'Completed' && r.historyAvailable && r.historyVerified))
    )
      return null
    const facts =
      r.receiptState === 'Completed'
        ? r.historyVerified &&
          r.sourceState === 'Restored' &&
          r.sourceRetained === false &&
          r.sourceRestored === true &&
          r.restoredByThisOperation === true
        : r.receiptState === 'Unknown'
          ? r.historyVerified &&
            r.sourceRetained === null &&
            r.sourceRestored === null &&
            r.restoredByThisOperation === null
          : r.bindingCurrent === false &&
            r.restoredByThisOperation === false &&
            (r.historyVerified
              ? r.sourceState === 'Trashed' &&
                r.sourceRetained === true &&
                r.sourceRestored === false
              : r.sourceState === null && r.sourceRetained === null && r.sourceRestored === null)
    return facts &&
      (r.state === 'Succeeded'
        ? r.receiptState === 'Completed' && r.bindingCurrent
        : r.state === 'Superseded'
          ? r.receiptState === 'Completed' && !r.bindingCurrent
          : r.state === 'Unknown'
            ? r.receiptState === 'Unknown'
            : ['Canceled', 'Failed'].includes(r.state)
              ? r.receiptState === 'Aborted'
              : true)
      ? r
      : null
  } catch {
    return null
  }
}
