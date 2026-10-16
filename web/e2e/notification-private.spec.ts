import { expect, test, type Page } from '@playwright/test'

const pageErrors = new WeakMap<Page, string[]>()
test.beforeEach(({ page }) => {
  const errors: string[] = []
  pageErrors.set(page, errors)
  page.on('pageerror', (error) => errors.push(error.message))
})
test.afterEach(({ page }) => expect(pageErrors.get(page)).toEqual([]))

const channel = {
  id: '59a29b72-dfbb-48c9-9323-c1688eaa3d71',
  name: 'Private fixture',
  provider: 'Webhook',
  destinationReference: 'approved-target',
  hasCredential: true,
  isEnabled: false,
  privateConfigurationBlocked: false,
  version: 2,
  createdAtUtc: '2026-10-14T01:00:00Z',
}
const privatePath = `notifications/manage/channels/${channel.id}/private`
const jobId = 'e8e95c77-eaad-4d2c-8a6d-29d00f5606ec'
async function fixture(
  page: Page,
  options: { provider?: string; forbidden?: boolean; unknown?: boolean } = {},
) {
  const writes: { path: string; key: string; body: string }[] = []
  let submitted = false
  let complete = false
  let hasTarget = true
  let denied = false
  let ackAttempts = 0
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1/', '')
    if (request.method() !== 'GET')
      writes.push({
        path,
        key: request.headers()['idempotency-key'] || '',
        body: request.postData() || '',
      })
    if (path === 'auth/csrf')
      return route.fulfill({ json: { headerName: 'X-CSRF', token: 'fixture-only' } })
    if (path === privatePath + '/status') {
      if (options.forbidden && denied)
        return route.fulfill({ status: 403, json: { title: 'Permission denied' } })
      return route.fulfill({
        json: options.unknown
          ? {
              jobId,
              state: 'Unknown',
              version: 4,
              resultCode: 'Unknown',
              canAcknowledgeUnknown: ackAttempts === 0,
            }
          : submitted
            ? {
                jobId,
                state: complete ? 'Succeeded' : 'Running',
                version: 3,
                resultCode: null,
                canAcknowledgeUnknown: false,
              }
            : null,
      })
    }
    if (path === privatePath && request.method() === 'GET')
      return route.fulfill({
        json: { channelVersion: 7, revision: 'redacted-revision', hasTarget, hasCredential: true },
      })
    if (path === 'secrets/stage') return route.fulfill({ json: { token: 'opaque-fixture-ticket' } })
    if (path === privatePath + '/plan') {
      const body = request.postDataJSON()
      hasTarget = body.action !== 'remove'
      return route.fulfill({
        json: {
          planHash: 'opaque-plan',
          expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          warning: '渠道将保持停用',
        },
      })
    }
    if (path === privatePath && request.method() === 'POST') {
      const count = writes.filter((write) => write.path === privatePath).length
      if (count === 1) return route.abort('failed')
      submitted = true
      return route.fulfill({ status: 202, json: { id: jobId, state: 'Pending', phase: 'Queued' } })
    }
    if (path === privatePath + '/acknowledge-unknown') {
      if (++ackAttempts === 1) return route.abort('failed')
      options.unknown = false
      return route.fulfill({ status: 204 })
    }
    if (path === `jobs/${jobId}`)
      return route.fulfill({
        json: { id: jobId, state: complete ? 'Succeeded' : 'Running', phase: 'AwaitingReceipt' },
      })
    if (path === 'notifications/manage/channels')
      return route.fulfill({
        json: {
          items: [{ ...channel, provider: options.provider || channel.provider }],
          hasMore: false,
          page: 1,
          take: 25,
        },
      })
    if (path === 'notifications/summary')
      return route.fulfill({ json: { unreadCount: 0, totalCount: 0, snapshotSequence: 0 } })
    if (path.startsWith('notifications/manage/'))
      return route.fulfill({ json: { items: [], hasMore: false, page: 1, take: 25 } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/settings/notifications')
  await page.getByRole('button', { name: '私有配置', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '私有配置 · Private fixture', exact: true })
  await expect(dialog.getByRole('button', { name: '核对状态' })).toBeEnabled()
  return {
    dialog,
    writes,
    complete: () => {
      complete = true
    },
    deny: () => {
      denied = true
    },
  }
}

for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: private plan, frozen retry, status and cleared inputs`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      const { dialog, writes, complete } = await fixture(page)
      const url = dialog.getByLabel('私有目标地址')
      await expect(url).toHaveValue('')
      await url.fill('https://example.invalid/events?fixture=not-real-secret')
      await dialog.getByRole('button', { name: '准备配置计划' }).click()
      await expect(dialog.getByText('计划已准备，秘密输入已清空', { exact: true })).toBeVisible()
      await expect(url).toHaveValue('')
      await expect(url).toBeDisabled()
      expect(writes.filter((write) => write.path === privatePath)).toHaveLength(0)
      expect(
        JSON.parse(writes.find((write) => write.path.endsWith('/plan'))!.body)
          .expectedChannelVersion,
      ).toBe(7)
      await dialog.getByRole('button', { name: '确认并提交' }).click()
      await page.getByRole('button', { name: '停用渠道并提交', exact: true }).click()
      await expect(
        dialog.getByText('提交响应可能丢失，原请求已冻结', { exact: true }),
      ).toBeVisible()
      expect(writes.filter((write) => write.path === privatePath)).toHaveLength(1)
      await page.waitForTimeout(250)
      expect(writes.filter((write) => write.path === privatePath)).toHaveLength(1)
      await dialog.getByRole('button', { name: '重试原请求', exact: true }).click()
      await expect(dialog.getByText('任务 ID：' + jobId, { exact: true })).toBeVisible()
      const submitted = writes.filter((write) => write.path === privatePath)
      expect(submitted).toHaveLength(2)
      expect(submitted[0]).toEqual(submitted[1])
      expect(submitted[0]!.key).toMatch(/^[a-f0-9-]{36}$/)
      expect(submitted[0]!.body).not.toContain('example.invalid')
      expect(writes.some((write) => /enable|test$/.test(write.path))).toBe(false)
      await expect(url).toBeDisabled()
      complete()
      await dialog.getByRole('button', { name: '核对状态' }).click()
      await expect(
        dialog.getByText('配置任务已完成，渠道仍保持停用', { exact: true }),
      ).toBeVisible()
      await expect(url).toBeEnabled()
      const footer = await dialog.getByRole('button', { name: '关闭并清空' }).boundingBox()
      expect(footer).not.toBeNull()
      expect(footer!.y + footer!.height).toBeLessThanOrEqual(1000)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({
        path: `test-results/notification-private-${width}-${colorScheme}.png`,
        scale: 'css',
      })
      await url.fill('https://example.invalid/unsaved')
      await dialog.getByRole('button', { name: '关闭并清空' }).click()
      await expect(dialog).not.toBeVisible()
      await page.getByRole('button', { name: '私有配置', exact: true }).click()
      await expect(page.getByRole('dialog').getByLabel('私有目标地址')).toHaveValue('')
      expect(
        await page.evaluate(() =>
          [localStorage, sessionStorage].some((storage) =>
            Object.values(storage).some((value) => String(value).includes('example.invalid')),
          ),
        ),
      ).toBe(false)
    })

test('removal never stages a secret and never enables sending', async ({ page }) => {
  const { dialog, writes, complete } = await fixture(page)
  await dialog.getByRole('button', { name: '准备清除配置' }).click()
  await dialog.getByRole('button', { name: '确认并提交' }).click()
  await page.getByRole('button', { name: '停用渠道并提交' }).click()
  await expect(dialog.getByRole('button', { name: '重试原请求' })).toBeVisible()
  await dialog.getByRole('button', { name: '重试原请求' }).click()
  await expect(dialog.getByText('任务 ID：' + jobId, { exact: true })).toBeVisible()
  complete()
  await dialog.getByRole('button', { name: '核对状态' }).click()
  await expect(dialog.getByText('私有配置已清除，发送继续阻断', { exact: true })).toBeVisible()
  expect(writes.some((write) => write.path === 'secrets/stage')).toBe(false)
  expect(
    JSON.parse(writes.find((write) => write.path === privatePath)!.body).inputTicket,
  ).toBeNull()
})

test('403 clears the private form instead of leaving credentials visible', async ({ page }) => {
  const { dialog, deny } = await fixture(page, { forbidden: true })
  await dialog.getByLabel('私有目标地址').fill('https://example.invalid/unsaved')
  deny()
  await dialog.getByRole('button', { name: '核对状态' }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.getByText('需要管理员权限', { exact: true })).toBeVisible()
})

test('email uses a structured SMTP form and clears replacement input after staging', async ({
  page,
}) => {
  const { dialog, writes } = await fixture(page, { provider: 'Email' })
  await dialog.getByLabel('SMTP 主机').fill('smtp.example.invalid')
  await dialog.getByLabel('发件邮箱').fill('sender@example.invalid')
  await dialog
    .getByLabel('收件邮箱（最多 10 个，逗号或换行分隔）')
    .fill('recipient@example.invalid')
  await dialog.getByText('保留已登记凭据（不回显）', { exact: true }).click()
  await page.getByRole('option', { name: '替换：全部重新输入' }).click()
  await dialog.getByLabel('SMTP 用户名').fill('fixture-only')
  await dialog.getByLabel('SMTP 密码').fill('fixture-not-a-real-password')
  await dialog.getByRole('button', { name: '准备配置计划' }).click()
  await expect(dialog.getByText('计划已准备，秘密输入已清空', { exact: true })).toBeVisible()
  const staged = JSON.parse(
    JSON.parse(writes.find((write) => write.path === 'secrets/stage')!.body).value,
  )
  expect(staged.target.tls).toBe('StartTls')
  expect(staged.credential.username).toBe('fixture-only')
  expect(staged.credential.password).toBe('fixture-not-a-real-password')
  await expect(dialog.getByLabel('SMTP 主机')).toHaveValue('')
  await expect(dialog.getByLabel('SMTP 密码')).toHaveCount(0)
})

test('unknown acknowledgement retries the frozen original after eligibility changes', async ({
  page,
}) => {
  const { dialog, writes } = await fixture(page, { unknown: true })
  await expect(dialog.getByText('宿主结果未知，不要盲目重复创建', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认未知结果', exact: true }).click()
  await page.getByRole('button', { name: '接受未知结果', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '重试原确认请求' })).toBeVisible()
  await dialog.getByRole('button', { name: '核对状态' }).click()
  await dialog.getByRole('button', { name: '重试原确认请求' }).click()
  await page.getByRole('button', { name: '接受未知结果', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '重试原确认请求' })).toHaveCount(0)
  const acks = writes.filter((write) => write.path.endsWith('/acknowledge-unknown'))
  expect(acks).toHaveLength(2)
  expect(acks[0]).toEqual(acks[1])
  expect(JSON.parse(acks[0]!.body)).toEqual({
    operationId: jobId,
    expectedVersion: 4,
    confirmed: true,
    acceptUnknownOutcome: true,
  })
})
