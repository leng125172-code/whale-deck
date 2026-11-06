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
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
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
  const recycle = `/api/v1/containers/${fixture.containerId}/files/recycles`
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recycle-purges`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const mutations = [], headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === recycle || path.startsWith(recycle + '/') || path === endpoint || path.startsWith(endpoint + '/'))) {
      mutations.push(path); headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key'] && !!h[csrf.headerName.toLowerCase()]))
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
  const completed = [], jobProofs = []
  phase = 'purge-ready-before-logout'
  files = await openPermissionFiles(page, fixture)
  await files.getByRole('button', { name: '清理留存原件', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '清理容器留存原件', exact: true })
  await dialog.locator('.purge-source-row').filter({ hasText: source.id }).getByRole('button', { name: '选择此来源', exact: true }).click()
  const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '生成留存清理预检', exact: true }).click()
  const response = await registration; assert.equal(response.status(), 202)
  assert.deepEqual(response.request().postDataJSON(), { sourceIntentId: source.id })
  const initial = await response.json(), key = (await response.request().allHeaders())['idempotency-key']
  await expect(dialog.getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible({ timeout: 60_000 })
  const ready = await get(endpoint + '/' + initial.id)
  assert.equal(ready.state, 'Ready'); assert.equal(ready.executionJobId, null); assert.ok(ready.confirmation)
  const began = performance.now(), expires = Date.parse(ready.readyExpiresAtUtc)
  assert.ok(expires > Date.now() && expires-Date.now() <= 300000)
  await coordinate('ready', { id: ready.id, sourceIntentId: source.id, sourceOperationId: source.executionJobId, version: ready.version })
  await page.screenshot({ path: join(evidence, 'ready-before-logout.png') })
  await closePermissionDetails(page, dialog)
  phase = 'ready-normal-dual-logout'; await logoutFromUserMenu(page, context, base)
  const anonymous=[]
  for (const path of [endpoint+'/'+ready.id,endpoint+'/by-key/'+key,'/api/v1/files/container-recycle-purges/recovery','/api/v1/files/container-recycle-purges/recovery/'+ready.id]) {
    const r=await context.request.get(base+path);anonymous.push(r.status());assert.equal(r.status(),401)
  }
  const oldConfirm=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{...headers,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}})
  assert.equal(oldConfirm.status(),401)
  phase = 'visible-relogin'; const again = await loginVisible(page,context,base);assert.equal(again.subject,profile.subject)
  const newReads=[]
  for (const path of [endpoint+'/'+ready.id,endpoint+'/by-key/'+key]) { const r=await context.request.get(base+path);newReads.push(r.status());assert.equal(r.status(),404) }
  const freshCsrf=await (await context.request.get(base+'/api/v1/auth/csrf')).json()
  const newConfirm=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{Origin:base,[freshCsrf.headerName]:freshCsrf.token,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}})
  assert.equal(newConfirm.status(),404)
  phase = 'natural-expiry-observation'; const observations=[];let final
  while (true) {
    const value=await get('/api/v1/files/container-recycle-purges/recovery/'+ready.id)
    assert.equal(value.id,ready.id);assert.equal(value.readyExpiresAtUtc,ready.readyExpiresAtUtc);assert.equal(value.executionJobId,null)
    observations.push({elapsedMs:Math.round(performance.now()-began),state:value.state})
    if (value.state==='Expired') { final=value;break }
    assert.equal(value.state,'Ready');assert.ok(Date.now()<expires+60000);await page.waitForTimeout(1500)
  }
  assert.ok(Date.now()>=expires)
  await coordinate('expiry', final)
  completed.push({name:'expiry',...final})
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
  phase = 'actual-historical-planning-job';await page.setViewportSize({width:1440,height:1050})
  const listing=page.waitForResponse(r=>isJobPageResponse(r,base));await page.goto(base+'/jobs');const listed=await (await listing).json();const position=listed.findIndex(j=>j.id===ready.planningJobId);assert.ok(position>=0)
  const job=await get('/api/v1/jobs/'+ready.planningJobId),facts=JSON.parse(job.resultJson)
  assert.equal(Object.keys(facts).length,16);assert.equal(facts.state,'Ready');assert.equal(facts.sourceIntentId,source.id)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click()
  const detail=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=detail.getByRole('region',{name:'留存清理安全结果',exact:true})
  await expect(summary).toContainText('预检完成，尚未删除留存原件');await expect(detail.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
  await page.screenshot({path:join(evidence,'historical-ready-job.png')});jobProofs.push(job)
  await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  assert.equal(mutations.filter(p=>p===endpoint).length,1)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/confirm')).length,0)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/cancel')).length,0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase='second-normal-dual-logout';await logoutFromUserMenu(page,context,base)
  await writeSignal(evidence,'browser.json',{revision,containerId:fixture.containerId,subject:profile.subject,source,original:{...ready,confirmation:null},final,key,restored,observations,elapsedMs:Math.round(performance.now()-began),anonymous,oldConfirmStatus:oldConfirm.status(),newLoginReads:newReads,newConfirmStatus:newConfirm.status(),normalDualLogouts:2,registrations:1,confirmations:0,cancellations:0,clockChanged:false,actualUi:true,jobProofs,mutations})
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualPurgeReadyLogoutBrowser: 'passed', cases: 2 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_purge_ready_logout.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-relogin'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualPurgeReadyLogoutBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
