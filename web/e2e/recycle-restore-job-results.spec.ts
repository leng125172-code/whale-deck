import { test, expect } from '@playwright/test'
import { createHash } from 'node:crypto'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
function execution(id: string) {
  const bytes = createHash('sha256')
    .update('whaledeck-container-recycle-restore-execution-id-v1:' + id.replace(/-/g, ''))
    .digest()
  const h = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((i) => bytes[i]!.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme} historical independent restore facts and safe fallback`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      const writes: string[] = []
      const errors: string[] = []
      page.on('pageerror', (e) => errors.push(e.message))
      const cases = [
        'Succeeded',
        'Unknown',
        'Acknowledged',
        'Ready',
        'Restored',
        'Empty',
        'Corrupt',
      ]
      const sourceId = '491a9ea0-e736-4f2c-a090-1cce1138fbb4'
      const sourceBytes = createHash('sha256')
        .update('whaledeck-container-recycle-execution-id-v1:' + sourceId.replace(/-/g, ''))
        .digest()
      const sourceHex = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
        .map((i) => sourceBytes[i]!.toString(16).padStart(2, '0'))
        .join('')
      const sourceExecution = `${sourceHex.slice(0, 8)}-${sourceHex.slice(8, 12)}-${sourceHex.slice(12, 16)}-${sourceHex.slice(16, 20)}-${sourceHex.slice(20)}`
      const jobs = cases.map((state, i) => {
        const intentId = `291a9ea0-e736-4f2c-a090-1cce1138fbb${i}`
        const id = state === 'Ready' ? intentId : execution(intentId)
        const unknown = ['Unknown', 'Acknowledged'].includes(state)
        const facts = {
          schemaVersion: 1,
          intentId,
          jobId: id,
          sourceIntentId: sourceId,
          sourceOperationId: sourceExecution,
          stage: state === 'Ready' ? 'planning' : 'execution',
          state: state === 'Restored' ? 'Superseded' : state,
          code:
            'CONTAINER_RECYCLE_RESTORE_' +
            ({
              Succeeded: 'COMPLETED',
              Unknown: 'RESULT_UNKNOWN',
              Acknowledged: 'UNKNOWN_ACKNOWLEDGED',
              Ready: 'READY',
              Restored: 'TARGET_CHANGED',
            }[state] ?? 'COMPLETED'),
          receiptState:
            state === 'Ready'
              ? null
              : unknown
                ? 'Unknown'
                : state === 'Restored'
                  ? 'Completed'
                  : 'Completed',
          bindingCurrent: state === 'Ready' ? null : !unknown && state !== 'Restored',
          sourceState: state === 'Ready' ? null : 'Restored',
          restoredByThisOperation: state === 'Ready' || unknown ? null : true,
          sourceRetained: state === 'Ready' || unknown ? null : false,
          sourceRestored: state === 'Ready' || unknown ? null : true,
          historyVerified: state !== 'Ready',
          historyAvailable: state === 'Ready' ? null : true,
          publishBarrier: state === 'Unknown',
        }
        return {
          id,
          jobType:
            state === 'Ready'
              ? 'containers.files.recycle-restore-plan'
              : 'containers.files.recycle-restore',
          state:
            state === 'Unknown'
              ? 'Running'
              : ['Acknowledged', 'Restored'].includes(state)
                ? 'Failed'
                : 'Succeeded',
          phase: state,
          progressPercent: null,
          errorCode: null,
          createdAtUtc: '2026-11-05T01:00:00Z',
          completedAtUtc: state === 'Unknown' ? null : '2026-11-05T01:01:00Z',
          resultJson:
            state === 'Empty'
              ? null
              : JSON.stringify(
                  state === 'Corrupt' ? { ...facts, privatePath: '/must-not-render' } : facts,
                ),
        }
      })
      await page.route('**/api/v1/**', async (route) => {
        const r = route.request(),
          path = new URL(r.url()).pathname.replace('/api/v1/', '')
        if (r.method() !== 'GET') writes.push(path)
        if (path.endsWith('/events'))
          return route.fulfill({ contentType: 'text/event-stream', body: ': fixture\n\n' })
        if (path === 'auth/me')
          return route.fulfill({
            json: {
              subject: 'preview-admin',
              name: 'Preview',
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
          region = drawer.getByRole('region', { name: '独立恢复安全结果', exact: true })
        await expect(region).toBeVisible()
        if (i >= 5) await expect(region).toContainText('没有可核实')
        else if (i === 1 || i === 2) {
          await expect(region).toContainText('未证明')
          await expect(region).toContainText('解除保护不证明回收或恢复成功')
        } else if (i === 3) await expect(region).toContainText('尚未恢复文件')
        else if (i === 4) await expect(region).toContainText('当前原件状态已变化')
        else await expect(region).toContainText('文件恢复已完成')
        await expect(drawer).not.toContainText('/must-not-render')
        await expect(
          drawer.getByRole('button', { name: '重新预检并重试', exact: true }),
        ).toHaveCount(0)
        if (i === 2)
          await page.screenshot({
            path: `../deploy/artifacts/ui-candidates/recycle-restore-summary/${width}-${colorScheme}.png`,
          })
        await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click()
      }
      expect(writes).toEqual([])
      expect(errors).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
    })
