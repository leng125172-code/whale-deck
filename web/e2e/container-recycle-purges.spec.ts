import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installRecyclePurgeFixture } from './fixtures/container-recycle-purges.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
const modal = (page: Page) => page.getByRole('dialog', { name: '清理容器留存原件', exact: true })
async function open(page: Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page.getByRole('button', { name: '清理留存原件', exact: true }).click()
}
async function prepare(page: Page) {
  await open(page)
  await modal(page).getByRole('button', { name: '选择此来源', exact: true }).click()
  await modal(page).getByRole('button', { name: '生成留存清理预检', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原留存清理任务', exact: true }).click()
  await expect(modal(page).getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible()
}
async function confirm(page: Page) {
  await modal(page).getByRole('button', { name: '确认留存清理', exact: true }).click()
  await page.getByRole('button', { name: '确认删除留存原件', exact: true }).click()
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme} explicit irreversible confirmation, source and footer`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      const c = await installRecyclePurgeFixture(page)
      await prepare(page)
      const button = modal(page).getByRole('button', { name: '确认留存清理', exact: true })
      await expect(button).toBeInViewport({ ratio: 1 })
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      expect(c.state.writes).toHaveLength(1)
      expect(c.state.writes[0]!.body).toEqual({ sourceIntentId: c.source.sourceIntentId })
      await button.click()
      await expect(
        page.getByRole('dialog', { name: '确认清理留存原件', exact: true }),
      ).toContainText('恢复须走独立归档复制入口并重新核验HDD备份')
      await page.getByRole('button', { name: '暂不清理', exact: true }).click()
      expect(c.state.writes).toHaveLength(1)
      await confirm(page)
      await modal(page).getByRole('button', { name: '查询原留存清理任务', exact: true }).click()
      await expect(modal(page).getByText('本次留存原件已清理', { exact: true })).toBeVisible()
      expect(
        c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm')),
      ).toHaveLength(1)
      expect(
        await page.evaluate(() =>
          sessionStorage.getItem('whaledeck-container-recycle-purge-hints-v1'),
        ),
      ).not.toContain('isolated-purge-confirmation')
      await page.screenshot({
        path: `../deploy/artifacts/ui-candidates/container-recycle-purge/${width}-${colorScheme}.png`,
      })
    })
test('lost registration reload queries original key without resubmitting', async ({ page }) => {
  const c = await installRecyclePurgeFixture(page, { loseQueue: true })
  await open(page)
  await modal(page).getByRole('button', { name: '选择此来源', exact: true }).click()
  await modal(page).getByRole('button', { name: '生成留存清理预检', exact: true }).click()
  const retry = modal(page).getByRole('button', { name: '重试原清理登记', exact: true })
  await expect(retry).toBeVisible()
  // The hint renders before the original POST reaches the fixture. Reload only
  // after the server accepted that exact registration and lost its response.
  await expect.poll(() => c.state.lostQueue).toBe(true)
  await expect(retry).toBeEnabled()
  expect(c.state.writes).toHaveLength(1)
  const originalKey = c.state.key
  expect(originalKey).toMatch(/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/)
  await expect
    .poll(() =>
      page.evaluate(
        () =>
          JSON.parse(
            sessionStorage.getItem('whaledeck-container-recycle-purge-hints-v1') ?? '[]',
          )[0],
      ),
    )
    .toMatchObject({ containerId: c.source.location.containerId, key: originalKey, id: null })
  c.state.reads.length = 0
  await page.reload()
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect(page.getByRole('region', { name: '容器文件', exact: true })).toBeVisible()
  await expect.poll(() => c.state.reads.includes(`/by-key/${originalKey}`)).toBe(true)
  expect(c.state.writes).toHaveLength(1)
  expect(c.state.writes[0]!.key).toBe(originalKey)
})
test('Unknown preserves null fact and never offers repeat or cancellation', async ({ page }) => {
  const c = await installRecyclePurgeFixture(page, { unknown: true })
  await prepare(page)
  await confirm(page)
  await modal(page).getByRole('button', { name: '查询原留存清理任务', exact: true }).click()
  await expect(modal(page).getByText('清理结果未知，保护已保留', { exact: true })).toBeVisible()
  await expect(modal(page).getByText(/尚未证明，不能推断原件仍在/)).toBeVisible()
  await expect(
    modal(page).getByRole('button', { name: '请求取消留存清理', exact: true }),
  ).toHaveCount(0)
  expect(c.row.purgeApplied).toBeNull()
  expect(c.state.writes).toHaveLength(2)
})
test('session end clears original reference and rejects late status', async ({ page }) => {
  const c = await installRecyclePurgeFixture(page)
  await prepare(page)
  c.state.paused = true
  await modal(page).getByRole('button', { name: '查询原留存清理任务', exact: true }).click()
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
  c.state.release()
  await page.waitForTimeout(200)
  await expect(modal(page)).toHaveCount(0)
  expect(
    (await page.evaluate(() =>
      sessionStorage.getItem('whaledeck-container-recycle-purge-hints-v1'),
    )) ?? '',
  ).not.toContain(c.id)
  expect(c.state.writes).toHaveLength(1)
})
test('manual administrator disposition preserves Unknown fact', async ({ page }) => {
  const c = await installRecyclePurgeFixture(page, { filePage: true, unknown: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '容器留存清理异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器留存清理异常处置', exact: true })
  await recovery.getByRole('button', { name: '刷新异常留存清理任务', exact: true }).click()
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await recovery.getByRole('button', { name: '暂不处置', exact: true }).click()
  expect(c.state.writes).toHaveLength(0)
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await recovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  await expect(recovery.getByText(/已处置历史未知结果并释放保护/)).toBeVisible()
  expect(c.row.purgeApplied).toBeNull()
  expect(c.state.writes).toHaveLength(1)
})
