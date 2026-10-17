import { expect, test } from '@playwright/test'
import { installArchiveFixture } from './fixtures/file-archives.mjs'

test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})

async function trash(page: import('@playwright/test').Page, name: string) {
  await page.getByRole('button', { name: `更多文件操作 ${name}`, exact: true }).click()
  await page.getByRole('menuitem', { name: '移入回收站', exact: true }).click()
  await expect(page.getByRole('dialog').getByRole('button', { name: '确认并登记' })).toBeEnabled()
}
const creates = (control) => control.archive.writes.filter((item) => item.path === '')

test('320px long directory link remains pointer-clickable beside the fixed action column', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 760 })
  const directoryName = 'wdv-archive-web-long-directory-name-0123456789'
  await installArchiveFixture(page, { directoryName })
  await page.goto('/files')
  const directory = page.getByRole('button', { name: directoryName, exact: true })
  await directory.scrollIntoViewIfNeeded()
  const pointerHitsLink = await directory.evaluate((el) => {
    const box = el.getBoundingClientRect()
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return hit === el || (!!hit && el.contains(hit))
  })
  expect(pointerHitsLink).toBe(true)
  await expect(directory).toHaveAttribute('title', directoryName)
  expect(
    await directory
      .locator('.file-name-label')
      .evaluate(
        (el) => getComputedStyle(el).textOverflow === 'ellipsis' && el.scrollWidth > el.clientWidth,
      ),
  ).toBe(true)
  await directory.click()
  await expect(
    page
      .getByRole('navigation', { name: 'Breadcrumb' })
      .getByRole('button', { name: directoryName, exact: true }),
  ).toBeVisible()
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
})

