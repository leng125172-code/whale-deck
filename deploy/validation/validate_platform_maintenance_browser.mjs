// Actual installed Authentik/Vue; no route fixtures, cookie injection or execution.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'

let phase='startup',browser,page,evidence
const base='http://127.0.0.1:8080'
try{
 const input=JSON.parse(await readFile('/dev/stdin','utf8')),revision=process.argv[2]
 assert.match(revision,/^[a-f0-9]{40}$/);assert.equal(input.revision,revision)
 assert.equal(input.accounts.length,2);evidence=input.evidence
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const results=[]
 for(const account of input.accounts){
  assert.ok(['administrator','ordinary'].includes(account.role))
  const administrator=account.role==='administrator',context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'})
  context.setDefaultTimeout(20_000);page=await context.newPage()
  let executionRequests=0,otherWrites=0,planRequests=0
  page.on('request',request=>{
   const url=new URL(request.url());if(url.origin!==base||!['POST','PUT','PATCH','DELETE'].includes(request.method())||!url.pathname.startsWith('/api/v1/'))return
   if(url.pathname.startsWith('/api/v1/auth/'))return
   if(/^\/api\/v1\/platform\/(apply-update|rollback)$/.test(url.pathname))executionRequests++
   else if(/^\/api\/v1\/platform\/(apply-update|rollback)\/plan$/.test(url.pathname))planRequests++
   else otherWrites++
  })
  phase='visible-login-'+account.role
  const profile=await loginVisible(page,context,base,administrator?'/platform/maintenance':'/',administrator,account)
  assert.equal(profile.isAdministrator,administrator)
  const scenes=[],cancelled=[],forbidden=[]
  if(administrator){
   for(const [width,colorScheme] of [[320,'dark'],[320,'light'],[1440,'dark'],[1440,'light']]){
    phase='actual-build-card';await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme})
    await expect.poll(()=>page.evaluate(()=>document.documentElement.classList.contains('dark'))).toBe(colorScheme==='dark')
    const card=page.getByRole('article',{name:'实际构建版本',exact:true})
    await expect(card.locator('code')).toHaveCount(2)
    for(const code of await card.locator('code').all()){
     await expect(code).toHaveAttribute('title',revision);await expect(code).toHaveText(revision.slice(0,12))
    }
    await expect(card.getByRole('alert')).toHaveCount(0)
    await card.scrollIntoViewIfNeeded();await expect(card).toBeInViewport({ratio:1})
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
    for(const path of ['/api/v1/system/status','/build-info.json']){
     const response=await context.request.get(base+path);assert.equal(response.status(),200)
     const data=await response.json();assert.equal((data.build??data).sourceRevision,revision)
    }
    const screenshot=`build-${width}-${colorScheme}.png`;await page.screenshot({path:join(evidence,screenshot)})
    scenes.push({width,colorScheme,screenshot,actualBuildRevision:revision,noHorizontalOverflow:true})
    if((width===320&&colorScheme==='dark')||(width===1440&&colorScheme==='light')){
     for(const [label,action,title] of [['执行更新','apply-update','平台更新确认'],['回滚','rollback','平台回滚确认']]){
      phase='cancel-'+action
      const plan=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===`/api/v1/platform/${action}/plan`)
      await page.getByRole('button',{name:label,exact:true}).click();const response=await plan;assert.equal(response.status(),200)
      const payload=await response.json();assert.ok(payload.planHash&&Array.isArray(payload.changes)&&Array.isArray(payload.warnings))
      const dialog=page.getByRole('dialog',{name:title,exact:true}),cancel=dialog.getByRole('button',{name:'取消',exact:true})
      await expect(dialog).toBeVisible();await expect(cancel).toBeInViewport({ratio:1})
      const screenshot=`cancel-${action}-${width}-${colorScheme}.png`;await page.screenshot({path:join(evidence,screenshot)})
      await cancel.click();await expect(dialog).toHaveCount(0)
      await expect(page.getByRole('button',{name:label,exact:true})).toBeEnabled()
      cancelled.push({action,width,colorScheme,screenshot,cancelVisible:true,planStatus:response.status()})
     }
    }
   }
   phase='actual-agent-page';const response=await context.request.get(base+'/api/v1/agents');assert.equal(response.status(),200)
   const agent=await response.json();assert.equal(agent.capabilities.protocolMajor,1);assert.equal(agent.capabilities.protocolMinor,51);assert.equal(agent.health.available,true)
   await page.goto(base+'/operations/agents');await expect(page.getByRole('heading',{name:'宿主机 Agent',exact:true})).toBeVisible()
   await expect(page.getByText('1.51',{exact:true})).toBeVisible();await page.screenshot({path:join(evidence,'agent-1440-light.png')})
  }else{
   const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json()
   for(const path of ['/api/v1/agents','/api/v1/jobs','/api/v1/containers']){
    const response=await context.request.get(base+path);assert.equal(response.status(),403);forbidden.push({kind:'management-get',status:response.status()})
   }
   for(const action of ['apply-update','rollback']){
    const response=await context.request.post(base+`/api/v1/platform/${action}/plan`,{headers:{Origin:base,[csrf.headerName]:csrf.token},data:{resourceId:null,parameters:{}}})
    assert.equal(response.status(),403);forbidden.push({kind:action+'-plan',status:response.status()})
   }
   for(const [width,colorScheme] of [[320,'dark'],[1440,'light']]){
    phase='ordinary-forbidden';await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme})
    await page.goto(base+'/platform/maintenance');await expect(page).toHaveURL(base+'/forbidden')
    await expect(page.getByRole('heading',{name:'没有管理权限',exact:true})).toBeVisible()
    for(const name of ['执行更新','回滚','实际构建版本'])await expect(page.getByRole('button',{name,exact:true})).toHaveCount(0)
    const screenshot=`ordinary-${width}-${colorScheme}.png`;await page.screenshot({path:join(evidence,screenshot)})
    scenes.push({width,colorScheme,screenshot,forbiddenPage:true})
   }
   await page.goto(base+'/')
  }
  assert.equal(executionRequests,0);assert.equal(otherWrites,0);assert.equal(planRequests,administrator?4:0)
  phase='normal-dual-logout-'+account.role;const logout=await logoutFromUserMenu(page,context,base)
  const anonymous=[]
  for(const path of ['/api/v1/agents','/api/v1/jobs','/api/v1/containers']){
   const response=await context.request.get(base+path);assert.equal(response.status(),401);anonymous.push(response.status())
  }
  results.push({role:account.role,scenes,cancelled,forbidden,anonymous,logout,executionRequests,otherWrites,planRequests})
  await context.close();page=undefined
 }
 const result={revision,actualUi:true,results}
 await writeFile(join(evidence,'browser.json'),JSON.stringify(result),{mode:0o600,flag:'wx'})
 await browser.close();browser=undefined;console.log(JSON.stringify({platformMaintenanceBrowser:'passed',actualUi:true}))
}catch(error){
 const frame=error?.stack?.split('\n').find(line=>line.includes('validate_platform_maintenance_browser.mjs:'))?.trim()
 console.error(JSON.stringify({platformMaintenanceBrowser:'failed',phase,failureType:error?.name,frame}))
 if(browser)await browser.close().catch(()=>{});process.exitCode=1
}
