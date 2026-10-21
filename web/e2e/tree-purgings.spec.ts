import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installTreePurgingFixture } from './fixtures/tree-purgings.mjs'

// Browser/BFF protocol evidence only; never a real Agent, HDD deletion or recovery assertion.
test.use({
  baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174',
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
})

type Control = Awaited<ReturnType<typeof installTreePurgingFixture>>
type Scope = 'Archive' | 'Originals' | 'All'
const scopeLabel: Record<Scope, string> = {
  Archive: '仅HDD归档',
  Originals: '仅保留原件',
  All: 'HDD归档和保留原件',
}
const creates = (control: Control) => control.state.writes.filter((item) => item.path === '')
const confirms = (control: Control) =>
  control.state.writes.filter((item) => item.path.endsWith('/confirm'))
const cancels = (control: Control) =>
  control.state.writes.filter((item) => item.path.endsWith('/cancel'))
const acknowledgments = (control: Control) =>
  control.state.writes.filter((item) => item.path.endsWith('/acknowledge'))
const catalogue = (page: Page) =>
  page.getByRole('dialog', { name: '目录回收与 HDD 捕获记录', exact: true })
const dialog = (page: Page) =>
  page.getByRole('dialog', { name: '永久清理目录历史组件', exact: true })

async function open(page: Page) {
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(catalogue(page).getByRole('button', { name: '永久清理', exact: true })).toBeVisible()
  await catalogue(page).getByRole('button', { name: '永久清理', exact: true }).click()
  await expect(dialog(page)).toBeVisible()
}

async function selectScope(page: Page, scope: Scope) {
  // Element's readonly input is covered by the visual placeholder. Use its
  // native keyboard interaction, not a forced click through that overlay.
  await dialog(page).getByRole('combobox', { name: '清理范围', exact: true }).press('Enter')
  await page.getByRole('option', { name: scopeLabel[scope], exact: true }).click()
}

async function plan(page: Page, scope: Scope) {
  await open(page)
  await expect(
    dialog(page).getByRole('button', { name: '生成清理计划', exact: true }),
  ).toBeDisabled()
  await selectScope(page, scope)
  await dialog(page).getByRole('button', { name: '生成清理计划', exact: true }).click()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('等待确认', { exact: true })).toBeVisible()
}

async function confirm(page: Page) {
  await dialog(page).getByRole('button', { name: '确认清理', exact: true }).click()
  await expect(page.getByText('永久清理不可撤销', { exact: true }).last()).toBeVisible()
  await page.getByRole('button', { name: '确认永久清理', exact: true }).click()
}

async function fit(page: Page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
  const box = await dialog(page).locator('.el-dialog').boundingBox()
  expect(box).not.toBeNull()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width)
  expect(box!.y + box!.height).toBeLessThanOrEqual(page.viewportSize()!.height)
}

const matrix = [
  { width: 320, colorScheme: 'light', scope: 'Archive', captureKind: 'Trash' },
  { width: 1045, colorScheme: 'dark', scope: 'Originals', captureKind: 'Overwrite' },
  { width: 1440, colorScheme: 'light', scope: 'All', captureKind: 'Trash' },
  { width: 320, colorScheme: 'dark', scope: 'All', captureKind: 'Overwrite' },
  { width: 1045, colorScheme: 'light', scope: 'Archive', captureKind: 'Overwrite' },
  { width: 1440, colorScheme: 'dark', scope: 'Originals', captureKind: 'Trash' },
] as const

