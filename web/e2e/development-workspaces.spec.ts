import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { expect, test, type Page } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

const workspacePath = '/development/workspaces'
const api = '/api/v1' + workspacePath
const id = '784bbbe8-e60d-4f2a-a839-c64c9f474504'
const runId = 'c42e390c-8cdd-4371-8afe-b20994d209ef'
const digest = 'a'.repeat(64)
const planDigest = 'b'.repeat(64)
const markdown = '# 开发空间运行报告\n\n已取消\n'
const report = {
  workspaceId: id,
  runId,
  outcome: 'Canceled',
  exitCode: null,
  finalizedAtUtc: '2026-11-09T13:00:00Z',
  retainUntilUtc: '2026-11-23T13:00:00Z',
  source: { fingerprint: null, gitCommit: null },
  environment: { digest, version: 3 },
  steps: [
    {
      stepId: runId,
      key: 'node-test',
      outcome: 'Canceled',
      exitCode: null,
      tests: [
        {
          artifactId: id,
          format: 'JUnit',
          integrity: 'Missing',
          collection: 'Unavailable',
          result: null,
        },
      ],
    },
  ],
}
const json = JSON.stringify(report)
const reportUrl = `${workspacePath}/${id}/reports/${runId}`

async function fixture(page: Page, developer = true) {
  await installTerminalFixture(page, { administrator: false })
  const state = {
    developer,
    reportAllowed: true,
    badDigest: false,
    registered: false,
    allocated: false,
    queued: false,
    canceled: false,
    loseQueueReply: true,
    queueKeys: [] as string[],
    allocationBody: null as unknown,
  }
  const runtime = { key: 'node', version: '22', image: 'registry.example/node@sha256:' + digest }
  const budget = { cpuMillis: 500, memoryMiB: 512, diskMiB: 1024 }
  const workspace = () => ({
    id,
    slug: 'browser-space',
    ownerSubject: 'preview-user',
    storageRootId: 'qa-root',
    state: state.allocated ? 'StorageAllocated' : 'Registered',
    version: state.allocated ? 3 : 1,
    budget,
    environment: { version: 1, runtimes: [runtime] },
    environmentSha256: digest,
  })
  const run = () => ({
    workspaceId: id,
    runId,
    runtime: 'node',
    task: 'test',
    state: state.canceled ? 'Canceled' : 'Running',
    phase: state.canceled ? 'CanceledAtHost' : 'DispatchCommitted',
    createdAtUtc: '2026-11-09T12:58:00Z',
    completedAtUtc: state.canceled ? report.finalizedAtUtc : null,
    cancellationRequested: state.canceled,
    errorCode: null,
    hostStatus: null,
    reportStatus: state.canceled ? 'Available' : null,
  })
  await page.route(
    (url) => url.pathname === api || url.pathname.startsWith(api + '/'),
    async (route) => {
      const request = route.request()
      const suffix = new URL(request.url()).pathname.slice(api.length)
      const fulfill = (body: unknown, status = 200) => route.fulfill({ status, json: body })
      if (suffix.startsWith(`/${id}/runs/${runId}/report/`)) {
        if (!state.reportAllowed) return fulfill({ code: 'WORKSPACE_REPORT_READER_REQUIRED' }, 403)
        const isJson = suffix.endsWith('/json')
        const body = isJson ? json : markdown
        const hash = state.badDigest
          ? '0'.repeat(64)
          : createHash('sha256').update(body).digest('hex')
        return route.fulfill({
          body,
          contentType: isJson ? 'application/json' : 'text/markdown',
          headers: { 'X-Content-SHA256': hash },
        })
      }
      if (!state.developer) return fulfill({ code: 'WORKSPACE_CREATE_PERMISSION_REQUIRED' }, 403)
      if (request.method() === 'POST')
        expect(request.headers()['x-xsrf-token']).toBe('ui-fixture-only')
      if (suffix === '/catalog')
        return fulfill({ runtimes: [runtime], storageRootIds: ['qa-root'], capacity: budget })
      if (suffix === '') {
        if (request.method() === 'GET')
          return fulfill({ items: state.registered ? [workspace()] : [], nextAfter: null })
        expect(request.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
        expect(request.postDataJSON()).toEqual({
          slug: 'browser-space',
          storageRootId: 'qa-root',
          runtimes: ['node'],
          budget,
        })
        state.registered = true
        return fulfill({ workspace: workspace(), replayed: false }, 201)
      }
      if (suffix === `/${id}`) return fulfill(workspace())
      if (suffix === `/${id}/storage-plan`)
        return fulfill({
          workspaceId: id,
          storageRootId: 'qa-root',
          environmentSha256: digest,
          diskMiB: 1024,
          planSha256: planDigest,
        })
      if (suffix === `/${id}/storage-allocation`) {
        state.allocationBody = request.postDataJSON()
        state.allocated = true
        return fulfill(workspace())
      }
      if (suffix === `/${id}/runner-preflight`)
        return fulfill({
          workspaceId: id,
          workspaceVersion: 3,
          environmentSha256: digest,
        })
      if (suffix === `/${id}/runs`) {
        if (request.method() === 'GET')
          return fulfill({ items: state.queued ? [run()] : [], nextAfter: null })
        state.queueKeys.push(request.headers()['idempotency-key'] ?? '')
        expect(request.postDataJSON()).toEqual({ runtime: 'node', task: 'test' })
        state.queued = true
        if (state.loseQueueReply) {
          state.loseQueueReply = false
          return route.abort('failed')
        }
        return fulfill(run(), 202)
      }
      if (suffix === `/${id}/runs/${runId}`) return fulfill(run())
      if (suffix === `/${id}/runs/${runId}/cancel`) {
        state.canceled = true
        return fulfill(run(), 202)
      }
      return fulfill({ code: 'FIXTURE_PATH_UNKNOWN' }, 404)
    },
  )
  return state
}

test('registers a space, confirms storage, recovers a lost submission and downloads a report', async ({
  page,
}, info) => {
  const state = await fixture(page)
  await page.goto(workspacePath)
  await expect(page.getByText('暂无可见的开发空间')).toBeVisible()
  await page.getByRole('link', { name: '登记开发空间', exact: true }).click()
  await page.getByRole('textbox', { name: '空间标识' }).fill('browser-space')
  await page.getByText('node 22', { exact: true }).click()
  await expect(page.getByRole('checkbox', { name: 'node 22' })).toBeChecked()
  await page.getByRole('button', { name: '登记空间', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'browser-space' })).toBeVisible()
  await page.getByRole('button', { name: '核对存储计划' }).click()
  await expect(page.getByText(planDigest, { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '确认分配存储' }).click()
  await expect(page.getByRole('button', { name: '核验 Runner 存储' })).toBeVisible()
  expect(state.allocationBody).toEqual({ planSha256: planDigest })
  await page.getByRole('button', { name: '核验 Runner 存储' }).click()
  await expect(page.getByText('Runner 存储和配额核验通过。')).toBeVisible()
  await page.getByRole('link', { name: '运行记录', exact: true }).click()
  await page.getByRole('button', { name: '提交运行' }).click()
  await page.getByRole('button', { name: '使用原任务重试' }).click()
  await expect(page).toHaveURL(new RegExp('/runs/' + runId + '$'))
  expect(state.queueKeys).toHaveLength(2)
  expect(state.queueKeys[0]).toMatch(/^[0-9a-f-]{36}$/)
  expect(state.queueKeys[0]).toBe(state.queueKeys[1])
  await expect(page.getByText('运行中', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '取消运行' }).click()
  await expect(page.getByText('已取消', { exact: true })).toBeVisible()
  await page.getByRole('link', { name: '返回运行记录' }).click()
  await page.reload()
  await expect(page.getByRole('link', { name: 'node · test' })).toBeVisible()
  await page.getByRole('link', { name: '运行报告', exact: true }).click()
  await expect(page.getByRole('heading', { name: '运行报告' })).toBeVisible()
  await expect(page.getByText('已取消', { exact: true })).toBeVisible()
  for (const [label, extension, expected] of [
    ['JSON', 'json', json],
    ['Markdown', 'md', markdown],
  ]) {
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: '下载 ' + label }).click()
    const saved = await download
    expect(saved.suggestedFilename()).toBe(`workspace-run-${runId}.${extension}`)
    expect(await readFile((await saved.path())!, 'utf8')).toBe(expected)
  }
  await page.screenshot({ path: info.outputPath('workspace-report-desktop.png') })
})

