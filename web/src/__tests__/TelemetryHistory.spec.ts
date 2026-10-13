import { describe, expect, it } from 'vitest'
import { weightedAlignedLatency } from '@/services/rollingTelemetry'
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
})
