import { expect, test } from '@playwright/test'
import { installTreeUnpackingFixture } from './fixtures/tree-unpackings.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
const creates = (control) => control.state.writes.filter((item) => item.path === '')
const confirms = (control) => control.state.writes.filter((item) => item.path.endsWith('/confirm'))
test('format changes never rename the requested new directory', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await open(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  const target = dialog.getByRole('textbox', { name: '目标相对路径', exact: true })
  await target.fill('自选新目录')
  await dialog.getByText('TAR.GZ（gzip）', { exact: true }).click()
  await page.getByRole('option', { name: 'TAR', exact: true }).click()
  await expect(target).toHaveValue('自选新目录')
  await dialog.getByText('TAR', { exact: true }).click()
  await page.getByRole('option', { name: 'TAR.GZ（gzip）', exact: true }).click()
  await expect(target).toHaveValue('自选新目录')
  expect(control.state.writes).toHaveLength(0)
})
test('changed input proof cannot reuse the old confirmation', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  control.row.archiveBytes++
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原计划输入证明或清单已改变', { exact: false })).toBeVisible()
  await expect(dialog.getByText('计划已过期', { exact: false })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeDisabled()
  expect(confirms(control)).toHaveLength(0)
  expect(creates(control)).toHaveLength(1)
})
async function open(page: import('@playwright/test').Page) {
  await page
    .getByRole('button', { name: /^更多文件操作 / })
    .first()
    .click()
  await page.getByRole('menuitem', { name: '解压归档', exact: true }).click()
}
async function plan(page: import('@playwright/test').Page) {
  await open(page)
  await page
    .getByRole('dialog', { name: '解压归档至新目录', exact: true })
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
      const control = await installTreeUnpackingFixture(page, {
        archiveName: '长归档-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
      })
      await page.goto('/files')
      await plan(page)
      const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
      await expect(dialog.getByText('计划已生成，尚未解压', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(0)
      await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
      const box = await dialog.locator('.el-dialog').boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      expect(box!.y).toBeGreaterThanOrEqual(0)
      expect(box!.y + box!.height).toBeLessThanOrEqual(1000)
      await page.screenshot({ path: `test-results/tree-unpack-ready-${width}-${colorScheme}.png` })
      await dialog.getByRole('button', { name: '确认解压', exact: true }).click()
      await expect(dialog.getByText('原解压任务已完成', { exact: true })).toBeVisible()
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
      await page.screenshot({
        path: `test-results/tree-unpack-complete-${width}-${colorScheme}.png`,
      })
    })
  }
