import { readonly, ref } from 'vue'
import { runtimeConfig } from '@/config/runtime'
import { ApiError, apiRequest } from '@/services/apiClient'
import { navigateToLogout } from '@/services/logoutNavigation'
import type { CurrentUser } from '@/services/contracts'
import {
  onSessionExpired,
  publishSessionEnded,
  sessionEndedStorageKey,
  validSessionEndedSignal,
} from '@/services/sessionExpiry'
import {
  clearFileBrowserLocation,
  reconcileFileBrowserLocation,
} from '@/services/fileBrowserLocation'

const previewStorageKey = 'whaledeck-preview-authenticated'
const authenticated = ref(false)
const initialized = ref(false)
const user = ref<CurrentUser | null>(null)
const unavailable = ref(false)
let initialization: Promise<void> | undefined
let generation = 0
let expiredAction: (() => void) | undefined
let stopLifecycle: (() => void) | undefined

function expireSession() {
  const hadIdentity = authenticated.value || user.value !== null
  generation++
  authenticated.value = false
  user.value = null
  unavailable.value = false
  initialized.value = true
  initialization = undefined
  clearFileBrowserLocation()
  if (import.meta.env.DEV) sessionStorage.removeItem(previewStorageKey)
  if (hadIdentity) expiredAction?.()
}

function bindLifecycle(onExpired: () => void) {
  stopLifecycle?.()
  expiredAction = onExpired
  const off = onSessionExpired(expireSession)
  const storage = (event: StorageEvent) => {
    try {
      if (
        event.key === sessionEndedStorageKey &&
        event.storageArea === localStorage &&
        validSessionEndedSignal(event.newValue)
      )
        expireSession()
    } catch {
      // No storage access is required for the BFF 401 fallback.
    }
  }
  window.addEventListener('storage', storage)
  const stop = () => {
    if (stopLifecycle !== stop) return
    off()
    window.removeEventListener('storage', storage)
    expiredAction = undefined
    stopLifecycle = undefined
  }
  stopLifecycle = stop
  return stop
}

async function loadAuthSession() {
  if (initialized.value) return
  const current = generation

  if (import.meta.env.DEV && sessionStorage.getItem(previewStorageKey) === 'true') {
    authenticated.value = true
    initialized.value = true
    user.value = {
      subject: 'preview-user',
      name: 'Preview User',
      groups: ['preview-administrators'],
      isAdministrator: true,
      permissionExpiresAtUtc: new Date(Date.now() + 7_200_000).toISOString(),
    }
    return
  }

  // The standalone Vite preview has no API process behind its proxy. Keep it
  // as a guest until the login action deliberately creates a preview session;
  // production always verifies the backend session.
  if (import.meta.env.DEV) {
    initialized.value = true
    return
  }

  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 2500)

  try {
    const resolved = await apiRequest<CurrentUser>('auth/me', { signal: controller.signal })
    if (current !== generation) return
    user.value = resolved
    authenticated.value = true
    unavailable.value = false
  } catch (reason) {
    if (current !== generation) return
    authenticated.value = false
    user.value = null
    unavailable.value = !(reason instanceof ApiError && reason.status === 401)
  } finally {
    window.clearTimeout(timeout)
    if (current === generation) {
      reconcileFileBrowserLocation(user.value?.subject, user.value?.isAdministrator === true)
      initialized.value = true
    }
  }
}

function initializeAuthSession() {
  if (initialized.value) return Promise.resolve()
  initialization ??= loadAuthSession()
  return initialization
}

function authenticatePreview() {
  if (!import.meta.env.DEV) return false
  sessionStorage.setItem(previewStorageKey, 'true')
  authenticated.value = true
  initialized.value = true
  user.value = {
    subject: 'preview-user',
    name: 'Preview User',
    groups: ['preview-administrators'],
    isAdministrator: true,
    permissionExpiresAtUtc: new Date(Date.now() + 7_200_000).toISOString(),
  }
  return true
}

function redirectToAuthentik(returnUrl = '/') {
  const safeReturnUrl = returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/'
  window.location.assign(
    `${runtimeConfig.apiBaseUrl}/auth/login?returnUrl=${encodeURIComponent(safeReturnUrl)}`,
  )
}

async function logout() {
  clearFileBrowserLocation()
  if (import.meta.env.DEV) {
    generation++
    sessionStorage.removeItem(previewStorageKey)
    authenticated.value = false
    user.value = null
    publishSessionEnded()
    window.location.assign('/login')
    return
  }
  // OIDC logout must be a top-level navigation: fetch follows the provider's
  // redirect in the background, where cross-origin identity pages are not JSON.
  // A same-origin POST form preserves CSRF protection and the browser session.
  await navigateToLogout()
  publishSessionEnded()
}

async function refresh() {
  const current = generation
  try {
    const resolved = await apiRequest<CurrentUser>('auth/me')
    if (current !== generation) return
    user.value = resolved
    authenticated.value = true
    unavailable.value = false
    reconcileFileBrowserLocation(user.value?.subject, user.value?.isAdministrator === true)
  } catch (reason) {
    if (current === generation) {
      if (reason instanceof ApiError && [401, 403].includes(reason.status)) expireSession()
      else unavailable.value = true
    }
    throw reason
  }
}

export const authSession = {
  authenticated: readonly(authenticated),
  initialized: readonly(initialized),
  unavailable: readonly(unavailable),
  user: readonly(user),
  initialize: initializeAuthSession,
  authenticatePreview,
  redirectToAuthentik,
  logout,
  refresh,
  bindLifecycle,
}
