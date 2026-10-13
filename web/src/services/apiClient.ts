import { runtimeConfig } from '@/config/runtime'

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

async function csrfHeaders(signal: AbortSignal) {
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

async function toApiError(response: Response) {
  let problem: ApiProblem = { status: response.status }
  try {
    problem = { ...problem, ...((await response.json()) as ApiProblem) }
  } catch {
    // Non-JSON upstream errors are deliberately reduced to the HTTP status.
  }
  return new ApiError(problem.title || `请求失败 (${response.status})`, response.status, problem)
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
  return crypto.randomUUID()
}
