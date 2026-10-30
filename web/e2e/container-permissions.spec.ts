import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerPermissionFixture } from './fixtures/container-permissions.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174', launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, ignoreDefaultArgs: ['--hide-scrollbars'] } })
const modal = (page: Page) => page.getByRole('dialog', { name: '修改容器文件或目录权限', exact: true })
const writes = (control: Awaited<ReturnType<typeof installContainerPermissionFixture>>, suffix: string) => control.state.writes.filter((v: { path: string }) => v.path === suffix || suffix && v.path.endsWith(suffix))
async function files(page: Page) {
  await page.goto('/containers')
  await page.locator('.container-card').filter({ hasText: 'demo-web' }).getByRole('button', { name: '详情', exact: true }).click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const region = page.getByRole('region', { name: '容器文件', exact: true })
  await expect(region.getByRole('listitem')).toHaveCount(4)
  return region
}
async function prepare(page: Page, control: Awaited<ReturnType<typeof installContainerPermissionFixture>>, directory = false) {
  const region = await files(page)
  const name = directory ? '资料' : control.base.state.longName
  await region.getByRole('button', { name: `权限 ${name}`, exact: true }).click()
  await modal(page).getByLabel('普通权限模式', { exact: true }).fill('0600')
  await modal(page).getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('等待预检', { exact: true })).toBeVisible()
  await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
}
async function fit(page: Page) {
  await expect.poll(async () => {
    const bounds = await modal(page).boundingBox()
    return bounds !== null && bounds.x >= 0 && bounds.x + bounds.width <= await page.evaluate(() => document.documentElement.clientWidth)
  }).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const button = await modal(page).getByRole('button', { name: '确认权限修改', exact: true }).boundingBox()
  expect(button).not.toBeNull(); expect(button!.y + button!.height).toBeLessThanOrEqual(await page.evaluate(() => innerHeight))
}
for (const width of [320, 1045, 1440]) for (const colorScheme of ['light', 'dark'] as const) for (const directory of [false, true]) {
  test(`${width} ${colorScheme} ${directory ? 'directory' : 'file'}: explicit plan and one execution`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 }); await page.emulateMedia({ colorScheme })
    const control = await installContainerPermissionFixture(page)
    await prepare(page, control, directory)
    expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
    await expect(modal(page)).toContainText('原权限 0640 → 0600')
    await fit(page)
    await page.screenshot({ path: `test-results/container-permission-${width}-${colorScheme}-${directory ? 'directory' : 'file'}.png` })
    await modal(page).getByRole('button', { name: '确认权限修改', exact: true }).click()
    await expect(modal(page).getByText('等待权限修改', { exact: true })).toBeVisible()
    await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
    await expect(modal(page).getByText('权限修改已完成', { exact: true })).toBeVisible()
    expect(writes(control, '/confirm')).toHaveLength(1)
    expect(writes(control, '/confirm')[0].key).toBe(control.id)
    expect(writes(control, '/confirm')[0].body).toEqual({ planHash: 'isolated-permission-confirmation', confirmed: true, version: 1 })
    expect(control.row.executionJobId).toBe(control.execution)
    expect(control.base.state.contentBodies).toHaveLength(0)
    expect(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-permission-hints-v1'))).not.toContain('isolated-permission-confirmation')
  })
}
test('closing a new form sends nothing; lost registration reload uses the original key GET only', async ({ page }) => {
  const control = await installContainerPermissionFixture(page, { loseQueue: true })
  const region = await files(page)
  await region.getByRole('button', { name: '权限 资料', exact: true }).click(); await modal(page).getByRole('button', { name: '返回', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
  await region.getByRole('button', { name: '权限 资料', exact: true }).click(); await modal(page).getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  await expect.poll(() => control.state.key).not.toBe('')
  const original = control.state.key
  await page.reload(); await files(page)
  await page.getByRole('button', { name: '查看权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
  expect(control.state.reads).toContain('/by-key/' + original)
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('lost confirmation cannot resend until queried and reload never confirms again', async ({ page }) => {
  const control = await installContainerPermissionFixture(page, { loseConfirm: true }); await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认权限修改', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '原确认尚未核实' })).toBeVisible()
  await expect(modal(page).getByRole('button', { name: '确认权限修改', exact: true })).toBeDisabled()
  await page.reload(); await files(page)
  await expect(page.getByRole('region', { name: '容器权限修改任务', exact: true })).toContainText('权限修改已完成')
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(1)
})
test('planning naturally polls after six seconds without additional writes', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); const region = await files(page)
  await region.getByRole('button', { name: '权限 资料', exact: true }).click(); await modal(page).getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 10000 })
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('restart preserves original instance and disables old plan confirmation', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); await prepare(page, control)
  control.base.state.target.instanceId = '888:2026-10-28T01:02:03.123456789Z'
  await page.reload(); await files(page); await page.getByRole('button', { name: '查看权限修改任务', exact: true }).click()
  await expect(modal(page)).toContainText('容器实例已变化')
  await expect(modal(page).getByRole('button', { name: '确认权限修改', exact: true })).toBeDisabled()
  expect(writes(control, '/confirm')).toHaveLength(0)
})
test('expired Ready never enables new confirmation', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); await prepare(page, control)
  control.row.state = 'Expired'; control.row.confirmation = null
  await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检计划已过期', { exact: true })).toBeVisible()
  expect(writes(control, '/confirm')).toHaveLength(0)
})
for (const status of [403, 404, 503]) test(`${status} cannot lose original reference or masquerade as empty success`, async ({ page }) => {
  const control = await installContainerPermissionFixture(page); await prepare(page, control); control.state.status = status
  await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  if (status === 403) {
    await expect(modal(page)).toHaveCount(0)
    expect(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-permission-hints-v1'))).not.toContain(control.id)
  } else {
    await expect(modal(page).getByRole('alert').filter({ hasText: status === 404 ? '尚未找到原登记' : '请求失败' })).toBeVisible()
    await expect(modal(page).getByRole('button', { name: '确认权限修改', exact: true })).toBeDisabled()
    const count = control.state.writes.length
    await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click(); expect(control.state.writes).toHaveLength(count)
  }
})
test('active permission cancellation requires explicit confirmation and keeps original intent', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); control.state.holdPermission = true; await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认权限修改', exact: true }).click(); await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('正在修改权限', { exact: true })).toBeVisible()
  await modal(page).getByRole('button', { name: '请求取消权限修改', exact: true }).click()
  await page.getByRole('button', { name: '继续等待', exact: true }).click(); expect(writes(control, '/cancel')).toHaveLength(0)
  await modal(page).getByRole('button', { name: '请求取消权限修改', exact: true }).click(); await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect.poll(() => writes(control, '/cancel').length).toBe(1)
  await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('已取消', { exact: true })).toBeVisible(); expect(writes(control, '/confirm')).toHaveLength(1)
})
test('ordinary user never sends a permission or recovery request', async ({ page }) => {
  const control = await installContainerPermissionFixture(page, { administrator: false }); await page.goto('/containers')
  await expect(page.getByRole('button', { name: /权限修改 / })).toHaveCount(0); expect(control.state.writes).toHaveLength(0); expect(control.state.reads).toHaveLength(0)
})
for (const lost of [false, true]) test(`current administrator Unknown disposition keeps metadata uncertainty; lost=${lost}`, async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  const control = await installContainerPermissionFixture(page, { filePage: true, loseAck: lost })
  await page.goto('/files'); await page.getByRole('button', { name: '容器权限修改异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器权限修改异常处置', exact: true })
  await expect(recovery).toContainText('可能存在HDD元数据')
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click(); expect(control.state.writes).toHaveLength(0)
  await recovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  if (lost) {
    await expect(recovery).toContainText('处置响应尚未核实')
    await expect(recovery.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeDisabled()
    await recovery.getByRole('button', { name: '查询原处置', exact: true }).click()
  }
  await expect(recovery.getByRole('status').filter({ hasText: lost ? '已核实原处置回执' : '已处置历史未知结果' })).toContainText('不代表权限修改成功')
  expect(control.state.writes).toHaveLength(1); expect(control.state.writes[0].body).toEqual({ version: 1, confirmed: true })
  expect(control.state.writes[0].key).toBe(control.id); expect(control.row.historyMayExist).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})

