import { treeUuid, validTreeLocation } from '@/services/fileTreeCopies'
import type { TreeLocation } from '@/services/fileTreeCopies'
import { validContainerUploadLocation } from '@/services/containerUploads'
import type { ContainerUploadLocation } from '@/services/containerUploads'

export const recoveryKinds = [
  'upload',
  'mutation',
  'archive',
  'tree-copy',
  'tree-move',
  'tree-pack',
  'tree-unpack',
  'tree-trash',
  'tree-restore',
  'tree-overwrite-restore',
  'tree-purge',
  'container-upload',
] as const
export type RecoveryKind = (typeof recoveryKinds)[number]
export type RecoveryDisposition = 'AcknowledgeUnknown' | 'ReconcileCompleted'
export type RecoveryReason = 'OriginalSessionLost' | 'OperatorHandover' | 'IncidentReview'
export interface RecoveryPurge {
  captureId: string
  captureKind: 'Trash' | 'Overwrite'
  scope: 'Archive' | 'Originals' | 'All'
  version: number
  state: string
  deletionStarted: boolean | null
  archiveRemoved: boolean | null
  originalsRemoved: boolean | null
}
export interface RecoveryCandidate {
  kind: RecoveryKind
  intentId: string
  executionJobId: string | null
  version: number
  createdAtUtc: string
  leaseExpiresAtUtc: string | null
  rootStartRecorded: boolean
  integrity: 'Verified' | 'InvalidBinding'
  source: TreeLocation | null
  target: TreeLocation | null
  hasRetainedObjects: boolean | null
  purge?: RecoveryPurge
  container?: ContainerUploadLocation
}
export interface RecoveryPage {
  entries: RecoveryCandidate[]
  nextCursor: string | null
}
export interface RecoveryCommand {
  kind: RecoveryKind
  intentId: string
  originalVersion: number
  disposition: RecoveryDisposition
  reasonCode: RecoveryReason
}
export interface RecoveryIntent {
  id: string
  planningJobId: string
  executionJobId: string | null
  kind: RecoveryKind
  originalIntentId: string
  originalExecutionJobId: string
  originalVersion: number
  state: string
  disposition: RecoveryDisposition
  reasonCode: RecoveryReason
  rootState: string | null
  readyExpiresAtUtc: string | null
  version: number
  resultCode: string | null
  originalResolution: string | null
  confirmation: { planHash: string; expiresAtUtc: string; warning: string } | null
  purge?: RecoveryPurge
}
export const recoveryPurgeScope = (scope: RecoveryPurge['scope']) =>
  ({ Archive: '仅HDD归档', Originals: '仅保留原件', All: 'HDD归档和保留原件' })[scope]
export const recoveryDeletionFact = (value: boolean | null) =>
  value === null ? '结果待核实，不推断未删除' : value ? '已确认删除' : '未执行该项删除'
export const recoveryPurgeState = (state: string) =>
  (
    ({
      Unknown: '结果未知',
      Succeeded: '完整清理已确认',
      Acknowledged: '已接受未知历史',
      Failed: '未完成',
      Canceled: '已取消',
      Expired: '已过期',
      Queued: '等待计划',
      Planning: '正在核对',
      Ready: '等待确认',
      PurgingQueued: '等待清理',
      Purging: '正在清理',
    }) as Record<string, string>
  )[state] || '无法核实'
