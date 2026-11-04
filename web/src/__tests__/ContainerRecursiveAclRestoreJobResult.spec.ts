import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { result } from './fixtures/recursiveAclRestoreJobResult'
import Component from '@/components/containers/ContainerRecursiveAclRestoreJobResult.vue'
import { readRecursiveAclRestoreJobResult as read } from '@/services/containerRecursiveAclRestoreJobResults'

const id = '291a9ea0-e736-4f2c-a090-1cce1138fbb3'
const jobId = 'ad934875-7ef6-4444-a660-9863c623c589'
const type = 'containers.files.recursive-acl-restore'

const parse = (v: unknown, state = 'Succeeded', jobType = type, target = jobId) =>
  read(JSON.stringify(v), jobType, state, target)
const props = (v: unknown, state = 'Succeeded') => ({
  jobId,
  jobType: type,
  state,
  resultJson: JSON.stringify(v),
})

describe('Recursive ACL restore historical job facts', () => {
  it('renders source changes and actual restored changes separately', () => {
    expect(parse(result)).toEqual(result)
    const wrapper = mount(Component, { props: props(result) })
    expect(wrapper.get('[aria-label="恢复来源原计数"]').text()).toContain('来源原修改2')
    expect(wrapper.get('[aria-label="本次恢复实际结果"]').text()).toContain('实际修改4')
    expect(wrapper.text()).toContain('不证明当前目录树或HDD仍可用')
    wrapper.unmount()
  })
  it.each(['Superseded', 'Canceled', 'Failed', 'Partial', 'Unknown', 'Acknowledged'])(
    'preserves %s and its receipt facts',
    (state) => {
      const v = { ...result, state, bindingCurrent: false }
      let jobState = 'Failed'
      if (state === 'Superseded') v.code = 'CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED'
      if (state === 'Canceled' || state === 'Failed') {
        Object.assign(v, {
          code: `CONTAINER_RECURSIVE_ACL_RESTORE_${state === 'Canceled' ? 'CANCELED' : 'INTERRUPTED'}`,
          receiptState: 'Aborted',
          completedCount: 1,
          changedCount: 0,
          abortedCount: 1,
          notStartedCount: 2,
        })
        jobState = state === 'Canceled' ? 'Canceled' : 'Failed'
      }
      if (state === 'Partial') {
        Object.assign(v, {
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT',
          receiptState: 'Partial',
          completedCount: 1,
          changedCount: 1,
          notStartedCount: 3,
          publishBarrier: true,
        })
        jobState = 'Running'
      }
      if (state === 'Unknown' || state === 'Acknowledged') {
        Object.assign(v, {
          code: `CONTAINER_RECURSIVE_ACL_RESTORE_${state === 'Unknown' ? 'RESULT_UNKNOWN' : 'UNKNOWN_ACKNOWLEDGED'}`,
          receiptState: 'Unknown',
          completedCount: 0,
          changedCount: 0,
          unknownCount: 4,
          publishBarrier: state === 'Unknown',
        })
        jobState = state === 'Unknown' ? 'Running' : 'Failed'
      }
      expect(parse(v, jobState)).toEqual(v)
      const wrapper = mount(Component, { props: props(v, jobState) })
      expect(wrapper.text()).toContain(state === 'Acknowledged' ? '不代表整树恢复成功' : '本次')
      expect(wrapper.text()).not.toContain('未记录可验证')
      wrapper.unmount()
    },
  )
  it('does not invent zeroes when an Unknown execution has no receipt', () => {
    const v = {
      ...result,
      state: 'Unknown',
      code: 'CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID',
      receiptState: null,
      completedCount: null,
      changedCount: null,
      abortedCount: null,
      notStartedCount: null,
      unknownCount: null,
      bindingCurrent: null,
      historyAvailable: null,
      historyVerified: false,
      publishBarrier: true,
    }
    expect(parse(v, 'Running')).toEqual(v)
    const wrapper = mount(Component, { props: props(v, 'Running') })
    expect(
      wrapper
        .get('[aria-label="本次恢复实际结果"]')
        .findAll('dd')
        .map((x) => x.text()),
    ).toEqual(Array(5).fill('未证明'))
    wrapper.unmount()
  })
  it('distinguishes a saved Ready plan from a later execution', () => {
    const v = {
      ...result,
      jobId: id,
      stage: 'planning',
      state: 'Ready',
      code: 'CONTAINER_RECURSIVE_ACL_RESTORE_READY',
      receiptState: null,
      completedCount: null,
      changedCount: null,
      abortedCount: null,
      notStartedCount: null,
      unknownCount: null,
      bindingCurrent: null,
      historyAvailable: null,
      historyVerified: false,
      historyMayExist: false,
    }
    expect(parse(v, 'Succeeded', type + '-plan', id)).toEqual(v)
    expect(parse(v, 'Running', type + '-plan', id)).toBeNull()
    expect(parse({ ...v, completedCount: 0 }, 'Succeeded', type + '-plan', id)).toBeNull()
  })
  it.each([
    ['entryCount', 10001],
    ['entryCount', 3],
    ['changedCount', 5],
    ['completedCount', 3],
    ['unknownCount', 1],
    ['changedCount', -1],
    ['abortedCount', 0.5],
    ['sourceEntryCount', 3],
    ['sourceChangedCount', 5],
    ['historyVerified', false],
    ['historyMayExist', false],
    ['historyAvailable', false],
    ['publishBarrier', true],
    ['bindingCurrent', false],
    ['code', 'CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN'],
    ['path', '/private-never-render'],
    ['schemaVersion', 2],
    ['receiptState', 'Unknown'],
  ])('rejects inconsistent or private %s', (key, value) => {
    expect(parse({ ...result, [key]: value })).toBeNull()
  })
  it('rejects missing fields, foreign jobs, primitive or legacy results without rendering raw JSON', () => {
    const missing: Record<string, unknown> = { ...result }
    delete missing.unknownCount
    expect(parse(missing)).toBeNull()
    expect(parse(result, 'Failed')).toBeNull()
    expect(parse(result, 'Succeeded', 'containers.files.acl')).toBeNull()
    expect(parse(result, 'Succeeded', type, id)).toBeNull()
    for (const raw of [
      null,
      '[]',
      'null',
      JSON.stringify({ ...result, path: '/private-never-render' }),
      ' '.repeat(4097),
    ]) {
      const wrapper = mount(Component, {
        props: { jobId, jobType: type, state: 'Succeeded', resultJson: raw },
      })
      expect(wrapper.text()).toContain('不将缺失结果视为零修改或成功')
      expect(wrapper.text()).not.toContain('/private-never-render')
      wrapper.unmount()
    }
  })
})
