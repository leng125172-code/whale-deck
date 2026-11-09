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
const repositoryId = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const objectId = /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/
const terminal = ['Published', 'Canceled', 'Failed']
export const sourceStates = {
  Uploading: '等待上传',
  Acquiring: '正在获取 Git 源码',
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
  finishedAtUtc?: string
  git?: GitSource
}

export interface GitSourceInput {
  repositoryId: string
  ref: string
}

interface GitSource extends GitSourceInput {
  snapshot: {
    resolvedObject: string
    commit: string
    archive: SourceArchive
    fileCount: number
    totalBytes: number
  } | null
}

export interface SourceHistory {
  items: SourceImport[]
  nextBefore: string | null
}

function validRepository(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= 48 &&
    value.trim() === value &&
    repositoryId.test(value)
  )
}

function isObjectId(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    (value.length === 40 || value.length === 64) &&
    objectId.test(value) &&
    /[1-9a-f]/.test(value)
  )
}

export function validGitRef(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= 255 &&
    value.trim() === value &&
    (value === 'HEAD' || isObjectId(value) || /^refs\/[A-Za-z0-9_./-]+$/.test(value))
  )
}

export function sameSourceOrigin(previous: SourceImport, next: SourceImport) {
  if (!previous.git || !next.git)
    return !previous.git && !next.git && sameSourceArchive(previous.archive, next.archive)
  if (previous.git.repositoryId !== next.git.repositoryId || previous.git.ref !== next.git.ref)
    return false
  const pinned = previous.git.snapshot
  const candidate = next.git.snapshot
  return (
    !pinned ||
    (!!candidate &&
      sameSourceArchive(previous.archive, next.archive) &&
      pinned.resolvedObject === candidate.resolvedObject &&
      pinned.commit === candidate.commit &&
      pinned.fileCount === candidate.fileCount &&
      pinned.totalBytes === candidate.totalBytes)
  )
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
  return typeof value === 'string' && value.length === 64 && hex.test(value)
}

function malformed(): never {
  throw new SourceInputError('导入回执未通过核验。请刷新服务器状态。')
}

function validGitSource(git: unknown, archive: SourceArchive, state: string) {
  if (git === undefined) return state !== 'Acquiring' && archive.archiveBytes > 0
  if (
    !isRecord(git) ||
    !validRepository(git.repositoryId) ||
    !validGitRef(git.ref) ||
    state === 'Uploading' ||
    archive.format !== 'tar'
  )
    return false
  if (git.snapshot === null)
    return (
      ['Acquiring', 'Canceled', 'Failed'].includes(state) &&
      archive.archiveBytes === 0 &&
      archive.archiveSha256 === ''
    )
  const snapshot = git.snapshot
  return (
    isRecord(snapshot) &&
    isObjectId(snapshot.resolvedObject) &&
    isObjectId(snapshot.commit) &&
    snapshot.resolvedObject.length === snapshot.commit.length &&
    (!isObjectId(git.ref) || git.ref === snapshot.resolvedObject) &&
    isRecord(snapshot.archive) &&
    snapshot.archive.format === archive.format &&
    snapshot.archive.archiveBytes === archive.archiveBytes &&
    snapshot.archive.archiveSha256 === archive.archiveSha256 &&
    archive.archiveBytes > 0 &&
    isInt(snapshot.fileCount, 1, 9999) &&
    isInt(snapshot.totalBytes, 0, archive.archiveBytes)
  )
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
    !isInt(value.archive.archiveBytes, 0, sourceArchiveLimit) ||
    (value.archive.archiveBytes === 0
      ? value.archive.archiveSha256 !== ''
      : !isDigest(value.archive.archiveSha256)) ||
    !isInt(value.receivedBytes, 0, value.archive.archiveBytes) ||
    typeof value.expiresAtUtc !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
    (value.code !== null &&
      (typeof value.code !== 'string' || !/^WORKSPACE_SOURCE_[A-Za-z_]{1,45}$/.test(value.code)))
  )
    return malformed()

  const archiveBytes = value.archive.archiveBytes
  if (
    !validGitSource(value.git, value.archive as unknown as SourceArchive, value.state) ||
    (value.finishedAtUtc !== undefined &&
      (!terminal.includes(value.state) ||
        typeof value.finishedAtUtc !== 'string' ||
        !Number.isFinite(Date.parse(value.finishedAtUtc))))
  )
    return malformed()
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
    ['Uploading', 'Acquiring', 'Ready'].includes(value.state) ||
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
  async gitRepositories(id: string, signal: AbortSignal): Promise<string[]> {
    const result = await apiRequest<unknown>(scope(id) + '/git-repositories', { signal })
    if (!Array.isArray(result) || result.length > 128 || !result.every(validRepository))
      return malformed()
    if (new Set(result).size !== result.length) return malformed()
    return result
  },
  async history(id: string, before: string | null, signal: AbortSignal): Promise<SourceHistory> {
    const query = new URLSearchParams({ take: '10' })
    if (before) query.set('before', before)
    const result = await apiRequest<unknown>(scope(id) + '/history?' + query, { signal })
    if (!isRecord(result) || !Array.isArray(result.items) || result.items.length > 10)
      return malformed()
    const items = result.items.map((item: unknown) => sourceReceipt(item, id))
    if (
      items.some(
        (item) => !terminal.includes(item.state) || !item.finishedAtUtc || item.importId === before,
      ) ||
      new Set(items.map((item) => item.importId)).size !== items.length ||
      (result.nextBefore !== null &&
        (items.length !== 10 || result.nextBefore !== items[items.length - 1]?.importId))
    )
      return malformed()
    return { items, nextBefore: result.nextBefore as string | null }
  },
  async beginGit(id: string, importId: string, git: GitSourceInput, signal: AbortSignal) {
    const result = await request(
      id,
      importId,
      { method: 'PUT', body: JSON.stringify(git), signal },
      '/git',
    )
    if (result.git?.repositoryId !== git.repositoryId || result.git.ref !== git.ref)
      return malformed()
    return result
  },
  async begin(id: string, importId: string, archive: SourceArchive, signal: AbortSignal) {
    const result = await request(id, importId, {
      method: 'PUT',
      body: JSON.stringify(archive),
      signal,
    })
    if (result.git || !sameSourceArchive(archive, result.archive)) return malformed()
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
      current.git ||
      result.git ||
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
      !sameSourceOrigin(current, result) ||
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
      !sameSourceOrigin(current, result) ||
      (current.plan && !sameSourcePlan(current.plan, result.plan)) ||
      !['Canceling', 'Canceled', 'Failed'].includes(result.state)
    )
      return malformed()
    return result
  },
}
