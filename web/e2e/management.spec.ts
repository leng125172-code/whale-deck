import { expect, test } from '@playwright/test'

const action =
  'exec_start: dotnet WhaleDeck.Worker.dll --healthcheck ' + 'long-argument-'.repeat(15)
const core = {
  id: 'core',
  name: 'whaledeck-worker',
  image: 'whaledeck-worker:dev',
  state: 'running',
  status: 'Up 2 hours (healthy)',
  isProtected: true,
  protectionLevel: 'ControlPlane',
  labels: { 'com.docker.compose.project': 'whaledeck' },
  ports: [],
  addresses: ['172.19.0.4'],
}
const ordinary = {
  ...core,
  id: 'demo',
  name: 'demo-web',
  isProtected: false,
  labels: { 'com.docker.compose.project': 'whaledeck-app-demo' },
  ports: ['0.0.0.0:8088 → 8080/tcp'],
}
const fixtures: Record<string, unknown> = {
  'containers/usage': {
    items: [],
    sampledAtUtc: new Date().toISOString(),
    isStale: false,
    isTruncated: false,
    skippedCount: 0,
    totalCount: 2,
    intervalSeconds: 6,
  },
  containers: [core, ordinary],
  'containers/core/inspect': {
    id: 'core',
    name: core.name,
    image: core.image,
    environmentNames: [],
    command: [],
    entrypoint: [],
    labels: {},
    ports: [],
    volumes: [],
    networks: ['whaledeck'],
    restartPolicy: 'unless-stopped',
    cpus: 1,
    memoryMb: 512,
    healthCommand: [],
    healthIntervalSeconds: 30,
    healthTimeoutSeconds: 5,
    healthRetries: 3,
  },
  'containers/core/logs': { lines: ['worker ready'], truncated: false },
  'containers/core/stats': { values: {} },
  'containers/update-history': [],
  'containers/events': [
    {
      fingerprint: 'event-1',
      occurredAtUtc: '2026-10-12T08:00:00Z',
      eventType: 'container',
      action,
      image: core.image,
    },
  ],
  applications: [
    {
      id: 'compose:whaledeck',
      name: 'whaledeck',
      type: 'ComposeProject',
      state: 'Running',
      version: core.image,
      isProtected: true,
      attributes: { containerCount: '3' },
    },
    {
      id: 'application:demo',
      name: 'demo',
      type: 'ComposeApplication',
      state: 'Running',
      version: 'nginxinc/nginx-unprivileged:alpine',
      isProtected: false,
      attributes: { composeProject: 'whaledeck-app-demo', containerCount: '1' },
    },
  ],
  'applications/popular': {
    applications: [
      {
        id: 'demo',
        name: 'Demo Web',
        image: 'nginxinc/nginx-unprivileged:alpine',
        version: 'alpine',
        description: '轻量 Web 应用',
        installed: true,
      },
    ],
    isStale: false,
  },
  'applications/installations': [],
}

for (const width of [1440, 320]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: management controls and full event actions`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      await page.route('**/api/v1/**', (route) =>
        route.fulfill({
          json: fixtures[new URL(route.request().url()).pathname.replace('/api/v1/', '')] ?? [],
        }),
      )
      await page.goto('/applications')
      await expect(page.locator('.installed-row')).toHaveCount(2)
      const protectedCard = page.locator('.installed-row').filter({ hasText: '受保护' })
      await expect(protectedCard.getByRole('button')).toHaveCount(1)
      await page.getByRole('textbox', { name: '搜索应用' }).fill('missing')
      await expect(page.getByText('没有匹配的应用')).toBeVisible()
      await page.getByRole('textbox', { name: '搜索应用' }).fill('')
      await page.locator('.app-toolbar .el-radio-button').filter({ hasText: '热门目录' }).click()
      await expect(page.locator('.catalog-card')).toHaveCount(1)
      await page.locator('.app-toolbar .el-radio-button').filter({ hasText: '待更新' }).click()
      await expect(page.getByText('暂无已确认的版本变更')).toBeVisible()
      await page.locator('.app-toolbar .el-radio-button').filter({ hasText: '已安装' }).click()
      await page.screenshot({ path: `test-results/apps-${width}-${colorScheme}.png`, scale: 'css' })
      await page
        .locator('.installed-row')
        .filter({ hasText: 'demo' })
        .getByRole('button', { name: '关联容器', exact: true })
        .click()
      await expect(page.locator('.container-card')).toHaveCount(1)
      await page.goto('/containers')
      await expect(page.locator('.container-card')).toHaveCount(2)
      await expect(
        page
          .locator('.container-card')
          .filter({ has: page.getByText(core.name, { exact: true }) })
          .getByRole('button'),
      ).toHaveCount(1)
      await page.getByRole('textbox', { name: '搜索容器' }).fill('demo-web')
      await expect(page.locator('.container-card')).toHaveCount(1)
      await page.getByRole('textbox', { name: '搜索容器' }).fill('')
      await page.locator('.container-filters .el-radio-button').filter({ hasText: '列表' }).click()
      await expect(page.locator('.container-table .el-table__body-wrapper tr')).toHaveCount(2)
      await page.screenshot({
        path: `test-results/container-list-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      await expect
        .poll(() =>
          page.evaluate(
            () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
          ),
        )
        .toBeLessThanOrEqual(1)
      await page.locator('.container-filters .el-radio-button').filter({ hasText: '卡片' }).click()
      await page
        .locator('.container-card')
        .filter({ has: page.getByText(core.name, { exact: true }) })
        .getByRole('button', { name: '详情', exact: true })
        .click()
      const actionButton = page.locator('.docker-event-action')
      const details = await page.locator('.container-detail').boundingBox()
      const actionBounds = await page
        .locator('.container-detail__actions button')
        .evaluateAll((elements) =>
          elements.map((element) => ({
            left: element.getBoundingClientRect().left,
            right: element.getBoundingClientRect().right,
          })),
        )
      expect(actionBounds.every((bounds) => bounds.left >= 0 && bounds.right <= width)).toBe(true)
      expect(details!.x).toBeGreaterThanOrEqual(0)
      expect(details!.x + details!.width).toBeLessThanOrEqual(width)
      const labels = await page
        .locator('.container-detail .el-descriptions__label')
        .evaluateAll((elements) =>
          elements.map((element) => ({
            x: element.getBoundingClientRect().x,
            width: element.getBoundingClientRect().width,
          })),
        )
      expect(labels.every((label) => label.x >= 0 && label.width >= 60)).toBe(true)
      await actionButton.scrollIntoViewIfNeeded()
      await expect(actionButton).toHaveAttribute('title', action)
      expect(await actionButton.evaluate((element) => getComputedStyle(element).textOverflow)).toBe(
        'ellipsis',
      )
      await actionButton.click()
      await expect(page.locator('.docker-event-action-full')).toHaveText(action)
      const popup = await page.locator('.el-popover:visible').boundingBox()
      expect(popup!.x).toBeGreaterThanOrEqual(0)
      expect(popup!.x + popup!.width).toBeLessThanOrEqual(width)
      await page.screenshot({
        path: `test-results/events-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      expect(errors).toEqual([])
    })
  }
}
