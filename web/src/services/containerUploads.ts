import {
  ApiError,
  apiRequest,
  apiUrl,
  createIdempotencyKey,
  csrfHeaders,
  toApiError,
} from '@/services/apiClient'
import type { ContainerFileTarget } from '@/services/containerFiles'
import { containerFileBase, containerFileQuery } from '@/services/containerFiles'
import type { FileEntry, FilePlan } from '@/services/fileManagement'
import { maximumFileBytes } from '@/services/fileManagement'

export interface ContainerUploadLocation {
  containerId: string
  instanceId: string
  path: string
}

export interface ContainerUploadIntent {
  id: string
  jobId: string
  location: ContainerUploadLocation
  sizeBytes: number
  sha256: string
  state: string
  expiresAtUtc: string
  resultCode: string | null
  version: number
  publishedVersion: string | null
  previousRetained: boolean
}

export interface ContainerUploadRegistration {
  planHash: string
  confirmed: true
  idempotencyKey: string
}

const uuid = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/
const emptyUuid = '00000000-0000-0000-0000-000000000000'
const hash = /^[A-F0-9]{64}$/
const states = [
  'AwaitingUpload',
  'Receiving',
  'Ready',
  'Publishing',
  'Unknown',
  'Succeeded',
  'Superseded',
  'Failed',
  'Canceled',
]
const terminalStates = ['Succeeded', 'Superseded', 'Failed', 'Canceled']

function exactKeys(value: unknown, fields: readonly string[]): value is Record<string, unknown> {
  return (
    !!value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === fields.length &&
    Object.keys(value).every((key) => fields.includes(key))
  )
}

function validPath(value: unknown): value is string {
  if (
    typeof value !== 'string' ||
    !value ||
    new TextEncoder().encode(value).length > 4096 ||
    value.startsWith('/') ||
    value.endsWith('/') ||
    value.includes('\\') ||
    value.includes('//') ||
    !validPathCharacters(value)
  )
    return false
  return value
    .split('/')
    .every(
      (part) =>
        part !== '' &&
        part !== '.' &&
        part !== '..' &&
        new TextEncoder().encode(part).length <= 255,
    )
}

function validPathCharacters(value: string) {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index)
    if (code < 0x20 || (code >= 0x7f && code <= 0x9f)) return false
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(index + 1)
      if (next < 0xdc00 || next > 0xdfff) return false
      index++
    } else if (code >= 0xdc00 && code <= 0xdfff) return false
  }
  return true
}