test('guards the workspace and verifies delegated report downloads on a narrow viewport', async ({
  page,
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  const state = await fixture(page, false)
  await page.goto(workspacePath)
  await expect(page.getByText(/当前账号没有这项开发空间权限/)).toBeVisible()
  await expect(page.getByRole('link', { name: '登记开发空间' })).toHaveCount(0)
  state.badDigest = true
  await page.goto(reportUrl)
  await expect(page.getByText('报告完整性核验失败。')).toBeVisible()
  await expect(page.getByRole('button', { name: '下载 JSON' })).toHaveCount(0)
  state.badDigest = false
  await page.getByRole('button', { name: '重新读取' }).click()
  await expect(page.getByText('运行结论')).toBeVisible()
  const viewport = await page.evaluate(() => ({
    width: window.innerWidth,
    content: document.documentElement.scrollWidth,
  }))
  expect(viewport.content).toBeLessThanOrEqual(viewport.width)
  await page.screenshot({ path: info.outputPath('workspace-report-mobile.png') })
  await page.locator('.theme-switch .el-switch__core').click({ timeout: 5000 })
  await expect(page.locator('html')).toHaveClass(/\bdark\b/)
  await expect(page.getByText('运行结论')).toBeVisible()
  await page.screenshot({ path: info.outputPath('workspace-report-mobile-dark.png') })
  state.reportAllowed = false
  await page.getByRole('button', { name: '下载 Markdown' }).click()
  await expect(page.getByText(/当前账号没有这项开发空间权限/)).toBeVisible()
  await expect(page.getByText('运行结论')).toHaveCount(0)
})
