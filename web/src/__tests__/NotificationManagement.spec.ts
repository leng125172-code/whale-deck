import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import ElementPlus from 'element-plus'
import NotificationSettingsView from '@/views/NotificationSettingsView.vue'
import { ApiError } from '@/services/apiClient'
import {
  executeNotificationIntent,
  notificationIntent,
  notificationPage,
} from '@/services/notificationManagement'

const { request, session } = vi.hoisted(() => ({
  request: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
  session: { user: null as unknown },
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/authSession', () => ({ authSession: session }))
const user = ref<{ isAdministrator: boolean } | null>({ isAdministrator: true })
session.user = user
const wrappers: ReturnType<typeof mount>[] = []
const channel = {
  id: '59a29b72-dfbb-48c9-9323-c1688eaa3d71',
  name: 'PRIVATE_ADMIN_CHANNEL',
  provider: 'Webhook',
  destinationReference: 'approved-target',
  hasCredential: true,
  isEnabled: false,
  version: 0,
  createdAtUtc: '2026-10-14T01:00:00Z',
}
function create() {
  const wrapper = mount(NotificationSettingsView, {
    attachTo: document.body,
    global: { plugins: [ElementPlus] },
  })
  wrappers.push(wrapper)
  return wrapper
}
beforeEach(() => {
  vi.clearAllMocks()
  user.value = { isAdministrator: true }
  request.mockResolvedValue({ items: [channel], hasMore: false, page: 1, take: 25 })
})
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
})
describe('Notification management', () => {
  it('freezes command and idempotency key across retries without automatic retry', async () => {
    const payload = { name: 'Initial', confirmed: true }
    const intent = notificationIntent('channels', 'POST', payload)
    payload.name = 'Changed'
    request.mockRejectedValueOnce(new Error('Response lost')).mockResolvedValueOnce(channel)
    const signal = new AbortController().signal
    await expect(executeNotificationIntent(intent, signal)).rejects.toThrow('Response lost')
    expect(request).toHaveBeenCalledTimes(1)
    await executeNotificationIntent(intent, signal)
    const first = request.mock.calls[0]![1]!
    const second = request.mock.calls[1]![1]!
    expect(first.body).toBe('{"name":"Initial","confirmed":true}')
    expect(second.body).toBe(first.body)
    expect(new Headers(second.headers).get('Idempotency-Key')).toBe(
      new Headers(first.headers).get('Idempotency-Key'),
    )
    expect(notificationIntent('channels', 'POST', payload).key).not.toBe(intent.key)
  })
  it('encodes filters and queries only bounded pages', async () => {
    await notificationPage('deliveries', 2, new AbortController().signal, {
      channelId: channel.id,
      state: 'Canceled',
    })
    expect(request.mock.calls[0]![0]).toBe(
      `notifications/manage/deliveries?page=2&take=25&channelId=${channel.id}&state=Canceled`,
    )
  })
  it('does not request or display configuration for ordinary users', async () => {
    user.value = { isAdministrator: false }
    const wrapper = create()
    await flushPromises()
    expect(request).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('需要管理员权限')
    expect(wrapper.text()).not.toContain(channel.name)
  })
  it('shows real metadata and an explicit not-sending warning', async () => {
    const wrapper = create()
    await flushPromises()
    expect(wrapper.text()).toContain(channel.name)
    expect(wrapper.text()).toContain('外部发送需要私有配置')
    expect(wrapper.text()).toContain('已配置')
    expect(wrapper.text()).not.toContain('private-reference')
  })
  it('removes private rows and ignores late responses after downgrade', async () => {
    let finish: (value: unknown) => void = () => {}
    request.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const wrapper = create()
    await flushPromises()
    user.value = { isAdministrator: false }
    await flushPromises()
    finish({ items: [channel], hasMore: false, page: 1, take: 100 })
    await flushPromises()
    expect(wrapper.text()).not.toContain(channel.name)
    expect(wrapper.text()).toContain('需要管理员权限')
  })
  it('fails closed on 403 and renders 429 without disguising it as missing data', async () => {
    request.mockRejectedValueOnce(
      new ApiError('Permission denied', 403, { traceId: 'scope-trace' }),
    )
    const wrapper = create()
    await flushPromises()
    expect(wrapper.text()).toContain('需要管理员权限')
    expect(wrapper.text()).not.toContain(channel.name)
    wrapper.unmount()
    wrappers.pop()
    request.mockRejectedValue(new ApiError('请求过于频繁，请 30 秒后再试。', 429, {}))
    const retry = create()
    await flushPromises()
    expect(retry.text()).toContain('30 秒')
  })
})
