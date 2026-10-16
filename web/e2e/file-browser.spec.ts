import { expect, test } from '@playwright/test'
import { installFileFixture } from './fixtures/files.mjs'

async function fixture(page, options: { hold?: boolean; status?: number } = {}) {
  const old = await installFileFixture(page)
  const state = { searches: [] as Record<string, unknown>[], directories: [] as string[], release: () => {} }
  await page.route('**/api/v1/files/directories?**', async route => {
    const url = new URL(route.request().url()), path = url.searchParams.get('path') || '', after = url.searchParams.get('after') || ''
    state.directories.push(path)
    const entries = path ? [] : [{ ...old.entry, name: after ? '第二批目录' : '资料', kind: 'Directory', sizeBytes: 0 }]
    await route.fulfill({ json: { entries, nextCursor: !path && !after ? '资料' : '', version: 'A'.repeat(64) } })
  })
  await page.route('**/api/v1/files/search', async route => {
    const command = route.request().postDataJSON(); state.searches.push(command)
    if (options.hold) await new Promise<void>(resolve => { state.release = resolve })
    if (options.status) { await route.fulfill({ status: options.status, json: { status: options.status, title: '搜索验证错误' } }); return }
    const again = !!command.cursor
    await route.fulfill({ json: { entries: [{ path: `资料/${again ? '第二个-' : ''}${old.entry.name}`, entry: old.entry }], nextCursor: again ? '' : 'opaque.cursor', scannedEntries: 100, excludedEntries: 2, complete: again, stopReason: again ? '' : 'ScanBudget', depthLimited: false } })
  })
  return state
}

for (const width of [320, 1045, 1440]) for (const colorScheme of ['light', 'dark'] as const) {
  test(`${width} ${colorScheme}: tree paging, recursive search, resume, filters and containing directory`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 }); await page.emulateMedia({ colorScheme })
    const state = await fixture(page)
    await page.goto('/files')
    await page.getByRole('button', { name: '展开目录树', exact: true }).click()
    await page.locator('.el-tree-node__expand-icon').first().click()
    await expect(page.locator('.tree-label', { hasText: '资料' })).toBeVisible()
    await page.locator('.tree-label', { hasText: '加载更多目录' }).click()
    await expect(page.locator('.tree-label', { hasText: '第二批目录' })).toBeVisible()
    await page.getByRole('button', { name: '收起目录树', exact: true }).click()
    await page.getByLabel('搜索文件名', { exact: true }).fill('中文')
    await page.getByRole('button', { name: '搜索', exact: true }).click()
    await expect(page.getByText('尚未搜完', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: '继续扫描', exact: true }).click()
    await expect(page.getByText('搜索完成', { exact: false })).toBeVisible()
    expect(state.searches[1]?.cursor).toBe('opaque.cursor')
    await page.getByText('区分大小写', { exact: true }).click()
    await expect(page.getByRole('checkbox', { name: '区分大小写', exact: true })).toBeChecked()
    await expect(page.getByText('搜索完成', { exact: false })).toHaveCount(0)
    await page.getByRole('button', { name: '搜索', exact: true }).click()
    expect(state.searches.at(-1)?.matchCase).toBe(true)
    await page.getByRole('button', { name: '打开所在目录', exact: true }).first().click()
    await expect(page.getByText('尚未搜完', { exact: false })).toHaveCount(0)
    await expect(page.getByText('此目录暂无可显示的文件')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  })
}
test('stopped search ignores late response and invalid names do not send requests', async ({ page }) => {
  const state = await fixture(page, { hold: true }); await page.goto('/files')
  await page.getByLabel('搜索文件名', { exact: true }).fill('../file')
  await expect(page.getByRole('button', { name: '搜索', exact: true })).toBeDisabled(); expect(state.searches).toHaveLength(0)
  await page.getByLabel('搜索文件名', { exact: true }).fill('report')
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect.poll(() => state.searches.length).toBe(1)
  await page.getByRole('button', { name: '停止搜索', exact: true }).click(); state.release()
  await expect(page.getByText('搜索已停止', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: '打开所在目录', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: '清除搜索', exact: true }).click()
  await expect(page.getByText('搜索已停止', { exact: false })).toHaveCount(0)
})
for (const status of [403, 409, 503]) test(`search ${status} is visible, never an empty-success result`, async ({ page }) => {
  await fixture(page, { status }); await page.goto('/files')
  await page.getByLabel('搜索文件名', { exact: true }).fill('report')
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect(page.locator('.el-alert--error')).toBeVisible()
  await expect(page.getByText('没有匹配的名称', { exact: true })).toHaveCount(0)
  if (status === 403) await expect(page.locator('.files-table .file-name')).toHaveCount(0)
})

