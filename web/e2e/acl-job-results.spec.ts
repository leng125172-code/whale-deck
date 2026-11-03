import { test, expect } from '@playwright/test'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: ACL historical summary and legacy empty results`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      const errors: string[] = [],
        writes: string[] = []
      page.on('pageerror', (e) => errors.push(e.message))
      const facts = {
        schemaVersion: 1,
        intentId: '6b2601c4-e509-34c0-27a8-56114f72780a',
        stage: 'execution',
        state: 'Succeeded',
        code: 'CONTAINER_ACL_COMPLETED',
        aclType: 'access',
        kind: 'File',
        targetMode: 0,
        noChange: false,
        aclConfirmed: true,
        receiptState: 'Completed',
        bindingCurrent: true,
        historyMayExist: true,
        historyVerified: true,
        hasPublishedVersion: true,
      }
      const make = (
        id: string,
        state: string,
        result: unknown,
        jobType = 'containers.files.acl',
      ) => ({
        id,
        jobType,
        state,
        phase:
          state === 'Running'
            ? 'AwaitingContainerAclReceipt'
            : id === 'execution-acknowledged'
              ? 'Acknowledged'
              : state,
        progressPercent: state === 'Succeeded' ? 100 : null,
        errorCode:
          id === 'execution-acknowledged'
            ? 'CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED'
            : state === 'Running'
              ? 'CONTAINER_ACL_RESULT_UNKNOWN'
              : null,
        createdAtUtc: '2026-11-03T01:00:00Z',
        completedAtUtc: state === 'Running' ? null : '2026-11-03T01:01:00Z',
        resultJson: result === null ? null : JSON.stringify(result),
      })
      const jobs = [
        make('execution-completed', 'Succeeded', facts),
        make('execution-unknown', 'Running', {
          ...facts,
          state: 'Unknown',
          code: 'CONTAINER_ACL_RESULT_UNKNOWN',
          aclConfirmed: null,
          receiptState: 'Unknown',
          bindingCurrent: false,
          hasPublishedVersion: false,
        }),
        make('execution-acknowledged', 'Failed', {
          ...facts,
          state: 'Acknowledged',
          code: 'CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED',
          aclConfirmed: null,
          receiptState: 'Unknown',
          bindingCurrent: false,
          hasPublishedVersion: false,
        }),
        make(
          'planning-ready',
          'Succeeded',
          {
            ...facts,
            stage: 'planning',
            state: 'Ready',
            code: 'CONTAINER_ACL_READY',
            aclType: 'default',
            kind: 'Directory',
            aclConfirmed: null,
            receiptState: null,
            bindingCurrent: null,
            historyMayExist: false,
            historyVerified: false,
            hasPublishedVersion: false,
          },
          'containers.files.acl-plan',
        ),
        make('legacy-completed', 'Succeeded', null),
        make('legacy-failed', 'Failed', null, 'containers.stop'),
        make('invalid-result', 'Succeeded', { ...facts, path: '/private-do-not-render' }),
        make(
          'planning-failed',
          'Failed',
          {
            ...facts,
            stage: 'planning',
            state: 'Failed',
            code: 'CONTAINER_ACL_VERSION_CONFLICT',
            kind: null,
            targetMode: null,
            noChange: null,
            aclConfirmed: null,
            receiptState: null,
            bindingCurrent: null,
            historyMayExist: false,
            historyVerified: false,
            hasPublishedVersion: false,
          },
          'containers.files.acl-plan',
        ),
        make('execution-failed', 'Failed', {
          ...facts,
          state: 'Failed',
          code: 'CONTAINER_ACL_INTERRUPTED',
          aclConfirmed: false,
          receiptState: 'Aborted',
          bindingCurrent: false,
          hasPublishedVersion: false,
        }),
        make('execution-superseded', 'Failed', {
          ...facts,
          state: 'Superseded',
          code: 'CONTAINER_ACL_TARGET_CHANGED',
          bindingCurrent: false,
        }),
      ]
      await page.route('**/api/v1/**', async (route) => {
        const request = route.request(),
          path = new URL(request.url()).pathname.replace('/api/v1/', '')
        if (request.method() !== 'GET') writes.push(path)
        if (path.endsWith('/events'))
          return route.fulfill({
            contentType: 'text/event-stream',
            body: ': no fixture events\n\n',
          })
        if (path === 'auth/me')
          return route.fulfill({
            json: {
              subject: 'preview-user',
              name: 'Preview User',
              groups: [],
              isAdministrator: true,
              permissionExpiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
            },
          })
        const job = jobs.find((j) => path === `jobs/${j.id}`)
        await route.fulfill({
          json:
            path === 'jobs'
              ? jobs
              : (job ?? (path.startsWith('notifications') ? { items: [], unreadCount: 0 } : [])),
        })
      })
      await page.goto('/jobs')
      for (let i = 0; i < jobs.length; i++) {
        await page
          .locator('.jobs-page .el-table__body tr')
          .nth(i)
          .getByRole('button', { name: '详情', exact: true })
          .click()
        const drawer = page.getByRole('dialog', { name: '任务详情', exact: true })
        if (i === 5)
          await expect(drawer.locator('.job-result')).toHaveText(
            '任务已结束，未记录可展示的安全结果。',
          )
        else {
          const region = drawer.getByRole('region', { name: 'ACL 安全结果', exact: true })
          const expected = [
            'ACL 修改已完成',
            '结果未知，仍保留写入保护',
            '管理员已明确处置未知结果',
            '预检完成，尚未修改 ACL',
            '此任务未记录可展示的 ACL 安全摘要，请结合任务状态与原文件任务核对。',
            '',
            '此任务未记录可展示的 ACL 安全摘要，请结合任务状态与原文件任务核对。',
            '预检失败，未进入 ACL 修改执行',
            '执行失败',
            '已有完成回执，实例绑定已变化',
          ][i]!
          await expect(region).toContainText(expected)
          await region.getByText(expected, { exact: true }).scrollIntoViewIfNeeded()
          await expect(region.getByText(expected, { exact: true })).toBeInViewport({ ratio: 1 })
          if (i === 0) await expect(region).toContainText('0000')
          if (i === 2)
            await expect(region).toContainText('处置仅解除业务保护，不证明 ACL 已恢复或重新执行。')
          if (i === 7) {
            await expect(region).not.toContainText('执行失败')
            for (const label of ['目标对象', '目标普通模式', '预检无需变更', '回执对象绑定有效'])
              await expect(
                region.getByText(label, { exact: true }).locator('xpath=following-sibling::dd[1]'),
              ).toHaveText('未证明')
            await expect(region).toContainText('未取得')
          }
          if (i === 8 || i === 9) {
            await expect(
              region
                .getByText('ACL 已确认', { exact: true })
                .locator('xpath=following-sibling::dd[1]'),
            ).toHaveText(i === 8 ? '否' : '是')
            await expect(region).toContainText(i === 8 ? 'Aborted' : 'Completed')
          }
          if (i === 1 || i === 2 || i === 7) {
            const value = region
              .getByText('ACL 已确认', { exact: true })
              .locator('xpath=following-sibling::dd[1]')
            await expect(value).toHaveText('未证明')
            await value.evaluate((el) => el.scrollIntoView({ block: 'center', inline: 'nearest' }))
            await expect(value).toBeInViewport({ ratio: 1 })
            const body = await drawer.locator('.el-drawer__body').boundingBox()
            expect(body).not.toBeNull()
            for (const box of await region.locator('dd').evaluateAll((elements) =>
              elements.map((el) => {
                const b = el.getBoundingClientRect()
                return { left: b.left, right: b.right }
              }),
            )) {
              expect(box.left).toBeGreaterThanOrEqual(body!.x)
              expect(box.right).toBeLessThanOrEqual(body!.x + body!.width)
            }
          }
        }
        await expect(drawer).not.toContainText('任务成功后')
        if (i === 1)
          await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0)
        if (i !== 5)
          await expect(
            drawer.getByRole('button', { name: '重新预检并重试', exact: true }),
          ).toHaveCount(0)
        await expect(drawer).not.toContainText('/private-do-not-render')
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
          true,
        )
        if (i < 4 || i >= 7)
          await page.screenshot({
            path: `../deploy/artifacts/ui-candidates/acl-job-summary/acl-job-result-${i}-${width}-${colorScheme}.png`,
            scale: 'css',
          })
        await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click()
        await expect(drawer).not.toBeVisible()
      }
      expect(writes).toEqual([])
      expect(errors).toEqual([])
    })
  }
