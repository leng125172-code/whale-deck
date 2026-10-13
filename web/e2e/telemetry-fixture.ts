import type { Page } from '@playwright/test'

export const telemetryKinds = [
  'network.receive',
  'network.send',
  'disk.read',
  'disk.write',
  'disk.read.iops',
  'disk.write.iops',
  'disk.read.latency',
  'disk.write.latency',
]

export function telemetryFixture(window: string) {
  const resolution =
    ({ live: 6, '1h': 30, '24h': 300, '7d': 3600 } as Record<string, number>)[window] ?? 6
  const count =
    ({ live: 61, '1h': 121, '24h': 289, '7d': 169 } as Record<string, number>)[window] ?? 61
  const end = Math.floor(Date.now() / (resolution * 1000)) * resolution * 1000
  return telemetryKinds.flatMap((kind) =>
    (kind.startsWith('network.') ? ['enp4s0', 'enxusb'] : ['nvme0n1', 'sda']).map((deviceId) => ({
      kind,
      deviceId,
      unit: kind.endsWith('.latency')
        ? 'milliseconds'
        : kind.endsWith('.iops')
          ? 'operationsPerSecond'
          : 'bytesPerSecond',
      windowStartUtc: new Date(end - (count - 1) * resolution * 1000).toISOString(),
      windowEndUtc: new Date(end).toISOString(),
      resolutionSeconds: resolution,
      points: Array.from({ length: count }, (_, index) => ({
        sampledAtUtc: new Date(end - (count - 1 - index) * resolution * 1000).toISOString(),
        value:
          index % 17 === 8
            ? null
            : kind.endsWith('.latency')
              ? 2 + (index % 3)
              : kind.endsWith('.iops')
                ? 10 + (index % 30)
                : (2 + (index % 8)) * 1024 * 1024,
        quality: index % 17 === 8 ? 'Missing' : 'Good',
        aggregationWeight:
          window !== 'live' && kind.endsWith('.latency') && index % 17 !== 8
            ? (10 + (index % 30)) * (resolution / 6)
            : null,
      })),
    })),
  )
}

export async function installTelemetryFixture(page: Page) {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) => {
    const url = new URL(route.request().url())
    const history = telemetryFixture('live')
    const current = history.map((series) => ({
      ...series.points[series.points.length - 1],
      kind: series.kind,
      deviceId: series.deviceId,
      unit: series.unit,
    }))
    return route.fulfill({
      json: url.pathname.endsWith('/metrics')
        ? telemetryFixture(url.searchParams.get('window') ?? 'live')
        : url.pathname.endsWith('/overview')
          ? {
              scope: 'Administrator',
              sampledAtUtc: new Date().toISOString(),
              platformStatus: 'Healthy',
              portals: [],
              agent: { available: true, dockerAvailable: true, status: 'Ready' },
              host: null,
              resources: [],
              metrics: current,
              applications: [],
              catalogIsStale: false,
            }
          : [],
    })
  })
}
