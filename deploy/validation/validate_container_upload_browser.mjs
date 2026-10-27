// Real installed SSO/UI; no API interception, mock, storageState, trace or credential exports.
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase='startup',browser,activePage,evidence
try {
  const revision=process.argv[2];assert.match(revision,/^[a-f0-9]{40}$/)
  const fixture=JSON.parse(await readFile('/dev/stdin','utf8'));assert.equal(fixture.revision,revision)
  evidence=fixture.evidence
  assert.match(fixture.containerId,/^[a-f0-9]{64}$/);assert.match(fixture.containerName,/^wdv-container-publish-[a-f0-9]{32}$/)
  const base=process.env.WHALEDECK_URL??'http://192.168.100.13:8080',origin=new URL(base)
  assert.equal(origin.protocol,'http:');assert.ok(['127.0.0.1','192.168.100.13','192.168.22.19'].includes(origin.hostname));assert.equal(origin.port,'8080')
  const username=process.env.VALIDATION_USERNAME,password=process.env.VALIDATION_PASSWORD;assert.ok(username&&password)
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  const context=await browser.newContext({viewport:{width:1440,height:1000},colorScheme:'light'})
  context.setDefaultTimeout(20_000)
  const page=await context.newPage()
  activePage=page
  assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  phase='normal-visible-authentik-login'
  await page.goto(base+'/login?returnUrl=/containers')
  await page.getByRole('button',{name:'使用用户名和密码登录',exact:true}).click()
  let identified=false,entered=false,consented=false
  for(let stage=0;stage<100;stage++){
    if(new URL(page.url()).origin===origin.origin&&new URL(page.url()).pathname==='/containers')break
    const identification=page.locator('ak-stage-identification'),secret=page.locator('ak-stage-password'),consent=page.locator('ak-stage-consent')
    const button=/^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if(!identified&&await identification.locator('input[name="uidField"]').isVisible()){
      await identification.locator('input[name="uidField"]').fill(username);await identification.getByRole('button',{name:button}).click();identified=true
    }else if(!entered&&await secret.locator('input[name="password"]').isVisible()){
      await secret.locator('input[name="password"]').fill(password);await secret.getByRole('button',{name:button}).click();entered=true
    }else if(entered&&!consented&&await consent.isVisible()){
      await consent.getByRole('button',{name:button}).click();consented=true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified&&entered);await expect(page).toHaveURL(base+'/containers')
  assert.equal((await(await context.request.get(base+'/api/v1/auth/me')).json()).isAdministrator,true)
  const cookie=(await context.cookies(base)).map(value=>value.name+'='+value.value).join('; ');assert.ok(cookie)
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/uploads`
  let registrations=0,bodies=0
  const headerChecks=[]
  page.on('request',request=>{
    const path=new URL(request.url()).pathname
    if(request.method()==='POST'&&path===endpoint)registrations++
    if(request.method()==='PUT'&&path.startsWith(endpoint+'/')&&path.endsWith('/content')){
      bodies++;headerChecks.push(request.allHeaders().then(headers=>headers['content-type']==='application/octet-stream'&&headers.origin===origin.origin&&!!headers['idempotency-key']))
    }
  })
  phase='owned-container-detail-and-work-directory'
  await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName)
  const card=page.locator('.container-card').filter({hasText:fixture.containerName})
  await expect(card).toHaveCount(1);await card.getByRole('button',{name:'详情',exact:true}).click()
  await page.getByRole('button',{name:'容器文件',exact:true}).click()
  const files=page.getByRole('region',{name:'容器文件',exact:true})
  await files.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work')
  await files.getByRole('button',{name:'前往',exact:true}).click()
  await expect(files.getByText('original.bin',{exact:true})).toBeVisible()
  const upload=files.getByRole('region',{name:'容器上传',exact:true})
  const body=createHash('shake256',{outputLength:fixture.bytes}).update('owned-browser-container-upload-v1').digest()
  assert.equal(createHash('sha256').update(body).digest('hex').toUpperCase(),fixture.sha256)
  const uploads=[]
  for(const [index,name] of ['original.bin','移动端新建.bin'].entries()){
    phase=index===0?'desktop-light-overwrite-confirmation':'mobile-dark-create-confirmation'
    if(index===1){await page.setViewportSize({width:320,height:1000});await page.emulateMedia({colorScheme:'dark'})}
    await upload.getByLabel('选择容器上传文件',{exact:true}).setInputFiles({name,mimeType:'application/octet-stream',buffer:body})
    await upload.getByRole('button',{name:'校验并上传',exact:true}).click()
    const confirmation=page.getByRole('dialog',{name:index===0?'确认覆盖容器文件':'确认上传容器文件',exact:true})
    await expect(confirmation).toBeVisible()
    if(index===0){
      await confirmation.getByRole('button',{name:'取消',exact:true}).click();assert.equal(registrations,0);assert.equal(bodies,0)
      await upload.getByRole('button',{name:'校验并上传',exact:true}).click();await expect(confirmation).toBeVisible()
    }
    const registered=page.waitForResponse(response=>response.request().method()==='POST'&&new URL(response.url()).pathname===endpoint&&response.status()===202)
    const received=page.waitForResponse(response=>response.request().method()==='PUT'&&new URL(response.url()).pathname.startsWith(endpoint+'/')&&response.status()===202)
    await confirmation.getByRole('button',{name:'确认并上传',exact:true}).click()
    const intent=await(await registered).json();assert.match(intent.id,/^[a-f0-9-]{36}$/);assert.equal(intent.id,intent.jobId)
    assert.equal((await(await received).json()).state,'Ready')
    await expect(upload.getByText('已发布',{exact:true})).toBeVisible({timeout:90_000})
    await expect(files.getByText(name,{exact:true})).toBeVisible()
    const completed=await(await context.request.get(base+endpoint+'/'+intent.id)).json()
    assert.equal(completed.state,'Succeeded');assert.equal(completed.previousRetained,index===0)
    uploads.push({id:completed.id,publishedVersion:completed.publishedVersion})
    phase=index===0?'desktop-upload-request-counts-and-headers':'mobile-upload-request-counts-and-headers'
    assert.equal(registrations,index+1);assert.equal(bodies,index+1);assert.ok((await Promise.all(headerChecks)).every(Boolean))
    phase=index===0?'desktop-upload-panel-width':'mobile-upload-panel-width'
    assert.ok(await files.evaluate(element=>element.scrollWidth<=element.clientWidth+1))
    phase=index===0?'desktop-upload-document-width':'mobile-upload-document-width'
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1))
    await files.screenshot({path:join(fixture.evidence,index===0?'desktop-light-upload.png':'mobile-dark-upload.png')})
    await writeFile(join(fixture.evidence,'results.json'),JSON.stringify({revision,uploads,bodyRequests:bodies,fullSsoLogout:false}),{mode:0o600})
  }
  phase='normal-two-page-full-sso-logout'
  const other=await context.newPage();await other.setViewportSize({width:1440,height:1000});await other.goto(base+'/')
  await other.getByRole('button',{name:'打开用户菜单',exact:true}).click()
  await other.getByRole('menuitem',{name:'退出登录',exact:true}).click()
  await expect(other).toHaveURL(base+'/login',{timeout:30_000})
  await expect(page).toHaveURL(base+'/login?returnUrl=/containers',{timeout:30_000})
  await expect(page.getByRole('region',{name:'容器上传',exact:true})).toHaveCount(0)
  await expect(page.getByLabel('选择容器上传文件',{exact:true})).toHaveCount(0)
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  assert.equal((await context.request.get(base+'/api/v1/auth/me',{headers:{Cookie:cookie}})).status(),401)
  assert.ok([401,403].includes((await context.request.get(origin.origin.replace(':8080',':8081')+'/api/v3/core/users/me/')).status()))
  await page.screenshot({path:join(fixture.evidence,'mobile-dark-after-logout.png')})
  await writeFile(join(fixture.evidence,'results.json'),JSON.stringify({revision,uploads,bodyRequests:bodies,fullSsoLogout:true}),{mode:0o600})
  await context.close();console.log(JSON.stringify({installedContainerUploadBrowser:'passed',revision,uploads:2,singleBody:true,fullSsoLogout:true}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_upload_browser\.mjs:([0-9]+):([0-9]+)/g)].map(match=>({line:Number(match[1]),column:Number(match[2])}))
  console.error(JSON.stringify({installedContainerUploadBrowser:'failed',phase,failureType:error?.constructor?.name??'Error',locations}));process.exitCode=1
  if(activePage&&evidence&&!activePage.isClosed()&&new URL(activePage.url()).pathname==='/containers'){
    const files=activePage.getByRole('region',{name:'容器文件',exact:true})
    if(await files.count()===1)await files.screenshot({path:join(evidence,'failed-file-panel.png')}).catch(()=>{})
  }
}finally{if(browser)await browser.close()}
