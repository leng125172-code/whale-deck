// Actual installed Authentik/Vue; never route fixtures, inject cookies or confirm execution.
import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
let phase='startup',browser,page,evidence
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const revision=process.argv[2],fixture=JSON.parse(await readFile('/dev/stdin','utf8'));assert.match(revision,/^[a-f0-9]{40}$/);assert.equal(fixture.revision,revision)
 assert.match(fixture.containerId,/^[a-f0-9]{64}$/);assert.match(fixture.containerName,/^wdv-container-publish-[a-f0-9]{32}$/);evidence=fixture.evidence
 const base='http://127.0.0.1:8080';assert.equal(process.env.WHALEDECK_URL??base,base)
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true});const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(20_000);page=await context.newPage()
 const status=await context.request.get(base+'/api/v1/system/status');assert.equal(status.status(),200);assert.equal((await status.json()).build.sourceRevision,revision)
 phase='normal-visible-login';const profile=await loginVisible(page,context,base),endpoint=`/api/v1/containers/${fixture.containerId}/files/acls`
 let registrations=0,confirmations=0,contentWrites=0,jobWrites=0
 page.on('request',r=>{const path=new URL(r.url()).pathname;if(r.method()==='POST'&&path===endpoint)registrations++;if(r.method()==='POST'&&path.startsWith(endpoint+'/')&&path.endsWith('/confirm'))confirmations++;if(['POST','PUT','PATCH','DELETE'].includes(r.method())&&path.includes('/files/')&&!path.includes('/acls'))contentWrites++;if(path.startsWith('/api/v1/jobs')&&r.method()!=='GET')jobWrites++})
 const files=await openPermissionFiles(page,fixture);await files.getByRole('button',{name:'ACL acl-file.bin',exact:true}).click();const dialog=page.getByRole('dialog',{name:'修改容器文件或目录ACL',exact:true}),generate=dialog.getByRole('button',{name:'生成ACL修改预检',exact:true})
 await expect(generate).toBeEnabled();await writeSignal(evidence,'opened.json',{subject:profile.subject,originalPageOpened:true});const changed=await waitSignal(evidence,'changed.json');assert.equal(changed.originalVersionStale,true)
 phase='submit-original-stale-version';const registered=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===endpoint);await generate.click();const response=await registered;assert.equal(response.status(),202);const accepted=await response.json(),body=response.request().postDataJSON(),headers=await response.request().allHeaders();assert.equal(body.expectedVersion,fixture.sourceVersion);assert.equal(accepted.executionJobId,null);assert.equal(headers.origin,base);assert.ok(headers['idempotency-key'])
 await expect(dialog.getByText('ACL修改未完成',{exact:true})).toBeVisible({timeout:45_000});const observed=await context.request.get(base+endpoint+'/'+accepted.id);assert.equal(observed.status(),200);const intent=await observed.json();assert.equal(intent.state,'Failed');assert.equal(intent.resultCode,'CONTAINER_ACL_VERSION_CONFLICT');assert.equal(intent.executionJobId,null);assert.equal(intent.confirmation,null);assert.equal(intent.receipt,null)
 await expect(dialog.getByRole('button',{name:'确认ACL修改',exact:true})).toHaveCount(0);await expect(dialog.getByRole('button',{name:'请求取消ACL修改',exact:true})).toHaveCount(0);await page.screenshot({path:join(evidence,'failed-preflight-mobile-dark.png')})
 const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json();const duplicate=await context.request.post(base+endpoint,{headers:{Origin:base,[csrf.headerName]:csrf.token,'Idempotency-Key':headers['idempotency-key']},data:body});assert.equal(duplicate.status(),202);const retried=await duplicate.json();assert.equal(retried.id,intent.id);assert.equal(retried.executionJobId,null);assert.equal(retried.state,'Failed')
 const jobResponse=await context.request.get(base+'/api/v1/jobs/'+intent.planningJobId);assert.equal(jobResponse.status(),200);const job=await jobResponse.json();assert.equal(job.state,'Failed');assert.equal(job.errorCode,'CONTAINER_ACL_VERSION_CONFLICT')
 await writeSignal(evidence,'result.json',{intent,job});const verified=await waitSignal(evidence,'verified-row.json');assert.equal(verified.id,intent.id);assert.equal(verified.noExecutionOrHistory,true)
 phase='real-task-center-rejected-planning';const taskCenter=[]
 for(const [width,colorScheme] of [[320,'dark'],[1440,'light']]){
  await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme});const listing=page.waitForResponse(r=>r.request().method()==='GET'&&new URL(r.url()).pathname==='/api/v1/jobs');await page.goto(base+'/jobs');const listResponse=await listing;assert.equal(listResponse.status(),200);const jobs=await listResponse.json(),position=jobs.findIndex(j=>j.id===intent.planningJobId);assert.ok(position>=0)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click();const drawer=page.getByRole('dialog',{name:'任务详情',exact:true}),region=drawer.getByRole('region',{name:'ACL 安全结果',exact:true}),title=region.getByText('预检失败，未进入 ACL 修改执行',{exact:true})
  await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(intent.planningJobId);await expect(drawer.locator('.el-descriptions .el-tag')).toHaveText('Failed');await expect(title).toBeVisible();await expect(region).not.toContainText('执行失败')
  async function field(label,value){const node=region.getByText(label,{exact:true}).locator('xpath=following-sibling::dd[1]');await expect(node).toHaveText(value);return node}
  await field('阶段','预检');for(const label of ['目标对象','目标普通模式','预检无需变更','ACL 已确认','回执对象绑定有效'])await field(label,'未证明');await field('历史回执','未取得');for(const label of ['可能已有历史','历史已验证','已记录发布版本'])await field(label,'否');await field('结果码','CONTAINER_ACL_VERSION_CONFLICT')
  for(const value of [fixture.containerId,fixture.instanceId,fixture.sourceVersion,profile.subject,'work/acl-file.bin'])assert.ok(!(await region.innerText()).includes(value))
  await expect(drawer.getByRole('button',{name:'取消任务',exact:true})).toHaveCount(0);await expect(drawer.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
  await title.scrollIntoViewIfNeeded();await expect(title).toBeInViewport({ratio:1});const titleScreenshot=`failed-plan-title-${width}-${colorScheme}.png`;await page.screenshot({path:join(evidence,titleScreenshot)})
  const confirmation=await field('ACL 已确认','未证明');await confirmation.evaluate(el=>el.scrollIntoView({block:'center',inline:'nearest'}));await expect(confirmation).toBeInViewport({ratio:1});const box=await drawer.locator('.el-drawer__body').boundingBox();assert.ok(box)
  for(const b of await region.locator('dd').evaluateAll(elements=>elements.map(el=>{const b=el.getBoundingClientRect();return {left:b.left,right:b.right}})))assert.ok(b.left>=box.x&&b.right<=box.x+box.width)
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth));const factsScreenshot=`failed-plan-facts-${width}-${colorScheme}.png`;await page.screenshot({path:join(evidence,factsScreenshot)})
  taskCenter.push({id:intent.planningJobId,width,colorScheme,titleScreenshot,factsScreenshot,missingConfirmationVisible:true,noGenericWriteControls:true});await drawer.getByRole('button',{name:'Close this dialog',exact:true}).click()
 }
 assert.equal(registrations,1);assert.equal(confirmations,0);assert.equal(contentWrites,0);assert.equal(jobWrites,0);phase='normal-original-dual-logout';const logout=await logoutFromUserMenu(page,context,base)
 await writeSignal(evidence,'browser.json',{revision,subject:profile.subject,id:intent.id,actualUi:true,normalDualLogout:true,logout,registrations,registrationRetries:1,confirmations,contentWrites,jobWrites,taskCenter});await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({aclStaleBrowser:'passed',actualUi:true}))
}catch(error){const frame=error?.stack?.split('\n').find(line=>line.includes('validate_container_acl_stale.mjs:'))?.trim();if(page&&evidence&&!['startup','normal-visible-login'].includes(phase))await page.screenshot({path:join(evidence,'failed-product-page.png')}).catch(()=>{});console.error(JSON.stringify({aclStaleBrowser:'failed',phase,failureType:error?.name,frame}));if(browser)await browser.close().catch(()=>{});process.exitCode=1}
