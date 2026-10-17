import { expect, test } from '@playwright/test'
import { createHash } from 'node:crypto'
import { Buffer } from 'node:buffer'
import { installFileFixture } from './fixtures/files.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174', launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, ignoreDefaultArgs: ['--hide-scrollbars'] } })

async function fixture(page: import('@playwright/test').Page, original = 'console.log("O0Il 中文")\r\n') {
  const control = await installFileFixture(page, { mutations: true })
  const state = { status: 200, corrupt: false, source: Buffer.from('\uFEFF' + original), uploaded: null as Buffer | null, reads: 0 }
  control.entry.sizeBytes = state.source.length
  await page.route('**/api/v1/files/entry?*', route => route.fulfill({ json: control.entry }))
  await page.route('**/api/v1/files/text?*', async route => {
    state.reads++
    if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status, code: 'FILES_TEXT_TEST_REJECTED' } })
    const body = control.intent.state === 'Succeeded' && state.uploaded ? state.uploaded : state.source
    const bom = body.subarray(0, 3).equals(Buffer.from([0xEF, 0xBB, 0xBF])), text = body.subarray(bom ? 3 : 0).toString('utf8')
    const rest = text.replace(/\r\n/g, ''), crlf = text.includes('\r\n'), cr = rest.includes('\r'), lf = rest.includes('\n')
    const lineEnding = Number(crlf) + Number(cr) + Number(lf) > 1 ? 'Mixed' : crlf ? 'CRLF' : cr ? 'CR' : lf ? 'LF' : 'None'
    await route.fulfill({ json: { rootId: 'whaledeck.files.shared', path: control.entry.name, version: control.entry.version,
      sizeBytes: body.length, sha256: state.corrupt ? 'F'.repeat(64) : createHash('sha256').update(body).digest('hex').toUpperCase(), text, utf8Bom: bom, lineEnding } })
  })
  await page.route('**/api/v1/files/uploads/*/content', async route => { state.uploaded = route.request().postDataBuffer(); await route.fallback() })
  await page.goto('/files')
  return { ...control, text: state }
}
async function open(page: import('@playwright/test').Page, name: string) {
  await page.getByRole('button', { name: `更多文件操作 ${name}`, exact: true }).click()
  await page.getByRole('menuitem', { name: '编辑文本', exact: true }).click()
  return page.locator('.file-text-editor:visible')
}

for (const width of [320, 390, 1045, 1440]) for (const colorScheme of ['light', 'dark'] as const) {
  test(`${width} ${colorScheme}: edit exact BOM/CRLF version through confirmed durable upload workflow`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 }); await page.emulateMedia({ colorScheme })
    const control = await fixture(page)
    await page.addStyleTag({ content: 'html { overflow-y: scroll !important; }' })
    const drawer = await open(page, control.entry.name)
    const input = drawer.getByRole('textbox', { name: '文件文本内容', exact: true })
    await expect(input).toHaveValue('console.log("O0Il 中文")\n')
    await expect(drawer.getByRole('checkbox', { name: 'UTF-8 BOM', exact: true })).toBeChecked()
    await expect(drawer.getByRole('button', { name: '校验并保存', exact: true })).toBeDisabled()
    await input.fill('console.log("编辑 中文 O0Il")\n')
    await expect(drawer.getByText('未保存', { exact: true })).toBeVisible()
    const button = await drawer.getByRole('button', { name: '校验并保存', exact: true }).boundingBox()
    const content = await page.evaluate(() => ({ width: document.documentElement.clientWidth, height: innerHeight }))
    const panel = await drawer.boundingBox()
    expect(panel!.x).toBeGreaterThanOrEqual(0); expect(panel!.x + panel!.width).toBeLessThanOrEqual(content.width)
    expect(button!.x).toBeGreaterThanOrEqual(0); expect(button!.x + button!.width).toBeLessThanOrEqual(content.width); expect(button!.y + button!.height).toBeLessThanOrEqual(content.height)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    await page.screenshot({ path: `test-results/file-text-${width}-${colorScheme}.png` })
    await drawer.getByRole('button', { name: '校验并保存', exact: true }).click()
    const confirm = page.getByRole('dialog', { name: '确认覆盖文件', exact: true })
    await expect(confirm).toContainText('HDD')
    await confirm.getByRole('button', { name: '确认并上传', exact: true }).click()
    await expect(page.getByText('已发布', { exact: true })).toBeVisible()
    expect(control.text.uploaded).toEqual(Buffer.from('\uFEFFconsole.log("编辑 中文 O0Il")\r\n'))
    expect(control.state.writes.find((item: { path: string }) => item.path === 'uploads/plan')?.body.expectedVersion).toBe('A'.repeat(64))
    expect(control.state.bodyCount).toBe(1)
    expect(await page.evaluate(() => JSON.stringify([Object.entries(localStorage), Object.entries(sessionStorage)]))).not.toContain('编辑 中文 O0Il')
  })
}

for (const status of [400, 403, 409, 503]) test(`text ${status} is an error, never editable empty success`, async ({ page }) => {
  const control = await fixture(page); control.text.status = status
  await open(page, control.entry.name)
  if (status === 403) await expect(page.locator('.file-text-editor:visible')).toHaveCount(0)
  else { await expect(page.locator('.file-text-editor .el-alert--error')).toBeVisible(); await expect(page.getByRole('textbox', { name: '文件文本内容', exact: true })).toHaveCount(0) }
  expect(control.state.bodyCount).toBe(0)
})

