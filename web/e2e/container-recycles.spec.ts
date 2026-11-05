import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerRecycleFixture } from './fixtures/container-recycles.mjs'
test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH },
})
const modal = (page: Page) => page.getByRole('dialog', { name: '回收容器普通文件', exact: true })
async function prepare(
  page: Page,
  control: Awaited<ReturnType<typeof installContainerRecycleFixture>>,
) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page
    .getByRole('region', { name: '容器文件', exact: true })
    .getByRole('button', { name: `回收 ${control.base.state.longName}`, exact: true })
    .click()
  await modal(page).getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原文件回收任务', exact: true }).click()
  await expect(modal(page).getByText('预检完成，尚未移除文件', { exact: true })).toBeVisible()
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme} explicit plan, bounded footer and one execution`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      const c = await installContainerRecycleFixture(page)
      await prepare(page, c)
      await expect(page.locator('html')).toHaveClass(
        colorScheme === 'dark' ? /dark/ : /^(?!.*dark)/,
      )
      const confirm = modal(page).getByRole('button', { name: '确认文件回收', exact: true })
      await expect(confirm).toBeInViewport({ ratio: 1 })
      await expect(
        modal(page).getByRole('button', { name: '请求取消文件回收', exact: true }),
      ).toBeInViewport({ ratio: 1 })
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      expect(
        c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm')),
      ).toHaveLength(0)
      await page.screenshot({
        path: `../deploy/artifacts/ui-candidates/container-recycle/${width}-${colorScheme}.png`,
      })
      await confirm.click()
      await modal(page).getByRole('button', { name: '查询原文件回收任务', exact: true }).click()
      await expect(modal(page).getByText('文件回收已完成', { exact: true })).toBeVisible()
      expect(
        c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm')),
      ).toHaveLength(1)
      expect(
        await page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-hints-v1')),
      ).not.toContain('isolated-recycle-confirmation')
    })
test('lost registration reload only queries the original key', async ({ page }) => {
  const c = await installContainerRecycleFixture(page, { loseQueue: true })
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page
    .getByRole('region', { name: '容器文件', exact: true })
    .getByRole('button', { name: `回收 ${c.base.state.longName}`, exact: true })
    .click()
  await modal(page).getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  await expect.poll(() => c.state.registered).toBe(true)
  const key = c.state.key
  await page.reload()
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect.poll(() => c.state.reads.some((v: string) => v === '/by-key/' + key)).toBe(true)
  expect(c.state.writes.filter((v: { path: string }) => v.path === '')).toHaveLength(1)
})
test('Unknown acknowledgement lost response is verified without resending', async ({ page }) => {
  const c = await installContainerRecycleFixture(page, { filePage: true, loseAck: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '容器文件回收异常', exact: true }).click()
  const d = page.getByRole('dialog', { name: '容器文件回收异常处置', exact: true })
  await d.getByRole('button', { name: '刷新异常文件回收任务', exact: true }).click()
  await expect(d).toContainText('尚未证明')
  await d.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await d.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  await d.getByRole('button', { name: '查询原处置', exact: true }).click()
  await expect(d).toContainText('已核实原处置回执')
  expect(c.row.state).toBe('Acknowledged')
  expect(
    c.state.writes.filter((v: { path: string }) => v.path.endsWith('/acknowledge')),
  ).toHaveLength(1)
})

for (const colorScheme of ['light', 'dark'] as const)
  test(`320x600 ${colorScheme} cancellation requires explicit choice`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 600 })
    await page.emulateMedia({ colorScheme })
    const c = await installContainerRecycleFixture(page)
    await prepare(page, c)
    const cancel = modal(page).getByRole('button', { name: '请求取消文件回收', exact: true })
    await cancel.click()
    await page.getByRole('button', { name: '继续等待', exact: true }).click()
    expect(c.state.writes.filter((v: { path: string }) => v.path.endsWith('/cancel'))).toHaveLength(
      0,
    )
    await cancel.click()
    await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
    await expect
      .poll(() => c.state.writes.filter((v: { path: string }) => v.path.endsWith('/cancel')).length)
      .toBe(1)
    expect(
      c.state.writes.filter((v: { path: string }) => v.path.endsWith('/confirm')),
    ).toHaveLength(0)
  })
