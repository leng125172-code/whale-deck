import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const stops: Array<() => void> = []
const admin = {
  subject: 'session-test-admin',
  name: 'Session test',
  groups: ['administrators'],
  isAdministrator: true,
  permissionExpiresAtUtc: '2026-10-22T16:00:00Z',
}
const signedOut = JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) })

beforeEach(() => {
  vi.resetModules()
  vi.stubEnv('DEV', false)
  sessionStorage.clear()
  localStorage.clear()
})
afterEach(() => {
  for (const stop of stops.splice(0)) stop()
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
})

async function setup() {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify(admin)))
  vi.stubGlobal('fetch', fetcher)
  const { authSession } = await import('@/services/authSession')
  const expired = vi.fn<() => void>()
  stops.push(authSession.bindLifecycle(expired))
  await authSession.initialize()
  return { authSession, fetcher, expired }
}

it('clears current identity and private location once on a real BFF 401', async () => {
  const { authSession, fetcher, expired } = await setup()
  const { rememberFileBrowserLocation, fileBrowserLocationKey } =
    await import('@/services/fileBrowserLocation')
  rememberFileBrowserLocation(admin.subject, { rootId: 'whaledeck.files.shared', path: 'private' })
  const { saveCopyHint, copyHint } = await import('@/services/containerCopyHints')
  saveCopyHint({
    subject: admin.subject,
    containerId: 'a'.repeat(64),
    key: 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
    id: null,
  })
  fetcher.mockResolvedValue(new Response('{}', { status: 401 }))
  const { apiRequest } = await import('@/services/apiClient')
  await expect(apiRequest('containers')).rejects.toMatchObject({ status: 401 })
  await expect(apiRequest('containers')).rejects.toMatchObject({ status: 401 })
  expect(authSession.user.value).toBeNull()
  expect(authSession.authenticated.value).toBe(false)
  expect(authSession.unavailable.value).toBe(false)
  expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
  expect(copyHint(admin.subject, 'a'.repeat(64))).toBeNull()
  expect(expired).toHaveBeenCalledOnce()
})

it.each([403, 429, 503])(
  'does not turn endpoint status %s into a global logout',
  async (status) => {
    const { authSession, fetcher, expired } = await setup()
    fetcher.mockResolvedValue(new Response('{}', { status }))
    const { apiRequest } = await import('@/services/apiClient')
    await expect(apiRequest('containers')).rejects.toMatchObject({ status })
    expect(authSession.user.value?.subject).toBe(admin.subject)
    expect(authSession.authenticated.value).toBe(true)
    expect(expired).not.toHaveBeenCalled()
  },
)

it('does not label a network failure as logout', async () => {
  const { authSession, fetcher, expired } = await setup()
  fetcher.mockRejectedValue(new TypeError('offline'))
  const { apiRequest } = await import('@/services/apiClient')
  await expect(apiRequest('containers')).rejects.toThrow('offline')
  expect(authSession.authenticated.value).toBe(true)
  expect(expired).not.toHaveBeenCalled()
})

it('invalidates for a binary/API error transport too and drops cached CSRF', async () => {
  const { authSession, fetcher } = await setup()
  const { csrfHeaders, toApiError } = await import('@/services/apiClient')
  fetcher.mockResolvedValueOnce(
    new Response(JSON.stringify({ headerName: 'X-CSRF', token: 'first' })),
  )
  expect(await csrfHeaders(new AbortController().signal)).toEqual({ 'X-CSRF': 'first' })
  await toApiError(new Response('{}', { status: 401 }))
  expect(authSession.authenticated.value).toBe(false)
  fetcher.mockResolvedValueOnce(
    new Response(JSON.stringify({ headerName: 'X-CSRF', token: 'next' })),
  )
  expect(await csrfHeaders(new AbortController().signal)).toEqual({ 'X-CSRF': 'next' })
})

it('cannot resurrect administrator state from a late in-flight refresh', async () => {
  const { authSession, fetcher, expired } = await setup()
  let finish!: (response: Response) => void
  fetcher.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)))
  const refresh = authSession.refresh()
  fetcher.mockResolvedValueOnce(new Response('{}', { status: 401 }))
  const { apiRequest } = await import('@/services/apiClient')
  await expect(apiRequest('containers')).rejects.toMatchObject({ status: 401 })
  finish(new Response(JSON.stringify(admin)))
  await refresh
  expect(authSession.user.value).toBeNull()
  expect(authSession.authenticated.value).toBe(false)
  expect(expired).toHaveBeenCalledOnce()
})

