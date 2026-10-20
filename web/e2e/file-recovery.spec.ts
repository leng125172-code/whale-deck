import { expect, test } from '@playwright/test'
import { installFileRecoveryFixture } from './fixtures/file-recovery.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
const creates = (control) => control.state.writes.filter((item) => item.path === '/operations')
const confirms = (control) => control.state.writes.filter((item) => item.path.endsWith('/confirm'))
async function plan(page: import('@playwright/test').Page) {
  await page.getByRole('button', { name: '异常任务接管', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '异常文件任务', exact: true })
  await dialog.getByRole('button', { name: '核对该任务', exact: true }).click()
  await dialog.getByRole('button', { name: '生成接管计划', exact: true }).click()
  await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeEnabled()
  return dialog
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: current-admin confirmation records uncertainty without claiming file success`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installFileRecoveryFixture(page)
      await page.goto('/files')
      const dialog = await plan(page)
      expect(confirms(control)).toHaveLength(0)
      await page.screenshot({
        path: `test-results/file-recovery-ready-${width}-${colorScheme}.png`,
      })
      await dialog.getByRole('button', { name: '接受未知结果', exact: true }).click()
      await page.getByRole('button', { name: '确认处置', exact: true }).click()
      await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
      await expect(
        dialog.getByText('已记录接受未知历史；没有证明原文件操作成功。请检查原路径和留存对象。', {
          exact: true,
        }),
      ).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(1)
      expect(confirms(control)[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
      expect(confirms(control)[0].headers['idempotency-key']).toBe(control.row.id)
      expect(control.row.executionJobId).not.toBe(control.row.originalExecutionJobId)
      const box = await dialog.locator('.el-dialog').boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      expect(page.url()).not.toContain('planHash')
    })
  }
test('reload recovers the original plan and metadata with GET only', async ({ page }) => {
  const control = await installFileRecoveryFixture(page)
  await page.goto('/files')
  await plan(page)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '接受未知结果', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(control.state.reads).toContain('/tree-move/' + control.candidate.intentId)
})
test('lost registration response survives reload by original key lookup, never a replacement POST', async ({
  page,
}) => {
  const control = await installFileRecoveryFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '异常任务接管', exact: true }).click()
  await page.getByRole('button', { name: '核对该任务', exact: true }).click()
  await page.getByRole('button', { name: '生成接管计划', exact: true }).click()
  await expect(page).toHaveURL(/fileRecoveryKey=/)
  // The URL is deliberately persisted before POST. Prove the fixture accepted
  // the request and lost its response before reload; URL alone does not prove it.
  await expect.poll(() => control.state.lostCreate).toBe(true)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByRole('button', { name: '接受未知结果', exact: true }),
  ).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(control.state.reads.some((path) => path.startsWith('/operations/by-key/'))).toBe(true)
})

test('directory packing is a distinct current-administrator recovery kind', async ({ page }) => {
  const control = await installFileRecoveryFixture(page, { kind: 'tree-pack' })
  await page.goto('/files')
  const dialog = await plan(page)
  await expect(dialog.getByText('目录压缩', { exact: true })).toBeVisible()
  expect(creates(control)[0].body.kind).toBe('tree-pack')
  await page.reload()
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeEnabled()
  expect(control.state.reads).toContain('/tree-pack/' + control.candidate.intentId)
  expect(confirms(control)).toHaveLength(0)
  await dialog.getByRole('button', { name: '接受未知结果', exact: true }).click()
  await page.getByRole('button', { name: '确认处置', exact: true }).click()
  await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
  await expect(dialog.getByText('处置已记录，不等于原文件操作成功', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(1)
})

for (const [kind, label] of [
  ['tree-unpack', '归档解压'],
  ['tree-trash', '目录回收'],
  ['tree-restore', '目录恢复'],
])
  test(`${kind} is a distinct current-administrator recovery kind`, async ({ page }) => {
    const control = await installFileRecoveryFixture(page, { kind })
    await page.goto('/files')
    const dialog = await plan(page)
    await expect(dialog.getByText(label, { exact: true })).toBeVisible()
    expect(creates(control)[0].body.kind).toBe(kind)
    if (kind === 'tree-trash') expect(control.candidate.target).toBeNull()
    await page.reload()
    await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeEnabled()
    expect(control.state.reads).toContain('/' + kind + '/' + control.candidate.intentId)
    expect(confirms(control)).toHaveLength(0)
    await dialog.getByRole('button', { name: '接受未知结果', exact: true }).click()
    await page.getByRole('button', { name: '确认处置', exact: true }).click()
    await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
    await expect(
      dialog.getByText('处置已记录，不等于原文件操作成功', { exact: true }),
    ).toBeVisible()
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(1)
  })
test('lost confirmation reply disables another confirmation until read-only reconciliation', async ({
  page,
}) => {
  const control = await installFileRecoveryFixture(page, { loseConfirmResponse: true })
  await page.goto('/files')
  const dialog = await plan(page)
  await dialog.getByRole('button', { name: '接受未知结果', exact: true }).click()
  await page.getByRole('button', { name: '确认处置', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeDisabled()
  await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
  await expect(dialog.getByText('处置已记录，不等于原文件操作成功', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})
test('a missing current-login reference never starts a new plan', async ({ page }) => {
  const control = await installFileRecoveryFixture(page)
  control.state.missing = true
  await page.goto('/files?fileRecoveryKey=c47ae017-288b-445f-bae1-ef0ce9baa4dd')
  await expect(page.getByText('尚未查到原引用', { exact: false })).toBeVisible()
  await page
    .getByRole('dialog')
    .getByRole('button', { name: '查询原接管任务', exact: true })
    .click()
  expect(control.state.writes).toHaveLength(0)
})
test('an invalid original binding cannot be selected or submitted', async ({ page }) => {
  const control = await installFileRecoveryFixture(page, { invalidBinding: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '异常任务接管', exact: true }).click()
  await expect(page.getByRole('button', { name: '核对该任务', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '生成接管计划', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})
test('ordinary deep links and reload have no management UI or recovery requests', async ({
  page,
}) => {
  const control = await installFileRecoveryFixture(page, { administrator: false })
  await page.goto('/files?fileRecoveryId=c47ae017-288b-445f-bae1-ef0ce9baa4dd')
  await page.reload()
  await expect(page.getByTestId('file-recovery-entry')).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
})
test('path text is escaped and cancellation affects only the independent recovery', async ({
  page,
}) => {
  const control = await installFileRecoveryFixture(page, {
    sourcePath: '资料/<img src=x onerror=alert(1)>/source',
  })
  await page.goto('/files')
  const dialog = await plan(page)
  await expect(dialog.locator('img')).toHaveCount(0)
  await dialog.getByRole('button', { name: '取消本次接管', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog.getByText('本次接管已取消，原任务不变', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(0)
  expect(control.state.writes.at(-1).path).toBe('/operations/' + control.row.id + '/cancel')
})
test('a plan expires locally without silently creating or confirming a replacement', async ({
  page,
}) => {
  await page.clock.install()
  const control = await installFileRecoveryFixture(page, { expiryMs: 1500 })
  await page.goto('/files')
  const dialog = await plan(page)
  await page.clock.fastForward(2500)
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeDisabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})
test('changed original metadata after reload cannot inherit an old confirmation', async ({
  page,
}) => {
  const control = await installFileRecoveryFixture(page)
  await page.goto('/files')
  await plan(page)
  control.candidate.version++
  await page.reload()
  const dialog = page.getByRole('dialog', { name: '异常文件任务', exact: true })
  await expect(dialog.getByText('原任务版本与本计划不同', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toBeDisabled()
  expect(confirms(control)).toHaveLength(0)
})
test('completed receipt reconciliation is an explicit different choice', async ({ page }) => {
  const control = await installFileRecoveryFixture(page)
  await page.goto('/files')
  await page.getByRole('button', { name: '异常任务接管', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '异常文件任务', exact: true })
  await dialog.getByRole('button', { name: '核对该任务', exact: true }).click()
  await dialog.locator('.recovery-form .el-select').first().click()
  await page.getByRole('option', { name: '仅按完整可信回执对账', exact: true }).click()
  await dialog.getByRole('button', { name: '生成接管计划', exact: true }).click()
  await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '接受未知结果', exact: true })).toHaveCount(0)
  await dialog.getByRole('button', { name: '确认对账', exact: true }).click()
  await page.getByRole('button', { name: '确认处置', exact: true }).click()
  await dialog.getByRole('button', { name: '查询原接管任务', exact: true }).click()
  expect(creates(control)[0].body.disposition).toBe('ReconcileCompleted')
  await expect(
    dialog.getByText('完整原回执已对账；当前管理员处置与原文件操作分别保留审计。', { exact: true }),
  ).toBeVisible()
})
