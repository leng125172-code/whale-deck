// Normal visible installed login and private, bounded QA coordination only.
import assert from 'node:assert/strict'
import { readFile, writeFile, rename } from 'node:fs/promises'
import { join } from 'node:path'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function writeSignal(directory, name, value) {
  assert.match(name, /^[a-z-]+\.json$/)
  const target = join(directory, name)
  await writeFile(target + '.pending', JSON.stringify(value), { mode: 0o600, flag: 'wx' })
  await rename(target + '.pending', target)
}

export async function waitSignal(directory, name, timeout = 220_000) {
  assert.match(name, /^[a-z-]+\.json$/)
  const until = Date.now() + timeout
  while (Date.now() < until) {
    try {
      const raw = await readFile(join(directory, name))
      assert.ok(raw.length <= 32768)
      return JSON.parse(raw)
    } catch (error) { if (error.code !== 'ENOENT') throw error }
    await new Promise(resolve => setTimeout(resolve, 40))
  }
  throw new Error('PRIVATE_PERMISSION_GATE_COORDINATION_TIMEOUT')
}

export async function loginVisible(page, context, base, destination = '/containers') {
  const origin = new URL(base)
  assert.equal(base, 'http://127.0.0.1:8080')
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  await page.goto(base + '/login?returnUrl=' + destination)
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === destination) break
    const identification = page.locator('ak-stage-identification'), secret = page.locator('ak-stage-password'), consent = page.locator('ak-stage-consent')
    const button = /^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if (!identified && await identification.locator('input[name="uidField"]').isVisible()) {
      await identification.locator('input[name="uidField"]').fill(username)
      await identification.getByRole('button', { name: button }).click(); identified = true
    } else if (!entered && await secret.locator('input[name="password"]').isVisible()) {
      await secret.locator('input[name="password"]').fill(password)
      await secret.getByRole('button', { name: button }).click(); entered = true
    } else if (entered && !consented && await consent.isVisible()) {
      await consent.getByRole('button', { name: button }).click(); consented = true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified && entered)
  await expect(page).toHaveURL(base + destination)
  const profile = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(profile.isAdministrator, true)
  return profile
}

export async function openPermissionFiles(page, fixture) {
  await page.getByRole('textbox', { name: '搜索容器', exact: true }).fill(fixture.containerName)
  const card = page.locator('.container-card').filter({ hasText: fixture.containerName })
  await expect(card).toHaveCount(1)
  await card.getByRole('button', { name: '详情', exact: true }).click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const files = page.getByRole('region', { name: '容器文件', exact: true })
  await files.getByRole('textbox', { name: '容器内相对目录', exact: true }).fill('work')
  await files.getByRole('button', { name: '前往', exact: true }).click()
  await expect(files.getByText('original.bin', { exact: true })).toBeVisible()
  return files
}

export async function closePermissionDetails(page, dialog) {
  await dialog.getByRole('button', { name: '关闭', exact: true }).click()
  await page.getByRole('dialog', { name: /容器详情/ }).locator('.el-drawer__close-btn').click()
}
