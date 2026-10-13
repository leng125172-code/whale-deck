import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TelemetryChart from '@/components/overview/TelemetryChart.vue'
import type { MetricSeriesSnapshot, OverviewResponse } from '@/services/contracts'
import {
  RollingTelemetryHistory,
  sumAlignedTelemetry,
  telemetrySegments,
} from '@/services/rollingTelemetry'
import { ApiError } from '@/services/apiClient'
import { fetchWorkstationOverview } from '@/services/workstationOverview'

const { request } = vi.hoisted(() => ({ request: vi.fn<(path: string) => Promise<unknown>>() }))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))

const base = Date.parse('2026-10-12T04:00:00Z')
const time = (seconds: number) => new Date(base + seconds * 1000).toISOString()
function series(
  device: string,
  samples: Array<[number, number | null]>,
  kind = 'disk.read',
): MetricSeriesSnapshot {
  return {
    kind,
    deviceId: device,
    unit: 'bytesPerSecond',
    points: samples.map(([seconds, value]) => ({
      sampledAtUtc: time(seconds),
      value,
      quality: 'Good',
    })),
  }
}
function overview(
  seconds = 0,
  scope: 'Administrator' | 'Public' = 'Administrator',
): OverviewResponse {
  return {
    scope,
    platformStatus: 'Ready',
    sampledAtUtc: time(seconds),
    portals: [],
    agent: null,
    host: null,
    resources: [],
    metrics: [],
    applications: [],
    catalogIsStale: false,
  }
}

beforeEach(() => request.mockReset())

