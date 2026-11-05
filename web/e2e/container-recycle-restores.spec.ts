import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installRecycleRestoreFixture } from './fixtures/container-recycle-restores.mjs'
test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH },
})
const modal = (page: Page) =>
  page.getByRole('dialog', { name: '恢复已回收的容器文件', exact: true })
async function open(page: Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page.getByRole('button', { name: '恢复已回收文件', exact: true }).click()
}
async function prepare(page: Page) {
  await open(page)
  await modal(page).getByRole('button', { name: '选择此来源', exact: true }).click()
  await modal(page).getByRole('button', { name: '生成独立恢复预检', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原文件恢复任务', exact: true }).click()
  await expect(modal(page).getByText('恢复预检完成，尚未写入', { exact: true })).toBeVisible()
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme} independent source confirmation and footer`, async ({ page }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      const c = await installRecycleRestoreFixture(page)
      await prepare(page)
      const confirm = modal(page).getByRole('button', { name: '确认文件恢复', exact: true })
      await expect(confirm).toBeInViewport({ ratio: 1 })
      await expect(
        modal(page).getByRole('button', { name: '请求取消文件恢复', exact: true }),
      ).toBeInViewport({ ratio: 1 })
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      expect(c.state.writes).toHaveLength(1)
      expect(c.state.writes[0]!.body).toEqual({ sourceIntentId: c.source.sourceIntentId })
      await page.screenshot({
        path: `../deploy/artifacts/ui-candidates/container-recycle-restore/${width}-${colorScheme}.png`,
      })
      await confirm.click()
      await modal(page).getByRole('button', { name: '查询原文件恢复任务', exact: true }).click()
      await expect(modal(page).getByText('本次文件恢复已完成', { exact: true })).toBeVisible()
      expect(
        c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm')),
      ).toHaveLength(1)
      expect(
        await page.evaluate(() =>
          sessionStorage.getItem('whaledeck-container-recycle-restore-hints-v1'),
        ),
      ).not.toContain('isolated-independent-restore-confirmation')
    })
test('lost registration reload only queries original key', async ({ page }) => {
  const c = await installRecycleRestoreFixture(page, { loseQueue: true })
  await open(page)
  await modal(page).getByRole('button', { name: '选择此来源', exact: true }).click()
  await modal(page).getByRole('button', { name: '生成独立恢复预检', exact: true }).click()
  await expect(
    modal(page).getByRole('button', { name: '重试原恢复登记', exact: true }),
  ).toBeVisible()
  await page.reload()
  await page.getByRole('button', { name: '详情', exact: true }).first().click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect
    .poll(() => c.state.reads.some((p: string) => p === `/by-key/${c.state.key}`))
    .toBe(true)
  expect(c.state.writes).toHaveLength(1)
})
test('cancel dialog dismissal sends nothing and explicit cancel cannot undo a restore', async ({
  page,
}) => {
  const c = await installRecycleRestoreFixture(page)
  await prepare(page)
  await modal(page).getByRole('button', { name: '请求取消文件恢复', exact: true }).click()
  await page.getByRole('button', { name: '继续等待', exact: true }).click()
  expect(c.state.writes).toHaveLength(1)
  await modal(page).getByRole('button', { name: '请求取消文件恢复', exact: true }).click()
  await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect(modal(page).getByText('恢复已取消', { exact: true })).toBeVisible()
  expect(c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm'))).toHaveLength(
    0,
  )
})
test('Unknown retains null attribution and offers no repeat or cancel', async ({ page }) => {
  const c = await installRecycleRestoreFixture(page, { unknown: true })
  await prepare(page)
  await modal(page).getByRole('button', { name: '确认文件恢复', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原文件恢复任务', exact: true }).click()
  await expect(modal(page).getByText('恢复结果未知，保护已保留', { exact: true })).toBeVisible()
  await expect(modal(page).getByText(/尚未证明。Unknown不代表没有发生写入/)).toBeVisible()
  await expect(
    modal(page).getByRole('button', { name: '请求取消文件恢复', exact: true }),
  ).toHaveCount(0)
  expect(c.row.receipt!.restoredByThisOperation).toBeNull()
})
test('session end removes refs and late status cannot restore the dialog', async ({ page }) => {
  const c = await installRecycleRestoreFixture(page)
  await prepare(page)
  c.state.paused = true
  await modal(page).getByRole('button', { name: '查询原文件恢复任务', exact: true }).click()
  await expect.poll(() => c.state.waiting).toBe(true)
  await page.evaluate(() =>
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'whaledeck-session-ended',
        storageArea: localStorage,
        newValue: JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) }),
      }),
    ),
  )
  await expect(modal(page)).toHaveCount(0)
  expect(
    (await page.evaluate(() =>
      sessionStorage.getItem('whaledeck-container-recycle-restore-hints-v1'),
    )) ?? '',
  ).not.toContain(c.id)
  c.state.release()
  await page.waitForTimeout(200)
  await expect(modal(page)).toHaveCount(0)
  expect(c.state.writes).toHaveLength(1)
})
test('new live instance never rewrites or confirms an old Ready plan', async ({ page }) => {
  const c = await installRecycleRestoreFixture(page)
  await prepare(page)
  c.base.state.target.instanceId = '888:2026-10-23T01:02:03Z'
  await page.reload()
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page.getByRole('button', { name: '恢复已回收文件', exact: true }).click()
  await expect(
    modal(page).getByRole('button', { name: '确认文件恢复', exact: true }),
  ).toBeDisabled()
  expect(c.state.writes).toHaveLength(1)
})
test('administrator disposition is separate and preserves unknown attribution', async ({
  page,
}) => {
  const c = await installRecycleRestoreFixture(page, { filePage: true, unknown: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '容器文件恢复异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器文件恢复异常处置', exact: true })
  await recovery.getByRole('button', { name: '刷新异常文件恢复任务', exact: true }).click()
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await recovery.getByRole('button', { name: '暂不处置', exact: true }).click()
  expect(c.state.writes).toHaveLength(0)
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await recovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  await expect(recovery.getByText(/已处置历史未知结果并释放保护/)).toBeVisible()
  expect(c.row.receipt!.restoredByThisOperation).toBeNull()
  expect(c.state.writes).toHaveLength(1)
})