for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width} ${colorScheme}: history, frozen trash confirmation and completed task fit`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installArchiveFixture(page)
      await page.goto('/files')
      await page.getByRole('button', { name: '历史与回收站', exact: true }).click()
      const history = page.getByRole('dialog', { name: '历史版本与回收站', exact: true })
      await expect(history.getByText('副本存在', { exact: true })).toBeVisible()
      await expect(history.getByText('同设备保留，不是异盘容灾', { exact: true })).toBeVisible()
      const valid = history.locator(`[data-version-id="${control.versions[0].id}"]`)
      await expect(valid.getByRole('button', { name: '恢复此版本' })).toBeEnabled()
      for (const version of control.versions.slice(1))
        await expect(
          history
            .locator(`[data-version-id="${version.id}"]`)
            .getByRole('button', { name: '恢复此版本' }),
        ).toBeDisabled()
      await page.screenshot({ path: `test-results/file-history-${width}-${colorScheme}.png` })
      await history.getByRole('button', { name: 'Close this dialog' }).click()
      await trash(page, control.entry.name)
      expect(creates(control)).toHaveLength(0)
      const dialog = page.getByRole('dialog', { name: '移入回收站', exact: true })
      await expect(dialog.getByText('3 B', { exact: true })).toBeVisible()
      const box = await dialog.boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(width)
      await page.screenshot({ path: `test-results/file-archive-plan-${width}-${colorScheme}.png` })
      await dialog.getByRole('button', { name: '确认并登记' }).click()
      await expect(
        page.getByTestId('file-archive-status').getByText('已完成', { exact: true }),
      ).toBeVisible()
      expect(creates(control)).toHaveLength(1)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await page.screenshot({
        path: `test-results/file-archive-result-${width}-${colorScheme}.png`,
      })
    })
  }

test('issued plan survives reload without silently confirming; cancellation fences it', async ({
  page,
}) => {
  const control = await installArchiveFixture(page)
  await page.goto('/files')
  await trash(page, control.entry.name)
  const original = [...control.plans.values()][0]
  await expect(page).toHaveURL(new RegExp(`archiveId=${original.operationId}`))
  expect(page.url()).not.toContain('planHash')
  expect(page.url()).not.toContain(original.idempotencyKey)
  await page.reload()
  await expect(
    page.getByRole('dialog').getByText('已找回原计划；不代表已确认执行。', { exact: false }),
  ).toBeVisible()
  expect(creates(control)).toHaveLength(0)
  expect(control.plans.size).toBe(1)
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeDisabled()
  await page.getByRole('dialog').getByRole('button', { name: '取消计划' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeEnabled()
  expect(control.plans.get(original.operationId).canceled).toBe(true)
  expect(
    control.archive.writes.some(
      (item) => item.path === `/registrations/${original.operationId}/cancel`,
    ),
  ).toBe(true)
  expect(creates(control)).toHaveLength(0)
})

test('lost response retries the original seal/key, while reload finds only the original job', async ({
  page,
}) => {
  const control = await installArchiveFixture(page, { loseResponse: true, hold: true })
  await page.goto('/files')
  await trash(page, control.entry.name)
  await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
  await expect(page.getByRole('button', { name: '重试原登记' })).toBeVisible()
  await page.getByRole('button', { name: '重试原登记' }).click()
  await expect(
    page.getByTestId('file-archive-status').getByText('等待执行', { exact: true }),
  ).toBeVisible()
  const requests = creates(control)
  expect(requests).toHaveLength(2)
  expect(requests[0].body).toEqual(requests[1].body)
  expect(requests[0].headers['idempotency-key']).toBe(requests[1].headers['idempotency-key'])
  await page.reload()
  await expect(
    page.getByTestId('file-archive-status').getByText('等待执行', { exact: true }),
  ).toBeVisible()
  expect(control.archive.accepted).toBe(1)
  expect(control.plans.size).toBe(1)
  expect(creates(control)).toHaveLength(2)
  await page.getByRole('button', { name: '请求取消任务' }).click()
  await expect(
    page.getByTestId('file-archive-status').getByText('已取消', { exact: true }),
  ).toBeVisible()
})

for (const existingTarget of [false, true])
  test(`restore ${existingTarget ? 'existing' : 'absent'} target then purge only the selected copy`, async ({
    page,
  }) => {
    const control = await installArchiveFixture(page, { existingTarget })
    await page.goto('/files')
    await page.getByRole('button', { name: '历史与回收站', exact: true }).click()
    await page
      .locator(`[data-version-id="${control.versions[0].id}"]`)
      .getByRole('button', { name: '恢复此版本' })
      .click()
    const dialog = page.getByRole('dialog', { name: '恢复历史版本', exact: true })
    await expect(
      dialog.getByText(
        existingTarget ? '已存在；覆盖前必须先完整备份' : '不存在；不会覆盖其他文件',
        { exact: true },
      ),
    ).toBeVisible()
    const plan = [...control.plans.values()][0]
    expect(plan.command.expectedVersion).toBe(existingTarget ? control.entry.version : 'ABSENT')
    expect(plan.command.versionId).toBe(control.versions[0].id)
    await dialog.getByRole('button', { name: '确认并登记' }).click()
    await expect(
      page.getByTestId('file-archive-status').getByText('已完成', { exact: true }),
    ).toBeVisible()
    await page.getByRole('button', { name: '历史与回收站', exact: true }).click()
    await page
      .locator(`[data-version-id="${control.versions[0].id}"]`)
      .getByRole('button', { name: '清理此 HDD 副本' })
      .click()
    await expect(
      page.getByRole('dialog').getByText('仅清理所选 HDD 副本', { exact: true }),
    ).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
    await expect(
      page.getByTestId('file-archive-status').getByText('已完成', { exact: true }),
    ).toBeVisible()
    expect(creates(control)).toHaveLength(2)
    expect([...control.plans.values()][1].command.versionId).toBe(control.versions[0].id)
    expect(control.state.hideEntry).toBe(false)
  })

test('unknown stays locked; acknowledgement never calls it success', async ({ page }) => {
  const control = await installArchiveFixture(page, { unknown: true })
  await page.goto('/files')
  await trash(page, control.entry.name)
  await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
  await expect(page.getByText('结果未知，等待人工核实', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '人工确认未知结果', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '保留待调查' }).click()
  expect(control.archive.writes.some((item) => item.path.endsWith('/acknowledge'))).toBe(false)
  await page.getByRole('button', { name: '人工确认未知结果', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '已核实，接受未知结果' }).click()
  await expect(page.getByText('已人工确认未知结果（不代表成功）', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
})

test('history pagination, conflict, invalid metadata and empty state remain read-only', async ({
  page,
}) => {
  const control = await installArchiveFixture(page)
  control.archive.paged = true
  control.archive.invalid = 1
  await page.goto('/files')
  await page.getByRole('button', { name: '历史与回收站', exact: true }).click()
  const history = page.getByRole('dialog', { name: '历史版本与回收站', exact: true })
  await expect(history.getByText('有 1 条无效元数据被跳过，请检查备份存储。')).toBeVisible()
  await history.getByRole('button', { name: '加载更多历史' }).click()
  await expect(history.locator('[data-version-id]')).toHaveCount(5)
  expect(control.archive.lists[1].after).toBe(control.versions[0].id)
  expect(control.archive.lists[1].expectedVersion).toBe('F'.repeat(64))
  control.archive.historyStatus = 409
  await history.getByRole('button', { name: '刷新历史' }).click()
  await expect(
    history.getByText('历史目录已变化，请刷新后重新浏览。', { exact: true }),
  ).toBeVisible()
  control.archive.historyStatus = 200
  control.archive.historyEmpty = true
  await history.getByRole('button', { name: '刷新历史' }).click()
  await expect(history.getByText('尚无可显示的历史版本', { exact: true })).toBeVisible()
  expect(control.archive.writes).toHaveLength(0)
})

test('expired unregistered and missing references do not create a replacement task', async ({
  page,
}) => {
  const control = await installArchiveFixture(page, { neverAccepted: true })
  await page.goto('/files')
  await trash(page, control.entry.name)
  const original = [...control.plans.values()][0]
  await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
  await expect(page.getByRole('button', { name: '查询原登记', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toBeEnabled()
  expect(creates(control)).toHaveLength(1)
  control.plans.delete(original.operationId)
  await page.goto(`/files?archiveId=${original.operationId}`)
  await expect(
    page.getByText('原引用暂时无法查询；不能据此断定没有执行。', { exact: false }),
  ).toBeVisible()
  await page.getByRole('button', { name: '关闭引用提示' }).click()
  await page.getByRole('dialog').getByRole('button', { name: '已核实，仅关闭提示' }).click()
  await expect(page.getByTestId('file-archive-status')).toHaveCount(0)
  expect(creates(control)).toHaveLength(1)
})

test('canceled in-flight planning cannot revive a late dialog; permission failure clears metadata', async ({
  page,
}) => {
  const control = await installArchiveFixture(page)
  control.archive.planDelayMs = 700
  await page.goto('/files')
  await page
    .getByRole('button', { name: `更多文件操作 ${control.entry.name}`, exact: true })
    .click()
  await page.getByRole('menuitem', { name: '移入回收站', exact: true }).click()
  await expect(page.getByText('正在检查文件版本、备份磁盘与容量…', { exact: true })).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: '取消计划' }).click()
  await page.waitForTimeout(850)
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(creates(control)).toHaveLength(0)
  control.archive.planDelayMs = 0
  control.archive.hold = true
  await trash(page, control.entry.name)
  await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
  await expect(page.getByText('等待执行', { exact: true })).toBeVisible()
  control.archive.status = 403
  await page.getByRole('button', { name: '查询原任务' }).click()
  await expect(page.getByTestId('file-archive-status')).toHaveCount(0)
})

test('ordinary user direct visit and refresh have no file UI or archive requests', async ({
  page,
}) => {
  const control = await installArchiveFixture(page, { administrator: false })
  const requests: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/files')) requests.push(request.url())
  })
  await page.goto('/files?archiveId=11111111-1111-4111-8111-111111111111')
  await expect(page.getByRole('button', { name: '历史与回收站', exact: true })).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toHaveCount(0)
  expect(requests).toHaveLength(0)
  expect(control.archive.writes).toHaveLength(0)
})

test('read-only root exposes per-file history and all-history navigation, never writable controls', async ({
  page,
}) => {
  const control = await installArchiveFixture(page, { readOnly: true })
  await page.goto('/files')
  await expect(page.getByRole('button', { name: '新建目录', exact: true })).toHaveCount(0)
  await page
    .getByRole('button', { name: `更多文件操作 ${control.entry.name}`, exact: true })
    .click()
  await expect(page.getByRole('menuitem', { name: '移入回收站', exact: true })).toHaveCount(0)
  await page.getByRole('menuitem', { name: '历史版本', exact: true }).click()
  const history = page.getByRole('dialog', { name: '历史版本与回收站', exact: true })
  await expect(history.locator('[data-version-id]')).toHaveCount(1)
  expect(control.archive.lists[0].path).toBe(control.entry.name)
  await expect(history.getByRole('button', { name: '恢复此版本', exact: true })).toBeDisabled()
  await history.getByRole('button', { name: '查看此根全部版本', exact: true }).click()
  await expect(history.locator('[data-version-id]')).toHaveCount(5)
  expect(control.archive.writes).toHaveLength(0)
})

test('direct reload after lost submit response only reads the original registration', async ({
  page,
}) => {
  const control = await installArchiveFixture(page, { loseResponse: true, hold: true })
  await page.goto('/files')
  await trash(page, control.entry.name)
  const plan = [...control.plans.values()][0]
  await page.getByRole('dialog').getByRole('button', { name: '确认并登记' }).click()
  await expect(page.getByRole('button', { name: '重试原登记', exact: true })).toBeVisible()
  await page.reload()
  await expect(
    page.getByTestId('file-archive-status').getByText('等待执行', { exact: true }),
  ).toBeVisible()
  expect(control.archive.accepted).toBe(1)
  expect(creates(control)).toHaveLength(1)
  expect(control.plans.size).toBe(1)
  expect(control.archive.registrations.at(-1)).toEqual({
    id: plan.operationId,
    state: 'Registered',
  })
  expect(control.archive.writes.some((item) => item.path.endsWith('/cancel'))).toBe(false)
})
