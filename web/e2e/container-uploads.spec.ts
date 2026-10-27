import { Buffer } from 'node:buffer'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

type Control = Awaited<ReturnType<typeof installContainerFilesFixture>>
const writes = (control: Control, path: string) =>
  control.state.uploadWrites.filter((item) => item.path === path)

async function openFiles(page: Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const files = page.getByRole('region', { name: '容器文件', exact: true })
  await expect(files.getByRole('listitem')).toHaveCount(4)
  return { files, upload: files.getByRole('region', { name: '容器上传', exact: true }) }
}

async function choose(upload: ReturnType<Page['getByRole']>, name: string, content: string) {
  await upload.getByLabel('选择容器上传文件', { exact: true }).setInputFiles({
    name,
    mimeType: 'application/octet-stream',
    buffer: Buffer.from(content),
  })
  await upload.getByRole('button', { name: '校验并上传', exact: true }).click()
}

for (const scenario of [
  { width: 320, colorScheme: 'dark' },
  { width: 1045, colorScheme: 'light' },
  { width: 1440, colorScheme: 'dark' },
] as const)
  test(`${scenario.width} ${scenario.colorScheme}: hashes, plans, confirms and sends new bytes exactly once`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: scenario.width, height: 1000 })
    await page.emulateMedia({ colorScheme: scenario.colorScheme })
    const control = await installContainerFilesFixture(page)
    const { files, upload } = await openFiles(page)
    await choose(upload, '上传-中文.txt', 'container-upload')
    await expect(page.getByText('确认上传容器文件', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: '取消', exact: true }).click()
    expect(writes(control, 'create')).toHaveLength(0)
    expect(writes(control, 'content')).toHaveLength(0)

    await upload.getByRole('button', { name: '校验并上传', exact: true }).click()
    await page.getByRole('button', { name: '确认并上传', exact: true }).click()
    await expect(upload.getByText('已发布', { exact: true })).toBeVisible()
    expect(writes(control, 'plan')).toHaveLength(2)
    expect(writes(control, 'create')).toHaveLength(1)
    expect(writes(control, 'content')).toHaveLength(1)
    expect(control.state.contentBodies).toHaveLength(1)
    expect(control.state.uploadMetadata).toMatchObject({
      instanceId: control.state.target.instanceId,
      path: '上传-中文.txt',
      sizeBytes: Buffer.byteLength('container-upload'),
      expectedVersion: 'ABSENT',
    })
    expect(Object.keys(control.state.uploadMetadata).sort()).toEqual([
      'expectedVersion',
      'instanceId',
      'path',
      'sha256',
      'sizeBytes',
    ])
    const registration = writes(control, 'create')[0]
    expect(registration.body).toEqual({
      planHash: 'container-upload-fixture-plan',
      confirmed: true,
      idempotencyKey: registration.headers['idempotency-key'],
    })
    expect(writes(control, 'content')[0].headers['content-type']).toBe('application/octet-stream')
    expect(writes(control, 'content')[0].headers['idempotency-key']).toBe(control.upload.id)
    expect(writes(control, 'content')[0].headers['x-xsrf-token']).toBe('ui-fixture-only')
    expect(writes(control, 'content')[0].headers.origin).toMatch(/^http:\/\/127\.0\.0\.1:517[45]$/)
    await expect(files.getByText('上传-中文.txt', { exact: true })).toBeVisible()
    expect(await files.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    if (scenario.width === 320)
      await page.screenshot({ path: 'test-results/container-upload-320-dark.png' })
  })

test('overwrite binds the current entry version and shows verified retention warning and result', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page, { overwrite: true })
  const { upload } = await openFiles(page)
  await choose(upload, '中文.bin', 'new')
  const confirmation = page.getByRole('dialog', { name: '确认覆盖容器文件', exact: true })
  await expect(confirmation.getByText(/HDD 备份或受控同盘留存/)).toBeVisible()
  await confirmation.getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(upload.getByText('旧版本已完成 HDD 或受控同盘留存', { exact: true })).toBeVisible()
  expect(control.state.uploadMetadata.expectedVersion).toBe(control.state.file.version)
  expect(control.upload.previousRetained).toBe(true)
  expect(writes(control, 'content')).toHaveLength(1)
  await expect(upload.getByRole('button', { name: /恢复/ })).toHaveCount(0)
})

test('lost registration retries only the identical frozen idempotent registration before one byte transfer', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page, { loseUploadCreateResponse: true })
  const { upload } = await openFiles(page)
  await choose(upload, '登记丢响应.txt', 'one-body')
  await page.getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(upload.getByRole('button', { name: '重试原登记', exact: true })).toBeVisible()
  expect(writes(control, 'create')).toHaveLength(1)
  expect(writes(control, 'content')).toHaveLength(0)
  await upload.getByRole('button', { name: '重试原登记', exact: true }).click()
  await expect(upload.getByText('已发布', { exact: true })).toBeVisible()
  const registrations = writes(control, 'create')
  expect(registrations).toHaveLength(2)
  expect(registrations[0].body).toEqual(registrations[1].body)
  expect(registrations[0].headers['idempotency-key']).toBe(
    registrations[1].headers['idempotency-key'],
  )
  expect(writes(control, 'content')).toHaveLength(1)
})

