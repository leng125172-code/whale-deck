import { expect, test, type Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'
import { installTerminalFixture } from './fixtures/terminal.mjs'

test.describe('production expired session UI', () => {
  test.skip(!process.env.WHALEDECK_PRODUCTION_PREVIEW_URL, 'Requires the production bundle')
  test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL })

  async function openFiles(page: Page) {
    await page.goto('/containers')
    await page
      .locator('.container-card')
      .filter({ hasText: 'demo-web' })
      .getByRole('button', { name: '详情', exact: true })
      .click()
    await page.getByRole('button', { name: '容器文件', exact: true }).click()
    const panel = page.getByRole('region', { name: '容器文件', exact: true })
    await expect(panel.getByRole('listitem')).toHaveCount(4)
    return panel
  }

  test('a BFF 401 removes private controls and redirects with the original return path', async ({
    page,
  }) => {
    const fixture = await installContainerFilesFixture(page)
    const panel = await openFiles(page)
    fixture.state.entriesStatus = 401
    await panel.getByRole('button', { name: '刷新', exact: true }).click()
    await expect(page).toHaveURL(/\/login\?returnUrl=\/containers$/)
    await expect(
      page.getByRole('button', { name: '使用用户名和密码登录', exact: true }),
    ).toBeVisible()
    await expect(panel).toHaveCount(0)
    await expect(page.getByRole('button', { name: '创建容器', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: '打开用户菜单', exact: true })).toHaveCount(0)
  })

  test('an Agent 503 keeps the authenticated page and allows retry', async ({ page }) => {
    const fixture = await installContainerFilesFixture(page)
    const panel = await openFiles(page)
    fixture.state.entriesStatus = 503
    await panel.getByRole('button', { name: '刷新', exact: true }).click()
    await expect(panel.getByText('Agent 暂不可用，请稍后重试', { exact: true })).toBeVisible()
    await expect(page).toHaveURL(/\/containers$/)
    fixture.state.entriesStatus = 200
    await panel.getByRole('button', { name: '刷新', exact: true }).click()
    await expect(panel.getByRole('listitem')).toHaveCount(4)
  })

  for (const width of [320, 1440])
    test(`${width}: another tab normal logout cancels the file UI without waiting for polling`, async ({
      page,
      context,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme: width === 320 ? 'dark' : 'light' })
      const fixture = await installContainerFilesFixture(page)
      let signedOut = false
      const auth = async (tab: Page) => {
        await tab.route('**/api/v1/auth/**', async (route) => {
          const pathname = new URL(route.request().url()).pathname
          if (pathname.endsWith('/csrf'))
            return route.fulfill({
              json: {
                headerName: 'X-XSRF-TOKEN',
                formFieldName: '__RequestVerificationToken',
                token: 'fixture-only',
              },
            })
          if (pathname.endsWith('/logout') && route.request().method() === 'POST') {
            signedOut = true
            return route.fulfill({ status: 302, headers: { Location: '/login' }, body: '' })
          }
          if (pathname.endsWith('/me') && signedOut) return route.fulfill({ status: 401, json: {} })
          return route.fallback()
        })
      }
      await auth(page)
      const panel = await openFiles(page)
      const other = await context.newPage()
      await other.setViewportSize({ width: 1440, height: 900 })
      await installTerminalFixture(other)
      await auth(other)
      await other.goto('/')
      await other.getByRole('button', { name: '打开用户菜单', exact: true }).click()
      fixture.state.pauseDownload = true
      await panel.getByRole('button', { name: '资料', exact: true }).click()
      await panel.getByRole('button', { name: '下载 中文.bin', exact: true }).click()
      await expect.poll(() => fixture.state.downloadWaiting).toBe(true)
      await other.getByRole('menuitem', { name: '退出登录', exact: true }).click()
      await expect(page).toHaveURL(/\/login\?returnUrl=\/containers$/)
      await expect(other).toHaveURL(/\/login$/)
      await expect(panel).toHaveCount(0)
      await expect(page.getByRole('button', { name: '创建容器', exact: true })).toHaveCount(0)
      await expect(page.getByText('文件已完整校验，已发起浏览器保存', { exact: true })).toHaveCount(
        0,
      )
      fixture.state.releaseDownload()
      await expect(
        page.getByRole('button', { name: '使用用户名和密码登录', exact: true }),
      ).toBeVisible()
      await other.close()
    })
})