it('cannot resurrect initial identity after cross-tab expiry during initialization', async () => {
  let finish!: (response: Response) => void
  vi.stubGlobal(
    'fetch',
    vi.fn<typeof fetch>(() => new Promise((resolve) => (finish = resolve))),
  )
  const { authSession } = await import('@/services/authSession')
  const expired = vi.fn<() => void>()
  stops.push(authSession.bindLifecycle(expired))
  const initialization = authSession.initialize()
  const { notifySessionExpired } = await import('@/services/sessionExpiry')
  notifySessionExpired()
  finish(new Response(JSON.stringify(admin)))
  await initialization
  expect(authSession.initialized.value).toBe(true)
  expect(authSession.user.value).toBeNull()
  expect(authSession.authenticated.value).toBe(false)
  expect(expired).not.toHaveBeenCalled()
})

it('accepts only a bounded logout-only marker from the same local storage area', async () => {
  const { authSession, expired } = await setup()
  const { sessionEndedStorageKey } = await import('@/services/sessionExpiry')
  const dispatch = (newValue: string | null, storageArea: Storage | null = localStorage) =>
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: sessionEndedStorageKey,
        newValue,
        storageArea,
        url: window.location.href,
      }),
    )
  for (const value of [null, '', '{}', 'not-json', signedOut + 'x', 'x'.repeat(161)])
    dispatch(value)
  dispatch(signedOut, sessionStorage)
  expect(authSession.authenticated.value).toBe(true)
  dispatch(signedOut)
  expect(authSession.authenticated.value).toBe(false)
  expect(expired).toHaveBeenCalledOnce()
})

it('replaces lifecycle handlers without accumulating callbacks and can dispose them', async () => {
  const { authSession, expired } = await setup()
  const latest = vi.fn<() => void>()
  const stop = authSession.bindLifecycle(latest)
  stops.push(stop)
  const { notifySessionExpired } = await import('@/services/sessionExpiry')
  stops[0]!()
  notifySessionExpired()
  expect(expired).not.toHaveBeenCalled()
  expect(latest).toHaveBeenCalledOnce()
  stop()
  notifySessionExpired()
  expect(latest).toHaveBeenCalledOnce()
})

it('publishes no credentials, identity or role and tolerates disabled storage', async () => {
  const { publishSessionEnded, sessionEndedStorageKey, validSessionEndedSignal } =
    await import('@/services/sessionExpiry')
  publishSessionEnded()
  const signal = localStorage.getItem(sessionEndedStorageKey)
  expect(validSessionEndedSignal(signal)).toBe(true)
  expect(Object.keys(JSON.parse(signal!)).sort()).toEqual(['id', 'kind', 'version'])
  for (const extra of [{ token: 'x' }, { administrator: true }, { subject: 'other' }])
    expect(validSessionEndedSignal(JSON.stringify({ ...JSON.parse(signedOut), ...extra }))).toBe(
      false,
    )
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new DOMException('Storage denied', 'SecurityError')
  })
  expect(publishSessionEnded).not.toThrow()
})

it('does not broadcast logout when CSRF acquisition fails', async () => {
  const { authSession, fetcher } = await setup()
  const { sessionEndedStorageKey } = await import('@/services/sessionExpiry')
  fetcher.mockResolvedValueOnce(
    new Response(JSON.stringify({ title: 'CSRF unavailable' }), { status: 503 }),
  )
  await expect(authSession.logout()).rejects.toThrow('CSRF unavailable')
  expect(localStorage.getItem(sessionEndedStorageKey)).toBeNull()
  expect(authSession.authenticated.value).toBe(true)
})

it('broadcasts only after preparing the original CSRF-protected logout navigation', async () => {
  const { authSession, fetcher } = await setup()
  const { sessionEndedStorageKey, validSessionEndedSignal } =
    await import('@/services/sessionExpiry')
  const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
  fetcher.mockResolvedValueOnce(
    new Response(
      JSON.stringify({ formFieldName: '__RequestVerificationToken', token: 'fixture-only' }),
    ),
  )
  await authSession.logout()
  expect(submit).toHaveBeenCalledOnce()
  expect(validSessionEndedSignal(localStorage.getItem(sessionEndedStorageKey))).toBe(true)
  const form = document.querySelector('form')!
  expect(form.method).toBe('post')
  expect(form.action).toMatch(/\/api\/v1\/auth\/logout$/)
  expect(form.querySelector('input')?.name).toBe('__RequestVerificationToken')
  expect(authSession.authenticated.value).toBe(true)
  form.remove()
})
