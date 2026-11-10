import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { crc32 } from 'node:zlib'
import { expect, test, type Page } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'
import type { WorkspaceArchive } from '../src/services/workspaceArchives'
import type { WorkspaceRetirement } from '../src/services/workspaceRetirements'

test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })

const workspacePath = '/development/workspaces'
const api = '/api/v1' + workspacePath
const id = '784bbbe8-e60d-4f2a-a839-c64c9f474504'
const runId = 'c42e390c-8cdd-4371-8afe-b20994d209ef'
const archiveId = 'a44984f7-2960-458b-91a5-4bb96f553d77'
const retirementId = 'b44984f7-2960-458b-91a5-4bb96f553d77'
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
    lifecycleState: 'Active',
    lifecycleVersion: 1,
    lifecycleKeys: [] as string[],
    loseLifecycleReply: false,
    archive: null as WorkspaceArchive | null,
    retirement: null as WorkspaceRetirement | null,
    retirementKeys: [] as string[],
    retirementConfirmations: [] as unknown[],
    archiveKeys: [] as string[],
    archiveConfirmations: [] as string[],
    archiveReads: 0,
    archiveStatus: 200,
    loseArchiveReply: false,
    loseArchiveConfirmation: false,
    loseArchiveCancel: false,
    lifecycleAuthorization: false,
    lifecycleOperation: null as null | {
      operationId: string
      workspaceId: string
      action: string
      state: string
      phase: string
      code: string | null
      completedAtUtc: string | null
    },
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
    lifecycleState: state.lifecycleState,
    lifecycleVersion: state.lifecycleVersion,
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
      if (suffix.startsWith(`/${id}/retirements`)) {
        if (request.method() === 'GET') return fulfill(state.retirement)
        if (suffix === `/${id}/retirements`) {
          const key = request.headers()['idempotency-key'] ?? ''
          expect(key).toMatch(/^[a-f0-9-]{36}$/)
          expect(request.postDataJSON()).toEqual({
            archiveId,
            expectedEpoch: state.lifecycleVersion,
          })
          state.retirementKeys.push(key)
          state.retirement ??= retirementView(state.lifecycleVersion)
          state.lifecycleState = 'Retiring'
          return fulfill(state.retirement, 202)
        }
        if (suffix === `/${id}/retirements/${retirementId}/confirm` && state.retirement) {
          expect(request.postDataJSON()).toEqual({
            planSha256: planDigest,
            deleteSourceAndReleaseBudget: true,
          })
          state.retirementConfirmations.push(request.postDataJSON())
          state.retirement.phase = 'Execute'
          state.retirement.awaitingConfirmation = false
          return fulfill(state.retirement, 202)
        }
      }
      if (suffix.startsWith(`/${id}/archives`)) {
        if (request.method() === 'GET') {
          state.archiveReads++
          if (state.archiveStatus !== 200)
            return fulfill({ code: 'AUTH_FORBIDDEN' }, state.archiveStatus)
          return fulfill(state.archive)
        }
        if (suffix === `/${id}/archives`) {
          const key = request.headers()['idempotency-key'] ?? ''
          expect(key).toMatch(/^[a-f0-9-]{36}$/)
          expect(request.postDataJSON()).toEqual({ expectedEpoch: state.lifecycleVersion })
          if (!state.archiveKeys.includes(key)) {
            state.archiveKeys.push(key)
            state.archive = archiveView()
          }
          if (state.loseArchiveReply) {
            state.loseArchiveReply = false
            return route.abort('failed')
          }
          return fulfill(state.archive, 202)
        }
        if (suffix === `/${id}/archives/${archiveId}/confirm` && state.archive) {
          expect(request.postDataJSON()).toEqual({ planSha256: planDigest })
          if (!state.archiveConfirmations.includes(archiveId))
            state.archiveConfirmations.push(archiveId)
          state.archive.awaitingConfirmation = false
          state.archive.phase = 'Capture'
          if (state.loseArchiveConfirmation) {
            state.loseArchiveConfirmation = false
            return route.abort('failed')
          }
          return fulfill(state.archive, 202)
        }
        if (suffix === `/${id}/archives/${archiveId}/cancel` && state.archive) {
          state.archive.cancelRequested = true
          if (state.loseArchiveCancel) {
            state.loseArchiveCancel = false
            return route.abort('failed')
          }
          return fulfill(state.archive, 202)
        }
        if (suffix === `/${id}/archives/${archiveId}/authorize` && state.archive) {
          state.archive.authorizationRequired = false
          state.archive.code = null
          return fulfill(state.archive, 202)
        }
      }
      if (suffix === `/${id}/lifecycle/plan`) {
        const action = new URL(request.url()).searchParams.get('action')
        return fulfill({
          workspaceId: id,
          action,
          expectedVersion: state.lifecycleVersion,
          planSha256: planDigest,
          retainsData: true,
          reservesBudget: true,
          requiresIdle: true,
        })
      }
      if (suffix === `/${id}/lifecycle` && request.method() === 'POST') {
        const body = request.postDataJSON()
        expect(body.planSha256).toBe(planDigest)
        const key = request.headers()['idempotency-key'] ?? ''
        expect(key).toMatch(/^[a-f0-9-]{36}$/)
        if (!state.lifecycleKeys.includes(key)) {
          state.lifecycleKeys.push(key)
          state.lifecycleVersion++
          state.lifecycleState = body.action === 'stop' ? 'Stopping' : 'Resuming'
          state.lifecycleOperation = {
            operationId: runId,
            workspaceId: id,
            action: body.action,
            state: 'Running',
            phase: 'HostConfirmation',
            completedAtUtc: null,
            code: state.lifecycleAuthorization
              ? 'WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED'
              : null,
          }
        }
        if (state.loseLifecycleReply) {
          state.loseLifecycleReply = false
          return route.abort('failed')
        }
        return fulfill(state.lifecycleOperation, 202)
      }
      if (suffix === `/${id}/lifecycle/${runId}/authorize`) {
        state.lifecycleAuthorization = false
        if (state.lifecycleOperation) state.lifecycleOperation.code = null
        return fulfill(state.lifecycleOperation, 202)
      }
      if (suffix === `/${id}/lifecycle` || suffix === `/${id}/lifecycle/${runId}`) {
        const operation = state.lifecycleOperation
        if (operation && !state.lifecycleAuthorization) {
          state.lifecycleState = operation.action === 'stop' ? 'Stopped' : 'Active'
          operation.state = 'Succeeded'
          operation.phase = state.lifecycleState
          operation.completedAtUtc = '2026-11-10T06:00:00Z'
        }
        return fulfill(operation)
      }
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