test('mixed line endings require explicit format choice; cancel dirty changes never writes', async ({ page }) => {
  const control = await fixture(page, 'a\r\nb\rc\n'), drawer = await open(page, control.entry.name)
  await drawer.getByRole('textbox', { name: '文件文本内容', exact: true }).fill('a\nb\nc\nedit')
  await expect(drawer.getByRole('button', { name: '校验并保存', exact: true })).toBeDisabled()
  await drawer.locator('.file-text-editor__formats .el-select').click()
  await page.getByRole('option', { name: 'LF', exact: true }).click()
  await expect(drawer.getByRole('button', { name: '校验并保存', exact: true })).toBeEnabled()
  await drawer.getByRole('button', { name: '取消', exact: true }).click()
  await page.getByRole('dialog', { name: '放弃编辑？', exact: true }).getByRole('button', { name: '继续编辑', exact: true }).click()
  await expect(drawer.getByRole('textbox', { name: '文件文本内容', exact: true })).toHaveValue('a\nb\nc\nedit')
  await drawer.getByRole('button', { name: '取消', exact: true }).click()
  await page.getByRole('dialog', { name: '放弃编辑？', exact: true }).getByRole('button', { name: '放弃编辑', exact: true }).click()
  await expect(drawer).toBeHidden(); expect(control.state.writes).toHaveLength(0)
})

test('source version changes never submit stale edits and retain a downloadable draft', async ({ page }) => {
  const control = await fixture(page), drawer = await open(page, control.entry.name)
  await drawer.getByRole('textbox', { name: '文件文本内容', exact: true }).fill('keep my draft\n')
  control.entry.version = 'C'.repeat(64)
  await drawer.getByRole('button', { name: '校验并保存', exact: true }).click()
  await expect(page.getByText('文件版本已变化，未保存；请重新打开后编辑', { exact: true })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  const downloaded = page.waitForEvent('download'); await page.getByRole('button', { name: '下载草稿', exact: true }).click()
  const artifact = await downloaded; expect(artifact.suggestedFilename()).toBe(control.entry.name + '.draft'); await artifact.delete()
  await page.getByRole('button', { name: '放弃草稿', exact: true }).click()
  await page.getByRole('dialog', { name: '放弃草稿？', exact: true }).getByRole('button', { name: '放弃草稿', exact: true }).click()
  await expect(page.getByRole('button', { name: '下载草稿', exact: true })).toHaveCount(0)
})

test('checksum mismatch cannot populate the editor', async ({ page }) => {
  const control = await fixture(page); control.text.corrupt = true
  await open(page, control.entry.name)
  await expect(page.locator('.file-text-editor .el-alert--error')).toContainText('校验不一致')
  await expect(page.getByRole('textbox', { name: '文件文本内容', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})

test('BOM and line-ending controls complete reversible cycles and save only the explicit format', async ({ page }) => {
  const control = await fixture(page), drawer = await open(page, control.entry.name)
  const bom = drawer.getByRole('checkbox', { name: 'UTF-8 BOM', exact: true })
  const bomLabel = drawer.locator('label.el-checkbox')
  await bomLabel.click(); await expect(bom).not.toBeChecked(); await bomLabel.click(); await expect(bom).toBeChecked()
  const formats = drawer.locator('.file-text-editor__formats .el-select')
  await formats.click(); await page.getByRole('option', { name: 'CR', exact: true }).click()
  await formats.click(); await page.getByRole('option', { name: 'CRLF', exact: true }).click()
  await expect(drawer.getByRole('button', { name: '校验并保存', exact: true })).toBeDisabled()
  await formats.click(); await page.getByRole('option', { name: 'LF', exact: true }).click(); await bomLabel.click(); await expect(bom).not.toBeChecked()
  await drawer.getByRole('button', { name: '校验并保存', exact: true }).click()
  await page.getByRole('dialog', { name: '确认覆盖文件', exact: true }).getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(page.getByText('已发布', { exact: true })).toBeVisible()
  expect(control.text.uploaded).toEqual(Buffer.from('console.log("O0Il 中文")\n'))
})

test('empty file is editable and markup remains literal text rather than executable UI', async ({ page }) => {
  const control = await fixture(page, ''), drawer = await open(page, control.entry.name)
  const literal = '<img src="invalid" onerror="window.__fileTextExecuted=true">\n中文'
  await expect(drawer.getByRole('textbox', { name: '文件文本内容', exact: true })).toHaveValue('')
  await drawer.getByRole('textbox', { name: '文件文本内容', exact: true }).fill(literal)
  expect(await page.evaluate(() => '__fileTextExecuted' in window)).toBe(false)
  await expect(drawer.locator('img')).toHaveCount(0)
  await drawer.getByRole('button', { name: '校验并保存', exact: true }).click()
  await page.getByRole('dialog', { name: '确认覆盖文件', exact: true }).getByRole('button', { name: '确认并上传', exact: true }).click()
  await expect(page.getByText('已发布', { exact: true })).toBeVisible()
  expect(control.text.uploaded).toEqual(Buffer.from('\uFEFF' + literal))
})

test('closing during a pending preview cannot resurrect text when the late response arrives', async ({ page }) => {
  const control = await fixture(page)
  let release!: () => void
  const pending = new Promise<void>(resolve => { release = resolve })
  let started!: () => void
  const reading = new Promise<void>(resolve => { started = resolve })
  await page.route('**/api/v1/files/text?*', async route => { started(); await pending; await route.fallback() })
  const drawer = await open(page, control.entry.name)
  await reading; await drawer.getByRole('button', { name: '取消', exact: true }).click(); release()
  await expect(drawer).toBeHidden()
  await expect(page.getByRole('textbox', { name: '文件文本内容', exact: true })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
})
