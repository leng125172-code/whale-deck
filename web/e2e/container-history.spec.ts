import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
async function setup(
  page: Page,
  options: { lost?: boolean; unknown?: boolean; pending?: boolean; tamper?: boolean; dropAttributes?: boolean; oldPlan?: boolean } = {},
) {
  const control = await installContainerFilesFixture(page, { overwrite: true })
  const target = control.state.target
  const source = {
    captureId: 'c'.repeat(32),
    containerId: target.containerId,
    originalInstanceId: '666:2026-10-21T01:02:03.123456789Z',
    path: '资料/中文.bin',
    sourceVersion: 'C'.repeat(64),
    sizeBytes: 3,
    sha256: 'D'.repeat(64),
    createdAtUnixMs: 0,
    differentSourceDevice: false,
    availability: 'Present',
    captureHash: 'E'.repeat(64),
  }
  const base = `/api/v1/containers/${target.containerId}/files`,
    id = '1a92fea1-53b1-4b12-9aaa-8282a57c31ac'
  const state = {
    plans: [] as Record<string, unknown>[],
    creates: [] as Record<string, unknown>[],
    reads: 0,
    canceled: false,
  }
  const intent = {
    id,
    jobId: id,
    location: { containerId: target.containerId, instanceId: target.instanceId, path: source.path },
    sizeBytes: source.sizeBytes,
    sha256: source.sha256,
    state: 'AwaitingUpload',
    expiresAtUtc: new Date(Date.now() + 900000).toISOString(),
    resultCode: null as string | null,
    version: 0,
    publishedVersion: null as string | null,
    previousRetained: false,
    restoreAttributes: undefined as true | undefined,
  }
  await page.route('**/files/history?**', async (route) => {
    const url = new URL(route.request().url())
    await route.fulfill({
      json: {
        location: {
          containerId: target.containerId,
          instanceId: target.instanceId,
          path: url.searchParams.get('path') || '',
        },
        targetProof: 'A'.repeat(64),
        entries: [{ ...source, ...(options.tamper ? { containerId: 'b'.repeat(64) } : {}) }],
        version: 'B'.repeat(64),
        nextCursor: '',
        scanned: 1,
        invalidRecords: 0,
        complete: true,
      },
    })
  })
  await page.route('**/files/restores**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname
    if (path === base + '/restores/plan') {
      state.plans.push(request.postDataJSON())
      intent.location.path = String(request.postDataJSON().path)
      intent.restoreAttributes = request.postDataJSON().restoreAttributes === true ? true : undefined
      return route.fulfill({
        json: {
          planHash: 'signed-hdd-plan',
          expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          overwrites: true,
          warning: '校验HDD来源并在覆盖前保留当前旧版本。',
          ...(!options.oldPlan && intent.restoreAttributes ? { restoreAttributes: true } : {}),
        },
      })
    }
    if (path === base + '/restores' && request.method() === 'POST') {
      state.creates.push(request.postDataJSON())
      if (options.lost && state.creates.length === 1)
        return route.fulfill({
          status: 503,
          json: { title: 'Fixture lost reply', code: 'FIXTURE_REPLY_LOST' },
        })
      return route.fulfill({ status: 202, json: intent })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      return route.fulfill({ status: 202, json: { id, state: 'CancelRequested' } })
    }
    state.reads++
    const status = state.canceled
      ? 'Canceled'
      : options.unknown
        ? 'Unknown'
        : options.pending
          ? 'Ready'
          : 'Succeeded'
    return route.fulfill({
      json: {
        ...intent,
        ...(options.dropAttributes ? { restoreAttributes: undefined } : {}),
        state: status,
        version: state.canceled ? 3 : 2,
        resultCode:
          status === 'Succeeded'
            ? 'CONTAINER_UPLOAD_PUBLISHED'
            : status === 'Unknown'
              ? 'CONTAINER_UPLOAD_RESULT_UNKNOWN'
              : status === 'Canceled'
                ? 'CONTAINER_UPLOAD_CANCELED'
                : null,
        publishedVersion: status === 'Succeeded' ? 'F'.repeat(64) : null,
        previousRetained: status === 'Succeeded',
      },
    })
  })
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const files = page.getByRole('region', { name: '容器文件', exact: true })
  await files.getByRole('button', { name: '资料', exact: true }).click()
  await files.getByRole('button', { name: '历史版本 中文.bin', exact: true }).click()
  const history = page.getByRole('dialog', { name: '容器文件历史版本', exact: true })
  await expect(history).toBeVisible()
  return {
    control,
    state,
    source,
    history,
    files,
    restore: history.getByRole('button', {
      name: `恢复 ${source.path} ${source.captureId.slice(0, 12)}`,
      exact: true,
    }),
  }
}

