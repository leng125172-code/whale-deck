// Real installed Vue and visible Authentik login. No routes, Cookie injection or SQL edits.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
let phase='startup',browser,evidence,page
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const revision=process.argv[2],fixture=JSON.parse(await readFile('/dev/stdin','utf8'));assert.match(revision,/^[a-f0-9]{40}$/);assert.equal(fixture.revision,revision)
 assert.match(fixture.containerId,/^[a-f0-9]{64}$/);assert.match(fixture.containerName,/^wdv-container-publish-[a-f0-9]{32}$/);evidence=fixture.evidence
 const base=process.env.WHALEDECK_URL??'http://127.0.0.1:8080';assert.equal(base,'http://127.0.0.1:8080')
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true});const context=await browser.newContext({viewport:{width:1440,height:800},colorScheme:'light',reducedMotion:'reduce'});context.setDefaultTimeout(20_000);page=await context.newPage()
 assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
 phase='normal-visible-authentik-login';const profile=await loginVisible(page,context,base),endpoint=`/api/v1/containers/${fixture.containerId}/files/acls`
 const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json(),headers={Origin:base,[csrf.headerName]:csrf.token}
 const basic=[{tag:'user_obj',id:null,permissions:6},{tag:'group_obj',id:null,permissions:4},{tag:'other',id:null,permissions:0}]
 const forbidden={instanceId:fixture.instanceId,path:'work/acl-file.bin',aclType:'access',entries:basic,expectedVersion:fixture.sourceVersion}
 assert.equal((await context.request.post(base+endpoint,{headers:{Origin:base,'Idempotency-Key':crypto.randomUUID()},data:forbidden})).status(),400)
 assert.equal((await context.request.post(base+endpoint,{headers:{...headers,Origin:'http://example.invalid','Idempotency-Key':crypto.randomUUID()},data:forbidden})).status(),403)
 const missing=structuredClone(forbidden);delete missing.entries[0].permissions;const rejected=await context.request.post(base+endpoint,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:missing});assert.equal(rejected.status(),400);assert.equal((await rejected.json()).code,'CONTAINER_ACL_INPUT_INVALID')
 let registrations=0,confirmations=0,uploads=0;const headerChecks=[],completed=[]
 page.on('request',request=>{const path=new URL(request.url()).pathname;if(request.method()==='POST'&&(path===endpoint||path.startsWith(endpoint+'/')&&path.endsWith('/confirm'))){if(path===endpoint)registrations++;else confirmations++;headerChecks.push(request.allHeaders().then(h=>h.origin===base&&!!h['idempotency-key']))}if(request.method()==='PUT'&&path.includes('/files/'))uploads++})
 async function query(id){const r=await context.request.get(base+endpoint+'/'+id);assert.equal(r.status(),200);return r.json()}
 const cases=[{name:'access',mask:4},{name:'noop',mask:4},{name:'mask',mask:6},{name:'root',mask:6,id:0,permission:0},{name:'base',base:true},{name:'default',mask:4,directory:true},{name:'default-noop',mask:4,directory:true},{name:'clear-default',directory:true,clear:true},{name:'cancel',mask:7,cancel:true}]
 for(const [index,item] of cases.entries()){
  phase=item.name;await page.setViewportSize({width:index%2?320:1440,height:item.cancel?600:800});await page.emulateMedia({colorScheme:index%2?'dark':'light'})
  const files=await openPermissionFiles(page,fixture),name=item.directory?'acl-directory':'acl-file.bin';await files.getByRole('button',{name:'ACL '+name,exact:true}).click();const dialog=page.getByRole('dialog',{name:'修改容器文件或目录ACL',exact:true})
  async function select(label,value){await dialog.getByRole('combobox',{name:label,exact:true}).press('ArrowDown');await page.getByRole('listbox',{name:label,exact:true}).getByRole('option',{name:value,exact:true}).click()}
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx'];async function permissions(n,value){await select(`条目${n} 权限`,`${value} · ${bits[value]}`)}
  if(item.directory)await dialog.getByText('default 默认继承',{exact:true}).click()
  if(item.clear)await dialog.getByText('明确清空默认ACL（不修改现有子项）',{exact:true}).click()
  else{
   await permissions(1,6);await permissions(2,item.base?4:0);await permissions(3,0)
   if(!item.base){await dialog.getByRole('button',{name:'添加ACL条目',exact:true}).click();await dialog.getByLabel('条目4 数值ID',{exact:true}).fill(String(item.id??2201));await permissions(4,item.permission??6);await select('新增条目类型','mask');await dialog.getByRole('button',{name:'添加ACL条目',exact:true}).click();await permissions(5,item.mask)}
  }
  const registering=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST');await dialog.getByRole('button',{name:'生成ACL修改预检',exact:true}).click();const response=await registering;assert.equal(response.status(),202)
  const registered=await response.json(),key=(await response.request().allHeaders())['idempotency-key'],body=response.request().postDataJSON();assert.equal(registered.executionJobId,null)
  await expect(dialog.getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible({timeout:45_000});const ready=await query(registered.id);assert.equal(ready.state,'Ready');assert.ok(ready.confirmation);assert.equal(ready.noChange,['noop','default-noop'].includes(item.name));assert.equal(ready.executorUid,1000);assert.equal(ready.executorGid,1000)
  const duplicate=await context.request.post(base+endpoint,{headers:{...headers,'Idempotency-Key':key},data:body});assert.equal(duplicate.status(),202);assert.equal((await duplicate.json()).id,registered.id)
  await page.reload();await expect(page).toHaveURL(base+'/containers');await openPermissionFiles(page,fixture);await page.getByRole('button',{name:'查看ACL修改任务',exact:true}).click();await expect(dialog.getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible()
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth),true);await expect(dialog.getByRole('button',{name:'确认ACL修改',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'请求取消ACL修改',exact:true})).toBeInViewport({ratio:1});await page.screenshot({path:join(evidence,item.name+'.png')})
  let result
  if(item.cancel){await dialog.getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal((await query(ready.id)).state,'Ready');await dialog.getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:30_000});result=await query(ready.id);assert.equal(result.executionJobId,null);assert.equal(result.receipt,null)}
  else{await dialog.getByRole('button',{name:'确认ACL修改',exact:true}).click();await expect(dialog.getByText('ACL修改已完成',{exact:true})).toBeVisible({timeout:45_000});result=await query(ready.id);assert.equal(result.state,'Succeeded');assert.equal(result.receipt.state,'Completed');const retry=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{...headers,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}});assert.equal(retry.status(),202);assert.equal((await retry.json()).executionJobId,result.executionJobId)}
  assert.equal(result.confirmation,null);completed.push({name:item.name,...result})
  if(fixture.jobResultsVersion===1){
   async function job(id){const r=await context.request.get(base+'/api/v1/jobs/'+id);assert.equal(r.status(),200);return r.json()}
   await writeSignal(evidence,item.name+'-jobs.json',{schemaVersion:1,intentId:result.id,plan:await job(result.planningJobId),execution:result.executionJobId?await job(result.executionJobId):null})
  }
  await writeSignal(evidence,item.name+'-result.json',{subject:profile.subject,result:completed.at(-1)});const proof=await waitSignal(evidence,item.name+'-verified.json');assert.equal(proof.id,result.id);assert.equal(proof.physicalAndHistoryVerified,true);await closePermissionDetails(page,dialog)
 }
 let aclHistory=null
 if(fixture.aclHistoryVersion===1){phase='actual-acl-history';aclHistory=await(await import('./validate_container_acl_history_browser.mjs')).runHistoryBrowser(page,context,base,fixture,profile.subject,completed)}
 const taskCenter=[]
 if(fixture.jobResultsVersion===1){
  phase='actual-task-center-results';let jobWrites=0;page.on('request',r=>{const path=new URL(r.url()).pathname;if(path.startsWith('/api/v1/jobs')&&r.method()!=='GET')jobWrites++})
  for(const [index,item] of completed.entries())for(const execution of [false,true]){
   if(execution&&!item.executionJobId)continue
   const id=execution?item.executionJobId:item.planningJobId;await page.setViewportSize({width:index%2?320:1440,height:600});await page.emulateMedia({colorScheme:index%2?'dark':'light'})
   const listing=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs'&&new URL(r.url()).searchParams.get('take')==='100'&&r.request().method()==='GET');await page.goto(base+'/jobs');const response=await listing;assert.equal(response.status(),200);const jobs=await response.json(),position=jobs.findIndex(j=>j.id===id);assert.ok(position>=0)
   const row=page.locator('.jobs-page .el-table__body tr').nth(position);await row.getByRole('button',{name:'详情',exact:true}).click();const drawer=page.getByRole('dialog',{name:'任务详情',exact:true}),region=drawer.getByRole('region',{name:'ACL 安全结果',exact:true})
   await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(id);await expect(drawer.locator('.el-descriptions .el-tag')).toHaveText('Succeeded');await expect(region).toContainText(item.id)
   const title=execution?'ACL 修改已完成':'预检完成，尚未修改 ACL';await expect(region.getByText(title,{exact:true})).toBeVisible()
   async function field(label,value){await expect(region.getByText(label,{exact:true}).locator('xpath=following-sibling::dd[1]')).toHaveText(value)}
   await field('阶段',execution?'执行':'预检');await field('ACL 类型',item.command.aclType==='access'?'access 访问权限':'default 默认继承');await field('目标普通模式',item.targetMode.toString(8).padStart(4,'0'));await field('预检无需变更',item.noChange?'是':'否');await field('ACL 已确认',execution?'是':'未证明');await field('历史回执',execution?'Completed':'未取得');await field('历史已验证',execution?'是':'否');await field('已记录发布版本',execution?'是':'否')
   for(const secret of [fixture.containerId,fixture.instanceId,item.command.location.path,profile.subject,item.receipt?.fingerprint,item.receipt?.targetProof,item.receipt?.historyDigest,item.publishedVersion])if(secret)assert.ok(!(await region.innerText()).includes(secret))
   await expect(drawer.getByRole('button',{name:'取消任务',exact:true})).toHaveCount(0);await expect(drawer.getByRole('button',{name:'重新预检并重试',exact:true})).toHaveCount(0)
   await region.getByText(title,{exact:true}).scrollIntoViewIfNeeded();await expect(region.getByText(title,{exact:true})).toBeInViewport({ratio:1});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
   const name=item.name+'-'+(execution?'execution':'plan')+'-job.png';await page.screenshot({path:join(evidence,name)});taskCenter.push({id,intentId:item.id,execution,state:'Succeeded',summaryText:await region.innerText(),screenshot:name});await drawer.getByRole('button',{name:'Close this dialog',exact:true}).click();await expect(drawer).not.toBeVisible()
  }
  const legacy=fixture.legacyJob;assert.ok(legacy?.id&&legacy.result_json===null);const legacyResponse=await context.request.get(base+'/api/v1/jobs/'+legacy.id);assert.equal(legacyResponse.status(),200);assert.equal((await legacyResponse.json()).resultJson,null)
  const listing=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs'&&new URL(r.url()).searchParams.get('take')==='100'&&r.request().method()==='GET');await page.goto(base+'/jobs');const response=await listing,jobs=await response.json(),position=jobs.findIndex(j=>j.id===legacy.id);assert.ok(position>=0)
  await page.locator('.jobs-page .el-table__body tr').nth(position).getByRole('button',{name:'详情',exact:true}).click();const drawer=page.getByRole('dialog',{name:'任务详情',exact:true}),region=drawer.getByRole('region',{name:'ACL 安全结果',exact:true});await expect(drawer.locator('.el-descriptions__content').first()).toHaveText(legacy.id);await expect(region).toContainText('此任务未记录可展示的 ACL 安全摘要');await expect(drawer).not.toContainText('任务成功后');await region.scrollIntoViewIfNeeded();await page.screenshot({path:join(evidence,'legacy-job.png')});taskCenter.push({id:legacy.id,legacy:true,resultJson:null,screenshot:'legacy-job.png'});await drawer.getByRole('button',{name:'Close this dialog',exact:true}).click();assert.equal(jobWrites,0)
 }
 assert.equal(registrations,9);assert.equal(confirmations,8);assert.equal(uploads,0);assert.ok((await Promise.all(headerChecks)).every(Boolean));phase='visible-dual-logout';const logout=await logoutFromUserMenu(page,context,base)
 let historyOrdinary=null
 if(fixture.aclHistoryVersion===1){phase='ordinary-acl-history';historyOrdinary=await(await import('./validate_container_acl_history_browser.mjs')).ordinaryHistoryBrowser(browser,base,fixture,completed[0])}
 await writeFile(join(evidence,'browser.json'),JSON.stringify({revision,containerId:fixture.containerId,subject:profile.subject,completed,registrations,confirmations,registrationRetries:9,confirmationRetries:8,contentUploads:uploads,actualUi:true,normalDualLogout:true,logout,jobResultsVersion:fixture.jobResultsVersion,taskCenter,...(fixture.aclHistoryVersion===1?{aclHistory,historyOrdinary}:{})}),{mode:0o600});await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({aclBrowser:'passed',actualUi:true,cases:9}))
}catch(error){const frame=error?.stack?.split('\n').find(line=>line.includes('validate_container_acl_browser.mjs:'))?.trim();if(page&&evidence&&!['startup','normal-visible-authentik-login'].includes(phase))await page.screenshot({path:join(evidence,'failed-product-page.png')}).catch(()=>{});console.error(JSON.stringify({aclBrowser:'failed',phase,failureType:error?.name,frame}));if(browser)await browser.close().catch(()=>{});process.exitCode=1}
