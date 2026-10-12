import { expect, test, type Page } from '@playwright/test'

const containers = [
  {
    id: 'core',
    name: 'whaledeck-worker',
    image: 'whaledeck-worker:dev',
    state: 'running',
    status: 'Up 1 hour',
    isProtected: true,
    protectionLevel: 'ControlPlane',
    labels: {},
    ports: [],
    addresses: [],
  },
  {
    id: 'demo',
    name: 'demo-web',
    image: 'nginx:alpine',
    state: 'running',
    status: 'Up 5 minutes',
    isProtected: false,
    protectionLevel: 'Managed',
    labels: {},
    ports: [],
    addresses: [],
  },
  {
    id: 'stopped',
    name: 'stopped-web',
    image: 'nginx:alpine',
    state: 'exited',
    status: 'Exited',
    isProtected: false,
    protectionLevel: 'Managed',
    labels: {},
    ports: [],
    addresses: [],
  },
]

function usage() {
  return {
    items: containers.map((container) => ({
      containerId: container.id,
      state: container.state,
      sampledAtUtc: new Date().toISOString(),
      cpuPercent: container.id === 'core' ? 125.3 : null,
      cpuWindowSeconds: 6,
      memoryUsedBytes: 104857600,
      memoryLimitBytes: 1073741824,
      memoryPercent: 9.76,
      networkReceiveBytes: 1048576,
      networkSendBytes: 2 * 1048576,
      blockReadBytes: 0,
      blockWriteBytes: 1048576,
      pids: 6,
      quality: container.id === 'core' ? 'Good' : 'Warmup',
    })),
    sampledAtUtc: new Date().toISOString(),
    isStale: false,
    isTruncated: false,
    skippedCount: 0,
    totalCount: containers.length,
    intervalSeconds: 6,
  }
}
async function setup(page: Page) {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) =>
    route.fulfill({
      json: new URL(route.request().url()).pathname.endsWith('/containers/usage')
        ? usage()
        : new URL(route.request().url()).pathname.endsWith('/containers')
          ? containers
          : [],
    }),
  )
}
async function refreshOption(page: Page, label: string) {
  await page
    .getByRole('combobox', { name: '容器自动刷新', exact: true })
    .locator('xpath=ancestor::div[contains(@class,"el-select__wrapper")]')
    .click()
  await page.getByRole('option', { name: label, exact: true }).click()
}
for (const width of [320, 760, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: populated card/table usage and refresh controls fit`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await setup(page)
      await page.goto('/containers')
      const core = page
        .locator('.container-card')
        .filter({ has: page.getByText('whaledeck-worker', { exact: true }) })
      await expect(core.locator('.container-usage')).toContainText('125.30%')
      await expect(core.locator('.container-usage')).toContainText('100.0 MiB / 1.0 GiB')
      await expect(core.getByRole('button')).toHaveCount(1)
      await expect(
        page.locator('.container-card').filter({ hasText: 'demo-web' }).locator('.container-usage'),
      ).toContainText('预热')
      await expect(
        page
          .locator('.container-card')
          .filter({ hasText: 'stopped-web' })
          .locator('.container-usage'),
      ).toContainText('未运行')
      const bounds = await page
        .locator('.page-heading__actions > *, .container-card__usage')
        .evaluateAll((elements) =>
          elements.map((element) => ({
            left: element.getBoundingClientRect().left,
            right: element.getBoundingClientRect().right,
          })),
        )
      expect(bounds.every((rect) => rect.left >= 0 && rect.right <= width)).toBe(true)
      await page.screenshot({
        path: `test-results/container-usage-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      await core.locator('.container-usage__values').focus()
      await expect(page.getByText('累计接收 / 发送：1.0 MiB / 2.0 MiB')).toBeVisible()
      await page.keyboard.press('Escape')
      for (const label of ['不自动刷新', '每 30 秒刷新', '每 60 秒刷新', '每 6 秒刷新'])
        await refreshOption(page, label)
      await page.locator('.container-filters .el-radio-button').filter({ hasText: '列表' }).click()
      await expect(page.locator('.container-table')).toContainText('125.30%')
      await expect
        .poll(() =>
          page.evaluate(
            () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
          ),
        )
        .toBeLessThanOrEqual(1)
      await page.screenshot({
        path: `test-results/container-usage-table-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      expect(errors).toEqual([])
    })
  }

test('batch refresh is silent, nonoverlapping, pausable and clears on forbidden', async ({
  page,
}) => {
  await setup(page)
  let requests = 0
  let pending = false
  let maximum = 0
  let active = 0
  let forbidden = false
  await page.route('**/api/v1/containers/usage', async (route) => {
    requests++
    active++
    maximum = Math.max(maximum, active)
    try {
      if (pending) await new Promise((resolve) => setTimeout(resolve, 8000))
      await route.fulfill(
        forbidden ? { status: 403, json: { title: 'Forbidden' } } : { json: usage() },
      )
    } catch {
      /* Deliberate cancellation on page exit. */
    } finally {
      active--
    }
  })
  await page.goto('/containers')
  await expect(page.locator('.container-card')).toHaveCount(3)
  pending = true
  await expect.poll(() => requests, { timeout: 8000 }).toBe(2)
  await expect(page.locator('.container-grid > .el-loading-mask')).toHaveCount(0)
  await page.waitForTimeout(6500)
  expect(requests).toBe(2)
  expect(maximum).toBe(1)
  pending = false
  await refreshOption(page, '不自动刷新')
  await page.waitForTimeout(2000)
  const paused = requests
  await page.waitForTimeout(6500)
  expect(requests).toBe(paused)
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect.poll(() => requests).toBe(paused + 1)
  forbidden = true
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.locator('.container-card')).toHaveCount(0)
  await expect(page.locator('.container-usage')).toHaveCount(0)
})

test('failed sampling stays explicit and leaving the page cancels polling', async ({ page }) => {
  await setup(page)
  let calls = 0
  await page.route('**/api/v1/containers/usage', (route) => {
    calls++
    return route.fulfill({ status: 503, json: { title: 'Unavailable' } })
  })
  await page.goto('/containers')
  await expect(page.getByText('容器资源采样暂不可用，旧采样不作为实时数据展示。')).toBeVisible()
  await expect(page.locator('.container-card').first().locator('.container-usage')).toContainText(
    '采样已过期',
  )
  await page.getByText('工作站概览', { exact: true }).click()
  const before = calls
  await page.waitForTimeout(6500)
  expect(calls).toBe(before)
})
