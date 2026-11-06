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
  assert.ok(['group-removal','account-disable'].includes(fixture.kind));assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/)
  assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  const credentials=fixture.credentials;delete fixture.credentials
  assert.equal(typeof credentials.owner.username,'string');assert.equal(typeof credentials.owner.password,'string')
  process.env.VALIDATION_USERNAME=credentials.owner.username;process.env.VALIDATION_PASSWORD=credentials.owner.password
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'
  assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  let context = await browser.newContext({ viewport: { width: 320, height: 600 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000); page = await context.newPage()
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'visible-login'; const profile = await loginVisible(page, context, base)
  let recoverySubject=null,ownerLogoutPosts=0,revocationUi
  page.on('request',r=>{const u=new URL(r.url());if(r.method()==='POST'&&u.origin===base&&u.pathname==='/api/v1/auth/logout')ownerLogoutPosts++})
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
  async function diagnosedLogout(label) {
    const navigation=[]
    const requested=request=>{
      if(!request.isNavigationRequest() || navigation.length>=20)return
      const url=new URL(request.url())
      // Booleans and origin/path only: never persist token/state/query values.
      navigation.push({kind:'request',method:request.method(),origin:url.origin,path:url.pathname,hasIdTokenHint:url.searchParams.has('id_token_hint'),hasPostLogoutRedirect:url.searchParams.has('post_logout_redirect_uri')})
    }
    const responded=response=>{
      if(!response.request().isNavigationRequest() || navigation.length>=20)return
      const url=new URL(response.url());navigation.push({kind:'response',status:response.status(),origin:url.origin,path:url.pathname})
    }
    page.on('request',requested);page.on('response',responded)
    try {
      return await logoutFromUserMenu(page,context,base,undefined,async observation=>{
        const url=new URL(page.url());await writeSignal(evidence,label+'-'+observation.phase+'.json',{...observation,pageOrigin:url.origin,pagePath:url.pathname})
      })
    } finally {
      page.off('request',requested);page.off('response',responded);await writeSignal(evidence,label+'-navigation.json',{navigation})
    }
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
  const archives=[],archiveKeys=[],archiveJobProofs=[];const recoveryWrites=[],originalOperationRecoveryWrites=[]
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
    let result
    if(item.name==='archive-cancel') {
      await dialog.getByRole('button',{name:'请求取消归档恢复',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click()
      assert.equal((await get(archiveEndpoint+'/'+ready.id)).state,'Ready')
      await dialog.getByRole('button',{name:'请求取消归档恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
      await expect(dialog.getByText('归档恢复已取消',{exact:true})).toBeVisible({timeout:30_000})
    } else {
      const signal={id:ready.id,sourceIntentId:ready.sourceIntentId,purgeOperationId:ready.purgeOperationId,sourceOperationId:ready.sourceOperationId,version:ready.version}
      await coordinate('archive-ready',signal)
      await dialog.getByRole('button',{name:'确认归档恢复',exact:true}).click()
      const confirmation=page.getByRole('dialog',{name:'确认从归档复制恢复',exact:true});await expect(confirmation).toContainText('不会覆盖已有文件')
      await confirmation.getByRole('button',{name:'暂不恢复',exact:true}).click();assert.equal((await get(archiveEndpoint+'/'+ready.id)).state,'Ready')
      await dialog.getByRole('button',{name:'确认归档恢复',exact:true}).click();await coordinate('archive-final-confirm',signal)
      await expect(confirmation.getByRole('button',{name:'确认复制为新文件',exact:true})).toBeInViewport({ratio:1})
      await confirmation.getByRole('button',{name:'确认复制为新文件',exact:true}).click()
      phase='wait-authority-change-and-original-exit'
      const settled=await waitSignal(evidence,'settled.json',210_000);assert.equal(settled.id,ready.id);assert.equal(settled.state,'Canceled')
      phase='revoked-original-session'
      const originalIntentStatus=(await context.request.get(base+archiveEndpoint+'/'+ready.id)).status();assert.ok([401,403].includes(originalIntentStatus))
      const me=await context.request.get(base+'/api/v1/auth/me');let currentSubject=null,currentlyAdministrator=null,renderedRevocationPage
      if(fixture.kind==='group-removal'){
        assert.equal(originalIntentStatus,403);assert.equal(me.status(),200);const identity=await me.json();assert.equal(identity.subject,profile.subject);assert.equal(identity.isAdministrator,false);currentSubject=identity.subject;currentlyAdministrator=false
      } else assert.ok([401,403].includes(me.status()))
      await page.reload();await expect(page.getByRole('status',{name:'正在启动 WhaleDeck 协同平台',exact:true})).toHaveCount(0)
      if(fixture.kind==='group-removal'){
        await expect(page).toHaveURL(base+'/forbidden');await expect(page.getByRole('heading',{name:'没有管理权限',exact:true})).toBeVisible();renderedRevocationPage='forbidden'
      }else{
        await expect(page).toHaveURL(url=>url.origin===base&&url.pathname==='/login');await expect(page.getByRole('heading',{name:'使用工作站账号登录',exact:true})).toBeVisible();renderedRevocationPage='login'
      }
      await expect(page.getByRole('region',{name:'独立归档恢复',exact:true})).toHaveCount(0);await expect(page.getByRole('dialog',{name:'从HDD归档复制恢复',exact:true})).toHaveCount(0)
      for(const key of ['whaledeck-container-recycle-archive-hints-v1','whaledeck-container-recycle-purge-hints-v1','whaledeck-container-recycle-hints-v1'])await expect.poll(()=>page.evaluate(k=>sessionStorage.getItem(k),key)).toBe(null)
      assert.equal(ownerLogoutPosts,0)
      if(fixture.kind==='group-removal')await page.screenshot({path:join(evidence,'authority-denied.png')})
      revocationUi={originalIntentStatus,currentIdentityStatus:me.status(),currentSubject,currentlyAdministrator,renderedRevocationPage,privatePanelsAbsent:true,hintsCleared:true}
      await context.close()
      context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'light',reducedMotion:'reduce'});context.setDefaultTimeout(20_000);page=await context.newPage();phase='visible-recovery-login'
      const admin=await loginVisible(page,context,base,'/containers',true,credentials.recovery);recoverySubject=admin.subject;assert.notEqual(admin.subject,profile.subject)
      context.on('request',request=>{
        const url=new URL(request.url())
        if(url.origin===base&&['POST','PATCH','PUT','DELETE'].includes(request.method())&&url.pathname.startsWith('/api/v1/')&&!url.pathname.startsWith('/api/v1/auth/')){
          recoveryWrites.push(url.pathname)
          if(url.pathname.startsWith(archiveEndpoint)||url.pathname.startsWith('/api/v1/files/container-recycle-archives/recovery/'))originalOperationRecoveryWrites.push(url.pathname)
        }
      })
      const paths=[endpoint+'/'+completed[0].id,endpoint+'/'+completed[1].id,...keys.map(k=>endpoint+'/by-key/'+k),recycle+'/'+source.id,archiveEndpoint+'/'+archives[0].id,archiveEndpoint+'/'+ready.id,...archiveKeys.map(k=>archiveEndpoint+'/by-key/'+k)],statuses=[];assert.equal(paths.length,9)
      for(const path of paths){const response=await context.request.get(base+path);assert.equal(response.status(),404);statuses.push(response.status())}
      revocationUi.oldReferenceStatuses=statuses
      result=await get('/api/v1/files/container-recycle-archives/recovery/'+ready.id);assert.equal(result.executionJobId,settled.executionJobId);assert.equal(recoveryWrites.length,0)
    }
    if(item.name==='archive-cancel')result=await get(archiveEndpoint+'/'+ready.id)
    assert.equal(result.state,'Canceled');assert.equal(result.confirmation,null)
    if(item.name==='archive-inflight'){assert.equal(result.archiveCopied,false);assert.equal(result.reservationRecorded,true);assert.equal(result.receipt.state,'Aborted')}
    await coordinate(item.name,result);archives.push({name:item.name,...result})
    await page.screenshot({path:join(evidence,item.name+'-result.png')});if(item.name==='archive-cancel')await closePermissionDetails(page,dialog)
  }
  phase = 'actual-job-summaries'; await page.setViewportSize({ width: 1440, height: 1050 })
  for (const [index, id] of [completed[0].planningJobId, completed[1].executionJobId].entries()) {
    const listing = page.waitForResponse(r => isJobPageResponse(r, base)); await page.goto(base + '/jobs')
    const listed = await (await listing).json(); const position = listed.findIndex(j => j.id === id); assert.ok(position >= 0)
    const job = await get('/api/v1/jobs/' + id), safe = JSON.parse(job.resultJson)
    assert.equal(Object.keys(safe).length, 16); assert.equal(safe.sourceIntentId, source.id); assert.equal(safe.sourceOperationId, source.executionJobId)
    assert.equal(safe.state, index === 0 ? 'Ready' : 'Succeeded'); assert.equal(safe.jobId, id)
    await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button', { name: '详情', exact: true }).click()
    const detail = page.getByRole('dialog', { name: '任务详情', exact: true }), summary = detail.getByRole('region', { name: '留存清理安全结果', exact: true })
    await expect(summary).toContainText(index === 0 ? '预检完成，尚未删除留存原件' : '留存原件清理已完成')
    await expect(summary).toContainText(source.id); await expect(summary).toContainText(source.executionJobId)
    await page.screenshot({ path: join(evidence, 'actual-job-' + index + '.png') }); jobProofs.push(job)
    await detail.getByRole('button', { name: 'Close this dialog', exact: true }).click()
  }
  for(const [index,id] of [archives[0].planningJobId,archives[1].executionJobId].entries()) {
    const listing=page.waitForResponse(r=>isJobPageResponse(r,base));await page.goto(base+'/jobs');const rows=await (await listing).json();const position=rows.findIndex(x=>x.id===id);assert.ok(position>=0)
    const job=await get('/api/v1/jobs/'+id),safe=JSON.parse(job.resultJson);assert.equal(Object.keys(safe).length,19)
    assert.equal(safe.sourceIntentId,completed[1].id);assert.equal(safe.originalIntentId,source.id);assert.equal(safe.purgeOperationId,completed[1].executionJobId);assert.equal(safe.sourceOperationId,source.executionJobId)
    assert.equal(safe.state,index===0?'Ready':'Canceled');if(index===1){assert.equal(safe.receiptState,'Aborted');assert.equal(safe.archiveCopied,false);assert.equal(safe.reservationRecorded,true);assert.equal(safe.applyStarted,true)}
    await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click()
    const detail=page.getByRole('dialog',{name:'任务详情',exact:true}),summary=detail.getByRole('region',{name:'归档复制安全结果',exact:true})
    await expect(detail.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
    await expect(summary).toContainText(index===0?'预检完成，尚未从HDD复制':'已取消归档恢复')
    await expect(summary).toContainText(source.id);await expect(summary).toContainText(completed[1].id)
    await page.screenshot({path:join(evidence,'actual-archive-job-'+index+'.png')});archiveJobProofs.push(job);await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  }
  assert.equal(mutations.filter(x=>x===archiveEndpoint).length,2)
  assert.equal(mutations.filter(x=>x.startsWith(archiveEndpoint+'/')&&x.endsWith('/confirm')).length,1)
  assert.equal(mutations.filter(x=>x.startsWith(archiveEndpoint+'/')&&x.endsWith('/cancel')).length,1);assert.equal(ownerLogoutPosts,0)
  assert.equal((await get(endpoint + '/sources')).items.some(r => r.sourceIntentId === source.id), false)
  assert.equal(mutations.filter(p => p === recycle).length, 1); assert.equal(mutations.filter(p => p === recycle + '/' + source.id + '/confirm').length, 1)
  assert.equal(mutations.filter(p => p === endpoint).length, 2); assert.equal(mutations.filter(p => p.startsWith(endpoint+'/')&&p.endsWith('/cancel')).length, 1)
  assert.equal(mutations.filter(p => p.startsWith(endpoint + '/') && p.endsWith('/confirm')).length, 1)
  assert.ok((await Promise.all(headerChecks)).every(Boolean))
  assert.deepEqual(recoveryWrites,[]);assert.deepEqual(originalOperationRecoveryWrites,[])
  phase='recovery-normal-dual-logout';const recoveryLogout=await diagnosedLogout('recovery-logout')
  await writeSignal(evidence,'browser.json',{revision,containerId:fixture.containerId,subject:profile.subject,recoverySubject,kind:fixture.kind,source,completed,keys,jobProofs,archives,archiveKeys,archiveJobProofs,mutations,ownerLogoutPosts,recoveryWrites,originalOperationRecoveryWrites,recoveryLogout,actualUi:true,...revocationUi})
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ actualArchiveRevocationBrowser: 'passed', purgeCases: 2, archiveCases: 2 }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_archive_revocation.mjs:'))?.trim()
  if (page && evidence && new URL(page.url()).origin==='http://127.0.0.1:8080' && new URL(page.url()).pathname!=='/login' && !['startup', 'visible-login', 'visible-relogin','visible-recovery-login'].includes(phase))
    await page.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ actualArchiveRevocationBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
