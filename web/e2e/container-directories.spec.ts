import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'
import { installFileFixture } from './fixtures/files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

async function fixture(page: Page, lost = false) {
  const base = await installContainerFilesFixture(page)
  const state = {
    writes: [] as { path: string; body: Record<string, unknown>; key: string | undefined }[],
    ready: false,
    canceled: false,
    lost,
  }
  const command = {
    location: {
      containerId: base.state.target.containerId,
      instanceId: base.state.target.instanceId,
      path: 'work/页面新目录',
    },
    mode: '0750',
  }
  const id = 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12'
  const intent = {
    id,
    jobId: id,
    command,
    state: 'Pending',
    expiresAtUtc: new Date(Date.now() + 900000).toISOString(),
    resultCode: null as string | null,
    version: 0,
    publishedVersion: null as string | null,
  }
  await page.route('**/api/v1/containers/*/files/directories**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.split('/directories')[1] || ''
    const body = request.postDataJSON() as Record<string, unknown> | null
    if (request.method() === 'POST')
      state.writes.push({ path, body: body || {}, key: request.headers()['idempotency-key'] })
    if (path === '/plan') {
      command.location.path = String(body?.path)
      command.mode = String(body?.mode)
      return route.fulfill({
        json: {
          planHash: 'fixture-directory-plan',
          command,
          expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          warning: '仅新建，不覆盖当前目录',
        },
      })
    }
    if (path === '' && request.method() === 'POST') {
      expect(body).toEqual({
        planHash: 'fixture-directory-plan',
        confirmed: true,
        idempotencyKey: request.headers()['idempotency-key'],
      })
      intent.version = 1
      if (state.lost) {
        state.lost = false
        return route.abort('failed')
      }
      return route.fulfill({ status: 202, json: intent })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      return route.fulfill({ status: 202, json: { id, state: 'CancelRequested' } })
    }
    if (path === '/resolve' || path === '/' + id) {
      intent.version++
      if (state.canceled) {
        intent.state = 'Canceled'
        intent.resultCode = 'CONTAINER_DIRECTORY_CANCELED'
      } else if (state.ready) {
        intent.state = 'Succeeded'
        intent.resultCode = 'CONTAINER_DIRECTORY_CREATED'
        intent.publishedVersion = 'F'.repeat(64)
      }
      return route.fulfill({ json: intent })
    }
    return route.fulfill({ status: 404, json: { title: 'Unexpected directory request' } })
  })
  return { ...base, directory: state, intent }
}
async function open(page: Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const files = page.getByRole('region', { name: '容器文件', exact: true })
  await expect(files.getByRole('listitem')).toHaveCount(4)
  await files.getByLabel('容器内相对目录', { exact: true }).fill('work')
  await files.getByRole('button', { name: '前往', exact: true }).click()
  await expect(files.getByRole('listitem')).toHaveCount(1)
  return files.getByRole('region', { name: '容器新建目录', exact: true })
}
async function plan(page: Page, panel: ReturnType<Page['getByRole']>) {
  await panel.getByRole('button', { name: '新建目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '新建容器目录', exact: true })
  await dialog.getByLabel('目录名称', { exact: true }).fill('页面新目录')
  await dialog.getByRole('button', { name: '生成目录计划', exact: true }).click()
  await expect(dialog.getByText('仅新建，不覆盖当前目录', { exact: true })).toBeVisible()
  return dialog
}
for (const view of [
  { width: 320, colorScheme: 'dark' },
  { width: 1440, colorScheme: 'light' },
] as const) {
  test(`${view.width} ${view.colorScheme}: cancellation sends no intent; explicit confirmation sends one`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: view.width, height: 1000 })
    await page.emulateMedia({ colorScheme: view.colorScheme })
    const control = await fixture(page)
    const panel = await open(page)
    let dialog = await plan(page, panel)
    await dialog.getByRole('button', { name: '取消', exact: true }).click()
    expect(control.directory.writes.filter((w) => w.path === '')).toHaveLength(0)
    dialog = await plan(page, panel)
    await dialog.getByRole('button', { name: '确认创建目录', exact: true }).click()
    await expect(dialog.getByRole('status')).toHaveText('等待执行')
    await dialog.getByRole('button', { name: '关闭', exact: true }).click()
    await expect(dialog).toBeHidden()
    control.directory.ready = true
    await panel.getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(panel.getByRole('status')).toContainText('目录已创建')
    expect(control.directory.writes.filter((w) => w.path === '')).toHaveLength(1)
    expect(control.directory.writes.filter((w) => w.path === '/plan')).toHaveLength(2)
    expect(control.directory.writes.filter((w) => w.path === '/plan')[1]?.body).toEqual({
      instanceId: control.state.target.instanceId,
      path: 'work/页面新目录',
      mode: '0750',
    })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}
test('lost registration response is resolved and refreshed without another create', async ({
  page,
}) => {
  const control = await fixture(page, true)
  let panel = await open(page)
  const dialog = await plan(page, panel)
  await dialog.getByRole('button', { name: '确认创建目录', exact: true }).click()
  await expect(dialog.getByRole('alert').filter({ hasText: '登记结果待核实' })).toBeVisible()
  await dialog.getByRole('button', { name: '关闭', exact: true }).click()
  await expect(dialog).toBeHidden()
  await panel.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(panel.getByRole('status')).toContainText('等待执行')
  panel = await open(page)
  await expect(panel.getByRole('status')).toContainText('等待执行')
  control.directory.ready = true
  await panel.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(panel.getByRole('status')).toContainText('目录已创建')
  expect(control.directory.writes.filter((w) => w.path === '')).toHaveLength(1)
  expect(control.directory.writes.filter((w) => w.path === '/resolve')).toHaveLength(1)
})
test('cancel requests only the existing task and never creates another directory', async ({
  page,
}) => {
  const control = await fixture(page)
  const panel = await open(page)
  const dialog = await plan(page, panel)
  await dialog.getByRole('button', { name: '确认创建目录', exact: true }).click()
  await expect(dialog.getByRole('status')).toHaveText('等待执行')
  await dialog.getByRole('button', { name: '关闭', exact: true }).click()
  await expect(dialog).toBeHidden()
  await panel.getByRole('button', { name: '请求取消', exact: true }).click()
  await expect(panel.getByRole('status')).toContainText('已取消')
  expect(control.directory.writes.filter((w) => w.path === '')).toHaveLength(1)
  expect(control.directory.writes.filter((w) => w.path.endsWith('/cancel'))[0]?.key).toBe(
    control.intent.id,
  )
})

async function recoveryFixture(
  page: Page,
  options: { lost?: boolean; denied?: number; administrator?: boolean } = {},
) {
  await installFileFixture(page, { administrator: options.administrator ?? true })
  const row = {
    id: 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
    jobId: 'ac12ac12-ac12-4c12-ac12-ac12ac12ac12',
    command: {
      location: {
        containerId: 'a'.repeat(64),
        instanceId: '777:2026-10-22T01:02:03.123456789Z',
        path: 'work/历史未知目录',
      },
      mode: '0750',
    },
    state: 'Unknown',
    version: 7,
    resultCode: 'CONTAINER_DIRECTORY_RESULT_UNKNOWN',
    publishedVersion: null,
    expiresAtUtc: new Date(Date.now() + 900000).toISOString(),
  }
  const state = {
    writes: [] as { body: unknown; key: string | undefined }[],
    reads: [] as string[],
  }
  await page.route('**/api/v1/files/container-directories/recovery**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname.split('/recovery')[1] || ''
    if (request.method() === 'POST') {
      state.writes.push({ body: request.postDataJSON(), key: request.headers()['idempotency-key'] })
      if (options.denied)
        return route.fulfill({
          status: options.denied,
          json: { status: options.denied, title: 'Access revoked' },
        })
      row.state = 'Acknowledged'
      row.version++
      row.resultCode = 'CONTAINER_DIRECTORY_UNKNOWN_ACKNOWLEDGED'
      if (options.lost) return route.abort('failed')
      return route.fulfill({ status: 202, json: { id: row.id, state: row.state } })
    }
    state.reads.push(path)
    return route.fulfill({ json: path ? row : [row] })
  })
  await page.goto('/files')
  return { state, row }
}
for (const width of [320, 1440]) {
  test(`${width}: directory recovery requires explicit confirmation and resolves lost acknowledgement read-only`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 })
    const control = await recoveryFixture(page, { lost: true })
    await page.getByRole('button', { name: '容器目录异常', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '容器目录异常处置', exact: true })
    await dialog.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await dialog.getByRole('button', { name: '暂不处置', exact: true }).click()
    expect(control.state.writes).toHaveLength(0)
    await dialog.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
    await expect(dialog.getByRole('alert').filter({ hasText: '未自动重试确认' })).toBeVisible()
    expect(control.state.writes).toEqual([
      { body: { version: 7, confirmed: true }, key: control.row.id },
    ])
    await dialog.getByRole('button', { name: '查询原处置', exact: true }).click()
    await expect(dialog.getByRole('status')).toContainText('已核实原处置回执；不代表目录创建成功')
    expect(control.state.writes).toHaveLength(1)
    expect(control.state.reads).toEqual(['', '/' + control.row.id])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}
for (const status of [401, 403]) {
  test(`directory recovery clears its actionable selection after ${status}`, async ({ page }) => {
    const control = await recoveryFixture(page, { denied: status })
    await page.getByRole('button', { name: '容器目录异常', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '容器目录异常处置', exact: true })
    await dialog.getByRole('button', { name: '处置历史结果', exact: true }).click()
    await dialog.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
    await expect(dialog).toBeHidden()
    await expect(page.getByRole('button', { name: '确认处置并释放保护', exact: true })).toHaveCount(
      0,
    )
    expect(control.state.writes).toHaveLength(1)
  })
}
test('ordinary users do not load administrator directory recovery', async ({ page }) => {
  const control = await recoveryFixture(page, { administrator: false })
  await expect(page.getByRole('button', { name: '容器目录异常', exact: true })).toHaveCount(0)
  expect(control.state.reads).toEqual([])
  expect(control.state.writes).toEqual([])
})
