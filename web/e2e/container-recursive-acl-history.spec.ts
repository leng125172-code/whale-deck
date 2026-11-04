import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'
// Synthetic browser contracts; deployed Authentik/Agent/HDD acceptance is separate.
test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
const modal = (page: Page) => page.getByRole('dialog', { name: '递归ACL历史', exact: true })
async function setup(page: Page, options: { administrator?: boolean } = {}) {
  const base = await installContainerFilesFixture(page, options),
    target = base.state.target
  const summary = {
    captureId: 'a'.repeat(32),
    historyDigest: 'B'.repeat(64),
    createdAtUnixMs: 0,
    sourceInstanceId: '666:2026-11-03T01:02:03Z',
    changedAclType: 'access',
    manifestDigest: 'D'.repeat(64),
    entryCount: 2,
    fileCount: 1,
    directoryCount: 1,
    changeCount: 0,
  }
  const acl = [
    { tag: 'user_obj', id: null, permissions: 0 },
    { tag: 'user', id: 0, permissions: 0 },
    { tag: 'group_obj', id: null, permissions: 0 },
    { tag: 'mask', id: null, permissions: 0 },
    { tag: 'other', id: null, permissions: 0 },
  ]
  const state = {
    requests: [] as { method: string; query: Record<string, string> }[],
    status: 200,
    pause: false,
    waiting: false,
    release: () => {},
    partial: false,
    malformed: false,
    bulk: false,
  }
  await page.route('**/files/recursive-acls/history**', async (route) => {
    const url = new URL(route.request().url())
    state.requests.push({
      method: route.request().method(),
      query: Object.fromEntries(url.searchParams),
    })
    if (state.pause) {
      state.waiting = true
      await new Promise<void>((resolve) => {
        state.release = resolve
      })
      if (route.request().failure()) return
    }
    if (state.status !== 200)
      return route.fulfill({ status: state.status, json: { title: 'private upstream error' } })
    const location = {
      containerId: target.containerId,
      instanceId: target.instanceId,
      path: url.searchParams.get('path')!,
    }
    if (url.pathname.endsWith('/history')) {
      if (state.bulk) {
        const records = Array.from({ length: 26 }, (_, i) => ({
          ...summary,
          captureId: (i + 1).toString(16).padStart(32, '0'),
        }))
        const more = !!url.searchParams.get('after'),
          entries = more ? records.slice(25) : records.slice(0, 25)
        return route.fulfill({
          json: {
            location,
            version: 'C'.repeat(64),
            entries,
            nextAfter: more ? '' : records[24]!.captureId,
            scannedCount: entries.length,
            invalidCount: 0,
            complete: more,
          },
        })
      }

      const partial = state.partial && !url.searchParams.get('after')
      return route.fulfill({
        json: {
          location,
          version: 'C'.repeat(64),
          entries: partial ? [] : [summary],
          nextAfter: partial ? '9'.repeat(32) : '',
          scannedCount: 1,
          invalidCount: 0,
          complete: !partial,
        },
      })
    }
    const offset = Number(url.searchParams.get('offset')),
      last = offset === 1
    return route.fulfill({
      json: {
        location,
        summary: { ...summary, ...(state.malformed ? { historyDigest: 'E'.repeat(64) } : {}) },
        offset,
        entries: [
          {
            relativePath: last ? '' : '原始前像-长文件名.bin',
            kind: last ? 'Directory' : 'File',
            originalMode: 0,
            accessAcl: acl,
            defaultAcl: [],
          },
        ],
        complete: last,
        nextOffset: last ? null : 1,
      },
    })
  })
  async function open() {
    await page.goto('/containers')
    await page
      .locator('.container-card')
      .filter({ hasText: 'demo-web' })
      .getByRole('button', { name: '详情', exact: true })
      .click()
    await page.getByRole('button', { name: '容器文件', exact: true }).click()
    await page
      .getByRole('region', { name: '容器文件', exact: true })
      .getByRole('button', { name: '递归ACL历史 资料', exact: true })
      .click()
  }
  return { base, state, summary, open }
}
for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: paginated heterogeneous preimages and footer remain readable`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 600 })
      await page.emulateMedia({ colorScheme })
      const c = await setup(page)
      await c.open()
      await expect(modal(page).getByText('原计划2项 · 文件1 · 目录1 · 原计划变更0')).toBeVisible()
      await modal(page)
        .getByRole('button', { name: `查看逐项前像 ${c.summary.captureId}`, exact: true })
        .click()
      await expect(
        modal(page).getByRole('article', { name: '原ACL 原始前像-长文件名.bin', exact: true }),
      ).toContainText('权限 0')
      await expect(modal(page)).toContainText('第1–1项，共2项（仍有前像未读取）')
      await modal(page).getByRole('button', { name: '下一页前像', exact: true }).click()
      await expect(
        modal(page).getByRole('article', { name: '原ACL 根目录', exact: true }),
      ).toBeVisible()
      await expect(modal(page)).toContainText('到达末页')
      await expect(
        modal(page).getByRole('button', { name: '下一页前像', exact: true }),
      ).toHaveCount(0)
      await modal(page).getByRole('button', { name: '上一页前像', exact: true }).click()
      await expect(modal(page)).toContainText('仍有前像未读取')
      expect(c.state.requests.map((v) => v.query.offset)).toEqual([undefined, '0', '1', '0'])
      expect(c.state.requests.every((v) => v.method === 'GET')).toBe(true)
      expect(c.state.requests[1]!.query.expectedHistoryDigest).toBe(c.summary.historyDigest)
      expect(c.state.requests[1]!.query.take).toBe('25')
      await expect(modal(page).getByRole('button', { name: '关闭', exact: true })).toBeInViewport({
        ratio: 1,
      })
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      ).toBe(true)
      await page.screenshot({
        path: `../deploy/artifacts/ui-candidates/recursive-acl-history-web/history-${width}-${colorScheme}.png`,
      })
      await modal(page).getByRole('button', { name: '关闭', exact: true }).click()
      await expect(modal(page)).toHaveCount(0)
    })
test('empty incomplete inventory retains version and never claims empty history', async ({
  page,
}) => {
  const c = await setup(page)
  c.state.partial = true
  await c.open()
  await expect(modal(page)).toContainText('当前列表尚未完整')
  await expect(
    modal(page).getByText('此目录暂无可核实的递归ACL历史。', { exact: true }),
  ).toHaveCount(0)
  await modal(page).getByRole('button', { name: '继续扫描', exact: true }).click()
  expect(c.state.requests[1]!.query.after).toBe('9'.repeat(32))
  expect(c.state.requests[1]!.query.expectedVersion).toBe('C'.repeat(64))
})
for (const status of [401, 403, 404, 409, 429, 503])
  test(`${status} never becomes a successful empty inventory`, async ({ page }) => {
    const c = await setup(page)
    c.state.status = status
    await c.open()
    if ([401, 403].includes(status)) await expect(modal(page)).toHaveCount(0)
    else {
      await expect(modal(page).locator('.recursive-history__error')).toBeVisible()
      await expect(modal(page)).not.toContainText('private upstream error')
      await expect(modal(page)).not.toContainText('此目录暂无可核实的递归ACL历史。')
    }
  })
test('changed digest does not display unverified entries', async ({ page }) => {
  const c = await setup(page)
  await c.open()
  c.state.malformed = true
  await modal(page)
    .getByRole('button', { name: `查看逐项前像 ${c.summary.captureId}`, exact: true })
    .click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '无法核实' })).toBeVisible()
  await expect(modal(page).getByRole('article')).toHaveCount(0)
})
for (const reason of ['close', 'session', 'hidden'])
  test(`${reason}: delayed private replies cannot reopen the panel`, async ({ page }) => {
    const c = await setup(page)
    c.state.pause = true
    await c.open()
    await expect.poll(() => c.state.waiting).toBe(true)
    if (reason === 'close') {
      await expect(
        modal(page).getByRole('button', { name: '取消读取并关闭', exact: true }),
      ).toBeInViewport({ ratio: 1 })
      await modal(page).getByRole('button', { name: '取消读取并关闭', exact: true }).click()
    }
    if (reason === 'session')
      await page.evaluate(() =>
        window.dispatchEvent(
          new StorageEvent('storage', {
            key: 'whaledeck-session-ended',
            storageArea: localStorage,
            newValue: JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) }),
          }),
        ),
      )
    if (reason === 'hidden')
      await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, get: () => true })
        document.dispatchEvent(new Event('visibilitychange'))
      })
    await expect(modal(page)).toHaveCount(0)
    c.state.release()
    await page.waitForTimeout(200)
    await expect(modal(page)).toHaveCount(0)
  })
for (const reason of ['close', 'session', 'hidden'])
  test(`${reason}: delayed next entry page cannot restore cleared private details`, async ({
    page,
  }) => {
    const c = await setup(page)
    await c.open()
    await modal(page)
      .getByRole('button', { name: `查看逐项前像 ${c.summary.captureId}`, exact: true })
      .click()
    await expect(modal(page).getByRole('article')).toHaveCount(1)
    c.state.pause = true
    await modal(page).getByRole('button', { name: '下一页前像', exact: true }).click()
    await expect.poll(() => c.state.waiting).toBe(true)
    await expect(modal(page).getByRole('article')).toHaveCount(0)
    if (reason === 'close')
      await modal(page).getByRole('button', { name: '取消读取并关闭', exact: true }).click()
    if (reason === 'session')
      await page.evaluate(() =>
        window.dispatchEvent(
          new StorageEvent('storage', {
            key: 'whaledeck-session-ended',
            storageArea: localStorage,
            newValue: JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) }),
          }),
        ),
      )
    if (reason === 'hidden')
      await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, get: () => true })
        document.dispatchEvent(new Event('visibilitychange'))
      })
    await expect(modal(page)).toHaveCount(0)
    c.state.release()
    await page.waitForTimeout(200)
    await expect(modal(page)).toHaveCount(0)
    expect(c.state.requests).toHaveLength(3)
  })

test('loaded inventories retain all captures while rendering at most 25 cards', async ({
  page,
}) => {
  const c = await setup(page)
  c.state.bulk = true
  await c.open()
  await expect(modal(page).locator('.recursive-history__records > li')).toHaveCount(25)
  await modal(page).getByRole('button', { name: '继续扫描', exact: true }).click()
  await expect(modal(page).locator('.recursive-history__records > li')).toHaveCount(1)
  await modal(page).getByRole('button', { name: '上一页历史', exact: true }).click()
  await expect(modal(page).locator('.recursive-history__records > li')).toHaveCount(25)
  await modal(page).getByRole('button', { name: '下一页已读历史', exact: true }).click()
  await expect(modal(page).locator('.recursive-history__records > li')).toHaveCount(1)
  expect(c.state.requests).toHaveLength(2)
})
