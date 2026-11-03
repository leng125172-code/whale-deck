import { expect, test } from '@playwright/test'
import { installFileFixture } from './fixtures/files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    for (const action of ['apply-update', 'rollback'] as const)
      test(`${width} ${colorScheme} ${action}: visible Chinese cancellation never submits maintenance`, async ({ page }) => {
        await page.setViewportSize({ width, height: 600 })
        await page.emulateMedia({ colorScheme })
        await installFileFixture(page)
        let executions = 0
        await page.route('**/api/v1/platform/**', async (route) => {
          const path = new URL(route.request().url()).pathname
          if (path.endsWith('/plan')) {
            await route.fulfill({ json: {
              planHash: 'test-plan', expiresAtUtc: '2099-01-01T00:00:00Z',
              changes: Array.from({ length: 18 }, (_, i) => `需要逐条核对的更新步骤 ${i + 1}，保留原数据和维护入口。`),
              warnings: ['不会对数据库执行不安全降级；取消不会提交执行。'],
            } })
          } else { executions++; await route.fulfill({ status: 500, json: {} }) }
        })
        await page.goto('/platform/maintenance')
        const trigger = page.getByRole('button', { name: action === 'rollback' ? '回滚' : '执行更新', exact: true })
        await trigger.click()
        const dialog = page.getByRole('dialog', { name: action === 'rollback' ? '平台回滚确认' : '平台更新确认', exact: true })
        await expect(dialog).toBeVisible()
        const cancel = dialog.getByRole('button', { name: '取消', exact: true })
        await expect(cancel).toBeInViewport({ ratio: 1 })
        await expect(dialog.getByRole('button', { name: action === 'rollback' ? '确认回退' : '确认更新', exact: true })).toBeInViewport({ ratio: 1 })
        await page.screenshot({ path: `test-results/maintenance-confirm-${width}-${colorScheme}-${action}.png` })
        await cancel.click()
        await expect(dialog).toHaveCount(0)
        await expect(trigger).toBeEnabled()
        expect(executions).toBe(0)
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
      })
