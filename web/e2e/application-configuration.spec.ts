import { expect, test } from '@playwright/test'
import { configurationResponse } from './fixtures/configuration.mjs'

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) =>
    route.fulfill({ json: configurationResponse(route.request().url()) }),
  )
})

for (const width of [1440, 1045, 390, 320])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: configuration form, dense overrides and footer fit`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await page.goto('/applications?configure=application%3Ademo')
      const dialog = page.getByRole('dialog', { name: '应用参数', exact: true })
      await expect(dialog.getByRole('spinbutton', { name: 'app CPU 上限' })).toBeVisible()
      await dialog.getByRole('tab', { name: 'sidecar', exact: true }).click()
      await expect(dialog.getByRole('spinbutton', { name: 'sidecar CPU 上限' })).toBeVisible()
      await dialog.getByRole('tab', { name: 'app', exact: true }).click()
      await dialog.getByRole('button', { name: '添加挂载', exact: true }).first().click()
      await dialog.getByRole('button', { name: '添加环境覆盖', exact: true }).click()
      await dialog.getByRole('textbox', { name: '环境变量 1 名称' }).fill('NEW')
      await dialog.getByRole('textbox', { name: '环境变量 1 新值' }).fill('value\nsecond line')
      await expect(dialog.getByRole('button', { name: '预检并提交', exact: true })).toBeVisible()
      const bounds = await dialog.evaluate((element) => ({
        right: element.getBoundingClientRect().right,
        left: element.getBoundingClientRect().left,
        bodyScroll: element.querySelector('.configuration-editor')!.scrollWidth,
        bodyWidth: element.querySelector('.configuration-editor')!.clientWidth,
        page: document.documentElement.scrollWidth,
        viewport: innerWidth,
      }))
      expect(bounds.left).toBeGreaterThanOrEqual(0)
      expect(bounds.right).toBeLessThanOrEqual(width + 1)
      expect(bounds.bodyScroll).toBeLessThanOrEqual(bounds.bodyWidth + 1)
      expect(bounds.page).toBeLessThanOrEqual(bounds.viewport + 1)
      await dialog.getByRole('button', { name: '取消', exact: true }).click()
      await expect(dialog).not.toBeVisible()
      expect(
        await page
          .locator('.installed-row')
          .last()
          .getByRole('button', { name: '参数', exact: true })
          .count(),
      ).toBe(0)
    })

for (const apply of [false, true])
  test(`${apply ? 'redeploy' : 'save only'}: confirmed submission stages the full candidate, not job plaintext`, async ({
    page,
  }) => {
    let candidate: Record<string, unknown> | null = null
    let queued: Record<string, unknown> | null = null
    await page.route('**/api/v1/secrets/stage', (route) => {
      candidate = JSON.parse(route.request().postDataJSON().value)
      return route.fulfill({ json: configurationResponse(route.request().url()) })
    })
    await page.route('**/api/v1/applications/configure', (route) => {
      queued = route.request().postDataJSON()
      return route.fulfill({ json: configurationResponse(route.request().url()) })
    })
    await page.goto('/applications?configure=application%3Ademo')
    const dialog = page.getByRole('dialog', { name: '应用参数', exact: true })
    await dialog.getByRole('spinbutton', { name: 'app CPU 上限' }).fill('1.5')
    await dialog.getByRole('button', { name: '添加环境覆盖', exact: true }).click()
    await dialog.getByRole('textbox', { name: '环境变量 1 名称' }).fill('LITERAL')
    await dialog.getByRole('textbox', { name: '环境变量 1 新值' }).fill('$BASE "quote"\n中文')
    if (apply) await dialog.getByText('保存并重部署', { exact: true }).click()
    await dialog.getByRole('button', { name: '预检并提交', exact: true }).click()
    const confirmation = page.getByRole('dialog', {
      name: apply ? '保存配置并重部署' : '仅保存配置',
      exact: true,
    })
    await expect(confirmation).toBeVisible()
    expect(queued).toBeNull()
    await confirmation.getByRole('button', { name: '确认提交', exact: true }).click()
    await expect(dialog).not.toBeVisible()
    expect(candidate).toMatchObject({
      services: { app: { cpus: 1.5 } },
      environment: { set: { LITERAL: '$BASE "quote"\n中文' } },
      apply,
    })
    expect(queued).toMatchObject({
      parameters: {
        slug: 'demo',
        inputTicket: 'b'.repeat(64),
        inputKind: 'environment',
        expectedFingerprint: 'a'.repeat(64),
      },
      confirmed: true,
      planHash: 'c'.repeat(64),
    })
    expect(JSON.stringify(queued)).not.toContain('LITERAL')
    expect(JSON.stringify(queued)).not.toContain('cpus')
  })

test('canceling confirmation never enqueues and reopening clears entered values', async ({
  page,
}) => {
  let queued = 0
  page.on('request', (request) => {
    if (request.url().endsWith('/applications/configure')) queued++
  })
  await page.goto('/applications?configure=application%3Ademo')
  const dialog = page.getByRole('dialog', { name: '应用参数', exact: true })
  await dialog.getByRole('button', { name: '添加环境覆盖', exact: true }).click()
  await dialog.getByRole('textbox', { name: '环境变量 1 名称' }).fill('FRESH')
  await dialog.getByRole('textbox', { name: '环境变量 1 新值' }).fill('not-retained')
  await dialog.getByRole('button', { name: '预检并提交', exact: true }).click()
  await page
    .getByRole('dialog', { name: '仅保存配置', exact: true })
    .getByRole('button', { name: '取消', exact: true })
    .click()
  expect(queued).toBe(0)
  await dialog.getByRole('button', { name: '取消', exact: true }).click()
  await page.getByRole('button', { name: '参数', exact: true }).click()
  await expect(dialog.getByRole('textbox', { name: '环境变量 1 新值' })).toHaveCount(0)
})

test('failed preflight remains editable, permission loss closes and clears', async ({ page }) => {
  await page.route('**/api/v1/applications/configuration/plan', (route) =>
    route.fulfill({ status: 409, json: { title: '配置已变化，请重新读取' } }),
  )
  await page.goto('/applications?configure=application%3Ademo')
  const dialog = page.getByRole('dialog', { name: '应用参数', exact: true })
  await dialog.getByRole('button', { name: '预检并提交', exact: true }).click()
  await expect(dialog.getByRole('alert').filter({ hasText: '配置已变化' })).toBeVisible()
  await page.route('**/api/v1/applications/configuration?*', (route) =>
    route.fulfill({ status: 403, json: { title: '管理权限失效' } }),
  )
  await dialog.getByRole('button', { name: '重新读取配置', exact: true }).click()
  await expect(dialog).not.toBeVisible()
})
