import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ContainerRecycleArchiveJobResult from '@/components/containers/ContainerRecycleArchiveJobResult.vue'
import {
  readRecycleArchiveJobResult,
  recycleArchiveExecutionId,
} from '@/services/containerRecycleArchiveJobResults'
import type { RecycleArchiveJobResult } from '@/services/containerRecycleArchiveJobResults'
import { recycleExecutionId } from '@/services/containerRecycleJobResults'
import { recyclePurgeExecutionId } from '@/services/containerRecyclePurgeJobResults'
const id = '291a9ea0-e736-4f2c-a090-1cce1138fbb3',
  source = '491a9ea0-e736-4f2c-a090-1cce1138fbb4',
  original = '591a9ea0-e736-4f2c-a090-1cce1138fbb5'
const facts = (): RecycleArchiveJobResult => ({
  schemaVersion: 1,
  intentId: id,
  jobId: recycleArchiveExecutionId(id),
  sourceIntentId: source,
  purgeOperationId: recyclePurgeExecutionId(source),
  originalIntentId: original,
  sourceOperationId: recycleExecutionId(original),
  stage: 'execution',
  state: 'Succeeded',
  code: 'CONTAINER_RECYCLE_ARCHIVE_COMPLETED',
  receiptState: 'Completed',
  reservationStarted: true,
  reservationRecorded: true,
  applyStarted: true,
  archiveCopied: true,
  sourceFenced: true,
  ownedFence: true,
  bindingCurrent: true,
  publishBarrier: false,
})
const state = (r: RecycleArchiveJobResult) =>
  ['Ready', 'Succeeded'].includes(r.state)
    ? 'Succeeded'
    : ['Reserving', 'Applying', 'Unknown'].includes(r.state)
      ? 'Running'
      : r.state === 'Canceled'
        ? 'Canceled'
        : 'Failed'
const type = (r: RecycleArchiveJobResult) =>
  r.stage === 'planning'
    ? 'containers.files.recycle-archive-plan'
    : 'containers.files.recycle-archive'
const read = (r: RecycleArchiveJobResult) =>
  readRecycleArchiveJobResult(JSON.stringify(r), r.jobId, type(r), state(r))
function render(r: RecycleArchiveJobResult, json = JSON.stringify(r)) {
  return mount(ContainerRecycleArchiveJobResult, {
    props: { jobId: r.jobId, jobType: type(r), state: state(r), resultJson: json },
  })
}
it('shows original and archive references as historical facts without action buttons', () => {
  const r = facts(),
    w = render(r)
  expect(read(r)).not.toBeNull()
  expect(w.text()).toContain('归档复制已完成')
  expect(w.text()).toContain(source)
  expect(w.text()).toContain('预留或复制入口不等于复制成功')
  expect(w.text()).toContain('原回收/清理任务和HDD备份不改写')
  expect(w.findAll('button')).toHaveLength(0)
})
it.each(['Ready', 'Expired', 'Canceled', 'Failed'])(
  'planning %s cannot invent execution facts',
  (s) => {
    const r: RecycleArchiveJobResult = {
      ...facts(),
      jobId: id,
      stage: 'planning',
      state: s,
      code:
        'CONTAINER_RECYCLE_ARCHIVE_' +
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
      archiveCopied: null,
      bindingCurrent: null,
      sourceFenced: null,
      ownedFence: null,
    }
    expect(read(r)).not.toBeNull()
    expect(read({ ...r, archiveCopied: false })).toBeNull()
    expect(read({ ...r, reservationRecorded: true })).toBeNull()
  },
)
it.each([false, true])('reservation recorded=%s does not prove copying', (recorded) => {
  const r: RecycleArchiveJobResult = {
    ...facts(),
    state: 'Reserving',
    code: 'CONTAINER_RECYCLE_ARCHIVE_' + (recorded ? 'RESERVED' : 'RESERVING'),
    receiptState: null,
    reservationRecorded: recorded,
    applyStarted: false,
    archiveCopied: null,
    bindingCurrent: null,
    sourceFenced: null,
    ownedFence: null,
    publishBarrier: true,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, archiveCopied: false })).toBeNull()
  expect(render(r).text()).toContain('尚未开始复制')
  expect(render(r).text()).toContain(recorded ? '归档保护已预留' : '正在预留归档保护')
})
it('apply marker and absent receipt cannot invent a completed outcome', () => {
  const r: RecycleArchiveJobResult = {
    ...facts(),
    state: 'Applying',
    code: 'CONTAINER_RECYCLE_ARCHIVE_APPLYING',
    receiptState: null,
    archiveCopied: null,
    bindingCurrent: null,
    sourceFenced: null,
    ownedFence: null,
    publishBarrier: true,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, reservationRecorded: false })).toBeNull()
  expect(render(r).text()).toContain('结果仍待核实')
})
it.each(['Unknown', 'Acknowledged'])('%s preserves null cleanup fact', (s) => {
  const r: RecycleArchiveJobResult = {
    ...facts(),
    state: s,
    code:
      'CONTAINER_RECYCLE_ARCHIVE_' + (s === 'Unknown' ? 'RESULT_UNKNOWN' : 'UNKNOWN_ACKNOWLEDGED'),
    receiptState: 'Unknown',
    archiveCopied: null,
    bindingCurrent: null,
    publishBarrier: s === 'Unknown',
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, archiveCopied: false })).toBeNull()
  expect(render(r).text()).toContain('解除保护不证明复制成功')
  expect(render(r).text()).not.toContain('归档复制已完成')
})
it('absent receipt retains null fence facts', () => {
  const r: RecycleArchiveJobResult = {
    ...facts(),
    state: 'Unknown',
    code: 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN',
    receiptState: null,
    archiveCopied: null,
    bindingCurrent: null,
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
    code: 'CONTAINER_RECYCLE_ARCHIVE_' + (s === 'Canceled' ? 'CANCELED' : 'INTERRUPTED'),
    receiptState: 'Aborted',
    archiveCopied: false,
    bindingCurrent: null,
    ownedFence: false,
  }
  expect(read(r)).not.toBeNull()
  expect(read({ ...r, archiveCopied: true })).toBeNull()
})
it('late proof after acknowledgement remains a failed disposition', () => {
  const r = {
    ...facts(),
    state: 'Acknowledged',
    code: 'CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED',
  }
  expect(state(r)).toBe('Failed')
  expect(read(r)).not.toBeNull()
  expect(render(r).text()).not.toContain('归档复制已完成')
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
    else if (field === 'outcome') r.archiveCopied = false
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
    raw.slice(0, -1) + ',"archiveCopied":true}',
    raw.replace('"sourceFenced":true,', ''),
    raw.replace('"archiveCopied":true', '"archiveCopied":"true"'),
    'null',
    '[]',
    'x'.repeat(4097),
  ]) {
    expect(readRecycleArchiveJobResult(json, r.jobId, type(r), state(r))).toBeNull()
    const w = render(r, json)
    expect(w.text()).toContain('没有可核实')
    expect(w.text()).not.toContain('/must-not-render')
  }
})

it('keeps historical completed copy with current binding false', () => {
  const r = { ...facts(), bindingCurrent: false, ownedFence: false }
  expect(read(r)).not.toBeNull()
  expect(render(r).text()).toContain('归档复制已完成')
  expect(read({ ...r, bindingCurrent: true })).toBeNull()
})
it.each(['originalIntentId', 'purgeOperationId', 'sourceOperationId'] as const)(
  'rejects substituted %s',
  (field) => {
    const r = facts()
    r[field] = r.intentId
    expect(read(r)).toBeNull()
  },
)
