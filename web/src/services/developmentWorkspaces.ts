import { sha256 } from '@noble/hashes/sha2.js'
import { bytesToHex } from '@noble/hashes/utils.js'
import { ApiError, apiRequest, apiUrl, toApiError } from './apiClient'

export const workspacePath = '/development/workspaces'
const endpoint = 'development/workspaces'
const reportLimit = 512 * 1024

export interface WorkspaceBudget {
  cpuMillis: number
  memoryMiB: number
  diskMiB: number
}
export interface RuntimePin {
  key: string
  version: string
  image: string
}
export interface Workspace {
  id: string
  slug: string
  ownerSubject: string
  storageRootId: string
  state: string
  version: number
  lifecycleState?: string
  lifecycleVersion?: number
  budget: WorkspaceBudget
  environment: { version: number; runtimes: RuntimePin[] }
  environmentSha256: string
}
export interface WorkspaceCatalog {
  runtimes: RuntimePin[]
  storageRootIds: string[]
  capacity: WorkspaceBudget
}
export interface WorkspaceRegistration {
  slug: string
  storageRootId: string
  runtimes: string[]
  budget: WorkspaceBudget
}
export interface StoragePlan {
  workspaceId: string
  storageRootId: string
  environmentSha256: string
  diskMiB: number
  planSha256: string
}
export interface LifecyclePlan {
  workspaceId: string
  action: 'stop' | 'resume'
  expectedVersion: number
  planSha256: string
  retainsData: boolean
  reservesBudget: boolean
  requiresIdle: boolean
}
export interface LifecycleOperation {
  operationId: string
  workspaceId: string
  action: 'stop' | 'resume'
  state: string
  phase: string
  code: string | null
  completedAtUtc: string | null
}
export interface RunInput {
  runtime: string
  task: string
}
export interface WorkspaceRun extends RunInput {
  runId: string
  workspaceId: string
  state: string
  phase: string
  createdAtUtc: string
  completedAtUtc: string | null
  cancellationRequested: boolean
  errorCode: string | null
  hostStatus: string | null
  reportStatus: string | null
}
export interface Page<T> {
  items: T[]
  nextAfter: string | null
}
export interface RunReport {
  workspaceId: string
  runId: string
  outcome: string
  exitCode: number | null
  finalizedAtUtc: string
  retainUntilUtc: string
  source: { fingerprint: string | null; gitCommit: string | null }
  environment: { digest: string; version: number }
  steps: {
    stepId: string
    key: string
    outcome: string
    exitCode: number | null
    tests: {
      artifactId: string
      format: string
      integrity: string
      collection?: string
      result: {
        state: string
        counts: { passed: number; failed: number; errors: number; skipped: number } | null
      } | null
    }[]
  }[]
}

const scope = (id: string) => `${endpoint}/${encodeURIComponent(id)}`
const runScope = (id: string, runId: string) => `${scope(id)}/runs/${encodeURIComponent(runId)}`
const pageQuery = (after?: string) =>
  after ? `?take=20&after=${encodeURIComponent(after)}` : '?take=20'
const post = (signal: AbortSignal, body?: object, key?: string): RequestInit => ({
  method: 'POST',
  signal,
  body: body ? JSON.stringify(body) : undefined,
  headers: key ? { 'Idempotency-Key': key } : undefined,
})

