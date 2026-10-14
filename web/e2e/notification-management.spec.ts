import { expect, test } from '@playwright/test'

const channel = {
  id: '59a29b72-dfbb-48c9-9323-c1688eaa3d71',
  name: 'Ops channel',
  provider: 'Webhook',
  destinationReference: 'approved-target',
  hasCredential: true,
  isEnabled: false,
  version: 0,
  createdAtUtc: '2026-10-14T01:00:00Z',
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: notification configuration and frozen retries`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      const writes: { key: string; body: string }[] = []
      let hasDelivery = false
      await page.route('**/api/v1/**', (route) => {
        const request = route.request()
        const path = new URL(request.url()).pathname.replace('/api/v1/', '')
        if (path === 'auth/csrf')
          return route.fulfill({ json: { headerName: 'X-CSRF', token: 'fixture-only' } })
        if (path.startsWith('notifications/manage/channels') && request.method() === 'PUT') {
          writes.push({
            key: request.headers()['idempotency-key'] || '',
            body: request.postData() || '',
          })
          if (writes.length === 1) return route.abort('failed')
          return route.fulfill({ json: { ...channel, name: 'Changed', version: 1 } })
        }
        if (path === 'notifications/manage/channels')
          return route.fulfill({ json: { items: [channel], hasMore: false, page: 1, take: 25 } })
        if (path === 'notifications/manage/routes')
          return route.fulfill({ json: { items: [], hasMore: false, page: 1, take: 25 } })
        if (path.endsWith('/attempts'))
          return route.fulfill({
            json: [
              {
                attemptNumber: 1,
                startedAtUtc: '2026-10-14T01:00:00Z',
                finishedAtUtc: '2026-10-14T01:00:01Z',
                resultCode: 'Accepted',
              },
            ],
          })
        if (path === 'notifications/manage/deliveries')
          return route.fulfill({
            json: {
              items: hasDelivery
                ? [
                    {
                      id: 'e8e95c77-eaad-4d2c-8a6d-29d00f5606ec',
                      notificationId: channel.id,
                      channelId: channel.id,
                      channelVersion: 0,
                      provider: 'Webhook',
                      state: 'Succeeded',
                      attemptCount: 1,
                      createdAtUtc: '2026-10-14T01:00:00Z',
                      lastAttemptAtUtc: '2026-10-14T01:00:00Z',
                      lastResultCode: 'Accepted',
                    },
                  ]
                : [],
              hasMore: false,
              page: 1,
              take: 25,
            },
          })
        if (path === 'notifications/summary')
          return route.fulfill({ json: { unreadCount: 0, totalCount: 0, snapshotSequence: 0 } })
        return route.fulfill({ json: [] })
      })
      await page.goto('/settings/notifications')
      await expect(page.getByRole('heading', { name: '通知管理' })).toBeVisible()
      await expect(page.getByText('外部发送需要私有配置')).toBeVisible()
      await expect(page.getByText('Ops channel', { exact: true })).toBeVisible()
      await page.getByRole('button', { name: '编辑', exact: true }).click()
      const dialog = page.getByRole('dialog', { name: '渠道配置' })
      await dialog.getByLabel('渠道名称').fill('Changed')
      await dialog.getByRole('button', { name: '确认并保存' }).click()
      await page.getByRole('button', { name: '确认保存', exact: true }).click()
      await expect(dialog.getByText('已提交的请求已冻结')).toBeVisible()
      await expect(dialog.getByLabel('渠道名称')).toBeDisabled()
      await dialog.getByRole('button', { name: '重试原请求' }).click()
      await expect(dialog).not.toBeVisible()
      expect(writes).toHaveLength(2)
      expect(writes[0]).toEqual(writes[1])
      expect(writes[0]!.key).toMatch(/^[a-f0-9-]{36}$/)
      expect(JSON.parse(writes[0]!.body)).toMatchObject({
        name: 'Changed',
        keepCredential: true,
        credentialReference: null,
        confirmed: true,
        expectedVersion: 0,
      })
      await page.getByRole('tab', { name: '投递记录', exact: true }).click()
      await expect(page.getByText('暂无投递记录', { exact: true })).toBeVisible()
      hasDelivery = true
      await page.getByRole('button', { name: '重新载入', exact: true }).click()
      await page.getByRole('button', { name: '尝试记录', exact: true }).click()
      const attempts = page.getByRole('dialog', { name: '投递尝试记录', exact: true })
      await expect(attempts.getByText('Accepted', { exact: true })).toBeVisible()
      await attempts.getByRole('button', { name: 'Close this dialog' }).click()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({
        path: `test-results/notification-management-${width}-${colorScheme}.png`,
        scale: 'css',
      })
    })
