// Installed Authentik/Vue only. No routed responses, injected sessions or native writes.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal, isJobPageResponse } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { registerWaitingDirectory } from './container_recycle_cross_directory.mjs'

let phase = 'startup', browser, page, evidence
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  const credentials=fixture.credentials;delete fixture.credentials
  assert.equal(typeof credentials.owner.username,'string');assert.equal(typeof credentials.owner.password,'string')
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  let context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base,'/containers',true,credentials.owner)
  let recoverySubject=null,sourceRefusal,waitingDirectory
  const recycle = `/api/v1/containers/${fixture.containerId}/files/recycles`
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recycle-purges`
  const archiveEndpoint=`/api/v1/containers/${fixture.containerId}/files/recycle-archives`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const mutations = [], headerChecks = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === recycle || path.startsWith(recycle + '/') || path === endpoint || path.startsWith(endpoint + '/') || path===archiveEndpoint || path.startsWith(archiveEndpoint+'/'))) {
      mutations.push(path); headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key'] && !!h[csrf.headerName.toLowerCase()]))
    }
    assert.ok(!(request.method() === 'PUT' && path.includes('/files/')))
  })
  async function get(path) { const response = await context.request.get(base + path); assert.equal(response.status(), 200); return response.json() }
  async function coordinate(name, value) {
    await writeSignal(evidence, name + '-completed.json', { subject: profile.subject,recoverySubject, result: value })
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
  const completed = [], keys = [], jobProofs = []
  phase = 'purge-csrf-origin-refusal'
  for (const variant of ['csrf', 'origin']) {
    const response = await context.request.post(base + endpoint, {
      headers: variant === 'csrf' ? { Origin: base, 'Idempotency-Key': crypto.randomUUID() } : { ...headers, Origin: 'http://unapproved.invalid', 'Idempotency-Key': crypto.randomUUID() },
      data: { sourceIntentId: source.id },
    })
    assert.ok([400, 403].includes(response.status()))
  }
  for (const item of [{ name: 'cancel', theme: 'light' }, { name: 'success', theme: 'dark' }]) {
    phase = 'purge-' + item.name; await page.emulateMedia({ colorScheme: item.theme })
    await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(item.theme === 'dark')
    files = await openPermissionFiles(page, fixture)
    await expect(files.getByText('recycle-rpc.bin', { exact: true })).toHaveCount(0)
    const catalogResponse = page.waitForResponse(r => new URL(r.url()).pathname === endpoint + '/sources' && r.request().method() === 'GET')
    await files.getByRole('button', { name: '清理留存原件', exact: true }).click()
    const catalog = await catalogResponse; assert.equal(catalog.status(), 200)
    const sourcePage = await catalog.json(); const selected = sourcePage.items.find(r => r.sourceIntentId === source.id)
    assert.ok(selected); assert.equal(selected.sourceOperationId, source.executionJobId)
    assert.equal(selected.historyDigest, source.historyDigest); assert.equal(selected.sha256, fixture.original.sha256)
    const dialog = page.getByRole('dialog', { name: '清理容器留存原件', exact: true })
    await dialog.locator('.purge-source-row').filter({ hasText: source.id }).getByRole('button', { name: '选择此来源', exact: true }).click()
    const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成留存清理预检', exact: true }).click()
    const response = await registration; assert.equal(response.status(), 202)
    assert.deepEqual(response.request().postDataJSON(), { sourceIntentId: source.id })
    const initial = await response.json(), key = (await response.request().allHeaders())['idempotency-key']
    assert.ok(!keys.includes(key)); keys.push(key)
    await expect(dialog.getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible({ timeout: 60_000 })
    const ready = await get(endpoint + '/' + initial.id)
    assert.equal(ready.state, 'Ready'); assert.equal(ready.sourceIntentId, source.id)
    assert.equal(ready.sourceOperationId, source.executionJobId); assert.equal(ready.executionJobId, null)
    assert.equal(ready.sha256, fixture.original.sha256); assert.equal(ready.sourceHistoryDigest, source.historyDigest)
    assert.equal((await get(endpoint + '/by-key/' + key)).id, ready.id)
    const duplicate = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': key }, data: { sourceIntentId: source.id } })
    assert.equal(duplicate.status(), 202); assert.equal((await duplicate.json()).id, ready.id)
    await page.reload(); await expect(page).toHaveURL(base + '/containers'); await openPermissionFiles(page, fixture)
    await page.getByRole('button', { name: '清理留存原件', exact: true }).click()
    await expect(dialog.getByText('清理预检完成，尚未删除', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(dialog.getByRole('button', { name: '请求取消留存清理', exact: true })).toBeInViewport({ ratio: 1 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(evidence, item.name + '-ready.png') })
    if (item.name === 'cancel') {
      await dialog.getByRole('button', { name: '请求取消留存清理', exact: true }).click()
      await page.getByRole('button', { name: '继续等待', exact: true }).click()
      assert.equal((await get(endpoint + '/' + ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '请求取消留存清理', exact: true }).click()
      await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
      await expect(dialog.getByText('清理已取消', { exact: true })).toBeVisible({ timeout: 30_000 })
    } else {
      await coordinate('ready-to-purge', { id: ready.id, sourceIntentId: source.id, sourceOperationId: source.executionJobId, version: ready.version })
      await dialog.getByRole('button', { name: '确认留存清理', exact: true }).click()
      const finalConfirm = page.getByRole('dialog', { name: '确认清理留存原件', exact: true })
      await expect(finalConfirm).toContainText('恢复须走独立归档复制入口并重新核验HDD备份')
      await finalConfirm.getByRole('button', { name: '暂不清理', exact: true }).click()
      assert.equal((await get(endpoint + '/' + ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '确认留存清理', exact: true }).click()
      const finalReady = await get(endpoint + '/' + ready.id); assert.equal(finalReady.state, 'Ready'); assert.equal(finalReady.version, ready.version)
      await coordinate('final-confirm', { id: finalReady.id, sourceIntentId: source.id, sourceOperationId: source.executionJobId, version: finalReady.version })
      await expect(finalConfirm.getByRole('button', { name: '确认删除留存原件', exact: true })).toBeInViewport({ ratio: 1 })
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
      await finalConfirm.getByRole('button', { name: '确认删除留存原件', exact: true }).click()
      await expect(dialog.getByText('本次留存原件已清理', { exact: true })).toBeVisible({ timeout: 60_000 })
    }
    const result = await get(endpoint + '/' + ready.id)
    assert.equal(result.state, item.name === 'cancel' ? 'Canceled' : 'Succeeded'); assert.equal(result.confirmation, null)
    completed.push({ name: item.name, ...result }); await coordinate(item.name, result)
    await page.screenshot({ path: join(evidence, item.name + '-result.png') }); await closePermissionDetails(page, dialog)
  }
  const archives=[],archiveKeys=[],archiveJobProofs=[]
  for(const item of [{name:'archive-cancel',theme:'light'},{name:'archive-inflight',theme:'dark'}]) {
    phase=item.name;await page.emulateMedia({colorScheme:item.theme})
    files=await openPermissionFiles(page,fixture)
    const listed=page.waitForResponse(r=>new URL(r.url()).pathname===archiveEndpoint+'/sources'&&r.request().method()==='GET')
    await files.getByRole('button',{name:'从归档复制恢复',exact:true}).click()
    const sourceResponse=await listed;assert.equal(sourceResponse.status(),200)
    const selected=(await sourceResponse.json()).items.find(x=>x.sourceIntentId===completed[1].id);assert.ok(selected)
    assert.equal(selected.purgeOperationId,completed[1].executionJobId);assert.equal(selected.sourceOperationId,source.executionJobId)
    assert.equal(selected.historyDigest,source.historyDigest);assert.equal(selected.sha256,fixture.original.sha256)
    const dialog=page.getByRole('dialog',{name:'从HDD归档复制恢复',exact:true})
    await dialog.locator('.archive-source-row').filter({hasText:completed[1].id}).getByRole('button',{name:'选择此来源',exact:true}).click()
    const registration=page.waitForResponse(r=>new URL(r.url()).pathname===archiveEndpoint&&r.request().method()==='POST')
    await dialog.getByRole('button',{name:'生成归档恢复预检',exact:true}).click()
    const response=await registration;assert.equal(response.status(),202);assert.deepEqual(response.request().postDataJSON(),{sourceIntentId:completed[1].id})
    const initial=await response.json(),key=(await response.request().allHeaders())['idempotency-key'];assert.ok(!archiveKeys.includes(key));archiveKeys.push(key)
    await expect(dialog.getByText('归档预检完成，尚未复制',{exact:true})).toBeVisible({timeout:60_000})
    const ready=await get(archiveEndpoint+'/'+initial.id);assert.equal(ready.state,'Ready');assert.equal(ready.sourceIntentId,completed[1].id)
    assert.equal(ready.purgeOperationId,completed[1].executionJobId);assert.equal(ready.sourceOperationId,source.executionJobId)
    assert.equal((await get(archiveEndpoint+'/by-key/'+key)).id,ready.id)
    await page.screenshot({path:join(evidence,item.name+'-ready.png')})
    if(item.name==='archive-cancel') {
      await dialog.getByRole('button',{name:'请求取消归档恢复',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click()
      assert.equal((await get(archiveEndpoint+'/'+ready.id)).state,'Ready')
      await dialog.getByRole('button',{name:'请求取消归档恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
      await expect(dialog.getByText('归档恢复已取消',{exact:true})).toBeVisible({timeout:30_000})
    } else {
      const signal={id:ready.id,sourceIntentId:ready.sourceIntentId,purgeOperationId:ready.purgeOperationId,sourceOperationId:ready.sourceOperationId,version:ready.version}
      await coordinate('archive-ready',signal)
      phase='same-source-new-key-refusal'
      const rejectedKey=crypto.randomUUID(),before=await get(archiveEndpoint+'/'+ready.id)
      await writeSignal(evidence,'source-refusal-api-began.json',{id:ready.id,rejectedKey,actualApiRequest:true})
      const refused=await context.request.post(base+archiveEndpoint,{headers:{...headers,'Idempotency-Key':rejectedKey},data:{sourceIntentId:ready.sourceIntentId},timeout:15_000,maxRedirects:0,failOnStatusCode:false})
      await writeSignal(evidence,'source-refusal-api-received.json',{id:ready.id,status:refused.status()})
      assert.equal(refused.status(),409);const problem=await refused.json();assert.equal(problem.code,'OPERATION_CONFLICT')
      sourceRefusal={id:ready.id,sourceIntentId:ready.sourceIntentId,rejectedKey,status:refused.status(),code:problem.code,actualApiRequest:true,originAndIdempotencySupplied:true,before,after:await get(archiveEndpoint+'/'+ready.id)}
      await coordinate('source-conflict',sourceRefusal)
      await dialog.getByRole('button',{name:'确认归档恢复',exact:true}).click()
      const confirmation=page.getByRole('dialog',{name:'确认从归档复制恢复',exact:true});await expect(confirmation).toContainText('不会覆盖已有文件')
      await confirmation.getByRole('button',{name:'暂不恢复',exact:true}).click();assert.equal((await get(archiveEndpoint+'/'+ready.id)).state,'Ready')
      await dialog.getByRole('button',{name:'确认归档恢复',exact:true}).click();await coordinate('archive-final-confirm',signal)
      await expect(confirmation.getByRole('button',{name:'确认复制为新文件',exact:true})).toBeInViewport({ratio:1})
      await confirmation.getByRole('button',{name:'确认复制为新文件',exact:true}).click()
      phase='wait-exact-archive-published-namespace-cut'
      const cut=await waitSignal(evidence,'cut-held.json',110_000);assert.equal(cut.id,ready.id)
      if(cut.missed) {
        const current=await get(archiveEndpoint+'/'+ready.id)
        await writeSignal(evidence,'missed-outcome.json',{id:ready.id,state:current.state,noReplay:true})
        throw new Error('EXACT_ARCHIVE_CUT_MISSED_NO_REPLAY')
      }
      phase='visible-cancel-original-archive-execution'
      await writeSignal(evidence,'cancel-request-began.json',{id:ready.id,executionJobId:cut.executionJobId,monotonicNs:process.hrtime.bigint().toString()})
      await dialog.getByRole('button',{name:'请求取消归档恢复',exact:true}).click()
      await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
      await expect(dialog.getByText('归档结果未知，保护已保留',{exact:true})).toBeVisible({timeout:60_000})
    }
    const result=await get(archiveEndpoint+'/'+ready.id);assert.equal(result.state,item.name==='archive-cancel'?'Canceled':'Unknown');assert.equal(result.confirmation,null)
    if(item.name==='archive-inflight'){assert.equal(result.archiveCopied,null);assert.equal(result.reservationRecorded,true);assert.equal(result.receipt.state,'Unknown')}
    await coordinate(item.name,result);archives.push({name:item.name,...result})
    await page.screenshot({path:join(evidence,item.name+'-result.png')});await closePermissionDetails(page,dialog)
  }
  assert.equal(mutations.filter(x=>x===archiveEndpoint).length,2);assert.equal(mutations.filter(x=>x.startsWith(archiveEndpoint+'/')&&x.endsWith('/confirm')).length,1);assert.equal(mutations.filter(x=>x.startsWith(archiveEndpoint+'/')&&x.endsWith('/cancel')).length,2)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase='normal-owner-logout';const ownerLogout=await logoutFromUserMenu(page,context,base)
  const anonymous=await context.request.get(base+archiveEndpoint+'/'+archives[1].id);assert.equal(anonymous.status(),401)
  for(const key of ['whaledeck-container-recycle-archive-hints-v1','whaledeck-container-recycle-purge-hints-v1','whaledeck-container-recycle-hints-v1'])await expect.poll(()=>page.evaluate(k=>sessionStorage.getItem(k),key)).toBe(null)
  await context.close();context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'light',reducedMotion:'reduce'});context.setDefaultTimeout(20_000);page=await context.newPage()
  phase='visible-recovery-login';const admin=await loginVisible(page,context,base,'/files',true,credentials.recovery);recoverySubject=admin.subject;assert.notEqual(admin.subject,profile.subject)
  const recoveryPath='/api/v1/files/container-recycle-archives/recovery/'+archives[1].id;let managementWrites=0,acknowledgements=0
  context.on('request',r=>{const u=new URL(r.url());if(u.origin===base&&['POST','PATCH','PUT','DELETE'].includes(r.method())&&u.pathname.startsWith('/api/v1/')&&!u.pathname.startsWith('/api/v1/auth/')){managementWrites++;if(r.method()==='POST'&&u.pathname===recoveryPath+'/acknowledge')acknowledgements++}})
  const old=await context.request.get(base+archiveEndpoint+'/'+archives[1].id);assert.equal(old.status(),404)
  async function showJob(state,title,screenshot){
    const listing=page.waitForResponse(r=>isJobPageResponse(r,base));await page.goto(base+'/jobs');const rows=await(await listing).json(),pos=rows.findIndex(j=>j.id===archives[1].executionJobId);assert.ok(pos>=0);assert.equal(rows[pos].state,state)
    const job=await get('/api/v1/jobs/'+archives[1].executionJobId),facts=JSON.parse(job.resultJson);assert.equal(Object.keys(facts).length,19);assert.equal(facts.archiveCopied,null);assert.equal(facts.receiptState,'Unknown');assert.equal(facts.sourceIntentId,completed[1].id)
    await page.locator('.jobs-page .el-table__body tr').nth(pos).getByRole('button',{name:'详情',exact:true}).click();const d=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=d.getByRole('region',{name:'归档复制安全结果',exact:true})
    await expect(summary).toContainText(title);await expect(summary).toContainText('未证明');await expect(d.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0);await page.screenshot({path:join(evidence,screenshot)});jobProofs.push(job);await d.getByRole('button',{name:'Close this dialog',exact:true}).click()
  }
  phase='register-peer-directory';await page.goto(base+'/containers');const showWaiting=await registerWaitingDirectory(page,base,fixture,{subject:recoverySubject})
  await showJob('Running','归档结果未知，仍需核实原任务','unknown-job.png')
  phase='different-admin-decision';await page.goto(base+'/files');await page.getByRole('button',{name:'容器归档恢复异常',exact:true}).click()
  const recovery=page.getByRole('dialog',{name:'容器归档恢复异常处置',exact:true}),row=recovery.locator('.archive-recovery__row').filter({hasText:archives[1].id});await expect(row).toHaveCount(1);await expect(row).toContainText('尚未证明')
  await row.getByRole('button',{name:'处置历史结果',exact:true}).click();await expect(recovery.getByRole('button',{name:'暂不处置',exact:true})).toBeInViewport({ratio:1});await page.screenshot({path:join(evidence,'ack-confirm.png')});await recovery.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(managementWrites,2);assert.equal(acknowledgements,0)
  await coordinate('decision',await get(recoveryPath))
  await row.getByRole('button',{name:'处置历史结果',exact:true}).click();const posted=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===recoveryPath+'/acknowledge');await recovery.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();assert.equal((await posted).status(),202);await expect(row).toHaveCount(0)
  const final=await get(recoveryPath);assert.equal(final.state,'Acknowledged');assert.equal(final.archiveCopied,null);assert.equal(final.receipt.state,'Unknown');await coordinate('ack',final);await page.screenshot({path:join(evidence,'acknowledged.png')});await recovery.getByRole('button',{name:'关闭',exact:true}).click()
  const finished=await waitSignal(evidence,'waiting-finished.json',120_000);waitingDirectory=await showWaiting(context,finished)
  await showJob('Failed','管理员已明确处置未知归档结果','acknowledged-job.png');assert.equal(managementWrites,3);assert.equal(acknowledgements,1)
  phase='normal-recovery-logout';const recoveryLogout=await logoutFromUserMenu(page,context,base)
  waitingDirectory.anonymousStatus=(await context.request.get(base+waitingDirectory.privateEndpoint)).status();assert.equal(waitingDirectory.anonymousStatus,401);delete waitingDirectory.privateEndpoint
  await writeSignal(evidence,'browser.json',{revision,containerId:fixture.containerId,subject:profile.subject,recoverySubject,source,completed,archives,keys,archiveKeys,mutations,final,actualUi:true,acknowledgements,managementWrites,originalSessionReadStatus:anonymous.status(),differentAdminOldIdStatus:old.status(),hintsCleared:true,cancelledDecisionPreserved:true,logoutReceipts:[ownerLogout,recoveryLogout],jobProofs,waitingDirectory,sourceRefusal})
  await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({actualArchiveCrossDirectoryBrowser:'passed'}))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_archive_cross_directory.mjs:'))?.trim()
  if (page && evidence && new URL(page.url()).origin==='http://127.0.0.1:8080' && new URL(page.url()).pathname!=='/login' && !['startup', 'visible-login', 'visible-recovery-login'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualArchiveCrossDirectoryBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
