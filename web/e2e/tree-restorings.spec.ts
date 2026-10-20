import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installTreeRestoringFixture } from './fixtures/tree-restorings.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
type Control = Awaited<ReturnType<typeof installTreeRestoringFixture>>
const creates = (control: Control) => control.state.writes.filter((item) => item.path === '')
const confirms = (control: Control) =>
  control.state.writes.filter((item) => item.path.endsWith('/confirm'))
const dialog = (page: Page) => page.getByRole('dialog', { name: '从历史归档恢复目录', exact: true })
const catalogue = (page: Page) =>
  page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
async function open(page: Page) {
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await catalogue(page).getByRole('button', { name: '恢复目录', exact: true }).click()
  await expect(dialog(page).getByRole('button', { name: '生成计划', exact: true })).toBeVisible()
}
async function plan(page: Page) {
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未恢复', { exact: true })).toBeVisible()
}
async function fit(page: Page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
  const bounds = await dialog(page).locator('.el-dialog').boundingBox()
  expect(bounds!.x).toBeGreaterThanOrEqual(0)
  expect(bounds!.y).toBeGreaterThanOrEqual(0)
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(page.viewportSize()!.height)
}
for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: original-path restoring is planned then explicitly confirmed, reload is read-only`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTreeRestoringFixture(page)
      await page.goto('/files')
      await open(page)
      expect(control.state.writes).toHaveLength(0)
      await expect(
        dialog(page).getByRole('textbox', { name: '目标相对路径', exact: true }),
      ).toHaveValue(control.catalogue.records[0].source.source.path)
      await fit(page)
      await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
      await expect(dialog(page).getByText('计划已生成，尚未恢复', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(0)
      expect(creates(control)[0].body).toEqual({
        captureId: control.catalogue.records[0].id,
        target: control.catalogue.records[0].source.source,
      })
      expect(creates(control)[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
      expect(creates(control)[0].headers['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
      await expect(dialog(page).getByText('D'.repeat(64), { exact: true })).toBeVisible()
      await expect(
        dialog(page)
          .getByText(/1(?:\.0+)? GiB/)
          .first(),
      ).toBeVisible()
      await expect(dialog(page).locator('img')).toHaveCount(0)
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('计划已生成，尚未恢复', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      await dialog(page).getByRole('button', { name: '确认恢复', exact: true }).click()
      await expect(dialog(page).getByText('原目录恢复任务已完成', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(1)
      expect(control.row.executionJobId).not.toBe(control.row.planningJobId)
      expect(control.row.executionJobId).not.toBe(control.row.command.captureId)
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('原目录恢复任务已完成', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(1)
    })

for (const [option, reason] of [
  ['noSourceGrant', '独立目录恢复授权'],
  ['noTargetGrant', '没有具备目录恢复写入授权'],
] as const)
  test(`${option}: history does not imply a restoring grant`, async ({ page }) => {
    const control = await installTreeRestoringFixture(page, { [option]: true })
    await page.goto('/files')
    await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
    await expect(
      catalogue(page).getByRole('button', { name: '恢复目录', exact: true }),
    ).toBeDisabled()
    await expect(catalogue(page).getByText(reason, { exact: false })).toBeVisible()
    expect(control.state.writes).toHaveLength(0)
  })

test('a lost registration response recovers the original key only; missing is not a new POST', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(page).toHaveURL(/treeRestoreKey=/)
  await expect(dialog(page).getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  control.state.missing = true
  await page.reload()
  await expect(dialog(page).getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  control.state.missing = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未恢复', { exact: true })).toBeVisible()
  expect(control.state.reads).toContain('/by-key/' + control.state.key)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('a lost confirmation response is queried, never automatically reconfirmed', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page, {
    loseConfirmResponse: true,
    holdRestoring: true,
  })
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '确认恢复', exact: true }).click()
  await expect(dialog(page).getByText('确认响应尚未核实', { exact: false })).toBeVisible()
  await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toBeDisabled()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('等待恢复', { exact: true })).toBeVisible()
  await page.reload()
  await expect(dialog(page).getByText('等待恢复', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdRestoring = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('原目录恢复任务已完成', { exact: true })).toBeVisible()
})
test('320x800 unknown receipt retains visible controls; acknowledgment needs a second confirmation', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 800 })
  await page.emulateMedia({ colorScheme: 'dark' })
  const control = await installTreeRestoringFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '确认恢复', exact: true }).click()
  await expect(dialog(page).getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await fit(page)
  const action = await dialog(page)
    .getByRole('button', { name: '确认未知结果', exact: true })
    .boundingBox()
  expect(action!.y + action!.height).toBeLessThanOrEqual(800)
  await page.reload()
  await expect(dialog(page).getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(control.state.writes).toHaveLength(2)
  await dialog(page).getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(
    dialog(page).getByText('已接受未知历史结果，不代表成功', { exact: true }),
  ).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(1)
})
for (const field of ['archiveSha256', 'target', 'captureId'] as const)
  test(`${field} proof change freezes the existing confirmation without replacement writes`, async ({
    page,
  }) => {
    const control = await installTreeRestoringFixture(page)
    await page.goto('/files')
    await plan(page)
    if (field === 'archiveSha256') control.row.archiveSha256 = 'F'.repeat(64)
    else if (field === 'target') control.row.command.target.path = 'different'
    else control.row.command.captureId = control.catalogue.records[1].id
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(
      dialog(page)
        .getByRole('alert')
        .filter({ hasText: /已改变|证明不一致/ }),
    ).toBeVisible()
    await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toBeDisabled()
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(0)
  })
test('expiry disables confirmation and never automatically executes', async ({ page }) => {
  const control = await installTreeRestoringFixture(page)
  await page.goto('/files')
  await plan(page)
  control.row.confirmation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toBeDisabled()
  await expect(dialog(page).getByText('计划已过期', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('protected descendants reject the whole plan without executing', async ({ page }) => {
  const control = await installTreeRestoringFixture(page, { protectedPlan: true })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog(page).getByText('归档包含受保护成员，计划已拒绝。', { exact: true }),
  ).toBeVisible()
  await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})
for (const status of [401, 403])
  test(`${status}: rejected session clears metadata and stops polling`, async ({ page }) => {
    const control = await installTreeRestoringFixture(page)
    await page.goto('/files')
    await plan(page)
    control.state.status = status
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page)).toHaveCount(0)
    await expect(page.locator('.tree-restore-reference')).toHaveCount(0)
    const reads = control.state.reads.length
    await page.waitForTimeout(2200)
    expect(control.state.reads).toHaveLength(reads)
    expect(confirms(control)).toHaveLength(0)
  })
test('cancel is explicit, dismissal does nothing, closing a reference does not cancel', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page)
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(control.state.writes).toHaveLength(1)
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-restore-reference')
    .getByRole('button', { name: '关闭引用', exact: true })
    .click()
  await expect(page.locator('.tree-restore-reference')).toHaveCount(0)
  expect(control.state.writes).toHaveLength(2)
  expect(confirms(control)).toHaveLength(0)
})
test('target traversal is rejected locally and a second selected capture is not substituted for an active reference', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page)
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('textbox', { name: '目标相对路径', exact: true }).fill('../escape')
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog(page).getByText('请选择具备恢复授权', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await dialog(page)
    .getByRole('textbox', { name: '目标相对路径', exact: true })
    .fill('自定义<img src=x>.tar.gz')
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未恢复', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '收起', exact: true }).click()
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await catalogue(page).getByRole('button', { name: '下一页', exact: true }).click()
  await expect(catalogue(page).getByText('另一份目录', { exact: true })).toBeVisible()
  await expect(
    catalogue(page).getByRole('button', { name: '恢复目录', exact: true }),
  ).toBeDisabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('ordinary users cannot mount the catalogue or restore a referenced intent on reload', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page, { administrator: false })
  await page.goto('/files?treeRestoreId=' + control.row.id)
  await expect(dialog(page)).toHaveCount(0)
  await expect(page.getByRole('button', { name: '目录回收记录', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog(page)).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
  expect(control.catalogue.state.captureReads).toHaveLength(0)
})

test('a first plan that differs from the selected capture cannot be confirmed', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page)
  await page.goto('/files')
  await open(page)
  control.catalogue.records[0].archiveSha256 = 'F'.repeat(64)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog(page).getByText('原捕获与恢复计划的归档证明不一致', { exact: false }),
  ).toBeVisible()
  await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toHaveCount(0)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('malformed completion does not display success or keep a stale confirmation usable', async ({
  page,
}) => {
  const control = await installTreeRestoringFixture(page)
  await page.goto('/files')
  await plan(page)
  Object.assign(control.row, { state: 'Succeeded', publishedVersion: null, confirmation: null })
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(
    dialog(page).getByText('目录恢复响应或历史归档证明无法核实', { exact: false }),
  ).toBeVisible()
  await expect(dialog(page).getByText('原目录恢复任务已完成', { exact: true })).toHaveCount(0)
  await expect(dialog(page).getByRole('button', { name: '确认恢复', exact: true })).toBeDisabled()
  expect(confirms(control)).toHaveLength(0)
})