export function sameRecoveryPurge(a: RecoveryPurge, b: RecoveryPurge) {
  return a.captureId === b.captureId && a.captureKind === b.captureKind && a.scope === b.scope
}
export function recoveryPurgeFollows(previous: RecoveryPurge, current: RecoveryPurge) {
  const facts = ['deletionStarted', 'archiveRemoved', 'originalsRemoved'] as const
  return (
    sameRecoveryPurge(previous, current) &&
    current.version >= previous.version &&
    facts.every((key) => previous[key] !== true || current[key] === true) &&
    (current.version > previous.version ||
      (current.state === previous.state && facts.every((key) => current[key] === previous[key])))
  )
}
export function recoveryTerminal(state: string) {
  return ['Disposed', 'Failed', 'Canceled', 'Expired'].includes(state)
}
export function recoveryKind(kind: RecoveryKind) {
  return {
    upload: '上传/编辑',
    mutation: '文件变更',
    archive: '历史与回收恢复',
    'tree-copy': '目录复制',
    'tree-move': '目录移动',
    'tree-pack': '目录压缩',
    'tree-unpack': '归档解压',
    'tree-trash': '目录回收',
    'tree-restore': '目录恢复',
    'tree-overwrite-restore': '目录覆盖恢复',
    'tree-purge': '目录永久清理',
    'container-upload': '容器文件写入',
  }[kind]
}
export function recoveryState(state: string) {
  return (
    (
      {
        Queued: '等待核对',
        Planning: '正在核对原回执',
        Ready: '计划已就绪，尚未处置',
        DispositionQueued: '等待封存与处置',
        Fencing: '正在封存并核对',
        Unknown: '封存响应待核实，原锁仍保留',
        Disposed: '处置已记录，不等于原文件操作成功',
        Failed: '处置未完成，原证据与锁不由本次释放',
        Canceled: '本次接管已取消，原任务不变',
        Expired: '本次计划已过期',
      } as Record<string, string>
    )[state] || '无法核实的状态'
  )
}
export function recoveryResult(code: string | null) {
  return (
    (
      {
        FILES_RECOVERY_ALREADY_RESERVED: '另一份接管计划仍占用该任务，请先核实原计划。',
        FILES_RECOVERY_ORIGINAL_BUSY: '原 Worker 的租约仍有效，请稍后重新读取元数据。',
        FILES_RECOVERY_ORIGINAL_CHANGED: '原任务版本或状态已变化，本次不处置，请重新读取。',
        FILES_RECOVERY_DISPOSITION_MISMATCH:
          '原回执与所选处置类型不同；完整回执应选择对账，未知结果应选择接受不确定性。',
        FILES_RECOVERY_RECEIPT_CHANGED: '原回执已变化，本次不处置；请重新核对并生成计划。',
        FILES_RECOVERY_AUTHORITY_UNAVAILABLE: '当前管理员权限无法核实，本次不处置。',
        FILES_RECOVERY_AUTHORITY_OR_LEASE_LOST: '权限或租约已失效，本次不处置。',
        FILES_RECOVERY_FENCE_UNCERTAIN: '未取得封存响应，后台只核对原操作，不重做文件操作。',
        FILES_RECOVERY_DISPOSED: '当前管理员处置已记录；原永久回执、原主体和历史证据仍保留。',
        FILES_RECOVERY_PLAN_EXPIRED: '计划已过期，不自动新建或重新确认。',
      } as Record<string, string>
    )[code || ''] || ''
  )
}
const date = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= 64 && Number.isFinite(Date.parse(value))
const integer = (value: unknown): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const uuid = (value: unknown): value is string =>
  typeof value === 'string' &&
  treeUuid.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000'
