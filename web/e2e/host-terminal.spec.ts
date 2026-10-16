import { expect, test } from '@playwright/test'
import { Buffer } from 'node:buffer'
import { installTerminalFixture } from './fixtures/terminal.mjs'

for (const width of [320, 390, 1045, 1440, 1920]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: terminal confirm input clear exit and responsive fit`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTerminalFixture(page)
      await page.goto('/host/terminal')
      await expect(page.getByRole('heading', { name: '宿主终端', exact: true })).toBeVisible()
      await expect(page.getByText('非 root', { exact: false }).first()).toBeVisible()
      expect(control.state.writes).toHaveLength(0)
      await page.getByRole('button', { name: '开始连接', exact: true }).click()
      await page.getByRole('button', { name: '取消', exact: true }).click()
      expect(control.state.writes).toHaveLength(0)
      await page.getByRole('button', { name: '开始连接', exact: true }).click()
      await page.getByRole('button', { name: '确认连接', exact: true }).click()
      await expect(page.getByText('已连接 · 受限宿主身份', { exact: true })).toBeVisible()
      const input = page.getByRole('textbox', { name: '宿主终端输入', exact: true })
      await input.pressSequentially('hello')
      await input.press('Enter')
      await expect(page.locator('.xterm-rows')).toContainText('收到输入: hello')
      await page.keyboard.insertText('中文🙂')
      await input.press('Enter')
      await expect(page.locator('.xterm-rows')).toContainText('中文🙂')
      await page.getByRole('button', { name: '清屏', exact: true }).click()
      expect(control.state.sockets).toBe(1)
      await input.pressSequentially('exit 7')
      await input.press('Enter')
      await expect(page.getByText('进程已结束 · 退出码 7', { exact: true })).toBeVisible()
      expect(
        control.state.socketUrls.every(
          (url: string) => !url.includes('?') && !url.includes('AAAA'),
        ),
      ).toBe(true)
      await page.screenshot({
        path: `test-results/terminal-${width}-${colorScheme}.png`,
        fullPage: false,
      })
      const boxes = await page
        .locator('.terminal-actions,.terminal-toolbar,.terminal-surface,.terminal-message')
        .evaluateAll((items) =>
          items.map((item) => {
            const box = item.getBoundingClientRect()
            return { left: box.left, right: box.right, height: box.height }
          }),
        )
      expect(boxes.every((box) => box.left >= 0 && box.right <= width && box.height > 0)).toBe(true)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      expect(errors).toEqual([])
    })
  }
}

test('manual disconnect, reconnect, theme and resize do not leak an extra session', async ({
  page,
}) => {
  const control = await installTerminalFixture(page)
  await page.goto('/host/terminal')
  await page.getByRole('button', { name: '开始连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByRole('button', { name: '断开连接' })).toBeVisible()
  await page.setViewportSize({ width: 390, height: 844 })
  await expect
    .poll(
      () => control.state.frames.filter((row: { kind?: string }) => row.kind === 'resize').length,
    )
    .toBeGreaterThan(1)
  const before = control.state.sockets
  await page.locator('.theme-switch .el-switch').click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  expect(control.state.sockets).toBe(before)
  await page.getByRole('button', { name: '断开连接' }).click()
  await expect(page.getByText('连接已断开。', { exact: true })).toBeVisible()
  await expect
    .poll(
      () =>
        control.state.writes.filter((item: { path: string }) => item.path.endsWith('/close'))
          .length,
    )
    .toBe(1)
  await page.getByRole('button', { name: '重新连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  expect(control.state.sockets).toBe(2)
})

test('503, interruption and permission loss clear terminal content and require human reconnect', async ({
  page,
}) => {
  const control = await installTerminalFixture(page, { grantStatus: 503 })
  await page.goto('/host/terminal')
  await page.getByRole('button', { name: '开始连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('无法建立终端会话，请稍后重试。', { exact: true })).toBeVisible()
  expect(control.state.sockets).toBe(0)
  control.state.grantStatus = 200
  await page.getByRole('button', { name: '重新连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  control.output('private-terminal-output\r\n')
  await expect(page.locator('.xterm-rows')).toContainText('private-terminal-output')
  control.interrupt()
  await expect(
    page.getByText('会话已中断或权限发生变化，请手动重新连接。', { exact: true }),
  ).toBeVisible()
  await expect(page.locator('.xterm-rows')).not.toContainText('private-terminal-output')
  expect(control.state.sockets).toBe(1)
  await page.getByRole('button', { name: '重新连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  control.revoke()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('menuitem', { name: '宿主终端', exact: true })).toHaveCount(0)
})

test('leaving a live terminal closes its exact session and leaves no retained output DOM', async ({
  page,
}) => {
  const control = await installTerminalFixture(page)
  await page.goto('/host/terminal')
  await page.getByRole('button', { name: '开始连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  control.output('<script>window.terminalXss = true</script>\r\n')
  await expect(page.locator('.xterm-rows')).toContainText('window.terminalXss')
  expect(await page.evaluate(() => 'terminalXss' in window)).toBe(false)
  await page.getByRole('menuitem', { name: '我的门户', exact: true }).click()
  await expect(page).toHaveURL(/\/portal$/)
  await expect
    .poll(
      () =>
        control.state.writes.filter((item: { path: string }) => item.path.endsWith('/close'))
          .length,
    )
    .toBe(1)
  await expect(page.locator('.terminal-surface')).toHaveCount(0)
})

test('screen reader full cycle preserves native Chinese IME input without reconnecting', async ({
  page,
}) => {
  const control = await installTerminalFixture(page)
  await page.goto('/host/terminal')
  await page.getByRole('button', { name: '开始连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  const input = page.getByRole('textbox', { name: '宿主终端输入', exact: true })
  const toggle = page.locator('.terminal-accessibility .el-switch')
  await expect(page.locator('.xterm-accessibility-tree')).toHaveCount(0)
  await toggle.click()
  await expect(page.locator('.xterm-accessibility-tree')).toBeVisible()
  await input.focus()
  const cdp = await page.context().newCDPSession(page)
  try {
    await cdp.send('Input.imeSetComposition', {
      text: '中文🙂',
      selectionStart: 4,
      selectionEnd: 4,
    })
    await cdp.send('Input.insertText', { text: '中文🙂' })
    await expect
      .poll(() =>
        control.state.frames
          .filter((frame: { kind?: string }) => frame.kind === 'input')
          .map((frame: { data: string }) => Buffer.from(frame.data, 'base64').toString('utf8'))
          .join(''),
      )
      .toBe('中文🙂')
    await input.press('Enter')
    await expect(page.locator('.xterm-rows')).toContainText('收到输入: 中文🙂')
  } finally {
    await cdp.detach()
  }
  await toggle.click()
  await expect(page.locator('.xterm-accessibility-tree')).toHaveCount(0)
  await input.focus()
  await page.keyboard.insertText('标准输入')
  await input.press('Enter')
  await expect(page.locator('.xterm-rows')).toContainText('收到输入: 标准输入')
  expect(control.state.sockets).toBe(1)
})

test('browser back while confirmation is open neither grants a session nor leaves the dialog', async ({
  page,
}) => {
  const control = await installTerminalFixture(page)
  await page.goto('/portal')
  await page.getByRole('menuitem', { name: '宿主终端', exact: true }).click()
  await page.getByRole('button', { name: '开始连接' }).click()
  await expect(page.getByRole('dialog', { name: '建立宿主终端连接' })).toBeVisible()
  await page.goBack()
  await expect(page).toHaveURL(/\/portal$/)
  await expect(page.getByRole('dialog', { name: '建立宿主终端连接' })).toHaveCount(0)
  expect(control.state.writes).toHaveLength(0)
  expect(control.state.sockets).toBe(0)
})

test('multi-byte oversized input is rejected before any command bytes are sent', async ({
  page,
}) => {
  const control = await installTerminalFixture(page)
  await page.goto('/host/terminal')
  await page.getByRole('button', { name: '开始连接' }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
  await page.getByRole('textbox', { name: '宿主终端输入', exact: true }).focus()
  await page.keyboard.insertText('中'.repeat(6000))
  await expect(
    page.getByText('单次输入过大，请分段粘贴（最多 16 KiB）。', { exact: true }),
  ).toBeVisible()
  expect(
    control.state.frames.filter((frame: { kind?: string }) => frame.kind === 'input'),
  ).toHaveLength(0)
})

for (const mobileWidth of [320, 390]) {
  test(`${mobileWidth}px: IME editing cancellation and short mobile viewport preserve the active session`, async ({
    page,
  }) => {
    const control = await installTerminalFixture(page)
    await page.setViewportSize({ width: mobileWidth, height: 560 })
    await page.goto('/host/terminal')
    await page.getByRole('button', { name: '开始连接' }).click()
    await page.getByRole('button', { name: '确认连接', exact: true }).click()
    await expect(page.getByText('已连接 · 受限宿主身份')).toBeVisible()
    const input = page.getByRole('textbox', { name: '宿主终端输入', exact: true })
    await input.focus()
    const cdp = await page.context().newCDPSession(page)
    try {
      await cdp.send('Input.imeSetComposition', {
        text: '待编辑',
        selectionStart: 3,
        selectionEnd: 3,
      })
      await cdp.send('Input.imeSetComposition', {
        text: '修改后',
        selectionStart: 3,
        selectionEnd: 3,
      })
      expect(
        control.state.frames.filter((frame: { kind?: string }) => frame.kind === 'input'),
      ).toHaveLength(0)
      await cdp.send('Input.imeSetComposition', { text: '', selectionStart: 0, selectionEnd: 0 })
      await input.pressSequentially('hello')
      await input.press('Enter')
      await expect(page.locator('.xterm-rows')).toContainText('收到输入: hello')
      await expect(page.locator('.xterm-rows')).not.toContainText('待编辑')
    } finally {
      await cdp.detach()
    }
    const bounds = await page
      .locator('.terminal-actions,.terminal-toolbar,.terminal-surface,.terminal-message')
      .evaluateAll((items) =>
        items.map((item) => {
          const box = item.getBoundingClientRect()
          return { top: box.top, bottom: box.bottom, left: box.left, right: box.right }
        }),
      )
    expect(
      bounds.every(
        (box) => box.top >= 0 && box.bottom <= 560 && box.left >= 0 && box.right <= mobileWidth,
      ),
    ).toBe(true)
    expect(control.state.sockets).toBe(1)
  })
}
