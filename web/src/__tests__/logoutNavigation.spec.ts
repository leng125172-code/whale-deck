import { afterEach, describe, expect, it, vi } from 'vitest'
import { navigateToLogout } from '@/services/logoutNavigation'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  document.body.replaceChildren()
})

describe('OIDC logout navigation', () => {
  it('uses a top-level CSRF-protected POST rather than a fetch logout', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          formFieldName: '__RequestVerificationToken',
          token: 'test-csrf',
        }),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetch)
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    await navigateToLogout()
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(String(fetch.mock.calls[0]?.[0])).toContain('/auth/csrf')
    const form = document.querySelector('form')!
    expect(form.method).toBe('post')
    expect(form.action).toContain('/api/v1/auth/logout')
    expect(form.hidden).toBe(true)
    expect(form.querySelector('input')?.name).toBe('__RequestVerificationToken')
    expect(form.querySelector('input')?.value).toBe('test-csrf')
    expect(submit).toHaveBeenCalledTimes(1)
  })

  it('does not navigate or discard the session when CSRF retrieval fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    await expect(navigateToLogout()).rejects.toThrow('请求失败 (503)')
    expect(submit).not.toHaveBeenCalled()
    expect(document.querySelector('form')).toBeNull()
  })
})
