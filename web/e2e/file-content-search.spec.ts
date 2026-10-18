import { expect, test } from '@playwright/test'
import { installFileFixture } from './fixtures/files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
async function fixture(
  page: import('@playwright/test').Page,
  options: { status?: number; hold?: boolean; ordinary?: boolean } = {},
) {
  await installFileFixture(page, options.ordinary ? { administrator: false } : {})
  const state = { requests: [] as Record<string, unknown>[], release: () => {} }
  await page.route('**/api/v1/files/content-search', async (route) => {
    const body = route.request().postDataJSON()
    state.requests.push(body)
    if (options.hold)
      await new Promise<void>((resolve) => {
        state.release = resolve
      })
    if (options.status)
      return route.fulfill({
        status: options.status,
        json: { status: options.status, code: 'CONTENT_TEST_FAILURE', traceId: 'content-fixture' },
      })
    const complete = !!body.cursor
    const name = complete ? 'second.txt' : '首个文件-abcdefghijklmnopqrstuvwxyz-0123456789.txt'
    return route.fulfill({
      json: {
        entries: [
          {
            path: '资料/' + name,
            entry: {
              name,
              kind: 'File',
              sizeBytes: 300,
              modifiedAtUnixMs: 1,
              version: 'A'.repeat(64),
              mode: '0644',
              isProtected: false,
            },
            lineNumber: 12,
            excerpt:
              '<script>window.__contentPwned=true</script> needle Api/V1\\[test]* ' +
              '很长的代码'.repeat(25),
            excerptTruncated: true,
          },
        ],
        nextCursor: complete ? '' : 'opaque.content.cursor',
        scannedEntries: 10,
        excludedEntries: 2,
        complete,
        stopReason: complete ? '' : 'ContentByteBudget',
        depthLimited: false,
        scannedFiles: 3,
        readBytes: 1048576,
        skippedNonTextFiles: 1,
        skippedOversizedFiles: 2,
      },
    })
  })
  return state
}
async function open(page: import('@playwright/test').Page) {
  await page.goto('/files')
  await page.getByRole('tab', { name: '正文搜索', exact: true }).click()
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: literal content paging, escaped snippets, accurate skipped counters and bounded layout`, async ({
      page,
    }) => {
      const state = await fixture(page)
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await open(page)
      await page.getByLabel('搜索正文', { exact: true }).fill('Api/V1\\[test]*')
      await page.getByRole('button', { name: '搜索正文', exact: true }).click()
      await expect(page.getByText('首个匹配：第 12 行', { exact: false })).toBeVisible()
      await expect(page.getByText('已达到本页读取预算', { exact: false })).toBeVisible()
      await expect(page.locator('.content-search__results pre')).toContainText(
        '<script>window.__contentPwned=true</script>',
      )
      expect(
        await page.evaluate(() => (window as unknown as Record<string, unknown>).__contentPwned),
      ).toBeUndefined()
      expect(state.requests[0]).toMatchObject({
        query: 'Api/V1\\[test]*',
        maxFileBytes: 16777216,
        cursor: '',
        take: 50,
      })
      expect(state.requests[0]).not.toHaveProperty('kind')
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await page.screenshot({
        path: `test-results/file-content-${width}-${colorScheme}.png`,
        fullPage: true,
      })
      await page.getByRole('button', { name: '继续扫描', exact: true }).click()
      await expect(page.locator('.content-search__results li')).toHaveCount(2)
      await expect(page.getByText('跳过：非文本 2 · 过大 4', { exact: false })).toBeVisible()
      expect(state.requests[1]).toMatchObject({ cursor: 'opaque.content.cursor' })
      await page.getByRole('button', { name: '清除搜索', exact: true }).click()
      await expect(page.locator('.content-search__results')).toHaveCount(0)
    })
  }
test('stop and changing tabs discard late content responses without appending snippets', async ({
  page,
}) => {
  const state = await fixture(page, { hold: true })
  await open(page)
  await page.getByLabel('搜索正文', { exact: true }).fill('needle')
  await page.getByRole('button', { name: '搜索正文', exact: true }).click()
  await expect.poll(() => state.requests.length).toBe(1)
  await page.getByRole('button', { name: '停止搜索', exact: true }).click()
  state.release()
  await expect(page.getByText('搜索已停止', { exact: false })).toBeVisible()
  await expect(page.locator('.content-search__results pre')).toHaveCount(0)
  await page.getByRole('tab', { name: '文件名搜索', exact: true }).click()
  await page.getByRole('tab', { name: '正文搜索', exact: true }).click()
  await expect(page.locator('.content-search__results')).toHaveCount(0)
})
for (const status of [403, 409, 503])
  test(`${status} does not become a successful empty content scan`, async ({ page }) => {
    await fixture(page, { status })
    await open(page)
    await page.getByLabel('搜索正文', { exact: true }).fill('needle')
    await page.getByRole('button', { name: '搜索正文', exact: true }).click()
    await expect(page.locator('.el-alert--error')).toBeVisible()
    await expect(page.getByText('已扫描的可读UTF-8文本中没有匹配', { exact: false })).toHaveCount(0)
  })
test('ordinary user first navigation and reload never mount a content-search form or issue a content request', async ({
  page,
}) => {
  const state = await fixture(page, { ordinary: true })
  await page.goto('/files')
  await page.reload()
  await expect(page.getByRole('tab', { name: '正文搜索', exact: true })).toHaveCount(0)
  await expect(page.getByLabel('搜索正文', { exact: true })).toHaveCount(0)
  expect(state.requests).toHaveLength(0)
})
test('single-file cap and case selection clear old results and bind the next explicit request', async ({
  page,
}) => {
  const state = await fixture(page)
  await open(page)
  await page.getByLabel('搜索正文', { exact: true }).fill('needle')
  await page.locator('.content-search').getByText('区分大小写', { exact: true }).click()
  await expect(
    page.locator('.content-search').getByRole('checkbox', { name: '区分大小写' }),
  ).toBeChecked()
  await page.locator('.content-search .el-select').click()
  await page.getByRole('option', { name: '单文件 ≤ 1 MiB', exact: true }).click()
  await page.getByRole('button', { name: '搜索正文', exact: true }).click()
  await expect.poll(() => state.requests.length).toBe(1)
  expect(state.requests[0]).toMatchObject({ matchCase: true, maxFileBytes: 1048576, cursor: '' })
})
