import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

for (const width of [320, 390, 1440]) for (const colorScheme of ['light', 'dark'] as const) for (const count of [13, 999]) {
  test(`${width} ${colorScheme} ${count}: badges never intercept the next header control`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); await page.emulateMedia({ colorScheme })
    await installTerminalFixture(page)
    await page.route('**/api/v1/jobs?*', route => route.fulfill({ json: Array.from({ length: 50 }, (_, index) => ({ id: `badge-fixture-${index}`, state: 'Running' })) }))
    await page.route('**/api/v1/notifications/summary', route => route.fulfill({ json: { unreadCount: count, totalCount: count, snapshotSequence: 1 } }))
    await page.goto('/host/logs')
    const job = page.getByRole('button', { name: '任务', exact: true }), notice = page.getByRole('button', { name: '通知', exact: true })
    await expect(job.locator('.el-badge__content')).toHaveText('50')
    await expect(notice.locator('.el-badge__content')).toHaveText(count > 99 ? '99+' : '13')
    await page.evaluate(() => document.fonts.ready.then(() => true))
    const jobBadge = await job.locator('.el-badge__content').boundingBox(), noticeButton = await notice.boundingBox()
    const noticeBadge = await notice.locator('.el-badge__content').boundingBox(), theme = await page.locator('.theme-switch .el-switch__core').boundingBox()
    expect(jobBadge!.x + jobBadge!.width + 4).toBeLessThanOrEqual(noticeButton!.x)
    expect(noticeBadge!.x + noticeBadge!.width + 4).toBeLessThanOrEqual(theme!.x)
    expect(theme!.x + theme!.width).toBeLessThanOrEqual(width)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    await page.locator('.theme-switch .el-switch__core').click()
    await page.screenshot({ path: `test-results/topbar-badges-${width}-${colorScheme}-${count}.png` })
  })
}
