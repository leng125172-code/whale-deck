import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ContainerUsageCell from '@/components/containers/ContainerUsageCell.vue'
import type { ContainerUsage } from '@/services/contracts'

const sample: ContainerUsage = {
  containerId: 'id',
  state: 'running',
  sampledAtUtc: new Date().toISOString(),
  quality: 'Good',
  cpuPercent: 125.3,
  cpuWindowSeconds: 6,
  memoryUsedBytes: 104857600,
  memoryLimitBytes: 1073741824,
  memoryPercent: 9.76,
  networkReceiveBytes: 10,
  networkSendBytes: 20,
  blockReadBytes: 30,
  blockWriteBytes: 40,
  pids: 6,
}
const mountCell = (props: { sample?: ContainerUsage; state: string; stale: boolean }) =>
  mount(ContainerUsageCell, {
    props,
    global: { stubs: { ElTooltip: { template: '<div><slot /><slot name="content" /></div>' } } },
  })
describe('Container usage presentation', () => {
  it('preserves multi-core CPU, binary memory units and cumulative I/O labels', () => {
    const cell = mountCell({ sample, state: 'running', stale: false })
    expect(cell.text()).toContain('125.30%')
    expect(cell.text()).toContain('100.0 MiB / 1.0 GiB')
    expect(cell.text()).toContain('累计接收 / 发送')
    expect(cell.text()).toContain('进程数：6')
    expect(cell.text()).toContain('CPU 100% 为一个逻辑核心')
  })
  it('does not turn stopped, missing, stale or warming-up values into zero usage', () => {
    expect(mountCell({ sample, state: 'exited', stale: false }).text()).toBe('未运行')
    expect(mountCell({ sample, state: 'running', stale: true }).text()).toBe('采样已过期')
    expect(mountCell({ state: 'running', stale: false }).text()).toBe('等待采样')
    expect(
      mountCell({
        sample: { ...sample, quality: 'Unavailable' },
        state: 'running',
        stale: false,
      }).text(),
    ).toBe('采样不可用')
    const warmup = mountCell({
      sample: { ...sample, quality: 'Warmup', cpuPercent: null },
      state: 'running',
      stale: false,
    })
    expect(warmup.find('.container-usage__values').text()).toContain('预热')
    expect(warmup.find('.container-usage__values').text()).not.toContain('0.00%')
  })
})
