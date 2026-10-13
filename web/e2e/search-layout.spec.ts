import { expect, test } from '@playwright/test'

const longTitle = 'database-platform-' + 'very-long-resource-name-'.repeat(12)
const longSubtitle = 'registry.example.invalid/' + 'long-image-path/'.repeat(16)

for (const colorScheme of ['light', 'dark'] as const) {
  test(`${colorScheme}: search stays compact and long results are ellipsized`, async ({ page }) => {
    await page.emulateMedia({ colorScheme })
    await page.addInitScript(() =>
      sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
    )
    await page.route('**/api/v1/**', (route) => {
      const endpoint = new URL(route.request().url()).pathname.replace('/api/v1/', '')
      return route.fulfill({
        json:
          endpoint === 'search'
            ? Array.from({ length: 30 }, (_, index) => ({
                id: `search-${index}`,
                kind: 'Container',
                title: `${longTitle}${index}`,
                subtitle: longSubtitle,
                targetUrl: '/containers',
                external: false,
              }))
            : [],
      })
    })
    await page.goto('/host/logs')
    await expect(page.locator('.app-shell')).toBeVisible()
    const search = page.getByRole('button', { name: '搜索平台功能', exact: true })
    for (const width of [1920, 1046, 1045, 760, 390, 320]) {
      await page.setViewportSize({ width, height: 900 })
      await expect(search).toBeVisible()
      const bounds = await search.boundingBox()
      expect(bounds!.width).toBeLessThanOrEqual(169)
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width)
      await search.click()
      const dialog = page.getByRole('dialog')
      await expect(dialog).toBeVisible()
      await dialog.getByPlaceholder('搜索功能或资源').fill('very-long')
      const results = dialog.locator('.global-search-results')
      await expect(results.locator('button')).toHaveCount(30)
      const title = results.locator('strong').first()
      await expect(title).toHaveAttribute('title', `${longTitle}0`)
      await expect(results.locator('small').first()).toHaveAttribute('title', longSubtitle)
      expect(
        await title.evaluate((el) => ({
          ellipsis: getComputedStyle(el).textOverflow,
          nowrap: getComputedStyle(el).whiteSpace,
          truncated: el.scrollWidth > el.clientWidth,
        })),
      ).toEqual({ ellipsis: 'ellipsis', nowrap: 'nowrap', truncated: true })
      expect(
        await results.evaluate((el) => ({
          scrollable: el.scrollHeight > el.clientHeight,
          fits: el.scrollWidth <= el.clientWidth + 1,
          height: el.clientHeight,
        })),
      ).toEqual({ scrollable: true, fits: true, height: 420 })
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1,
        ),
      ).toBe(true)
      if (width === 1920 || width === 320)
        await page.screenshot({
          path: `test-results/search-${width}-${colorScheme}.png`,
          scale: 'css',
        })
      await dialog.getByRole('button', { name: 'Close this dialog' }).click()
      await expect(dialog).not.toBeVisible()
    }
    // A short window still exposes the search field and scrolls inside the results.
    await page.setViewportSize({ width: 760, height: 400 })
    await page.keyboard.press('Control+k')
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await dialog.getByPlaceholder('搜索功能或资源').fill('very-long')
    await expect(dialog.locator('.global-search-results button')).toHaveCount(30)
    const bounds = await dialog.boundingBox()
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(400)
    const results = dialog.locator('.global-search-results')
    await results.hover()
    await page.mouse.wheel(0, 2000)
    await expect.poll(() => results.evaluate((el) => el.scrollTop)).toBeGreaterThan(0)
    await results.locator('button').last().scrollIntoViewIfNeeded()
    await results.locator('button').last().click()
    await expect(page).toHaveURL('/containers')
    await expect(dialog).not.toBeVisible()
  })
}
