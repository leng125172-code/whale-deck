import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import ResourceListView from '@/views/ResourceListView.vue'
import { ApiError } from '@/services/apiClient'

const { request } = vi.hoisted(() => ({ request: vi.fn<(path: string) => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
const wrappers: ReturnType<typeof mount>[] = []
const resource = (id: string) => ({
  id,
  name: id,
  type: 'DockerNetwork',
  state: 'External',
  version: 'bridge',
  isProtected: false,
  attributes: { containerCount: '1' },
})
async function create(path = '/networks') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/images', component: ResourceListView, meta: { endpoint: 'images' } },
      { path: '/networks', component: ResourceListView, meta: { endpoint: 'networks' } },
    ],
  })
  await router.push(path)
  const wrapper = mount(ResourceListView, { global: { plugins: [ElementPlus, router] } })
  wrappers.push(wrapper)
  return { wrapper, router }
}
beforeEach(() => {
  request.mockReset()
  request.mockResolvedValue([resource('network:exact'), resource('network:other')])
})
afterEach(() => wrappers.splice(0).forEach((wrapper) => wrapper.unmount()))
describe('Resource navigation isolation', () => {
  it('focus matches exact ID rather than a similar name, and reset shows all', async () => {
    const { wrapper, router } = await create('/networks?resourceId=network:exact')
    await flushPromises()
    expect(wrapper.findAll('.el-table__row')).toHaveLength(1)
    await router.push('/networks')
    await flushPromises()
    expect(wrapper.findAll('.el-table__row')).toHaveLength(2)
    expect(request).toHaveBeenCalledTimes(1)
  })
  it('late response from the previous endpoint cannot populate the new route', async () => {
    let resolve: ((value: unknown) => void) | undefined
    request.mockImplementation(async (path) =>
      path === 'images'
        ? new Promise((done) => {
            resolve = done
          })
        : [resource('current-network')],
    )
    const { wrapper, router } = await create('/images')
    await router.push('/networks')
    await flushPromises()
    resolve!([resource('private-old-image')])
    await flushPromises()
    expect(wrapper.text()).toContain('current-network')
    expect(wrapper.text()).not.toContain('private-old-image')
  })
  it('403 clears stale rows and reports the error', async () => {
    const { wrapper } = await create()
    await flushPromises()
    request.mockRejectedValue(new ApiError('权限已失效', 403, {}))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.el-table__row')).toHaveLength(0)
    expect(wrapper.text()).toContain('权限已失效')
  })
})
