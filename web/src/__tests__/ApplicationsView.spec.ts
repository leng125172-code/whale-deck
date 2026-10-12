import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ApplicationsView from '@/views/ApplicationsView.vue'
import { ApiError } from '@/services/apiClient'

const { request, enqueue, push } = vi.hoisted(() => ({
  request: vi.fn<(path: string) => Promise<unknown>>(),
  enqueue: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  push: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/operations', () => ({
  enqueueOperation: enqueue,
  planOperation: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  stageSecret: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {}, name: 'applications' }),
  useRouter: () => ({ push }),
}))
const managed = {
  id: 'application:demo',
  name: 'demo',
  type: 'ComposeApplication',
  state: 'Running',
  version: 'demo:1.0',
  isProtected: false,
  attributes: { composeProject: 'whaledeck-app-demo', containerCount: '1' },
}
const wrappers: ReturnType<typeof mount>[] = []
beforeEach(() => {
  vi.clearAllMocks()
  request.mockImplementation(async (path: string) =>
    path === 'applications/popular'
      ? { applications: [], isStale: false }
      : path === 'applications'
        ? [
            managed,
            {
              ...managed,
              id: 'compose:whaledeck',
              name: 'whaledeck',
              type: 'ComposeProject',
              isProtected: true,
              version: '',
              attributes: { composeProject: 'whaledeck' },
            },
          ]
        : [],
  )
})
afterEach(() => wrappers.splice(0).forEach((wrapper) => wrapper.unmount()))
function create() {
  const wrapper = mount(ApplicationsView, { global: { plugins: [ElementPlus] } })
  wrappers.push(wrapper)
  return wrapper
}
describe('Installed applications', () => {
  it('clears private cards after permission expires', async () => {
    const wrapper = create()
    await flushPromises()
    request.mockRejectedValue(new ApiError('管理权限已失效', 403, {}))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.installed-row')).toHaveLength(0)
    expect(wrapper.text()).toContain('管理权限已失效')
  })
  it('keeps core resources read-only while managed application actions follow state', async () => {
    const wrapper = create()
    await flushPromises()
    const cards = wrapper.findAll('.installed-row')
    expect(cards).toHaveLength(2)
    expect(cards[0]!.text()).toContain('已启动')
    expect(cards[0]!.text()).toContain('停止')
    expect(cards[0]!.text()).not.toContain('启动重启')
    expect(cards[1]!.text()).toContain('受保护')
    expect(cards[1]!.findAll('button').map((button) => button.text())).toEqual(['关联容器'])
    expect(enqueue).not.toHaveBeenCalled()
  })
  it('filters by name and links the actual Compose project to containers', async () => {
    const wrapper = create()
    await flushPromises()
    await wrapper.get('input[aria-label="搜索应用"]').setValue('demo')
    expect(wrapper.findAll('.installed-row')).toHaveLength(1)
    await wrapper.get('.installed-row button').trigger('click')
    expect(push).toHaveBeenCalledWith({
      path: '/containers',
      query: { project: 'whaledeck-app-demo' },
    })
    await wrapper.get('input[aria-label="搜索应用"]').setValue('missing')
    expect(wrapper.text()).toContain('没有匹配的应用')
    await wrapper.get('input[aria-label="搜索应用"]').setValue('')
    expect(wrapper.findAll('.installed-row')).toHaveLength(2)
  })
})
