import { expect, test } from '@playwright/test'
import { installTreeOverwriteRestoringFixture } from './fixtures/tree-overwrite-restorings.mjs'

// UI contract evidence only; not real Authentik, PostgreSQL, UDS or deletion.
test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH },
})

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    for (const scenario of ['Purged', 'Uncertain', 'Active'] as const)
      test(`${width} ${colorScheme} ${scenario}: historical proof cannot masquerade as recoverable archive`, async ({
        page,
      }) => {
        await page.setViewportSize({ width, height: 900 })
        await page.emulateMedia({ colorScheme })
        const control = await installTreeOverwriteRestoringFixture(page)
        const record = control.catalogue.records[0]!
        record.archiveState = scenario === 'Active' ? 'Retained' : scenario
        record.cleanupActive = scenario === 'Active'
        await page.goto('/files')
        await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
        const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
        await expect(panel.getByRole('button', { name: '恢复目录', exact: true })).toBeDisabled()
        await expect(
          panel.getByRole('button', { name: '覆盖现有目录（先备份）', exact: true }),
        ).toHaveCount(0)
        await expect(
          panel.getByText(
            scenario === 'Active'
              ? '此捕获正在清理或等待结果核实，暂不能恢复'
              : scenario === 'Purged'
                ? '此归档已永久清理，不能恢复'
                : '归档清理结果不确定，不能确认可恢复',
            { exact: true },
          ),
        ).toBeVisible()
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
          ),
        ).toBe(true)
        expect(control.state.writes).toHaveLength(0)
        expect(control.catalogue.state.writes).toHaveLength(0)
        if (width === 320) {
          const item = panel.locator('.tree-capture-item').first()
          const title = await item.locator('strong').boundingBox()
          const card = await item.boundingBox()
          expect(title!.width).toBeGreaterThanOrEqual(card!.width - 30)
        }
        if (scenario === 'Uncertain')
          await page.screenshot({
            path: `test-results/capture-cleanup-${width}-${colorScheme}.png`,
          })
      })

test('originals-only cleanup does not disable a still-unpurged archive', async ({ page }) => {
  const control = await installTreeOverwriteRestoringFixture(page)
  control.catalogue.records[0]!.originalsState = 'Purged'
  await page.goto('/files')
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
  await expect(panel.getByRole('button', { name: '恢复目录', exact: true })).toBeEnabled()
  await expect(
    panel.getByRole('button', { name: '覆盖现有目录（先备份）', exact: true }),
  ).toBeEnabled()
  await expect(
    panel.getByText('归档：无已知清理（恢复前复验） · 原件：已永久清理', { exact: true }),
  ).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
})