function validInstance(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match =
    /^([1-9][0-9]{0,9}):[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z$/.exec(
      value,
    )
  if (!match) return false
  const pid = Number(match[1])
  return Number.isSafeInteger(pid) && pid >= 2 && pid <= 2_147_483_647
}

export function containerUploadState(state: string) {
  return (
    (
      {
        AwaitingUpload: '等待传输',
        Receiving: '接收中',
        Ready: '已暂存，等待发布',
        Publishing: '正在发布',
        Unknown: '结果未知，已停止自动操作',
        Succeeded: '已发布',
        Superseded: '已发布，但当前目标随后发生变化',
        Failed: '上传失败',
        Canceled: '已取消',
      } as Record<string, string>
    )[state] || '无法核实的上传状态'
  )
}

export function containerUploadTerminal(state: string) {
  return terminalStates.includes(state)
}

export function assertContainerUploadPlan(value: FilePlan) {
  if (
    !exactKeys(value, ['planHash', 'expiresAtUtc', 'overwrites', 'warning']) ||
    typeof value.planHash !== 'string' ||
    !value.planHash ||
    value.planHash.length > 65536 ||
    typeof value.expiresAtUtc !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
    typeof value.overwrites !== 'boolean' ||
    typeof value.warning !== 'string' ||
    !value.warning ||
    value.warning.length > 2048
  )
    throw new Error('容器上传计划无法核实')
  return value
}

export function assertContainerUploadIntent(
  value: ContainerUploadIntent,
  expected?: { target: ContainerFileTarget; path: string; sizeBytes: number; sha256: string },
) {
  const location = value?.location
  if (
    !exactKeys(value, [
      'id',
      'jobId',
      'location',
      'sizeBytes',
      'sha256',
      'state',
      'expiresAtUtc',
      'resultCode',
      'version',
      'publishedVersion',
      'previousRetained',
    ]) ||
    !uuid.test(value.id) ||
    value.id === emptyUuid ||
    !uuid.test(value.jobId) ||
    value.jobId !== value.id ||
    !exactKeys(location, ['containerId', 'instanceId', 'path']) ||
    typeof location.containerId !== 'string' ||
    !/^[a-f0-9]{64}$/.test(location.containerId) ||
    !validInstance(location.instanceId) ||
    !validPath(location.path) ||
    !Number.isSafeInteger(value.sizeBytes) ||
    value.sizeBytes < 0 ||
    value.sizeBytes > maximumFileBytes ||
    !hash.test(value.sha256) ||
    !states.includes(value.state) ||
    typeof value.expiresAtUtc !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
    (value.resultCode !== null &&
      (typeof value.resultCode !== 'string' ||
        value.resultCode.length > 64 ||
        !/^CONTAINER_UPLOAD_[A-Z_]+$/.test(value.resultCode))) ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    (value.publishedVersion !== null && !hash.test(value.publishedVersion)) ||
    typeof value.previousRetained !== 'boolean' ||
    (['Succeeded', 'Superseded'].includes(value.state) && value.publishedVersion === null) ||
    (expected !== undefined &&
      (location.containerId !== expected.target.containerId ||
        location.instanceId !== expected.target.instanceId ||
        location.path !== expected.path ||
        value.sizeBytes !== expected.sizeBytes ||
        value.sha256 !== expected.sha256))
  )
    throw new Error('容器上传任务或实例绑定无法核实；保留任务编号，不重传文件')
  return value
}

export function containerUploadFollows(
  previous: ContainerUploadIntent,
  current: ContainerUploadIntent,
) {
  const bindingUnchanged =
    previous.id === current.id &&
    previous.jobId === current.jobId &&
    previous.location.containerId === current.location.containerId &&
    previous.location.instanceId === current.location.instanceId &&
    previous.location.path === current.location.path &&
    previous.sizeBytes === current.sizeBytes &&
    previous.sha256 === current.sha256 &&
    previous.expiresAtUtc === current.expiresAtUtc
  if (!bindingUnchanged || current.version < previous.version) return false
  const unchanged =
    previous.state === current.state &&
    previous.resultCode === current.resultCode &&
    previous.publishedVersion === current.publishedVersion &&
    previous.previousRetained === current.previousRetained
  if (current.version === previous.version) return unchanged
  return (
    !containerUploadTerminal(previous.state) &&
    (!previous.previousRetained || current.previousRetained)
  )
}

export async function inspectContainerUploadEntry(
  target: ContainerFileTarget,
  path: string,
  signal: AbortSignal,
) {
  try {
    return await apiRequest<FileEntry>(
      `${containerFileBase(target.containerId)}/entry?${containerFileQuery(target, path)}`,
      { signal },
    )
  } catch (reason) {
    if (reason instanceof ApiError && reason.status === 404) return null
    throw reason
  }
}

export async function planContainerUpload(
  target: ContainerFileTarget,
  path: string,
  file: File,
  sha256: string,
  expectedVersion: string,
  signal: AbortSignal,
) {
  if (
    !validPath(path) ||
    !hash.test(sha256) ||
    !Number.isSafeInteger(file.size) ||
    file.size > maximumFileBytes
  )
    throw new Error('容器上传文件、路径或摘要无效')
  if (expectedVersion !== 'ABSENT' && !hash.test(expectedVersion))
    throw new Error('容器目标版本无法核实')
  return assertContainerUploadPlan(
    await apiRequest<FilePlan>(`${containerFileBase(target.containerId)}/uploads/plan`, {
      method: 'POST',
      signal,
      body: JSON.stringify({
        instanceId: target.instanceId,
        path,
        sizeBytes: file.size,
        sha256,
        expectedVersion,
      }),
    }),
  )
}

export function newContainerUploadRegistration(planHash: string): ContainerUploadRegistration {
  return { planHash, confirmed: true, idempotencyKey: createIdempotencyKey() }
}

export async function createContainerUpload(
  target: ContainerFileTarget,
  registration: ContainerUploadRegistration,
  expected: { path: string; sizeBytes: number; sha256: string },
  signal: AbortSignal,
) {
  return assertContainerUploadIntent(
    await apiRequest<ContainerUploadIntent>(`${containerFileBase(target.containerId)}/uploads`, {
      method: 'POST',
      signal,
      headers: { 'Idempotency-Key': registration.idempotencyKey },
      body: JSON.stringify(registration),
    }),
    { target, ...expected },
  )
}

/** Sends the selected bytes at most once per caller invocation; callers must never retry it automatically. */
export async function uploadContainerContent(
  target: ContainerFileTarget,
  intent: ContainerUploadIntent,
  file: File,
  signal: AbortSignal,
  progress: (percent: number) => void,
): Promise<ContainerUploadIntent> {
  assertContainerUploadIntent(intent, {
    target,
    path: intent.location.path,
    sizeBytes: file.size,
    sha256: intent.sha256,
  })
  const headers = await csrfHeaders(signal)
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const request = new XMLHttpRequest()
    const abort = () => request.abort()
    const finish = () => signal.removeEventListener('abort', abort)
    signal.addEventListener('abort', abort, { once: true })
    request.open(
      'PUT',
      apiUrl(`${containerFileBase(target.containerId)}/uploads/${intent.id}/content`),
    )
    request.withCredentials = true
    request.timeout = 600_000
    request.setRequestHeader('Accept', 'application/json')
    request.setRequestHeader('Content-Type', 'application/octet-stream')
    request.setRequestHeader('Idempotency-Key', intent.id)
    for (const [key, value] of Object.entries(headers)) request.setRequestHeader(key, value)
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) progress(Math.min(100, (event.loaded / event.total) * 100))
    }
    request.onload = async () => {
      finish()
      try {
        if (request.status !== 202)
          throw await toApiError(
            new Response(request.responseText, {
              status: request.status,
              headers: { 'Content-Type': 'application/json' },
            }),
          )
        resolve(
          assertContainerUploadIntent(JSON.parse(request.responseText) as ContainerUploadIntent, {
            target,
            path: intent.location.path,
            sizeBytes: file.size,
            sha256: intent.sha256,
          }),
        )
      } catch (reason) {
        reject(reason)
      }
    }
    request.onerror = () => {
      finish()
      reject(new Error('上传连接中断；只查询原任务，不会重新发送文件字节'))
    }
    request.ontimeout = () => {
      finish()
      reject(new Error('上传超时；只查询原任务，不会重新发送文件字节'))
    }
    request.onabort = () => {
      finish()
      reject(new DOMException('Aborted', 'AbortError'))
    }
    request.send(file)
  })
}

export async function getContainerUpload(
  target: ContainerFileTarget,
  id: string,
  expected: { path: string; sizeBytes: number; sha256: string },
  signal: AbortSignal,
) {
  if (!uuid.test(id)) throw new Error('容器上传任务编号无效')
  return assertContainerUploadIntent(
    await apiRequest<ContainerUploadIntent>(
      `${containerFileBase(target.containerId)}/uploads/${id}`,
      { signal },
    ),
    { target, ...expected },
  )
}

export async function cancelContainerUpload(
  target: ContainerFileTarget,
  id: string,
  signal: AbortSignal,
) {
  if (!uuid.test(id)) throw new Error('容器上传任务编号无效')
  await apiRequest(`${containerFileBase(target.containerId)}/uploads/${id}/cancel`, {
    method: 'POST',
    signal,
    headers: { 'Idempotency-Key': id },
  })
}
