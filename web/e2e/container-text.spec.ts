import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
async function open(page: Page, textContent = '\uFEFF中英 O0Il\r\nline\r\n', pending = false) {
  const control = await installContainerFilesFixture(page, { overwrite: true, textContent })
  control.state.pauseDownload = pending
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const files = page.getByRole('region', { name: '容器文件', exact: true })
  await files.getByRole('button', { name: '资料', exact: true }).click()
  await files.getByRole('button', { name: '编辑 中文.bin', exact: true }).click()
  const editor = page.getByRole('dialog', { name: '编辑容器文本 · 中文.bin', exact: true })
  await expect(editor).toBeVisible()
  return { control, files, editor }
}
for (const scenario of [
  { width: 320, colorScheme: 'dark' },
  { width: 1440, colorScheme: 'light' },
] as const)
  test(`${scenario.width} ${scenario.colorScheme}: edit preserves BOM/CRLF, original version and one confirmed body`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: scenario.width, height: 1000 })
    await page.emulateMedia({ colorScheme: scenario.colorScheme })
    const { control, files, editor } = await open(page)
    await expect(
      editor.getByRole('textbox', { name: '容器文件文本内容', exact: true }),
    ).toHaveValue('中英 O0Il\nline\n')
    await expect(
      editor.getByRole('checkbox', { name: '容器文本 UTF-8 BOM', exact: true }),
    ).toBeChecked()
    await editor
      .getByRole('textbox', { name: '容器文件文本内容', exact: true })
      .fill('新内容\nline\n')
    await editor.screenshot({
      path: `test-results/container-text-${scenario.width}-${scenario.colorScheme}.png`,
    })
    await editor.getByRole('button', { name: '校验并保存', exact: true }).click()
    const confirm = page.getByRole('dialog', { name: '确认覆盖容器文件', exact: true })
    await expect(confirm).toBeVisible()
    await confirm.getByRole('button', { name: '取消', exact: true }).click()
    expect(control.state.contentBodies).toHaveLength(0)
    await files.getByRole('button', { name: '返回编辑草稿', exact: true }).click()
    await expect(
      editor.getByRole('textbox', { name: '容器文件文本内容', exact: true }),
    ).toHaveValue('新内容\nline\n')
    await editor.getByRole('button', { name: '校验并保存', exact: true }).click()
    await expect(confirm).toBeVisible()
    await confirm.getByRole('button', { name: '确认并上传', exact: true }).click()
    await expect(files.getByText('已发布', { exact: true })).toBeVisible()
    expect(control.state.contentBodies).toHaveLength(1)
    expect(control.state.contentBodies[0].toString('utf8')).toBe('\uFEFF新内容\r\nline\r\n')
    expect(control.state.uploadMetadata.expectedVersion).toBe('A'.repeat(64))
    expect(control.state.uploadMetadata.path).toBe('资料/中文.bin')
    expect(await files.evaluate((element) => element.scrollWidth <= element.clientWidth + 1)).toBe(
      true,
    )
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(
      true,
    )
  })
test('concurrent file version change refuses any plan/body and keeps an editable private draft', async ({
  page,
}) => {
  const { control, files, editor } = await open(page)
  await editor.getByRole('textbox', { name: '容器文件文本内容', exact: true }).fill('保留我的修改')
  control.state.file.version = 'B'.repeat(64)
  await editor.getByRole('button', { name: '校验并保存', exact: true }).click()
  await expect(files.getByText(/自打开编辑器后已经变化/)).toBeVisible()
  expect(control.state.uploadWrites).toHaveLength(0)
  await files.getByRole('button', { name: '返回编辑草稿', exact: true }).click()
  await expect(editor.getByRole('textbox', { name: '容器文件文本内容', exact: true })).toHaveValue(
    '保留我的修改',
  )
  await editor.getByRole('button', { name: '取消', exact: true }).click()
  const discard = page.getByRole('dialog', { name: '放弃容器文本编辑？', exact: true })
  await discard.getByRole('button', { name: '继续编辑', exact: true }).click()
  await expect(editor).toBeVisible()
  await editor.getByRole('button', { name: '取消', exact: true }).click()
  await discard.getByRole('button', { name: '放弃编辑', exact: true }).click()
  await expect(editor).toHaveCount(0)
  expect(control.state.contentBodies).toHaveLength(0)
})
test('binary NUL is refused without editable contents or file writes', async ({ page }) => {
  const { control, editor } = await open(page, 'a\u0000b')
  await expect(editor.getByText(/NUL/)).toBeVisible()
  await expect(editor.getByRole('textbox', { name: '容器文件文本内容', exact: true })).toHaveCount(
    0,
  )
  await expect(editor.getByRole('button', { name: '校验并保存', exact: true })).toBeDisabled()
  expect(control.state.uploadWrites).toHaveLength(0)
})

test('a closed pending read cannot restore private text after a late response', async ({
  page,
}) => {
  const { control, editor } = await open(page, '迟到私有内容', true)
  await expect.poll(() => control.state.downloadWaiting).toBe(true)
  await editor.getByRole('button', { name: '取消', exact: true }).click()
  control.state.releaseDownload()
  await expect(editor).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: '容器文件文本内容', exact: true })).toHaveCount(0)
  expect(control.state.uploadWrites).toHaveLength(0)
})

test('cross-page session invalidation closes the owned discard dialog and clears the draft', async ({
  page,
}) => {
  const { control, editor } = await open(page)
  await editor
    .getByRole('textbox', { name: '容器文件文本内容', exact: true })
    .fill('不能出现在下一账号的私有草稿')
  await editor.getByRole('button', { name: '取消', exact: true }).click()
  const discard = page.getByRole('dialog', { name: '放弃容器文本编辑？', exact: true })
  await expect(discard).toBeVisible()
  await page.evaluate(() =>
    window.dispatchEvent(
      new StorageEvent('storage', {
        key: 'whaledeck-session-ended',
        storageArea: localStorage,
        newValue: JSON.stringify({ version: 1, kind: 'signed-out', id: 'a'.repeat(32) }),
      }),
    ),
  )
  await expect(page).toHaveURL(/\/login\?returnUrl=\/containers/)
  await expect(discard).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: '容器文件文本内容', exact: true })).toHaveCount(0)
  expect(control.state.uploadWrites).toHaveLength(0)
})
