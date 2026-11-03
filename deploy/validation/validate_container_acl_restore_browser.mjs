// Installed Authentik/Vue restoration only. Never intercept routes or inject a Cookie.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {openPermissionFiles,closePermissionDetails,writeSignal,waitSignal,loginVisible} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
const specs=[['restore-root','base',false,false],['restore-basic','access',false,false],['restore-noop','access',false,true],['restore-default','clear-default',false,false],['restore-clear','default',false,false],['restore-cancel','base',true,false]]
export async function runRestoreBrowser(page,context,base,fixture,subject,original) {
 const endpoint=`/api/v1/containers/${fixture.containerId}/files/acls`,restore=endpoint+'/restores',completed=[],writes=[]
 const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json(),headers={Origin:base,[csrf.headerName]:csrf.token}
 const record=request=>{const path=new URL(request.url()).pathname;if(path.startsWith(endpoint)&&request.method()!=='GET')writes.push({method:request.method(),path})}
 page.on('request',record)
 try {
  async function query(id){const r=await context.request.get(base+restore+'/'+id);assert.equal(r.status(),200);assert.ok((r.headers()['cache-control']??'').includes('no-store'));return r.json()}
  for(const [index,[name,capture,cancel,noop]] of specs.entries()) {
   const selected=original.find(v=>v.name===capture),captureId=selected.executionJobId.replaceAll('-',''),relative=selected.command.location.path.slice(5)
   await page.setViewportSize({width:index%2?1440:320,height:600});await page.emulateMedia({colorScheme:index%2?'light':'dark'})
   await page.goto(base+'/containers');const files=await openPermissionFiles(page,fixture),history=page.getByRole('dialog',{name:'ACL历史',exact:true})
   let listing=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/history'&&r.request().method()==='GET')
   await files.getByRole('button',{name:'ACL历史 '+relative,exact:true}).click()
   for(let pageIndex=0;pageIndex<100;pageIndex++) {
    const response=await listing;assert.equal(response.status(),200);const value=await response.json()
    if(value.entries.some(v=>v.captureId===captureId))break
    assert.equal(value.complete,false);assert.ok(pageIndex<99)
    listing=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/history'&&r.request().method()==='GET')
    await history.getByRole('button',{name:'继续扫描',exact:true}).click()
   }
   const reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/history/'+captureId&&r.request().method()==='GET')
   await history.getByRole('button',{name:'查看原ACL '+captureId,exact:true}).click();const read=await reading;assert.equal(read.status(),200);const source=await read.json()
   assert.equal(source.entry.historyDigest,selected.historyDigest);assert.deepEqual(source.accessAcl,selected.originalAccessAcl);assert.deepEqual(source.defaultAcl,selected.originalDefaultAcl)
   await history.getByRole('button',{name:'从此历史生成恢复预检',exact:true}).click();const dialog=page.getByRole('dialog',{name:'从历史恢复容器ACL',exact:true})
   await expect(dialog.getByRole('region',{name:'ACL恢复来源',exact:true})).toContainText(captureId)
   const registration=page.waitForResponse(r=>new URL(r.url()).pathname===restore&&r.request().method()==='POST')
   await dialog.getByRole('button',{name:'生成ACL恢复预检',exact:true}).click();const response=await registration;assert.equal(response.status(),202)
   assert.ok((response.headers()['cache-control']??'').includes('no-store'))
   const body=response.request().postDataJSON(),key=(await response.request().allHeaders())['idempotency-key'],registered=await response.json()
   assert.deepEqual(Object.keys(body).sort(),['instanceId','path','captureId','expectedHistoryDigest','expectedVersion'].sort())
   assert.equal(body.captureId,captureId);assert.equal(body.expectedHistoryDigest,source.entry.historyDigest);assert.deepEqual(registered.source,source);assert.equal(registered.intent.executionJobId,null)
   assert.equal(response.headers().location,restore+'/'+registered.intent.id)
   await expect(dialog.getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible({timeout:45_000})
   const ready=await query(registered.intent.id);assert.equal(ready.intent.state,'Ready');assert.equal(ready.intent.noChange,noop);assert.deepEqual(ready.source,source)
   const duplicate=await context.request.post(base+restore,{headers:{...headers,'Idempotency-Key':key},data:body});assert.equal(duplicate.status(),202);assert.equal((await duplicate.json()).intent.id,ready.intent.id)
   if(index===0){
    assert.equal((await context.request.post(base+restore,{headers:{Origin:base,'Idempotency-Key':crypto.randomUUID()},data:body})).status(),400)
    assert.equal((await context.request.post(base+restore,{headers:{...headers,Origin:'http://example.invalid','Idempotency-Key':crypto.randomUUID()},data:body})).status(),403)
    const bad={...body,entries:source.accessAcl};assert.equal((await context.request.post(base+restore,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:bad})).status(),400)
   }
   await page.reload();await openPermissionFiles(page,fixture);await page.getByRole('button',{name:'查看ACL修改任务',exact:true}).click()
   await expect(dialog.getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible();await expect(dialog.getByRole('region',{name:'ACL恢复来源',exact:true})).toContainText(source.entry.historyDigest)
   await dialog.getByRole('region',{name:'目标'+ready.intent.command.aclType+' ACL',exact:true}).scrollIntoViewIfNeeded()
   await expect(dialog.getByRole('button',{name:'确认ACL历史恢复',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'请求取消ACL修改',exact:true})).toBeInViewport({ratio:1})
   const hints=JSON.parse(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-acl-hints-v1')));assert.equal(hints.find(v=>v.id===ready.intent.id)?.restore,true)
   for(const value of [captureId,source.entry.historyDigest,'accessAcl','defaultAcl'])assert.ok(!JSON.stringify(hints).includes(value))
   await page.screenshot({path:join(fixture.evidence,name+'-ready.png')})
   if(cancel){await dialog.getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal((await query(ready.intent.id)).intent.state,'Ready');await dialog.getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:30_000})}
   else{await dialog.getByRole('button',{name:'确认ACL历史恢复',exact:true}).click();await expect(dialog.getByText('ACL修改已完成',{exact:true})).toBeVisible({timeout:45_000})}
   const value=await query(ready.intent.id),result={name,...value.intent};assert.deepEqual(value.source,source);assert.equal(result.state,cancel?'Canceled':'Succeeded');assert.equal(result.confirmation,null)
   if(!cancel){const replay=await context.request.post(base+endpoint+'/'+result.id+'/confirm',{headers:{...headers,'Idempotency-Key':result.id},data:{planHash:ready.intent.confirmation.planHash,confirmed:true,version:ready.intent.version}});assert.equal(replay.status(),202);assert.equal((await replay.json()).executionJobId,result.executionJobId);assert.notEqual(result.executionJobId.replaceAll('-',''),captureId)}
   async function job(id){const r=await context.request.get(base+'/api/v1/jobs/'+id);assert.equal(r.status(),200);return r.json()}
   await writeSignal(fixture.evidence,name+'-jobs.json',{schemaVersion:1,intentId:result.id,plan:await job(result.planningJobId),execution:result.executionJobId?await job(result.executionJobId):null})
   await page.screenshot({path:join(fixture.evidence,name+'-result.png')});completed.push({source,result,key})
   await writeSignal(fixture.evidence,name+'-result.json',{subject,source,result,key})
   const proof=await waitSignal(fixture.evidence,name+'-verified.json');assert.equal(proof.id,result.id);assert.equal(proof.selectedSourceAndNewCurrentAclBackupVerified,true)
   await closePermissionDetails(page,dialog)
  }
  assert.equal(writes.filter(v=>v.path===restore).length,6);assert.equal(writes.filter(v=>v.path.endsWith('/confirm')).length,5);assert.equal(writes.filter(v=>v.path.endsWith('/cancel')).length,1);assert.ok(writes.every(v=>v.method==='POST'))
  return {version:1,completed,writes,registrationRetries:6,confirmationRetries:5,realUi:true}
 }finally{page.off('request',record)}
}

