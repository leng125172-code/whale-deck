// Installed UI only: normal Authentik credentials, real HTTP, no mocked response/session injection.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, evidence, activePage
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/); assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.hostname, '127.0.0.1'); assert.equal(origin.port, '8080')
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage()
  activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-visible-authentik-login'
  await page.goto(base + '/login?returnUrl=/containers')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === '/containers') break
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
  assert.ok(identified && entered); await expect(page).toHaveURL(base + '/containers')
  const profile = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(profile.isAdministrator, true)
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/recycles`
  const csrf=await (await context.request.get(base+'/api/v1/auth/csrf')).json();const headers={Origin:origin.origin,[csrf.headerName]:csrf.token}
  phase='csrf-origin-refusal'
  const command={instanceId:fixture.instanceId,path:'work/recycle-rpc.bin',expectedVersion:fixture.sourceVersion}
  for(const variant of ['csrf','origin']) {
    const denied=await context.request.post(base+endpoint,{headers:variant==='csrf'?{Origin:origin.origin,'Idempotency-Key':crypto.randomUUID()}:{...headers,Origin:'http://unapproved.invalid','Idempotency-Key':crypto.randomUUID()},data:command})
    assert.ok([400,403].includes(denied.status()))
  }
  let registrations=0,confirmations=0,cancellations=0,uploads=0;const headerChecks=[];const completed=[]
  page.on('request',r=>{const path=new URL(r.url()).pathname;if(r.method()==='POST'&&path===endpoint)registrations++;if(r.method()==='POST'&&path.startsWith(endpoint)&&path.endsWith('/confirm'))confirmations++;if(r.method()==='POST'&&path.startsWith(endpoint)&&path.endsWith('/cancel'))cancellations++;if(r.method()==='PUT'&&path.includes('/files/'))uploads++;if(r.method()==='POST'&&path.startsWith(endpoint))headerChecks.push(r.allHeaders().then(h=>h.origin===origin.origin&&!!h['idempotency-key']))})
  async function openFiles(){await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName);const card=page.locator('.container-card').filter({hasText:fixture.containerName});await expect(card).toHaveCount(1);await card.getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();const f=page.getByRole('region',{name:'容器文件',exact:true});await f.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work');await f.getByRole('button',{name:'前往',exact:true}).click();await expect(f.getByText('recycle-rpc.bin',{exact:true})).toBeVisible();return f}
  async function query(id){const response=await context.request.get(base+endpoint+'/'+id);assert.equal(response.status(),200);return response.json()}
  for(const item of [{name:'cancel',cancel:true,theme:'light'},{name:'trash',cancel:false,theme:'dark'}]){
    phase=item.name;await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme:item.theme});let f=await openFiles();await f.getByRole('button',{name:'回收 recycle-rpc.bin',exact:true}).click();const dialog=page.getByRole('dialog',{name:'回收容器普通文件',exact:true})
    const registration=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST');await dialog.getByRole('button',{name:'生成文件回收预检',exact:true}).click();const response=await registration;assert.equal(response.status(),202);const initial=await response.json();assert.equal(initial.state,'Queued');assert.equal(initial.executionJobId,null)
    await expect(dialog.getByText('预检完成，尚未移除文件',{exact:true})).toBeVisible({timeout:60000});const ready=await query(initial.id);assert.equal(ready.state,'Ready');assert.equal(ready.sizeBytes,fixture.original.sizeBytes);assert.equal(ready.sha256,fixture.original.sha256);assert.equal(ready.confirmation!==null,true)
    const key=(await response.request().allHeaders())['idempotency-key'];const duplicate=await context.request.post(base+endpoint,{headers:{...headers,'Idempotency-Key':key},data:response.request().postDataJSON()});assert.equal(duplicate.status(),202);assert.equal((await duplicate.json()).id,ready.id)
    const otherKey=await context.request.get(base+endpoint+'/by-key/'+key);assert.equal(otherKey.status(),200);assert.equal((await otherKey.json()).id,ready.id)
    await page.reload();await expect(page).toHaveURL(base+'/containers');await openFiles();await page.getByRole('button',{name:'查看文件回收任务',exact:true}).click();await expect(dialog.getByText('预检完成，尚未移除文件',{exact:true})).toBeVisible({timeout:30000});await expect(dialog.getByRole('button',{name:'请求取消文件回收',exact:true})).toBeInViewport({ratio:1});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:join(evidence,item.name+'-ready.png')})
    if(item.cancel){await dialog.getByRole('button',{name:'请求取消文件回收',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal((await query(ready.id)).state,'Ready');await dialog.getByRole('button',{name:'请求取消文件回收',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:30000})}
    else {await dialog.getByRole('button',{name:'确认文件回收',exact:true}).click();await expect(dialog.getByText('文件回收已完成',{exact:true})).toBeVisible({timeout:60000});const value=await query(ready.id);assert.equal(value.state,'Succeeded');const retry=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{...headers,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}});assert.equal(retry.status(),202);assert.equal((await retry.json()).executionJobId,value.executionJobId)}
    const result=await query(ready.id);assert.equal(result.confirmation,null);completed.push({name:item.name,...result});if(item.cancel){await writeFile(join(evidence,'cancel-completed.json'),JSON.stringify({subject:profile.subject,result:{name:item.name,...result}}),{mode:0o600});const deadline=Date.now()+45000;while(true){try{assert.equal(JSON.parse(await readFile(join(evidence,'cancel-proof-accepted.json'),'utf8')).id,result.id);break}catch(error){if(error.code!=='ENOENT')throw error}assert.ok(Date.now()<deadline);await page.waitForTimeout(200)}}await page.screenshot({path:join(evidence,item.name+'-result.png')});await dialog.getByRole('button',{name:'关闭',exact:true}).click();await page.getByRole('dialog',{name:new RegExp('容器详情')}).locator('.el-drawer__close-btn').click()
  }
  phase='job-summary'
  const listing=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs'&&r.request().method()==='GET');await page.goto(base+'/jobs');const listed=await (await listing).json();const result=completed.find(v=>v.name==='trash');
  const job=await context.request.get(base+'/api/v1/jobs/'+result.executionJobId);assert.equal(job.status(),200);const jobValue=await job.json();const safe=JSON.parse(jobValue.resultJson);assert.equal(safe.state,'Succeeded');assert.equal(safe.receiptState,'Trashed');assert.equal(safe.sourceRetained,true)
  // Select the exact original execution from the actual task list, never synthesize a session or response.
  const index=listed.findIndex(v=>v.id===result.executionJobId);assert.ok(index>=0);const jobRow=page.locator('.jobs-page .el-table__body tr').nth(index);await jobRow.getByRole('button',{name:'详情',exact:true}).click();const detail=page.getByRole('dialog',{name:'任务详情',exact:true});await expect(detail).toContainText(result.executionJobId);await expect(detail.getByRole('region',{name:'回收安全结果',exact:true})).toContainText('文件回收已完成');await page.screenshot({path:join(evidence,'actual-job.png')});await detail.getByRole('button',{name:'Close this dialog',exact:true}).click()
  assert.equal(registrations,2);assert.equal(confirmations,1);assert.equal(cancellations,1);assert.equal(uploads,0);assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase='visible-dual-logout';await logoutFromUserMenu(page,context,base)
  await writeFile(join(evidence,'browser.json'),JSON.stringify({revision,containerId:fixture.containerId,subject:profile.subject,completed,registrations,confirmations,cancellations,contentUploads:uploads,normalDualLogout:true,actualUi:true,job:jobValue}),{mode:0o600});await context.close();await browser.close();browser=undefined;phase='done';console.log(JSON.stringify({recycleBrowser:'passed',cases:2}))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_browser.mjs:'))?.trim()
  // Only an already authenticated product page may be captured, never login/credentials.
  if (activePage && evidence && !['startup', 'normal-visible-authentik-login'].includes(phase))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ recycleBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