describe('Fixed rolling telemetry window', () => {
  it('does not invent zero percent usage when the metrics are absent or invalid', async () => {
    request.mockResolvedValueOnce(overview()).mockResolvedValueOnce([])
    const empty = await fetchWorkstationOverview(new RollingTelemetryHistory())
    expect(empty.usage.every((item) => item.percentage === null && item.value === '暂无采样')).toBe(
      true,
    )
    request
      .mockResolvedValueOnce({
        ...overview(),
        metrics: [{ kind: 'cpu.utilization', value: NaN, quality: 'Good' }],
      })
      .mockResolvedValueOnce([])
    const invalid = await fetchWorkstationOverview(new RollingTelemetryHistory())
    expect(invalid.usage[0]!.percentage).toBeNull()
  })
  it('places short initial history on the right without stretching or invented zeros', () => {
    const history = new RollingTelemetryHistory()
    const points = history.update(
      [
        series('nvme0n1', [
          [-6, 8],
          [0, 9],
        ]),
      ],
      [],
      time(0),
    )[0]!.points
    expect(points).toHaveLength(61)
    expect(points[0]!.sampledAtUtc).toBe(time(-360))
    expect(points.slice(0, 59).every((point) => point.value === null)).toBe(true)
    expect(points.slice(-2).map((point) => point.value)).toEqual([8, 9])
  })

  it('merges duplicate timestamps, retains previous samples and rolls exactly one slot', () => {
    const history = new RollingTelemetryHistory()
    history.update(
      [
        series('disk', [
          [-6, 1],
          [0, 2],
        ]),
      ],
      [],
      time(0),
    )
    const points = history.update(
      [
        series('disk', [
          [0, 3],
          [6, 4],
        ]),
      ],
      [],
      time(6),
    )[0]!.points
    expect(points).toHaveLength(61)
    expect(points.slice(-3).map((point) => point.value)).toEqual([1, 3, 4])
    expect(points[0]!.sampledAtUtc).toBe(time(-354))
  })

  it('aligns devices by timestamps, leaving missing samples blank before summing', () => {
    const result = new RollingTelemetryHistory().update(
      [
        series('a', [
          [-12, 1],
          [0, 3],
        ]),
        series('b', [
          [-6, 5],
          [0, 7],
        ]),
      ],
      [],
      time(0),
    )
    const sum = sumAlignedTelemetry(result.map((item) => item.points.map((point) => point.value)))
    expect(sum.slice(-3)).toEqual([null, null, 10])
    expect(result[0]!.points.slice(-3).map((point) => point.value)).toEqual([1, null, 3])
  })

  it('retains history over a short fetch failure and expires samples after six minutes', () => {
    const history = new RollingTelemetryHistory()
    history.update([series('disk', [[0, 2]])], [], time(0))
    expect(
      history
        .update([], [], time(6))[0]!
        .points.slice(-2)
        .map((point) => point.value),
    ).toEqual([2, null])
    expect(history.update([], [], time(366))).toEqual([])
  })

  it('appends the current Agent sample even when the historical request contains no data', () => {
    const history = new RollingTelemetryHistory()
    history.update([series('disk', [[0, 2]])], [], time(0))
    const result = history.update(
      [],
      [
        {
          kind: 'disk.read',
          deviceId: 'disk',
          unit: 'bytesPerSecond',
          value: 4,
          quality: 'Good',
          sampledAtUtc: time(6),
        },
      ],
      time(6),
    )
    expect(result[0]!.points.slice(-2).map((point) => point.value)).toEqual([2, 4])
  })

  it('uses the freshest sample in a slot and ignores invalid, future and unsupported samples', () => {
    const history = new RollingTelemetryHistory()
    const samples = series('disk', [
      [1, 8],
      [0, 7],
      [600, 9],
    ])
    samples.points.push({ sampledAtUtc: 'invalid', value: 1, quality: 'Good' })
    expect(
      history.update([samples, series('cpu', [[0, 9]], 'cpu.utilization')], [], time(2))[0]!
        .points[60]!.value,
    ).toBe(8)
    history.clear()
    expect(history.update([], [], time(2))).toEqual([])
  })

  it('bounds retained series and treats bad quality, NaN and null as gaps', () => {
    const history = new RollingTelemetryHistory()
    const values = Array.from({ length: 1000 }, (_, index) => series(String(index), [[0, index]]))
    expect(history.update(values, [], time(0))).toHaveLength(256)
    history.clear()
    const bad = series('disk', [
      [-12, NaN],
      [-6, null],
      [0, 9],
    ])
    bad.points[2]!.quality = 'Unavailable'
    expect(
      history
        .update([bad], [], time(0))[0]!
        .points.slice(-3)
        .map((point) => point.value),
    ).toEqual([null, null, null])
  })

  it('breaks chart line and area segments across unknown data', () => {
    expect(telemetrySegments([null, 1, 2, null, 3, null])).toEqual([
      [
        { index: 1, value: 1 },
        { index: 2, value: 2 },
      ],
      [{ index: 4, value: 3 }],
    ])
    const wrapper = mount(TelemetryChart, {
      props: {
        labels: Array.from({ length: 61 }, () => '12:00:00'),
        unit: 'MiB/s',
        series: [{ name: '读取', color: '#409eff', values: [null, 1, 2, null, 3, null] }],
      },
    })
    expect(wrapper.findAll('.telemetry-chart__axis span')).toHaveLength(3)
    expect(wrapper.findAll('svg text')).toHaveLength(5)
    expect(wrapper.get('.telemetry-chart__line').attributes('d')!.match(/M /g)).toHaveLength(2)
    expect(wrapper.text()).not.toContain('12:00:00')
    wrapper.unmount()
  })

  it('preserves history when the history request fails, but clears it on downgrade', async () => {
    const history = new RollingTelemetryHistory()
    request
      .mockResolvedValueOnce(overview())
      .mockResolvedValueOnce([series('disk', [[0, 1048576]])])
    const first = await fetchWorkstationOverview(history)
    expect(first.diskIo.filters[1]!.series[0]!.values[60]).toBe(1)
    request.mockResolvedValueOnce(overview(6)).mockRejectedValueOnce(new Error('Temporary outage'))
    const second = await fetchWorkstationOverview(history)
    expect(second.diskIo.filters[1]!.series[0]!.values.slice(-2)).toEqual([1, null])
    request.mockResolvedValueOnce(overview(12, 'Public'))
    expect((await fetchWorkstationOverview(history)).diskIo.filters[0]!.value).toBe('none')
    expect(history.update([], [], time(12))).toEqual([])
  })

  it('does not retain administrator history after an authorization failure', async () => {
    const history = new RollingTelemetryHistory()
    history.update([series('disk', [[0, 3]])], [], time(0))
    request
      .mockResolvedValueOnce(overview(6))
      .mockRejectedValueOnce(new ApiError('Forbidden', 403, {}))
    await expect(fetchWorkstationOverview(history)).rejects.toMatchObject({ status: 403 })
    expect(history.update([], [], time(6))).toEqual([])
  })
})
