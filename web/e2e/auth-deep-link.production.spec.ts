import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

// These cases deliberately require the production bundle: Vite's explicit
// DEV preview identity must not be mistaken for real initial authentication.
test.describe('production initial authentication', () => {
  test.skip(!process.env.WHALEDECK_PRODUCTION_PREVIEW_URL, 'Requires a built production preview')
  test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL })
  for (const authenticationStatus of [200, 401]) {
    test(`container terminal direct authorization: ${authenticationStatus}`, async ({ page }) => {
      const control = await installTerminalFixture(page, {
        administrator: false,
        authenticationStatus,
        authenticationDelayMs: 350,
      })
      await page.goto('/containers/' + 'a'.repeat(64) + '/terminal')
      await expect(page).toHaveURL(authenticationStatus === 401 ? /\/login\?/ : /\/forbidden$/)
      await expect(page.locator('.terminal-surface')).toHaveCount(0)
      expect(control.state.inspections).toBe(0)
      expect(control.state.writes).toHaveLength(0)
      expect(control.state.sockets).toBe(0)
    })
  }

  test('ordinary direct terminal navigation waits for identity then shows 403 without loading the terminal', async ({
    page,
  }) => {
    const control = await installTerminalFixture(page, {
      administrator: false,
      authenticationDelayMs: 350,
    })
    await page.goto('/host/terminal')
    await expect(page.locator('.terminal-surface')).toHaveCount(0)
    await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
    await expect(page).toHaveURL(/\/forbidden$/)
    expect(control.state.authReads).toBe(1)
    expect(control.state.writes).toHaveLength(0)
    expect(control.state.sockets).toBe(0)
  })

  test('anonymous direct navigation redirects to login with the original return path', async ({
    page,
  }) => {
    const control = await installTerminalFixture(page, { authenticationStatus: 401 })
    await page.goto('/host/terminal')
    await expect(
      page.getByRole('button', { name: '使用用户名和密码登录', exact: true }),
    ).toBeVisible()
    const target = new URL(page.url())
    expect(target.pathname).toBe('/login')
    expect(target.searchParams.get('returnUrl')).toBe('/host/terminal')
    await expect(page.locator('.terminal-surface')).toHaveCount(0)
    expect(control.state.authReads).toBe(1)
  })

  test('administrator direct navigation makes one identity request and never auto-connects', async ({
    page,
  }) => {
    const control = await installTerminalFixture(page, { authenticationDelayMs: 350 })
    await page.goto('/host/terminal')
    await expect(page.getByRole('heading', { name: '宿主终端', exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '开始连接', exact: true })).toBeVisible()
    expect(control.state.authReads).toBe(1)
    expect(control.state.writes).toHaveLength(0)
    expect(control.state.sockets).toBe(0)
  })
})
