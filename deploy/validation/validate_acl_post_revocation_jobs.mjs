// Read-only original completed jobs under a different visible SSO administrator.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, writeSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
let browser, page, directory, phase = 'startup'
try {
  const fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  directory = fixture.evidence
  const prefix = fileURLToPath(new URL('../artifacts/', import.meta.url))
  assert.ok(directory.startsWith(prefix)); assert.match(directory.slice(prefix.length), /^container-acl-faults-[a-f0-9]{32}\/current-admin-jobs$/)
  assert.match(fixture.revision, /^[a-f0-9]{40}$/); assert.equal(fixture.jobs.length, 2)
  const base = 'http://127.0.0.1:8080'
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 800 }, reducedMotion: 'reduce' }); context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, fixture.revision)
  phase = 'normal-visible-login'; const profile = await loginVisible(page, context, base, '/jobs')
  assert.ok(fixture.jobs.every(job => job.originalSubject !== profile.subject))
  let managementWrites = 0
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) && path.startsWith('/api/v1/') && !path.startsWith('/api/v1/auth/')) managementWrites++
  })
  const results = []
  for (const [index, job] of fixture.jobs.entries()) {
    phase = 'visible-original-job-' + index
    await page.setViewportSize({ width: index ? 1440 : 320, height: 600 }); await page.emulateMedia({ colorScheme: index ? 'light' : 'dark' })
    await page.goto(base + '/jobs'); await expect(page.getByRole('heading', { name: '任务中心', exact: true })).toBeVisible()
    const response = await context.request.get(base + '/api/v1/jobs?take=100'); assert.equal(response.status(), 200)
    const jobs = await response.json(), position = jobs.findIndex(item => item.id === job.id); assert.ok(position >= 0)
    assert.equal(jobs[position].state, 'Succeeded'); assert.equal(jobs[position].jobType, 'containers.files.acl')
    const rows = page.locator('.el-table__body-wrapper .el-table__row'); await expect(rows).toHaveCount(jobs.length)
    await rows.nth(position).getByRole('button', { name: '详情', exact: true }).click()
    const drawer = page.getByRole('dialog', { name: '任务详情', exact: true })
    const identifier = drawer.getByText(job.id, { exact: true }); await expect(identifier).toBeVisible(); await identifier.scrollIntoViewIfNeeded(); await expect(identifier).toBeInViewport({ ratio: 1 })
    await expect(drawer.locator('.el-descriptions').getByText('containers.files.acl', { exact: true })).toBeVisible()
    await expect(drawer.locator('.el-descriptions').getByText('Succeeded', { exact: true })).toBeVisible()
    await expect(drawer.getByRole('button', { name: '取消任务', exact: true })).toHaveCount(0)
    await expect(drawer.getByRole('button', { name: '重新预检并重试', exact: true })).toHaveCount(0)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(directory, 'job-' + index + '.png') })
    const detail = await context.request.get(base + '/api/v1/jobs/' + job.id); assert.equal(detail.status(), 200); const original = await detail.json()
    assert.equal(original.id, job.id); assert.equal(original.state, 'Succeeded'); assert.equal(original.jobType, 'containers.files.acl')
    results.push({ id: job.id, originalSubject: job.originalSubject, state: original.state, jobType: original.jobType, actualUi: true })
    await drawer.locator('.el-drawer__close-btn').click()
  }
  assert.equal(managementWrites, 0); phase = 'normal-current-admin-dual-logout'; const logout = await logoutFromUserMenu(page, context, base)
  await writeSignal(directory, 'verified.json', { revision: fixture.revision, subject: profile.subject, results, managementWrites, actualUi: true, normalDualLogout: true, logout })
  await context.close(); console.log(JSON.stringify({ currentAdminJobsUi: 'passed', cases: 2, managementWrites }))
} catch (error) {
  const locations = [...(error?.stack ?? '').matchAll(/validate_acl_post_revocation_jobs\.mjs:([0-9]+):([0-9]+)/g)].map(match => ({ line: Number(match[1]), column: Number(match[2]) }))
  if (page && directory && !phase.includes('login') && phase !== 'startup') await page.screenshot({ path: join(directory, 'failed-page.png') }).catch(() => {})
  console.error(JSON.stringify({ currentAdminJobsUi: 'failed', phase, failureType: error?.name, locations })); process.exitCode = 1
} finally { if (browser) await browser.close() }
