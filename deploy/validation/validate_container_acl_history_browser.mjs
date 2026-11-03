// Real installed UI only: no route interception, Cookie injection or synthetic histories.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {openPermissionFiles,closePermissionDetails,writeSignal,waitSignal,loginVisible} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'

function proof(response) {
 const request=response.request(),url=new URL(response.url())
 return {method:request.method(),pathname:url.pathname,query:Object.fromEntries(url.searchParams),status:response.status(),noStore:(response.headers()['cache-control']??'').includes('no-store')}
}
export async function runHistoryBrowser(page,context,base,fixture,subject,completed) {
 const pages=[],snapshots=[],requests=[]
 const endpoint=`/api/v1/containers/${fixture.containerId}/files/acls/history`
 const record=request=>{if(new URL(request.url()).pathname.startsWith(endpoint))requests.push({method:request.method(),path:new URL(request.url()).pathname})}
 page.on('request',record)
 try {
  for(const relative of ['acl-file.bin','acl-directory']) {
   const path='work/'+relative,items=completed.filter(v=>v.executionJobId&&v.command.location.path===path)
   assert.equal(items.length,relative==='acl-file.bin'?5:3)
   await page.setViewportSize({width:relative==='acl-file.bin'?320:1440,height:600});await page.emulateMedia({colorScheme:relative==='acl-file.bin'?'dark':'light'})
   await page.goto(base+'/containers');const files=await openPermissionFiles(page,fixture)
   const dialog=page.getByRole('dialog',{name:'ACL历史',exact:true})
   let response=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
   await files.getByRole('button',{name:'ACL历史 '+relative,exact:true}).click()
   const listing=[]
   for(let count=0;count<100;count++) {
    const r=await response;assert.equal(r.status(),200);const value=await r.json();assert.equal(value.location.path,path)
    pages.push({...proof(r),page:value});listing.push(...value.entries)
    if(value.complete)break
    assert.ok(count<99);response=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
    await dialog.getByRole('button',{name:'继续扫描',exact:true}).click()
   }
   assert.deepEqual(listing.map(v=>v.captureId).sort(),items.map(v=>v.executionJobId.replaceAll('-','')).sort())
   for(const item of items) {
    const captureId=item.executionJobId.replaceAll('-',''),reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+captureId&&r.request().method()==='GET')
    await dialog.getByRole('button',{name:'查看原ACL '+captureId,exact:true}).click();const r=await reading;assert.equal(r.status(),200);const value=await r.json()
    assert.equal(value.entry.captureId,captureId);assert.equal(value.entry.historyDigest,item.historyDigest)
    assert.deepEqual(value.accessAcl,item.originalAccessAcl);assert.deepEqual(value.defaultAcl,item.originalDefaultAcl)
    const region=dialog.getByRole('region',{name:'ACL历史快照',exact:true});await expect(region).toContainText(item.historyDigest)
    if(item.name==='base')await expect(region.getByText('指定用户 0',{exact:true})).toBeVisible()
    await dialog.getByRole('region',{name:'历史default ACL',exact:true}).scrollIntoViewIfNeeded()
    await expect(dialog.getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1})
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
    const screenshot='history-'+item.name+'.png';await page.screenshot({path:join(fixture.evidence,screenshot)})
    const result={subject,name:item.name,captureId,snapshot:value,request:proof(r),pages:[...pages],screenshot}
    snapshots.push(value);await writeSignal(fixture.evidence,'history-'+item.name+'-result.json',result)
    const verified=await waitSignal(fixture.evidence,'history-'+item.name+'-verified.json');assert.equal(verified.captureId,captureId);assert.equal(verified.physicalRootHddAndAuditVerified,true)
    await dialog.getByRole('button',{name:'返回历史列表',exact:true}).click()
   }
   await closePermissionDetails(page,dialog)
  }
  assert.equal(snapshots.length,8);assert.ok(requests.every(v=>v.method==='GET'));assert.equal(requests.length,pages.length+8)
  return {version:1,reads:8,pages,requests,mutations:0}
 } finally {page.off('request',record)}
}

export async function ordinaryHistoryBrowser(browser,base,fixture,example) {
 const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'}),page=await context.newPage()
 try {
  const profile=await loginVisible(page,context,base,'/',false,{username:process.env.HISTORY_ORDINARY_USERNAME,password:process.env.HISTORY_ORDINARY_PASSWORD})
  const query=new URLSearchParams({instanceId:fixture.instanceId,path:example.command.location.path}),endpoint=`/api/v1/containers/${fixture.containerId}/files/acls/history`
  const urls=[base+endpoint+'?'+query,base+endpoint+'/'+example.executionJobId.replaceAll('-','')+'?'+new URLSearchParams({...Object.fromEntries(query),expectedHistoryDigest:example.historyDigest})]
  const denied=[];for(const url of urls)denied.push((await context.request.get(url)).status());assert.deepEqual(denied,[403,403])
  let pageRequests=0;page.on('request',r=>{if(new URL(r.url()).pathname.startsWith(endpoint))pageRequests++})
  await page.goto(base+'/containers');await expect(page).toHaveURL(base+'/forbidden');await expect(page.getByRole('button',{name:/^ACL历史 /})).toHaveCount(0);assert.equal(pageRequests,0)
  await page.screenshot({path:join(fixture.evidence,'history-ordinary.png')})
  const logout=await logoutFromUserMenu(page,context,base),anonymous=[]
  for(const url of urls)anonymous.push((await context.request.get(url)).status());assert.deepEqual(anonymous,[401,401])
  const result={subject:profile.subject,denied,anonymous,normalDualLogout:logout,uiHistoryRequests:0}
  await writeSignal(fixture.evidence,'history-ordinary-result.json',result)
  const verified=await waitSignal(fixture.evidence,'history-ordinary-verified.json');assert.equal(verified.ordinaryAndAnonymousDenied,true)
  return result
 } finally {await context.close()}
}
