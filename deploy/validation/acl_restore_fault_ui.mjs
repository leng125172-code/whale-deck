// New own capture via actual Vue, followed by a selected-history restore preflight.
import assert from 'node:assert/strict'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { openPermissionFiles, closePermissionDetails, writeSignal, waitSignal } from './installed_ui_permission_session.mjs'

export async function prepareRestoreFault(page, context, base, fixture, profile) {
  assert.ok([1, 2, 3, 4].includes(fixture.aclRestoreFaultVersion))
  assert.ok((fixture.aclRestoreFaultVersion === 1 ? ['cancel', 'logout'] : fixture.aclRestoreFaultVersion === 2 ? ['group-removal', 'account-disable'] : fixture.aclRestoreFaultVersion === 3 ? ['worker-crash', 'agent-restart'] : ['post-acl-cancel', 'post-acl-worker-crash']).includes(fixture.kind))
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/acls`, directory = fixture.evidence
  let files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: 'ACL acl-file.bin', exact: true }).click()
  const captureDialog = page.getByRole('dialog', { name: '修改容器文件或目录ACL', exact: true })
  async function select(label, value) {
    await captureDialog.getByRole('combobox', { name: label, exact: true }).press('ArrowDown')
    await page.getByRole('listbox', { name: label, exact: true }).getByRole('option', { name: value, exact: true }).click()
  }
  await select('条目1 权限', '6 · rw-'); await select('条目2 权限', '0 · ---'); await select('条目3 权限', '0 · ---')
  await captureDialog.getByRole('button', { name: '添加ACL条目', exact: true }).click()
  await captureDialog.getByLabel('条目4 数值ID', { exact: true }).fill('2201'); await select('条目4 权限', '6 · rw-')
  await select('新增条目类型', 'mask'); await captureDialog.getByRole('button', { name: '添加ACL条目', exact: true }).click(); await select('条目5 权限', '4 · r--')
  const registering = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await captureDialog.getByRole('button', { name: '生成ACL修改预检', exact: true }).click()
  const accepted = await registering; assert.equal(accepted.status(), 202); const capture = await accepted.json()
  await expect(captureDialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 45_000 })
  await captureDialog.getByRole('button', { name: '确认ACL修改', exact: true }).click()
  await expect(captureDialog.getByText('ACL修改已完成', { exact: true })).toBeVisible({ timeout: 45_000 })
  const captured = await context.request.get(base + endpoint + '/' + capture.id); assert.equal(captured.status(), 200)
  const result = { name: 'access', ...await captured.json() }
  assert.equal(result.state, 'Succeeded'); assert.equal(result.confirmation, null)
  await writeSignal(directory, 'source-captured.json', { subject: profile.subject, result })
  const trusted = await waitSignal(directory, 'source-verified.json')
  assert.equal(trusted.physicalAndOriginalHistoryVerified, true)
  const source = trusted.source, captureId = result.executionJobId.replaceAll('-', '')
  assert.equal(source.entry.captureId, captureId)
  await closePermissionDetails(page, captureDialog)
  files = await openPermissionFiles(page, fixture)
  const history = page.getByRole('dialog', { name: 'ACL历史', exact: true })
  let listing = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/history' && r.request().method() === 'GET')
  await files.getByRole('button', { name: 'ACL历史 acl-file.bin', exact: true }).click()
  for (let index = 0; index < 100; index++) {
    const response = await listing; assert.equal(response.status(), 200); const value = await response.json()
    if (value.entries.some(v => v.captureId === captureId)) break
    assert.equal(value.complete, false); assert.ok(index < 99)
    listing = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/history' && r.request().method() === 'GET')
    await history.getByRole('button', { name: '继续扫描', exact: true }).click()
  }
  const reading = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/history/' + captureId && r.request().method() === 'GET')
  await history.getByRole('button', { name: '查看原ACL ' + captureId, exact: true }).click()
  const response = await reading; assert.equal(response.status(), 200); assert.deepEqual(await response.json(), source)
  await history.getByRole('button', { name: '从此历史生成恢复预检', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '从历史恢复容器ACL', exact: true })
  await expect(dialog.getByRole('region', { name: 'ACL恢复来源', exact: true })).toContainText(captureId)
  const restoring = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/restores' && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成ACL恢复预检', exact: true }).click()
  const registered = await restoring; assert.equal(registered.status(), 202)
  assert.ok((registered.headers()['cache-control'] ?? '').includes('no-store'))
  const body = registered.request().postDataJSON(), value = await registered.json()
  assert.deepEqual(Object.keys(body).sort(), ['instanceId', 'path', 'captureId', 'expectedHistoryDigest', 'expectedVersion'].sort())
  assert.equal(body.captureId, captureId); assert.equal(body.expectedHistoryDigest, source.entry.historyDigest)
  assert.deepEqual(value.source, source); assert.equal(value.intent.executionJobId, null)
  return { dialog, intent: value.intent, source }
}
