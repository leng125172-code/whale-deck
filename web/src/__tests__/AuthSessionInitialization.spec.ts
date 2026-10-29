import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { fileBrowserLocationKey, rememberFileBrowserLocation } from '@/services/fileBrowserLocation'
import { copyHint, saveCopyHint } from '@/services/containerCopyHints'

const { request } = vi.hoisted(() => ({ request: vi.fn<() => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/logoutNavigation', () => ({ navigateToLogout: vi.fn<() => Promise<void>>() }))
beforeEach(() => {
  sessionStorage.clear()
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

it.each(['ordinary', 'another-admin'])(
  'clears another or downgraded directory hint after production login: %s',
  async (subject) => {
    rememberFileBrowserLocation('admin', { rootId: 'whaledeck.files.shared', path: '资料' })
    saveCopyHint({
      subject: 'admin',
      containerId: 'a'.repeat(64),
      key: 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
      id: null,
    })
    request.mockResolvedValueOnce({ subject, isAdministrator: subject === 'another-admin' })
    const { authSession } = await import('@/services/authSession')
    await authSession.initialize()
    expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
    expect(copyHint('admin', 'a'.repeat(64))).toBeNull()
  },
)
it('keeps same-admin hint on refresh, clears on downgrade and logout without a FilesView', async () => {
  const admin = { subject: 'admin', isAdministrator: true }
  request
    .mockResolvedValueOnce(admin)
    .mockResolvedValueOnce(admin)
    .mockResolvedValueOnce({ ...admin, isAdministrator: false })
  const { authSession } = await import('@/services/authSession')
  rememberFileBrowserLocation('admin', { rootId: 'whaledeck.files.shared', path: '资料' })
  saveCopyHint({
    subject: 'admin',
    containerId: 'a'.repeat(64),
    key: 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
    id: null,
  })
  await authSession.initialize()
  await authSession.refresh()
  expect(sessionStorage.getItem(fileBrowserLocationKey)).not.toBeNull()
  expect(copyHint('admin', 'a'.repeat(64))).not.toBeNull()
  await authSession.refresh()
  expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
  expect(copyHint('admin', 'a'.repeat(64))).toBeNull()
  rememberFileBrowserLocation('admin', { rootId: 'whaledeck.files.shared', path: '资料' })
  saveCopyHint({
    subject: 'admin',
    containerId: 'a'.repeat(64),
    key: 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12',
    id: null,
  })
  await authSession.logout()
  expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
  expect(copyHint('admin', 'a'.repeat(64))).toBeNull()
  const { navigateToLogout } = await import('@/services/logoutNavigation')
  expect(navigateToLogout).toHaveBeenCalledOnce()
})
