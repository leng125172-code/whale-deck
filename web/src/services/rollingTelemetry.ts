import type { MetricSeriesSnapshot, MetricValue } from '@/services/contracts'

export const TELEMETRY_INTERVAL_MS = 6000
export const TELEMETRY_WINDOW_MS = 360_000
export const TELEMETRY_SLOT_COUNT = TELEMETRY_WINDOW_MS / TELEMETRY_INTERVAL_MS + 1
const MAX_SERIES = 256
const supportedKinds = new Set([
  'network.receive',
  'network.send',
  'disk.read',
  'disk.write',
  'disk.read.iops',
  'disk.write.iops',
  'disk.read.latency',
  'disk.write.latency',
])

type Point = MetricSeriesSnapshot['points'][number]

/** View-owned, bounded history. Never share administrator samples between sessions. */
export class RollingTelemetryHistory {
  private readonly series = new Map<
    string,
    { kind: string; deviceId: string; unit: string; points: Map<number, Point> }
  >()

  clear() {
    this.series.clear()
  }

  update(history: MetricSeriesSnapshot[], current: MetricValue[], sampledAtUtc: string) {
    const sampledAt = Date.parse(sampledAtUtc)
    const observedAt = Number.isFinite(sampledAt) ? sampledAt : Date.now()
    // Health is observed before the metrics RPC. A collector tick between them is legitimate,
    // but an implausibly future point must not move the whole plot into the future.
    const newestCurrent = current.reduce((newest, item) => {
      const timestamp = Date.parse(item.sampledAtUtc)
      return supportedKinds.has(item.kind) &&
        Number.isFinite(timestamp) &&
        timestamp <= observedAt + TELEMETRY_INTERVAL_MS
        ? Math.max(newest, timestamp)
        : newest
    }, observedAt)
    const end = Math.floor(newestCurrent / TELEMETRY_INTERVAL_MS) * TELEMETRY_INTERVAL_MS
    const start = end - TELEMETRY_WINDOW_MS
    const add = (kind: string, deviceId: string, unit: string, point: Point) => {
      if (!supportedKinds.has(kind)) return
      const timestamp = Date.parse(point.sampledAtUtc)
      if (!Number.isFinite(timestamp)) return
      const slot = Math.floor(timestamp / TELEMETRY_INTERVAL_MS) * TELEMETRY_INTERVAL_MS
      if (slot < start || slot > end) return
      const key = JSON.stringify([kind, deviceId])
      let series = this.series.get(key)
      if (!series) {
        if (this.series.size >= MAX_SERIES) return
        series = { kind, deviceId, unit, points: new Map() }
        this.series.set(key, series)
      }
      const previous = series.points.get(slot)
      if (previous && Date.parse(previous.sampledAtUtc) > timestamp) return
      series.points.set(slot, {
        ...point,
        value:
          point.quality.toLowerCase() === 'good' &&
          point.value !== null &&
          Number.isFinite(point.value) &&
          point.value >= 0
            ? point.value
            : null,
      })
    }
    // Drop old slots before admitting new devices, including after a long offline period.
    for (const [key, series] of this.series) {
      for (const timestamp of series.points.keys()) {
        if (timestamp < start || timestamp > end) series.points.delete(timestamp)
      }
      if (series.points.size === 0) this.series.delete(key)
    }
    for (const item of history) {
      for (const point of item.points) add(item.kind, item.deviceId, item.unit, point)
    }
    for (const item of current) add(item.kind, item.deviceId, item.unit, item)
    return [...this.series.values()].map((series): MetricSeriesSnapshot => ({
      kind: series.kind,
      deviceId: series.deviceId,
      unit: series.unit,
      points: Array.from({ length: TELEMETRY_SLOT_COUNT }, (_, index) => {
        const timestamp = start + index * TELEMETRY_INTERVAL_MS
        const point = series.points.get(timestamp)
        return {
          sampledAtUtc: new Date(timestamp).toISOString(),
          value: point?.value ?? null,
          quality: point?.quality ?? 'Missing',
        }
      }),
    }))
  }
}

/** Aggregate only synchronized, known samples; missing is not measured zero. */
export function sumAlignedTelemetry(values: Array<Array<number | null>>) {
  const length = Math.max(0, ...values.map((item) => item.length))
  return Array.from({ length }, (_, index) => {
    const samples = values.map((item) => item[index])
    return samples.some((value) => value === null || value === undefined)
      ? null
      : samples.reduce<number>((sum, value) => sum + (value ?? 0), 0)
  })
}

/** Latency is weighted by completed I/O. An idle disk contributes no weight, not zero latency. */
export function weightedAlignedLatency(
  values: Array<Array<number | null>>,
  weights: Array<Array<number | null>>,
) {
  const length = Math.max(0, ...values.map((item) => item.length))
  return Array.from({ length }, (_, index) => {
    let total = 0
    let weighted = 0
    for (let device = 0; device < values.length; device++) {
      const weight = weights[device]?.[index]
      const value = values[device]?.[index]
      if (weight === null || weight === undefined || !Number.isFinite(weight) || weight < 0)
        return null
      if (weight === 0) continue
      if (value === null || value === undefined || !Number.isFinite(value) || value < 0) return null
      total += weight
      weighted += value * weight
    }
    return total > 0 ? weighted / total : null
  })
}

export function telemetrySegments(values: Array<number | null>) {
  const segments: Array<Array<{ index: number; value: number }>> = []
  let segment: Array<{ index: number; value: number }> = []
  values.forEach((value, index) => {
    if (value === null || !Number.isFinite(value)) {
      if (segment.length) segments.push(segment)
      segment = []
    } else {
      segment.push({ index, value })
    }
  })
  if (segment.length) segments.push(segment)
  return segments
}
