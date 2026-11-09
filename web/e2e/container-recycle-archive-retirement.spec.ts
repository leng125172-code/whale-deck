import { createHash } from 'node:crypto'
import { expect, test, type Page } from '@playwright/test'
import { installRecycleArchiveFixture } from './fixtures/container-recycle-archives.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

const intentId = 'aa0a8865-343e-49bd-b01a-72680131bf39'
const archiveId = 'edb98883-7abe-44fc-9335-4b3024fac09a'
const bytes = createHash('sha256')
  .update(
    'whaledeck-container-recycle-archive-retirement-business-v1:execution:' +
      intentId.replace(/-/g, ''),
  )
  .digest()
const hex = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
  .map((i) => bytes[i]!.toString(16).padStart(2, '0'))
  .join('')
const execution = `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
const token = `c2FmZS1maXh0dXJl.${'A'.repeat(64)}`
const dialog = (page: Page) => page.getByRole('dialog', { name: '归档退役', exact: true })

async function fixture(page: Page, loseQueue = false, unknown = false) {
  const legacy = await installRecycleArchiveFixture(page)
  const cid = legacy.source.location.containerId
  const base = `/api/v1/containers/${cid}/files/recycle-archive-retirements`
  const readyExpiresAtUtc = new Date(Date.now() + 240000).toISOString()
  const notBeforeUnixMs = Date.now() - 10000
  const requests: { method: string; suffix: string; key: string | undefined; body: unknown }[] = []
  let key: string | undefined
  let state = unknown ? 'Unknown' : 'Ready'
  let version = 3
  function response(recovery = false) {
    const applied = state === 'Succeeded'
    const executed = ['Succeeded', 'Unknown', 'Acknowledged'].includes(state)
    return {
      id: intentId,
      planningJobId: intentId,
      executionJobId: executed ? execution : null,
      archiveIntentId: archiveId,
      retainDays: 30,
      state,
      version,
      readyExpiresAtUtc,
      notBeforeUnixMs,
      archiveBytes: 128,
      retirementEligible: true,
      reservationRecorded: executed,
      applyStarted: executed,
      publishBarrier: state === 'Unknown',
      retirementApplied: applied ? true : null,
      rootState: applied ? 'Completed' : executed ? 'Unknown' : null,
      resultCode: null,
      confirmation:
        state === 'Ready' && !recovery ? { token, expiresAtUtc: readyExpiresAtUtc } : null,
    }
  }
  await page.route(
    (url) => url.pathname === base || url.pathname.startsWith(base + '/'),
    async (route) => {
      const request = route.request()
      const suffix = new URL(request.url()).pathname.slice(base.length)
      requests.push({
        method: request.method(),
        suffix,
        key: request.headers()['idempotency-key'],
        body: request.postDataJSON(),
      })
      if (request.method() === 'POST' && suffix === '') {
        key = request.headers()['idempotency-key']
        if (loseQueue) await route.abort('failed')
        else await route.fulfill({ status: 202, json: response() })
        return
      }
      if (suffix.startsWith('/by-key/') && suffix !== `/by-key/${key}`) {
        await route.fulfill({ status: 404, json: { code: 'RESOURCE_NOT_FOUND' } })
        return
      }
      if (suffix === `/${intentId}/confirm`) {
        state = 'Succeeded'
        version++
        await route.fulfill({ status: 202, json: response() })
        return
      }
      if (suffix === `/${intentId}/cancel`) {
        state = 'Canceled'
        version++
        await route.fulfill({ status: 202, json: { id: intentId, state: 'Canceled' } })
        return
      }
      if (suffix === `/${intentId}/recovery/acknowledge`) {
        state = 'Acknowledged'
        version++
        await route.fulfill({ status: 202, json: { id: intentId, state: 'Acknowledged' } })
        return
      }
      await route.fulfill({ json: response(suffix.endsWith('/recovery')) })
    },
  )
  await page.route(
    (url) => url.pathname === '/api/v1/jobs' || url.pathname === `/api/v1/jobs/${execution}`,
    async (route) => {
      const facts = {
        schemaVersion: 1,
        intentId,
        jobId: execution,
        archiveIntentId: archiveId,
        containerId: cid,
        retainDays: 30,
        state,
        code:
          'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_' +
          (state === 'Unknown' ? 'RESULT_UNKNOWN' : 'UNKNOWN_ACKNOWLEDGED'),
        notBeforeUnixMs,
        reservationStarted: true,
        reservationRecorded: true,
        applyStarted: true,
        publishBarrier: state === 'Unknown',
        receiptState: 'Unknown',
        retirementApplied: null,
      }
      const job = {
        id: execution,
        jobType: 'containers.files.recycle-archive-retirement',
        state: state === 'Unknown' ? 'Running' : 'Failed',
        phase: state,
        progressPercent: null,
        errorCode: null,
        createdAtUtc: readyExpiresAtUtc,
        completedAtUtc: null,
        resultJson: JSON.stringify(facts),
      }
      await route.fulfill({
        json: new URL(route.request().url()).pathname.endsWith('/jobs') ? [job] : job,
      })
    },
  )
  return { requests }
}

async function openFiles(page: Page) {
  await page.goto('/containers')
  await page
    .locator('.container-card')
    .filter({ hasText: 'demo-web' })
    .getByRole('button', { name: '详情', exact: true })
    .click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  await page.getByRole('button', { name: '管理归档退役', exact: true }).click()
}

async function prepare(page: Page) {
  await dialog(page).getByPlaceholder('可从归档恢复任务摘要复制').fill(archiveId)
  await dialog(page).getByRole('spinbutton').fill('30')
  await dialog(page).getByRole('button', { name: '登记退役预检', exact: true }).click()
}

for (const [width, colorScheme] of [
  [320, 'dark'],
  [1440, 'light'],
] as const) {
  test(`${width} ${colorScheme}: retirement preview and explicit confirmation`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 650 })
    await page.emulateMedia({ colorScheme })
    const { requests } = await fixture(page)
    await openFiles(page)
    await prepare(page)
    await dialog(page).getByRole('button', { name: '准备确认退役', exact: true }).click()
    const submit = dialog(page).getByRole('button', { name: '提交明确确认', exact: true })
    await expect(submit).toBeDisabled()
    await expect.poll(() => requests.filter((r) => r.method === 'POST').length).toBe(1)
    await dialog(page)
      .getByText('我确认按当前预检和留存策略删除这份 HDD 数据', { exact: true })
      .click()
    await submit.click()
    await expect(dialog(page).getByText('退役完成', { exact: true })).toBeVisible()
    expect(requests.filter((r) => r.suffix.endsWith('/confirm'))).toEqual([
      {
        method: 'POST',
        suffix: `/${intentId}/confirm`,
        key: intentId,
        body: { planHash: token, confirmed: true, version: 3 },
      },
    ])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(
      await page.evaluate(() =>
        sessionStorage.getItem('whaledeck-container-recycle-archive-retirement-hints-v1'),
      ),
    ).not.toContain(token)
  })
}
test('lost registration reopens by original key without repeating writes', async ({ page }) => {
  const { requests } = await fixture(page, true)
  await openFiles(page)
  await prepare(page)
  await expect(dialog(page).getByRole('alert').last()).toContainText('结果未核实')
  const initial = requests.find((r) => r.method === 'POST')!
  await openFiles(page)
  await expect(dialog(page).getByText('预检就绪', { exact: true })).toBeVisible()
  expect(requests.filter((r) => r.method === 'POST')).toHaveLength(1)
  expect(requests.some((r) => r.suffix === `/by-key/${initial.key}`)).toBe(true)
})
test('cancel uses the dedicated original intent and reads back its outcome', async ({ page }) => {
  const { requests } = await fixture(page)
  await openFiles(page)
  await prepare(page)
  await dialog(page).getByRole('button', { name: '请求取消', exact: true }).click()
  const submit = dialog(page).getByRole('button', { name: '提交明确确认', exact: true })
  await expect(submit).toBeDisabled()
  await dialog(page).getByRole('checkbox').focus()
  await page.keyboard.press('Space')
  await submit.click()
  await expect(dialog(page).getByText('已取消', { exact: true })).toBeVisible()
  expect(
    requests.filter((request) => request.method === 'POST').map((request) => request.suffix),
  ).toEqual(['', `/${intentId}/cancel`])
  expect(requests.at(-1)).toMatchObject({ method: 'GET', suffix: `/${intentId}` })
})
test('Jobs opens the original Unknown and acknowledges with no retry', async ({ page }) => {
  const { requests } = await fixture(page, false, true)
  await page.goto(`/jobs?jobId=${execution}`)
  const result = page.getByRole('region', { name: '归档退役安全结果', exact: true })
  await expect(result.getByText('结果未知，保护仍生效')).toBeVisible()
  await expect(page.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
  await result.getByRole('button', { name: '核实并处置归档退役', exact: true }).click()
  await dialog(page).getByRole('button', { name: '准备明确处置', exact: true }).click()
  await dialog(page).getByRole('checkbox').focus()
  await page.keyboard.press('Space')
  await dialog(page).getByRole('button', { name: '提交明确确认', exact: true }).click()
  await expect(result.getByText('已明确处置', { exact: true })).toBeVisible()
  expect(requests.filter((r) => r.method === 'POST')).toEqual([
    {
      method: 'POST',
      suffix: `/${intentId}/recovery/acknowledge`,
      key: intentId,
      body: { confirmed: true, version: 3 },
    },
  ])
})
