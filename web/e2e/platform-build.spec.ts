import { expect, test } from '@playwright/test'
import { installFileFixture } from './fixtures/files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
const revision = 'a'.repeat(40)
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: actual-version card shows bounded full revision without false global sync`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await installFileFixture(page)
      await page.route('**/api/v1/system/status', (route) =>
        route.fulfill({
          json: {
            build: { component: 'WhaleDeck.Api', version: '0.1.0', sourceRevision: revision },
          },
        }),
      )
      await page.route('**/build-info.json', (route) =>
        route.fulfill({
          json: {
            component: 'WhaleDeck.Gateway',
            version: '0.1.0',
            sourceRevision: revision,
          },
        }),
      )
      await page.goto('/platform/maintenance')
      const card = page.getByRole('article', { name: '实际构建版本' })
      await expect(card.locator('code')).toHaveCount(2)
      await expect(card.locator('code').first()).toHaveAttribute('title', revision)
      await expect(
        card.getByText('此处不代表 Worker、Agent 或依赖服务已经同步；完整核验由部署验证完成。'),
      ).toBeVisible()
      await expect(card.getByRole('alert')).toHaveCount(0)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({
        path: `test-results/platform-build-${width}-${colorScheme}.png`,
        fullPage: true,
      })
    })

test('different revisions are a warning, not a false successful update', async ({ page }) => {
  await installFileFixture(page)
  await page.route('**/api/v1/system/status', (route) =>
    route.fulfill({
      json: {
        build: { component: 'WhaleDeck.Api', version: '0.1.0', sourceRevision: revision },
      },
    }),
  )
  await page.route('**/build-info.json', (route) =>
    route.fulfill({
      json: {
        component: 'WhaleDeck.Gateway',
        version: '0.1.0',
        sourceRevision: 'b'.repeat(40),
      },
    }),
  )
  await page.goto('/platform/maintenance')
  await expect(
    page.getByRole('alert').getByText('API 与网页源码提交不一致，可能仍在更新中。'),
  ).toBeVisible()
})

test('missing build evidence is explicit and never falls back to configured version', async ({
  page,
}) => {
  await installFileFixture(page)
  await page.route('**/api/v1/system/status', (route) => route.fulfill({ status: 503, json: {} }))
  await page.route('**/build-info.json', (route) => route.fulfill({ status: 404, body: 'missing' }))
  await page.goto('/platform/maintenance')
  const card = page.getByRole('article', { name: '实际构建版本' })
  await expect(card.getByText('未能读取版本', { exact: true })).toHaveCount(2)
  await expect(card.locator('code')).toHaveCount(0)
})