test('type selection, empty completed search and clear return to initial state', async ({ page }) => {
  const state = await fixture(page); await page.goto('/files')
  await page.getByLabel('搜索文件名', { exact: true }).fill('report')
  await page.locator('.file-search .el-select').click()
  await page.getByRole('option', { name: '仅目录', exact: true }).click()
  await page.route('**/api/v1/files/search', route => { state.searches.push(route.request().postDataJSON()); return route.fulfill({ json: { entries: [], nextCursor: '', scannedEntries: 12, excludedEntries: 0, complete: true, stopReason: '', depthLimited: false } }) })
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect(page.getByText('没有匹配的名称', { exact: true })).toBeVisible()
  expect(state.searches.at(-1)?.kind).toBe('Directory')
  await page.getByRole('button', { name: '清除搜索', exact: true }).click()
  await expect(page.locator('.file-search__results')).toHaveCount(0)
})

test('changing directory aborts search and late response cannot repopulate results', async ({ page }) => {
  const state = await fixture(page, { hold: true }); await page.goto('/files')
  await page.getByLabel('搜索文件名', { exact: true }).fill('report')
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect.poll(() => state.searches.length).toBe(1)
  await page.locator('.files-table').getByRole('button', { name: '资料', exact: true }).click()
  state.release()
  await expect(page.getByText('此目录暂无可显示的文件')).toBeVisible()
  await expect(page.locator('.file-search__results')).toHaveCount(0)
  await expect(page.locator('.el-alert--error')).toHaveCount(0)
})

test('tree continuation detects directory changes, refresh retries rather than inventing an empty branch', async ({ page }) => {
  await fixture(page); await page.goto('/files')
  await page.getByRole('button', { name: '展开目录树', exact: true }).click()
  await page.locator('.el-tree-node__expand-icon').first().click()
  await expect(page.locator('.tree-label', { hasText: '加载更多目录' })).toBeVisible()
  await page.route('**/api/v1/files/directories?**', route => route.fulfill({ json: { entries: [], nextCursor: '', version: 'B'.repeat(64) } }))
  await page.locator('.tree-label', { hasText: '加载更多目录' }).click()
  await expect(page.getByText('目录已变化，请刷新目录树后重试', { exact: true })).toBeVisible()
  await expect(page.locator('.tree-label', { hasText: '资料' })).toBeVisible()
  await page.getByRole('button', { name: '刷新目录树', exact: true }).click()
  await expect(page.locator('.tree-label', { hasText: '加载更多目录' })).toHaveCount(0)
})

test('a legal more: directory cannot collide with root continuation nodes', async ({ page }) => {
  await fixture(page)
  await page.route('**/api/v1/files/directories?**', route => {
    const after = new URL(route.request().url()).searchParams.get('after')
    return route.fulfill({ json: { entries: [{ name: after ? 'next' : 'more:', kind: 'Directory', sizeBytes: 0, modifiedAtUnixMs: 0, version: 'A'.repeat(64), mode: '0750', isProtected: false }], nextCursor: after ? '' : 'more:', version: 'A'.repeat(64) } })
  })
  await page.goto('/files')
  await page.getByRole('button', { name: '展开目录树', exact: true }).click()
  await page.locator('.el-tree-node__expand-icon').first().click()
  await expect(page.locator('.tree-label').filter({ hasText: /^more:$/ })).toBeVisible()
  await page.locator('.tree-label', { hasText: '加载更多目录' }).click()
  await expect(page.locator('.tree-label').filter({ hasText: /^more:$/ })).toBeVisible()
  await expect(page.locator('.tree-label').filter({ hasText: /^next$/ })).toBeVisible()
  await page.locator('.tree-label').filter({ hasText: /^more:$/ }).click()
  await expect(page.getByText('此目录暂无可显示的文件')).toBeVisible()
})
