import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

// Playwright hides scrollbars by default in headless mode; that would silently
// remove the exact 15px content-area reduction this regression must exercise.
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174', launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, ignoreDefaultArgs: ['--hide-scrollbars'] } })

for (const width of [320, 390, 1440]) for (const colorScheme of ['light', 'dark'] as const) for (const count of [13, 999]) for (const classicScrollbar of [false, true]) {
  test(`${width} ${colorScheme} ${count} scrollbar=${classicScrollbar}: badges and controls fit the content area`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); await page.emulateMedia({ colorScheme })
    await installTerminalFixture(page)
    await page.route('**/api/v1/jobs?*', route => route.fulfill({ json: Array.from({ length: 50 }, (_, index) => ({ id: `badge-fixture-${index}`, state: 'Running' })) }))
    await page.route('**/api/v1/notifications/summary', route => route.fulfill({ json: { unreadCount: count, totalCount: count, snapshotSequence: 1 } }))
    await page.goto('/host/logs')
    if (classicScrollbar) await page.addStyleTag({ content: 'html {overflow-y:scroll !important; scrollbar-gutter:stable} body {min-height:2000px !important} ::-webkit-scrollbar {width:15px !important}' })
    const job = page.getByRole('button', { name: '任务', exact: true }), notice = page.getByRole('button', { name: '通知', exact: true })
    await expect(job.locator('.el-badge__content')).toHaveText('50')
    await expect(notice.locator('.el-badge__content')).toHaveText(count > 99 ? '99+' : '13')
    await page.evaluate(() => document.fonts.ready.then(() => true))
    const jobBadge = await job.locator('.el-badge__content').boundingBox(), noticeButton = await notice.boundingBox()
    const noticeBadge = await notice.locator('.el-badge__content').boundingBox(), theme = await page.locator('.theme-switch .el-switch__core').boundingBox()
    expect(jobBadge!.x + jobBadge!.width + 4).toBeLessThanOrEqual(noticeButton!.x)
    expect(noticeBadge!.x + noticeBadge!.width + 4).toBeLessThanOrEqual(theme!.x)
    const clientWidth = await page.evaluate(() => document.documentElement.clientWidth)
    if (classicScrollbar) expect(clientWidth).toBe(width - 15)
    for (const control of await page.locator('.topbar button:visible, .topbar .el-switch__core, .topbar .el-badge__content:visible, .topbar .backend-version').all()) {
      const rect = await control.boundingBox()
      expect(rect!.x).toBeGreaterThanOrEqual(0)
      expect(rect!.x + rect!.width, await control.getAttribute('aria-label') || await control.getAttribute('class') || 'header control').toBeLessThanOrEqual(clientWidth)
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    const initiallyDark = await page.locator('html').evaluate(element => element.classList.contains('dark'))
    await page.locator('.theme-switch .el-switch__core').click()
    await expect(page.locator('html')).toHaveClass(initiallyDark ? /^(?!.*\bdark\b).*$/ : /\bdark\b/)
    await page.locator('.theme-switch .el-switch__core').click()
    await expect(page.locator('html')).toHaveClass(initiallyDark ? /\bdark\b/ : /^(?!.*\bdark\b).*$/)
    await page.screenshot({ path: `test-results/topbar-badges-${width}-${colorScheme}-${count}-${classicScrollbar}.png` })
  })
}

test('320 classic scrollbar: header controls remain operable by pointer and keyboard', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 720 })
  await installTerminalFixture(page)
  await page.goto('/host/logs')
  await page.addStyleTag({ content: 'html {overflow-y:scroll !important; scrollbar-gutter:stable} body {min-height:2000px !important} ::-webkit-scrollbar {width:15px !important}' })
  await page.getByRole('button', { name: '搜索平台功能', exact: true }).focus()
  await page.keyboard.press('Enter')
  const search = page.getByRole('dialog', { name: '搜索', exact: true })
  await expect(search).toBeVisible()
  await search.getByPlaceholder('搜索功能或资源').fill('不存在的功能'.repeat(40))
  await expect(search).toContainText('没有可访问的匹配功能')
  await page.keyboard.press('Escape'); await expect(search).toBeHidden()
  await page.getByRole('button', { name: '打开导航', exact: true }).click()
  await expect(page.locator('.sidebar')).toHaveClass(/sidebar--mobile-open/)
  await page.locator('.navigation-backdrop').click({ position: { x: 285, y: 120 } })
  await expect(page.locator('.sidebar')).not.toHaveClass(/sidebar--mobile-open/)
  for (const [button, title] of [['任务', '任务中心'], ['通知', '消息中心']]) {
    await page.getByRole('button', { name: button, exact: true }).click()
    await expect(page.locator('.el-drawer:visible')).toContainText(title!)
    await page.keyboard.press('Escape'); await expect(page.locator('.el-drawer:visible')).toHaveCount(0)
  }
  await page.mouse.wheel(0, 500)
  await expect(page.locator('[data-test=topbar]')).toHaveClass(/topbar--scrolled/)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
