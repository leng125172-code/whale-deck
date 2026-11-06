import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ContainerRecyclePurgeJobResult from '@/components/containers/ContainerRecyclePurgeJobResult.vue'
import {
  readRecyclePurgeJobResult,
  recyclePurgeExecutionId,
} from '@/services/containerRecyclePurgeJobResults'
import type { RecyclePurgeJobResult } from '@/services/containerRecyclePurgeJobResults'
import { recycleExecutionId } from '@/services/containerRecycleJobResults'
const id = '291a9ea0-e736-4f2c-a090-1cce1138fbb3',
  source = '491a9ea0-e736-4f2c-a090-1cce1138fbb4'
const facts = (): RecyclePurgeJobResult => ({
  schemaVersion: 1,
  intentId: id,
  jobId: recyclePurgeExecutionId(id),
  sourceIntentId: source,
  sourceOperationId: recycleExecutionId(source),
  stage: 'execution',
  state: 'Succeeded',
  code: 'CONTAINER_RECYCLE_PURGE_COMPLETED',
  receiptState: 'Completed',
  reservationStarted: true,
  reservationRecorded: true,
  applyStarted: true,
  purgeApplied: true,
  sourceFenced: true,
  ownedFence: true,
  publishBarrier: false,
})
const state = (r: RecyclePurgeJobResult) =>
  ['Ready', 'Succeeded'].includes(r.state)
    ? 'Succeeded'
    : ['Reserving', 'Applying', 'Unknown'].includes(r.state)
      ? 'Running'
      : r.state === 'Canceled'
        ? 'Canceled'
        : 'Failed'
const type = (r: RecyclePurgeJobResult) =>
  r.stage === 'planning' ? 'containers.files.recycle-purge-plan' : 'containers.files.recycle-purge'
const read = (r: RecyclePurgeJobResult) =>
  readRecyclePurgeJobResult(JSON.stringify(r), r.jobId, type(r), state(r))