for (const scenario of matrix)
  test(`${scenario.width} ${scenario.colorScheme} ${scenario.captureKind} ${scenario.scope}: explicit plan and second confirmation preserve capture history`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: scenario.width, height: 1000 })
    await page.emulateMedia({ colorScheme: scenario.colorScheme })
    const control = await installTreePurgingFixture(page, {
      captureKind: scenario.captureKind,
    })
    await page.goto('/files')
    await open(page)
    await expect(
      dialog(page).getByRole('button', { name: '生成清理计划', exact: true }),
    ).toBeDisabled()
    expect(control.state.writes).toHaveLength(0)
    await fit(page)
    await selectScope(page, scenario.scope)
    await dialog(page).getByRole('button', { name: '生成清理计划', exact: true }).click()
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page).getByText('等待确认', { exact: true })).toBeVisible()
    expect(Object.keys(control.row)).toHaveLength(15)
    expect(creates(control)).toHaveLength(1)
    expect(creates(control)[0].body).toEqual({
      captureId: control.record.id,
      scope: scenario.scope,
    })
    expect(Object.keys(creates(control)[0].body).sort()).toEqual(['captureId', 'scope'])
    expect(control.row.command.captureKind).toBe(scenario.captureKind)
    expect(control.row.executionJobId).toBeNull()
    await fit(page)

    await dialog(page).getByRole('button', { name: '确认清理', exact: true }).click()
    await page.getByRole('button', { name: '暂不清理', exact: true }).click()
    expect(confirms(control)).toHaveLength(0)
    await confirm(page)
    expect(confirms(control)).toHaveLength(1)
    expect(confirms(control)[0].body).toEqual({
      planHash: 'fixture-tree-purge-plan',
      confirmed: true,
      version: 1,
    })
    expect(control.row.executionJobId).not.toBe(control.row.planningJobId)
    expect(control.row.executionJobId).not.toBe(control.row.command.captureId)
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page).getByText('完整清理已确认', { exact: true })).toBeVisible()
    expect(control.row.archiveRemoved).toBe(scenario.scope !== 'Originals')
    expect(control.row.originalsRemoved).toBe(scenario.scope !== 'Archive')
    expect(control.catalogue.records).toHaveLength(2)
    await fit(page)
    if (scenario.width === 320 && scenario.colorScheme === 'dark')
      await page.screenshot({ path: 'test-results/tree-purgings-320-dark.png' })
  })

test('refreshing a Ready plan performs GET only and never recreates or confirms it', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page)
  await page.goto('/files')
  await plan(page, 'Archive')
  const writes = control.state.writes.length
  const reads = control.state.reads.length
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  expect(control.state.writes).toHaveLength(writes)
  expect(control.state.reads.length).toBeGreaterThan(reads)
  expect(control.state.reads.at(-1)).toBe(`/${control.row.id}`)
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('lost create response reloads by the original key without registering another purge', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, { loseCreateResponse: true })
  await page.goto('/files')
  await open(page)
  await selectScope(page, 'All')
  await dialog(page).getByRole('button', { name: '生成清理计划', exact: true }).click()
  await expect(page).toHaveURL(/treePurgeKey=/)
  const originalKey = new URL(page.url()).searchParams.get('treePurgeKey')
  expect(originalKey).toBe(control.state.key)
  await page.reload()
  await expect(dialog(page).getByText('等待确认', { exact: true })).toBeVisible()
  expect(control.state.reads).toContain(`/by-key/${originalKey}`)
  expect(creates(control)).toHaveLength(1)
  expect(creates(control)[0].headers['idempotency-key']).toBe(originalKey)
})

test('lost confirm response only reads the original intent and never resends confirmation', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, {
    loseConfirmResponse: true,
    holdPurging: true,
  })
  await page.goto('/files')
  await plan(page, 'All')
  await confirm(page)
  await expect(dialog(page).getByText('确认响应尚未核实', { exact: false })).toBeVisible()
  await expect(dialog(page).getByRole('button', { name: '确认清理', exact: true })).toHaveCount(0)
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('等待清理', { exact: true })).toBeVisible()
  expect(control.state.reads.at(-1)).toBe(`/${control.row.id}`)
  expect(confirms(control)).toHaveLength(1)
  control.state.holdPurging = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('完整清理已确认', { exact: true })).toBeVisible()
  expect(confirms(control)).toHaveLength(1)
})

for (const stale of ['command', 'plan'] as const)
  test(`${stale} changes freeze a Ready purge without replacement writes`, async ({ page }) => {
    const control = await installTreePurgingFixture(page)
    await page.goto('/files')
    await plan(page, 'Archive')
    if (stale === 'command') control.row.command.scope = 'Originals'
    else control.row.entryCount++
    await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
    await expect(dialog(page).locator('.el-alert--error')).toBeVisible()
    await expect(dialog(page).getByRole('button', { name: '确认清理', exact: true })).toBeDisabled()
    expect(creates(control)).toHaveLength(1)
    expect(confirms(control)).toHaveLength(0)
  })

