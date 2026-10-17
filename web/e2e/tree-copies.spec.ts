import { expect, test } from '@playwright/test'
import { installTreeCopyFixture } from './fixtures/tree-copies.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
const creates = (control) => control.state.writes.filter((item) => item.path === '')
const confirms = (control) => control.state.writes.filter((item) => item.path.endsWith('/confirm'))
async function plan(page: import('@playwright/test').Page) {
  await page.getByRole('button', { name: '复制目录', exact: true }).click()
  await page
    .getByRole('dialog', { name: '复制目录', exact: true })
    .getByRole('button', { name: '生成计划', exact: true })
    .click()
}
for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: full scan is not copy; explicit confirmation produces a distinct completed execution`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTreeCopyFixture(page, {
        directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
      })
      await page.goto('/files')
      await plan(page)
      const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
      await expect(dialog.getByText('计划已生成，尚未复制', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(0)
      await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
      const box = await dialog.locator('.el-dialog').boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      expect(box!.y).toBeGreaterThanOrEqual(0)
      expect(box!.y + box!.height).toBeLessThanOrEqual(1000)
      await page.screenshot({ path: `test-results/tree-copy-ready-${width}-${colorScheme}.png` })
      await dialog.getByRole('button', { name: '确认复制', exact: true }).click()
      await expect(dialog.getByText('原复制任务已完成', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(1)
      expect(control.row.executionJobId).not.toBe(control.row.planningJobId)
      expect(confirms(control)[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
      expect(confirms(control)[0].headers['idempotency-key']).toBe(control.row.id)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await page.screenshot({ path: `test-results/tree-copy-complete-${width}-${colorScheme}.png` })
    })
  }
test('reload restores an unconfirmed original plan using GET only', async ({ page }) => {
  const control = await installTreeCopyFixture(page)
  await page.goto('/files')
  await plan(page)
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未复制', { exact: true }),
  ).toBeVisible()
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '确认复制', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(page.url()).not.toContain('planHash')
})
test('lost registration response survives reload by read-only original-key lookup, never another POST', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await plan(page)
  await expect(page).toHaveURL(/treeCopyKey=/)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未复制', { exact: true }),
  ).toBeVisible()
  expect(control.state.reads.some((path) => path.startsWith('/by-key/'))).toBe(true)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('a missing original reference never silently creates a replacement', async ({ page }) => {
  const control = await installTreeCopyFixture(page)
  control.state.missing = true
  await page.goto('/files?treeCopyKey=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await expect(page.getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await page.getByRole('dialog').getByRole('button', { name: '查询原任务', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
})
test('cancel an in-progress plan fences confirmation without treating a scan as execution', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page, { holdPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByText('正在验证完整目录', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog.getByText('已取消', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('unknown result requires explicit separate acknowledgment and never becomes success', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认复制', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(dialog.getByText('已确认未知结果，不代表成功', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('ordinary user initial deep link and reload issue no directory workflow requests', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page, { administrator: false })
  await page.goto('/files?treeCopyId=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await page.reload()
  await expect(page.getByRole('button', { name: '复制目录', exact: true })).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})

test('lost confirmation response recovers the existing execution without another write', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page, { loseConfirmResponse: true, holdCopy: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认复制', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('等待复制', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog.getByText('等待复制', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdCopy = false
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原复制任务已完成', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('expired confirmation is disabled and never executes automatically', async ({ page }) => {
  const control = await installTreeCopyFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
  control.row.confirmation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeDisabled()
  await expect(dialog.getByText('计划已过期', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('permission rejection clears task metadata and stops background reads', async ({ page }) => {
  const control = await installTreeCopyFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
  control.state.status = 403
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('.tree-copy-reference')).toHaveCount(0)
  const reads = control.state.reads.length
  await page.waitForTimeout(2200)
  expect(control.state.reads).toHaveLength(reads)
  expect(confirms(control)).toHaveLength(0)
})
test('closing a retained reference does not cancel or confirm the original plan', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认复制', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-copy-reference')
    .getByRole('button', { name: '关闭引用', exact: true })
    .click()
  await page.getByRole('button', { name: '仅关闭引用', exact: true }).click()
  await expect(page.locator('.tree-copy-reference')).toHaveCount(0)
  expect(control.state.writes).toHaveLength(1)
})

test('target path input rejects traversal before registration and preserves literal names', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page)
  await page.goto('/files')
  await page.getByRole('button', { name: '复制目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '复制目录', exact: true })
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill('../escape')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog.getByText('请选择允许写入的目标根和合法相对路径', { exact: true }),
  ).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await dialog
    .getByRole('textbox', { name: '目标相对路径', exact: true })
    .fill('自定义<img src=x>副本')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog.getByText('计划已生成，尚未复制', { exact: true })).toBeVisible()
  expect(control.row.command.target.path).toBe('自定义<img src=x>副本')
  await expect(dialog.locator('img')).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})
test('ordinary mutation permission never exposes directory copy without its independent root action', async ({
  page,
}) => {
  const control = await installTreeCopyFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-tree-copy')
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '复制目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})
