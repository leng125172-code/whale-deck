import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerRecursivePermissionFixture } from './fixtures/container-recursive-permissions.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174', launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, ignoreDefaultArgs: ['--hide-scrollbars'] } })
const modal = (page: Page) => page.getByRole('dialog', { name: '递归修改容器目录权限', exact: true })
const writes = (control: Awaited<ReturnType<typeof installContainerRecursivePermissionFixture>>, suffix: string) => control.state.writes.filter((v: { path: string }) => v.path === suffix || suffix && v.path.endsWith(suffix))
async function inputModes(page: Page, fileMode = '0640', directoryMode = '0750') {
  await modal(page).getByLabel('文件模式', { exact: true }).fill(fileMode)
  await modal(page).getByLabel('目录模式', { exact: true }).fill(directoryMode)
}
async function files(page: Page) {
  await page.goto('/containers')
  await page.locator('.container-card').filter({ hasText: 'demo-web' }).getByRole('button', { name: '详情', exact: true }).click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const region = page.getByRole('region', { name: '容器文件', exact: true })
  await expect(region.getByRole('listitem')).toHaveCount(4)
  return region
}
async function prepare(page: Page, control: Awaited<ReturnType<typeof installContainerRecursivePermissionFixture>>, directory = true) {
  const region = await files(page)
  const name = directory ? '资料' : '资料'
  await region.getByRole('button', { name: `递归权限 ${name}`, exact: true }).click()
  await inputModes(page); await modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('等待预检', { exact: true })).toBeVisible()
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
}
async function fit(page: Page) {
  await expect.poll(async () => {
    const bounds = await modal(page).boundingBox()
    return bounds !== null && bounds.x >= 0 && bounds.x + bounds.width <= await page.evaluate(() => document.documentElement.clientWidth)
  }).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const button = await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).boundingBox()
  expect(button).not.toBeNull(); expect(button!.y + button!.height).toBeLessThanOrEqual(await page.evaluate(() => innerHeight))
  const cancel = await modal(page).getByRole('button', { name: '请求取消递归权限修改', exact: true }).boundingBox()
  expect(cancel).not.toBeNull(); expect(cancel!.y + cancel!.height).toBeLessThanOrEqual(await page.evaluate(() => innerHeight))
}
for (const width of [320, 1045, 1440]) for (const colorScheme of ['light', 'dark'] as const) for (const directory of [true]) {
  test(`${width} ${colorScheme} ${directory ? 'directory' : 'file'}: explicit plan and one execution`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 }); await page.emulateMedia({ colorScheme })
    const control = await installContainerRecursivePermissionFixture(page)
    await prepare(page, control, directory)
    expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
    await expect(modal(page)).toContainText('文件模式 0640 · 目录模式 0750')
    await fit(page)
    await page.screenshot({ path: `../deploy/artifacts/ui-candidates/container-recursive-permission/${width}-${colorScheme}-directory.png` })
    await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).click()
    await expect(modal(page).getByText('等待递归权限修改', { exact: true })).toBeVisible()
    await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
    await expect(modal(page).getByText('递归权限修改已完成', { exact: true })).toBeVisible()
    expect(writes(control, '/confirm')).toHaveLength(1)
    expect(writes(control, '/confirm')[0].key).toBe(control.id)
    expect(writes(control, '/confirm')[0].body).toEqual({ planHash: 'isolated-recursive-confirmation', confirmed: true, version: 1 })
    expect(control.row.executionJobId).toBe(control.execution)
    expect(control.base.state.contentBodies).toHaveLength(0)
    expect(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-recursive-permission-hints-v1'))).not.toContain('isolated-recursive-confirmation')
  })
}
for (const colorScheme of ['light', 'dark'] as const) test(`320x600 ${colorScheme}: preflight cancel stays in viewport and cancels only original intent`, async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 600 }); await page.emulateMedia({ colorScheme })
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control)
  await fit(page)
  const cancel = modal(page).getByRole('button', { name: '请求取消递归权限修改', exact: true })
  const box = await cancel.boundingBox()
  expect(box!.y).toBeGreaterThanOrEqual(0)
  await expect(cancel).toBeInViewport({ ratio: 1 })
  await cancel.click(); await page.getByRole('button', { name: '继续等待', exact: true }).click()
  expect(writes(control, '/cancel')).toHaveLength(0)
  await cancel.click(); await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect.poll(() => writes(control, '/cancel').length).toBe(1)
  expect(writes(control, '/cancel')[0].key).toBe(control.id)
  expect(writes(control, '/confirm')).toHaveLength(0)
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('已取消', { exact: true })).toBeVisible()
})
test('closing a new form sends nothing; lost registration reload uses the original key GET only', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { loseQueue: true })
  const region = await files(page)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click(); await modal(page).getByRole('button', { name: '返回', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click(); await inputModes(page); await modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
  await expect.poll(() => control.state.key).not.toBe('')
  const original = control.state.key
  await page.reload(); await files(page)
  await page.getByRole('button', { name: '查看递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
  expect(control.state.reads).toContain('/by-key/' + original)
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('lost confirmation cannot resend until queried and reload never confirms again', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { loseConfirm: true }); await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '原确认尚未核实' })).toBeVisible()
  await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toBeDisabled()
  await page.reload(); await files(page)
  await expect(page.getByRole('region', { name: '容器递归权限修改任务', exact: true })).toContainText('递归权限修改已完成')
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(1)
})
test('planning naturally polls after six seconds without additional writes', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); const region = await files(page)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click(); await inputModes(page); await modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible({ timeout: 10000 })
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('restart preserves original instance and disables old plan confirmation', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control)
  control.base.state.target.instanceId = '888:2026-10-28T01:02:03.123456789Z'
  await page.reload(); await files(page); await page.getByRole('button', { name: '查看递归权限修改任务', exact: true }).click()
  await expect(modal(page)).toContainText('容器实例已变化')
  await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toBeDisabled()
  expect(writes(control, '/confirm')).toHaveLength(0)
})
test('expired Ready never enables new confirmation', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control)
  control.row.state = 'Expired'; control.row.confirmation = null
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('预检计划已过期', { exact: true })).toBeVisible()
  await expect(modal(page)).toContainText('预检计划已过期，请查询原任务。')
  await expect(modal(page)).not.toContainText('核对原目录树清单与文件、目录模式后再确认')
  expect(writes(control, '/confirm')).toHaveLength(0)
})
for (const status of [401, 403, 404, 503]) test(`${status} cannot lose original reference or masquerade as empty success`, async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control); control.state.status = status
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  if ([401, 403].includes(status)) {
    await expect(modal(page)).toHaveCount(0)
    const stored = await page.evaluate(() => sessionStorage.getItem('whaledeck-container-recursive-permission-hints-v1'))
    if (status === 401) expect(stored).toBeNull()
    else expect(stored).not.toContain(control.id)
  } else {
    await expect(modal(page).getByRole('alert').filter({ hasText: status === 404 ? '尚未找到原登记' : '请求失败' })).toBeVisible()
    await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toBeDisabled()
    const count = control.state.writes.length
    await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click(); expect(control.state.writes).toHaveLength(count)
  }
})
test('active recursive permission cancellation requires explicit confirmation and keeps original intent', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); control.state.holdRecursive = true; await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).click(); await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('正在逐项修改权限', { exact: true })).toBeVisible()
  await modal(page).getByRole('button', { name: '请求取消递归权限修改', exact: true }).click()
  await page.getByRole('button', { name: '继续等待', exact: true }).click(); expect(writes(control, '/cancel')).toHaveLength(0)
  await modal(page).getByRole('button', { name: '请求取消递归权限修改', exact: true }).click(); await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
  await expect.poll(() => writes(control, '/cancel').length).toBe(1)
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByText('已取消', { exact: true })).toBeVisible(); expect(writes(control, '/confirm')).toHaveLength(1)
})
test('ordinary user never sends a recursive permission or recovery request', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { administrator: false }); await page.goto('/containers')
  await expect(page.getByRole('button', { name: /递归权限修改 / })).toHaveCount(0); expect(control.state.writes).toHaveLength(0); expect(control.state.reads).toHaveLength(0)
})
for (const unresolved of ['Unknown', 'Partial']) for (const height of [600, 800]) for (const colorScheme of ['light', 'dark'] as const) for (const lost of [false, true]) test(`current administrator ${unresolved} disposition preserves counts; height=${height} theme=${colorScheme} lost=${lost}`, async ({ page }) => {
  await page.setViewportSize({ width: 320, height }); await page.emulateMedia({ colorScheme })
  const control = await installContainerRecursivePermissionFixture(page, { filePage: true, loseAck: lost, unresolved })
  control.row.command.location.path = 'work/' + Array(5).fill('长目录'.repeat(20)).join('/') + '.bin'
  await page.goto('/files'); await page.getByRole('button', { name: '容器递归权限修改异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器递归权限修改异常处置', exact: true })
  await expect(recovery).toContainText(unresolved === 'Partial' ? '已验证留存' : '可能存在，尚未验证')
  const originalReceipt = structuredClone(control.row.receipt)
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click(); expect(control.state.writes).toHaveLength(0)
  await expect(recovery.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeInViewport({ ratio: 1 })
  await expect(recovery.getByRole('button', { name: '暂不处置', exact: true })).toBeInViewport({ ratio: 1 })
  await recovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  if (lost) {
    await expect(recovery).toContainText('处置响应尚未核实')
    await expect(recovery.getByRole('button', { name: '确认处置并释放保护', exact: true })).toBeDisabled()
    await expect(recovery.getByRole('button', { name: '查询原处置', exact: true })).toBeInViewport({ ratio: 1 })
    await recovery.getByRole('button', { name: '查询原处置', exact: true }).click()
  }
  await expect(recovery.getByRole('status').filter({ hasText: lost ? '已核实原处置回执' : '已处置部分修改或未知历史结果' })).toContainText('不代表整树修改成功')
  expect(control.state.writes).toHaveLength(1); expect(control.state.writes[0].body).toEqual({ version: 1, confirmed: true })
  expect(control.state.writes[0].key).toBe(control.id); expect(control.row.historyMayExist).toBe(true); expect(control.row.receipt).toEqual(originalReceipt)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})

test('a later Ready response cannot replace the original confirmation or enable a write', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control)
  control.row.version++
  control.row.confirmation!.planHash = 'replacement-confirmation'
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '不同的原计划' })).toBeVisible()
  await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toBeDisabled()
  expect(writes(control, '/confirm')).toHaveLength(0)
})
test('malformed completed proof keeps original execution protected and cannot offer stale confirmation', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).click()
  await expect(modal(page).getByText('等待递归权限修改', { exact: true })).toBeVisible()
  control.state.malformed = true
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '递归权限回执或HDD事实无法核实' })).toBeVisible()
  await expect(modal(page).getByText('递归权限修改已完成', { exact: true })).toHaveCount(0)
  await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toHaveCount(0)
  expect(writes(control, '/confirm')).toHaveLength(1)
})
test('blank and special-bit modes do not register a task', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page), region = await files(page)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click()
  const submit = modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true })
  await submit.click()
  await expect(modal(page).getByRole('alert').filter({ hasText: '请分别明确填写文件和目录' })).toBeVisible()
  await modal(page).getByLabel('文件模式', { exact: true }).fill('4755')
  await modal(page).getByLabel('目录模式', { exact: true }).fill('0750')
  await submit.click()
  expect(control.state.writes).toHaveLength(0)
  expect(await page.evaluate(() => sessionStorage.getItem('whaledeck-container-recursive-permission-hints-v1'))).toBeNull()
})
test('hidden page stops polling and resumes only the original GET', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page); control.state.holdPlan = true
  const region = await files(page)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click()
  await inputModes(page); await modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
  await expect(modal(page).getByText('等待预检', { exact: true })).toBeVisible()
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }); document.dispatchEvent(new Event('visibilitychange')) })
  const reads = control.state.reads.length; await page.waitForTimeout(6500); expect(control.state.reads).toHaveLength(reads)
  control.state.holdPlan = false
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => false }); document.dispatchEvent(new Event('visibilitychange')) })
  await expect(modal(page).getByText('预检已完成，尚未修改权限', { exact: true })).toBeVisible()
  expect(writes(control, '')).toHaveLength(1); expect(writes(control, '/confirm')).toHaveLength(0)
})
test('recovery list failure cannot be presented as no pending tasks', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { filePage: true }); control.state.status = 503
  await page.goto('/files'); await page.getByRole('button', { name: '容器递归权限修改异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器递归权限修改异常处置', exact: true })
  await expect(recovery.getByRole('alert').filter({ hasText: '请求失败' })).toBeVisible()
  await expect(recovery.getByText('暂无待处置的递归权限修改任务', { exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})


for (const unresolved of ['Partial','Unknown']) test(`${unresolved} stops auto polling, retains original execution and offers no retry`, async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { unresolved }); await prepare(page, control)
  await modal(page).getByRole('button', { name: '确认递归权限修改', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page)).toContainText(unresolved === 'Partial' ? '部分修改，保护已保留' : '结果未知，保护已保留')
  await expect(modal(page).getByRole('button', { name: '请求取消递归权限修改', exact: true })).toHaveCount(0)
  await expect(modal(page).getByRole('button', { name: '确认递归权限修改', exact: true })).toHaveCount(0)
  const reads = control.state.reads.length; await page.waitForTimeout(6500); expect(control.state.reads).toHaveLength(reads)
  expect(writes(control, '/confirm')).toHaveLength(1); expect(control.row.executionJobId).toBe(control.execution)
  await expect(modal(page).getByRole('link', { name: '前往递归权限修改异常处置', exact: true })).toBeVisible()
})

