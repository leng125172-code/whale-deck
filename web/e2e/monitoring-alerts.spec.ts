import { expect, test } from '@playwright/test'

const rule = {
  id: 'native-style-rule',
  ruleType: 'CpuUtilization',
  resourceSelectorJson: '{}',
  thresholdJson: '{"value":85,"criticalValue":95,"recoveryValue":80}',
  evaluationWindowSeconds: 60,
  severity: 'Warning',
  isEnabled: true,
  version: 0,
}
const now = new Date().toISOString()
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: monitoring quality, thresholds and lifecycle remain usable`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      let unknown = false
      let saved: Record<string, unknown> | null = null
      await page.route('**/api/v1/**', (route) => {
        const endpoint = new URL(route.request().url()).pathname.replace('/api/v1/', '')
        if (endpoint === 'alerts/rules') {
          if (route.request().method() === 'POST') saved = route.request().postDataJSON()
          return route.fulfill({ json: route.request().method() === 'POST' ? rule : [rule] })
        }
        if (endpoint === 'alerts')
          return route.fulfill({
            json: [
              {
                id: 'alarm',
                ruleId: rule.id,
                resourceId: 'local',
                state: 'Active',
                severity: 'Critical',
                occurrenceCount: 2,
                firstOccurredAtUtc: now,
                lastOccurredAtUtc: now,
                recoveredAtUtc: null,
                acknowledgedBySubject: null,
                acknowledgedAtUtc: null,
                silencedUntilUtc: null,
                summaryCode: 'ALERT_CPUUTILIZATION',
                monitoring: {
                  triggered: unknown ? null : true,
                  severity: 'Critical',
                  value: unknown ? null : 96.1,
                  coveragePercent: unknown ? 0 : 100,
                  observedAtUtc: now,
                  reason: unknown ? 'TelemetryUnknown' : 'Observed',
                },
              },
            ],
          })
        return route.fulfill({ json: [] })
      })
      await page.goto('/alerts')
      await expect(page.getByText('窗口均值 96.1% · 有效采样 100%')).toBeVisible()
      unknown = true
      await page.getByRole('button', { name: '刷新', exact: true }).click()
      await expect(page.getByText('采样不足或已失联 · 不会自动恢复')).toBeVisible()
      await page.getByRole('button', { name: '新增规则' }).click()
      const dialog = page.getByRole('dialog')
      await expect(dialog).toBeVisible()
      await dialog.getByLabel('告警阈值（%）').fill('98')
      await dialog.getByRole('button', { name: '保存', exact: true }).click()
      await expect(page.getByText('阈值需要满足：恢复阈值 ≤ 告警阈值 ≤ 严重阈值')).toBeVisible()
      expect(saved).toBeNull()
      await dialog.getByLabel('严重阈值（%，可留空）').fill('99')
      await dialog.getByLabel('恢复阈值（%，可留空）').fill('90')
      await dialog.getByLabel('最低有效采样（%）').fill('75')
      await page.screenshot({
        path: `test-results/alert-thresholds-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      const save = dialog.getByRole('button', { name: '保存', exact: true })
      const bounds = await save.boundingBox()
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(900)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await save.click()
      await expect(dialog).not.toBeVisible()
      expect(JSON.parse(saved!.thresholdJson as string)).toEqual({
        operator: '>=',
        value: 98,
        criticalValue: 99,
        recoveryValue: 90,
        minimumCoveragePercent: 75,
      })
    })
