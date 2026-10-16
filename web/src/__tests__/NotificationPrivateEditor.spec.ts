import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import ElementPlus, { ElMessageBox } from 'element-plus'
import NotificationPrivateEditor from '@/components/NotificationPrivateEditor.vue'
import { ApiError } from '@/services/apiClient'

const { request, session } = vi.hoisted(() => ({
  request: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
  session: { user: null as unknown },
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/authSession', () => ({ authSession: session }))
const user = ref<{ subject: string; isAdministrator: boolean } | null>(null)
session.user = user
const wrappers: ReturnType<typeof mount>[] = []
const channel = {
  id: '59a29b72-dfbb-48c9-9323-c1688eaa3d71',
  name: 'Fixture',
  provider: 'Webhook' as const,
  destinationReference: 'approved',
  hasCredential: true,
  isEnabled: false,
  version: 2,
  createdAtUtc: '2026-10-14T01:00:00Z',
}
function create() {
  const wrapper = mount(NotificationPrivateEditor, {
    props: { channel },
    attachTo: document.body,
    global: { plugins: [ElementPlus] },
  })
  wrappers.push(wrapper)
  return wrapper
}
beforeEach(() => {
  vi.clearAllMocks()
  user.value = { subject: 'fixture-owner', isAdministrator: true }
  request.mockImplementation(async (path) =>
    path.endsWith('/status')
      ? null
      : { channelVersion: 7, revision: 'opaque', hasTarget: true, hasCredential: true },
  )
})
afterEach(() => {
  vi.restoreAllMocks()
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
})
describe('Private editor ownership and late replies', () => {
  async function prepareFixture(expiresAtUtc: string) {
    request.mockImplementation(async (path) => {
      if (path.endsWith('/status')) return null
      if (path === 'secrets/stage') return { token: 'opaque-fixture' }
      if (path.endsWith('/plan')) return { planHash: 'opaque-plan', expiresAtUtc, warning: 'pause' }
      return { channelVersion: 7, revision: 'opaque', hasTarget: true, hasCredential: true }
    })
    const wrapper = create()
    await flushPromises()
    await wrapper.find('input[type="password"]').setValue('https://example.invalid/private-fixture')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '准备配置计划')!
      .trigger('click')
    await flushPromises()
    return wrapper
  }
  it('discards an expired plan and unlocks preparation without a submit request', async () => {
    const wrapper = await prepareFixture(new Date(Date.now() - 1000).toISOString())
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认并提交')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('计划已过期')
    const prepareButton = wrapper
      .findAll('button')
      .find((button) => button.text() === '准备配置计划')!
    expect(prepareButton.attributes('disabled')).toBeUndefined()
    expect(
      request.mock.calls.filter(
        ([path, init]) => path.endsWith('/private') && init?.method === 'POST',
      ),
    ).toHaveLength(0)
  })
  it('opens only one confirmation and never submits its late result after downgrade', async () => {
    const wrapper = await prepareFixture(new Date(Date.now() + 300000).toISOString())
    let finish: (value: 'confirm') => void = () => {}
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = (action) => resolve(action as Awaited<ReturnType<typeof ElMessageBox.confirm>>)
        }),
    )
    const submit = wrapper.findAll('button').find((button) => button.text() === '确认并提交')!
    await submit.trigger('click')
    await submit.trigger('click')
    expect(confirm).toHaveBeenCalledTimes(1)
    user.value = { subject: 'fixture-owner', isAdministrator: false }
    await flushPromises()
    finish('confirm')
    await flushPromises()
    expect(wrapper.emitted('close')).toBeTruthy()
    expect(
      request.mock.calls.filter(
        ([path, init]) => path.endsWith('/private') && init?.method === 'POST',
      ),
    ).toHaveLength(0)
  })
  it('never requests private metadata for an ordinary user', async () => {
    user.value = { subject: 'fixture-owner', isAdministrator: false }
    create()
    await flushPromises()
    expect(request).not.toHaveBeenCalled()
  })
  it.each(['downgrade', 'different-owner'] as const)(
    'clears secret input on %s and ignores a late metadata reply',
    async (change) => {
      const wrapper = create()
      await flushPromises()
      await wrapper
        .find('input[type="password"]')
        .setValue('https://example.invalid/private-fixture')
      let finish: (result: unknown) => void = () => {}
      request.mockImplementation(
        () =>
          new Promise((resolve) => {
            finish = resolve
          }),
      )
      const refresh = wrapper.findAll('button').find((button) => button.text() === '核对状态')!
      await refresh.trigger('click')
      await flushPromises()
      const count = request.mock.calls.length
      user.value =
        change === 'downgrade'
          ? { subject: 'fixture-owner', isAdministrator: false }
          : { subject: 'other-owner', isAdministrator: true }
      await flushPromises()
      expect(wrapper.emitted('close')).toBeTruthy()
      expect((wrapper.find('input[type="password"]').element as HTMLInputElement).value).toBe('')
      finish({
        jobId: 'late-private-job',
        state: 'Running',
        version: 3,
        canAcknowledgeUnknown: false,
      })
      await flushPromises()
      expect(request).toHaveBeenCalledTimes(count)
      expect(wrapper.text()).not.toContain('late-private-job')
    },
  )
  it('closes and clears a filled form on API permission rejection', async () => {
    const wrapper = create()
    await flushPromises()
    await wrapper.find('input[type="password"]').setValue('https://example.invalid/private-fixture')
    request.mockRejectedValueOnce(new ApiError('Permission denied', 403, {}))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '核对状态')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.emitted('forbidden')).toBeTruthy()
    expect(wrapper.emitted('close')).toBeTruthy()
    expect((wrapper.find('input[type="password"]').element as HTMLInputElement).value).toBe('')
  })
})
