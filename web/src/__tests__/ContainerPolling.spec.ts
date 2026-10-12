import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ContainersView from '@/views/ContainersView.vue'
import { ApiError } from '@/services/apiClient'

const { request } = vi.hoisted(() => ({ request: vi.fn<(path: string) => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {}, params: {} }) }))
const container = {
  id: 'core',
  name: 'core-worker',
  image: 'worker:dev',
  state: 'running',
  status: 'Up 1 hour',
  isProtected: true,
  protectionLevel: 'ControlPlane',
  labels: {},
}
const wrappers: ReturnType<typeof mount>[] = []
const batch = () => ({
  items: [
    {
      containerId: 'core',
      state: 'running',
      sampledAtUtc: new Date().toISOString(),
      cpuPercent: 20,
      cpuWindowSeconds: 6,
      memoryUsedBytes: 100,
      memoryLimitBytes: 1000,
      quality: 'Good',
    },
  ],
  sampledAtUtc: new Date().toISOString(),
  isStale: false,
  isTruncated: false,
  skippedCount: 0,
  totalCount: 1,
  intervalSeconds: 6,
})
function create() {
  const wrapper = mount(ContainersView, { global: { plugins: [ElementPlus] } })
  wrappers.push(wrapper)
  return wrapper
}
beforeEach(() => {
  vi.clearAllMocks()
  vi.useFakeTimers()
  vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  request.mockImplementation(async (path) =>
    path.startsWith('containers?') ? [container] : path === 'containers/usage' ? batch() : [],
  )
})
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  vi.restoreAllMocks()
  vi.useRealTimers()
  document.body.innerHTML = ''
})
describe('Container page polling', () => {
  it('skips hidden tabs, resumes on visibility and stops on unmount', async () => {
    const wrapper = create()
    await flushPromises()
    expect(request.mock.calls.filter(([path]) => path === 'containers/usage')).toHaveLength(1)
    const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
    await vi.advanceTimersByTimeAsync(6000)
    expect(request.mock.calls.filter(([path]) => path === 'containers/usage')).toHaveLength(1)
    hidden.mockReturnValue(false)
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()
    expect(request.mock.calls.filter(([path]) => path === 'containers/usage')).toHaveLength(2)
    wrapper.unmount()
    wrappers.splice(0)
    await vi.advanceTimersByTimeAsync(12_000)
    expect(request.mock.calls.filter(([path]) => path === 'containers/usage')).toHaveLength(2)
  })
  it('does not overlap a slow usage request or show a full loading mask on background polling', async () => {
    const wrapper = create()
    await flushPromises()
    let resolve: ((value: unknown) => void) | undefined
    request.mockImplementation(async (path) =>
      path === 'containers/usage'
        ? new Promise((done) => {
            resolve = done
          })
        : [container],
    )
    await vi.advanceTimersByTimeAsync(12_000)
    expect(request.mock.calls.filter(([path]) => path === 'containers/usage')).toHaveLength(2)
    expect(wrapper.find('.container-grid .el-loading-mask').exists()).toBe(false)
    resolve!(batch())
    await flushPromises()
  })
  it('permission loss clears private cards and stops recurring requests', async () => {
    const wrapper = create()
    await flushPromises()
    expect(wrapper.text()).toContain('20.00%')
    request.mockRejectedValue(new ApiError('管理权限已失效', 403, {}))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.container-card')).toHaveLength(0)
    const count = request.mock.calls.length
    await vi.advanceTimersByTimeAsync(12_000)
    expect(request).toHaveBeenCalledTimes(count)
  })
})
