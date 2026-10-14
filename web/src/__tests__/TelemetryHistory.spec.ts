import { describe, expect, it } from 'vitest'
import { RollingTelemetryHistory, weightedAlignedLatency } from '@/services/rollingTelemetry'
import { makeTelemetry } from '@/services/workstationOverview'
import type { MetricSeriesSnapshot } from '@/services/contracts'
import { mount } from '@vue/test-utils'
import TelemetryChart from '@/components/overview/TelemetryChart.vue'

const sample = (
  kind: string,
  deviceId: string,
  value: number | null,
  quality = 'Good',
): MetricSeriesSnapshot => ({
  kind,
  deviceId,
  unit: 'milliseconds',
  points: [{ sampledAtUtc: '2026-10-12T10:00:00Z', value, quality }],
})
describe('History and disk metrics', () => {
  it('uses a fresh previous slot for cards without filling the chart gap', () => {
    const history = new RollingTelemetryHistory().update(
      [sample('disk.read.iops', 'disk', 20), sample('disk.write.iops', 'disk', 10)],
      [],
      '2026-10-12T10:00:06Z',
    )
    const chart = makeTelemetry('disk', history, [], 'iops')
    const device = chart.filters.find((item) => item.value === 'disk')!
    expect(device.stats.slice(0, 2).map((item) => item.value)).toEqual(['20.0 次/s', '10.0 次/s'])
    expect(device.series[0]!.values.slice(-1)[0]).toBeNull()
    expect(chart.filters[0]!.stats[0]!.value).toBe('20.0 次/s')
    expect(device.stats[0]!.sampleBucketUtc).toBe('2026-10-12T10:00:00.000Z')
  })
  it.each([18, 60])('does not present %s second old data as a current value', (age) => {
    const history = new RollingTelemetryHistory().update(
      [sample('disk.read.iops', 'disk', 20)],
      [],
      new Date(Date.parse('2026-10-12T10:00:00Z') + age * 1000).toISOString(),
    )
    const chart = makeTelemetry('disk', history, [], 'iops')
    expect(chart.filters[0]!.stats[0]!.value).toBe('暂无有效采样')
    expect(chart.filters[1]!.stats[0]!.value).toBe('暂无有效采样')
  })
  it('keeps a newer explicit unavailable sample unknown instead of falling back', () => {
    const history = new RollingTelemetryHistory().update(
      [
        sample('disk.read.iops', 'disk', 20),
        {
          ...sample('disk.read.iops', 'disk', 0, 'Unavailable'),
          points: [{ sampledAtUtc: '2026-10-12T10:00:06Z', value: 0, quality: 'Unavailable' }],
        },
      ],
      [],
      '2026-10-12T10:00:12Z',
    )
    const chart = makeTelemetry('disk', history, [], 'iops')
    expect(chart.filters[1]!.stats[0]!.value).toBe('暂无有效采样')
  })
  it('does not sum individually fresh device samples from different time slots', () => {
    const first = sample('disk.read.iops', 'a', 20)
    const second = sample('disk.read.iops', 'b', 10)
    second.points[0]!.sampledAtUtc = '2026-10-12T10:00:06Z'
    const history = new RollingTelemetryHistory().update(
      [first, second],
      [],
      '2026-10-12T10:00:12Z',
    )
    const chart = makeTelemetry('disk', history, [], 'iops')
    expect(chart.filters[1]!.stats[0]!.value).toBe('20.0 次/s')
    expect(chart.filters[2]!.stats[0]!.value).toBe('10.0 次/s')
    expect(chart.filters[0]!.stats[0]!.value).toBe('暂无有效采样')
  })
  it('distinguishes recent idle latency from expired idle data, including all disks', () => {
    const roll = new RollingTelemetryHistory()
    const input = [
      sample('disk.read.latency', 'disk', 0, 'Idle'),
      sample('disk.read.iops', 'disk', 0),
    ]
    const fresh = makeTelemetry(
      'disk',
      roll.update(input, [], '2026-10-12T10:00:06Z'),
      [],
      'latency',
    )
    expect(fresh.filters[0]!.stats[0]!.value).toBe('无 I/O')
    expect(fresh.filters[1]!.stats[0]!.value).toBe('无 I/O')
    expect(fresh.filters[0]!.series[0]!.values.slice(-2)[0]).toBeNull()
    const old = makeTelemetry('disk', roll.update([], [], '2026-10-12T10:00:18Z'), [], 'latency')
    expect(old.filters[0]!.stats[0]!.value).toBe('暂无有效采样')
  })
  it('does not silently substitute older data in historical endpoint mean cards', () => {
    const samples = sample('disk.read.iops', 'disk', 20)
    samples.points.push({ sampledAtUtc: '2026-10-12T10:05:00Z', value: null, quality: 'Missing' })
    const chart = makeTelemetry('disk', [samples], [], 'iops', '24h')
    expect(chart.filters[1]!.stats[0]!.value).toBe('暂无有效采样')
  })
  it('does not use a latency weight from another timestamp', () => {
    const latency = sample('disk.read.latency', 'disk', 10)
    const weight = sample('disk.read.iops', 'disk', 2)
    weight.points[0]!.sampledAtUtc = '2026-10-12T10:00:06Z'
    const chart = makeTelemetry('disk', [latency, weight], [], 'latency')
    expect(chart.filters[0]!.series[0]!.values).toEqual([null])
  })
  it('labels binary throughput accurately and preserves unknown cumulative totals', () => {
    const chart = makeTelemetry(
      'network',
      [sample('network.receive', 'en0', 1024 ** 2), sample('network.send', 'en0', 512)],
      [],
    )
    expect(chart.unit).toBe('MiB/s')
    const network = chart.filters.find((item) => item.value === 'en0')!
    expect(network.series[0]!.values).toEqual([1])
    expect(network.stats[0]!.value).toBe('1.0 MiB/s')
    expect(network.stats[1]!.value).toBe('512 B/s')
    expect(network.stats[2]!.value).toBe('—')
  })
  it.each([121, 289, 169])(
    'fits %i historical slots inside the plot and keeps gaps disconnected',
    (count) => {
      const values = Array.from({ length: count }, (_, index) => (index === 8 ? null : 2))
      const chart = mount(TelemetryChart, {
        props: {
          labels: values.map(String),
          series: [{ name: '读取', color: '#409eff', values }],
          unit: 'ms',
          window: '7d',
        },
      })
      const path = chart.find('.telemetry-chart__line').attributes('d') ?? ''
      expect(path.split('M')).toHaveLength(3)
      expect(path).toContain('L 986.00')
      const coordinates = [...path.matchAll(/[ML] ([\d.]+) ([\d.]+)/g)]
      expect(coordinates.every((value) => Number(value[1]) >= 46 && Number(value[1]) <= 986)).toBe(
        true,
      )
      expect(chart.find('.telemetry-chart__axis').text()).toContain('7 天前')
      chart.unmount()
    },
  )
  it('weights busy disks by completed I/O and never treats idle as zero latency', () => {
    expect(
      weightedAlignedLatency(
        [
          [2, null, 2],
          [20, null, null],
        ],
        [
          [100, 0, 1],
          [1, 0, 0],
        ],
      ),
    ).toEqual([220 / 101, null, 2])
    expect(weightedAlignedLatency([[2], [null]], [[1], [null]])).toEqual([null])
    expect(weightedAlignedLatency([[2], [null]], [[1], [1]])).toEqual([null])
  })
  it('uses IOPS/ms rather than scaling all disk measurements as bytes', () => {
    const iops = makeTelemetry(
      'disk',
      [sample('disk.read.iops', 'disk', 20), sample('disk.write.iops', 'disk', 10)],
      [],
      'iops',
      '24h',
    )
    expect(iops.unit).toBe('IOPS')
    expect(iops.filters[0]!.series[0]!.values).toEqual([20])
    expect(iops.filters[0]!.stats[0]!.value).toBe('20.0 次/s')
    expect(iops.filters[0]!.stats[0]!.label).toContain('均值')
    const latency = makeTelemetry(
      'disk',
      [
        sample('disk.read.latency', 'disk', null, 'Idle'),
        sample('disk.write.latency', 'disk', null, 'Idle'),
        sample('disk.read.iops', 'disk', 0),
        sample('disk.write.iops', 'disk', 0),
      ],
      [],
      'latency',
      '7d',
    )
    expect(latency.unit).toBe('ms')
    expect(latency.filters[0]!.series[0]!.values).toEqual([null])
    expect(latency.filters[0]!.stats[0]!.value).toBe('无 I/O')
    expect(makeTelemetry('disk', [], [], 'latency').unit).toBe('ms')
  })
  it('uses actual completed-I/O weights for history with unequal per-disk sample counts', () => {
    const first = sample('disk.read.latency', 'sda', 10)
    first.points[0]!.aggregationWeight = 100
    const second = sample('disk.read.latency', 'sdb', 100)
    second.points[0]!.aggregationWeight = 10
    const chart = makeTelemetry(
      'disk',
      [first, second, sample('disk.read.iops', 'sda', 1), sample('disk.read.iops', 'sdb', 1)],
      [],
      'latency',
      '7d',
    )
    expect(chart.filters[0]!.series[0]!.values[0]).toBeCloseTo(2000 / 110)
    second.points[0]!.aggregationWeight = null
    const missing = makeTelemetry('disk', [first, second], [], 'latency', '7d')
    expect(missing.filters[0]!.series[0]!.values).toEqual([null])
  })
})
