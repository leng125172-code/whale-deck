import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installTreeOverwriteRestoringFixture } from './fixtures/tree-overwrite-restorings.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})
type Control = Awaited<ReturnType<typeof installTreeOverwriteRestoringFixture>>
const creates = (control: Control) => control.state.writes.filter((item) => item.path === '')
const confirms = (control: Control) =>
  control.state.writes.filter((item) => item.path.endsWith('/confirm'))
const dialog = (page: Page) =>
  page.getByRole('dialog', { name: '覆盖现有目录（先备份）', exact: true })
const catalogue = (page: Page) =>
  page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
async function open(page: Page) {
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(
    catalogue(page).getByRole('button', { name: '覆盖现有目录（先备份）', exact: true }),
  ).toBeVisible()
  await catalogue(page).getByRole('button', { name: '覆盖现有目录（先备份）', exact: true }).click()
  await expect(dialog(page).getByRole('button', { name: '生成计划', exact: true })).toBeVisible()
}
async function plan(page: Page) {
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未覆盖', { exact: true })).toBeVisible()
}
async function fit(page: Page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
  const box = await dialog(page).locator('.el-dialog').boundingBox()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width)
  expect(box!.y + box!.height).toBeLessThanOrEqual(page.viewportSize()!.height)
}

for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: explicit overwrite plan, target version, independent jobs and read-only reload`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTreeOverwriteRestoringFixture(page)
      await page.goto('/files')
      await open(page)
      expect(control.state.writes).toHaveLength(0)
      await fit(page)
      await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
      await expect(dialog(page).getByText('计划已生成，尚未覆盖', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(0)
      expect(creates(control)[0].body).toEqual({
        captureId: control.catalogue.records[0].id,
        target: { rootId: control.root.id, path: control.target.name },
        targetExpectedVersion: control.target.version,
      })
      expect(creates(control)[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
      expect(creates(control)[0].headers['idempotency-key']).toMatch(/^[a-f0-9-]{36}$/)
      expect(Object.keys(control.row)).toHaveLength(24)
      await expect(dialog(page).getByText('D'.repeat(64), { exact: true })).toBeVisible()
      await expect(dialog(page).locator('img')).toHaveCount(0)
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('计划已生成，尚未覆盖', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      await dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }).click()
      await expect(dialog(page).getByText('原覆盖恢复任务已完成', { exact: true })).toBeVisible()
      expect(confirms(control)).toHaveLength(1)
      expect(confirms(control)[0].body).toEqual({
        planHash: 'fixture-independent-overwrite-plan',
        confirmed: true,
        version: 1,
      })
      expect(confirms(control)[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
      expect(control.row.executionJobId).not.toBe(control.row.planningJobId)
      expect(control.row.executionJobId).not.toBe(control.row.command.captureId)
      expect(control.row.overwrittenCaptureId).toBe(control.row.executionJobId)
      await expect(dialog(page).getByText('曾完整验证', { exact: true })).toBeVisible()
      await fit(page)
      await page.reload()
      await expect(dialog(page).getByText('原覆盖恢复任务已完成', { exact: true })).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(confirms(control)).toHaveLength(1)
    })

test('an Overwrite historical capture remains distinct from the next displaced backup', async ({
  page,
}) => {
  const control = await installTreeOverwriteRestoringFixture(page, { captureKind: 'Overwrite' })
  await page.goto('/files')
  await plan(page)
  await expect(dialog(page).getByText('覆盖前备份归档', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }).click()
  await expect(dialog(page).getByText('原覆盖恢复任务已完成', { exact: true })).toBeVisible()
  expect(control.row.command.captureKind).toBe('Overwrite')
  expect(control.row.overwrittenCaptureId).not.toBe(control.row.command.captureId)
})

test('lost create uses only the saved key and never re-registers a missing task', async ({
  page,
}) => {
  const control = await installTreeOverwriteRestoringFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(page).toHaveURL(/treeOverwriteRestoreKey=/)
  await expect(dialog(page).getByRole('button', { name: '查询原任务', exact: true })).toBeEnabled()
  control.state.missing = true
  await page.reload()
  await expect(dialog(page).getByText('尚未查到原任务', { exact: false })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
  control.state.missing = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('计划已生成，尚未覆盖', { exact: true })).toBeVisible()
  expect(control.state.reads).toContain('/by-key/' + control.state.key)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('lost confirm disables retransmission and reload only queries the original execution', async ({
  page,
}) => {
  const control = await installTreeOverwriteRestoringFixture(page, {
    loseConfirmResponse: true,
    holdRestoring: true,
  })
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }).click()
  await expect(dialog(page).getByText('确认响应尚未核实', { exact: false })).toBeVisible()
  await expect(
    dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }),
  ).toBeDisabled()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('等待覆盖恢复', { exact: true })).toBeVisible()
  await page.reload()
  await expect(dialog(page).getByText('等待覆盖恢复', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
  control.state.holdRestoring = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('原覆盖恢复任务已完成', { exact: true })).toBeVisible()
})

test('320x800 Unknown preserves backup evidence and acknowledgment needs a second confirmation', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 800 })
  await page.emulateMedia({ colorScheme: 'dark' })
  const control = await installTreeOverwriteRestoringFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }).click()
  await expect(
    dialog(page).getByText('结果未知，保留锁、备份与证据', { exact: true }),
  ).toBeVisible()
  await expect(dialog(page).getByText('曾完整验证', { exact: true })).toBeVisible()
  await expect(dialog(page).getByText('A'.repeat(64), { exact: true })).toBeVisible()
  await fit(page)
  const action = await dialog(page)
    .getByRole('button', { name: '确认未知结果', exact: true })
    .boundingBox()
  expect(action!.y + action!.height).toBeLessThanOrEqual(800)
  await page.reload()
  await expect(
    dialog(page).getByText('结果未知，保留锁、备份与证据', { exact: true }),
  ).toBeVisible()
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
  expect(control.row.backupVerified).toBe(true)
  expect(control.row.publishedVersion).toBeNull()
})

test('cancel is explicit, dismissing does not write, and an active overwrite locks other file mutations', async ({
  page,
}) => {
  const control = await installTreeOverwriteRestoringFixture(page)
  await page.goto('/files')
  await plan(page)
  await dialog(page).getByRole('button', { name: '收起', exact: true }).click()
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '选择文件', exact: true })).toBeDisabled()
  await expect(page.locator('.files-toolbar').getByRole('combobox')).toBeDisabled()
  await page.getByRole('button', { name: '查看覆盖恢复任务', exact: true }).click()
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(control.state.writes).toHaveLength(1)
  await dialog(page).getByRole('button', { name: '取消操作', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(2)
  expect(confirms(control)).toHaveLength(0)
  expect(control.catalogue.files.state.writes).toHaveLength(0)
})

for (const field of [
  'archiveSha256',
  'targetExpectedVersion',
  'targetTotalBytes',
  'captureId',
] as const)
  test(`${field}: changed plan freezes confirmation without replacement writes`, async ({
    page,
  }) => {
    const control = await installTreeOverwriteRestoringFixture(page)
    await page.goto('/files')
    await plan(page)
    if (field === 'archiveSha256') control.row.archiveSha256 = 'F'.repeat(64)
    else if (field === 'targetExpectedVersion')
      control.row.command.targetExpectedVersion = 'F'.repeat(64)
    else if (field === 'targetTotalBytes') control.row.targetTotalBytes++
    else control.row.command.captureId = control.catalogue.records[1].id
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(
      dialog(page)
        .getByRole('alert')
        .filter({ hasText: /已改变|证明不一致/ }),
    ).toBeVisible()
    await expect(
      dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }),
    ).toBeDisabled()
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(0)
  })

for (const field of ['backupArchiveSha256', 'backupArchiveBytes', 'overwrittenCaptureId'] as const)
  test(`${field}: changed verified backup cannot erase or replace the original Unknown evidence`, async ({
    page,
  }) => {
    const control = await installTreeOverwriteRestoringFixture(page, { unknown: true })
    await page.goto('/files')
    await plan(page)
    await dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }).click()
    await expect(
      dialog(page).getByText('结果未知，保留锁、备份与证据', { exact: true }),
    ).toBeVisible()
    if (field === 'backupArchiveSha256') control.row.backupArchiveSha256 = 'F'.repeat(64)
    else if (field === 'backupArchiveBytes') control.row.backupArchiveBytes++
    else control.row.overwrittenCaptureId = control.row.command.captureId
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(
      dialog(page)
        .getByRole('alert')
        .filter({ hasText: /已改变|无法核实/ }),
    ).toBeVisible()
    await expect(
      dialog(page).getByRole('button', { name: '确认未知结果', exact: true }),
    ).toBeDisabled()
    expect(confirms(control)).toHaveLength(1)
  })

test('a changed current target fails its plan and never offers execution', async ({ page }) => {
  const control = await installTreeOverwriteRestoringFixture(page, { targetChanged: true })
  await page.goto('/files')
  await open(page)
  await dialog(page).getByRole('button', { name: '生成计划', exact: true }).click()
  await expect(
    dialog(page).getByText('当前目标版本已变化，原计划不会覆盖；请核实原任务。', { exact: true }),
  ).toBeVisible()
  await expect(
    dialog(page).getByRole('button', { name: '确认先备份再覆盖', exact: true }),
  ).toHaveCount(0)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('ordinary users do not mount file management, capture catalogue or an overwrite reference', async ({
  page,
}) => {
  let fileRequests = 0
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/files/')) fileRequests++
  })
  const control = await installTreeOverwriteRestoringFixture(page, { administrator: false })
  await page.goto('/files?treeOverwriteRestoreId=' + control.row.id)
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  await expect(dialog(page)).toHaveCount(0)
  await expect(page.getByRole('button', { name: '目录回收记录', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  await expect(dialog(page)).toHaveCount(0)
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
  expect(control.catalogue.state.captureReads).toHaveLength(0)
  expect(control.entryState.reads).toBe(0)
  expect(fileRequests).toBe(0)
})

for (const option of ['noGrant', 'protectedTarget'] as const)
  test(`${option}: a historical archive alone cannot expose a destructive overwrite entry`, async ({
    page,
  }) => {
    const control = await installTreeOverwriteRestoringFixture(page, { [option]: true })
    await page.goto('/files')
    await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
    await expect(
      catalogue(page).getByRole('button', { name: '覆盖现有目录（先备份）', exact: true }),
    ).toHaveCount(0)
    expect(control.state.writes).toHaveLength(0)
  })
