import { expect, test } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
async function openFiles(page: import('@playwright/test').Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect(
    page.getByRole('region', { name: '容器文件', exact: true }).getByRole('listitem'),
  ).toHaveCount(4)
  return page.getByRole('region', { name: '容器文件', exact: true })
}
for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: browse, breadcrumb, pagination and complete download`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      const fixture = await installContainerFilesFixture(page)
      await page.goto('/containers')
      expect(fixture.state.requests).toHaveLength(0)
      const panel = await openFiles(page)
      await expect(panel.getByRole('button', { name: '下载 .env', exact: true })).toBeDisabled()
      await expect(panel.getByRole('button', { name: '下载 link', exact: true })).toBeDisabled()
      await expect(panel.getByTitle(fixture.state.longName, { exact: true })).toBeVisible()
      await panel.getByRole('button', { name: '加载更多', exact: true }).click()
      await expect(panel.getByRole('listitem')).toHaveCount(5)
      await panel.getByRole('button', { name: '资料', exact: true }).click()
      await expect(panel.getByRole('listitem')).toHaveCount(1)
      const downloading = page.waitForEvent('download')
      await panel.getByRole('button', { name: '下载 中文.bin', exact: true }).click()
      const download = await downloading
      expect(download.suggestedFilename()).toBe('中文.bin')
      expect(await download.failure()).toBeNull()
      const stream = await download.createReadStream()
      const bytes: number[] = []
      for await (const chunk of stream!) bytes.push(...chunk)
      expect(bytes).toEqual([1, 2, 3])
      await panel
        .getByRole('navigation', { name: '容器文件路径' })
        .getByRole('button', { name: '/', exact: true })
        .click()
      await expect(panel.getByRole('listitem')).toHaveCount(4)
      await panel.getByRole('textbox', { name: '容器内相对目录' }).fill('资料')
      await panel.getByRole('button', { name: '前往', exact: true }).click()
      await expect(panel.getByRole('listitem')).toHaveCount(1)
      await panel.getByRole('button', { name: '刷新', exact: true }).click()
      await expect(panel.getByRole('listitem')).toHaveCount(1)
      await panel.scrollIntoViewIfNeeded()
      expect(await panel.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(
        true,
      )
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({ path: `test-results/container-files-${width}-${colorScheme}.png` })
      await page.getByRole('button', { name: '收起文件', exact: true }).click()
      await expect(panel).toHaveCount(0)
      expect(fixture.state.requests.every((row: { method: string }) => row.method === 'GET')).toBe(
        true,
      )
      expect(fixture.session.state.writes).toEqual([])
      expect(errors).toEqual([])
    })
  }
test('changed instance clears old entries and requires explicit reconnect', async ({ page }) => {
  const fixture = await installContainerFilesFixture(page)
  const panel = await openFiles(page)
  fixture.state.entriesStatus = 409
  await panel.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(panel.getByText('容器实例或文件已变化，请重新连接')).toBeVisible()
  await expect(panel.getByRole('listitem')).toHaveCount(0)
  expect(fixture.state.targetReads).toBe(1)
  fixture.state.entriesStatus = 200
  fixture.state.target.instanceId = '778:2026-10-22T01:02:04Z'
  await panel.getByRole('button', { name: '重新连接', exact: true }).click()
  await expect(panel.getByRole('listitem')).toHaveCount(4)
  expect(fixture.state.targetReads).toBe(2)
  expect(fixture.state.requests.at(-1)!.query.instanceId).toBe(fixture.state.target.instanceId)
})
test('changed directory pagination never merges a different snapshot', async ({ page }) => {
  const fixture = await installContainerFilesFixture(page)
  const panel = await openFiles(page)
  fixture.state.moreChanged = true
  await panel.getByRole('button', { name: '加载更多', exact: true }).click()
  await expect(panel.getByText('目录已变化，请刷新后重新加载')).toBeVisible()
  await expect(panel.getByRole('listitem')).toHaveCount(4)
})
test('protected container has no generic file control', async ({ page }) => {
  const fixture = await installContainerFilesFixture(page)
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'unrelated' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await expect(page.getByRole('button', { name: '容器文件', exact: true })).toHaveCount(0)
  expect(fixture.state.requests).toHaveLength(0)
})

test('collapsing a pending read cannot resurrect the old private list', async ({ page }) => {
  const fixture = await installContainerFilesFixture(page)
  const panel = await openFiles(page)
  fixture.state.pauseEntries = true
  await panel.getByRole('button', { name: '刷新', exact: true }).click()
  await expect.poll(() => fixture.state.entriesWaiting).toBe(true)
  await page.getByRole('button', { name: '收起文件', exact: true }).click()
  await expect(panel).toHaveCount(0)
  fixture.state.pauseEntries = false
  fixture.state.releaseEntries()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect(panel.getByRole('listitem')).toHaveCount(4)
  expect(fixture.state.targetReads).toBe(2)
})

test('canceling a pending download never creates a browser save', async ({ page }) => {
  const fixture = await installContainerFilesFixture(page)
  const panel = await openFiles(page)
  await panel.getByRole('button', { name: '资料', exact: true }).click()
  await expect(panel.getByRole('listitem')).toHaveCount(1)
  const downloads: string[] = []
  page.on('download', (download) => downloads.push(download.suggestedFilename()))
  fixture.state.pauseDownload = true
  await panel.getByRole('button', { name: '下载 中文.bin', exact: true }).click()
  await expect.poll(() => fixture.state.downloadWaiting).toBe(true)
  await panel.getByRole('button', { name: '取消', exact: true }).click()
  fixture.state.releaseDownload()
  await expect(panel.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
  await panel.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(panel.getByRole('listitem')).toHaveCount(1)
  await expect(panel.getByText('文件已完整校验，已发起浏览器保存')).toHaveCount(0)
  expect(downloads).toEqual([])
})
