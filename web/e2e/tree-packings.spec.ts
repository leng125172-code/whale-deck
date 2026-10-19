import { createHash } from 'node:crypto'
import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'
import { installTreePackingFixture } from './fixtures/tree-packings.mjs'

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
  await page.getByRole('button', { name: '压缩目录', exact: true }).click()
  await page
    .getByRole('dialog', { name: '压缩目录', exact: true })
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
      const control = await installTreePackingFixture(page, {
        directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
      })
      await page.goto('/files')
      await plan(page)
      const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
      await expect(dialog.getByText('计划已生成，尚未压缩', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(0)
      await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
      const box = await dialog.locator('.el-dialog').boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      expect(box!.y).toBeGreaterThanOrEqual(0)
      expect(box!.y + box!.height).toBeLessThanOrEqual(1000)
      await page.screenshot({ path: `test-results/tree-pack-ready-${width}-${colorScheme}.png` })
      await dialog.getByRole('button', { name: '确认压缩', exact: true }).click()
      await expect(dialog.getByText('原压缩任务已完成', { exact: true })).toBeVisible()
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
      await page.screenshot({ path: `test-results/tree-pack-complete-${width}-${colorScheme}.png` })
    })
  }
test('reload restores an unconfirmed original plan using GET only', async ({ page }) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未压缩', { exact: true }),
  ).toBeVisible()
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '确认压缩', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(page.url()).not.toContain('planHash')
})
test('lost registration response survives reload by read-only original-key lookup, never another POST', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await plan(page)
  await expect(page).toHaveURL(/treePackKey=/)
  await expect.poll(() => control.state.lostCreate).toBe(true)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByText('计划已生成，尚未压缩', { exact: true }),
  ).toBeVisible()
  expect(control.state.reads.some((path) => path.startsWith('/by-key/'))).toBe(true)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('a missing original reference never silently creates a replacement', async ({ page }) => {
  const control = await installTreePackingFixture(page)
  control.state.missing = true
  await page.goto('/files?treePackKey=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await expect(page.getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await page.getByRole('dialog').getByRole('button', { name: '查询原任务', exact: true }).click()
  expect(control.state.writes).toHaveLength(0)
})
test('cancel an in-progress plan fences confirmation without treating a scan as execution', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, { holdPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByText('正在验证完整目录', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog.getByText('已取消', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('unknown result requires explicit separate acknowledgment and never becomes success', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认压缩', exact: true }).click()
  await expect(dialog.getByText('结果未知，保留锁与证据', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，确认处置', exact: true }).click()
  await expect(dialog.getByText('已确认未知结果，不代表成功', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('ordinary user initial deep link and reload issue no directory workflow requests', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, { administrator: false })
  await page.goto('/files?treePackId=fc1cac12-5815-4426-b80e-23db4fd3e974')
  await page.reload()
  await expect(page.getByRole('button', { name: '压缩目录', exact: true })).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})

test('lost confirmation response recovers the existing execution without another write', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, {
    loseConfirmResponse: true,
    holdPacking: true,
  })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认压缩', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('等待压缩', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(dialog.getByText('等待压缩', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdPacking = false
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原压缩任务已完成', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('expired confirmation is disabled and never executes automatically', async ({ page }) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  control.row.confirmation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeDisabled()
  await expect(dialog.getByText('计划已过期', { exact: false })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
})
test('permission rejection clears task metadata and stops background reads', async ({ page }) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  control.state.status = 403
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('.tree-pack-reference')).toHaveCount(0)
  const reads = control.state.reads.length
  await page.waitForTimeout(2200)
  expect(control.state.reads).toHaveLength(reads)
  expect(confirms(control)).toHaveLength(0)
})
test('closing a retained reference does not cancel or confirm the original plan', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  await dialog.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-pack-reference')
    .getByRole('button', { name: '关闭引用', exact: true })
    .click()
  await page.getByRole('button', { name: '仅关闭引用', exact: true }).click()
  await expect(page.locator('.tree-pack-reference')).toHaveCount(0)
  expect(control.state.writes).toHaveLength(1)
})

test('target path input rejects traversal before registration and preserves literal names', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await page.getByRole('button', { name: '压缩目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await dialog.getByRole('textbox', { name: '目标相对路径', exact: true }).fill('../escape')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog.getByText('请选择允许压缩的目标根与相对路径，扩展名须匹配格式且不能位于源目录内', {
      exact: true,
    }),
  ).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await dialog
    .getByRole('textbox', { name: '目标相对路径', exact: true })
    .fill('自定义<img src=x>.tar.gz')
  await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog.getByText('计划已生成，尚未压缩', { exact: true })).toBeVisible()
  expect(control.row.command.target.path).toBe('自定义<img src=x>.tar.gz')
  await expect(dialog.locator('img')).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})
test('ordinary mutation permission never exposes packing without its independent root action', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-tree-pack')
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '压缩目录', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('no authorized destination explains disabled planning without granting new root actions', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  control.root.actions = control.root.actions.filter((action) => action !== 'files-upload')
  await page.goto('/files')
  await page.getByRole('button', { name: '压缩目录', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(
    dialog.getByText('没有具备压缩写入授权的目标根；请检查登记根配置，不会自动扩权。', {
      exact: true,
    }),
  ).toBeVisible()
  await expect(dialog.getByRole('button', { name: '生成计划', exact: true })).toBeDisabled()
  expect(control.state.writes).toHaveLength(0)
})

test('TAR selection changes suffix; native download transfers complete fixture bytes with no page Blob', async ({
  page,
}) => {
  const payload = Buffer.from('packing-fixture-not-a-real-tar')
  const server = createServer((_request, response) => {
    response.writeHead(200, {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': 'attachment; filename="fixture.tar"',
      'Content-Length': String(payload.length),
    })
    response.end(payload)
  })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const address = server.address()
  if (!address || typeof address === 'string') throw new Error('Fixture HTTP server unavailable')
  const control = await installTreePackingFixture(page, {
    downloadUrl: `http://127.0.0.1:${address.port}/archive`,
  })
  try {
    await page.goto('/files')
    await page.getByRole('button', { name: '压缩目录', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
    await dialog.getByText('TAR.GZ（压缩）', { exact: true }).click()
    await page.getByRole('option', { name: 'TAR（仅打包）', exact: true }).click()
    await expect(dialog.getByRole('textbox', { name: '目标相对路径', exact: true })).toHaveValue(
      '资料.tar',
    )
    await dialog.getByText('TAR（仅打包）', { exact: true }).click()
    await page.getByRole('option', { name: 'TAR.GZ（压缩）', exact: true }).click()
    await expect(dialog.getByRole('textbox', { name: '目标相对路径', exact: true })).toHaveValue(
      '资料.tar.gz',
    )
    await dialog.getByText('TAR.GZ（压缩）', { exact: true }).click()
    await page.getByRole('option', { name: 'TAR（仅打包）', exact: true }).click()
    await dialog.getByRole('button', { name: '生成计划', exact: true }).click()
    await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
    await dialog.getByRole('button', { name: '确认压缩', exact: true }).click()
    const link = dialog.getByRole('link', { name: '下载归档', exact: true })
    await expect(link).toBeVisible()
    await expect(link).toHaveAttribute(
      'href',
      /\/tree-packings\/[a-f0-9-]+\/download\?expectedVersion=F{64}$/,
    )
    expect(control.row.command.format).toBe('tar')
    await page.evaluate(() => {
      URL.createObjectURL = () => {
        throw new Error('No page Blob for archive downloads')
      }
    })
    const event = page.waitForEvent('download')
    await link.click()
    const download = await event
    expect(await download.failure()).toBeNull()
    const bytes = await readFile((await download.path())!)
    expect(bytes.equals(control.payload)).toBe(true)
    expect(createHash('sha256').update(bytes).digest('hex').toUpperCase()).toBe(control.digest)
    expect(control.state.downloads).toBe(1)
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(1)
  } finally {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve())),
    )
  }
})

test('changed command response disables stale confirmation and downloads; no replacement write', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  control.row.command.target.path = 'other.tar.gz'
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('原任务源、目标或格式已改变', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeDisabled()
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('protected descendant rejects full plan; unknown never exposes an artifact download', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page, { protectedPlan: true })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(
    dialog.getByText('目录包含受保护成员，整个计划已拒绝；没有开始压缩。', { exact: true }),
  ).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})

test('320x800 dense Unknown retains explicit controls and never offers a download', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 800 })
  const control = await installTreePackingFixture(page, {
    unknown: true,
    directoryName: '长目录-中文-abcdefghijklmnopqrstuvwxyz-0123456789',
  })
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await dialog.getByRole('button', { name: '确认压缩', exact: true }).click()
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
  const control = await installTreePackingFixture(page)
  await page.goto('/files')
  await plan(page)
  const dialog = page.getByRole('dialog', { name: '压缩目录', exact: true })
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeEnabled()
  Object.assign(control.row, {
    state: 'Succeeded',
    confirmation: null,
    executionJobId: '129f14ed-3254-4868-9885-acad522acefa',
    publishedVersion: 'F'.repeat(64),
    archiveBytes: 30,
    archiveSha256: 'invalid',
  })
  await dialog.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog.getByText('归档结果无法核实', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '确认压缩', exact: true })).toBeDisabled()
  await expect(dialog.getByRole('link', { name: '下载归档', exact: true })).toHaveCount(0)
  expect(confirms(control)).toHaveLength(0)
})

test('multiple retained workflows do not stack initial packing and copy dialogs or create jobs', async ({
  page,
}) => {
  const control = await installTreePackingFixture(page)
  control.row.command = {
    source: { rootId: control.root.id, path: 'source' },
    expectedVersion: 'A'.repeat(64),
    target: { rootId: control.root.id, path: 'target.tar' },
    format: 'tar',
  }
  await page.goto(
    '/files?treeCopyId=129f14ed-3254-4868-9885-acad522acefa&treePackId=' + control.row.id,
  )
  const copy = page.getByRole('dialog', { name: '复制目录', exact: true })
  await expect(copy).toBeVisible()
  await expect(page.getByRole('dialog', { name: '压缩目录', exact: true })).toHaveCount(0)
  await copy.getByRole('button', { name: '收起', exact: true }).click()
  await page
    .locator('.tree-pack-reference')
    .getByRole('button', { name: '查看目录任务', exact: true })
    .click()
  await expect(page.getByRole('dialog', { name: '压缩目录', exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
})