test('reload restores an unconfirmed original plan using GET only', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未解压', { exact: true }),
  ).toBeVisible()
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '确认解压', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(page.url()).not.toContain('planHash')
})
test('lost registration response survives reload by read-only original-key lookup, never another POST', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await plan(page)
  await expect(page).toHaveURL(/treeUnpackKey=/)
  await expect.poll(() => control.state.lostCreate).toBe(true)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未解压', { exact: true }),
  ).toBeVisible()
  expect(control.state.reads.some((path) => path.startsWith('/by-key/'))).toBe(true)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('a missing original reference never silently creates a replacement', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  control.state.missing = true
  await page.goto('/files?treeUnpackKey=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await expect(page.getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await page.getByRole('dialog').getByRole('button', { name: '查询原任务', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
})
test('cancel an in-progress plan fences confirmation without treating a scan as execution', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, { holdPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByText('正在验证输入归档与完整清单', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog.getByText('已取消', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('unknown result requires explicit separate acknowledgment and never becomes success', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认解压', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(dialog.getByText('已确认未知结果，不代表成功', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('ordinary user initial deep link and reload issue no directory workflow requests', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, { administrator: false })
  await page.goto('/files?treeUnpackId=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await page.reload()
  await expect(page.getByRole('menuitem', { name: '解压归档', exact: true })).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})

test('lost confirmation response recovers the existing execution without another write', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, {
    loseConfirmResponse: true,
    holdUnpacking: true,
  })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认解压', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('等待解压', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog.getByText('等待解压', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdUnpacking = false
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原解压任务已完成', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('expired confirmation is disabled and never executes automatically', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  control.row.confirmation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeDisabled()
  await expect(dialog.getByText('计划已过期', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('permission rejection clears task metadata and stops background reads', async ({ page }) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  control.state.status = 403
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('.tree-unpack-reference')).toHaveCount(0)
  const reads = control.state.reads.length
  await page.waitForTimeout(2200)
  expect(control.state.reads).toHaveLength(reads)
  expect(confirms(control)).toHaveLength(0)
})
test('closing a retained reference does not cancel or confirm the original plan', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-unpack-reference')
    .getByRole('button', { name: '关闭引用', exact: true })
    .click()
  await page.getByRole('button', { name: '仅关闭引用', exact: true }).click()
  await expect(page.locator('.tree-unpack-reference')).toHaveCount(0)
  expect(control.state.writes).toHaveLength(1)
})

test('target path input rejects traversal before registration and preserves literal names', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await open(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill('../escape')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog.getByText('请选择具备解压授权的目标根和不存在的新目录，源归档与目标不能重叠', {
      exact: true,
    }),
  ).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await dialog
    .getByRole('textbox', { name: '目标相对路径', exact: true })
    .fill('自定义<img src=x>.tar.gz')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog.getByText('计划已生成，尚未解压', { exact: true })).toBeVisible()
  expect(control.row.command.target.path).toBe('自定义<img src=x>.tar.gz')
  await expect(dialog.locator('img')).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})
test('ordinary mutation permission never exposes unpacking without its independent root action', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-tree-unpack')
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeVisible()
  await page
    .getByRole('button', { name: /^更多文件操作 / })
    .first()
    .click()
  await expect(page.getByRole('button', { name: '解压归档至新目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('no authorized destination explains disabled planning without granting new root actions', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-upload')
  await page.goto('/files')
  await open(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(
    dialog.getByText('没有具备解压写入授权的目标根；请检查登记根配置，不会自动扩权。', {
      exact: true,
    }),
  ).toBeVisible()
  await expect(dialog.getByRole('button', { name: '生成计划', exact: true })).toBeDisabled()
  expect(control.state.writes).toHaveLength(0)
})

test('changed command response disables stale confirmation and downloads; no replacement write', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  control.row.command.target.path = 'other.tar.gz'
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原任务源、目标或格式已改变', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeDisabled()
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('protected descendant rejects full plan; unknown never exposes an artifact download', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page, { protectedPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(
    dialog.getByText('目录包含受保护成员，整个计划已拒绝；没有开始解压。', { exact: true }),
  ).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})

test('320x800 dense Unknown retains explicit controls and never offers a download', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 800 })
  const control = await installTreeUnpackingFixture(page, {
    unknown: true,
    directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
  })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await dialog.getByRole('button', { name: '确认解压', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  const bounds = await dialog.locator('.el-dialog').boundingBox()
  expect(bounds!.y).toBeGreaterThanOrEqual(0)
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(800)
  const action = await dialog
    .getByRole('button', { name: '确认未知结果', exact: true })
    .boundingBox()
  expect(action!.y + action!.height).toBeLessThanOrEqual(800)
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  expect(creates(control)).toHaveLength(1)
})

test('a malformed completed receipt cannot enable download or leave stale confirmation usable', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '解压归档至新目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeEnabled()
  Object.assign(control.row, {
    state: 'Succeeded',
    confirmation: null,
    executionJobId: '129f14ed-3254-4868-9885-acad522acefa',
    publishedVersion: 'F'.repeat(64),
    archiveBytes: 30,
    archiveSha256: 'invalid',
  })
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('解压结果或输入归档证明无法核实', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认解压', exact: true })).toBeDisabled()
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})

test('multiple retained workflows do not stack initial unpacking and copy dialogs or create jobs', async ({
  page,
}) => {
  const control = await installTreeUnpackingFixture(page)
  control.row.command = {
    source: { rootId: control.root.id, path: 'source' },
    expectedVersion: 'A'.repeat(64),
    target: { rootId: control.root.id, path: 'target.tar' },
    format: 'tar',
  }
  await page.goto(
    '/files?treeCopyId=129f14ed-3254-4868-9885-acad522acefa&treeUnpackId=' + control.row.id,
  )
  const copy = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(copy).toBeVisible()
  await expect(page.getByRole('dialog', { name: '解压归档至新目录', exact: true })).toHaveCount(0)
  await copy.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-unpack-reference')
    .getByRole('button', { name: '查看目录任务', exact: true })
    .click()
  await expect(page.getByRole('dialog', { name: '解压归档至新目录', exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
})
