// Visible installed UI only; the owning parent proves each original disk/SQL boundary.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
import {observeJob} from './installed_ui_restore_job_results.mjs'

export async function runPartialResolution(f){
 const {browser,context,page,base,fixture,credentials,admin,dialog,readySignal,pending,counts,rawReady,query}=f
 const evidence=fixture.evidence,id=readySignal.ready.id,endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acl-restores`,history=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`,recovery=`/api/v1/files/container-recursive-acl-restores/recovery/${id}`
 const urls=[base+endpoint+'/'+id,base+endpoint+'/by-key/'+readySignal.key,base+'/api/v1/files/container-recursive-acl-restores/recovery',base+recovery,
  base+history+'?'+new URLSearchParams({instanceId:fixture.instanceId,path:'work/recursive-tree',after:'',take:'25',expectedVersion:''}),
  base+history+'/'+fixture.captureId+'?'+new URLSearchParams({instanceId:fixture.instanceId,path:'work/recursive-tree',expectedHistoryDigest:fixture.historyDigest,offset:'0',take:'25'}),
  base+'/api/v1/jobs?take=100',base+'/api/v1/jobs/'+id,base+'/api/v1/jobs/'+pending.executionJobId]
 async function statuses(c,status){const values=[];for(const u of urls)values.push((await c.request.get(u)).status());assert.deepEqual(values,Array(9).fill(status));return values}
 async function job(c,j,state,prefix){const p=await c.newPage();try{return await observeJob(p,c,base,j,'execution',state,evidence,prefix)}finally{await p.close()}}
 let phase='first-completed-wait',activePage=page,resolverContext,ordinaryContext
 try{
  assert.equal((await waitSignal(evidence,'action-verified.json',180000)).boundFirstCompleted,true)
  phase='normal-cancel';await dialog.getByRole('button',{name:'查询原递归ACL恢复任务',exact:true}).click();await expect(dialog.getByText('正在逐项恢复ACL',{exact:true}).first()).toBeVisible()
  await page.screenshot({path:join(evidence,'inflight.png')})
  await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal(counts.cancellations,0)
  await dialog.getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
  await expect.poll(()=>counts.cancellations).toBe(1);await writeSignal(evidence,'action-result.json',{id,subject:admin.subject,cancellations:counts.cancellations})
  const settled=await waitSignal(evidence,'settled-verified.json',180000);assert.equal(settled.id,id);assert.equal(settled.state,'Partial')
  phase='partial-original-task';await dialog.getByRole('button',{name:'查询原递归ACL恢复任务',exact:true}).click();await expect(dialog.getByText('部分修改，保护已保留',{exact:true}).first()).toBeVisible()
  const result=await query();assert.equal(result.state,'Partial');assert.equal(result.executionJobId,pending.executionJobId);assert.equal(result.receipt.state,'Partial')
  assert.deepEqual(['completedCount','changedCount','abortedCount','notStartedCount','unknownCount'].map(k=>result.receipt[k]),[1,1,0,25,0])
  await page.screenshot({path:join(evidence,'partial.png')});const partialJob=await job(context,pending.executionJobId,'Partial','job-fault-partial-execution')
  await closePermissionDetails(page,dialog);phase='original-normal-logout';const logout=await logoutFromUserMenu(page,context,base),anonymous=await statuses(context,401)
  assert.deepEqual(counts,{registrations:1,confirmations:1,cancellations:1,uploads:0})
  const partial={id,subject:admin.subject,readySignal,pending,result,job:partialJob,logout,anonymous,...counts}
  await writeSignal(evidence,'partial-result.json',partial);assert.equal((await waitSignal(evidence,'partial-verified.json')).partialPhysicalVerified,true)
  resolverContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});resolverContext.setDefaultTimeout(30000);activePage=await resolverContext.newPage()
  phase='resolver-visible-login';const resolver=await loginVisible(activePage,resolverContext,base,'/files',true,credentials.resolver);assert.notEqual(resolver.subject,admin.subject)
  const ownedLookup=(await resolverContext.request.get(base+endpoint+'/'+id)).status();assert.equal(ownedLookup,404)
  let acknowledgements=0;activePage.on('request',r=>{if(new URL(r.url()).pathname===recovery+'/acknowledge'&&r.method()==='POST')acknowledgements++})
  phase='resolver-dismiss';await activePage.getByRole('button',{name:'容器递归ACL恢复异常',exact:true}).click()
  const rd=activePage.getByRole('dialog',{name:'容器递归ACL恢复异常处置',exact:true}),row=rd.locator('.recursive-acl-restore-recovery__row').filter({hasText:id})
  await expect(row).toHaveCount(1);await expect(row).toContainText('部分修改，保护已保留');await row.getByRole('button',{name:'处置历史结果',exact:true}).click()
  await activePage.screenshot({path:join(evidence,'recovery-partial.png')});await rd.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(acknowledgements,0)
  await writeSignal(evidence,'recovery-dismissed-result.json',{id,subject:resolver.subject,acknowledgements,ownedLookup});assert.equal((await waitSignal(evidence,'recovery-allow-verified.json')).originalFactsPreserved,true)
  phase='resolver-explicit-ack';await row.getByRole('button',{name:'处置历史结果',exact:true}).click()
  const response=activePage.waitForResponse(r=>new URL(r.url()).pathname===recovery+'/acknowledge'&&r.request().method()==='POST');await rd.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();assert.equal((await response).status(),202)
  await expect(rd.getByRole('status')).toHaveText('已处置部分修改或未知历史结果并释放保护；不代表整树恢复成功，不会续做或再次恢复ACL，不删除原对象。')
  await activePage.screenshot({path:join(evidence,'recovery-acknowledged.png')});const rr=await resolverContext.request.get(base+recovery);assert.equal(rr.status(),200);const resolved=await rr.json();assert.equal(resolved.state,'Acknowledged');assert.equal(acknowledgements,1)
  await rd.getByRole('button',{name:'关闭',exact:true}).click();const acknowledgedJob=await job(resolverContext,pending.executionJobId,'Acknowledged','job-fault-acknowledged-execution')
  phase='resolver-normal-logout';const resolverLogout=await logoutFromUserMenu(activePage,resolverContext,base),resolverAnonymous=await statuses(resolverContext,401)
  const recoveryResult={id,subject:resolver.subject,result:resolved,job:acknowledgedJob,acknowledgements,logout:resolverLogout,anonymous:resolverAnonymous}
  await writeSignal(evidence,'recovery-result.json',recoveryResult);assert.equal((await waitSignal(evidence,'recovery-done-verified.json')).ackAndPhysicalPreserved,true)
  ordinaryContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});ordinaryContext.setDefaultTimeout(30000);activePage=await ordinaryContext.newPage()
  phase='ordinary-visible-login';const ordinary=await loginVisible(activePage,ordinaryContext,base,'/',false,credentials.ordinary);assert.notEqual(ordinary.subject,admin.subject);assert.notEqual(ordinary.subject,resolver.subject)
  let uiRequests=0;const watch=r=>{if(['/api/v1/jobs',endpoint,history,'/api/v1/files/container-recursive-acl-restores/recovery'].some(p=>new URL(r.url()).pathname.startsWith(p)))uiRequests++};activePage.on('request',watch)
  await activePage.goto(base+'/containers');await expect(activePage).toHaveURL(base+'/forbidden');await activePage.goto(base+'/jobs');await expect(activePage).toHaveURL(base+'/forbidden');assert.equal(uiRequests,0);activePage.off('request',watch)
  const denied=await statuses(ordinaryContext,403),csrf=await(await ordinaryContext.request.get(base+'/api/v1/auth/csrf')).json(),posts=[]
  for(const [url,data]of [[endpoint+'/'+id+'/confirm',{confirmed:true,version:readySignal.ready.version,planHash:rawReady.confirmation.planHash}],[endpoint+'/'+id+'/cancel',undefined],[recovery+'/acknowledge',{version:resolved.version,confirmed:true}]]){
   const r=await ordinaryContext.request.post(base+url,{headers:{Origin:base,[csrf.headerName]:csrf.token,'Idempotency-Key':id},data});assert.equal(r.status(),403);posts.push(r.status())
  }
  await activePage.screenshot({path:join(evidence,'ordinary-denied.png')});await activePage.getByRole('button',{name:'首页',exact:true}).click();phase='ordinary-normal-logout'
  const ordinaryLogout=await logoutFromUserMenu(activePage,ordinaryContext,base),ordinaryAnonymous=await statuses(ordinaryContext,401)
  const final={revision:fixture.revision,partial,recovery:recoveryResult,ordinary:{subject:ordinary.subject,denied,posts,anonymous:ordinaryAnonymous,uiRequests,logout:ordinaryLogout},actualUi:true}
  await writeSignal(evidence,'final-auth-result.json',final);assert.equal((await waitSignal(evidence,'final-auth-verified.json')).partialAndAckVerified,true)
  await writeSignal(evidence,'browser.json',final);await ordinaryContext.close();await resolverContext.close();await context.close();await browser.close()
 }catch(error){
  if(!phase.includes('login')&&!phase.includes('logout'))await activePage.screenshot({path:join(evidence,'partial-flow-failed.png')}).catch(()=>{})
  console.error(JSON.stringify({restorePartialFlow:'failed',phase,type:error?.name}));throw error
 }
}
