import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'
import { relationshipInspect, relationshipResponse } from './fixtures/relationships.mjs'

// Synthetic UI layout only; deployed Docker/identity acceptance is separate.
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
const environmentNames = Array.from(
  { length: 18 },
  (_, index) => `Authentication__LongConfigurationSection${index}__` + 'PublicSetting'.repeat(8),
)

for (const width of [320, 760, 1045, 1046, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: detail metrics stay readable with long configuration`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const fixture = await installTerminalFixture(page)
      await page.route('**/api/v1/containers**', (route) => {
        const path = new URL(route.request().url()).pathname
        return route.fulfill({
          json: path.endsWith('/inspect')
            ? { ...relationshipInspect, environmentNames }
            : path.endsWith('/stats')
              ? {
                  values: {
                    cpu: '1.99%',
                    memory: '156.3 MiB / 1.0 GiB',
                    memoryPercent: '15.26%',
                    network: '556 MiB / 209 MiB',
                    block: '0 B / 0 B',
                    pids: '24',
                  },
                }
              : path.endsWith('/containers')
                ? relationshipResponse(path).map((item: { status: string }) => ({
                    ...item,
                    status: 'Up 58 minutes (healthy)',
                  }))
                : relationshipResponse(path),
        })
      })
      await page.goto('/containers')
      await page
        .locator('.container-card')
        .filter({ hasText: 'demo-web' })
        .getByRole('button', { name: '详情', exact: true })
        .click()
      const detail = page.locator('.container-detail')
      await expect(detail.getByText(environmentNames[0]!, { exact: false })).toBeVisible()
      await page.evaluate(() => document.fonts.ready.then(() => true))
      const value = (label: string) =>
        detail
          .locator('tr')
          .filter({ has: page.getByText(label, { exact: true }) })
          .locator('.el-descriptions__content')
          .first()
      for (const label of ['状态', 'CPU', '网络 I/O', '资源限制']) {
        const cell = value(label)
        const geometry = await cell.evaluate((element) => ({
          width: element.getBoundingClientRect().width,
          height: element.getBoundingClientRect().height,
          lineHeight: Number.parseFloat(getComputedStyle(element).lineHeight),
        }))
        expect(geometry.width).toBeGreaterThan(160)
        expect(geometry.height).toBeLessThan(geometry.lineHeight * 3 + 20)
      }
      const environment = value('环境变量名')
      await expect(environment).toContainText(environmentNames.at(-1)!)
      const configurations = environment.locator('xpath=ancestor::table')
      await expect(configurations.locator('tr').first().locator('td')).toHaveCount(2)
      expect(await environment.evaluate((element) => element.clientWidth)).toBeGreaterThan(
        width > 1045 ? 500 : 160,
      )
      const summary = detail.locator('.container-detail__summary')
      await expect(summary.locator('tr').first().locator('td, th')).toHaveCount(
        width > 1045 ? 4 : 2,
      )
      if (width > 1045) {
        const values = summary.locator('tr').first().locator('.el-descriptions__content')
        const first = await values.nth(0).boundingBox(),
          second = await values.nth(1).boundingBox()
        expect(Math.abs(first!.width - second!.width)).toBeLessThanOrEqual(2)
      }
      expect(await detail.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(
        true,
      )
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await detail.locator('.el-descriptions').first().scrollIntoViewIfNeeded()
      await page.screenshot({ path: `test-results/container-detail-${width}-${colorScheme}.png` })
      expect(fixture.state.writes).toEqual([])
      expect(errors).toEqual([])
    })