test('lost content response preserves job id, only queries the original task and never resends bytes', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page, {
    loseUploadContentResponse: true,
    unknownUpload: true,
  })
  const { upload } = await openFiles(page)
  await choose(upload, '未知.txt', 'single-attempt')
  await page.getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(upload.getByText(/不会重新发送文件字节/).first()).toBeVisible()
  expect(writes(control, 'content')).toHaveLength(1)
  await upload.getByRole('button', { name: '查询原任务', exact: true }).click()
  await expect(upload.getByText('结果未知，已停止自动操作', { exact: true })).toBeVisible()
  await expect(upload.getByText(`任务 ${control.upload.jobId}`, { exact: true })).toBeVisible()
  await expect(upload.getByText(/保留任务编号，需管理员核实/)).toBeVisible()
  const reads = control.state.uploadReads
  await page.waitForTimeout(2200)
  expect(control.state.uploadReads).toBe(reads)
  expect(writes(control, 'content')).toHaveLength(1)
})

test('manual cancel is explicit, queries durable state and does not resend an Unknown upload', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page, { unknownUpload: true })
  const { upload } = await openFiles(page)
  await choose(upload, '取消.txt', 'cancel-once')
  await page.getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(upload.getByText('结果未知，已停止自动操作', { exact: true })).toBeVisible()
  await upload.getByRole('button', { name: '取消上传', exact: true }).click()
  await page.getByRole('button', { name: '继续保留', exact: true }).click()
  expect(writes(control, 'cancel')).toHaveLength(0)
  await upload.getByRole('button', { name: '取消上传', exact: true }).click()
  await page.getByRole('button', { name: '确认取消', exact: true }).click()
  await expect(upload.getByText('已取消', { exact: true })).toBeVisible()
  expect(writes(control, 'cancel')).toHaveLength(1)
  expect(writes(control, 'content')).toHaveLength(1)
})

test('collapsing the panel aborts an in-flight body and never starts a second transfer', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page, { pauseUploadContent: true })
  const { upload } = await openFiles(page)
  await choose(upload, '中止.txt', 'abort-body')
  await page.getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect.poll(() => control.state.uploadContentWaiting).toBe(true)
  await page.getByRole('button', { name: '收起文件', exact: true }).click()
  await expect(upload).toHaveCount(0)
  control.state.releaseUploadContent()
  await page.waitForTimeout(100)
  expect(writes(control, 'content')).toHaveLength(1)
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await expect(
    page.getByRole('region', { name: '容器上传', exact: true }).getByText('中止.txt', {
      exact: true,
    }),
  ).toHaveCount(0)
  expect(writes(control, 'content')).toHaveLength(1)
})

test('changing directory dismisses the owned confirmation and clears its private path', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page)
  const { files, upload } = await openFiles(page)
  const privateName = '私有-切目录.txt'
  await choose(upload, privateName, 'never-created')
  const confirmation = page.getByRole('dialog', { name: '确认上传容器文件', exact: true })
  await expect(confirmation).toContainText(privateName)
  await files
    .getByRole('button', { name: '资料', exact: true })
    .evaluate((element: HTMLButtonElement) => element.click())
  await expect(confirmation).toHaveCount(0)
  await expect(page.getByText(privateName, { exact: false })).toHaveCount(0)
  expect(writes(control, 'create')).toHaveLength(0)
  expect(writes(control, 'content')).toHaveLength(0)
})

test('unmounting the file panel destroys its confirmation and cannot create a late upload', async ({
  page,
}) => {
  const control = await installContainerFilesFixture(page)
  const { upload } = await openFiles(page)
  const privateName = '私有-退出.txt'
  await choose(upload, privateName, 'never-created')
  const confirmation = page.getByRole('dialog', { name: '确认上传容器文件', exact: true })
  await expect(confirmation).toContainText(privateName)
  await page
    .getByRole('button', { name: '收起文件', exact: true })
    .evaluate((element: HTMLButtonElement) => element.click())
  await expect(upload).toHaveCount(0)
  await expect(confirmation).toHaveCount(0)
  await expect(page.getByText(privateName, { exact: false })).toHaveCount(0)
  expect(writes(control, 'create')).toHaveLength(0)
  expect(writes(control, 'content')).toHaveLength(0)
})

test('protected and ordinary-user surfaces do not expose container upload', async ({ page }) => {
  const control = await installContainerFilesFixture(page, { administrator: false })
  await page.goto('/containers')
  await expect(page.getByRole('region', { name: '容器上传', exact: true })).toHaveCount(0)
  expect(control.state.uploadWrites).toHaveLength(0)
})
