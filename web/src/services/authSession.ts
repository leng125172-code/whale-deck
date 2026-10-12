import { readonly, ref } from 'vue'
import { runtimeConfig } from '@/config/runtime'
import { ApiError, apiRequest } from '@/services/apiClient'
import { navigateToLogout } from '@/services/logoutNavigation'
import type { CurrentUser } from '@/services/contracts'

const previewStorageKey = 'whaledeck-preview-authenticated'
const authenticated = ref(false)
const initialized = ref(false)
const user = ref<CurrentUser | null>(null)
const unavailable = ref(false)

async function initializeAuthSession() {
  if (initialized.value) return

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
    user.value = await apiRequest<CurrentUser>('auth/me', { signal: controller.signal })
    authenticated.value = true
    unavailable.value = false
  } catch (reason) {
    authenticated.value = false
    user.value = null
    unavailable.value = !(reason instanceof ApiError && reason.status === 401)
  } finally {
    window.clearTimeout(timeout)
    initialized.value = true
  }
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
  if (import.meta.env.DEV) {
    sessionStorage.removeItem(previewStorageKey)
    authenticated.value = false
    user.value = null
    window.location.assign('/login')
    return
  }
  // OIDC logout must be a top-level navigation: fetch follows the provider's
  // redirect in the background, where cross-origin identity pages are not JSON.
  // A same-origin POST form preserves CSRF protection and the browser session.
  await navigateToLogout()
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
}
