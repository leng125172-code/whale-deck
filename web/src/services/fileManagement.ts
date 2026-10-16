import { apiUrl, csrfHeaders, toApiError } from '@/services/apiClient'

export interface FileRoot {
  id: string
  name: string
  actions: string[]
}
export interface FileEntry {
  name: string
  kind: string
  sizeBytes: number
  modifiedAtUnixMs: number
  version: string
  mode: string
  isProtected: boolean
}
export interface FilePage {
  entries: FileEntry[]
  nextCursor: string
  version: string
}
export interface FilePlan {
  planHash: string
  expiresAtUtc: string
  overwrites: boolean
  warning: string
}
export interface FileIntent {
  id: string
  jobId: string
  rootId: string
  path: string
  sizeBytes: number
  sha256: string
  state: string
  expiresAtUtc: string
  resultCode: string | null
  version: number
  publishedVersion: string | null
  previousRetained: boolean
}
export const maximumFileBytes = 1024 ** 3
export function fileQuery(rootId: string, path: string, extra: Record<string, string> = {}) {
  return new URLSearchParams({ rootId, path, ...extra }).toString()
}
export function filePath(parent: string, name: string) {
  return parent ? `${parent}/${name}` : name
}
export function fileTerminal(state: string) {
  return ['Succeeded', 'Superseded', 'Failed', 'Canceled'].includes(state)
}
export function fileState(state: string) {
  return (
    (
      {
        AwaitingUpload: '等待传输',
        Receiving: '接收中',
        Ready: '已暂存，等待发布',
        Publishing: '任务发布中',
        Unknown: '结果未知，禁止自动重试',
        Succeeded: '已发布',
        Superseded: '历史发布成功，当前目标已变化',
        Failed: '失败',
        Canceled: '已取消',
      } as Record<string, string>
    )[state] || state
  )
}

export function hashFile(
  file: File,
  signal: AbortSignal,
  progress: (percent: number) => void,
): Promise<string> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const worker = new Worker(new URL('./fileHash.worker.ts', import.meta.url), { type: 'module' })
    const finish = () => {
      signal.removeEventListener('abort', abort)
      worker.terminate()
    }
    const abort = () => {
      finish()
      reject(new DOMException('Aborted', 'AbortError'))
    }
    signal.addEventListener('abort', abort, { once: true })
    worker.onmessage = (
      event: MessageEvent<{ progress?: number; digest?: string; failed?: boolean }>,
    ) => {
      if (event.data.failed) {
        finish()
        reject(new Error('计算文件摘要失败'))
        return
      }
      if (event.data.progress !== undefined) progress(event.data.progress)
      if (event.data.digest) {
        finish()
        resolve(event.data.digest)
      }
    }
    worker.onerror = () => {
      finish()
      reject(new Error('文件校验线程不可用'))
    }
    worker.postMessage(file)
  })
}

/** One receive attempt only. Never automatically resend a potentially accepted body. */
export async function uploadFile(
  intent: FileIntent,
  file: File,
  signal: AbortSignal,
  progress: (percent: number) => void,
): Promise<FileIntent> {
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
    request.open('PUT', apiUrl(`files/uploads/${intent.id}/content`))
    request.withCredentials = true
    request.timeout = 600_000
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
        resolve(JSON.parse(request.responseText) as FileIntent)
      } catch (error) {
        reject(error)
      }
    }
    request.onerror = () => {
      finish()
      reject(new Error('上传连接中断；请查询原任务，不要自动重发'))
    }
    request.ontimeout = () => {
      finish()
      reject(new Error('上传超时；请查询原任务，不要自动重发'))
    }
    request.onabort = () => {
      finish()
      reject(new DOMException('Aborted', 'AbortError'))
    }
    request.send(file)
  })
}

export async function downloadFile(
  rootId: string,
  path: string,
  entry: FileEntry,
  signal: AbortSignal,
  progress: (percent: number) => void,
): Promise<Blob> {
  const response = await fetch(
    apiUrl(`files/download?${fileQuery(rootId, path, { expectedVersion: entry.version })}`),
    { credentials: 'include', signal, headers: { Accept: 'application/octet-stream' } },
  )
  if (!response.ok) throw await toApiError(response)
  if (!response.body || response.headers.get('Content-Length') !== String(entry.sizeBytes))
    throw new Error('下载大小已变化，请刷新目录')
  let size = 0
  const stream = response.body.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        size += chunk.length
        if (size > entry.sizeBytes) throw new Error('下载超出声明大小')
        progress(entry.sizeBytes === 0 ? 100 : (size / entry.sizeBytes) * 100)
        controller.enqueue(chunk)
      },
    }),
  )
  // Blob storage is managed by the browser. Never concatenate a 1GiB JS array.
  const blob = await new Response(stream).blob()
  if (size !== entry.sizeBytes || blob.size !== entry.sizeBytes)
    throw new Error('下载未完整结束，未生成保存链接')
  return blob
}
