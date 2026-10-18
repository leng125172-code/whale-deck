import { apiRequest } from './apiClient'

export interface BuildIdentity {
  component: string
  version: string
  sourceRevision: string | null
}

export function validateBuildIdentity(value: unknown, component: string): BuildIdentity {
  if (!value || typeof value !== 'object') throw new Error('版本信息无效')
  const row = value as Record<string, unknown>
  if (
    row.component !== component ||
    typeof row.version !== 'string' ||
    !/^(?:\d{1,6}\.){2,3}\d{1,6}$/.test(row.version) ||
    (row.sourceRevision !== null &&
      (typeof row.sourceRevision !== 'string' || !/^[a-f0-9]{40}$/.test(row.sourceRevision)))
  )
    throw new Error('版本信息无效')
  return { component, version: row.version, sourceRevision: row.sourceRevision as string | null }
}

export async function loadPlatformBuild(signal: AbortSignal) {
  const [api, gateway] = await Promise.allSettled([
    apiRequest<{ build: unknown }>('system/status', { signal }).then((row) =>
      validateBuildIdentity(row.build, 'WhaleDeck.Api'),
    ),
    fetch('/build-info.json', { signal, cache: 'no-store', credentials: 'same-origin' }).then(
      async (response) => {
        if (!response.ok) throw new Error('网页版本信息不可用')
        return validateBuildIdentity(await response.json(), 'WhaleDeck.Gateway')
      },
    ),
  ])
  return {
    api: api.status === 'fulfilled' ? api.value : null,
    gateway: gateway.status === 'fulfilled' ? gateway.value : null,
  }
}
