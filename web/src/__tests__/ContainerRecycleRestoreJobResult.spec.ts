import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ContainerRecycleRestoreJobResult from '@/components/containers/ContainerRecycleRestoreJobResult.vue'
import {
  readRecycleRestoreJobResult,
  recycleRestoreExecutionId,
} from '@/services/containerRecycleRestoreJobResults'
import type { RecycleRestoreJobResult } from '@/services/containerRecycleRestoreJobResults'
import { recycleExecutionId } from '@/services/containerRecycleJobResults'
const id = '291a9ea0-e736-4f2c-a090-1cce1138fbb3',
  source = '491a9ea0-e736-4f2c-a090-1cce1138fbb4'
const facts = (): RecycleRestoreJobResult => ({
  schemaVersion: 1,
  intentId: id,
  jobId: recycleRestoreExecutionId(id),
  sourceIntentId: source,
  sourceOperationId: recycleExecutionId(source),
  stage: 'execution',
  state: 'Succeeded',
  code: 'CONTAINER_RECYCLE_RESTORE_COMPLETED',
  receiptState: 'Completed',
  sourceState: 'Restored',
  bindingCurrent: true,
  sourceRetained: false,
  sourceRestored: true,
  restoredByThisOperation: true,
  historyVerified: true,
  historyAvailable: true,
  publishBarrier: false,
})
function read(v: RecycleRestoreJobResult) {
  return readRecycleRestoreJobResult(
    JSON.stringify(v),
    v.jobId,
    v.stage === 'planning'
      ? 'containers.files.recycle-restore-plan'
      : 'containers.files.recycle-restore',
    v.state === 'Ready' || v.state === 'Succeeded'
      ? 'Succeeded'
      : v.state === 'Unknown'
        ? 'Running'
        : v.state === 'Canceled'
          ? 'Canceled'
          : 'Failed',
  )
}
it('renders separate original and restore refs as historical facts only', () => {
  const v = facts()
  const wrapper = mount(ContainerRecycleRestoreJobResult, {
    props: {
      jobId: v.jobId,
      jobType: 'containers.files.recycle-restore',
      state: 'Succeeded',
      resultJson: JSON.stringify(v),
    },
  })
  expect(wrapper.text()).toContain('文件恢复已完成')
  expect(wrapper.text()).toContain(source)
  expect(wrapper.text()).toContain(v.sourceOperationId)
  expect(wrapper.text()).toContain('不改写原回收任务')
  expect(wrapper.findAll('button')).toHaveLength(0)
})
it.each(['Ready', 'Expired', 'Canceled', 'Failed'])(
  'planning %s never invents execution facts',
  (state) => {
    const v: RecycleRestoreJobResult = {
      ...facts(),
      jobId: id,
      stage: 'planning',
      state,
      code:
        'CONTAINER_RECYCLE_RESTORE_' +
        (
          {
            Ready: 'READY',
            Expired: 'PLAN_EXPIRED',
            Canceled: 'CANCELED',
            Failed: 'SOURCE_CHANGED',
          } as Record<string, string>
        )[state],
      receiptState: null,
      sourceState: null,
      bindingCurrent: null,
      sourceRetained: null,
      sourceRestored: null,
      restoredByThisOperation: null,
      historyVerified: false,
      historyAvailable: null,
    }
    expect(read(v)).not.toBeNull()
    expect(read({ ...v, restoredByThisOperation: false })).toBeNull()
  },
)
it.each(['Unknown', 'Acknowledged'])(
  '%s retains null attribution even if source outcome is Restored',
  (state) => {
    const v: RecycleRestoreJobResult = {
      ...facts(),
      state,
      code:
        'CONTAINER_RECYCLE_RESTORE_' +
        (state === 'Unknown' ? 'RESULT_UNKNOWN' : 'UNKNOWN_ACKNOWLEDGED'),
      receiptState: 'Unknown',
      bindingCurrent: false,
      sourceRetained: null,
      sourceRestored: null,
      restoredByThisOperation: null,
      publishBarrier: state === 'Unknown',
    }
    expect(read(v)).not.toBeNull()
    expect(read({ ...v, restoredByThisOperation: false })).toBeNull()
    const wrapper = mount(ContainerRecycleRestoreJobResult, {
      props: {
        jobId: v.jobId,
        jobType: 'containers.files.recycle-restore',
        state: state === 'Unknown' ? 'Running' : 'Failed',
        resultJson: JSON.stringify(v),
      },
    })
    expect(wrapper.text()).toContain('未证明')
    expect(wrapper.text()).not.toContain('文件恢复已完成')
  },
)
it('distinguishes unclaimed abort from a verified retained-source abort', () => {
  const v: RecycleRestoreJobResult = {
    ...facts(),
    state: 'Canceled',
    code: 'CONTAINER_RECYCLE_RESTORE_CANCELED',
    receiptState: 'Aborted',
    sourceState: null,
    bindingCurrent: false,
    sourceRetained: null,
    sourceRestored: null,
    restoredByThisOperation: false,
    historyVerified: false,
    historyAvailable: false,
  }
  expect(read(v)).not.toBeNull()
  expect(
    read({
      ...v,
      sourceState: 'Trashed',
      sourceRetained: true,
      sourceRestored: false,
      historyVerified: true,
      historyAvailable: true,
    }),
  ).not.toBeNull()
  expect(read({ ...v, restoredByThisOperation: true })).toBeNull()
})
it('missing reply is not a false source/restoration fact', () => {
  const v: RecycleRestoreJobResult = {
    ...facts(),
    state: 'Unknown',
    code: 'CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN',
    receiptState: null,
    sourceState: null,
    bindingCurrent: null,
    sourceRetained: null,
    sourceRestored: null,
    restoredByThisOperation: null,
    historyVerified: false,
    historyAvailable: null,
    publishBarrier: true,
  }
  expect(read(v)).not.toBeNull()
  expect(read({ ...v, sourceRestored: false })).toBeNull()
})
it.each(['job', 'source', 'stage', 'version', 'code', 'attribution', 'barrier'])(
  'rejects mismatched %s',
  (field) => {
    const v = facts()
    if (field === 'job') v.jobId = id
    else if (field === 'source') v.sourceOperationId = v.jobId
    else if (field === 'stage') v.stage = 'planning'
    else if (field === 'version') v.schemaVersion = 2
    else if (field === 'code') v.code = 'CONTAINER_RECYCLE_TRASHED'
    else if (field === 'attribution') v.restoredByThisOperation = false
    else v.publishBarrier = true
    expect(read(v)).toBeNull()
  },
)
it('does not show extra, duplicate, missing, coerced or prototype JSON', () => {
  const v = facts(),
    raw = JSON.stringify(v)
  for (const value of [
    raw.slice(0, -1) + ',"privatePath":"/must-not-render"}',
    raw.slice(0, -1) + ',"restoredByThisOperation":true}',
    raw.replace('"sourceRestored":true,', ''),
    raw.replace('"restoredByThisOperation":true', '"restoredByThisOperation":"true"'),
    ' {"state":"Succeeded"}',
  ]) {
    expect(
      readRecycleRestoreJobResult(value, v.jobId, 'containers.files.recycle-restore', 'Succeeded'),
    ).toBeNull()
    const wrapper = mount(ContainerRecycleRestoreJobResult, {
      props: {
        jobId: v.jobId,
        jobType: 'containers.files.recycle-restore',
        state: 'Succeeded',
        resultJson: value,
      },
    })
    expect(wrapper.text()).not.toContain('/must-not-render')
    expect(wrapper.text()).toContain('没有可核实')
  }
})
it('acknowledged late completed proof remains a failed job, not a successful acknowledgement', () => {
  const v = {
    ...facts(),
    state: 'Acknowledged',
    code: 'CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED',
  }
  expect(read(v)).not.toBeNull()
  const wrapper = mount(ContainerRecycleRestoreJobResult, {
    props: {
      jobId: v.jobId,
      jobType: 'containers.files.recycle-restore',
      state: 'Failed',
      resultJson: JSON.stringify(v),
    },
  })
  expect(wrapper.text()).toContain('管理员已明确处置')
  expect(wrapper.text()).not.toContain('文件恢复已完成')
})
