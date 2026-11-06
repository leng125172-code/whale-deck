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
  phase='wait-service-fault-and-original-unknown'
  const settled=await waitSignal(evidence,'restart-settled.json',270000);assert.equal(settled.id,ready.id);assert.equal(settled.state,'Unknown')
  const ownerResult=await get(endpoint+'/'+ready.id);assert.equal(ownerResult.executionJobId,settled.executionJobId);assert.equal(ownerResult.state,'Unknown');assert.equal(ownerResult.purgeApplied,null)
  const owner=await get('/api/v1/auth/me');assert.equal(owner.subject,profile.subject);assert.equal(owner.isAdministrator,true);assert.equal(ownerLogoutPosts,0)
  assert.equal(mutations.filter(p=>p.endsWith('/cancel')).length,0)
  await dialog.getByRole('button',{name:'查询原留存清理任务',exact:true}).click();await expect(dialog.getByText('清理结果未知，保护已保留',{exact:true})).toBeVisible({timeout:20000})
  await page.screenshot({path:join(evidence,'owner-after-service-unknown.png')});await closePermissionDetails(page,dialog)
  phase='normal-owner-logout-after-fault';const ownerLogout=await logoutFromUserMenu(page,context,base)
  const anonymous=await context.request.get(base+endpoint+'/'+ready.id);assert.equal(anonymous.status(),401)
  for(const name of ['whaledeck-container-recycle-hints-v1','whaledeck-container-recycle-purge-hints-v1'])await expect.poll(()=>page.evaluate(key=>sessionStorage.getItem(key),name)).toBe(null)
  assert.equal(mutations.filter(p=>p===endpoint).length,1);assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/confirm')).length,1);assert.equal(mutations.filter(p=>p.startsWith(endpoint+'/')&&p.endsWith('/cancel')).length,0);assert.ok((await Promise.all(headerChecks)).every(Boolean))
  await context.close()
  context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'light',reducedMotion:'reduce'});context.setDefaultTimeout(20000);page=await context.newPage()
  phase='visible-recovery-login';const admin=await loginVisible(page,context,base,'/files',true,{username:process.env.RECOVERY_USERNAME,password:process.env.RECOVERY_PASSWORD});assert.notEqual(admin.subject,profile.subject);recoverySubject=admin.subject
  let managementWrites=0,acknowledgements=0;const recoveryPath='/api/v1/files/container-recycle-purges/recovery/'+ready.id
  context.on('request',r=>{const u=new URL(r.url());if(u.origin===base && ['POST','PATCH','PUT','DELETE'].includes(r.method()) && u.pathname.startsWith('/api/v1/') && !u.pathname.startsWith('/api/v1/auth/')){managementWrites++;if(r.method()==='POST'&&u.pathname===recoveryPath+'/acknowledge')acknowledgements++}})
  const old=await context.request.get(base+endpoint+'/'+ready.id);assert.equal(old.status(),404)
  let unknown;const until=Date.now()+60000
  while(true){unknown=await get(recoveryPath);if(unknown.state==='Unknown')break;assert.ok(['Pending','Reserving','Applying'].includes(unknown.state));assert.ok(Date.now()<until);await page.waitForTimeout(250)}
  assert.equal(unknown.executionJobId,settled.executionJobId);assert.equal(unknown.purgeApplied,null);assert.equal(unknown.receipt.state,'Unknown');await coordinate('unknown',unknown)
  async function showJob(state,title,screenshot){
    const listing=page.waitForResponse(r=>isJobPageResponse(r,base));await page.goto(base+'/jobs');const jobs=await(await listing).json(),position=jobs.findIndex(j=>j.id===settled.executionJobId);assert.ok(position>=0);assert.equal(jobs[position].state,state)
    const job=await get('/api/v1/jobs/'+settled.executionJobId),facts=JSON.parse(job.resultJson);assert.equal(Object.keys(facts).length,16);assert.equal(facts.purgeApplied,null);assert.equal(facts.receiptState,'Unknown')
    await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click();const detail=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=detail.getByRole('region',{name:'留存清理安全结果',exact:true})
    await expect(summary).toContainText(title);await expect(summary).toContainText('未证明');await expect(detail.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0);await page.screenshot({path:join(evidence,screenshot)});jobProofs.push(job);await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  }
  await showJob('Running','清理结果未知，仍需核实原任务','unknown-job.png')
  await page.goto(base+'/files');await page.getByRole('button',{name:'容器留存清理异常',exact:true}).click()
  const recovery=page.getByRole('dialog',{name:'容器留存清理异常处置',exact:true}),row=recovery.locator('.purge-recovery__row').filter({hasText:ready.id});await expect(row).toHaveCount(1);await expect(row).toContainText('尚未证明')
  await row.getByRole('button',{name:'处置历史结果',exact:true}).click();await expect(recovery.getByRole('button',{name:'暂不处置',exact:true})).toBeInViewport({ratio:1});await page.screenshot({path:join(evidence,'ack-confirm.png')})
  await recovery.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(acknowledgements,0);assert.equal(managementWrites,0)
  await coordinate('decision',await get(recoveryPath))
  await row.getByRole('button',{name:'处置历史结果',exact:true}).click();const posted=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===recoveryPath+'/acknowledge')
  await recovery.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();assert.equal((await posted).status(),202);await expect(row).toHaveCount(0);await expect(recovery.getByRole('status')).toContainText('已处置历史未知结果并释放保护')
  const final=await get(recoveryPath);assert.equal(final.state,'Acknowledged');assert.equal(final.purgeApplied,null);assert.equal(final.receipt.state,'Unknown')
  await coordinate('ack',final);await page.screenshot({path:join(evidence,'acknowledged.png')});await recovery.getByRole('button',{name:'关闭',exact:true}).click()
  await showJob('Failed','管理员已明确处置未知清理结果','acknowledged-job.png');assert.equal(acknowledgements,1);assert.equal(managementWrites,1)
  phase='normal-recovery-logout';const recoveryLogout=await logoutFromUserMenu(page,context,base)
  await writeSignal(evidence,'browser.json',{revision,kind:fixture.kind,originalOwnerStillAdministrator:true,ownerLogoutPostsBeforeOutcome:0,containerId:fixture.containerId,subject:profile.subject,recoverySubject:admin.subject,source,unknown,final,key,actualUi:true,registrations:1,confirmations:1,cancellations:0,acknowledgements,managementWrites,originalSessionReadStatus:anonymous.status(),differentAdminOldIdStatus:old.status(),hintsCleared:true,cancelledDecisionPreserved:true,logoutReceipts:[ownerLogout,recoveryLogout],jobProofs})
  await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({purgeUnlinkedRestartBrowser:'passed'}))
} catch(error){
  const frame=error?.stack?.split('\n').find(line=>line.includes('validate_container_recycle_purge_unlinked_restart.mjs:'))?.trim()
  if(page&&evidence&&!['startup','visible-login','visible-recovery-login'].includes(phase))await page.screenshot({path:join(evidence,'failed-product-page.png')}).catch(()=>{})
  console.error(JSON.stringify({purgeUnlinkedRestartBrowser:'failed',phase,failureType:error?.name,frame}));if(browser)await browser.close().catch(()=>{});process.exitCode=1
}
