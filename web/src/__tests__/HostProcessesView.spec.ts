import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import HostProcessesView from '@/views/HostProcessesView.vue'

const { request } = vi.hoisted(() => ({ request: vi.fn<(path: string) => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
const process = {
  pid: 42,
  parentPid: 1,
  userId: 1000,
  instanceId: '42:638957376000000000',
  name: 'WhaleDeck.Agent',
  state: 'S',
  threadCount: 8,
  startedAtUtc: '2026-10-12T00:00:00Z',
  cpuTimeSeconds: 8,
  cpuPercent: null,
  memoryBytes: 1048576,
  virtualMemoryBytes: 4194304,
}
const snapshot = {
  items: [process],
  total: 1,
  page: 1,
  pageSize: 50,
  sampledAtUtc: '2026-10-12T00:00:06Z',
  cpuWindowSeconds: 6,
  isStale: false,
  isTruncated: false,
  skippedCount: 0,
}
const wrappers: ReturnType<typeof mount>[] = []

beforeEach(() => {
  request.mockReset()
})
afterEach(() => wrappers.splice(0).forEach((wrapper) => wrapper.unmount()))
function create() {
  const wrapper = mount(HostProcessesView, {
    global: {
      plugins: [ElementPlus],
      stubs: {
        ElDrawer: {
          props: ['modelValue'],
          template: '<section v-if="modelValue"><slot/></section>',
        },
      },
    },
  })
  wrappers.push(wrapper)
  return wrapper
}

describe('Read-only host processes', () => {
  it('shows unknown CPU and sends trimmed filtering with bounded pagination', async () => {
    request.mockResolvedValue(snapshot)
    const wrapper = create()
    await flushPromises()
    expect(wrapper.text()).toContain('WhaleDeck.Agent')
    expect(wrapper.get('.el-table__body').text()).toContain('—')
    await wrapper.get('input[placeholder="名称或 PID"]').setValue('  Agent  ')
    await wrapper.get('input[placeholder="名称或 PID"]').trigger('keyup', { key: 'Enter' })
    await flushPromises()
    const path = request.mock.calls[request.mock.calls.length - 1]![0]
    expect(path).toContain('filter=Agent')
    expect(path).toContain('page=1&pageSize=50')
    expect(wrapper.findAll('button').some((button) => button.text().includes('结束进程'))).toBe(
      false,
    )
  })

  it('pins details to the start identity and explains PID reuse without stale details', async () => {
    const { ApiError } = await import('@/services/apiClient')
    request.mockImplementation(async (path) => {
      if (path.startsWith('host/processes?')) return snapshot
      throw new ApiError('Missing', 404, { code: 'AGENT_RESOURCE_NOT_FOUND' })
    })
    const wrapper = create()
    await flushPromises()
    await wrapper.get('.el-table__body tr').trigger('click')
    await flushPromises()
    expect(request).toHaveBeenCalledWith(
      `host/processes/42?instanceId=${encodeURIComponent(process.instanceId)}`,
    )
    expect(wrapper.text()).toContain('进程已退出或 PID 已被复用')
  })

  it('marks stale and truncated snapshots explicitly', async () => {
    request.mockResolvedValue({ ...snapshot, isStale: true, isTruncated: true, skippedCount: 3 })
    const wrapper = create()
    await flushPromises()
    expect(wrapper.text()).toContain('进程采样已过期')
    expect(wrapper.text()).toContain('结果为部分主机进程')
    expect(wrapper.text()).toContain('3 个进程退出或无法读取')
  })

  it('clears private data on permission loss and ignores a late detail response', async () => {
    const { ApiError } = await import('@/services/apiClient')
    let completeDetail!: (value: unknown) => void
    request.mockResolvedValueOnce(snapshot)
    const wrapper = create()
    await flushPromises()
    request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          completeDetail = resolve
        }),
    )
    await wrapper.get('.el-table__body tr').trigger('click')
    await flushPromises()
    request.mockRejectedValueOnce(new ApiError('管理权限已失效', 403, { traceId: 'denied' }))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    completeDetail({ ...process, name: 'late-private-process' })
    await flushPromises()
    expect(wrapper.text()).toContain('管理权限已失效')
    expect(wrapper.text()).not.toContain('WhaleDeck.Agent')
    expect(wrapper.text()).not.toContain('late-private-process')
    expect(
      wrapper.get('[role="switch"][aria-label="自动刷新进程"]').attributes('aria-checked'),
    ).toBe('false')
  })
})
