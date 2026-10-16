import { describe, expect, it, vi } from 'vitest'
import { createAccessGuard } from '@/routing/accessGuard'

function session(authenticated = true, administrator = false) {
  return {
    initialize: vi.fn<() => Promise<void>>(async () => {}),
    authenticated: { value: authenticated },
    user: { value: authenticated ? { isAdministrator: administrator } : null },
  }
}
const restricted = {
  name: 'host-terminal',
  fullPath: '/host/terminal',
  meta: { requiresAuth: true, administratorOnly: true },
}

describe('initial route authorization', () => {
  it('waits for the initial snapshot before rejecting an ordinary-user deep link', async () => {
    const identity = session()
    let complete!: () => void
    identity.initialize.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          complete = resolve
        }),
    )
    const guard = createAccessGuard(identity)
    let settled = false
    const pending = guard(restricted).then((result) => {
      settled = true
      return result
    })
    await Promise.resolve()
    expect(settled).toBe(false)
    complete()
    await expect(pending).resolves.toEqual({ name: 'forbidden' })
  })
  it('rejects anonymous deep links with the original return path', async () => {
    await expect(createAccessGuard(session(false))(restricted)).resolves.toEqual({
      name: 'login',
      query: { returnUrl: '/host/terminal' },
    })
  })
  it('allows an authenticated administrator after initialization', async () => {
    const identity = session(true, true)
    await expect(createAccessGuard(identity)(restricted)).resolves.toBeUndefined()
    expect(identity.initialize).toHaveBeenCalledOnce()
  })
  it('does not let a missing user record inherit administrative access', async () => {
    const identity = session()
    identity.user.value = null
    await expect(createAccessGuard(identity)(restricted)).resolves.toEqual({ name: 'forbidden' })
  })
  it('requires a real boolean administrator flag rather than a truthy malformed snapshot', async () => {
    const identity = session()
    identity.user.value = { isAdministrator: 'false' } as unknown as { isAdministrator: boolean }
    await expect(createAccessGuard(identity)(restricted)).resolves.toEqual({ name: 'forbidden' })
  })
  it('keeps the authenticated login redirect and ordinary portal access', async () => {
    const guard = createAccessGuard(session())
    await expect(guard({ name: 'login', fullPath: '/login', meta: {} })).resolves.toEqual({
      name: 'overview',
    })
    await expect(
      guard({ name: 'portal', fullPath: '/portal', meta: { requiresAuth: true } }),
    ).resolves.toBeUndefined()
  })
})
