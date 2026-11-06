import { test, expect } from '@playwright/test'
import { createHash } from 'node:crypto'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
function execution(id: string) {
  const bytes = createHash('sha256')
    .update('whaledeck-container-recycle-purge-execution-id-v1:' + id.replace(/-/g, ''))
    .digest()
  const h = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
    .map((i) => bytes[i]!.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme} historical retained-original purge facts and safe fallback`, async ({
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
        'Reserving',
        'Reserved',
        'Applying',
        'Canceled',
        'Failed',
        'Expired',
        'PlanFailed',
        'NoReceipt',
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
      const jobs = cases.map((value, i) => {
        const intentId = `291a9ea0-e736-4f2c-a090-1cce1138fbb${i.toString(16)}`
        const planning = ['Ready', 'Expired', 'PlanFailed'].includes(value)
        const state =
          value === 'Reserved'
            ? 'Reserving'
            : value === 'PlanFailed'
              ? 'Failed'
              : value === 'NoReceipt'
                ? 'Unknown'
                : ['Empty', 'Corrupt'].includes(value)
                  ? 'Succeeded'
                  : value
        const id = planning ? intentId : execution(intentId)
        const noReply = [
          'Ready',
          'Expired',
          'PlanFailed',
          'Reserving',
          'Reserved',
          'Applying',
          'NoReceipt',
        ].includes(value)
        const aborted = ['Canceled', 'Failed'].includes(value)
        const unknown = ['Unknown', 'Acknowledged', 'NoReceipt'].includes(value)
        const suffix =
          (
            {
              Succeeded: 'COMPLETED',
              Unknown: 'RESULT_UNKNOWN',
              Acknowledged: 'UNKNOWN_ACKNOWLEDGED',
              Ready: 'READY',
              Reserving: 'RESERVING',
              Reserved: 'RESERVED',
              Applying: 'APPLYING',
              Canceled: 'CANCELED',
              Failed: 'INTERRUPTED',
              Expired: 'PLAN_EXPIRED',
              PlanFailed: 'SOURCE_CHANGED',
              NoReceipt: 'RESULT_UNKNOWN',
            } as Record<string, string>
          )[value] ?? 'COMPLETED'
        const facts = {
          schemaVersion: 1,
          intentId,
          jobId: id,
          sourceIntentId: sourceId,
          sourceOperationId: sourceExecution,
          stage: planning ? 'planning' : 'execution',
          state,
          code: 'CONTAINER_RECYCLE_PURGE_' + suffix,
          receiptState: noReply ? null : unknown ? 'Unknown' : aborted ? 'Aborted' : 'Completed',
          reservationStarted: !planning,
          reservationRecorded: !planning && value !== 'Reserving',
          applyStarted: !planning && !['Reserving', 'Reserved'].includes(value),
          purgeApplied: noReply || unknown ? null : !aborted,
          sourceFenced: noReply ? null : !aborted,
          ownedFence: noReply ? null : !aborted,
          publishBarrier: ['Unknown', 'NoReceipt', 'Reserving', 'Reserved', 'Applying'].includes(
            value,
          ),
        }
        return {
          id,
          jobType: planning
            ? 'containers.files.recycle-purge-plan'
            : 'containers.files.recycle-purge',
          state: ['Ready', 'Succeeded'].includes(state)
            ? 'Succeeded'
            : ['Unknown', 'Reserving', 'Applying'].includes(state)
              ? 'Running'
              : state === 'Canceled'
                ? 'Canceled'
                : 'Failed',
          phase: state === 'Unknown' ? 'AwaitingContainerRecyclePurgeReceipt' : state,
          progressPercent: null,
          errorCode: null,
          createdAtUtc: '2026-11-06T01:00:00Z',
          completedAtUtc: ['Unknown', 'Reserving', 'Applying'].includes(state)
            ? null
            : '2026-11-06T01:01:00Z',
          resultJson:
            value === 'Empty'
              ? null
              : JSON.stringify(
                  value === 'Corrupt' ? { ...facts, privatePath: '/must-not-render' } : facts,
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
          region = drawer.getByRole('region', { name: '留存清理安全结果', exact: true })
        await expect(region).toBeVisible()
        const current = cases[i]
        if (['Empty', 'Corrupt'].includes(current!))
          await expect(region).toContainText('没有可核实')
        else if (['Unknown', 'Acknowledged', 'NoReceipt'].includes(current!)) {
          await expect(region).toContainText('未证明')
          await expect(region).toContainText('解除保护不证明清理成功')
        } else if (current === 'Ready') await expect(region).toContainText('尚未删除留存原件')
        else if (['Reserving', 'Reserved'].includes(current!))
          await expect(region).toContainText('尚未进入删除')
        else if (current === 'Applying') await expect(region).toContainText('结果仍待核实')
        else if (current === 'PlanFailed')
          await expect(region).toContainText('预检失败，未进入清理执行')
        else if (current === 'Succeeded') await expect(region).toContainText('留存原件清理已完成')
        if (['Unknown', 'NoReceipt'].includes(current!))
          await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0)
        if (current === 'Reserved')
          await expect(region).toContainText('原件保护已预留，尚未进入删除')
        await expect(drawer).not.toContainText('/must-not-render')
        await expect(
          drawer.getByRole('button', { name: '重新预检并重试', exact: true }),
        ).toHaveCount(0)
        if (i === 2)
          await page.screenshot({
            path: `../deploy/artifacts/ui-candidates/recycle-purge-summary/${width}-${colorScheme}.png`,
          })
        await drawer.getByRole('button', { name: 'Close this dialog', exact: true }).click()
      }
      expect(writes).toEqual([])
      expect(errors).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
    })
