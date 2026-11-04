export interface RecursiveAclRestoreJobResult {
  schemaVersion: number
  intentId: string
  jobId: string
  stage: string
  state: string
  code: string
  aclType: string | null
  entryCount: number | null
  fileCount: number | null
  directoryCount: number | null
  changeCount: number | null
  completedCount: number | null
  changedCount: number | null
  abortedCount: number | null
  notStartedCount: number | null
  unknownCount: number | null
  receiptState: string | null
  bindingCurrent: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  historyAvailable: boolean | null
  publishBarrier: boolean
  sourceEntryCount: number | null
  sourceFileCount: number | null
  sourceDirectoryCount: number | null
  sourceChangedCount: number | null
}
const numbers = [
  'entryCount',
  'fileCount',
  'directoryCount',
  'changeCount',
  'completedCount',
  'changedCount',
  'abortedCount',
  'notStartedCount',
  'unknownCount',
  'sourceEntryCount',
  'sourceFileCount',
  'sourceDirectoryCount',
  'sourceChangedCount',
] as const
const keys = [
  'schemaVersion',
  'intentId',
  'jobId',
  'stage',
  'state',
  'code',
  'aclType',
  ...numbers,
  'receiptState',
  'bindingCurrent',
  'historyMayExist',
  'historyVerified',
  'historyAvailable',
  'publishBarrier',
]
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000'
function counts(n: number | null, f: number | null, d: number | null, c: number | null) {
  return n === null
    ? f === null && d === null && c === null
    : n >= 1 && f !== null && d !== null && c !== null && d >= 1 && f + d === n && c <= n
}
export function readRecursiveAclRestoreJobResult(
  json: string | null,
  jobType: string,
  jobState: string,
  jobId: string,
): RecursiveAclRestoreJobResult | null {
  if (
    !json ||
    json.length > 4096 ||
    ![
      'containers.files.recursive-acl-restore',
      'containers.files.recursive-acl-restore-plan',
    ].includes(jobType)
  )
    return null
  try {
    const r = JSON.parse(json) as Record<string, unknown>
    if (
      !r ||
      Array.isArray(r) ||
      Object.keys(r).length !== keys.length ||
      !keys.every((k) => Object.prototype.hasOwnProperty.call(r, k)) ||
      r.schemaVersion !== 1 ||
      !guid(r.intentId) ||
      !guid(r.jobId) ||
      r.jobId !== jobId
    )
      return null
    if (
      !numbers.every(
        (k) =>
          r[k] === null ||
          (typeof r[k] === 'number' && Number.isSafeInteger(r[k]) && r[k] >= 0 && r[k] <= 10000),
      ) ||
      !['historyMayExist', 'historyVerified', 'publishBarrier'].every(
        (k) => typeof r[k] === 'boolean',
      ) ||
      !['bindingCurrent', 'historyAvailable'].every(
        (k) => r[k] === null || typeof r[k] === 'boolean',
      ) ||
      typeof r.state !== 'string' ||
      typeof r.code !== 'string' ||
      r.code.length > 64 ||
      !/^CONTAINER_RECURSIVE_ACL_RESTORE_[A-Z_]+$/.test(r.code) ||
      ![null, 'access', 'default'].includes(r.aclType as string | null) ||
      ![null, 'Completed', 'Aborted', 'Partial', 'Unknown'].includes(
        r.receiptState as string | null,
      )
    )
      return null
    const v = r as unknown as RecursiveAclRestoreJobResult,
      execution = jobType === 'containers.files.recursive-acl-restore'
    if (
      v.stage !== (execution ? 'execution' : 'planning') ||
      (!execution && v.jobId !== v.intentId) ||
      !(
        execution
          ? ['Succeeded', 'Superseded', 'Canceled', 'Failed', 'Partial', 'Unknown', 'Acknowledged']
          : ['Ready', 'Expired', 'Canceled', 'Failed']
      ).includes(v.state) ||
      jobState !==
        (['Ready', 'Succeeded'].includes(v.state)
          ? 'Succeeded'
          : ['Partial', 'Unknown'].includes(v.state)
            ? 'Running'
            : v.state === 'Canceled'
              ? 'Canceled'
              : 'Failed') ||
      (v.historyVerified && !v.historyMayExist) ||
      (v.historyAvailable === true && !v.historyVerified) ||
      !counts(v.entryCount, v.fileCount, v.directoryCount, v.changeCount) ||
      !counts(
        v.sourceEntryCount,
        v.sourceFileCount,
        v.sourceDirectoryCount,
        v.sourceChangedCount,
      ) ||
      (v.entryCount === null) !== (v.aclType === null) ||
      v.entryCount !== v.sourceEntryCount ||
      v.fileCount !== v.sourceFileCount ||
      v.directoryCount !== v.sourceDirectoryCount
    )
      return null
    const suffix = v.code.slice('CONTAINER_RECURSIVE_ACL_RESTORE_'.length)
    const codes: Record<string, string[]> = {
      Ready: ['READY'],
      Expired: ['PLAN_EXPIRED'],
      Succeeded: ['COMPLETED'],
      Superseded: ['TARGET_CHANGED'],
      Canceled: ['CANCELED'],
      Partial: ['PARTIAL_REQUIRES_ACKNOWLEDGEMENT'],
      Unknown: ['RESULT_UNKNOWN', 'OUTPUT_INVALID', 'BINDING_CHANGED'],
      Acknowledged: [
        v.receiptState === 'Partial' ? 'PARTIAL_ACKNOWLEDGED' : 'UNKNOWN_ACKNOWLEDGED',
      ],
    }
    if (
      v.state === 'Failed'
        ? [
            'READY',
            'PLAN_EXPIRED',
            'COMPLETED',
            'TARGET_CHANGED',
            'CANCELED',
            'PARTIAL_REQUIRES_ACKNOWLEDGEMENT',
            'RESULT_UNKNOWN',
            'PARTIAL_ACKNOWLEDGED',
            'UNKNOWN_ACKNOWLEDGED',
          ].includes(suffix)
        : !codes[v.state]?.includes(suffix)
    )
      return null
    const actual = [
      v.completedCount,
      v.changedCount,
      v.abortedCount,
      v.notStartedCount,
      v.unknownCount,
    ]
    if (v.receiptState === null)
      return actual.every((n) => n === null) &&
        v.bindingCurrent === null &&
        v.historyAvailable === null &&
        (execution
          ? v.state === 'Unknown'
          : !v.publishBarrier &&
            !v.historyVerified &&
            !v.historyMayExist &&
            (v.state !== 'Ready' || v.entryCount !== null))
        ? v
        : null
    if (
      !execution ||
      v.entryCount === null ||
      actual.some((n) => n === null) ||
      v.bindingCurrent === null ||
      v.historyAvailable === null ||
      v.changedCount! > v.completedCount! ||
      v.changedCount! > v.changeCount! ||
      v.completedCount! + v.abortedCount! + v.notStartedCount! + v.unknownCount! !== v.entryCount
    )
      return null
    const expected = ['Succeeded', 'Superseded'].includes(v.state)
      ? 'Completed'
      : ['Canceled', 'Failed'].includes(v.state)
        ? 'Aborted'
        : v.state === 'Acknowledged'
          ? v.receiptState
          : v.state
    if (
      v.receiptState !== expected ||
      (v.state === 'Acknowledged' && !['Partial', 'Unknown'].includes(v.receiptState)) ||
      (v.receiptState === 'Completed' &&
        (v.completedCount !== v.entryCount ||
          v.changedCount !== v.changeCount ||
          !v.historyVerified)) ||
      (v.receiptState === 'Aborted' &&
        (v.changedCount !== 0 || v.unknownCount !== 0 || v.bindingCurrent !== false)) ||
      (v.receiptState === 'Partial' &&
        (v.changedCount! <= 0 ||
          v.completedCount! >= v.entryCount ||
          v.unknownCount !== 0 ||
          !v.historyVerified)) ||
      (v.receiptState === 'Unknown' && v.unknownCount! <= 0) ||
      (v.bindingCurrent && (v.receiptState !== 'Completed' || !v.historyAvailable)) ||
      (v.publishBarrier && !['Partial', 'Unknown'].includes(v.state)) ||
      (v.state === 'Succeeded' && !v.bindingCurrent) ||
      (v.state === 'Superseded' && v.bindingCurrent)
    )
      return null
    return v
  } catch {
    return null
  }
}
