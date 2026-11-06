// Installed Authentik/Vue only. No routed responses, injected sessions or native writes.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, page, evidence
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision);assert.ok(['worker-crash','agent-restart'].includes(fixture.kind))
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base)
  let ownerLogoutPosts=0
  page.on('request',r=>{const u=new URL(r.url());if(r.method()==='POST'&&u.origin===base&&u.pathname==='/api/v1/auth/logout')ownerLogoutPosts++})
  const recycle = `/api/v1/containers/${fixture.containerId}/files/recycles`
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recycle-purges`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const mutations = [], headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === recycle || path.startsWith(recycle + '/') || path === endpoint || path.startsWith(endpoint + '/'))) {
      mutations.push(path); headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key'] && !!h[csrf.headerName.toLowerCase()]).catch(() => false))
    }
    assert.ok(!(request.method() === 'PUT' && path.includes('/files/')))
  })
  async function get(path) { const response = await context.request.get(base + path); assert.equal(response.status(), 200); return response.json() }
  async function coordinate(name, value) {
    await writeSignal(evidence, name + '-completed.json', { subject: profile.subject, result: value })
    assert.equal((await waitSignal(evidence, name + '-accepted.json', 60_000)).id, value.id)
  }
  phase = 'page-create-original-recycle-source'
  let files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '回收 recycle-rpc.bin', exact: true }).click()
  const sourceDialog = page.getByRole('dialog', { name: '回收容器普通文件', exact: true })
  const sourceRegistration = page.waitForResponse(r => new URL(r.url()).pathname === recycle && r.request().method() === 'POST')
  await sourceDialog.getByRole('button', { name: '生成文件回收预检', exact: true }).click()
  const sourceResponse = await sourceRegistration; assert.equal(sourceResponse.status(), 202)
  const sourceInitial = await sourceResponse.json()
  await expect(sourceDialog.getByText('预检完成，尚未移除文件', { exact: true })).toBeVisible({ timeout: 60_000 })
  const sourceReady = await get(recycle + '/' + sourceInitial.id)
  assert.equal(sourceReady.state, 'Ready'); assert.equal(sourceReady.sha256, fixture.original.sha256)
  await sourceDialog.getByRole('button', { name: '确认文件回收', exact: true }).click()
  await expect(sourceDialog.getByText('文件回收已完成', { exact: true })).toBeVisible({ timeout: 60_000 })
  const source = await get(recycle + '/' + sourceInitial.id)
  assert.equal(source.state, 'Succeeded'); assert.equal(source.receipt.sourceRetained, true)
  await coordinate('source', { ...source, name: 'trash' })
  await closePermissionDetails(page, sourceDialog)
  const jobProofs = []
  phase = 'purge-arm-before-confirm'
  files=await openPermissionFiles(page,fixture)
  await files.getByRole('button',{name:'清理留存原件',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'清理容器留存原件',exact:true})
  await dialog.locator('.purge-source-row').filter({hasText:source.id}).getByRole('button',{name:'选择此来源',exact:true}).click()
  const registration=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST')
  await dialog.getByRole('button',{name:'生成留存清理预检',exact:true}).click()
  const response=await registration;assert.equal(response.status(),202);assert.deepEqual(response.request().postDataJSON(),{sourceIntentId:source.id})
  const initial=await response.json(),key=(await response.request().allHeaders())['idempotency-key']
  await expect(dialog.getByText('清理预检完成，尚未删除',{exact:true})).toBeVisible({timeout:60000})
  const ready=await get(endpoint+'/'+initial.id);assert.equal(ready.state,'Ready')
  await coordinate('armed',{id:ready.id,sourceIntentId:source.id,sourceOperationId:source.executionJobId,version:ready.version})
  await dialog.getByRole('button',{name:'确认留存清理',exact:true}).click()
  const confirm=page.getByRole('dialog',{name:'确认清理留存原件',exact:true})
  await expect(confirm).toContainText('当前尚未提供清理后的归档恢复入口')
  await confirm.getByRole('button',{name:'确认删除留存原件',exact:true}).click()
  phase='wait-fixed-service-fault-and-original-result'
  const settled=await waitSignal(evidence,'settled.json',240000)
  assert.equal(settled.id,ready.id);assert.equal(settled.state,'Failed')
  const final=await get(endpoint+'/'+ready.id);assert.equal(final.executionJobId,settled.executionJobId);assert.equal(final.state,'Failed')
  assert.equal(final.resultCode,'CONTAINER_RECYCLE_PURGE_INTERRUPTED');assert.equal(final.receipt.state,'Aborted');assert.equal(final.purgeApplied,false);assert.equal(final.reservationRecorded,true)
  const owner=await get('/api/v1/auth/me');assert.equal(owner.subject,profile.subject);assert.equal(owner.isAdministrator,true);assert.equal(ownerLogoutPosts,0)
  await coordinate('restart',final);await page.screenshot({path:join(evidence,'interrupted-before-delete.png')});await closePermissionDetails(page,dialog)
  // Restore only this gate's source through the existing separately confirmed product workflow.
  phase = 'dedicated-restore-for-qa-cleanup'
  files=await openPermissionFiles(page,fixture)
  await files.getByRole('button',{name:'恢复已回收文件',exact:true}).click()
  const restoreDialog=page.getByRole('dialog',{name:'恢复已回收的容器文件',exact:true})
  await restoreDialog.locator('.restore-source-row').filter({hasText:source.id}).getByRole('button',{name:'选择此来源',exact:true}).click()
  const restoreBase=`/api/v1/containers/${fixture.containerId}/files/recycle-restores`
  const restoreRegistration=page.waitForResponse(r=>new URL(r.url()).pathname===restoreBase&&r.request().method()==='POST')
  await restoreDialog.getByRole('button',{name:'生成独立恢复预检',exact:true}).click()
  const restoredInitial=await (await restoreRegistration).json()
  await expect(restoreDialog.getByText('恢复预检完成，尚未写入',{exact:true})).toBeVisible({timeout:60000})
  const restoreReady=await get(restoreBase+'/'+restoredInitial.id)
  await coordinate('restore-ready',{id:restoreReady.id,sourceIntentId:source.id,sourceOperationId:source.executionJobId,version:restoreReady.version})
  await restoreDialog.getByRole('button',{name:'确认文件恢复',exact:true}).click()
  await expect(restoreDialog.getByText('本次文件恢复已完成',{exact:true})).toBeVisible({timeout:60000})
  const restored=await get(restoreBase+'/'+restoreReady.id);assert.equal(restored.state,'Succeeded')
  await coordinate('restored',restored);await page.screenshot({path:join(evidence,'qa-source-restored.png')});await closePermissionDetails(page,restoreDialog)
  phase = 'actual-canceled-execution-job';await page.setViewportSize({width:1440,height:1050})
  const listing=page.waitForResponse(r=>isJobPageResponse(r,base));await page.goto(base+'/jobs');const listed=await (await listing).json();const position=listed.findIndex(j=>j.id===final.executionJobId);assert.ok(position>=0)
  const job=await get('/api/v1/jobs/'+final.executionJobId),facts=JSON.parse(job.resultJson)
  assert.equal(Object.keys(facts).length,16);assert.equal(facts.state,'Failed');assert.equal(facts.receiptState,'Aborted');assert.equal(facts.purgeApplied,false);assert.equal(facts.reservationRecorded,true);assert.equal(facts.applyStarted,true);assert.equal(facts.sourceIntentId,source.id)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click()
  const detail=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=detail.getByRole('region',{name:'留存清理安全结果',exact:true})
  await expect(summary).toContainText('清理任务未完成');await expect(detail.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
  await page.screenshot({path:join(evidence,'interrupted-execution-job.png')});jobProofs.push(job)
  await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  assert.equal(mutations.filter(p=>p===endpoint).length,1)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/confirm')).length,1)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/cancel')).length,0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  assert.equal(ownerLogoutPosts,0)
  phase='normal-owner-dual-logout';const logout=await logoutFromUserMenu(page,context,base)
  await writeSignal(evidence,'browser.json',{revision,kind:fixture.kind,containerId:fixture.containerId,subject:profile.subject,source,original:{...ready,confirmation:null},final,key,restored,
    registrations:1,confirmations:1,cancellations:0,originalOwnerStillAdministrator:true,ownerLogoutPostsBeforeRestartOutcome:0,logout,actualUi:true,jobProofs,mutations})
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualPurgeRestartBrowser: 'passed', cases: 2 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_purge_restart.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-relogin'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualPurgeRestartBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
