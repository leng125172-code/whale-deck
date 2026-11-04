// Installed restore preview only. Capture via signed RPC; cancel before execution.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'

let phase='startup',browser,page,evidence
function proof(response){
 const url=new URL(response.url());assert.equal(new Set(url.searchParams.keys()).size,[...url.searchParams.keys()].length)
 return {method:response.request().method(),pathname:url.pathname,query:Object.fromEntries(url.searchParams),status:response.status(),noStore:(response.headers()['cache-control']??'').includes('no-store')}
}
async function denied(context,urls,status){const values=[];for(const url of urls)values.push((await context.request.get(url)).status());assert.deepEqual(values,urls.map(()=>status));return values}
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const packet=JSON.parse(await readFile('/dev/stdin','utf8')),fixture=packet.fixture,credentials=packet.credentials
 const base='http://127.0.0.1:8080',endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`
 assert.equal(process.argv[2],fixture.revision);assert.match(fixture.captureId,/^[a-f0-9]{32}$/);assert.match(fixture.historyDigest,/^[A-F0-9]{64}$/)
 evidence=fixture.evidence;browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(30_000);page=await context.newPage()
 phase='admin-visible-login';const admin=await loginVisible(page,context,base,'/containers',true,credentials.admin)
 assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,fixture.revision)
 phase='actual-history-controls';const files=await openPermissionFiles(page,fixture),hist=page.getByRole('dialog',{name:'递归ACL历史',exact:true})
 const requests=[];const record=r=>{if(new URL(r.url()).pathname.startsWith(endpoint))requests.push({method:r.method(),pathname:new URL(r.url()).pathname})};page.on('request',record)
 let listingResponse=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
 await files.getByRole('button',{name:'递归ACL历史 recursive-tree',exact:true}).click()
 const pages=[],entries=[]
 for(let n=0;n<100;n++){
  const r=await listingResponse;assert.equal(r.status(),200);const value=await r.json();pages.push({request:proof(r),page:value});entries.push(...value.entries)
  if(value.complete)break
  assert.ok(n<99);listingResponse=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET');await hist.getByRole('button',{name:'继续扫描',exact:true}).click()
 }
 assert.equal(pages.at(-1).page.complete,true);assert.deepEqual(entries.map(e=>e.captureId),[fixture.captureId])
 const selectRead=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+fixture.captureId&&r.request().method()==='GET')
 await hist.getByRole('button',{name:'查看逐项前像 '+fixture.captureId,exact:true}).click();const selectedResponse=await selectRead,selectedSnapshot=await selectedResponse.json()
 assert.equal(selectedResponse.status(),200);assert.equal(selectedSnapshot.offset,0);assert.equal(selectedSnapshot.entries.length,25);assert.equal(selectedSnapshot.complete,false)
 await page.screenshot({path:join(evidence,'selection.png')})
 const selected={subject:admin.subject,index:0,snapshot:selectedSnapshot,request:proof(selectedResponse),screenshot:'selection.png'}
 const restoreEndpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acl-restores`,counts={registrations:0,confirmations:0,cancellations:0,uploads:0}
 page.on('request',r=>{const p=new URL(r.url()).pathname;if(r.method()==='POST'&&(p===restoreEndpoint||p.startsWith(restoreEndpoint+'/'))){if(p===restoreEndpoint)counts.registrations++;else if(p.endsWith('/confirm'))counts.confirmations++;else if(p.endsWith('/cancel'))counts.cancellations++}if(r.method()==='PUT'&&p.includes('/files/'))counts.uploads++})
 await hist.getByRole('button',{name:'预检恢复此递归ACL历史',exact:true}).click()
 const dialog=page.getByRole('dialog',{name:'递归恢复容器目录历史ACL',exact:true});await expect(dialog).toBeVisible()
 const registration=page.waitForResponse(r=>new URL(r.url()).pathname===restoreEndpoint&&r.request().method()==='POST')
 await dialog.getByRole('button',{name:'生成递归ACL恢复预检',exact:true}).click();const registeredResponse=await registration;assert.equal(registeredResponse.status(),202)
 const registered=await registeredResponse.json(),key=(await registeredResponse.request().allHeaders())['idempotency-key'],body=registeredResponse.request().postDataJSON()
 await expect(dialog.getByText('预检已完成，尚未恢复ACL',{exact:true})).toBeVisible({timeout:45000})
 async function query(){const r=await context.request.get(base+restoreEndpoint+'/'+registered.id);assert.equal(r.status(),200);return r.json()}
 const rawReady=await query(),ready={...rawReady,confirmation:null};assert.equal(ready.state,'Ready');assert.deepEqual([ready.entryCount,ready.fileCount,ready.directoryCount,ready.changeCount],[26,25,1,26]);assert.deepEqual(ready.restoreSource,selectedSnapshot.summary)
 const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json(),headers={Origin:base,[csrf.headerName]:csrf.token,'Idempotency-Key':key}
 const duplicate=await context.request.post(base+restoreEndpoint,{headers,data:body});assert.equal(duplicate.status(),202);const duplicateRegistrationId=(await duplicate.json()).id;assert.equal(duplicateRegistrationId,ready.id)
 const reference={subject:admin.subject,containerId:fixture.containerId,key,id:ready.id}
 async function checkReference(){const values=JSON.parse(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-restore-hints-v1')));assert.deepEqual(values.find(v=>v.containerId===fixture.containerId),reference)}
 await checkReference();await page.reload();await openPermissionFiles(page,fixture);await checkReference();await page.getByRole('button',{name:'查看递归ACL恢复任务',exact:true}).click();await expect(dialog.getByText('预检已完成，尚未恢复ACL',{exact:true})).toBeVisible({timeout:30000})
 const labels=['first','last','back','last-again'],signals=[]
 for(let index=0;index<4;index++){
  phase='restore-preview-'+labels[index]
  if(index===2){await page.setViewportSize({width:1440,height:600});await page.emulateMedia({colorScheme:'light'})}
  const reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+fixture.captureId&&r.request().method()==='GET')
  await dialog.getByRole('button',{name:index===0?'查看逐项恢复目标ACL':index===2?'上一页恢复前像':'下一页恢复前像',exact:true}).click()
  const r=await reading;assert.equal(r.status(),200);const value=await r.json();assert.equal(value.offset,index%2?25:0);assert.equal(value.entries.length,index%2?1:25);assert.deepEqual(value.summary,ready.restoreSource)
  assert.equal(value.complete,index%2===1);assert.equal(value.nextOffset,index%2?null:25)
  const details=dialog.getByRole('region',{name:'逐项恢复前像',exact:true});await expect(details.locator('article')).toHaveCount(value.entries.length)
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx'],names={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
  for(const [n,entry]of value.entries.entries()){
   const article=details.locator('article').nth(n);await article.scrollIntoViewIfNeeded();await expect(article).toContainText(entry.relativePath||'（根目录）')
   const region=article.getByRole('region',{name:'恢复目标 access ACL',exact:true}),acl=entry.accessAcl;await expect(region.locator('dt')).toHaveCount(acl.length)
   for(const [i,e]of acl.entries()){await expect(region.locator('dt').nth(i)).toHaveText(names[e.tag]+(e.id===null?'':` ${e.id}`));await expect(region.locator('dd code').nth(i)).toHaveText(bits[e.permissions])}
  }
  const navigation=dialog.getByRole('button',{name:index%2?'上一页恢复前像':'下一页恢复前像',exact:true});await navigation.scrollIntoViewIfNeeded();await expect(navigation).toBeInViewport({ratio:1})
  for(const name of ['关闭','确认递归ACL恢复','请求取消递归ACL恢复'])await expect(dialog.getByRole('button',{name,exact:true})).toBeInViewport({ratio:1})
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
  const screenshot=`paging-${index}.png`;await page.screenshot({path:join(evidence,screenshot)})
  const signal={subject:admin.subject,index,snapshot:value,request:proof(r),pages:[...pages],screenshot,selected,registered,ready,key,body,reference,duplicateRegistrationId};signals.push(signal)
  await writeSignal(evidence,'paging-'+labels[index]+'-result.json',signal)
  assert.equal((await waitSignal(evidence,'paging-'+labels[index]+'-verified.json')).physicalSourceAndReadyVerified,true)
 }
 assert.ok(requests.every(r=>r.method==='GET'));assert.equal(requests.length,pages.length+5);page.off('request',record)
 phase='cancel-before-execution'
 await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal((await query()).state,'Ready');assert.equal(counts.cancellations,0)
 await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:30000});const result=await query();assert.equal(result.state,'Canceled');assert.equal(result.executionJobId,null)
 assert.deepEqual(counts,{registrations:1,confirmations:0,cancellations:1,uploads:0});await page.screenshot({path:join(evidence,'canceled.png')})
 await closePermissionDetails(page,dialog)
 const historyQuery={instanceId:fixture.instanceId,path:'work/recursive-tree'}
 const urls=[base+restoreEndpoint+'/'+ready.id,base+restoreEndpoint+'/by-key/'+key,base+'/api/v1/files/container-recursive-acl-restores/recovery',base+'/api/v1/files/container-recursive-acl-restores/recovery/'+ready.id,base+endpoint+'?'+new URLSearchParams({...historyQuery,after:'',take:'25',expectedVersion:''}),base+endpoint+'/'+fixture.captureId+'?'+new URLSearchParams({...historyQuery,expectedHistoryDigest:fixture.historyDigest,offset:'25',take:'25'})]
 phase='admin-normal-dual-logout';const adminLogout=await logoutFromUserMenu(page,context,base);const adminAnonymous=await denied(context,urls,401)
 const ordinaryContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});ordinaryContext.setDefaultTimeout(30_000);const ordinaryPage=await ordinaryContext.newPage();page=ordinaryPage
 phase='ordinary-visible-login';const ordinary=await loginVisible(ordinaryPage,ordinaryContext,base,'/',false,credentials.ordinary)
 let uiRequests=0;ordinaryPage.on('request',r=>{if([endpoint,restoreEndpoint,'/api/v1/files/container-recursive-acl-restores/recovery'].some(v=>new URL(r.url()).pathname.startsWith(v)))uiRequests++})
 phase='ordinary-denied';await ordinaryPage.goto(base+'/containers');await expect(ordinaryPage).toHaveURL(base+'/forbidden');assert.equal(uiRequests,0)
 const ordinaryDenied=await denied(ordinaryContext,urls,403)
 await ordinaryPage.screenshot({path:join(evidence,'paging-ordinary.png')})
 await ordinaryPage.getByRole('button',{name:'首页',exact:true}).click();await expect(ordinaryPage).toHaveURL(base+'/')
 phase='ordinary-normal-dual-logout';const ordinaryLogout=await logoutFromUserMenu(ordinaryPage,ordinaryContext,base);const anonymous=await denied(ordinaryContext,urls,401)
 const browserResult={result,ready,registered,key,body,reference,duplicateRegistrationId,dismissedWithoutWrite:true,...counts,revision:fixture.revision,subject:admin.subject,pages,signals,requests,mutations:2,adminLogout,adminAnonymous,ordinary:{subject:ordinary.subject,denied:ordinaryDenied,anonymous,uiRequests,logout:ordinaryLogout},realRestoreSnapshotPagination:true,restoreExecutions:0}
 await writeSignal(evidence,'paging-auth-result.json',browserResult);assert.equal((await waitSignal(evidence,'paging-auth-verified.json')).canceledNoWriteAndAuthorityVerified,true)
 await writeFile(join(evidence,'browser.json'),JSON.stringify(browserResult),{mode:0o600,flag:'wx'})
 await ordinaryContext.close();await context.close();await browser.close();browser=undefined
 console.log(JSON.stringify({recursiveAclRestorePaging:'passed',snapshotOffsets:[0,25,0,25],actualUi:true}))
}catch(error){
 if(page&&evidence&&!phase.includes('login')&&!phase.includes('logout'))await page.screenshot({path:join(evidence,'paging-failed.png')}).catch(()=>{})
 console.error(JSON.stringify({recursiveAclRestorePaging:'failed',phase,failureType:error?.name}))
 if(browser)await browser.close().catch(()=>{});process.exitCode=1
}
