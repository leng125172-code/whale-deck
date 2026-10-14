import { expect, test, type Page } from '@playwright/test'
import { installTelemetryFixture, telemetryFixture } from './telemetry-fixture'

async function choose(page: Page, name: string, label: string) {
  await page
    .getByRole('combobox', { name, exact: true })
    .locator('xpath=ancestor::div[contains(@class,"el-select__wrapper")]')
    .click()
  await page.getByRole('option', { name: label, exact: true }).click()
}

for (const state of ['fresh', 'idle', 'expired'] as const) {
  test(`live disk cards distinguish ${state} samples from a trailing unsampled slot`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 1000 })
    await page.emulateMedia({ colorScheme: 'dark' })
    await installTelemetryFixture(page)
    const observedAt = Math.floor(Date.now() / 6000) * 6000
    const sampledAtUtc = new Date(observedAt - (state === 'expired' ? 18000 : 6000)).toISOString()
    const samples = [
      { kind: 'disk.read.iops', value: 0, quality: 'Good', unit: 'operationsPerSecond' },
      { kind: 'disk.write.iops', value: 14.33, quality: 'Good', unit: 'operationsPerSecond' },
      { kind: 'disk.read.latency', value: 0, quality: 'Idle', unit: 'milliseconds' },
      { kind: 'disk.write.latency', value: 6.24, quality: 'Good', unit: 'milliseconds' },
    ].map((sample) => ({ ...sample, deviceId: 'nvme0n1', sampledAtUtc }))
    await page.route('**/api/v1/overview', (route) =>
      route.fulfill({
        json: {
          scope: 'Administrator',
          sampledAtUtc: new Date(observedAt).toISOString(),
          platformStatus: 'Healthy',
          portals: [],
          agent: { available: true, dockerAvailable: true, status: 'Ready' },
          host: null,
          resources: [],
          metrics: samples,
          applications: [],
          catalogIsStale: false,
        },
      }),
    )
    await page.route('**/api/v1/metrics?**', (route) =>
      route.fulfill({
        json: samples.map((sample) => ({
          kind: sample.kind,
          deviceId: sample.deviceId,
          unit: sample.unit,
          points: [{ sampledAtUtc, value: sample.value, quality: sample.quality }],
        })),
      }),
    )
    await page.goto('/')
    const panel = page.locator('#monitoring')
    await panel.getByText('磁盘 IO', { exact: true }).click()
    await choose(page, '磁盘指标', 'IOPS')
    await expect(panel.locator('.telemetry-stat').nth(1)).toContainText(
      state === 'expired' ? '暂无有效采样' : '14.3 次/s',
    )
    await choose(page, '磁盘指标', '读写延迟')
    await expect(panel.locator('.telemetry-stat').first()).toContainText(
      state === 'expired' ? '暂无有效采样' : '无 I/O',
    )
    await expect(panel.locator('.telemetry-stat').nth(1)).toContainText(
      state === 'expired' ? '暂无有效采样' : '6.24 ms',
    )
    await expect
      .poll(() =>
        page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        ),
      )
      .toBeLessThanOrEqual(1)
  })
}