// Synthetic metadata exercises the production Vue bundle only. It does not
// establish real Agent, identity, HDD copying or archive verification.
function archiveView(): WorkspaceArchive {
  return {
    archiveId,
    workspaceId: id,
    state: 'Running',
    phase: 'AwaitingConfirmation',
    code: null,
    awaitingConfirmation: true,
    authorizationRequired: false,
    cancelRequested: false,
    planJson: JSON.stringify({ planSha256: planDigest, entryCount: 2, totalBytes: 17 }),
    captureJson: null,
    verifyJson: null,
    completedAtUtc: null,
    retainsSource: true,
    reservesBudget: true,
  }
}
function archiveReceipt() {
  return JSON.stringify({
    operationId: archiveId,
    receiptSha256: 'c'.repeat(64),
    archiveSha256: 'd'.repeat(64),
    archiveIndexSha256: 'e'.repeat(64),
    archiveBytes: 128,
  })
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`workspace HDD archive ${width} ${colorScheme}: explicit confirmation, reload, verify receipt and resume fence`, async ({
      page,
    }) => {
      const state = await fixture(page)
      state.registered = state.allocated = true
      state.lifecycleState = 'Stopped'
      state.lifecycleVersion = 2
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.goto(`${workspacePath}/${id}/environment`)
      const card = page.getByRole('region', { name: '开发空间 HDD 归档' })
      await card.getByRole('button', { name: '核对 HDD 归档计划', exact: true }).click()
      await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible()
      const confirm = card.getByRole('button', { name: '确认 HDD 归档', exact: true })
      await expect(confirm).toBeDisabled()
      await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeDisabled()
      await card.locator('label.el-checkbox').click()
      await confirm.click()
      await expect(card.getByText('正在复制到 HDD', { exact: true })).toBeVisible()
      await page.reload()
      await expect(card.getByText('正在复制到 HDD', { exact: true })).toBeVisible()
      state.archive!.captureJson = archiveReceipt()
      state.archive!.phase = 'Verify'
      await card.getByRole('button', { name: '刷新归档状态' }).click()
      await expect(card.getByText('正在独立校验归档', { exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeDisabled()
      state.archive!.verifyJson = archiveReceipt()
      state.archive!.state = 'Succeeded'
      state.archive!.phase = 'Archived'
      state.archive!.completedAtUtc = '2026-11-10T16:00:00Z'
      await card.getByRole('button', { name: '刷新归档状态' }).click()
      await expect(card.getByText('HDD 副本已独立校验；原源码与资源预算继续保留。')).toBeVisible()
      await expect(card.getByText('c'.repeat(64), { exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeEnabled()
      expect(state.archiveKeys).toHaveLength(1)
      expect(state.archiveConfirmations).toHaveLength(1)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await page.screenshot({
        path: `test-results/workspace-archive-${width}-${colorScheme}.png`,
        fullPage: true,
      })
    })
  }
test('workspace HDD archive: lost replies recover the original operation and cancellation retains the fence until settled', async ({
  page,
}) => {
  const state = await fixture(page)
  state.registered = state.allocated = true
  state.lifecycleState = 'Stopped'
  state.lifecycleVersion = 2
  state.loseArchiveReply = state.loseArchiveConfirmation = state.loseArchiveCancel = true
  await page.goto(`${workspacePath}/${id}/environment`)
  const card = page.getByRole('region', { name: '开发空间 HDD 归档' })
  await card.getByRole('button', { name: '核对 HDD 归档计划', exact: true }).click()
  await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible({ timeout: 10_000 })
  await card.locator('label.el-checkbox').click()
  await card.getByRole('button', { name: '确认 HDD 归档', exact: true }).click()
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(card.getByText('正在复制到 HDD', { exact: true })).toBeVisible()
  state.archive!.authorizationRequired = true
  state.archive!.code = 'WORKSPACE_ARCHIVE_AUTHORIZATION_REQUIRED'
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await card.getByRole('button', { name: '重新授权归档', exact: true }).click()
  await expect(card.getByRole('button', { name: '重新授权归档', exact: true })).toHaveCount(0)
  await card.getByRole('button', { name: '取消归档', exact: true }).click()
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(card.getByRole('button', { name: '等待原任务停止确认', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeDisabled()
  state.archive!.state = state.archive!.phase = 'Canceled'
  state.archive!.completedAtUtc = '2026-11-10T16:00:00Z'
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(
    card.getByText('原执行已结束。HDD 可能保留部分副本与日志，请勿把取消当作数据已清理。'),
  ).toBeVisible()
  await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeEnabled()
  expect(state.archiveKeys).toHaveLength(1)
  expect(state.archiveConfirmations).toHaveLength(1)
})
test('workspace HDD archive: denied reads stop polling and hide retained metadata until manual recovery', async ({
  page,
}) => {
  const state = await fixture(page)
  state.registered = state.allocated = true
  state.lifecycleState = 'Stopped'
  state.lifecycleVersion = 2
  state.archive = archiveView()
  await page.goto(`${workspacePath}/${id}/environment`)
  const card = page.getByRole('region', { name: '开发空间 HDD 归档' })
  await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible()
  state.archiveStatus = 403
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(
    card.getByText('登录或权限已变化，已停止自动刷新。请重新登录或恢复权限后刷新归档状态。'),
  ).toBeVisible()
  await expect(card.getByText(archiveId, { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toBeDisabled()
  const reads = state.archiveReads
  await page.waitForTimeout(3500)
  expect(state.archiveReads).toBe(reads)
  state.archiveStatus = 200
  await card.getByRole('button', { name: '刷新归档状态' }).click()
  await expect(card.getByText('等待确认归档', { exact: true })).toBeVisible()
})

test('idle stop and resume preserve the plan and original key after a lost reply at 320px', async ({
  page,
}) => {
  const state = await fixture(page)
  state.registered = state.allocated = true
  state.loseLifecycleReply = true
  await page.setViewportSize({ width: 320, height: 800 })
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.goto(`${workspacePath}/${id}/environment`)
  const card = page.locator('.workspace-lifecycle')
  await card.getByRole('button', { name: '核对停用计划', exact: true }).click()
  const confirm = card.getByRole('button', { name: '确认停用空间', exact: true })
  await expect(confirm).toBeDisabled()
  await card.locator('label.el-checkbox').click()
  await confirm.click()
  await expect(
    card.getByText('未能确认服务器结果。请检查连接后刷新，或用原提交重试。'),
  ).toBeVisible()
  await confirm.click()
  await expect(card.getByText('已停用', { exact: true })).toBeVisible()
  expect(state.lifecycleKeys).toHaveLength(1)
  await page.getByRole('link', { name: '运行记录', exact: true }).click()
  await expect(page.getByText('空间已停用或正在切换状态，恢复后可提交新任务。')).toBeVisible()
  await expect(page.getByRole('button', { name: '提交运行', exact: true })).toHaveCount(0)
  await page.getByRole('link', { name: '环境与存储', exact: true }).click()
  await card.getByRole('button', { name: '核对恢复计划', exact: true }).click()
  await card.locator('label.el-checkbox').click()
  await card.getByRole('button', { name: '确认恢复空间', exact: true }).click()
  await expect(card.getByText('可用', { exact: true })).toBeVisible()
  expect(state.lifecycleVersion).toBe(3)
  expect(state.lifecycleKeys).toHaveLength(2)
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true)
})

test('a pending stop survives reload and requires fresh authorization before confirmation', async ({
  page,
}) => {
  const state = await fixture(page)
  state.registered = state.allocated = true
  state.lifecycleAuthorization = true
  await page.goto(`${workspacePath}/${id}/environment`)
  const card = page.locator('.workspace-lifecycle')
  await card.getByRole('button', { name: '核对停用计划', exact: true }).click()
  await card.locator('label.el-checkbox').click()
  await card.getByRole('button', { name: '确认停用空间', exact: true }).click()
  await page.reload()
  await expect(card.getByText('停用确认中', { exact: true })).toBeVisible()
  await card.getByRole('button', { name: '重新授权继续', exact: true }).click()
  await expect(card.getByText('已停用', { exact: true })).toBeVisible()
  expect(state.lifecycleKeys).toHaveLength(1)
})

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

const sourcePath = `${workspacePath}/${id}/source`
const chunkSize = 1024 * 1024
const sourcePlan = {
  planSha256: 'c'.repeat(64),
  contentSha256: 'd'.repeat(64),
  fileCount: 1,
  totalBytes: chunkSize + 64,
}

function sampleZip(fill: number) {
  const name = Buffer.from('src/sample.bin')
  const bytes = Buffer.alloc(sourcePlan.totalBytes, fill)
  const crc = crc32(bytes)
  const local = Buffer.alloc(30)
  local.writeUInt32LE(0x04034b50, 0)
  local.writeUInt16LE(20, 4)
  local.writeUInt32LE(crc, 14)
  local.writeUInt32LE(bytes.length, 18)
  local.writeUInt32LE(bytes.length, 22)
  local.writeUInt16LE(name.length, 26)
  const central = Buffer.alloc(46)
  central.writeUInt32LE(0x02014b50, 0)
  central.writeUInt16LE(20, 4)
  central.writeUInt16LE(20, 6)
  central.writeUInt32LE(crc, 16)
  central.writeUInt32LE(bytes.length, 20)
  central.writeUInt32LE(bytes.length, 24)
  central.writeUInt16LE(name.length, 28)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(1, 8)
  end.writeUInt16LE(1, 10)
  end.writeUInt32LE(central.length + name.length, 12)
  end.writeUInt32LE(local.length + name.length + bytes.length, 16)
  return Buffer.concat([local, name, bytes, central, name, end])
}

async function sourceFixture(page: Page) {
  const base = await fixture(page)
  base.allocated = true
  const file = sampleZip(65)
  const archive = {
    format: 'zip',
    archiveBytes: file.length,
    archiveSha256: createHash('sha256').update(file).digest('hex'),
  }
  const state = {
    allowed: true,
    wrongWorkspace: false,
    loseChunkReply: true,
    losePublishReply: true,
    id: '',
    receivedBytes: 0,
    phase: 'Uploading',
    beginCalls: 0,
    publishCalls: 0,
    cancelCalls: 0,
    offsets: [] as number[],
  }
  const prefix = `${api}/${id}/source-imports`
  await page.route(
    (url) => url.pathname === prefix || url.pathname.startsWith(prefix + '/'),
    async (route) => {
      const request = route.request()
      if (!state.allowed) return route.fulfill({ status: 403, json: {} })
      const suffix = new URL(request.url()).pathname.slice(prefix.length)
      if (request.method() === 'GET' && suffix === '/git-repositories')
        return route.fulfill({ json: [] })
      const receipt = () => ({
        workspaceId: state.wrongWorkspace ? runId : id,
        importId: state.id,
        state: state.phase,
        archive,
        receivedBytes: state.receivedBytes,
        plan: ['Uploading', 'Ready'].includes(state.phase) ? null : sourcePlan,
        code: state.phase === 'Canceled' ? 'WORKSPACE_SOURCE_CANCELED' : null,
        expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      })
      if (request.method() === 'GET' && suffix === '/published')
        return state.phase === 'Published'
          ? route.fulfill({ json: receipt() })
          : route.fulfill({ status: 204 })
      if (request.method() === 'GET' && suffix === '') {
        if (!state.id) return route.fulfill({ status: 204 })
        if (state.phase === 'Ready') state.phase = 'Staged'
        else if (state.phase === 'Publishing') state.phase = 'Published'
        else if (state.phase === 'Canceling') state.phase = 'Canceled'
        return route.fulfill({ json: receipt() })
      }
      expect(request.headers()['x-xsrf-token']).toBe('ui-fixture-only')
      if (request.method() === 'PUT' && /^\/[0-9a-f-]{36}$/.test(suffix)) {
        expect(request.postDataJSON()).toEqual(archive)
        state.beginCalls++
        state.id = suffix.slice(1)
        return route.fulfill({ status: 202, json: receipt() })
      }
      const item = `/${state.id}`
      if (request.method() === 'PUT' && suffix.startsWith(item + '/chunks/')) {
        expect(request.headers()['content-type']).toBe('application/octet-stream')
        const offset = Number(suffix.slice((item + '/chunks/').length))
        expect(offset).toBe(state.receivedBytes)
        const body = request.postDataBuffer()
        expect(body).toEqual(file.subarray(offset, offset + chunkSize))
        state.offsets.push(offset)
        state.receivedBytes += body!.length
        if (state.receivedBytes === file.length) state.phase = 'Ready'
        if (state.loseChunkReply) {
          state.loseChunkReply = false
          return route.abort('failed')
        }
        return route.fulfill({ json: receipt() })
      }
      if (request.method() === 'POST' && suffix === item + '/publish') {
        expect(state.phase).toBe('Staged')
        expect(request.postDataJSON()).toEqual({ planSha256: sourcePlan.planSha256 })
        state.publishCalls++
        state.phase = 'Publishing'
        if (state.losePublishReply) {
          state.losePublishReply = false
          return route.abort('failed')
        }
        return route.fulfill({ status: 202, json: receipt() })
      }
      if (request.method() === 'POST' && suffix === item + '/cancel') {
        expect(state.phase).toBe('Staged')
        state.cancelCalls++
        state.phase = 'Canceling'
        return route.fulfill({ status: 202, json: receipt() })
      }
      return route.fulfill({ status: 404, json: {} })
    },
  )
  const input = { name: 'project.zip', mimeType: 'application/zip', buffer: file }
  return { state, input }
}

test('resumes a committed archive chunk after reload and confirms the exact source plan once', async ({
  page,
}, info) => {
  const { state, input } = await sourceFixture(page)
  await page.addInitScript(() => {
    Object.defineProperty(window.crypto, 'randomUUID', { value: undefined })
    Object.defineProperty(window.crypto, 'subtle', { value: undefined })
  })
  await page.goto(`${workspacePath}/${id}/environment`)
  await page.getByRole('link', { name: '源码导入' }).click()
  await expect(page.getByRole('heading', { name: '导入源码', exact: true })).toBeVisible()
  await page.getByLabel('选择归档文件').setInputFiles(input)
  await page.getByRole('button', { name: '开始上传' }).click()
  await expect(page.getByText(/未能确认服务器结果/)).toBeVisible()
  expect(state.offsets).toEqual([0])
  const originalId = state.id
  await page.reload()
  await expect(page.getByText(originalId, { exact: true })).toBeVisible()
  await expect(page.getByText(/已接收 1,048,576 字节/)).toBeVisible()
  const file = page.getByLabel('重新选择原归档')
  await file.setInputFiles({ ...input, buffer: sampleZip(66) })
  await page.getByRole('button', { name: '继续上传' }).click()
  await expect(page.getByText(/所选文件与待续传归档/)).toBeVisible()
  expect(state.offsets).toEqual([0])
  await file.setInputFiles(input)
  await page.getByRole('button', { name: '继续上传' }).click()
  await expect(page.getByText(sourcePlan.planSha256, { exact: true })).toBeVisible()
  expect(state.id).toBe(originalId)
  expect(state.beginCalls).toBe(1)
  expect(state.offsets).toEqual([0, chunkSize])
  const publish = page.getByRole('button', { name: '确认发布源码' })
  await expect(publish).toBeDisabled()
  await page.screenshot({ path: info.outputPath('source-plan-desktop.png') })
  await page.getByText('我已核对暂存计划，确认发布到该空间', { exact: true }).click()
  await expect(page.getByRole('checkbox', { name: /我已核对暂存计划/ })).toBeChecked()
  await publish.click()
  await expect(page.getByText(/服务器状态待确认/)).toBeVisible()
  await expect(publish).toBeDisabled()
  await page.getByRole('button', { name: '刷新导入状态' }).click()
  await expect(page.getByText('已发布', { exact: true })).toBeVisible()
  expect(state.publishCalls).toBe(1)
  await expect(page.getByLabel('选择归档文件')).toHaveCount(0)
})

test('rejects a wrong-scope receipt, clears revoked state and cancels from narrow dark layout', async ({
  page,
}, info) => {
  await page.setViewportSize({ width: 375, height: 820 })
  const { state, input } = await sourceFixture(page)
  state.loseChunkReply = false
  await page.goto(sourcePath)
  await page.getByLabel('选择归档文件').setInputFiles({ ...input, name: 'project.txt' })
  await expect(page.getByText('请选择 ZIP、tar 或 tar.gz 归档。')).toBeVisible()
  expect(state.beginCalls).toBe(0)
  await page.getByLabel('选择归档文件').setInputFiles(input)
  await page.getByRole('button', { name: '开始上传' }).click()
  const refresh = page.getByRole('button', { name: '刷新导入状态' })
  await expect(page.getByText('待确认发布', { exact: true })).toBeVisible()
  await page.locator('.theme-switch .el-switch__core').click({ timeout: 5000 })
  await expect(page.locator('html')).toHaveClass(/\bdark\b/)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  await page.screenshot({ path: info.outputPath('source-plan-narrow-dark.png') })
  state.wrongWorkspace = true
  await refresh.click()
  await expect(page.getByText(/导入回执未通过核验/)).toBeVisible()
  await expect(page.getByRole('button', { name: '确认发布源码' })).toBeDisabled()
  state.wrongWorkspace = false
  state.allowed = false
  await refresh.click()
  await expect(page.getByText(/当前账号没有这项开发空间权限/)).toBeVisible()
  await expect(page.getByText(sourcePlan.planSha256, { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '确认发布源码' })).toHaveCount(0)
  state.allowed = true
  await refresh.click()
  await page.getByRole('button', { name: '取消导入' }).click()
  await expect(page.getByText('已取消', { exact: true })).toBeVisible()
  expect(state.cancelCalls).toBe(1)
  expect(state.publishCalls).toBe(0)
})

test('rejects duplicate and protected direct files before creating an import', async ({
  page,
}, info) => {
  await page.setViewportSize({ width: 375, height: 820 })
  const { state } = await sourceFixture(page)
  await page.goto(sourcePath)
  const input = page.getByLabel('选择源码文件')
  const entry = (name: string, content: string) => ({
    name,
    mimeType: 'text/plain',
    buffer: Buffer.from(content),
  })
  await input.setInputFiles([entry('main.cjs', 'one'), entry('main.cjs', 'two')])
  const upload = page.getByRole('button', { name: '开始上传' })
  await upload.click()
  await expect(page.getByText(/所选内容含同名文件/)).toBeVisible()
  await input.setInputFiles(entry('.env', 'SYNTHETIC=fixture'))
  await upload.click()
  await expect(page.getByText(/所选内容含受保护的路径/)).toBeVisible()
  await expect(page.getByLabel('选择源码目录')).toBeVisible()
  await page.locator('.theme-switch .el-switch__core').click()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  await page.screenshot({ path: info.outputPath('source-files-narrow-dark.png') })
  expect(state.beginCalls).toBe(0)
  expect(state.offsets).toEqual([])
})

function retirementView(epoch: number): WorkspaceRetirement {
  return {
    retirementId,
    workspaceId: id,
    archiveId,
    state: 'Running',
    phase: 'AwaitingConfirmation',
    code: null,
    awaitingConfirmation: true,
    authorizationRequired: false,
    cancelRequested: false,
    attentionRequired: false,
    planJson: JSON.stringify({
      archiveId,
      retirementPlanSha256: planDigest,
      expectedEpoch: epoch,
      entryCount: 2,
      totalBytes: 17,
      retainsSource: true,
      reservesBudget: true,
      deletionAuthorized: false,
    }),
    planExpiresAtUtc: '2026-11-11T12:10:00Z',
    confirmedAtUtc: null,
    executionJson: null,
    completedAtUtc: null,
    sourceRetired: false,
    budgetReleased: false,
    retainsHdd: true,
  }
}
for (const width of [320, 1440])
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`workspace retirement ${width} ${colorScheme}: independent delete consent, reload and retained history`, async ({
      page,
    }, testInfo) => {
      const state = await fixture(page)
      state.registered = state.allocated = true
      state.lifecycleState = 'Stopped'
      state.lifecycleVersion = 2
      state.archive = {
        ...archiveView(),
        state: 'Succeeded',
        phase: 'Archived',
        awaitingConfirmation: false,
        planJson: JSON.stringify({
          planSha256: planDigest,
          expectedEpoch: 2,
          entryCount: 2,
          totalBytes: 17,
        }),
        captureJson: archiveReceipt(),
        verifyJson: archiveReceipt(),
        completedAtUtc: '2026-11-11T11:00:00Z',
      }
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await page.goto(`${workspacePath}/${id}/environment`)
      const card = page.getByRole('region', { name: '开发空间退役', exact: true })
      await card.getByRole('button', { name: '核对空间退役计划', exact: true }).click()
      const confirm = card.getByRole('button', { name: '确认删除源码并退役空间', exact: true })
      await expect(confirm).toBeDisabled()
      expect(state.retirementConfirmations).toHaveLength(0)
      await card.locator('label.el-checkbox').click()
      await expect(card.getByRole('checkbox')).toBeChecked()
      await confirm.click()
      await expect(card.getByText('正在退役源码和释放配额', { exact: true })).toBeVisible()
      expect(state.retirementKeys).toHaveLength(1)
      expect(state.retirementConfirmations).toHaveLength(1)
      await page.reload()
      await expect(card.getByText('正在退役源码和释放配额', { exact: true })).toBeVisible()
      expect(state.retirementKeys).toHaveLength(1)
      state.lifecycleState = 'Retired'
      Object.assign(state.retirement!, {
        state: 'Succeeded',
        phase: 'Retired',
        completedAtUtc: '2026-11-11T12:00:00Z',
        budgetReleased: true,
        sourceRetired: true,
        executionJson: JSON.stringify({
          workspaceId: id,
          retirementId,
          retirementPlanSha256: planDigest,
          sourceRemoved: true,
          quotaReleased: true,
          claimReleased: true,
          retainsHdd: true,
        }),
      })
      await card.getByRole('button', { name: '刷新退役状态', exact: true }).click()
      await expect(
        card.getByText(
          '源码和磁盘配额已退役，应用资源预算已释放。HDD 留存、历史记录和报告仍可读取。',
        ),
      ).toBeVisible()
      await expect(page.getByRole('link', { name: '运行记录', exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: '核对恢复计划', exact: true })).toHaveCount(0)
      await expect(page.getByRole('button', { name: '核验 Runner 存储', exact: true })).toHaveCount(0)
      await expect(page.getByText('已释放，原登记：')).toBeVisible()
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      ).toBe(true)
      await page.screenshot({
        path: testInfo.outputPath(`retirement-${width}-${colorScheme}.png`),
        fullPage: true,
      })
    })
  }
