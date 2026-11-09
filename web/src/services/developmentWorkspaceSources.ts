import { apiRequest } from './apiClient'
import {
  isSourceFormat,
  sameSourceArchive,
  sourceArchiveLimit,
  sourceChunkBytes,
  SourceInputError,
  type SourceArchive,
} from './workspaceSourcePayload'

const hex = /^[a-f0-9]{64}$/
const uuid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/
export const sourceStates = {
  Uploading: '等待上传',
  Ready: '等待暂存',
  Staged: '待确认发布',
  Publishing: '正在发布',
  Canceling: '正在清理',
  Published: '已发布',
  Canceled: '已取消',
  Failed: '导入失败',
} as const

export interface SourcePlan {
  planSha256: string
  contentSha256: string
  fileCount: number
  totalBytes: number
}

export interface SourceImport {
  workspaceId: string
  importId: string
  state: keyof typeof sourceStates
  archive: SourceArchive
  receivedBytes: number
  plan: SourcePlan | null
  code: string | null
  expiresAtUtc: string
}

export function sameSourcePlan(left: SourcePlan | null, right: SourcePlan | null) {
  return (
    left === right ||
    (!!left &&
      !!right &&
      left.planSha256 === right.planSha256 &&
      left.contentSha256 === right.contentSha256 &&
      left.fileCount === right.fileCount &&
      left.totalBytes === right.totalBytes)
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isInt(value: unknown, min: number, max: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max
}

function isDigest(value: unknown): value is string {
  return typeof value === 'string' && hex.test(value)
}

function malformed(): never {
  throw new SourceInputError('导入回执未通过核验。请刷新服务器状态。')
}

function sourceReceipt(value: unknown, workspaceId: string, importId?: string): SourceImport {
  if (
    !isRecord(value) ||
    value.workspaceId !== workspaceId ||
    typeof value.importId !== 'string' ||
    !uuid.test(value.importId) ||
    (importId !== undefined && importId !== value.importId) ||
    typeof value.state !== 'string' ||
    !Object.prototype.hasOwnProperty.call(sourceStates, value.state) ||
    !isRecord(value.archive) ||
    !isSourceFormat(value.archive.format) ||
    !isInt(value.archive.archiveBytes, 1, sourceArchiveLimit) ||
    !isDigest(value.archive.archiveSha256) ||
    !isInt(value.receivedBytes, 0, value.archive.archiveBytes) ||
    typeof value.expiresAtUtc !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
    (value.code !== null &&
      (typeof value.code !== 'string' || !/^WORKSPACE_SOURCE_[A-Za-z_]{1,45}$/.test(value.code)))
  )
    return malformed()

  const archiveBytes = value.archive.archiveBytes
  if (
    (value.receivedBytes !== archiveBytes && value.receivedBytes % sourceChunkBytes !== 0) ||
    (value.state === 'Uploading' && value.receivedBytes === archiveBytes) ||
    (['Ready', 'Staged', 'Publishing', 'Published'].includes(value.state) &&
      value.receivedBytes !== archiveBytes)
  )
    return malformed()

  if (value.plan === null) {
    if (['Staged', 'Publishing', 'Published'].includes(value.state)) return malformed()
  } else if (
    !isRecord(value.plan) ||
    ['Uploading', 'Ready'].includes(value.state) ||
    !isDigest(value.plan.planSha256) ||
    !isDigest(value.plan.contentSha256) ||
    !isInt(value.plan.fileCount, 1, 9999) ||
    !isInt(value.plan.totalBytes, 0, Math.min(1024 * sourceChunkBytes, 256 * archiveBytes))
  )
    return malformed()

  return value as unknown as SourceImport
}

const scope = (id: string) => `development/workspaces/${encodeURIComponent(id)}/source-imports`
const target = (id: string, importId: string) => `${scope(id)}/${encodeURIComponent(importId)}`
const request = async (
  id: string,
  importId: string,
  init: RequestInit,
  suffix = '',
): Promise<SourceImport> =>
  sourceReceipt(await apiRequest<unknown>(target(id, importId) + suffix, init), id, importId)

export const workspaceSources = {
  async current(id: string, signal: AbortSignal): Promise<SourceImport | null> {
    const result = await apiRequest<unknown>(scope(id), { signal })
    return result === undefined ? null : sourceReceipt(result, id)
  },
  async begin(id: string, importId: string, archive: SourceArchive, signal: AbortSignal) {
    const result = await request(id, importId, {
      method: 'PUT',
      body: JSON.stringify(archive),
      signal,
    })
    if (!sameSourceArchive(archive, result.archive)) return malformed()
    return result
  },
  async chunk(id: string, current: SourceImport, body: Blob, signal: AbortSignal) {
    const offset = current.receivedBytes
    const result = await request(
      id,
      current.importId,
      { method: 'PUT', body, signal, headers: { 'Content-Type': 'application/octet-stream' } },
      `/chunks/${offset}`,
    )
    if (
      !sameSourceArchive(current.archive, result.archive) ||
      !['Uploading', 'Ready'].includes(result.state) ||
      result.receivedBytes < offset + body.size
    )
      return malformed()
    return result
  },
  async publish(id: string, current: SourceImport, planSha256: string, signal: AbortSignal) {
    const result = await request(
      id,
      current.importId,
      { method: 'POST', body: JSON.stringify({ planSha256 }), signal },
      '/publish',
    )
    if (
      !sameSourceArchive(current.archive, result.archive) ||
      result.plan?.planSha256 !== planSha256 ||
      !sameSourcePlan(current.plan, result.plan) ||
      !['Publishing', 'Published'].includes(result.state)
    )
      return malformed()
    return result
  },
  async cancel(id: string, current: SourceImport, signal: AbortSignal) {
    const result = await request(id, current.importId, { method: 'POST', signal }, '/cancel')
    if (
      !sameSourceArchive(current.archive, result.archive) ||
      (current.plan && !sameSourcePlan(current.plan, result.plan)) ||
      !['Canceling', 'Canceled', 'Failed'].includes(result.state)
    )
      return malformed()
    return result
  },
}
