import { expect, test } from '@playwright/test'
import { configurationResponse } from './fixtures/configuration.mjs'

const id = 'cb2105f1-346f-4e16-a888-c761f8264ac9'
const records = [
  {
    id: 'record-one',
    instanceResourceId: 'application:demo',
    policyId: null,
    jobId: id,
    status: 'Succeeded',
    startedAtUtc: '2026-10-13T01:20:00Z',
    verifiedAtUtc: '2026-10-13T01:21:00Z',
    sizeBytes: 1024 * 1024,
    checksum: 'a'.repeat(64),
    errorCode: null,
  },
  {
    id: 'record-old',
    instanceResourceId: 'application:demo',
    policyId: 'policy',
    jobId: 'dd2105f1-346f-4e16-a888-c761f8264ac9',
    status: 'Expired',
    startedAtUtc: '2026-10-12T01:20:00Z',
    verifiedAtUtc: null,
    sizeBytes: null,
    checksum: null,
    errorCode: null,
  },
]
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) => {
    const key = route.request().url().split('/api/v1/')[1]?.split('?')[0] || ''
    if (key === 'backups/records') return route.fulfill({ json: records })
    if (key.endsWith('/plan'))
      return route.fulfill({
        json: {
          planHash: 'b'.repeat(64),
          changes: ['仅批准本应用私有归档'],
          warnings: ['保留最新一份，运行数据不受影响'],
        },
      })
    return route.fulfill({ json: configurationResponse(route.request().url()) })
  })
})
async function open(page: import('@playwright/test').Page) {
  await page.goto('/applications')
  await page
    .locator('.installed-row')
    .first()
    .getByRole('button', { name: '备份', exact: true })
    .click()
  const dialog = page.getByRole('dialog', { name: '应用备份', exact: true })
  await expect(dialog.getByText('最近 100 条备份记录')).toBeVisible()
  return dialog
}
for (const width of [320, 390, 1045, 1440, 1920])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: archive controls, history and footer fit`, async ({
      page,
    }, info) => {
      await page.setViewportSize({ width, height: width <= 390 ? 844 : 1000 })
      await page.emulateMedia({ colorScheme })
      const dialog = await open(page)
      await expect(dialog.getByRole('button', { name: '立即备份', exact: true })).toBeVisible()
      await expect(dialog.getByRole('button', { name: '关闭', exact: true })).toBeInViewport()
      await expect(dialog.locator('.archive-row')).toHaveCount(2)
      await expect(dialog.locator('.archive-row').last().getByRole('button')).toBeDisabled()
      const bounds = await dialog.evaluate((element) => ({
        left: element.getBoundingClientRect().left,
        right: element.getBoundingClientRect().right,
        scroll: element.querySelector('.application-backups')!.scrollWidth,
        width: element.querySelector('.application-backups')!.clientWidth,
        page: document.documentElement.scrollWidth,
        viewport: innerWidth,
      }))
      expect(bounds.left).toBeGreaterThanOrEqual(0)
      expect(bounds.right).toBeLessThanOrEqual(width + 1)
      expect(bounds.scroll).toBeLessThanOrEqual(bounds.width + 1)
      expect(bounds.page).toBeLessThanOrEqual(bounds.viewport + 1)
      await page.screenshot({ path: info.outputPath('archive.png'), scale: 'css' })
      await dialog.getByRole('button', { name: '关闭', exact: true }).click()
      await expect(dialog).not.toBeVisible()
      await expect(
        page.locator('.installed-row').last().getByRole('button', { name: '备份', exact: true }),
      ).toHaveCount(0)
    })

test('confirmed archive and cancellation keep the original job', async ({ page }) => {
  let payload: Record<string, unknown> | null = null
  let state = 'Queued'
  await page.route('**/api/v1/applications/backup', (route) => {
    payload = route.request().postDataJSON()
    return route.fulfill({ status: 202, json: { id, state, phase: 'Queued', progressPercent: 0 } })
  })
  await page.route(`**/api/v1/jobs/${id}`, (route) =>
    route.fulfill({ json: { id, state, phase: state, progressPercent: 0 } }),
  )
  await page.route(`**/api/v1/jobs/${id}/cancel`, (route) => {
    state = 'Canceled'
    return route.fulfill({ status: 204 })
  })
  const dialog = await open(page)
  await dialog.getByRole('button', { name: '立即备份', exact: true }).click()
  const confirmation = page.getByRole('dialog', { name: '确认应用备份', exact: true })
  await expect(confirmation).toBeVisible()
  expect(payload).toBeNull()
  await confirmation.getByRole('button', { name: '确认提交', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '立即备份', exact: true })).toBeDisabled()
  expect(payload).toMatchObject({
    resourceId: 'application:demo',
    parameters: { consistency: 'quiesced' },
    confirmed: true,
    planHash: 'b'.repeat(64),
  })
  await dialog.getByRole('button', { name: '请求取消', exact: true }).click()
  await page
    .getByRole('dialog', { name: '确认取消任务', exact: true })
    .getByRole('button', { name: '请求取消', exact: true })
    .click()
  await expect(dialog.locator('.backup-job')).toContainText('已取消')
})

for (const action of ['verify-backup', 'cleanup-backups'] as const)
  test(`${action}: selected archive or retention is confirmed before queuing`, async ({ page }) => {
    let payload: Record<string, unknown> | null = null
    await page.route(`**/api/v1/applications/${action}`, (route) => {
      payload = route.request().postDataJSON()
      return route.fulfill({ status: 202, json: { id, state: 'Succeeded', phase: 'Completed' } })
    })
    const dialog = await open(page)
    await dialog
      .getByRole('button', {
        name: action === 'verify-backup' ? '校验此备份' : '预检并清理',
        exact: true,
      })
      .first()
      .click()
    const confirmation = page.getByRole('dialog', {
      name: action === 'verify-backup' ? '确认校验备份' : '确认清理旧备份',
      exact: true,
    })
    expect(payload).toBeNull()
    await confirmation.getByRole('button', { name: '确认提交', exact: true }).click()
    await expect.poll(() => payload).not.toBeNull()
    expect(payload).toMatchObject({
      confirmed: true,
      resourceId: 'application:demo',
      parameters:
        action === 'verify-backup'
          ? { backupId: id.replace(/-/g, '') }
          : { retentionDays: '14', retentionCount: '14' },
    })
  })

test('permission expiry clears archived metadata and disables new actions', async ({ page }) => {
  const dialog = await open(page)
  await page.route('**/api/v1/backups/records**', (route) =>
    route.fulfill({ status: 403, json: { title: '管理权限已失效' } }),
  )
  await dialog.getByRole('button', { name: '刷新历史', exact: true }).click()
  await expect(dialog.locator('.archive-row')).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: '立即备份', exact: true })).toBeDisabled()
  await expect(dialog).toContainText('管理权限已失效')
})

test('service errors preserve previous history without pretending success', async ({ page }) => {
  const dialog = await open(page)
  await page.route('**/api/v1/backups/records**', (route) =>
    route.fulfill({ status: 503, json: { title: '备份服务暂不可用' } }),
  )
  await dialog.getByRole('button', { name: '刷新历史', exact: true }).click()
  await expect(dialog.locator('.archive-row')).toHaveCount(2)
  await expect(dialog).toContainText('备份服务暂不可用')
})

test('archive mode switches both ways and canceled confirmation never queues', async ({ page }) => {
  let mutations = 0
  await page.route('**/api/v1/applications/backup', (route) => {
    mutations++
    return route.fulfill({ status: 202, json: { id, state: 'Queued' } })
  })
  const dialog = await open(page)
  await dialog.locator('.backup-toolbar .el-select').click()
  await page.getByRole('option', { name: '实时归档 · 文件可能变化', exact: true }).click()
  await dialog.locator('.backup-toolbar .el-select').click()
  await page.getByRole('option', { name: '暂停归档 · 临时暂停应用', exact: true }).click()
  await dialog.getByRole('button', { name: '立即备份', exact: true }).click()
  await page
    .getByRole('dialog', { name: '确认应用备份', exact: true })
    .getByRole('button', { name: '取消', exact: true })
    .click()
  await expect(dialog.getByRole('button', { name: '立即备份', exact: true })).toBeEnabled()
  expect(mutations).toBe(0)
})

test('application policy form enforces gzip, verification and explicit enabling confirmation', async ({
  page,
}) => {
  let saved: Record<string, unknown> | null = null
  await page.route('**/api/v1/backups/policies', (route) => {
    if (route.request().method() === 'POST') {
      saved = route.request().postDataJSON()
      return route.fulfill({ json: { ...saved, id: 'policy', version: 1 } })
    }
    return route.fulfill({ json: [] })
  })
  await page.goto('/backups')
  await page.getByRole('button', { name: '新增策略', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '新增备份策略', exact: true })
  await dialog.getByRole('combobox').first().click()
  await page.getByRole('option', { name: 'demo', exact: true }).click()
  await expect(dialog.getByRole('combobox').nth(1)).toBeDisabled()
  await dialog.getByRole('button', { name: '保存', exact: true }).click()
  const confirmation = page.getByRole('dialog', { name: '启用应用备份策略', exact: true })
  await expect(confirmation).toBeVisible()
  expect(saved).toBeNull()
  await confirmation.getByRole('button', { name: '确认启用', exact: true }).click()
  await expect.poll(() => saved).not.toBeNull()
  expect(saved).toMatchObject({
    instanceResourceId: 'application:demo',
    compression: 'gzip',
    verifyAfterBackup: true,
    consistency: 'quiesced',
    confirmed: true,
  })
})
