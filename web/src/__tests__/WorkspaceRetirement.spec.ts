import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import WorkspaceRetirement from '@/components/WorkspaceRetirement.vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { Workspace } from '@/services/developmentWorkspaces'
import type { WorkspaceRetirement as Retirement } from '@/services/workspaceRetirements'
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn<typeof apiRequest>(),
}))
const request = vi.mocked(apiRequest),
  id = '11111111-1111-4111-8111-111111111111',
  archiveId = '22222222-2222-4222-8222-222222222222',
  retirementId = '33333333-3333-4333-8333-333333333333',
  digest = 'a'.repeat(64)
const workspace: Workspace = {
  id,
  slug: 'retirement-test',
  ownerSubject: 'owner',
  storageRootId: 'approved',
  state: 'StorageAllocated',
  version: 3,
  lifecycleState: 'Stopped',
  lifecycleVersion: 2,
  budget: { cpuMillis: 500, memoryMiB: 512, diskMiB: 1024 },
  environment: { version: 1, runtimes: [] },
  environmentSha256: 'b'.repeat(64),
}
const plan = {
  archiveId,
  retirementPlanSha256: digest,
  expectedEpoch: 2,
  entryCount: 2,
  totalBytes: 17,
  retainsSource: true,
  reservesBudget: true,
  deletionAuthorized: false,
}
function ready(): Retirement {
  return {
    retirementId,
    workspaceId: id,
    archiveId,
    state: 'Running',
    phase: 'AwaitingConfirmation',
    code: null,
    awaitingConfirmation: true,
    authorizationRequired: false,
    cancelRequested: false,
    attentionRequired: false,
    planJson: JSON.stringify(plan),
    planExpiresAtUtc: '2026-11-11T12:10:00Z',
    confirmedAtUtc: null,
    executionJson: null,
    completedAtUtc: null,
    sourceRetired: false,
    budgetReleased: false,
    retainsHdd: true,
  }
}
const wrappers: ReturnType<typeof mount>[] = []
function render() {
  const wrapper = mount(WorkspaceRetirement, {
    props: { workspace },
    global: { plugins: [ElementPlus] },
  })
  wrappers.push(wrapper)
  return wrapper
}
function button(wrapper: ReturnType<typeof render>, label: string) {
  const found = wrapper.findAll('button').find((value) => value.text() === label)
  if (!found) throw new Error('Missing button: ' + label)
  return found
}
beforeEach(() => request.mockReset())
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  vi.useRealTimers()
})
it('requires independent deletion consent and replays the same confirmation after a lost reply', async () => {
  request
    .mockResolvedValueOnce(ready())
    .mockRejectedValueOnce(new Error('lost reply'))
    .mockResolvedValueOnce({ ...ready(), phase: 'Execute', awaitingConfirmation: false })
  const wrapper = render()
  await flushPromises()
  const confirm = button(wrapper, '确认删除源码并退役空间')
  expect(confirm.attributes('disabled')).toBeDefined()
  await wrapper.get('input[type=checkbox]').setValue(true)
  await confirm.trigger('click')
  await flushPromises()
  await confirm.trigger('click')
  await flushPromises()
  const calls = request.mock.calls.filter(([path]) => path.endsWith('/confirm'))
  expect(calls).toHaveLength(2)
  expect(calls[0]?.[1]?.body).toBe(calls[1]?.[1]?.body)
  expect(JSON.parse(String(calls[0]?.[1]?.body))).toEqual({
    planSha256: digest,
    deleteSourceAndReleaseBudget: true,
  })
  expect(wrapper.text()).not.toContain('应用资源预算已释放')
})
it.each(['foreign-workspace', 'foreign-archive', 'wrong-epoch', 'authorized-plan', 'unsafe-count'])(
  'refuses deletion for %s',
  async (kind) => {
    const value = ready(),
      changed = { ...plan }
    if (kind === 'foreign-workspace') value.workspaceId = archiveId
    if (kind === 'foreign-archive') changed.archiveId = id
    if (kind === 'wrong-epoch') changed.expectedEpoch = 4
    if (kind === 'authorized-plan') changed.deletionAuthorized = true
    if (kind === 'unsafe-count') changed.entryCount = Number.MAX_SAFE_INTEGER + 1
    value.planJson = JSON.stringify(changed)
    request.mockResolvedValue(value)
    const wrapper = render()
    await flushPromises()
    expect(
      wrapper.findAll('button').some((value) => value.text() === '确认删除源码并退役空间'),
    ).toBe(false)
  },
)
it('reconnects an unanswered begin with the same key, archive and epoch', async () => {
  const archived = {
    archiveId,
    workspaceId: id,
    state: 'Succeeded',
    completedAtUtc: '2026-11-11T11:00:00Z',
    planJson: JSON.stringify({ expectedEpoch: 2 }),
    verifyJson: JSON.stringify({ operationId: archiveId }),
  }
  request
    .mockResolvedValueOnce(null)
    .mockResolvedValueOnce(archived)
    .mockRejectedValueOnce(new Error('lost begin'))
    .mockResolvedValueOnce({ ...ready(), phase: 'Plan', awaitingConfirmation: false })
  const wrapper = render()
  await flushPromises()
  await button(wrapper, '核对空间退役计划').trigger('click')
  await flushPromises()
  await button(wrapper, '重连原退役请求').trigger('click')
  await flushPromises()
  const calls = request.mock.calls.filter(([, input]) => input?.method === 'POST')
  expect(calls).toHaveLength(2)
  expect(calls[0]?.[1]?.headers).toEqual(calls[1]?.[1]?.headers)
  expect(calls[0]?.[1]?.body).toBe(calls[1]?.[1]?.body)
  expect(JSON.parse(String(calls[0]?.[1]?.body))).toEqual({ archiveId, expectedEpoch: 2 })
})
it('refuses a stale-epoch archived copy for a fresh begin', async () => {
  request.mockResolvedValueOnce(null).mockResolvedValueOnce({
    archiveId,
    workspaceId: id,
    state: 'Succeeded',
    completedAtUtc: '2026-11-11T11:00:00Z',
    planJson: JSON.stringify({ expectedEpoch: 4 }),
    verifyJson: JSON.stringify({ operationId: archiveId }),
  })
  const wrapper = render()
  await flushPromises()
  expect(button(wrapper, '核对空间退役计划').attributes('disabled')).toBeDefined()
  expect(request.mock.calls.filter(([, input]) => input?.method === 'POST')).toHaveLength(0)
})
it('clears metadata and consent on 403, stops polling and aborts on unmount', async () => {
  vi.useFakeTimers()
  request
    .mockResolvedValueOnce(ready())
    .mockRejectedValueOnce(new ApiError('revoked', 403, { code: 'AUTH_FORBIDDEN' }))
  const wrapper = render()
  await flushPromises()
  await wrapper.get('input[type=checkbox]').setValue(true)
  await button(wrapper, '刷新退役状态').trigger('click')
  await flushPromises()
  expect(wrapper.text()).not.toContain(retirementId)
  expect(wrapper.text()).not.toContain(digest)
  const count = request.mock.calls.length
  await vi.advanceTimersByTimeAsync(9000)
  expect(request).toHaveBeenCalledTimes(count)
  const signal = request.mock.calls[0]?.[1]?.signal
  wrapper.unmount()
  wrappers.pop()
  expect(signal?.aborted).toBe(true)
})
it('keeps interrupted execution charged without automatic retry or cancel controls', async () => {
  vi.useFakeTimers()
  request.mockResolvedValue({
    ...ready(),
    phase: 'AttentionRequired',
    awaitingConfirmation: false,
    attentionRequired: true,
  })
  const wrapper = render()
  await flushPromises()
  await vi.advanceTimersByTimeAsync(9000)
  expect(request).toHaveBeenCalledTimes(1)
  expect(wrapper.text()).toContain('空间继续隔离并占用预算')
  expect(wrapper.text()).not.toContain('应用资源预算已释放')
  expect(wrapper.findAll('button').map((value) => value.text())).not.toContain('取消退役')
})
it.each([false, true])(
  'announces release only with a complete bound receipt: %s',
  async (valid) => {
    const value = {
      ...ready(),
      state: 'Succeeded',
      phase: 'Retired',
      awaitingConfirmation: false,
      completedAtUtc: '2026-11-11T12:00:00Z',
      sourceRetired: true,
      budgetReleased: true,
      executionJson: valid
        ? JSON.stringify({
            workspaceId: id,
            retirementId,
            retirementPlanSha256: digest,
            sourceRemoved: true,
            quotaReleased: true,
            claimReleased: true,
            retainsHdd: true,
          })
        : null,
    }
    request.mockResolvedValue(value)
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text().includes('应用资源预算已释放')).toBe(valid)
    expect(wrapper.emitted('refresh')?.length ?? 0).toBe(valid ? 1 : 0)
  },
)