export const developmentWorkspaces = {
  catalog: (signal: AbortSignal) => apiRequest<WorkspaceCatalog>(`${endpoint}/catalog`, { signal }),
  list: (signal: AbortSignal, after?: string) =>
    apiRequest<Page<Workspace>>(endpoint + pageQuery(after), { signal }),
  get: (id: string, signal: AbortSignal) => apiRequest<Workspace>(scope(id), { signal }),
  register: (input: WorkspaceRegistration, key: string, signal: AbortSignal) =>
    apiRequest<{ workspace: Workspace; replayed: boolean }>(endpoint, post(signal, input, key)),
  plan: (id: string, signal: AbortSignal) =>
    apiRequest<StoragePlan>(`${scope(id)}/storage-plan`, { signal }),
  allocate: (id: string, planSha256: string, signal: AbortSignal) =>
    apiRequest<Workspace>(`${scope(id)}/storage-allocation`, post(signal, { planSha256 })),
  preflight: (id: string, signal: AbortSignal) =>
    apiRequest<{ workspaceId: string; workspaceVersion: number; environmentSha256: string }>(
      `${scope(id)}/runner-preflight`,
      post(signal),
    ),
  lifecyclePlan: (id: string, action: 'stop' | 'resume', signal: AbortSignal) =>
    apiRequest<LifecyclePlan>(`${scope(id)}/lifecycle/plan?action=${action}`, { signal }),
  lifecycleCurrent: (id: string, signal: AbortSignal) =>
    apiRequest<LifecycleOperation | null>(`${scope(id)}/lifecycle`, { signal }),
  lifecycleGet: (id: string, operationId: string, signal: AbortSignal) =>
    apiRequest<LifecycleOperation>(`${scope(id)}/lifecycle/${encodeURIComponent(operationId)}`, {
      signal,
    }),
  lifecycleApply: (id: string, plan: LifecyclePlan, key: string, signal: AbortSignal) =>
    apiRequest<LifecycleOperation>(
      `${scope(id)}/lifecycle`,
      post(signal, { action: plan.action, planSha256: plan.planSha256 }, key),
    ),
  lifecycleAuthorize: (id: string, operationId: string, signal: AbortSignal) =>
    apiRequest<LifecycleOperation>(
      `${scope(id)}/lifecycle/${encodeURIComponent(operationId)}/authorize`,
      post(signal),
    ),
  listRuns: (id: string, signal: AbortSignal, after?: string) =>
    apiRequest<Page<WorkspaceRun>>(`${scope(id)}/runs` + pageQuery(after), { signal }),
  run: (id: string, runId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRun>(runScope(id, runId), { signal }),
  queue: (id: string, input: RunInput, key: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRun>(`${scope(id)}/runs`, post(signal, input, key)),
  cancel: (id: string, runId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRun>(`${runScope(id, runId)}/cancel`, post(signal)),
}

export async function readRunReport(
  id: string,
  runId: string,
  format: 'json' | 'markdown',
  signal: AbortSignal,
) {
  const response = await fetch(apiUrl(`${runScope(id, runId)}/report/${format}`), {
    credentials: 'include',
    signal: AbortSignal.any([signal, AbortSignal.timeout(15_000)]),
    cache: 'no-store',
  })
  if (!response.ok) throw await toApiError(response)
  const digest = response.headers.get('X-Content-SHA256') ?? ''
  const reader = response.body?.getReader()
  if (!reader || !/^[a-f0-9]{64}$/.test(digest)) throw new Error('报告完整性核验失败。')
  const chunks: Uint8Array[] = []
  let length = 0
  try {
    while (true) {
      const chunk = await reader.read()
      if (chunk.done) break
      length += chunk.value.length
      if (length > reportLimit) throw new Error('报告超过下载上限。')
      chunks.push(chunk.value)
    }
  } finally {
    await reader.cancel().catch(() => undefined)
  }
  const bytes = new Uint8Array(length)
  let offset = 0
  for (const chunk of chunks) {
    bytes.set(chunk, offset)
    offset += chunk.length
  }
  if (!length || bytesToHex(sha256(bytes)) !== digest) throw new Error('报告完整性核验失败。')
  return { bytes, digest }
}

export function workspaceError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.problem.code === 'WORKSPACE_ARCHIVE_NOT_SETTLED')
      return '归档尚未取得原执行结束确认。请先完成或取消归档，再恢复空间。'
    if (error.problem.code === 'WORKSPACE_LIFECYCLE_NOT_IDLE')
      return '空间还有运行、导入或报告处理。请先完成或取消任务，再核对停用计划。'
    if (error.problem.code === 'WORKSPACE_LIFECYCLE_GIT_DRAINING')
      return '刚取消的 Git 获取仍在确认资源收尾，请稍后重新核对停用计划。'
    if (error.problem.code === 'WORKSPACE_LIFECYCLE_INACTIVE')
      return '空间已停用或正在切换状态。恢复后才能提交新任务和源码导入。'
    if (error.status === 403) return '当前账号没有这项开发空间权限，或权限已撤销。'
    if (error.status === 404) return '记录不存在、已到期或当前账号不可见。'
    if (error.status === 409)
      return error.problem.code
        ? `当前状态不允许该操作（${error.problem.code}）。请刷新并核对状态。`
        : '当前状态不允许该操作。请刷新并核对状态。'
    if (error.status < 500) return error.message
  }
  return '未能确认服务器结果。请检查连接后刷新，或用原提交重试。'
}
