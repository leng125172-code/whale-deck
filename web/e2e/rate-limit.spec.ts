import { expect, test } from '@playwright/test'

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: rate rejection preserves inbox and never repeats a write`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      let writes = 0
      await page.route('**/api/v1/**', (route) => {
        const path = new URL(route.request().url()).pathname.replace('/api/v1/', '')
        if (path === 'auth/csrf')
          return route.fulfill({ json: { headerName: 'X-CSRF-TOKEN', token: 'fixture-only' } })
        if (path === 'notifications/summary')
          return route.fulfill({ json: { unreadCount: 1, totalCount: 1, snapshotSequence: 1 } })
        if (path === 'notifications')
          return route.fulfill({
            json: {
              items: [
                {
                  id: 'owned',
                  sequence: 1,
                  sourceKind: 'Job',
                  messageCode: 'JOB_SUCCEEDED',
                  severity: 'Info',
                  title: '本人消息',
                  summary: '限流时保留已加载内容',
                  occurredAtUtc: new Date().toISOString(),
                  publishedAtUtc: new Date().toISOString(),
                  readAtUtc: null,
                  targetPath: null,
                },
              ],
              nextBefore: null,
              snapshotSequence: 1,
            },
          })
        if (path === 'notifications/read-all-snapshot')
          return route.fulfill({
            json: {
              snapshotId: '00000000-0000-0000-0000-000000000001',
              itemCount: 1,
              expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
            },
          })
        if (path === 'notifications/read-all') {
          writes++
          return route.fulfill({
            status: 429,
            headers: { 'Retry-After': '60' },
            json: { code: 'API_RATE_LIMITED', title: 'budget' },
          })
        }
        return route.fulfill({ json: [] })
      })
      await page.goto('/host/logs')
      await page.getByRole('button', { name: '通知', exact: true }).click()
      const drawer = page.getByRole('dialog', { name: '消息中心', exact: true })
      await expect(drawer.getByText('本人消息', { exact: true })).toBeVisible()
      await drawer.getByRole('button', { name: '全部已读', exact: true }).click()
      await expect(drawer.getByRole('alert')).toContainText('60 秒后再试')
      await expect(drawer.getByRole('alert')).toContainText('批次会保留')
      await expect(drawer.getByText('本人消息', { exact: true })).toBeVisible()
      expect(writes).toBe(1)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({
        path: `test-results/rate-limit-${width}-${colorScheme}.png`,
        scale: 'css',
      })
    })
