// Actual installed UI and Authentik only; no routes, exported cookies or traces.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase='startup',browser
try{
  const revision=process.argv[2];assert.match(revision,/^[a-f0-9]{40}$/)
  const fixture=JSON.parse(await readFile('/dev/stdin','utf8'));assert.equal(fixture.revision,revision)
  assert.match(fixture.intent,/^[a-f0-9-]{36}$/);assert.match(fixture.containerId,/^[a-f0-9]{64}$/)
  const base=process.env.WHALEDECK_URL??'http://192.168.100.13:8080',origin=new URL(base)
  assert.equal(origin.protocol,'http:');assert.equal(origin.port,'8080');assert.ok(['127.0.0.1','192.168.100.13','192.168.22.19'].includes(origin.hostname))
  const username=process.env.VALIDATION_USERNAME,password=process.env.VALIDATION_PASSWORD;assert.ok(username&&password)
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  const context=await browser.newContext({viewport:{width:320,height:1100},colorScheme:'dark',reducedMotion:'reduce'})
  context.setDefaultTimeout(20_000)
  const page=await context.newPage();assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  phase='normal-visible-different-admin-login'
  await page.goto(base+'/login?returnUrl=/files');await page.getByRole('button',{name:'使用用户名和密码登录',exact:true}).click()
  let identified=false,entered=false,consented=false
  for(let stage=0;stage<100;stage++){
    if(new URL(page.url()).origin===origin.origin&&new URL(page.url()).pathname==='/files')break
    const identification=page.locator('ak-stage-identification'),secret=page.locator('ak-stage-password'),consent=page.locator('ak-stage-consent'),button=/^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if(!identified&&await identification.locator('input[name="uidField"]').isVisible()){
      await identification.locator('input[name="uidField"]').fill(username);await identification.getByRole('button',{name:button}).click();identified=true
    }else if(!entered&&await secret.locator('input[name="password"]').isVisible()){
      await secret.locator('input[name="password"]').fill(password);await secret.getByRole('button',{name:button}).click();entered=true
    }else if(entered&&!consented&&await consent.isVisible()){
      await consent.getByRole('button',{name:button}).click();consented=true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified&&entered);await expect(page).toHaveURL(base+'/files')
  const profile=await(await context.request.get(base+'/api/v1/auth/me')).json();assert.equal(profile.isAdministrator,true)
  let registrations=0,confirms=0,contentRequests=0,recoveryId
  page.on('request',request=>{
    const path=new URL(request.url()).pathname
    if(request.method()==='POST'&&path==='/api/v1/files/recovery/operations')registrations++
    if(request.method()==='POST'&&/^\/api\/v1\/files\/recovery\/operations\/[a-f0-9-]{36}\/confirm$/.test(path))confirms++
    if(request.method()==='PUT'&&path.includes('/files/'))contentRequests++
  })
  phase='original-attribute-restoration-is-labelled-as-container-file-write'
  await page.getByRole('button',{name:'异常任务接管',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'异常文件任务',exact:true});await expect(dialog).toBeVisible()
  const candidate=dialog.locator('.recovery-candidate').filter({hasText:fixture.intent});await expect(candidate).toHaveCount(1)
  await expect(candidate.getByText('容器文件写入',{exact:true})).toBeVisible()
  await candidate.getByRole('button',{name:'核对该任务',exact:true}).click()
  await expect(dialog.getByText(/不会重新上传、编辑或恢复文件/)).toBeVisible()
  const created=page.waitForResponse(response=>response.request().method()==='POST'&&new URL(response.url()).pathname==='/api/v1/files/recovery/operations'&&response.status()===202)
  await dialog.getByRole('button',{name:'生成接管计划',exact:true}).click()
  const initial=await(await created).json();recoveryId=initial.id;assert.match(recoveryId,/^[a-f0-9-]{36}$/)
  const endpoint=base+'/api/v1/files/recovery/operations/'+recoveryId
  phase='read-only-plan-poll-and-normal-second-confirmation'
  for(let count=0;count<120;count++){
    const accept=dialog.getByRole('button',{name:'接受未知结果',exact:true})
    if(await accept.count()&&await accept.isEnabled())break
    await dialog.getByRole('button',{name:'查询原接管任务',exact:true}).click();await page.waitForTimeout(500)
  }
  await expect(dialog.getByRole('button',{name:'接受未知结果',exact:true})).toBeEnabled()
  assert.equal(registrations,1);assert.equal(confirms,0);assert.equal(contentRequests,0)
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1))
  await dialog.screenshot({path:join(fixture.evidence,'mobile-dark-seal-ready.png')})
  await dialog.getByRole('button',{name:'接受未知结果',exact:true}).click()
  const confirm=page.getByRole('dialog',{name:'明确接受历史不确定性',exact:true});await expect(confirm).toBeVisible()
  await confirm.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(confirms,0)
  await dialog.getByRole('button',{name:'接受未知结果',exact:true}).click()
  await confirm.getByRole('button',{name:'确认处置',exact:true}).click()
  let completed
  for(let count=0;count<160;count++){
    completed=await(await context.request.get(endpoint)).json()
    if(completed.state==='Disposed')break
    assert.ok(!['Failed','Canceled','Expired','Unknown'].includes(completed.state));await page.waitForTimeout(500)
  }
  assert.equal(completed.state,'Disposed');assert.equal(completed.originalResolution,'Failed')
  await dialog.getByRole('button',{name:'查询原接管任务',exact:true}).click()
  await expect(dialog.getByText('已封存容器文件任务的历史不确定性；不表示操作成功，也不会重新上传、编辑或恢复文件。',{exact:true})).toBeVisible()
  assert.equal(registrations,1);assert.equal(confirms,1);assert.equal(contentRequests,0)
  assert.ok(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth+1));assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1))
  await dialog.screenshot({path:join(fixture.evidence,'mobile-dark-seal-completed.png')})
  await writeFile(join(fixture.evidence,'browser-result.json'),JSON.stringify({revision,recoveryId,executionJobId:completed.executionJobId,
    originalResolution:completed.originalResolution,registrations,confirms,contentRequests,fullSsoLogout:false}),{mode:0o600})
  phase='normal-logout-clears-private-recovery-page'
  await dialog.locator('.el-dialog__headerbtn').click()
  if(!await page.getByRole('button',{name:'打开用户菜单',exact:true}).isVisible())await page.getByRole('button',{name:'打开导航',exact:true}).click()
  await page.getByRole('button',{name:'打开用户菜单',exact:true}).click();await page.getByRole('menuitem',{name:'退出登录',exact:true}).click()
  await expect(page).toHaveURL(base+'/login',{timeout:30_000});await expect(dialog).toHaveCount(0)
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  assert.ok([401,403].includes((await context.request.get(origin.origin.replace(':8080',':8081')+'/api/v3/core/users/me/')).status()))
  await writeFile(join(fixture.evidence,'browser-result.json'),JSON.stringify({revision,recoveryId,executionJobId:completed.executionJobId,
    originalResolution:completed.originalResolution,registrations,confirms,contentRequests,fullSsoLogout:true}),{mode:0o600})
  await context.close();console.log(JSON.stringify({restorationPublicationUi:'passed',revision,registrations,confirms,contentRequests}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_restoration_publication\.mjs:([0-9]+):([0-9]+)/g)].map(match=>({line:Number(match[1]),column:Number(match[2])}))
  console.error(JSON.stringify({restorationPublicationUi:'failed',phase,failureType:error?.constructor?.name??'Error',locations}));process.exitCode=1
}finally{if(browser)await browser.close()}
