import { expect, test } from '@playwright/test'
import { configurationResponse } from './fixtures/configuration.mjs'

const rule = {
  id: 'capacity-rule',
  ruleType: 'BackupStorageCapacity',
  resourceSelectorJson: '{}',
  thresholdJson: '{}',
  evaluationWindowSeconds: 60,
  severity: 'Warning',
  isEnabled: true,
  version: 0,
}
function event(available = true, state = 'Active') {
  return {
    id: 'capacity-event',
    ruleId: rule.id,
    resourceId: 'demo',
    state,
    severity: 'Warning',
    occurrenceCount: 1,
    firstOccurredAtUtc: new Date().toISOString(),
    lastOccurredAtUtc: new Date().toISOString(),
    recoveredAtUtc: null,
    acknowledgedBySubject: null,
    acknowledgedAtUtc: null,
    silencedUntilUtc: null,
    summaryCode: 'ALERT_BACKUPSTORAGECAPACITY',
    capacity: {
      policyId: 'policy',
      targetDirectoryId: 'database-platform.backup.hdd',
      available,
      usedPercent: available ? 82.4 : null,
      freeBytes: available ? 19 * 1024 ** 3 : null,
      warningPercent: 80,
      criticalPercent: 90,
      observedAtUtc: new Date().toISOString(),
    },
  }
}
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) => {
    const path = route.request().url().split('/api/v1/')[1]?.split('?')[0]
    if (path === 'alerts') return route.fulfill({ json: [event()] })
    if (path === 'alerts/rules') return route.fulfill({ json: [rule] })
    if (path === 'alerts/history') return route.fulfill({ json: [] })
    return route.fulfill({ json: configurationResponse(route.request().url()) })
  })
})
for (const width of [320, 390, 1045, 1440])
  for (const scheme of ['light', 'dark'] as const)
    test(`${width}px ${scheme}: capacity details and policy-managed rule remain readable`, async ({
      page,
    }, info) => {
      await page.setViewportSize({ width, height: 844 })
      await page.emulateMedia({ colorScheme: scheme })
      await page.goto('/alerts')
      await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toBeVisible()
      await expect(page.getByText('预警 80% / 临界 90%')).toBeVisible()
      const capacity = await page.getByText('HDD 已用 82.4% · 可用 19.0 GiB').boundingBox()
      expect(capacity).not.toBeNull()
      expect(capacity!.x).toBeGreaterThanOrEqual(0)
      expect(capacity!.x + capacity!.width).toBeLessThanOrEqual(width)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({ path: info.outputPath('capacity.png'), scale: 'css' })
      await page.getByRole('tab', { name: '告警规则', exact: true }).click()
      await expect(page.getByText('跟随备份策略', { exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: '编辑', exact: true })).toHaveCount(0)
    })
test('unknown capacity and recovered controls never imply a healthy zero sample', async ({
  page,
}) => {
  await page.route('**/api/v1/alerts?**', (route) => route.fulfill({ json: [event(false)] }))
  await page.goto('/alerts')
  await expect(page.getByText('容量未知 · 不会因此解除风险')).toBeVisible()
  await page.route('**/api/v1/alerts?**', (route) =>
    route.fulfill({ json: [event(true, 'Recovered')] }),
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '静默', exact: true })).toBeDisabled()
})
test('403 clears private capacity and blocks another refresh', async ({ page }) => {
  await page.goto('/alerts')
  await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toBeVisible()
  await page.route('**/api/v1/alerts?**', (route) =>
    route.fulfill({ status: 403, json: { title: '权限已失效' } }),
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.getByText('权限已失效')).toBeVisible()
  await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '新增规则', exact: true })).toBeDisabled()
})

test('confirmation, silence cancellation and accepted silence use normal controls', async ({
  page,
}) => {
  const writes: string[] = []
  await page.route('**/api/v1/alerts/capacity-event/**', (route) => {
    writes.push(route.request().url().split('/').pop() || '')
    return route.fulfill({ json: event() })
  })
  await page.goto('/alerts')
  await page.getByRole('button', { name: '确认', exact: true }).click()
  await expect.poll(() => writes).toEqual(['acknowledge'])
  await page.getByRole('button', { name: '静默', exact: true }).click()
  await page.getByRole('button', { name: '取消', exact: true }).click()
  expect(writes).toEqual(['acknowledge'])
  await page.getByRole('button', { name: '静默', exact: true }).click()
  await page.getByRole('button', { name: '开始静默', exact: true }).click()
  await expect.poll(() => writes).toEqual(['acknowledge', 'silence'])
})

test('503 preserves prior observations and permission loss during a write clears them', async ({
  page,
}) => {
  await page.goto('/alerts')
  await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toBeVisible()
  await page.route('**/api/v1/alerts?**', (route) =>
    route.fulfill({ status: 503, json: { title: '容量服务暂不可用' } }),
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.getByText('容量服务暂不可用')).toBeVisible()
  await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toBeVisible()
  await page.route('**/api/v1/alerts/capacity-event/acknowledge', (route) =>
    route.fulfill({ status: 403, json: { title: '管理权限已失效' } }),
  )
  await page.getByRole('button', { name: '确认', exact: true }).click()
  await expect(page.getByText('管理权限已失效')).toBeVisible()
  await expect(page.getByText('HDD 已用 82.4% · 可用 19.0 GiB')).toHaveCount(0)
})