test('malformed completed proof keeps original execution protected and cannot offer stale confirmation', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认权限修改', exact: true }).click()
  await expect(modal(page).getByText('等待权限修改', { exact: true })).toBeVisible()
  control.state.malformed = true
  await modal(page).getByRole('button', { name: '查询原权限修改任务', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '权限结果或HDD历史事实无法核实' })).toBeVisible()
  await expect(modal(page).getByText('权限修改已完成', { exact: true })).toHaveCount(0)
  await expect(modal(page).getByRole('button', { name: '确认权限修改', exact: true })).toHaveCount(0)
  expect(writes(control, '/confirm')).toHaveLength(1)
})
test('special modes are refused without registering or storing a replacement intent', async ({ page }) => {
  const control = await installContainerPermissionFixture(page), region = await files(page)
  await region.getByRole('button', { name: '权限 资料', exact: true }).click()
  await modal(page).getByLabel('普通权限模式', { exact: true }).fill('4755')
  await modal(page).getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '四位八进制普通权限' })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  expect(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-permission-hints-v1'))).toBeNull()
})
test('hidden page stops polling and resumes only the original GET', async ({ page }) => {
  const control = await installContainerPermissionFixture(page); control.state.holdPlan = true
  const region = await files(page)
  await region.getByRole('button', { name: '权限 资料', exact: true }).click()
  await modal(page).getByRole('button', { name: '生成权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('等待预检', { exact: true })).toBeVisible()
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }); document.dispatchEvent(new Event('visibilitychange')) })
  const reads = control.state.reads.length; await page.waitForTimeout(6500); expect(control.state.reads).toHaveLength(reads)
  control.state.holdPlan = false
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => false }); document.dispatchEvent(new Event('visibilitychange')) })
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('recovery list failure cannot be presented as no pending tasks', async ({ page }) => {
  const control = await installContainerPermissionFixture(page, { filePage: true }); control.state.status = 503
  await page.goto('/files'); await page.getByRole('button', { name: '容器权限修改异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器权限修改异常处置', exact: true })
  await expect(recovery.getByRole('alert').filter({ hasText: '请求失败' })).toBeVisible()
  await expect(recovery.getByText('暂无待处置的权限修改任务', { exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})
