import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import ElementPlus from 'element-plus'
import AppShell from '@/layouts/AppShell.vue'
import type { CurrentUser } from '@/services/contracts'

const { request, session } = vi.hoisted(() => ({
  request: vi.fn<(path: string) => Promise<unknown>>(),
  session: { user: null as unknown, authenticated: null as unknown },
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/authSession', () => ({ authSession: session }))
vi.mock('vue-router', async (original) => ({
  ...(await original<typeof import('vue-router')>()),
  useRoute: () => ({ path: '/portal' }),
  useRouter: () => ({ push: vi.fn<(path: string) => Promise<void>>() }),
}))
const user = ref<CurrentUser | null>(null)
const authenticated = ref(true)
session.user = user
session.authenticated = authenticated
const wrappers: ReturnType<typeof mount>[] = []
const identity = (administrator: boolean): CurrentUser => ({
  subject: 'test-user',
  name: 'Test',
  groups: [],
  isAdministrator: administrator,
  permissionExpiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
})
function create() {
  const wrapper = mount(AppShell, {
    attachTo: document.body,
    global: {
      plugins: [ElementPlus],
      stubs: { RouterView: true, RouterLink: true, ThemeSwitch: true },
    },
  })
  wrappers.push(wrapper)
  return wrapper
}
beforeEach(() => {
  vi.clearAllMocks()
  authenticated.value = true
  user.value = identity(false)
  request.mockImplementation(async (path) =>
    path === 'notifications/summary'
      ? { unreadCount: 3, totalCount: 5, snapshotSequence: 10 }
      : path.startsWith('notifications?')
        ? { items: [], nextBefore: null, snapshotSequence: 10 }
        : [],
  )
})
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('Notification shell scope', () => {
  it('ordinary users have a real unread inbox but no task or management activity controls', async () => {
    const wrapper = create()
    await flushPromises()
    expect(wrapper.find('button[aria-label="通知"]').exists()).toBe(true)
    expect(wrapper.find('button[aria-label="任务"]').exists()).toBe(false)
    expect(wrapper.find('button[aria-label="通知"] .el-badge__content').text()).toBe('3')
    await wrapper.find('button[aria-label="通知"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).toContain('消息中心')
    expect(document.body.textContent).not.toContain('活动告警')
    expect(request.mock.calls.some(([path]) => path.startsWith('alerts'))).toBe(false)
  })
  it('downgrade closes private drawers and ignores a late administrator response', async () => {
    user.value = identity(true)
    let finish: (value: unknown) => void = () => {}
    request.mockImplementation(async (path) =>
      path.startsWith('alerts?')
        ? new Promise((resolve) => {
            finish = resolve
          })
        : path === 'notifications/summary'
          ? { unreadCount: 9, totalCount: 9, snapshotSequence: 9 }
          : [],
    )
    const wrapper = create()
    await flushPromises()
    expect(wrapper.find('button[aria-label="任务"]').exists()).toBe(true)
    user.value = identity(false)
    await flushPromises()
    finish([{ id: 'private', summaryCode: 'PRIVATE_ADMIN_RISK', state: 'Active' }])
    await flushPromises()
    expect(wrapper.find('button[aria-label="任务"]').exists()).toBe(false)
    expect(document.body.textContent).not.toContain('PRIVATE_ADMIN_RISK')
    authenticated.value = false
    user.value = null
    await flushPromises()
    expect(wrapper.find('button[aria-label="通知"] .el-badge__content').exists()).toBe(false)
  })
})