test('a stale deletion fact cannot rewrite the original Unknown history', async ({ page }) => {
  const control = await installTreePurgingFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page, 'All')
  await confirm(page)
  await expect(dialog(page).getByText('结果未知', { exact: true })).toBeVisible()
  expect(control.row.archiveRemoved).toBe(true)
  control.row.archiveRemoved = false
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).locator('.el-alert--error')).toBeVisible()
  await expect(
    dialog(page).getByRole('button', { name: '确认未知结果', exact: true }),
  ).toBeDisabled()
  expect(confirms(control)).toHaveLength(1)
})

test('ordinary users mount no capture catalogue or purge protocol', async ({ page }) => {
  const control = await installTreePurgingFixture(page, { administrator: false })
  await page.goto(`/files?treePurgeId=${control.row.id}`)
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible()
  expect(control.state.reads).toHaveLength(0)
  expect(control.state.writes).toHaveLength(0)
  expect(control.catalogue.state.captureReads).toHaveLength(0)
})

test('missing root grant exposes no permanent purge action and performs no purge writes', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, { noGrant: true })
  await page.goto('/files')
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(catalogue(page).getByRole('button', { name: '永久清理', exact: true })).toHaveCount(
    0,
  )
  expect(control.state.writes).toHaveLength(0)
})

test('fully Purged capture keeps its record but permanently disables another purge', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, {
    archiveState: 'Purged',
    originalsState: 'Purged',
  })
  await page.goto('/files')
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(
    catalogue(page).getByText(control.record.source.source.path, { exact: true }),
  ).toBeVisible()
  await expect(
    catalogue(page).getByRole('button', { name: '永久清理', exact: true }),
  ).toBeDisabled()
  expect(control.state.writes).toHaveLength(0)
})

test('Uncertain capture requires a fresh explicit plan instead of inferring component presence', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, {
    archiveState: 'Uncertain',
    originalsState: 'Uncertain',
  })
  await page.goto('/files')
  await open(page)
  await expect(dialog(page).getByText('此捕获存在历史清理不确定性', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  await selectScope(page, 'Archive')
  await dialog(page).getByRole('button', { name: '生成清理计划', exact: true }).click()
  await dialog(page).getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(dialog(page).getByText('等待确认', { exact: true })).toBeVisible()
  expect(creates(control)).toHaveLength(1)
})

test('expired Ready plan never offers execution or automatically creates a replacement', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, { expired: true })
  await page.goto('/files')
  await plan(page, 'All')
  await expect(dialog(page).getByText('原计划已过期', { exact: false })).toBeVisible()
  await expect(dialog(page).getByRole('button', { name: '确认清理', exact: true })).toBeDisabled()
  expect(creates(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
})

test('cancel requires its own second confirmation and never starts deletion', async ({ page }) => {
  const control = await installTreePurgingFixture(page)
  await page.goto('/files')
  await plan(page, 'All')
  await dialog(page).getByRole('button', { name: '取消清理', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(cancels(control)).toHaveLength(0)
  await dialog(page).getByRole('button', { name: '取消清理', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  expect(cancels(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(0)
  expect(control.row.deletionStarted).toBe(false)
})

test('Unknown acknowledgment is explicit, does not resume deletion and never deletes capture history', async ({
  page,
}) => {
  const control = await installTreePurgingFixture(page, { unknown: true })
  await page.goto('/files')
  await plan(page, 'All')
  await confirm(page)
  await expect(dialog(page).getByText('结果未知', { exact: true })).toBeVisible()
  await dialog(page).getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(acknowledgments(control)).toHaveLength(0)
  await dialog(page).getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '已核实，接受未知结果', exact: true }).click()
  await expect(dialog(page).getByText('已接受未知历史', { exact: true })).toBeVisible()
  expect(acknowledgments(control)).toHaveLength(1)
  expect(confirms(control)).toHaveLength(1)
  expect(creates(control)).toHaveLength(1)
  expect(control.catalogue.records).toContain(control.record)
  await dialog(page).getByRole('button', { name: '收起', exact: true }).click()
  await page.getByRole('button', { name: '目录回收记录', exact: true }).click()
  await expect(
    catalogue(page).getByText(control.record.source.source.path, { exact: true }),
  ).toBeVisible()
})