for (const width of [320, 1045, 1440]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: history/disk controls and chart fit`, async ({ page }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await installTelemetryFixture(page)
      await page.goto('/')
      const panel = page.locator('#monitoring')
      await expect(panel.getByRole('heading', { name: '实时监控' })).toBeVisible()
      await expect(panel.locator('.telemetry-chart')).toHaveAttribute('aria-label', /单位 MiB\/s/)
      await panel.getByText('磁盘 IO', { exact: true }).click()
      for (const [label, axis] of [
        ['1 小时', '1 小时前'],
        ['24 小时', '24 小时前'],
        ['7 天', '7 天前'],
      ]) {
        await choose(page, '监控时间窗口', label!)
        await expect(
          panel.locator('.telemetry-chart__axis').getByText(axis!, { exact: true }),
        ).toBeVisible()
        await expect(panel.locator('.telemetry-chart path').first()).toHaveAttribute('d', /M/)
      }
      await choose(page, '磁盘指标', 'IOPS')
      await expect(panel.locator('.telemetry-chart')).toHaveAttribute('aria-label', /单位 IOPS/)
      await choose(page, '磁盘指标', '读写延迟')
      await expect(panel.locator('.telemetry-chart')).toHaveAttribute('aria-label', /单位 ms/)
      await choose(page, '监控设备', 'nvme0n1')
      await expect(panel.locator('.telemetry-stat').first()).toContainText('ms')
      await panel.scrollIntoViewIfNeeded()
      const bounds = await panel
        .locator('.monitoring-toolbar > *, .telemetry-chart, .telemetry-summary')
        .evaluateAll((elements) =>
          elements.map((element) => {
            const rect = element.getBoundingClientRect()
            return { left: rect.left, right: rect.right }
          }),
        )
      expect(bounds.every((rect) => rect.left >= 0 && rect.right <= width)).toBe(true)
      await expect
        .poll(() =>
          page.evaluate(
            () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
          ),
        )
        .toBeLessThanOrEqual(1)
      await panel.screenshot({
        path: `test-results/telemetry-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      await choose(page, '监控设备', '全部磁盘')
      await choose(page, '磁盘指标', '读写速率')
      await choose(page, '监控时间窗口', '实时 · 6 分钟')
      await expect(panel.getByRole('heading', { name: '实时监控' })).toBeVisible()
      const path = panel.locator('.telemetry-chart path').first()
      await expect(path).toHaveAttribute('d', /M/)
      // A real six-second refresh keeps the existing rolling window instead of resetting it.
      const before = (await path.getAttribute('d'))!.split('M').length
      await page.waitForTimeout(6500)
      expect((await path.getAttribute('d'))!.split('M').length).toBeGreaterThanOrEqual(before - 1)
      expect(errors).toEqual([])
    })
  }
}

test('late history is ignored; failures and permission loss do not show stale administrator data', async ({
  page,
}) => {
  await installTelemetryFixture(page)
  let oneHourRequested = false
  let failure = false
  let forbidden = false
  await page.route('**/api/v1/metrics?**', async (route) => {
    const window = new URL(route.request().url()).searchParams.get('window') ?? 'live'
    if (window === '1h') {
      oneHourRequested = true
      await new Promise((resolve) => setTimeout(resolve, 1200))
    }
    try {
      await route.fulfill(
        forbidden
          ? { status: 403, json: { title: 'Forbidden' } }
          : failure
            ? { status: 503, json: { title: 'Unavailable' } }
            : { json: telemetryFixture(window) },
      )
    } catch {
      /* A deliberately aborted request can no longer be fulfilled. */
    }
  })
  await page.goto('/')
  const panel = page.locator('#monitoring')
  await expect(panel).toBeVisible()
  await choose(page, '监控时间窗口', '1 小时')
  await expect.poll(() => oneHourRequested).toBe(true)
  await choose(page, '监控时间窗口', '24 小时')
  await expect(panel.locator('.telemetry-chart')).toHaveAttribute('aria-label', /24 小时前/)
  await page.waitForTimeout(1400)
  await expect(
    panel.locator('.telemetry-chart__axis').getByText('24 小时前', { exact: true }),
  ).toBeVisible()
  failure = true
  await choose(page, '监控时间窗口', '7 天')
  await expect(panel.getByText('历史查询失败，请稍后重试；主机当前状态仍可查看。')).toBeVisible()
  await expect(panel.locator('.telemetry-chart path')).toHaveCount(0)
  forbidden = true
  await choose(page, '监控时间窗口', '24 小时')
  await expect(panel).toHaveCount(0)
  await expect(page.locator('.overview-page .el-alert')).toBeVisible()
})
