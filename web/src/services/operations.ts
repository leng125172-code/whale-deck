import { apiRequest, apiUrl, createIdempotencyKey } from '@/services/apiClient'
import type { Job, JobEvent } from '@/services/contracts'

export interface OperationPlan {
  planHash: string
  expiresAtUtc: string
  changes: string[]
  warnings: string[]
}

export async function planOperation(
  area: string,
  action: string,
  resourceId: string | null,
  parameters: Record<string, string>,
) {
  return apiRequest<OperationPlan>(`${area}/${action}/plan`, {
    method: 'POST',
    body: JSON.stringify({ resourceId, parameters }),
  })
}

export async function enqueueOperation(
  area: string,
  action: string,
  resourceId: string | null,
  parameters: Record<string, string>,
  confirmed: boolean,
  planHash: string | null = null,
) {
  const idempotencyKey = createIdempotencyKey()
  return apiRequest<Job>(`${area}/${action}`, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify({ resourceId, parameters, planHash, confirmed, idempotencyKey }),
  })
}

export async function stageSecret(value?: string) {
  return apiRequest<{ token: string; expiresAtUtc: string }>('secrets/stage', {
    method: 'POST',
    body: JSON.stringify({ value: value || null, generate: !value }),
  })
}

export async function confirmedOperation(
  area: string,
  action: string,
  resourceId: string | null,
  parameters: Record<string, string>,
) {
  const plan = await planOperation(area, action, resourceId, parameters)
  return {
    plan,
    submit: () => enqueueOperation(area, action, resourceId, parameters, true, plan.planHash),
  }
}

export function subscribeToJob(
  jobId: string,
  handlers: { progress: (event: JobEvent) => void; error?: () => void },
) {
  const source = new EventSource(apiUrl(`jobs/${encodeURIComponent(jobId)}/events`), {
    withCredentials: true,
  })
  let lastSequence = 0
  let errors = 0
  let closed = false
  const close = () => {
    closed = true
    source.close()
  }
  source.onopen = () => {
    errors = 0
  }
  source.addEventListener('progress', (message) => {
    if (closed) return
    const event = parseJobEvent(message.data)
    if (!event) {
      close()
      handlers.error?.()
      return
    }
    if (event.sequence <= lastSequence) return
    lastSequence = event.sequence
    if (['Succeeded', 'Failed', 'Canceled', 'RolledBack'].includes(event.state)) close()
    handlers.progress(event)
  })
  source.onerror = () => {
    if (closed) return
    // Keep the same EventSource: the browser owns Last-Event-ID and reconnects
    // without losing the cursor. Bound consecutive failed opens; never retry a write.
    if (source.readyState === EventSource.CLOSED || ++errors >= 5) close()
    handlers.error?.()
  }
  return close
}

export function parseJobEvent(raw: string): JobEvent | null {
  try {
    if (raw.length > 8192) return null
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null
    const data = value as Record<string, unknown>
    const field = (name: string) => data[name] ?? data[name[0]!.toUpperCase() + name.slice(1)]
    const sequence = field('sequence'),
      state = field('state'),
      phase = field('phase')
    const progressPercent = field('progressPercent') ?? null
    const messageCode = field('messageCode'),
      occurredAtUtc = field('occurredAtUtc')
    if (
      !Number.isSafeInteger(sequence) ||
      (sequence as number) <= 0 ||
      typeof state !== 'string' ||
      state.length > 64 ||
      typeof phase !== 'string' ||
      phase.length > 256 ||
      typeof messageCode !== 'string' ||
      messageCode.length > 256 ||
      typeof occurredAtUtc !== 'string' ||
      !Number.isFinite(Date.parse(occurredAtUtc)) ||
      (progressPercent !== null &&
        (typeof progressPercent !== 'number' ||
          !Number.isFinite(progressPercent) ||
          progressPercent < 0 ||
          progressPercent > 100))
    )
      return null
    return {
      sequence: sequence as number,
      state,
      phase,
      progressPercent: progressPercent as number | null,
      messageCode,
      occurredAtUtc,
    }
  } catch {
    return null
  }
}
