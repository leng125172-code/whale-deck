// Actual installed Vue controls only; login/logout and secrets stay with the caller.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
const specs=[['restore-access','access',4],['restore-noop','access',0],['restore-default','clear-default',2],['restore-clear','default',2],['restore-cancel','root',4]]
const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx'],labels={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
function httpProof(response){const url=new URL(response.url());assert.equal(new Set(url.searchParams.keys()).size,[...url.searchParams.keys()].length);return {method:response.request().method(),pathname:url.pathname,query:Object.fromEntries(url.searchParams),status:response.status(),noStore:(response.headers()['cache-control']??'').includes('no-store')}}
export function restoreUrls(base,fixture,summary){const endpoint=`${base}/api/v1/containers/${fixture.containerId}/files/recursive-acl-restores`,id=summary.completed[0].id;return [endpoint+'/'+id,endpoint+'/by-key/'+crypto.randomUUID(),base+'/api/v1/files/container-recursive-acl-restores/recovery',base+'/api/v1/files/container-recursive-acl-restores/recovery/'+id]}
export async function restoreDenials(context,urls){const results=[];for(const path of urls)results.push((await context.request.get(path)).status());return results}
export async function ordinaryRestorePosts(context,base,fixture,summary,headers){
 const endpoint=`${base}/api/v1/containers/${fixture.containerId}/files/recursive-acl-restores`,row=summary.completed[0],data=summary.bodies[0],values=[]
 for(const path of [endpoint,endpoint+'/'+row.id+'/confirm',endpoint+'/'+row.id+'/cancel',`${base}/api/v1/files/container-recursive-acl-restores/recovery/${row.id}/acknowledge`])values.push((await context.request.post(path,{headers:{...headers,'Idempotency-Key':row.id},data:path===endpoint?data:{version:row.version,confirmed:true,planHash:'not-authority'}})).status())
 return values
}
export async function runRecursiveRestoreBrowser(page,context,base,fixture,subject,completed,headers){
 const endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acl-restores`,history=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`
 const counts={registrations:0,confirmations:0,cancellations:0,uploads:0},headerChecks=[],results=[],bodies=[]
 const watch=request=>{const path=new URL(request.url()).pathname;if(request.method()==='POST'&&(path===endpoint||path.startsWith(endpoint+'/'))){if(path===endpoint)counts.registrations++;else if(path.endsWith('/confirm'))counts.confirmations++;else if(path.endsWith('/cancel'))counts.cancellations++;headerChecks.push(request.allHeaders().then(h=>h.origin===base&&!!h['idempotency-key']))}if(request.method()==='PUT'&&path.includes('/files/'))counts.uploads++}
 page.on('request',watch)
 const first=completed.find(v=>v.name==='access'),invalidBody={instanceId:fixture.instanceId,path:'work/recursive-tree',captureId:first.executionJobId.replaceAll('-',''),expectedHistoryDigest:first.historyDigest,expectedVersion:fixture.sourceVersion}
 const noCsrf=await context.request.post(base+endpoint,{headers:{Origin:base,'Idempotency-Key':crypto.randomUUID()},data:invalidBody});assert.equal(noCsrf.status(),400)
 const badOrigin=await context.request.post(base+endpoint,{headers:{...headers,Origin:'http://example.invalid','Idempotency-Key':crypto.randomUUID()},data:invalidBody});assert.equal(badOrigin.status(),403)
 const missing={...invalidBody};delete missing.expectedHistoryDigest
 const malformed=await context.request.post(base+endpoint,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:missing});assert.equal(malformed.status(),400);assert.equal((await malformed.json()).code,'CONTAINER_RECURSIVE_ACL_RESTORE_INPUT_INVALID')
 const securityChecks=[noCsrf.status(),badOrigin.status(),malformed.status()]
 async function query(id){const r=await context.request.get(base+endpoint+'/'+id);assert.equal(r.status(),200);assert.ok((r.headers()['cache-control']??'').includes('no-store'));return r.json()}
 try{
  for(const [index,[name,sourceName,changes]]of specs.entries()){
   await page.setViewportSize({width:index%2?1440:320,height:600});await page.emulateMedia({colorScheme:index%2?'light':'dark'})
   const files=await openPermissionFiles(page,fixture),source=completed.find(v=>v.name===sourceName),captureId=source.executionJobId.replaceAll('-','')
   const hist=page.getByRole('dialog',{name:'递归ACL历史',exact:true})
   let loading=page.waitForResponse(r=>new URL(r.url()).pathname===history&&r.request().method()==='GET')
   await files.getByRole('button',{name:'递归ACL历史 recursive-tree',exact:true}).click()
   let selected=false
   for(let n=0;n<100;n++){
    const r=await loading;assert.equal(r.status(),200);const listing=await r.json()
    if(listing.entries.some(e=>e.captureId===captureId)){selected=true;break}
    assert.equal(listing.complete,false);loading=page.waitForResponse(r=>new URL(r.url()).pathname===history&&r.request().method()==='GET');await hist.getByRole('button',{name:'继续扫描',exact:true}).click()
   }
   assert.equal(selected,true)
   const reading=page.waitForResponse(r=>new URL(r.url()).pathname===history+'/'+captureId&&r.request().method()==='GET')
   await hist.getByRole('button',{name:'查看逐项前像 '+captureId,exact:true}).click();const read=await reading,snapshot=await read.json();assert.equal(read.status(),200);assert.equal(snapshot.complete,true);assert.equal(snapshot.entries.length,4)
   await hist.getByRole('button',{name:'预检恢复此递归ACL历史',exact:true}).click()
   const dialog=page.getByRole('dialog',{name:'递归恢复容器目录历史ACL',exact:true});await expect(dialog).toBeVisible()
   const registration=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST')
   await dialog.getByRole('button',{name:'生成递归ACL恢复预检',exact:true}).click();const registeredResponse=await registration;assert.equal(registeredResponse.status(),202)
   const registered=await registeredResponse.json(),key=(await registeredResponse.request().allHeaders())['idempotency-key'],body=registeredResponse.request().postDataJSON()
   assert.equal(registered.state,'Queued');assert.equal(registered.executionJobId,null)
   await expect(dialog.getByText('预检已完成，尚未恢复ACL',{exact:true})).toBeVisible({timeout:45000})
   const ready=await query(registered.id);assert.equal(ready.state,'Ready');assert.ok(ready.confirmation);assert.deepEqual([ready.entryCount,ready.fileCount,ready.directoryCount,ready.changeCount],[4,2,2,changes]);assert.deepEqual(ready.restoreSource,snapshot.summary)
   const duplicate=await context.request.post(base+endpoint,{headers:{...headers,'Idempotency-Key':key},data:body});assert.equal(duplicate.status(),202);assert.equal((await duplicate.json()).id,ready.id)
   const originalReference={subject,containerId:fixture.containerId,key,id:ready.id}
   const reference=async()=>{const values=JSON.parse(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-restore-hints-v1')));assert.ok(Array.isArray(values));const current=values.find(v=>v.containerId===fixture.containerId);assert.deepEqual(current,originalReference);assert.deepEqual(Object.keys(current).sort(),['containerId','id','key','subject']);return current}
   await reference();await page.reload();await openPermissionFiles(page,fixture);await reference();await page.getByRole('button',{name:'查看递归ACL恢复任务',exact:true}).click();await expect(dialog.getByText('预检已完成，尚未恢复ACL',{exact:true})).toBeVisible({timeout:30000})
   await expect(dialog.getByRole('button',{name:'确认递归ACL恢复',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true})).toBeInViewport({ratio:1});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
   await page.screenshot({path:join(fixture.evidence,name+'-ready.png')})
   const previewRead=page.waitForResponse(r=>new URL(r.url()).pathname===history+'/'+captureId&&r.request().method()==='GET')
   await dialog.getByRole('button',{name:'查看逐项恢复目标ACL',exact:true}).click();const previewResponse=await previewRead,preview=await previewResponse.json();assert.equal(previewResponse.status(),200);assert.deepEqual(preview,snapshot)
   const view=dialog.getByRole('region',{name:'逐项恢复前像',exact:true});await expect(view.locator('article')).toHaveCount(4)
   for(const [n,item]of snapshot.entries.entries()){
    const article=view.locator('article').nth(n);await article.scrollIntoViewIfNeeded()
    await expect(article).toContainText(item.relativePath||'（根目录）')
    if(snapshot.summary.changedAclType==='default'&&item.kind==='File')await expect(article).toContainText('文件不受default恢复影响')
    else{
     const values=item[snapshot.summary.changedAclType+'Acl'],target=article.getByRole('region',{name:`恢复目标 ${snapshot.summary.changedAclType} ACL`,exact:true});await expect(target.locator('dt')).toHaveCount(values.length)
     for(const [i,e]of values.entries()){await expect(target.locator('dt').nth(i)).toHaveText(labels[e.tag]+(e.id===null?'':` ${e.id}`));await expect(target.locator('dd code').nth(i)).toHaveText(bits[e.permissions])}
     if(!values.length)await expect(target).toContainText('空（无默认ACL）')
    }
    await page.screenshot({path:join(fixture.evidence,`${name}-target-${n}.png`)})
   }
   let result,replayId=null,cancelDismissed=false
   if(name==='restore-cancel'){
    await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal((await query(ready.id)).state,'Ready');assert.equal(counts.cancellations,0);cancelDismissed=true
    await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:30000});result=await query(ready.id);assert.equal(result.state,'Canceled');assert.equal(result.executionJobId,null)
   }else{
    await dialog.getByRole('button',{name:'确认递归ACL恢复',exact:true}).click();await expect(dialog.getByText('递归ACL恢复已完成',{exact:true})).toBeVisible({timeout:45000});result=await query(ready.id);assert.equal(result.state,'Succeeded');assert.equal(result.receipt.state,'Completed')
    const repeated=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{...headers,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}});assert.equal(repeated.status(),202);replayId=(await repeated.json()).executionJobId;assert.equal(replayId,result.executionJobId)
   }
   await page.screenshot({path:join(fixture.evidence,name+'-result.png')});const response={name,...result};results.push(response);bodies.push(body)
   await writeSignal(fixture.evidence,name+'-result.json',{name,subject,source:snapshot,sourceRequest:httpProof(read),preview,previewRequest:httpProof(previewResponse),registered,ready,result:response,key,body,originalReference,duplicateRegistrationId:(await duplicate.json()).id,confirmationReplayId:replayId,cancelDismissedWithoutWrite:cancelDismissed})
   const accepted=await waitSignal(fixture.evidence,name+'-verified.json');assert.equal(accepted.id,result.id);assert.equal(accepted.physicalRootHddAndSqlVerified,true)
   await closePermissionDetails(page,dialog)
  }
  assert.deepEqual(counts,{registrations:5,confirmations:4,cancellations:1,uploads:0});assert.ok((await Promise.all(headerChecks)).every(Boolean))
  return {...counts,completed:results,bodies,securityChecks,realSourceSnapshotPagination:false}
 }finally{page.off('request',watch)}
}
