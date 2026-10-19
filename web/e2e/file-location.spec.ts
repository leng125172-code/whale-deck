import { expect, test } from '@playwright/test'
import { installFileFixture } from './fixtures/files.mjs'

const storageKey = 'whaledeck-files-location-v1'
const rootId = 'whaledeck.files.shared'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
// UI protocol fixture, not real identity, filesystem or Agent acceptance.
async function setup(page) {
  const fixture = await installFileFixture(page)
  const reads: string[] = []
  page.on('request', request => {
    const url = new URL(request.url())
    if (url.pathname.endsWith('/files/entries')) reads.push(url.searchParams.get('path') || '')
  })
  return { ...fixture, reads }
}
async function enter(page) {
  await page.goto('/files')
  await page.getByRole('button', { name: '资料', exact: true }).click()
  await expect.poll(() => page.evaluate(key => JSON.parse(sessionStorage.getItem(key) || 'null')?.path, storageKey)).toBe('资料')
  await expect(page.getByText('此目录暂无可显示的文件')).toBeVisible()
}
for (const width of [320, 1440]) test(`${width}: reload returns to validated directory, no write or URL path`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 })
  const { state, reads } = await setup(page)
  await enter(page)
  const before = reads.length
  await page.reload()
  await expect(page.getByText('此目录暂无可显示的文件')).toBeVisible()
  await expect.poll(() => reads.slice(before)).toEqual(['资料'])
  expect(state.writes).toHaveLength(0)
  expect(page.url()).not.toContain(encodeURIComponent('资料'))
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  await page.locator('.el-breadcrumb').getByRole('button', { name: '根目录', exact: true }).click()
  await expect(page.getByRole('button', { name: '资料', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('button', { name: '资料', exact: true })).toBeVisible()
  expect(reads.at(-1)).toBe('')
})
test('deleted saved directory falls back only after 404; unavailable does not fake root success', async ({ page }) => {
  const { state, reads } = await setup(page)
  await enter(page)
  let failure = 503
  await page.route('**/api/v1/files/entries?**', async route => {
    if (!new URL(route.request().url()).searchParams.get('path')) return route.fallback()
    return route.fulfill({ status: failure, json: { status: failure, code: 'LOCATION_UNAVAILABLE' } })
  })
  let before = reads.length
  await page.reload()
  await expect(page.getByText('Agent 暂时不可用', { exact: true })).toBeVisible()
  expect(reads.slice(before)).toEqual(['资料'])
  failure = 404
  before = reads.length
  await page.reload()
  await expect(page.getByRole('button', { name: '资料', exact: true })).toBeVisible()
  expect(reads.slice(before)).toEqual(['资料', ''])
  expect(state.writes).toHaveLength(0)
})
for (const kind of ['subject', 'root', 'path', '403']) test(`rejects stale ${kind} hint without unauthorized replay`, async ({ page }) => {
  const { state, reads } = await setup(page)
  await enter(page)
  const hint = { schema: 1, subject: kind === 'subject' ? 'another-admin' : 'preview-user', rootId: kind === 'root' ? 'revoked-root' : rootId, path: kind === 'path' ? '../private' : '资料' }
  await page.evaluate(({ key, value }) => sessionStorage.setItem(key, JSON.stringify(value)), { key: storageKey, value: hint })
  if (kind === '403') state.entryStatus = 403
  const before = reads.length
  await page.reload()
  if (kind === '403') {
    await expect(page.getByText('权限不足或此文件受到保护', { exact: true })).toBeVisible()
    expect(await page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull()
    expect(reads.slice(before)).toEqual(['资料'])
  } else {
    await expect(page.getByRole('button', { name: '资料', exact: true })).toBeVisible()
    expect(reads.slice(before)).toEqual([''])
  }
  expect(state.writes).toHaveLength(0)
})
