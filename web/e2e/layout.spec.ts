import { expect, test, type Page } from '@playwright/test'

const now = '2026-10-12T03:00:00Z'
const longName = 'database-platform-' + 'long-resource-name-'.repeat(8)
const resource = {
  id: 'database-platform.postgres',
  name: longName,
  type: 'Database',
  state: 'Running',
  version: '18.6',
  isProtected: true,
  protectionLevel: 'Core',
  attributes: {},
}

for (const width of [1100, 390]) {
  test(`${width}px: empty and dependency-error states also fit`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.addInitScript(() =>
      sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
    )
    let failed = false
    await page.route('**/api/v1/**', (route) =>
      route.fulfill(
        failed
          ? {
              status: 503,
              json: {
                title: '服务暂时不可用',
                code: 'DEPENDENCY_UNAVAILABLE',
                traceId: 'layout-trace',
              },
            }
          : { json: [] },
      ),
    )
    await page.goto('/host/logs')
    await expect(page.locator('.el-table__empty-text')).toBeVisible()
    await expectViewportFit(page)
    failed = true
    await page.getByRole('button', { name: '查询', exact: true }).click()
    await expect(page.getByRole('alert')).toContainText('服务暂时不可用')
    await expectViewportFit(page)
  })
}
const fixtures: Record<string, unknown> = {
  'host/journal': [
    {
      occurredAtUtc: now,
      priority: '6',
      unit: 'whaledeck-agent.service',
      process: 'WhaleDeck.Agent',
      processId: '42',
      message: 'long-message-'.repeat(60),
    },
  ],
  audit: [
    {
      id: 'audit-1',
      actorSubject: longName,
      action: 'containers.restart',
      resourceId: longName,
      result: 'Accepted',
      traceId: 'layout-trace',
      jobId: 'layout-job',
      createdAtUtc: now,
    },
  ],
  containers: [
    {
      ...resource,
      id: 'container-1',
      type: 'Container',
      state: 'running',
      image: 'registry.invalid/team/' + longName + ':1.0',
      status: 'Up 2 hours',
      isProtected: false,
      labels: {},
    },
  ],
  databases: [resource, { ...resource, id: 'database-platform.valkey', name: 'Valkey' }],
  images: [{ ...resource, type: 'Image', id: 'sha256:layout-image' }],
  'host/network-interfaces': [
    {
      ...resource,
      id: 'eth0',
      name: 'eth0',
      attributes: { isVirtual: 'false', addresses: '192.168.100.13', speed: '1000000000' },
    },
  ],
  'host/storage-devices': [
    {
      ...resource,
      id: 'nvme0n1',
      name: 'nvme0n1',
      attributes: { model: longName, mountPoints: '/data', sizeBytes: '1000000000000' },
    },
  ],
  users: [{ ...resource, type: 'User' }],
  jobs: [
    {
      id: 'layout-job',
      jobType: 'containers.restart',
      resourceId: longName,
      state: 'Succeeded',
      phase: 'Completed',
      progressPercent: 100,
      createdAtUtc: now,
    },
  ],
  overview: { scope: 'Public', sampledAtUtc: now, platformStatus: 'Online', portals: [] },
  settings: [],
}

const pages = [
  ['系统日志', '/host/logs'],
  ['审计日志', '/audit'],
  ['镜像', '/docker/images'],
  ['数据库', '/databases'],
  ['容器', '/containers'],
  ['主机设备', '/host/resources'],
  ['任务中心', '/jobs'],
  ['身份用户', '/identity/users'],
  ['平台设置', '/settings'],
] as const

async function navigate(page: Page, label: string) {
  const menuItem = page.locator('.side-menu').getByRole('menuitem', { name: label, exact: true })
  if ((page.viewportSize()?.width || 0) <= 1045) {
    await page.locator('.navigation-trigger').click()
  }
  await menuItem.click()
  await expect(page.locator('.navigation-backdrop')).toHaveCount(0)
}

async function expectViewportFit(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(() => {
        const doc = document.documentElement
        return doc.scrollWidth - doc.clientWidth
      }),
    )
    .toBeLessThanOrEqual(1)
  const bounds = await page.locator('.page-content > *').evaluateAll((elements) =>
    elements.map((element) => {
      const rect = element.getBoundingClientRect()
      return { left: rect.left, right: rect.right, viewport: document.documentElement.clientWidth }
    }),
  )
  for (const rect of bounds) {
    expect(rect.left).toBeGreaterThanOrEqual(0)
    expect(rect.right).toBeLessThanOrEqual(rect.viewport + 1)
  }
  const controls = page.locator('.topbar button:visible, .topbar .el-switch')
  for (const control of await controls.all()) {
    const box = await control.boundingBox()
    if (box)
      expect(box.x + box.width).toBeLessThanOrEqual(
        await page.evaluate(() => document.documentElement.clientWidth),
      )
  }
}

for (const width of [1920, 1280, 1100, 1046, 1045, 900, 760, 600, 390, 320]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: page changes keep overflow inside cards`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      await page.route('**/api/v1/**', (route) => {
        const endpoint = new URL(route.request().url()).pathname.replace('/api/v1/', '')
        return route.fulfill({ json: fixtures[endpoint] ?? [] })
      })
      await page.goto('/host/logs')
      await expect(page.locator('.app-shell')).toBeVisible()
      await expect
        .poll(() => page.locator('.app-shell').evaluate((el) => getComputedStyle(el).filter))
        .toBe('none')
      await expect(page.locator('.el-table__body-wrapper tr')).toHaveCount(1)
      await expectViewportFit(page)
      // The column widths remain readable, with local scrolling on narrow screens.
      if (width <= 1280) {
        const table = page.locator('.logs-panel .el-table__body-wrapper .el-scrollbar__wrap')
        expect(await table.evaluate((el) => el.scrollWidth > el.clientWidth)).toBe(true)
        await table.hover()
        await page.mouse.wheel(500, 0)
        await expect.poll(() => table.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0)
        await expectViewportFit(page)
      }
      for (const [label, path] of pages.slice(1)) {
        await navigate(page, label)
        await expect(page).toHaveURL(path)
        await expectViewportFit(page)
      }
      await navigate(page, '系统日志')
      await page.getByPlaceholder('筛选消息关键字').fill('a'.repeat(160))
      await expectViewportFit(page)
      await page.screenshot({
        path: `test-results/layout-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      await page.getByRole('button', { name: '搜索平台功能' }).click()
      await expect(page.getByRole('dialog')).toBeVisible()
      await expectViewportFit(page)
    })
  }
}
