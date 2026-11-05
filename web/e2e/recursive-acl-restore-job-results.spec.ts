import { test, expect } from '@playwright/test'
import { result } from '../src/__tests__/fixtures/recursiveAclRestoreJobResult'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: restore task historical results and unproved receipts`, async ({
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
      const partial = {
        ...result,
        state: 'Partial',
        code: 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT',
        receiptState: 'Partial',
        bindingCurrent: false,
        completedCount: 1,
        changedCount: 1,
        notStartedCount: 3,
        publishBarrier: true,
      }
      const unknown = {
        ...result,
        state: 'Unknown',
        code: 'CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID',
        receiptState: null,
        bindingCurrent: null,
        completedCount: null,
        changedCount: null,
        abortedCount: null,
        notStartedCount: null,
        unknownCount: null,
        historyAvailable: null,
        historyVerified: false,
        publishBarrier: true,
      }
      const ready = {
        ...result,
        stage: 'planning',
        state: 'Ready',
        code: 'CONTAINER_RECURSIVE_ACL_RESTORE_READY',
        receiptState: null,
        bindingCurrent: null,
        completedCount: null,
        changedCount: null,
        abortedCount: null,
        notStartedCount: null,
        unknownCount: null,
        historyAvailable: null,
        historyMayExist: false,
        historyVerified: false,
      }
      const facts = [
        result,
        partial,
        unknown,
        {
          ...partial,
          state: 'Acknowledged',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_ACKNOWLEDGED',
          publishBarrier: false,
        },
        ready,
        null,
        { ...result, path: '/private-never-render' },
        {
          ...ready,
          intentId: result.intentId.slice(0, -1) + '4',
          state: 'Failed',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED',
          aclType: null,
          entryCount: null,
          fileCount: null,
          directoryCount: null,
          changeCount: null,
          sourceEntryCount: null,
          sourceFileCount: null,
          sourceDirectoryCount: null,
          sourceChangedCount: null,
        },
        {
          ...result,
          state: 'Acknowledged',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED',
        },
        {
          ...partial,
          state: 'Acknowledged',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED',
          publishBarrier: false,
        },
        {
          ...result,
          state: 'Acknowledged',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED',
          receiptState: 'Aborted',
          completedCount: 0,
          changedCount: 0,
          notStartedCount: 4,
          bindingCurrent: false,
          historyVerified: false,
          historyAvailable: false,
        },
        {
          ...result,
          state: 'Unknown',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN',
          receiptState: 'Unknown',
          completedCount: 0,
          changedCount: 0,
          unknownCount: 4,
          bindingCurrent: false,
          publishBarrier: true,
        },
        {
          ...result,
          state: 'Acknowledged',
          code: 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED',
          receiptState: 'Unknown',
          completedCount: 0,
          changedCount: 0,
          unknownCount: 4,
          bindingCurrent: false,
          publishBarrier: false,
        },
      ]
      const jobs = facts.map((facts, i) => {
        const id =
          i === 4
            ? result.intentId
            : i === 7
              ? result.intentId.slice(0, -1) + '4'
              : result.jobId.slice(0, -1) + i.toString(16)
        return {
          id,
          jobType:
            i === 4 || i === 7
              ? 'containers.files.recursive-acl-restore-plan'
              : 'containers.files.recursive-acl-restore',
          state: [1, 2, 11].includes(i)
            ? 'Running'
            : i === 3 || i === 7 || i >= 8
              ? 'Failed'
              : 'Succeeded',
          phase:
            i === 1
              ? 'AwaitingContainerRecursiveAclRestoreResolution'
              : i === 2 || i === 11
                ? 'AwaitingContainerRecursiveAclRestoreReceipt'
                : i === 3 || i >= 8
                  ? 'Acknowledged'
                  : 'Succeeded',
          progressPercent: null,
          errorCode: null,
          createdAtUtc: '2026-11-05T01:00:00Z',
          completedAtUtc: [1, 2, 11].includes(i) ? null : '2026-11-05T01:01:00Z',
          resultJson: facts ? JSON.stringify({ ...facts, jobId: id }) : null,
        }
      })
      await page.route('**/api/v1/**', async (route) => {
        const request = route.request(),
          path = new URL(request.url()).pathname.replace('/api/v1/', '')
        if (request.method() !== 'GET') writes.push(path)
        if (path.endsWith('/events'))
          return route.fulfill({ contentType: 'text/event-stream', body: ': fixture only\n\n' })
        if (path === 'auth/me')
          return route.fulfill({
            json: {
              subject: 'preview-admin',
              name: 'Preview Admin',
              groups: [],
              isAdministrator: true,
              permissionExpiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
            },
          })
        return route.fulfill({
          json:
            path === 'jobs'
              ? jobs
              : (jobs.find((v) => path === `jobs/${v.id}`) ??
                (path.startsWith('notifications') ? { items: [], unreadCount: 0 } : [])),
        })
      })
      await page.goto('/jobs')
      for (let i = 0; i < jobs.length; i++) {
        await page
          .locator('.jobs-page .el-table__body tr')
          .nth(i)
          .getByRole('button', { name: '详情', exact: true })
          .click()
        const drawer = page.getByRole('dialog', { name: '任务详情', exact: true }),
          region = drawer.getByRole('region', { name: '递归ACL恢复安全结果', exact: true })
        if (i === 5 || i === 6) {
          await expect(region).toContainText('不将缺失结果视为零修改或成功')
        } else {
          const stateTexts: Record<string, string> = {
            Succeeded: '递归ACL恢复已完成',
            Partial: '部分修改，保护已保留',
            Unknown: '结果未知，保护已保留',
            Acknowledged: '已处置历史结果，不代表整树修改成功',
            Ready: '预检已完成，尚未恢复ACL',
            Failed: '递归ACL恢复未完成',
          }
          await expect(region).toContainText(stateTexts[facts[i]!.state]!)
          const actual = region.locator('[aria-label="本次恢复实际结果"]')
          await expect(actual.locator('dd')).toHaveText(
            i === 0 || i === 8
              ? ['4', '4', '0', '0', '0']
              : i === 1 || i === 3 || i === 9
                ? ['1', '1', '0', '3', '0']
                : i >= 11
                  ? ['0', '0', '0', '0', '4']
                  : i === 10
                    ? ['0', '0', '0', '4', '0']
                    : Array(5).fill('未证明'),
          )
          await expect(region.locator('[aria-label="恢复来源原计数"]')).toContainText(
            i === 7 ? '来源原修改未证明' : '来源原修改2',
          )
          if (i === 3 || (i >= 8 && i !== 11)) {
            await expect(region).toContainText('不代表整树恢复成功')
            await expect(region).not.toContainText('递归ACL恢复已完成')
          }
          if ([2, 11, 12].includes(i)) {
            const note = region.getByRole('note', { name: '未知回执计数限制', exact: true })
            await expect(note).toContainText('0不代表未发生修改')
            await expect(actual).toContainText('回执已证明修改')
            await expect(actual).not.toContainText('实际修改0')
            await note.scrollIntoViewIfNeeded()
            await expect(note).toBeInViewport()
            await page.screenshot({
              path: `${process.env.WHALEDECK_JOB_RESULT_SCREENSHOTS || '../deploy/artifacts/ui-candidates/recursive-acl-restore-job-results'}/warning-${i}-${width}-${colorScheme}.png`,
              scale: 'css',
            })
          }
          await actual.scrollIntoViewIfNeeded()
          await page.screenshot({
            path: `${process.env.WHALEDECK_JOB_RESULT_SCREENSHOTS || '../deploy/artifacts/ui-candidates/recursive-acl-restore-job-results'}/result-${i}-${width}-${colorScheme}.png`,
            scale: 'css',
          })
          const body = await drawer.locator('.el-drawer__body').boundingBox()
          expect(body).not.toBeNull()
          for (const box of await region.locator('dd').evaluateAll((nodes) =>
            nodes.map((n) => {
              const r = n.getBoundingClientRect()
              return { left: r.left, right: r.right }
            }),
          )) {
            expect(box.left).toBeGreaterThanOrEqual(body!.x)
            expect(box.right).toBeLessThanOrEqual(body!.x + body!.width)
          }
        }
        await expect(drawer).not.toContainText('/private-never-render')
        await expect(drawer.locator('pre.job-result')).toHaveCount(0)
        await expect(
          drawer.getByRole('button', { name: '重新预检并重试', exact: true }),
        ).toHaveCount(0)
        if (i === 1 || i === 2)
          await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0)
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
          true,
        )
        await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click()
        await expect(drawer).not.toBeVisible()
      }
      expect(writes).toEqual([])
      expect(errors).toEqual([])
    })
  }