for (const size of [
  { width: 320, colorScheme: 'dark' },
  { width: 1440, colorScheme: 'light' },
] as const)
  test(`${size.width} ${size.colorScheme}: exact source confirmation, canceled plan and one durable job without a browser body`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: size.width, height: 1000 })
    await page.emulateMedia({ colorScheme: size.colorScheme })
    const { control, state, source, history, restore, files } = await setup(page)
    await restore.click()
    const confirmation = page.getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    await expect(confirmation).toBeVisible()
    await confirmation.getByRole('button', { name: '取消', exact: true }).click()
    expect(state.creates).toHaveLength(0)
    expect(control.state.contentBodies).toHaveLength(0)
    await restore.click()
    await confirmation.getByRole('button', { name: '确认并恢复', exact: true }).click()
    await expect(history.getByText('已恢复', { exact: true })).toBeVisible()
    expect(state.creates).toHaveLength(1)
    expect(state.plans).toHaveLength(2)
    expect(state.plans[0]).toEqual({
      instanceId: control.state.target.instanceId,
      path: source.path,
      captureId: source.captureId,
      expectedCaptureHash: source.captureHash,
      expectedVersion: 'A'.repeat(64),
    })
    expect(control.state.contentBodies).toHaveLength(0)
    expect(control.state.uploadWrites).toHaveLength(0)
    expect(
      await history.evaluate((element) => element.scrollWidth <= element.clientWidth + 1),
    ).toBe(true)
    expect(await files.evaluate((element) => element.scrollWidth <= element.clientWidth + 1)).toBe(
      true,
    )
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(
      true,
    )
    await history.screenshot({
      path: `test-results/container-history-${size.width}-${size.colorScheme}.png`,
    })
  })
test('lost registration response keeps the exact signed plan and key, never a new recovery or browser body', async ({
  page,
}) => {
  const { state, history, restore, control } = await setup(page, { lost: true })
  await restore.click()
  await page
    .getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    .getByRole('button', { name: '确认并恢复', exact: true })
    .click()
  await history.getByRole('button', { name: '重试登记原任务', exact: true }).click()
  await expect(history.getByText('已恢复', { exact: true })).toBeVisible()
  expect(state.plans).toHaveLength(1)
  expect(state.creates).toHaveLength(2)
  expect(state.creates[1]).toEqual(state.creates[0])
  expect(control.state.contentBodies).toHaveLength(0)
})
test('Unknown stops automatic polling and refuses another restore until original facts are resolved', async ({
  page,
}) => {
  const { state, history, restore } = await setup(page, { unknown: true })
  await restore.click()
  await page
    .getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    .getByRole('button', { name: '确认并恢复', exact: true })
    .click()
  await expect(history.getByText('结果未知，已停止自动操作', { exact: true })).toBeVisible()
  const reads = state.reads
  await page.waitForTimeout(2400)
  expect(state.reads).toBe(reads)
  await expect(restore).toBeDisabled()
  expect(state.creates).toHaveLength(1)
})
test('cancel is another owned confirmation and only requests cancellation of the original job', async ({
  page,
}) => {
  const { state, history, restore } = await setup(page, { pending: true })
  await restore.click()
  await page
    .getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    .getByRole('button', { name: '确认并恢复', exact: true })
    .click()
  await history.getByRole('button', { name: '请求取消恢复', exact: true }).click()
  const confirmation = page.getByRole('dialog', { name: '取消容器恢复', exact: true })
  await confirmation.getByRole('button', { name: '保留任务', exact: true }).click()
  expect(state.canceled).toBe(false)
  await history.getByRole('button', { name: '请求取消恢复', exact: true }).click()
  await confirmation.getByRole('button', { name: '确认取消恢复', exact: true }).click()
  await expect(history.getByText('已取消', { exact: true })).toBeVisible()
  expect(state.canceled).toBe(true)
  expect(state.creates).toHaveLength(1)
})
test('a cross-page session end removes private history and its pending confirmation without creating a job', async ({
  page,
}) => {
  const { state, restore, history } = await setup(page)
  await restore.click()
  const confirmation = page.getByRole('dialog', { name: '确认恢复容器文件', exact: true })
  await expect(confirmation).toBeVisible()
  await page.evaluate(() =>
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'whaledeck-session-ended',
        storageArea: localStorage,
        newValue: JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) }),
      }),
    ),
  )
  await expect(confirmation).toHaveCount(0)
  await expect(history).toHaveCount(0)
  expect(state.creates).toHaveLength(0)
})
test('a history record from another container cannot become a source', async ({ page }) => {
  const { state, history } = await setup(page, { tamper: true })
  await expect(history.getByText(/历史记录发生变化/)).toBeVisible()
  expect(state.plans).toHaveLength(0)
  expect(state.creates).toHaveLength(0)
})

