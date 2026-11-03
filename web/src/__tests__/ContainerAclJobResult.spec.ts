import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ContainerAclJobResult from '@/components/containers/ContainerAclJobResult.vue'
import { readAclJobResult } from '@/services/containerAclJobResults'
const facts = {
  schemaVersion: 1,
  intentId: '6b2601c4-e509-34c0-27a8-56114f72780a',
  stage: 'execution',
  state: 'Succeeded',
  code: 'CONTAINER_ACL_COMPLETED',
  aclType: 'access',
  kind: 'File',
  targetMode: 0,
  noChange: false,
  aclConfirmed: true,
  receiptState: 'Completed',
  bindingCurrent: true,
  historyMayExist: true,
  historyVerified: true,
  hasPublishedVersion: true,
}
const render = (value: unknown, state = 'Succeeded', jobType = 'containers.files.acl') =>
  mount(ContainerAclJobResult, {
    props: {
      resultJson: typeof value === 'string' ? value : JSON.stringify(value),
      state,
      jobType,
    },
  })
describe('ACL task historical facts', () => {
  it('shows explicit zero and false facts without exposing raw JSON or proof fields', () => {
    const wrapper = render(facts)
    expect(wrapper.text()).toContain('ACL 修改已完成')
    expect(wrapper.text()).toContain('0000')
    expect(wrapper.text()).toContain('对象绑定有效不代表原用户仍有管理权限')
    expect(wrapper.text()).not.toContain('schemaVersion')
  })
  it('distinguishes planning, missing proof and unknown acknowledgement without claiming restoration', () => {
    const ready = render(
      {
        ...facts,
        stage: 'planning',
        state: 'Ready',
        aclConfirmed: null,
        receiptState: null,
        bindingCurrent: null,
        historyMayExist: false,
        historyVerified: false,
        hasPublishedVersion: false,
      },
      'Succeeded',
      'containers.files.acl-plan',
    )
    expect(ready.text()).toContain('预检完成，尚未修改 ACL')
    expect(ready.text()).toContain('未证明')
    const ack = render(
      {
        ...facts,
        state: 'Acknowledged',
        aclConfirmed: null,
        receiptState: 'Unknown',
        hasPublishedVersion: false,
      },
      'Failed',
    )
    expect(ack.text()).toContain('处置仅解除业务保护，不证明 ACL 已恢复或重新执行')
    expect(ack.text()).toContain('未证明')
    expect(ack.text()).not.toContain('ACL 修改已完成')
  })
  it.each([
    null,
    '{}',
    '{broken',
    { ...facts, path: '/private' },
    { ...facts, schemaVersion: 2 },
    { ...facts, targetMode: 4095 },
    { ...facts, code: '<img src=x onerror=alert(1)>' },
    { ...facts, aclConfirmed: 'true' },
  ])('rejects invalid/unrecognized summaries without raw fallback: %j', (value) => {
    const wrapper = render(value)
    expect(wrapper.text()).toContain('未记录可展示的 ACL 安全摘要')
    expect(wrapper.text()).not.toContain('/private')
    expect(wrapper.find('img').exists()).toBe(false)
  })
  it('rejects a stale summary after a task state change and missing fields', () => {
    expect(readAclJobResult(JSON.stringify(facts), 'containers.files.acl', 'Running')).toBeNull()
    const { historyVerified: omitted, ...incomplete } = facts
    expect(omitted).toBe(true)
    expect(
      readAclJobResult(JSON.stringify(incomplete), 'containers.files.acl', 'Succeeded'),
    ).toBeNull()
  })
  it('distinguishes a rejected preflight with no receipt from an interrupted execution', () => {
    const rejected = render(
      {
        ...facts,
        stage: 'planning',
        state: 'Failed',
        code: 'CONTAINER_ACL_VERSION_CONFLICT',
        kind: null,
        targetMode: null,
        noChange: null,
        aclConfirmed: null,
        receiptState: null,
        bindingCurrent: null,
        historyMayExist: false,
        historyVerified: false,
        hasPublishedVersion: false,
      },
      'Failed',
      'containers.files.acl-plan',
    )
    expect(rejected.text()).toContain('预检失败，未进入 ACL 修改执行')
    expect(rejected.text()).not.toContain('执行失败')
    expect(rejected.text()).toContain('未取得')
    expect(rejected.text()).toContain('未证明')
    const interrupted = render(
      {
        ...facts,
        state: 'Failed',
        code: 'CONTAINER_ACL_INTERRUPTED',
        aclConfirmed: false,
        receiptState: 'Aborted',
        bindingCurrent: false,
        hasPublishedVersion: false,
      },
      'Failed',
    )
    expect(interrupted.text()).toContain('执行失败')
    expect(interrupted.text()).not.toContain('未进入 ACL 修改执行')
    expect(interrupted.text()).toContain('Aborted')
  })
})