export async function ordinaryRestoreBrowser(browser,base,fixture,example) {
 const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'}),page=await context.newPage()
 try {
  const profile=await loginVisible(page,context,base,'/',false,{username:process.env.HISTORY_ORDINARY_USERNAME,password:process.env.HISTORY_ORDINARY_PASSWORD})
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/acls`,restore=endpoint+'/restores',item=example.result,source=example.source
  const urls=[base+restore+'/'+item.id,base+restore+'/by-key/'+example.key,base+endpoint+'/history/'+source.entry.captureId+'?'+new URLSearchParams({instanceId:fixture.instanceId,path:source.location.path,expectedHistoryDigest:source.entry.historyDigest})]
  const denied=[];for(const url of urls)denied.push((await context.request.get(url)).status())
  const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json(),headers={Origin:base,[csrf.headerName]:csrf.token,'Idempotency-Key':item.id}
  denied.push((await context.request.post(base+restore,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:{instanceId:fixture.instanceId,path:source.location.path,captureId:source.entry.captureId,expectedHistoryDigest:source.entry.historyDigest,expectedVersion:item.command.expectedVersion}})).status())
  denied.push((await context.request.post(base+endpoint+'/'+item.id+'/confirm',{headers,data:{planHash:'not-authority',version:item.version,confirmed:true}})).status())
  denied.push((await context.request.post(base+endpoint+'/'+item.id+'/cancel',{headers})).status());assert.deepEqual(denied,Array(6).fill(403))
  let uiRestoreRequests=0;page.on('request',r=>{if(new URL(r.url()).pathname.startsWith(endpoint))uiRestoreRequests++})
  await page.goto(base+'/containers');await expect(page).toHaveURL(base+'/forbidden');await expect(page.getByRole('button',{name:'从此历史生成恢复预检',exact:true})).toHaveCount(0);assert.equal(uiRestoreRequests,0)
  await page.screenshot({path:join(fixture.evidence,'restore-ordinary.png')});await page.goto(base+'/');const logout=await logoutFromUserMenu(page,context,base),anonymous=[]
  for(const url of urls)anonymous.push((await context.request.get(url)).status());assert.deepEqual(anonymous,[401,401,401])
  const result={subject:profile.subject,denied,anonymous,uiRestoreRequests,normalDualLogout:logout};await writeSignal(fixture.evidence,'restore-ordinary-result.json',result)
  assert.equal((await waitSignal(fixture.evidence,'restore-ordinary-verified.json')).ordinaryAndAnonymousDenied,true);return result
 }finally{await context.close()}
}