for (const width of [320, 1440])
  test(`${width}: alternate destination is inspected, confirmed and frozen through a lost registration response`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    const { state, source, history, control } = await setup(page, { lost: true })
    await history.getByRole('button', { name: `恢复到其他路径 ${source.path} ${source.captureId.slice(0,12)}`, exact: true }).click()
    const destination = page.getByRole('dialog', { name: '选择恢复目标', exact: true })
    await destination.getByRole('textbox', { name: '恢复目标相对路径', exact: true }).fill('../escape.bin')
    await destination.getByRole('button', { name: '核对恢复目标', exact: true }).click()
    await expect(destination.getByText(/请输入容器内有效的相对文件路径/)).toBeVisible()
    expect(state.plans).toHaveLength(0)
    const path = '恢复副本/另一份中文.bin'
    await destination.getByRole('textbox', { name: '恢复目标相对路径', exact: true }).fill(path)
    await destination.getByRole('button', { name: '核对恢复目标', exact: true }).click()
    const confirmation = page.getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    await expect(confirmation.getByText(`历史来源：${source.path}`, { exact: true })).toBeVisible()
    await expect(confirmation.getByText(`恢复目标：${path}`, { exact: true })).toBeVisible()
    expect(state.plans[0]?.path).toBe(path)
    expect(control.state.requests.some(request => request.path.endsWith('/files/entry') && request.query.path === path)).toBe(true)
    await confirmation.getByRole('button', { name: '确认并恢复', exact: true }).click()
    await history.getByRole('button', { name: '重试登记原任务', exact: true }).click()
    await expect(history.getByText('已恢复', { exact: true })).toBeVisible()
    expect(state.creates).toHaveLength(2)
    expect(state.creates[0]).toEqual(state.creates[1])
    expect(state.plans).toHaveLength(1)
    expect(control.state.contentBodies).toHaveLength(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true)
  })

for (const width of [320, 1440])
  test(`${width}: historical attributes are explicitly confirmed and stay frozen after a lost reply`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    const { state, source, history, restore, control } = await setup(page, { lost: true })
    const policy = history.getByRole('checkbox', { name: '同时恢复历史普通属性', exact: true })
    await expect(policy).not.toBeChecked()
    await history.getByText('同时恢复历史普通属性', { exact: true }).click()
    await expect(policy).toBeChecked()
    await restore.click()
    const confirmation = page.getByRole('dialog', { name: '确认恢复容器文件', exact: true })
    await expect(confirmation.getByText('恢复方式：内容与历史普通属性', { exact: true })).toBeVisible()
    await expect(policy).toBeDisabled()
    await confirmation.getByRole('button', { name: '确认并恢复', exact: true }).click()
    await expect(history.getByRole('button', { name: '重试登记原任务', exact: true })).toBeVisible()
    await expect(policy).toBeDisabled()
    await history.getByRole('button', { name: '重试登记原任务', exact: true }).click()
    await expect(history.getByText('已恢复', { exact: true })).toBeVisible()
    expect(state.plans[0]?.restoreAttributes).toBe(true)
    expect(state.plans[0]?.captureId).toBe(source.captureId)
    expect(state.creates).toHaveLength(2)
    expect(state.creates[1]).toEqual(state.creates[0])
    expect(Object.keys(state.creates[0]!).sort()).toEqual(['confirmed','idempotencyKey','planHash'])
    await history.getByText('同时恢复历史普通属性', { exact: true }).click()
    await expect(policy).not.toBeChecked()
    await history.getByRole('button', { name: '核对恢复任务', exact: true }).click()
    await expect(history.getByText('恢复方式：内容与历史普通属性', { exact: true })).toBeVisible()
    expect(control.state.contentBodies).toHaveLength(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true)
  })
test('older backend cannot silently downgrade an attribute plan before registration', async ({ page }) => {
  const { state, history, restore } = await setup(page, { oldPlan: true })
  await history.getByText('同时恢复历史普通属性', { exact: true }).click()
  await expect(history.getByRole('checkbox', { name: '同时恢复历史普通属性', exact: true })).toBeChecked()
  await restore.click()
  await expect(history.getByText(/恢复计划未确认属性策略/)).toBeVisible()
  expect(state.creates).toHaveLength(0)
  await expect(page.getByRole('dialog', { name: '确认恢复容器文件', exact: true })).toHaveCount(0)
})
test('a task response cannot drop the frozen attribute policy or trigger another publication', async ({ page }) => {
  const { state, history, restore } = await setup(page, { dropAttributes: true })
  await history.getByText('同时恢复历史普通属性', { exact: true }).click()
  await expect(history.getByRole('checkbox', { name: '同时恢复历史普通属性', exact: true })).toBeChecked()
  await restore.click()
  await page.getByRole('dialog', { name: '确认恢复容器文件', exact: true }).getByRole('button', { name: '确认并恢复', exact: true }).click()
  await expect(history.getByText(/容器上传任务或实例绑定无法核实/)).toBeVisible()
  expect(state.creates).toHaveLength(1)
  expect(state.plans).toHaveLength(1)
})