const privateFields = [
  'loginBinding',
  'ownerHash',
  'originalJson',
  'observationJson',
  'requestHash',
  'planJson',
  'commandJson',
  'originalBindingHash',
]
function privateMaterial(value: object) {
  return Object.keys(value).some((key) => privateFields.includes(key))
}
export function validRecoveryPurge(value: RecoveryPurge | undefined): value is RecoveryPurge {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const fields = [
    'captureId',
    'captureKind',
    'scope',
    'version',
    'state',
    'deletionStarted',
    'archiveRemoved',
    'originalsRemoved',
  ]
  if (
    Object.keys(value).length !== fields.length ||
    Object.keys(value).some((key) => !fields.includes(key)) ||
    !uuid(value.captureId) ||
    !['Trash', 'Overwrite'].includes(value.captureKind) ||
    !['Archive', 'Originals', 'All'].includes(value.scope) ||
    !integer(value.version) ||
    ![
      'Queued',
      'Planning',
      'Ready',
      'PurgingQueued',
      'Purging',
      'Unknown',
      'Succeeded',
      'Failed',
      'Canceled',
      'Expired',
      'Acknowledged',
    ].includes(value.state) ||
    [value.deletionStarted, value.archiveRemoved, value.originalsRemoved].some(
      (v) => v !== null && typeof v !== 'boolean',
    )
  )
    return false
  for (const [component, fact] of [
    ['Archive', value.archiveRemoved],
    ['Originals', value.originalsRemoved],
  ] as const) {
    const selected = value.scope === 'All' || value.scope === component
    if (
      (!selected && fact !== false) ||
      (selected && value.deletionStarted === false && fact !== false) ||
      (selected && value.deletionStarted === null && fact !== null) ||
      (selected && value.deletionStarted === true && fact === false) ||
      (fact === true && value.deletionStarted !== true) ||
      (selected && value.state === 'Succeeded' && fact !== true)
    )
      return false
  }
  return value.state !== 'Succeeded' || value.deletionStarted === true
}
export function assertRecoveryPage(page: RecoveryPage) {
  if (
    !page ||
    !Array.isArray(page.entries) ||
    page.entries.length > 25 ||
    (page.nextCursor !== null &&
      (typeof page.nextCursor !== 'string' || !/^[a-zA-Z0-9_-]{1,160}$/.test(page.nextCursor)))
  )
    throw new Error('异常任务清单无法核实')
  const seen = new Set<string>()
  for (const row of page.entries) {
    if (
      !row ||
      privateMaterial(row) ||
      !recoveryKinds.includes(row.kind) ||
      !uuid(row.intentId) ||
      (row.executionJobId !== null && !uuid(row.executionJobId)) ||
      !integer(row.version) ||
      !date(row.createdAtUtc) ||
      (row.leaseExpiresAtUtc !== null && !date(row.leaseExpiresAtUtc)) ||
      typeof row.rootStartRecorded !== 'boolean' ||
      !['Verified', 'InvalidBinding'].includes(row.integrity) ||
      (row.source !== null && !validTreeLocation(row.source)) ||
      (row.target !== null && !validTreeLocation(row.target)) ||
      (row.hasRetainedObjects !== null && typeof row.hasRetainedObjects !== 'boolean') ||
      (row.integrity === 'InvalidBinding' &&
        (row.source !== null ||
          row.target !== null ||
          row.hasRetainedObjects !== null ||
          row.purge !== undefined ||
          row.container !== undefined)) ||
      (row.kind === 'container-upload' && row.integrity === 'Verified'
        ? row.source !== null ||
          row.target !== null ||
          row.executionJobId !== row.intentId ||
          !validContainerUploadLocation(row.container) ||
          row.hasRetainedObjects === false ||
          row.purge !== undefined
        : row.container !== undefined) ||
      (row.kind === 'tree-purge' && row.integrity === 'Verified'
        ? !validRecoveryPurge(row.purge) ||
          row.purge.version !== row.version ||
          row.purge.state !== 'Unknown' ||
          row.target !== null ||
          row.hasRetainedObjects !== null
        : row.purge !== undefined) ||
      seen.has(`${row.kind}:${row.intentId}`)
    )
      throw new Error('异常任务元数据无法核实')
    seen.add(`${row.kind}:${row.intentId}`)
  }
  return page
}
export function assertRecoveryIntent(
  row: RecoveryIntent,
  expectedId?: string,
  command?: RecoveryCommand,
) {
  const execution = ['DispositionQueued', 'Fencing', 'Unknown', 'Disposed'].includes(row?.state)
  if (
    !row ||
    privateMaterial(row) ||
    !uuid(row.id) ||
    row.planningJobId !== row.id ||
    (expectedId && row.id !== expectedId) ||
    !recoveryKinds.includes(row.kind) ||
    !uuid(row.originalIntentId) ||
    !uuid(row.originalExecutionJobId) ||
    !integer(row.originalVersion) ||
    !integer(row.version) ||
    (row.kind === 'tree-purge'
      ? !validRecoveryPurge(row.purge) ||
        row.purge.version < row.originalVersion ||
        (row.state === 'Disposed' && row.purge.state !== row.originalResolution)
      : row.purge !== undefined) ||
    (row.executionJobId !== null &&
      (!uuid(row.executionJobId) ||
        row.executionJobId === row.id ||
        row.executionJobId === row.originalExecutionJobId)) ||
    (execution && row.executionJobId === null) ||
    ![
      'Queued',
      'Planning',
      'Ready',
      'DispositionQueued',
      'Fencing',
      'Unknown',
      'Disposed',
      'Failed',
      'Canceled',
      'Expired',
    ].includes(row.state) ||
    !['AcknowledgeUnknown', 'ReconcileCompleted'].includes(row.disposition) ||
    (row.kind === 'container-upload' &&
      (row.disposition !== 'AcknowledgeUnknown' ||
        (row.rootState !== null && row.rootState !== 'Unknown') ||
        (row.state === 'Disposed' &&
          (row.originalResolution !== 'Failed' || row.resultCode !== 'FILES_RECOVERY_DISPOSED')) ||
        ['Acknowledged', 'Succeeded', 'Superseded'].includes(row.originalResolution || ''))) ||
    !['OriginalSessionLost', 'OperatorHandover', 'IncidentReview'].includes(row.reasonCode) ||
    (row.rootState !== null &&
      !['Missing', 'Aborted', 'Unknown', 'Completed'].includes(row.rootState)) ||
    (row.readyExpiresAtUtc !== null && !date(row.readyExpiresAtUtc)) ||
    (row.resultCode !== null &&
      (typeof row.resultCode !== 'string' || !/^FILES_[A-Z_]{1,74}$/.test(row.resultCode))) ||
    (row.originalResolution !== null &&
      !['Acknowledged', 'Failed', 'Succeeded', 'Superseded'].includes(row.originalResolution)) ||
    (row.state === 'Disposed') !== (row.originalResolution !== null) ||
    (row.rootState !== null &&
      (row.rootState === 'Completed') !== (row.disposition === 'ReconcileCompleted')) ||
    (row.state === 'Disposed' &&
      row.disposition === 'AcknowledgeUnknown' &&
      !['Acknowledged', 'Failed'].includes(row.originalResolution || '')) ||
    (row.state === 'Disposed' &&
      row.disposition === 'ReconcileCompleted' &&
      !['Succeeded', 'Superseded'].includes(row.originalResolution || '')) ||
    (command &&
      (row.kind !== command.kind ||
        row.originalIntentId !== command.intentId ||
        row.originalVersion !== command.originalVersion ||
        row.disposition !== command.disposition ||
        row.reasonCode !== command.reasonCode))
  )
    throw new Error('接管任务响应无法核实，保留原引用，不重新登记')
  const confirmation = row.confirmation
  if (
    confirmation === undefined ||
    (['Ready', 'DispositionQueued', 'Fencing', 'Unknown', 'Disposed'].includes(row.state) &&
      (row.rootState === null || row.readyExpiresAtUtc === null)) ||
    (row.state === 'Ready' && confirmation === null) ||
    (confirmation !== null &&
      (row.state !== 'Ready' ||
        row.executionJobId !== null ||
        typeof confirmation.planHash !== 'string' ||
        !confirmation.planHash ||
        confirmation.planHash.length > 8192 ||
        !date(confirmation.expiresAtUtc) ||
        confirmation.expiresAtUtc !== row.readyExpiresAtUtc ||
        typeof confirmation.warning !== 'string' ||
        confirmation.warning.length > 2048))
  )
    throw new Error('接管确认信息无法核实')
  return row
}
