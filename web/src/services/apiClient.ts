import { runtimeConfig } from '@/config/runtime'
import { notifySessionExpired } from '@/services/sessionExpiry'

export interface ApiProblem {
  status?: number
  title?: string
  detail?: string
  code?: string
  traceId?: string
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly problem: ApiProblem,
    readonly retryAfterSeconds: number | null = null,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

let csrf: { headerName: string; token: string } | null = null

export function apiUrl(path: string) {
  const base = runtimeConfig.apiBaseUrl.replace(/\/$/, '')
  return `${base}/${path.replace(/^\//, '')}`
}

export async function csrfHeaders(signal: AbortSignal) {
  if (!csrf) {
    const response = await fetch(apiUrl('auth/csrf'), {
      credentials: 'include',
      headers: { Accept: 'application/json' },
      signal,
    })
    if (!response.ok) throw await toApiError(response)
    csrf = (await response.json()) as { headerName: string; token: string }
  }
  return { [csrf.headerName]: csrf.token }
}

export async function toApiError(response: Response) {
  if (response.status === 401) {
    csrf = null
    notifySessionExpired()
  }
  let problem: ApiProblem = { status: response.status }
  try {
    problem = { ...problem, ...((await response.json()) as ApiProblem) }
  } catch {
    // Non-JSON upstream errors are deliberately reduced to the HTTP status.
  }
  const retryHeader = response.headers.get('Retry-After') || ''
  const retrySeconds = /^[0-9]{1,6}$/.test(retryHeader) ? Number(retryHeader) : null
  const retryAfter =
    retrySeconds !== null && retrySeconds > 0 && retrySeconds <= 3600 ? retrySeconds : null
  const message =
    response.status === 429
      ? retryAfter === null
        ? '请求过于频繁，请稍后再试。'
        : `请求过于频繁，请 ${retryAfter} 秒后再试。`
      : problem.title || `请求失败 (${response.status})`
  // Do not automatically retry a write: the operation may already have been committed.
  return new ApiError(message, response.status, problem, retryAfter)
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method || 'GET').toUpperCase()
  const writes = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  const controller = new AbortController()
  const externalAbort = () => controller.abort()
  if (init.signal?.aborted) controller.abort()
  else init.signal?.addEventListener('abort', externalAbort, { once: true })
  const timeout = window.setTimeout(() => controller.abort(), 15_000)
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  try {
    if (writes) {
      for (const [name, value] of Object.entries(await csrfHeaders(controller.signal)))
        headers.set(name, value)
    }
    const response = await fetch(apiUrl(path), {
      ...init,
      credentials: 'include',
      headers,
      signal: controller.signal,
    })
    if (!response.ok) {
      if (response.status === 400 && writes) csrf = null
      throw await toApiError(response)
    }
    if (response.status === 204) return undefined as T
    return (await response.json()) as T
  } finally {
    window.clearTimeout(timeout)
    init.signal?.removeEventListener('abort', externalAbort)
  }
}

export function createIdempotencyKey() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  // HTTP/IP deployments are not secure contexts; getRandomValues remains
  // available there, while randomUUID may not. Never fall back to Math.random.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6]! & 0x0f) | 0x40
  bytes[8] = (bytes[8]! & 0x3f) | 0x80
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
