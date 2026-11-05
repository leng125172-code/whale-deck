import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ContainerRecycleJobResult from '@/components/containers/ContainerRecycleJobResult.vue'
import { readRecycleJobResult, recycleExecutionId } from '@/services/containerRecycleJobResults'
const intentId = '291a9ea0-e736-4f2c-a090-1cce1138fbb3'
const facts = () => ({
  schemaVersion: 1,
  intentId,
  jobId: recycleExecutionId(intentId),
  stage: 'execution',
  state: 'Succeeded',
  code: 'CONTAINER_RECYCLE_TRASHED',
  receiptState: 'Trashed',
  bindingCurrent: true,
  sourceRetained: true as boolean | null,
  sourceRestored: false as boolean | null,
  historyMayExist: true,
  historyVerified: true,
  historyAvailable: true,
  publishBarrier: false,
})
const render = (value: ReturnType<typeof facts>) =>
  mount(ContainerRecycleJobResult, {
    props: {
      jobId: value.jobId,
      jobType: 'containers.files.recycle',
      state:
        value.state === 'Unknown'
          ? 'Running'
          : value.state === 'Succeeded'
            ? 'Succeeded'
            : 'Failed',
      resultJson: JSON.stringify(value),
    },
  })
it('shows historical proof without raw JSON', () => {
  const w = render(facts())
  expect(w.text()).toContain('文件回收已完成')
  expect(w.text()).toContain('保存时的历史事实')
  expect(w.text()).not.toContain('schemaVersion')
})
it('acknowledgement retains unproved source fields and never means restored', () => {
  const v = {
    ...facts(),
    state: 'Acknowledged',
    code: 'CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED',
    receiptState: 'Unknown',
    bindingCurrent: false,
    sourceRetained: null,
    sourceRestored: null,
  }
  const w = render(v)
  expect(w.text()).toContain('未证明')
  expect(w.text()).toContain('解除保护不证明回收或恢复成功')
  expect(w.text()).not.toContain('文件回收已完成')
})
it.each(['sourceRetained', 'receiptState', 'jobId', 'code', 'publishBarrier', 'historyVerified'])(
  'rejects contradictory %s without dumping private values',
  (field) => {
    const v = facts()
    const bad = {
      ...v,
      [field]:
        field === 'publishBarrier' ? true : field === 'historyVerified' ? false : 'private-value',
    }
    expect(
      readRecycleJobResult(JSON.stringify(bad), v.jobId, 'containers.files.recycle', 'Succeeded'),
    ).toBeNull()
  },
)
it('rejects extra, duplicate, omitted or unrelated job JSON', () => {
  const v = facts(),
    raw = JSON.stringify(v)
  for (const value of [
    raw.slice(0, -1) + ',"privatePath":"secret"}',
    raw.slice(0, -1) + ',"state":"Succeeded"}',
    JSON.stringify({ schemaVersion: 1 }),
  ])
    expect(readRecycleJobResult(value, v.jobId, 'containers.files.recycle', 'Succeeded')).toBeNull()
  expect(readRecycleJobResult(raw, v.jobId, 'containers.delete', 'Succeeded')).toBeNull()
})
it('restored original is superseded, never a new successful trash', () => {
  const v = {
    ...facts(),
    state: 'Superseded',
    code: 'CONTAINER_RECYCLE_ALREADY_RESTORED',
    receiptState: 'Restored',
    sourceRetained: false,
    sourceRestored: true,
  }
  expect(render(v).text()).toContain('当前原件状态已变化')
  expect(
    readRecycleJobResult(
      JSON.stringify({ ...v, state: 'Succeeded' }),
      v.jobId,
      'containers.files.recycle',
      'Succeeded',
    ),
  ).toBeNull()
})
