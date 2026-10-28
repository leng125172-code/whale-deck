import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installTreeRecyclingFixture } from './fixtures/tree-recyclings.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
const creates = (control: Awaited<ReturnType<typeof installTreeRecyclingFixture>>) =>
  control.state.writes.filter((item) => item.path === '')
const confirms = (control: Awaited<ReturnType<typeof installTreeRecyclingFixture>>) =>
  control.state.writes.filter((item) => item.path.endsWith('/confirm'))
const dialog = (page: Page) =>
  page.getByRole('dialog', { name: '回收目录 · 先验证 HDD 归档', exact: true })
async function open(page: Page) {
  await page.getByRole('button', { name: '回收目录', exact: true }).click()
}
async function plan(page: Page) {
  await open(page)
  await dialog(page).getByRole('button', { name: '生成回收计划', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未回收', { exact: true })).toBeVisible()
}
async function fit(page: Page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
}
for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: scan then explicit recycle keeps full proof, units and readonly reload`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTreeRecyclingFixture(page, {
        directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
      })
      await page.goto('/files')
      await open(page)
      expect(control.state.writes).toHaveLength(0)
      await fit(page)
      await dialog(page).getByRole('button', { name: '生成回收计划', exact: true }).click()
      await expect(dialog(page).getByText('计划已生成，尚未回收', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(0)
      await expect(dialog(page).getByText(/1(?:\.0+)? GiB/)).toBeVisible()
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('计划已生成，尚未回收', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      await dialog(page).getByRole('button', { name: '确认回收目录', exact: true }).click()
      await expect(dialog(page).getByText('原目录回收任务已完成', { exact: true })).toBeVisible()
      await expect(dialog(page).getByText('原任务已移除源目录路径', { exact: true })).toBeVisible()
      await expect(dialog(page).getByText('原件已私有保留', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(1)
      expect(control.row.executionJobId).not.toBe(control.row.planningJobId)
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('原目录回收任务已完成', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(1)
      expect(creates(control)).toHaveLength(1)
    })
    test(`${width} ${colorScheme}: capture catalogue queries whole root, pagination and escaped dense proof`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTreeRecyclingFixture(page)
      await page.goto('/files')
      await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
      const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
      await expect(panel.getByText('归档曾验证', { exact: true })).toBeVisible()
      expect(control.state.captureReads[0].path).toBe('')
      expect(control.state.writes).toHaveLength(0)
      await panel.getByText('查看捕获证明', { exact: true }).click()
      await expect(panel.getByText('归档 SHA256', { exact: true })).toBeVisible()
      await expect(panel.locator('img[src=x]')).toHaveCount(0)
      await fit(page)
      await panel.getByRole('button', { name: '下一页', exact: true }).click()
      await expect(panel.getByText('另一份目录', { exact: true })).toBeVisible()
      expect(control.state.captureReads[control.state.captureReads.length - 1].after).toBe(
        control.records[0].id,
      )
      await panel.getByRole('button', { name: '上一页', exact: true }).click()
      await expect(panel.getByText('归档曾验证', { exact: true })).toBeVisible()
      expect(
        control.state.captureReads[control.state.captureReads.length - 1].after,
      ).toBeUndefined()
      await panel
        .getByRole('textbox', { name: '捕获记录相对路径', exact: true })
        .fill('不存在/目录')
      await panel.getByRole('button', { name: '查询记录', exact: true }).click()
      await expect(panel.getByText('此范围暂无已验证的目录捕获记录')).toBeVisible()
      expect(control.state.captureReads[control.state.captureReads.length - 1]).toMatchObject({
        path: '不存在/目录',
      })
      expect(
        control.state.captureReads[control.state.captureReads.length - 1].after,
      ).toBeUndefined()
      expect(control.state.writes).toHaveLength(0)
      await fit(page)
    })
  }
test('unknown receipt does not show false absence; acknowledgment never retries recycle', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '确认回收目录', exact: true }).click()
  await expect(dialog(page).getByText('未知，不能推断未发生', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(
    dialog(page).getByText('已接受未知历史结果，不代表成功', { exact: true }),
  ).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  expect(creates(control)).toHaveLength(1)
})
test('lost registration and confirmation responses recover by readonly original references', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page, {
    loseCreateResponse: true,
    loseConfirmResponse: true,
  })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成回收计划', exact: true }).click()
  await expect(page).toHaveURL(/treeTrashKey=/)
  const originalKey = new URL(page.url()).searchParams.get('treeTrashKey')
  // The original key is deliberately retained before sending the POST.
  // Exercise a lost response AFTER registration, not a reload before receipt.
  await expect.poll(() => control.state.key).toBe(originalKey)
  await page.reload()
  await expect(dialog(page).getByText('计划已生成，尚未回收', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认回收目录', exact: true }).click()
  await expect(dialog(page).getByText('确认响应尚未核实', { exact: false })).toBeVisible()
  await expect(
    dialog(page).getByRole('button', { name: '确认回收目录', exact: true }),
  ).toBeDisabled()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('原目录回收任务已完成', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(1)
  expect(creates(control)[0].headers['idempotency-key']).toBe(originalKey)
})
test('changed plan refuses confirmation and counterfeit catalogue proof is not rendered', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page)
  await page.goto('/files')
  await plan(page)
  control.row.totalBytes++
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('原目录清单已改变', { exact: false })).toBeVisible()
  await expect(
    dialog(page).getByRole('button', { name: '确认回收目录', exact: true }),
  ).toBeDisabled()
  expect(confirms(control)).toHaveLength(0)
  await dialog(page).getByRole('button', { name: '收起', exact: true }).click()
  control.state.captureBad = true
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
  await expect(panel.getByText('目录捕获证明或作用域无法核实', { exact: true })).toBeVisible()
  await expect(panel.getByText('归档曾验证', { exact: true })).toHaveCount(0)
})
test('ordinary user creates no directory recycle or catalogue requests', async ({ page }) => {
  const control = await installTreeRecyclingFixture(page, { administrator: false })
  await page.goto('/files?treeTrashId=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})
test('missing root grant hides directory recycle action', async ({ page }) => {
  const control = await installTreeRecyclingFixture(page, { noGrant: true })
  await page.goto('/files')
  await expect(page.getByTestId('files-page')).toBeVisible()
  await expect(page.getByRole('button', { name: '回收目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('cancel requires explicit second confirmation and never recycles the ready source', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page)
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(control.state.writes.filter((item) => item.path.endsWith('/cancel'))).toHaveLength(0)
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  expect(control.state.writes.filter((item) => item.path.endsWith('/cancel'))).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  await page.reload()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
})

for (const status of [404, 503])
  test(`${status} preserves the original reference and recovers without creating another intent`, async ({
    page,
  }) => {
    const control = await installTreeRecyclingFixture(page)
    await page.goto('/files')
    await plan(page)
    control.state.status = status
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page).locator('.el-alert--error')).toBeVisible()
    await expect(page).toHaveURL(/treeTrashId=/)
    control.state.status = 200
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page).locator('.el-alert--error')).toHaveCount(0)
    await expect(
      dialog(page).getByRole('button', { name: '确认回收目录', exact: true }),
    ).toBeEnabled()
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(0)
  })

test('403 clears the recycle dialog and stops workflow polling', async ({ page }) => {
  const control = await installTreeRecyclingFixture(page)
  await page.goto('/files')
  await plan(page)
  control.state.status = 403
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page)).toHaveCount(0)
  await expect(page.locator('.tree-trash-reference')).toHaveCount(0)
  const reads = control.state.reads.length
  await page.waitForTimeout(2200)
  expect(control.state.reads).toHaveLength(reads)
  expect(confirms(control)).toHaveLength(0)
})

test('read-only catalogue failure can retry without submitting any mutation', async ({ page }) => {
  const control = await installTreeRecyclingFixture(page)
  control.state.status = 503
  await page.goto('/files')
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
  await expect(panel.locator('.el-alert--error')).toBeVisible()
  control.state.status = 200
  await panel.getByRole('button', { name: '查询记录', exact: true }).click()
  await expect(panel.getByText('归档曾验证', { exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
})

test('planning and recycling intermediate states survive reload without repeating writes', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page, { holdPlan: true, holdRecycling: true })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成回收计划', exact: true }).click()
  await expect(dialog(page).getByText('正在验证完整目录', { exact: true })).toBeVisible()
  await page.reload()
  await expect(dialog(page).getByText('正在验证完整目录', { exact: true })).toBeVisible()
  control.state.holdPlan = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未回收', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认回收目录', exact: true }).click()
  await expect(dialog(page).getByText('等待回收', { exact: true })).toBeVisible()
  await page.reload()
  await expect(dialog(page).getByText('等待回收', { exact: true })).toBeVisible()
  control.state.holdRecycling = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('原目录回收任务已完成', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(1)
})

test('closing catalogue invalidates a delayed response before reopening a different query', async ({
  page,
}) => {
  const control = await installTreeRecyclingFixture(page)
  let release: (() => void) | undefined
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let started = false
  await page.route(
    '**/api/v1/files/tree-recyclings/captures?**',
    async (route) => {
      started = true
      await held
      await route
        .fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [control.records[0]], next: null }),
        })
        .catch(() => {})
    },
    { times: 1 },
  )
  await page.goto('/files')
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect.poll(() => started).toBe(true)
  const panel = page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
  await panel.getByRole('button', { name: '关闭', exact: true }).click()
  await expect(panel).toHaveCount(0)
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(panel.getByText('归档曾验证', { exact: true })).toBeVisible()
  await panel.getByRole('textbox', { name: '捕获记录相对路径', exact: true }).fill('不存在/目录')
  await panel.getByRole('button', { name: '查询记录', exact: true }).click()
  await expect(panel.getByText('此范围暂无已验证的目录捕获记录')).toBeVisible()
  release!()
  await page.waitForTimeout(100)
  await expect(panel.getByText('归档曾验证', { exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})
