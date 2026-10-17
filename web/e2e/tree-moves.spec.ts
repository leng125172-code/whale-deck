import { expect, test } from '@playwright/test'
import { installTreeMoveFixture } from './fixtures/tree-moves.mjs'

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
  await page.getByRole('button', { name: '移动目录', exact: true }).click()
  await page
    .getByRole('dialog', { name: '移动目录', exact: true })
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
      const control = await installTreeMoveFixture(page, {
        directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
      })
      await page.goto('/files')
      await plan(page)
      const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
      await expect(dialog.getByText('计划已生成，尚未移动', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(0)
      await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
      const box = await dialog.locator('.el-dialog').boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      expect(box!.y).toBeGreaterThanOrEqual(0)
      expect(box!.y + box!.height).toBeLessThanOrEqual(1000)
      await page.screenshot({ path: `test-results/tree-move-ready-${width}-${colorScheme}.png` })
      await dialog.getByRole('button', { name: '确认移动', exact: true }).click()
      await expect(dialog.getByText('原移动任务已完成', { exact: true })).toBeVisible()
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
      await page.screenshot({ path: `test-results/tree-move-complete-${width}-${colorScheme}.png` })
    })
  }
test('reload restores an unconfirmed original plan using GET only', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未移动', { exact: true }),
  ).toBeVisible()
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '确认移动', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(page.url()).not.toContain('planHash')
})
test('lost registration response survives reload by read-only original-key lookup, never another POST', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await plan(page)
  await expect(page).toHaveURL(/treeMoveKey=/)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未移动', { exact: true }),
  ).toBeVisible()
  expect(control.state.reads.some((path) => path.startsWith('/by-key/'))).toBe(true)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('a missing original reference never silently creates a replacement', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  control.state.missing = true
  await page.goto('/files?treeMoveKey=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await expect(page.getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await page.getByRole('dialog').getByRole('button', { name: '查询原任务', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
})
test('cancel an in-progress plan fences confirmation without treating a scan as execution', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page, { holdPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByText('正在验证完整目录', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog.getByText('已取消', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('unknown result requires explicit separate acknowledgment and never becomes success', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认移动', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(dialog.getByText('已确认未知结果，不代表成功', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('ordinary user initial deep link and reload issue no directory workflow requests', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page, { administrator: false })
  await page.goto('/files?treeMoveId=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await page.reload()
  await expect(page.getByRole('button', { name: '移动目录', exact: true })).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})

test('lost confirmation response recovers the existing execution without another write', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page, { loseConfirmResponse: true, holdMove: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认移动', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('等待移动', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog.getByText('等待移动', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdMove = false
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原移动任务已完成', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('expired confirmation is disabled and never executes automatically', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  control.row.confirmation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeDisabled()
  await expect(dialog.getByText('计划已过期', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('permission rejection clears task metadata and stops background reads', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  control.state.status = 403
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('.tree-move-reference')).toHaveCount(0)
  const reads = control.state.reads.length
  await page.waitForTimeout(2200)
  expect(control.state.reads).toHaveLength(reads)
  expect(confirms(control)).toHaveLength(0)
})
test('closing a retained reference does not cancel or confirm the original plan', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-move-reference')
    .getByRole('button', { name: '关闭引用', exact: true })
    .click()
  await page.getByRole('button', { name: '仅关闭引用', exact: true }).click()
  await expect(page.locator('.tree-move-reference')).toHaveCount(0)
  expect(control.state.writes).toHaveLength(1)
})

test('target path input rejects traversal before registration and preserves literal names', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await page.getByRole('button', { name: '移动目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill('../escape')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog.getByText('请输入合法相对路径；不能为空、绝对路径或包含上级跳转', { exact: true }),
  ).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill('')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
  await dialog
    .getByRole('textbox', { name: '目标相对路径', exact: true })
    .fill('自定义<img src=x>副本')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog.getByText('计划已生成，尚未移动', { exact: true })).toBeVisible()
  expect(control.row.command.target.path).toBe('自定义<img src=x>副本')
  await expect(dialog.locator('img')).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})
test('ordinary mutation permission never exposes directory move without its independent root action', async ({
  page,
}) => {
  const control = await installTreeMoveFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-tree-move')
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '移动目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('ready facts remain explicitly unknown until a root result has arrived', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByText('尚无可核实回执', { exact: true })).toHaveCount(3)
  expect(confirms(control)).toHaveLength(0)
})

test('completed move presents three historical facts, not a claim that originals are HDD backups', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await dialog.getByRole('button', { name: '确认移动', exact: true }).click()
  await expect(dialog.getByText('原移动任务已完成', { exact: true })).toBeVisible()
  await expect(dialog.getByText('原回执确认', { exact: true })).toHaveCount(3)
  await expect(dialog.getByText('保留原件不是 HDD 备份', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})

test('contradictory receipt cannot turn the last original reference into a claimed success', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeEnabled()
  Object.assign(control.row, { state: 'Succeeded', executionJobId: '129f14ed-3254-4868-9885-acad522acefa', confirmation: null,
    publishedVersion: 'F'.repeat(64), destinationPublished: true, sourceRemoved: true, sourceRetained: false })
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('目录移动结果事实无法核实', { exact: false })).toBeVisible()
  await expect(dialog.getByText('原移动任务已完成', { exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
  expect(creates(control)).toHaveLength(1)
})

test('two opaque references do not stack initial copy and move dialogs or generate replacement jobs', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  control.row.command = { source: { rootId: control.root.id, path: 'source' }, expectedVersion: 'A'.repeat(64), target: { rootId: control.root.id, path: 'target' } }
  await page.goto('/files?treeCopyId=129f14ed-3254-4868-9885-acad522acefa&treeMoveId=' + control.row.id)
  const copy = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(copy).toBeVisible()
  await expect(page.getByRole('dialog', { name: '移动目录', exact: true })).toHaveCount(0)
  await copy.getByRole('button', { name: '收起', exact: true }).click()
  await page.locator('.tree-move-reference').getByRole('button', { name: '查看目录任务', exact: true }).click()
  await expect(page.getByRole('dialog', { name: '移动目录', exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
})

test('copy-specific root grant does not imply permission to move directories', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  control.root.actions = control.root.actions.filter(action => action !== 'files-tree-move')
  control.root.actions.push('files-tree-copy')
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '复制目录', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '移动目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('320 by 800 dense unknown state keeps the dialog and explicit disposition controls inside the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  const control = await installTreeMoveFixture(page, { unknown: true, directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789' })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await dialog.getByRole('button', { name: '确认移动', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  control.row.resultCode = 'FILES_RESULT_UNKNOWN'
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  const bounds = await dialog.locator('.el-dialog').boundingBox()
  expect(bounds!.y).toBeGreaterThanOrEqual(0)
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(800)
  const action = await dialog.getByRole('button', { name: '确认未知结果', exact: true }).boundingBox()
  expect(action!.y + action!.height).toBeLessThanOrEqual(800)
  expect(confirms(control)).toHaveLength(1)
})

test('protected member rejection explains the failed plan and never exposes a confirmation action', async ({ page }) => {
  const control = await installTreeMoveFixture(page, { protectedPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByText('移动未完成', { exact: true })).toBeVisible()
  await expect(dialog.getByText('目录包含受保护成员，完整计划已拒绝；未开始移动。', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})

for (const colorScheme of ['light', 'dark'] as const) {
  test(`320 ${colorScheme}: initial move form fits and preserves its target input`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await page.emulateMedia({ colorScheme })
    const control = await installTreeMoveFixture(page)
    await page.goto('/files')
    await page.getByRole('button', { name: '移动目录', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
    await expect(dialog.getByRole('textbox', { name: '目标相对路径', exact: true })).toBeVisible()
    const bounds = await dialog.locator('.el-dialog').boundingBox()
    expect(bounds!.y).toBeGreaterThanOrEqual(0)
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(800)
    expect(control.state.writes).toHaveLength(0)
  })
}

test('no eligible target root explains disabled planning without sending a write', async ({ page }) => {
  const control = await installTreeMoveFixture(page)
  control.root.actions = control.root.actions.filter(action => action !== 'files-upload')
  await page.goto('/files')
  await page.getByRole('button', { name: '移动目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  await expect(dialog.getByText('暂无允许移动写入的目标根；需要目标根同时授予上传、文件变更和目录移动权限。', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '生成计划', exact: true })).toBeDisabled()
  expect(control.state.writes).toHaveLength(0)
})

test('long legal target remains scrollable while original controls stay fixed inside the mobile dialog', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  const control = await installTreeMoveFixture(page)
  await page.goto('/files')
  await page.getByRole('button', { name: '移动目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '移动目录', exact: true })
  const target = Array.from({ length: 12 }, (_, i) => `层${i}-abcdefghijklmnopqrstuvwxyz-0123456789`).join('/')
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill(target)
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog.getByText('计划已生成，尚未移动', { exact: true })).toBeVisible()
  const body = dialog.locator('.el-dialog__body')
  const sizes = await body.evaluate(e => ({ client: e.clientHeight, scroll: e.scrollHeight }))
  expect(sizes.scroll).toBeGreaterThan(sizes.client)
  await body.hover()
  await page.mouse.wheel(0, 2000)
  await expect(dialog.getByText('尚无可核实回执', { exact: true }).last()).toBeInViewport()
  await expect(dialog.getByRole('button', { name: '确认移动', exact: true })).toBeInViewport()
  const action = await dialog.getByRole('button', { name: '确认移动', exact: true }).boundingBox()
  expect(action!.y + action!.height).toBeLessThanOrEqual(800)
  expect(confirms(control)).toHaveLength(0)
})
