import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const { request } = vi.hoisted(() => ({ request: vi.fn<() => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
beforeEach(() => {
  vi.resetModules()
  vi.stubEnv('DEV', false)
  request.mockReset()
})
afterEach(() => vi.unstubAllEnvs())

it('shares one production auth request between router and startup and keeps the ordinary role', async () => {
  let complete!: (value: unknown) => void
  request.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        complete = resolve
      }),
  )
  const { authSession } = await import('@/services/authSession')
  const router = authSession.initialize(),
    startup = authSession.initialize()
  expect(router).toBe(startup)
  expect(authSession.initialized.value).toBe(false)
  expect(request).toHaveBeenCalledOnce()
  complete({
    subject: 'ordinary',
    name: 'Ordinary',
    groups: [],
    isAdministrator: false,
    permissionExpiresAtUtc: '2026-10-16T12:00:00Z',
  })
  await Promise.all([router, startup])
  expect(authSession.initialized.value).toBe(true)
  expect(authSession.user.value?.isAdministrator).toBe(false)
  await authSession.initialize()
  expect(request).toHaveBeenCalledOnce()
})

it('settles initialization without identity or privileges after an unavailable backend', async () => {
  request.mockRejectedValueOnce(new Error('unavailable'))
  const { authSession } = await import('@/services/authSession')
  await authSession.initialize()
  expect(authSession.initialized.value).toBe(true)
  expect(authSession.authenticated.value).toBe(false)
  expect(authSession.user.value).toBeNull()
  expect(authSession.unavailable.value).toBe(true)
})
