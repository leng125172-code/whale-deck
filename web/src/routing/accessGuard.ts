import type { RouteLocationNormalized } from 'vue-router'

interface RouteSession {
  initialize: () => Promise<void>
  authenticated: { readonly value: boolean }
  user: { readonly value: { isAdministrator: boolean } | null }
}

export function createAccessGuard(session: RouteSession) {
  return async (to: Pick<RouteLocationNormalized, 'name' | 'fullPath' | 'meta'>) => {
    // Initial direct navigation starts before App.onMounted. Never authorize
    // a route from the absence of an initialized identity snapshot.
    await session.initialize()
    if (to.meta.requiresAuth && session.authenticated.value !== true)
      return { name: 'login', query: { returnUrl: to.fullPath } }
    if (to.name === 'login' && session.authenticated.value) return { name: 'overview' }
    if (to.meta.administratorOnly && session.user.value?.isAdministrator !== true)
      return { name: 'forbidden' }
  }
}
