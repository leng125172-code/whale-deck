import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ComposeProjectsView from '@/views/ComposeProjectsView.vue'

const { request, preview, enqueue } = vi.hoisted(() => ({
  request: vi.fn<(path: string) => Promise<unknown>>(),
  preview: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  enqueue: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
vi.mock('@/services/operations', () => ({ planOperation: preview, enqueueOperation: enqueue }))
const project = {
  id: 'application:demo',
  name: 'demo',
  state: 'Running',
  isProtected: false,
  attributes: {
    allowedActions: 'start,stop,restart,pull,redeploy',
    services: 'web,db',
    containerCount: '2',
    configurationSource: 'compose.yml',
  },
}
const wrappers: ReturnType<typeof mount>[] = []
beforeEach(() => {
  request.mockReset()
  preview.mockReset()
  enqueue.mockReset()
  request.mockResolvedValue([
    project,
    { ...project, id: 'compose:core', name: 'core', isProtected: true },
  ])
  preview.mockResolvedValue({
    planHash: 'confirmed-plan',
    expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
    changes: ['project summary', 'web: ConfigurationChanged; affected containers: 1'],
    warnings: [],
  })
  enqueue.mockResolvedValue({ id: 'job-1' })
})
afterEach(() => wrappers.splice(0).forEach((wrapper) => wrapper.unmount()))
function create() {
  const wrapper = mount(ComposeProjectsView, {
    global: {
      plugins: [ElementPlus],
      stubs: {
        RouterLink: { template: '<a><slot/></a>' },
        ElDialog: {
          props: ['modelValue'],
          template:
            '<section v-if="modelValue" class="test-dialog"><slot/><slot name="footer"/></section>',
        },
      },
    },
  })
  wrappers.push(wrapper)
  return wrapper
}
describe('Compose project confirmation', () => {
  it('protects core projects and previews before queuing any write', async () => {
    const wrapper = create()
    await flushPromises()
    expect(wrapper.findAll('.compose-project')[1]!.find('button').exists()).toBe(false)
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '重部署')!
      .trigger('click')
    await flushPromises()
    expect(preview).toHaveBeenCalledWith('compose-projects', 'redeploy', 'application:demo', {})
    expect(enqueue).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('配置已变化')
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '确认重部署')!
      .trigger('click')
    await flushPromises()
    expect(enqueue).toHaveBeenCalledWith(
      'compose-projects',
      'redeploy',
      'application:demo',
      {},
      true,
      'confirmed-plan',
    )
    expect(wrapper.text()).toContain('任务已提交')
  })
  it('does not submit an expired plan', async () => {
    preview.mockResolvedValue({
      planHash: 'expired',
      expiresAtUtc: '2000-01-01T00:00:00Z',
      changes: [],
      warnings: [],
    })
    const wrapper = create()
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '拉取镜像')!
      .trigger('click')
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '确认拉取镜像')!
      .trigger('click')
    await flushPromises()
    expect(enqueue).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('计划已使用、过期')
  })
  it('clears private project details when authorization fails', async () => {
    const { ApiError } = await import('@/services/apiClient')
    const wrapper = create()
    await flushPromises()
    request.mockRejectedValueOnce(
      new ApiError('管理权限已失效', 403, { traceId: 'compose-denied' }),
    )
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.compose-project')).toHaveLength(0)
    expect(wrapper.text()).toContain('管理权限已失效')
  })
})
