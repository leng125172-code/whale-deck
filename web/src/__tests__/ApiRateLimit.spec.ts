import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

describe('API rate limit feedback', () => {
  beforeEach(() => vi.resetModules())
  afterEach(() => vi.unstubAllGlobals())

  it('keeps 429 separate from dependency failure and exposes a bounded wait', async () => {
    const fetcher = vi
      .fn<typeof fetch>()
      .mockResolvedValue(
        new Response(
          JSON.stringify({ code: 'API_RATE_LIMITED', title: 'budget', traceId: 'fixture' }),
          { status: 429, headers: { 'Retry-After': '60' } },
        ),
      )
    vi.stubGlobal('fetch', fetcher)
    const { apiRequest } = await import('@/services/apiClient')
    await expect(apiRequest('fixture')).rejects.toMatchObject({
      status: 429,
      retryAfterSeconds: 60,
      message: '请求过于频繁，请 60 秒后再试。',
      problem: { code: 'API_RATE_LIMITED', traceId: 'fixture' },
    })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it.each(['', '0', '-1', 'not-a-duration', '3601', '999999999999'])(
    'rejects invalid Retry-After %s without automatic retry',
    async (header) => {
      const fetcher = vi
        .fn<typeof fetch>()
        .mockResolvedValue(
          new Response('upstream', { status: 429, headers: { 'Retry-After': header } }),
        )
      vi.stubGlobal('fetch', fetcher)
      const { apiRequest } = await import('@/services/apiClient')
      await expect(apiRequest('fixture')).rejects.toMatchObject({
        status: 429,
        retryAfterSeconds: null,
        message: '请求过于频繁，请稍后再试。',
      })
      expect(fetcher).toHaveBeenCalledTimes(1)
    },
  )

  it('never repeats a write after rate rejection', async () => {
    const fetcher = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ headerName: 'X-XSRF-TOKEN', token: 'fixture-only' })),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ code: 'API_RATE_LIMITED' }), {
          status: 429,
          headers: { 'Retry-After': '15' },
        }),
      )
    vi.stubGlobal('fetch', fetcher)
    const { apiRequest } = await import('@/services/apiClient')
    await expect(apiRequest('fixture', { method: 'POST', body: '{}' })).rejects.toMatchObject({
      status: 429,
      retryAfterSeconds: 15,
    })
    expect(fetcher).toHaveBeenCalledTimes(2) // one CSRF read, one attempted write
  })
})