test('only directories expose recursion; sealed form keeps separate modes and interlocks other writers', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page)
  const region = await files(page)
  await expect(region.getByRole('button', { name: `递归权限 ${control.base.state.longName}`, exact: true })).toHaveCount(0)
  await region.getByRole('button', { name: '递归权限 资料', exact: true }).click()
  await inputModes(page, '0000', '0777')
  await modal(page).getByRole('button', { name: '生成递归权限修改预检', exact: true }).click()
  await modal(page).getByRole('button', { name: '查询原递归权限修改任务', exact: true }).click()
  await expect(modal(page).getByLabel('文件模式', { exact: true })).toBeDisabled()
  await expect(modal(page).getByLabel('目录模式', { exact: true })).toBeDisabled()
  await expect(region.getByRole('button', { name: '所有者 资料', exact: true })).toBeDisabled()
  await expect(region.getByRole('button', { name: `编辑 ${control.base.state.longName}`, exact: true })).toBeDisabled()
  expect(writes(control, '')[0].body.fileMode).toBe('0000')
  expect(writes(control, '')[0].body.directoryMode).toBe('0777')
  await expect(modal(page)).toContainText('文件模式 0000 · 目录模式 0777')
  expect(writes(control, '/confirm')).toHaveLength(0)
})

test('historical HDD verification remains separate from current availability and actual changes', async ({ page }) => {
  const control = await installContainerRecursivePermissionFixture(page, { filePage: true, unresolved: 'Partial' })
  control.row.receipt.historyAvailable = false
  await page.goto('/files'); await page.getByRole('button', { name: '容器递归权限修改异常', exact: true }).click()
  const recovery = page.getByRole('dialog', { name: '容器递归权限修改异常处置', exact: true })
  await expect(recovery).toContainText('HDD元数据不可用或未证明可用')
  await expect(recovery).toContainText('历史证明：已验证留存')
  expect(control.row.receipt.changedCount).toBe(1)
  await recovery.getByRole('button', { name: '处置历史结果', exact: true }).click()
  await recovery.getByRole('button', { name: '确认处置并释放保护', exact: true }).click()
  await expect(recovery).toContainText('不代表整树修改成功')
  expect(control.row.receipt.historyAvailable).toBe(false); expect(control.row.historyVerified).toBe(true)
  expect(control.row.receipt.changedCount).toBe(1)
})
