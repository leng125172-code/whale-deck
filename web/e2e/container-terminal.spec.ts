import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

// UI-only transport fixtures. Real Docker/UDS acceptance is recorded separately.
const target = {
  containerId: 'a'.repeat(64),
  instanceId: '10:2026-10-16T00:00:00Z',
  name: 'ui-only-demo',
  imageId: 'image',
  configuredUser: '1000:1000',
}
for (const width of [320, 390, 1045, 1440, 1920]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: exact container confirmation, input, resize and exit`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const control = await installTerminalFixture(page, { containerTarget: target })
      await page.goto(`/containers/${target.containerId}/terminal`)
      await expect(page.getByRole('heading', { name: '容器终端', exact: true })).toBeVisible()
      expect(control.state.inspections).toBe(0)
      expect(control.state.writes).toHaveLength(0)
      await page.getByRole('button', { name: '开始连接', exact: true }).click()
      await expect(page.getByText('配置用户：1000:1000。', { exact: false })).toBeVisible()
      await page.getByRole('button', { name: '取消', exact: true }).click()
      expect(control.state.writes).toHaveLength(0)
      await page.getByRole('button', { name: '开始连接', exact: true }).click()
      await page.getByRole('button', { name: '确认连接', exact: true }).click()
      await expect(page.getByText('已连接 · 容器配置身份', { exact: true })).toBeVisible()
      const grant = control.state.writes.find(
        (item: { path: string }) => item.path === 'terminals/containers/grants',
      )
      expect(grant?.body.containerId).toBe(target.containerId)
      expect(grant?.body.instanceId).toBe(target.instanceId)
      expect(grant?.body.shell).toBe('sh')
      const input = page.getByRole('textbox', { name: '容器终端输入', exact: true })
      await input.pressSequentially('hello')
      await input.press('Enter')
      await expect(page.locator('.xterm-rows')).toContainText('收到输入: hello')
      await page.keyboard.insertText('中文🙂')
      await input.press('Enter')
      await expect(page.locator('.xterm-rows')).toContainText('中文🙂')
      await page.screenshot({ path: `test-results/container-terminal-${width}-${colorScheme}.png` })
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await input.pressSequentially('exit 7')
      await input.press('Enter')
      await expect(page.getByText('进程已结束 · 退出码 7', { exact: true })).toBeVisible()
      expect(control.state.sockets).toBe(1)
      expect(errors).toEqual([])
    })
  }
}

test('failed inspection never opens a confirmation or grants a session', async ({ page }) => {
  const control = await installTerminalFixture(page, {
    containerTarget: target,
    inspectStatus: 403,
  })
  await page.goto(`/containers/${target.containerId}/terminal`)
  await page.getByRole('button', { name: '开始连接', exact: true }).click()
  await expect(page.getByText('无法连接此容器。', { exact: false })).toBeVisible()
  expect(control.state.writes).toHaveLength(0)
  expect(control.state.sockets).toBe(0)
})

test('changing container routes disposes the previous session and requires a fresh confirmation', async ({
  page,
}) => {
  const control = await installTerminalFixture(page, { containerTarget: target })
  await page.goto(`/containers/${target.containerId}/terminal`)
  await page.getByRole('button', { name: '开始连接', exact: true }).click()
  await page.getByRole('button', { name: '确认连接', exact: true }).click()
  await expect(page.getByText('已连接 · 容器配置身份', { exact: true })).toBeVisible()
  await page.evaluate(() => {
    history.pushState({}, '', '/containers/' + 'b'.repeat(64) + '/terminal')
    dispatchEvent(new PopStateEvent('popstate'))
  })
  await expect(page.getByRole('button', { name: '开始连接', exact: true })).toBeVisible()
  await expect
    .poll(
      () =>
        control.state.writes.filter((item: { path: string }) => item.path.endsWith('/close'))
          .length,
    )
    .toBe(1)
  expect(control.state.sockets).toBe(1)
})
