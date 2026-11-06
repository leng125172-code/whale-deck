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
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision);assert.ok(['group-removal','account-disable'].includes(fixture.kind))
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  let context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base)
  let recoverySubject=null,ownerLogoutPosts=0
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
    await writeSignal(evidence, name + '-completed.json', { subject: profile.subject, recoverySubject, result: value })
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
  await expect(confirm).toContainText('恢复须走独立归档复制入口并重新核验HDD备份')
  await confirm.getByRole('button',{name:'确认删除留存原件',exact:true}).click()
  phase='wait-authority-change-and-original-exit'
  const settled=await waitSignal(evidence,'settled.json',210000)
  assert.equal(settled.id,ready.id);assert.equal(settled.state,'Canceled')
  phase = 'revoked-original-session'
  const originalIntentStatus = (await context.request.get(base + endpoint + '/' + ready.id)).status()
  assert.ok([401, 403].includes(originalIntentStatus))
  const me = await context.request.get(base + '/api/v1/auth/me'), currentIdentityStatus = me.status()
  let currentSubject = null, currentlyAdministrator = null, renderedRevocationPage
  if (fixture.kind === 'group-removal') {
    assert.equal(originalIntentStatus, 403); assert.equal(me.status(), 200)
    const value = await me.json(); assert.equal(value.subject, profile.subject); assert.equal(value.isAdministrator, false)
    currentSubject = value.subject; currentlyAdministrator = false
  } else assert.ok([401, 403].includes(me.status()))
  await page.reload()
  await expect(page.getByRole('status', { name: '正在启动 WhaleDeck 协同平台', exact: true })).toHaveCount(0)
  if (fixture.kind === 'group-removal') {
    await expect(page).toHaveURL(base + '/forbidden')
    await expect(page.getByRole('heading', { name: '没有管理权限', exact: true })).toBeVisible(); renderedRevocationPage = 'forbidden'
  } else {
    await expect(page).toHaveURL(url => url.origin === base && url.pathname === '/login')
    await expect(page.getByRole('heading', { name: '使用工作站账号登录', exact: true })).toBeVisible(); renderedRevocationPage = 'login'
  }
  await expect(page.getByRole('region', { name: '独立留存清理', exact: true })).toHaveCount(0)
  await expect(page.getByRole('dialog', { name: '清理容器留存原件', exact: true })).toHaveCount(0)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-purge-hints-v1'))).toBe(null)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('whaledeck-container-recycle-hints-v1'))).toBe(null)
  await expect(page.getByRole('region', { name: '容器文件回收任务', exact: true })).toHaveCount(0)
  await page.screenshot({ path: join(evidence, 'authority-denied.png') })
  const registrations = mutations.filter(p => p === endpoint).length, confirmations = mutations.filter(p => p.startsWith(endpoint + '/') && p.endsWith('/confirm')).length, cancellations = mutations.filter(p => p.endsWith('/cancel')).length
  assert.equal(registrations, 1); assert.equal(confirmations, 1); assert.equal(cancellations, 0); assert.equal(ownerLogoutPosts, 0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  await context.close()


  context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'light',reducedMotion:'reduce'})
  context.setDefaultTimeout(20000);page=await context.newPage();phase='visible-recovery-login'
  const admin=await loginVisible(page,context,base,'/containers',true,{username:process.env.RECOVERY_USERNAME,password:process.env.RECOVERY_PASSWORD})
  recoverySubject=admin.subject;assert.notEqual(admin.subject,profile.subject)
  const recoveryWrites=[],originalOperationRecoveryWrites=[]
  context.on('request',request=>{
    const u=new URL(request.url())
    if(u.origin===base && ['POST','PATCH','PUT','DELETE'].includes(request.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')) {
      recoveryWrites.push(u.pathname)
      if(u.pathname===endpoint||u.pathname.startsWith(endpoint+'/')||u.pathname.startsWith('/api/v1/files/container-recycle-purges/recovery/'))originalOperationRecoveryWrites.push(u.pathname)
    }
  })
  phase='different-admin-read-original-result'
  const oldPaths=[endpoint+'/'+ready.id,endpoint+'/by-key/'+key,recycle+'/'+source.id]
  for(const path of oldPaths)assert.equal((await context.request.get(base+path)).status(),404)
  const final=await get('/api/v1/files/container-recycle-purges/recovery/'+ready.id)
  assert.equal(final.id,ready.id);assert.equal(final.executionJobId,settled.executionJobId);assert.equal(final.state,'Canceled')
  assert.equal(final.receipt.state,'Aborted');assert.equal(final.purgeApplied,false);assert.equal(final.reservationRecorded,true)
  assert.equal(recoveryWrites.length,0)
  await coordinate('revocation',final)
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
  assert.equal(Object.keys(facts).length,16);assert.equal(facts.state,'Canceled');assert.equal(facts.receiptState,'Aborted');assert.equal(facts.purgeApplied,false);assert.equal(facts.reservationRecorded,true);assert.equal(facts.applyStarted,true);assert.equal(facts.sourceIntentId,source.id)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click()
  const detail=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=detail.getByRole('region',{name:'留存清理安全结果',exact:true})
  await expect(summary).toContainText('已取消清理');await expect(detail.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
  await page.screenshot({path:join(evidence,'revoked-execution-job.png')});jobProofs.push(job)
  await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  assert.equal(mutations.filter(p=>p===endpoint).length,1)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/confirm')).length,1)
  assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/cancel')).length,0)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  assert.deepEqual(recoveryWrites,[restoreBase,restoreBase+'/'+restored.id+'/confirm']);assert.deepEqual(originalOperationRecoveryWrites,[])
  phase='recovery-normal-dual-logout';const recoveryLogout=await logoutFromUserMenu(page,context,base)
  await writeSignal(evidence,'browser.json',{revision,containerId:fixture.containerId,subject:profile.subject,recoverySubject:admin.subject,source,original:{...ready,confirmation:null},final,key,restored,
    kind:fixture.kind,registrations,confirmations,cancellations,ownerLogoutPosts,originalIntentStatus,currentIdentityStatus,currentSubject,currentlyAdministrator,renderedRevocationPage,
    privatePanelsAbsent:true,hintsCleared:true,oldOwnerReferencesDenied:oldPaths.length,recoveryWrites,originalOperationRecoveryWrites,recoveryLogout,actualUi:true,jobProofs,mutations})
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualPurgeRevocationBrowser: 'passed', cases: 2 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_purge_revocation.mjs:'))?.trim()
  if (page && evidence && !['startup', 'visible-login', 'visible-recovery-login'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualPurgeRevocationBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
