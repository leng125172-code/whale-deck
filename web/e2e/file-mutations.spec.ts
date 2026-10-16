import { expect, test } from '@playwright/test'
import { installMutationFixture } from './fixtures/file-mutations.mjs'

for (const width of [320, 1045, 1440]) for (const colorScheme of ['light', 'dark'] as const) {
  test(`${width} ${colorScheme}: mutation form, exact plan confirmation and async result fit viewport`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 }); await page.emulateMedia({ colorScheme })
    const control = await installMutationFixture(page)
    await page.goto('/files'); await page.getByRole('button', { name: '新建目录', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await page.getByLabel('操作目标相对路径', { exact: true }).fill('../unsafe'); await expect(dialog.getByRole('button', { name: '生成操作计划' })).toBeDisabled()
    await page.getByLabel('操作目标相对路径', { exact: true }).fill('测试目录'); await page.getByLabel('操作权限模式').fill('4755')
    await expect(dialog.getByRole('button', { name: '生成操作计划' })).toBeDisabled(); await page.getByLabel('操作权限模式').fill('0750')
    await dialog.getByRole('button', { name: '生成操作计划' }).click()
    await expect(dialog.getByText('测试目录', { exact: false })).toBeVisible()
    expect(control.mutation.writes.filter(item => item.path === '')).toHaveLength(0)
    await page.screenshot({ path: `test-results/file-mutation-plan-${width}-${colorScheme}.png` })
    const box = await dialog.boundingBox(); expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(width)
    await dialog.getByRole('button', { name: '确认并执行', exact: true }).click()
    await expect(page.getByTestId('file-mutation-status').getByText('已完成', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '测试目录', exact: true })).toBeVisible()
    expect(control.mutation.writes.filter(item => item.path === '')).toHaveLength(1)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    await page.screenshot({ path: `test-results/file-mutation-result-${width}-${colorScheme}.png` })
  })
}
for (const kind of ['copy', 'move', 'chmod'] as const) {
  test(`${kind}: row menu freezes source/version and shows a compact human result`, async ({ page }) => {
    const control = await installMutationFixture(page); await page.goto('/files')
    await page.getByRole('button', { name: `更多文件操作 ${control.entry.name}`, exact: true }).click()
    await page.getByRole('menuitem', { name: { copy: '复制文件', move: '移动文件', chmod: '修改权限' }[kind], exact: true }).click()
    if (kind !== 'chmod') await page.getByLabel('操作目标相对路径').fill('目标文件.txt')
    else await page.getByLabel('操作权限模式').fill('0640')
    const dialog = page.getByRole('dialog'); await dialog.getByRole('button', { name: '生成操作计划' }).click()
    await expect(dialog.getByRole('button', { name: '确认并执行' })).toBeVisible()
    const plan = control.mutation.writes.find(item => item.path === '/plan')!.body
    expect(plan.kind).toBe(kind); expect(plan.source.path).toBe(control.entry.name); expect(plan.expectedVersion).toBe(control.entry.version)
    await dialog.getByRole('button', { name: '确认并执行' }).click(); await expect(page.getByTestId('file-mutation-status').getByText('已完成', { exact: true })).toBeVisible()
    if (kind === 'move') await expect(page.getByTestId('file-mutation-status').getByText('完整原文件已保留', { exact: false })).toBeVisible()
    if (kind === 'chmod') await expect(page.getByRole('row').filter({ hasText: control.entry.name }).getByRole('cell', { name: '0640', exact: true })).toBeVisible()
  })
}
test('lost registration response retries the same sealed request and key', async ({ page }) => {
  const control = await installMutationFixture(page, { mutationLoseResponse: true }); await page.goto('/files')
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click(); await page.getByRole('button', { name: '确认并执行' }).click()
  await page.getByRole('button', { name: '重试原操作登记' }).click(); await expect(page.getByTestId('file-mutation-status').getByText('已完成', { exact: true })).toBeVisible()
  const creates = control.mutation.writes.filter(item => item.path === '')
  expect(creates).toHaveLength(2); expect(creates[0].body).toEqual(creates[1].body); expect(creates[0].headers['idempotency-key']).toBe(creates[1].headers['idempotency-key'])
})
test('pending cancellation waits for a receipt instead of treating 202 as terminal', async ({ page }) => {
  const control = await installMutationFixture(page, { mutationHold: true }); await page.goto('/files')
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click(); await page.getByRole('button', { name: '确认并执行' }).click()
  await expect(page.getByText('等待执行', { exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeDisabled()
  await expect(page).toHaveURL(new RegExp(`operationId=${control.operation.id}`)); await page.reload()
  await expect(page.getByText('等待执行', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '请求取消操作' }).click(); await expect(page.getByText('已取消', { exact: true })).toBeVisible()
  expect(control.mutation.writes.filter(item => item.path === '')).toHaveLength(1)
})
test('expired unregistered frozen request can be resolved without a replacement mutation', async ({ page }) => {
  const control = await installMutationFixture(page, { registrationNeverAccepted: true }); await page.goto('/files')
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click(); await page.getByRole('button', { name: '确认并执行' }).click()
  await expect(page.getByRole('button', { name: '重试原操作登记' })).toBeVisible()
  await page.getByRole('button', { name: '查询原登记（到期未登记可释放）' }).click()
  await expect(page.getByTestId('file-mutation-status')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeEnabled()
  expect(control.mutation.writes.filter(item => item.path === '')).toHaveLength(1)
})
test('unknown stays locked and is acknowledged as uncertainty, never success', async ({ page }) => {
  const control = await installMutationFixture(page, { mutationUnknown: true }); await page.goto('/files')
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click(); await page.getByRole('button', { name: '确认并执行' }).click()
  await expect(page.getByText('结果未知，等待人工核实', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '查询原操作' }).click(); expect(control.mutation.writes.filter(item => item.path === '')).toHaveLength(1)
  await page.getByRole('button', { name: '人工确认未知结果', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '保留待调查' }).click()
  expect(control.mutation.writes.some(item => item.path.endsWith('/acknowledge'))).toBe(false)
  await page.getByRole('button', { name: '人工确认未知结果', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: '已核实，接受未知结果' }).click()
  await expect(page.getByText('已人工确认未知结果（不代表成功）', { exact: true })).toBeVisible()
})
test('plan conflict can be canceled, and permission loss clears mutation metadata', async ({ page }) => {
  const control = await installMutationFixture(page); control.mutation.planStatus = 409; await page.goto('/files')
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click()
  await expect(page.getByRole('dialog').getByText('版本、路径锁或任务状态已变化，请刷新并核实原任务', { exact: true })).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click()
  control.mutation.planStatus = 200; control.mutation.hold = true
  await page.getByRole('button', { name: '新建目录', exact: true }).click(); await page.getByRole('button', { name: '生成操作计划' }).click(); await page.getByRole('button', { name: '确认并执行' }).click()
  await expect(page.getByText('等待执行', { exact: true })).toBeVisible(); control.mutation.status = 403
  await page.getByRole('button', { name: '查询原操作' }).click(); await expect(page.getByTestId('file-mutation-status')).toHaveCount(0)
})