function render(r: RecyclePurgeJobResult, json = JSON.stringify(r)) {
  return mount(ContainerRecyclePurgeJobResult, {
    props: { jobId: r.jobId, jobType: type(r), state: state(r), resultJson: json },
  })
}
it('shows original and purge references as historical facts without action buttons', () => {
  const r = facts(),
    w = render(r)
  expect(read(r)).not.toBeNull()
  expect(w.text()).toContain('留存原件清理已完成')
  expect(w.text()).toContain(source)
  expect(w.text()).toContain('预留证明不等于已经删除')
  expect(w.text()).toContain('恢复须走独立归档复制入口并重新核验HDD备份')
  expect(w.findAll('button')).toHaveLength(0)
})
it.each(['Ready', 'Expired', 'Canceled', 'Failed'])(
  'planning %s cannot invent execution facts',
  (s) => {
    const r: RecyclePurgeJobResult = {
      ...facts(),
      jobId: id,
      stage: 'planning',
      state: s,
      code:
        'CONTAINER_RECYCLE_PURGE_' +
        (
          {
            Ready: 'READY',
            Expired: 'PLAN_EXPIRED',
            Canceled: 'CANCELED',
            Failed: 'SOURCE_CHANGED',
          } as Record<string, string>
        )[s],
      receiptState: null,
      reservationStarted: false,
      reservationRecorded: false,
      applyStarted: false,
      purgeApplied: null,
      sourceFenced: null,
      ownedFence: null,
    }
    expect(read(r)).not.toBeNull()
    expect(read({ ...r, purgeApplied: false })).toBeNull()
    expect(read({ ...r, reservationRecorded: true })).toBeNull()
  },
)
it.each([false, true])('reservation recorded=%s does not prove deletion', (recorded) => {
  const r: RecyclePurgeJobResult = {
    ...facts(),
    state: 'Reserving',
    code: 'CONTAINER_RECYCLE_PURGE_' + (recorded ? 'RESERVED' : 'RESERVING'),
    receiptState: null,
    reservationRecorded: recorded,
    applyStarted: false,
    purgeApplied: null,
    sourceFenced: null,
    ownedFence: null,
    publishBarrier: true,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, purgeApplied: false })).toBeNull()
  expect(render(r).text()).toContain('尚未进入删除')
  expect(render(r).text()).toContain(recorded ? '原件保护已预留' : '正在预留原件保护')
})
it('apply marker and absent receipt cannot invent a completed outcome', () => {
  const r: RecyclePurgeJobResult = {
    ...facts(),
    state: 'Applying',
    code: 'CONTAINER_RECYCLE_PURGE_APPLYING',
    receiptState: null,
    purgeApplied: null,
    sourceFenced: null,
    ownedFence: null,
    publishBarrier: true,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, reservationRecorded: false })).toBeNull()
  expect(render(r).text()).toContain('结果仍待核实')
})
it.each(['Unknown', 'Acknowledged'])('%s preserves null cleanup fact', (s) => {
  const r: RecyclePurgeJobResult = {
    ...facts(),
    state: s,
    code:
      'CONTAINER_RECYCLE_PURGE_' + (s === 'Unknown' ? 'RESULT_UNKNOWN' : 'UNKNOWN_ACKNOWLEDGED'),
    receiptState: 'Unknown',
    purgeApplied: null,
    publishBarrier: s === 'Unknown',
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, purgeApplied: false })).toBeNull()
  expect(render(r).text()).toContain('解除保护不证明清理成功')
  expect(render(r).text()).not.toContain('留存原件清理已完成')
})
it('absent receipt retains null fence facts', () => {
  const r: RecyclePurgeJobResult = {
    ...facts(),
    state: 'Unknown',
    code: 'CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN',
    receiptState: null,
    purgeApplied: null,
    sourceFenced: null,
    ownedFence: null,
    publishBarrier: true,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, sourceFenced: false })).toBeNull()
})
it.each(['Canceled', 'Failed'])('aborted %s preserves proven false', (s) => {
  const r = {
    ...facts(),
    state: s,
    code: 'CONTAINER_RECYCLE_PURGE_' + (s === 'Canceled' ? 'CANCELED' : 'INTERRUPTED'),
    receiptState: 'Aborted',
    purgeApplied: false,
    ownedFence: false,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, purgeApplied: true })).toBeNull()
})
it('late proof after acknowledgement remains a failed disposition', () => {
  const r = {
    ...facts(),
    state: 'Acknowledged',
    code: 'CONTAINER_RECYCLE_PURGE_UNKNOWN_ACKNOWLEDGED',
  }
  expect(state(r)).toBe('Failed')
  expect(read(r)).not.toBeNull()
  expect(render(r).text()).not.toContain('留存原件清理已完成')
})
it.each(['job', 'source', 'stage', 'schema', 'code', 'outcome', 'barrier', 'reservation'])(
  'rejects mismatched %s',
  (field) => {
    const r = facts()
    if (field === 'job') r.jobId = id
    else if (field === 'source') r.sourceOperationId = r.jobId
    else if (field === 'stage') r.stage = 'planning'
    else if (field === 'schema') r.schemaVersion = 2
    else if (field === 'code') r.code = 'CONTAINER_RECYCLE_COMPLETED'
    else if (field === 'outcome') r.purgeApplied = false
    else if (field === 'barrier') r.publishBarrier = true
    else r.reservationStarted = false
    expect(read(r)).toBeNull()
  },
)
it('never displays foreign, duplicate, coerced or missing fields as raw JSON', () => {
  const r = facts(),
    raw = JSON.stringify(r)
  for (const json of [
    raw.slice(0, -1) + ',"privatePath":"/must-not-render"}',
    raw.slice(0, -1) + ',"purgeApplied":true}',
    raw.replace('"sourceFenced":true,', ''),
    raw.replace('"purgeApplied":true', '"purgeApplied":"true"'),
    'null',
    '[]',
    'x'.repeat(4097),
  ]) {
    expect(readRecyclePurgeJobResult(json, r.jobId, type(r), state(r))).toBeNull()
    const w = render(r, json)
    expect(w.text()).toContain('没有可核实')
    expect(w.text()).not.toContain('/must-not-render')
  }
})
