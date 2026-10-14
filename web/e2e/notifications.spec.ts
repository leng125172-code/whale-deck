import { expect, test } from '@playwright/test'

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: inbox reads, filters, pagination and errors`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      const now = new Date().toISOString()
      let read = false
      let readAll = false
      let fail = false
      let batchCount = 0
      const snapshotId = '00000000-0000-0000-0000-000000000001'
      const writes: { path: string; body: unknown; csrf: string | undefined }[] = []
      await page.route('**/api/v1/**', (route) => {
        const url = new URL(route.request().url())
        const path = url.pathname.replace('/api/v1/', '')
        if (path === 'auth/csrf')
          return route.fulfill({ json: { headerName: 'X-CSRF-TOKEN', token: 'browser-test-only' } })
        if (path === 'notifications/summary')
          return route.fulfill({
            json: { unreadCount: readAll ? 0 : read ? 1 : 2, totalCount: 2, snapshotSequence: 10 },
          })
        if (route.request().method() === 'POST' && path.startsWith('notifications/')) {
          writes.push({
            path,
            body: route.request().postData() ? route.request().postDataJSON() : null,
            csrf: route.request().headers()['x-csrf-token'],
          })
          if (path === 'notifications/read-all-snapshot')
            return route.fulfill({
              json: {
                snapshotId,
                itemCount: 2,
                expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
              },
            })
          if (path === 'notifications/read-all') {
            batchCount++
            readAll = batchCount === 2
            return route.fulfill({
              json: { changedCount: 1, processedCount: 1, hasMore: !readAll },
            })
          }
          read = route.request().postDataJSON().isRead
          return route.fulfill({
            json: { id: 'first', isRead: read, readAtUtc: read ? now : null },
          })
        }
        if (path === 'notifications') {
          if (fail) return route.fulfill({ status: 503, json: { title: 'Dependency unavailable' } })
          const more = url.searchParams.has('before')
          const unread = url.searchParams.get('status') === 'unread'
          const items =
            unread && readAll
              ? []
              : [
                  {
                    id: more ? 'second' : 'first',
                    sequence: more ? 9 : 10,
                    sourceKind: 'Job',
                    messageCode: 'JOB_SUCCEEDED',
                    severity: 'Info',
                    title: more ? '较早任务' : '任务已完成',
                    summary: '验证长文本不会导致消息抽屉横向溢出。'.repeat(8),
                    occurredAtUtc: now,
                    publishedAtUtc: now,
                    readAtUtc: readAll || read ? now : null,
                    targetPath: null,
                  },
                ]
          return route.fulfill({
            json: { items, nextBefore: more || unread ? null : 10, snapshotSequence: 10 },
          })
        }
        return route.fulfill({ json: [] })
      })
      await page.goto('/host/logs')
      const bell = page.getByRole('button', { name: '通知', exact: true })
      await expect(bell.locator('.el-badge__content')).toHaveText('2')
      await bell.click()
      const drawer = page.getByRole('dialog', { name: '消息中心', exact: true })
      await expect(drawer.locator('[data-test="inbox-message"]')).toHaveCount(1)
      await drawer.getByRole('button', { name: '加载更多', exact: true }).click()
      await expect(drawer.locator('[data-test="inbox-message"]')).toHaveCount(2)
      await drawer.getByRole('button', { name: '标为已读', exact: true }).first().click()
      await expect(bell.locator('.el-badge__content')).toHaveText('1')
      await expect(drawer.getByRole('button', { name: '标为未读', exact: true })).toBeVisible()
      await drawer.getByRole('button', { name: '全部已读', exact: true }).click()
      await expect(bell.locator('.el-badge__content')).toBeHidden()
      expect(batchCount).toBe(2)
      expect(writes.every((write) => write.csrf === 'browser-test-only')).toBe(true)
      expect(
        writes.filter((write) => write.path.endsWith('read-all')).map((write) => write.body),
      ).toEqual([{ snapshotId }, { snapshotId }])
      expect(writes.filter((write) => write.path.endsWith('read-all-snapshot'))).toHaveLength(1)
      await drawer.getByText('未读', { exact: true }).click()
      await expect(drawer.getByText('没有未读消息', { exact: true })).toBeVisible()
      await drawer.getByText('全部', { exact: true }).click()
      await expect(drawer.getByText('任务已完成', { exact: true })).toBeVisible()
      await page.screenshot({
        path: `test-results/notifications-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      const bounds = await drawer.boundingBox()
      expect(bounds!.x).toBeGreaterThanOrEqual(0)
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1)
      fail = true
      await drawer.getByRole('button', { name: '加载更多', exact: true }).click()
      await expect(drawer.getByRole('alert')).toContainText('消息服务暂时不可用')
      await expect(drawer.getByText('暂无消息', { exact: true })).not.toBeVisible()
      fail = false
      await drawer.getByRole('button', { name: '重试', exact: true }).click()
      await expect(drawer.getByRole('alert')).not.toBeVisible()
      await drawer.getByRole('button', { name: 'Close this dialog' }).click()
      await expect(drawer).not.toBeVisible()
    })
