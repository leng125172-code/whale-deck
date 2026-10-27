import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import JobsView from '@/views/JobsView.vue'
import type { JobEvent } from '@/services/contracts'

const { request, stage, subscribe } = vi.hoisted(() => ({
  request: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
  stage: vi.fn<(value?: string) => Promise<{ token: string }>>(),
  subscribe:
    vi.fn<
      (
        id: string,
        handlers: { progress: (event: JobEvent) => void; error: () => void },
      ) => () => void
    >(),
}))
vi.mock('@/services/apiClient', () => ({
  apiRequest: request,
  apiUrl: (path: string) => `/api/v1/${path}`,
  createIdempotencyKey: () => 'new-retry-key',
}))
vi.mock('@/services/operations', () => ({ stageSecret: stage, subscribeToJob: subscribe }))

const source = {
  id: 'source-job',
  jobType: 'containers.delete',
  state: 'Failed',
  phase: 'Failed',
  progressPercent: 10,
  errorCode: 'DOCKER_OPERATION_FAILED',
  createdAtUtc: '2026-10-12T01:00:00Z',
  completedAtUtc: '2026-10-12T01:01:00Z',
  resultJson: null,
}
const context = {
  jobId: source.id,
  jobType: source.jobType,
  resourceId: 'temporary-container',
  requiresInput: false,
  inputKind: null,
}
const plan = {
  planHash: 'fresh-plan',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  changes: ['删除临时容器'],
  warnings: ['数据将按所选策略处理'],
}
const stubs = {
  ElDrawer: {
    props: ['modelValue'],
    template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>',
  },
  ElDialog: {
    props: ['modelValue'],
    template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>',
  },
}
const wrappers: ReturnType<typeof mount>[] = []

beforeEach(() => {
  request.mockReset()
  stage.mockReset()
  subscribe.mockReset()
  subscribe.mockReturnValue(() => {})
  request.mockImplementation(async (path: string) => {
    if (path === 'jobs?take=100') return [source]
    if (path === `jobs/${source.id}`) return source
    if (path.endsWith('/retry-context')) return context
    if (path.endsWith('/retry/plan')) return { context, plan }
    if (path.endsWith('/retry')) return { ...source, id: 'retry-job', state: 'Queued' }
    if (path === 'jobs/retry-job') return { ...source, id: 'retry-job', state: 'Queued' }
    throw new Error(`Unexpected request: ${path}`)
  })
})
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
})

describe('Job retry', () => {
  it.each(['files.upload', 'containers.files.upload', 'containers.files.restore'])(
    '%s requires a selected file and never exposes generic retry',
    async (jobType) => {
      const upload = { ...source, jobType }
      request.mockImplementation(async (path: string) => {
        if (path === 'jobs?take=100') return [upload]
        if (path === `jobs/${source.id}`) return upload
        throw new Error(`Unexpected request: ${path}`)
      })
      const wrapper = mount(JobsView, { global: { plugins: [ElementPlus], stubs } })
      wrappers.push(wrapper)
      await flushPromises()
      await wrapper.get('.el-table__body tr').trigger('click')
      await flushPromises()
      expect(wrapper.findAll('button').some((button) => button.text() === '重新预检并重试')).toBe(
        false,
      )
      expect(wrapper.text()).toContain('原任务不会自动重传或重复发布')
      expect(request.mock.calls.some(([path]) => path.includes('/retry'))).toBe(false)
    },
  )
  it('loads persisted events even for a finished job and closes the subscription on unmount', async () => {
    const close = vi.fn<() => void>()
    subscribe.mockReturnValue(close)
    const wrapper = mount(JobsView, { global: { plugins: [ElementPlus], stubs } })
    await flushPromises()
    await wrapper.get('.el-table__body tr').trigger('click')
    await flushPromises()
    expect(subscribe).toHaveBeenCalledWith(source.id, expect.any(Object))
    const handler = subscribe.mock.calls[0]![1]
    handler.progress({
      sequence: 1,
      state: 'Failed',
      phase: '已记录的失败',
      progressPercent: null,
      messageCode: 'FAILED',
      occurredAtUtc: source.completedAtUtc,
    })
    await flushPromises()
    expect(wrapper.find('.el-timeline').text()).toContain('已记录的失败')
    wrapper.unmount()
    expect(close).toHaveBeenCalledOnce()
  })

  it('previews current changes and submits only after explicit confirmation with a new key', async () => {
    const wrapper = mount(JobsView, { global: { plugins: [ElementPlus], stubs } })
    wrappers.push(wrapper)
    await flushPromises()
    await wrapper.get('.el-table__body tr').trigger('click')
    await flushPromises()
    const click = async (label: string) => {
      const button = wrapper.findAll('button').find((item) => item.text() === label)
      expect(button).toBeDefined()
      await button!.trigger('click')
      await flushPromises()
    }
    await click('重新预检并重试')
    await click('重新预检')
    expect(wrapper.text()).toContain('删除临时容器')
    expect(request.mock.calls.some(([path]) => path.endsWith('/retry'))).toBe(false)
    await click('确认重试')
    const submitted = request.mock.calls.find(([path]) => path.endsWith('/retry'))
    expect(submitted).toBeDefined()
    expect(submitted![1]!.headers).toEqual({ 'Idempotency-Key': 'new-retry-key' })
    expect(JSON.parse(submitted![1]!.body as string)).toMatchObject({
      confirmed: true,
      planHash: 'fresh-plan',
      idempotencyKey: 'new-retry-key',
    })
  })

  it('requires replacement input and stages it outside the retry payload', async () => {
    const secretContext = { ...context, requiresInput: true, inputKind: 'password' }
    request.mockImplementation(async (path: string) => {
      if (path === 'jobs?take=100') return [source]
      if (path === `jobs/${source.id}`) return source
      if (path.endsWith('/retry-context')) return secretContext
      if (path.endsWith('/retry/plan')) return { context: secretContext, plan }
      throw new Error(`Unexpected request: ${path}`)
    })
    stage.mockResolvedValue({ token: 'restaged-ticket' })
    const wrapper = mount(JobsView, { global: { plugins: [ElementPlus], stubs } })
    wrappers.push(wrapper)
    await flushPromises()
    await wrapper.get('.el-table__body tr').trigger('click')
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '重新预检并重试')!
      .trigger('click')
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '重新预检')!
      .trigger('click')
    await flushPromises()
    expect(stage).not.toHaveBeenCalled()
    expect(request.mock.calls.some(([path]) => path.endsWith('/retry/plan'))).toBe(false)
    await wrapper.get('input[type="password"]').setValue('acceptance-example-only')
    await wrapper
      .findAll('button')
      .find((item) => item.text() === '重新预检')!
      .trigger('click')
    await flushPromises()
    expect(stage).toHaveBeenCalledWith('acceptance-example-only')
    const preview = request.mock.calls.find(([path]) => path.endsWith('/retry/plan'))
    expect(JSON.parse(preview![1]!.body as string)).toEqual({ inputTicket: 'restaged-ticket' })
    expect(preview![1]!.body).not.toContain('acceptance-example-only')
  })
})
