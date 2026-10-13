import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('API cancellation budget', () => {
  it('keeps the 15s deadline when the inbox supplies a cancellation signal', async () => {
    let received: AbortSignal | undefined
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((_, init) => {
        received = init!.signal as AbortSignal
        return new Promise((_resolve, reject) =>
          received!.addEventListener('abort', () =>
            reject(new DOMException('Aborted', 'AbortError')),
          ),
        )
      }),
    )
    const { apiRequest } = await import('@/services/apiClient')
    const external = new AbortController()
    const result = apiRequest('notifications', { signal: external.signal }).catch(
      (error: unknown) => error,
    )
    await vi.advanceTimersByTimeAsync(15000)
    expect(await result).toMatchObject({ name: 'AbortError', message: 'Aborted' })
    expect(received!.aborted).toBe(true)
    expect(external.signal.aborted).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })
  it('cancels a CSRF fetch and releases the deadline when a write is cancelled', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(
        (_, init) =>
          new Promise((_resolve, reject) => {
            init!.signal!.addEventListener('abort', () =>
              reject(new DOMException('Aborted', 'AbortError')),
            )
          }),
      ),
    )
    const { apiRequest } = await import('@/services/apiClient')
    const external = new AbortController()
    const result = apiRequest('notifications/read-all', {
      method: 'POST',
      signal: external.signal,
    }).catch((error: unknown) => error)
    external.abort()
    expect(await result).toMatchObject({ name: 'AbortError', message: 'Aborted' })
    expect(vi.getTimerCount()).toBe(0)
  })
  it('releases the timer when CSRF acquisition fails instead of leaking a deadline', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(
        async () => new Response(JSON.stringify({ title: 'Unavailable' }), { status: 503 }),
      ),
    )
    const { apiRequest } = await import('@/services/apiClient')
    await expect(apiRequest('notifications/read-all', { method: 'POST' })).rejects.toThrow(
      'Unavailable',
    )
    expect(vi.getTimerCount()).toBe(0)
  })
})
