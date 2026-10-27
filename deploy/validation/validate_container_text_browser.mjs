// Installed UI only. No HTTP replacement, storageState, trace or credential export.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'

let phase='startup', browser
try {
  const revision=process.argv[2]; assert.match(revision,/^[a-f0-9]{40}$/)
  const fixture=JSON.parse(await readFile('/dev/stdin','utf8')); assert.equal(fixture.revision,revision)
  assert.match(fixture.containerId,/^[a-f0-9]{64}$/)
  assert.match(fixture.containerName,/^wdv-container-publish-[a-f0-9]{32}$/)
  assert.equal(fixture.leaf,'编辑示例.txt'); assert.equal(fixture.texts.length,2)
  const base=process.env.WHALEDECK_URL??'http://192.168.100.13:8080', origin=new URL(base)
  assert.equal(origin.protocol,'http:'); assert.equal(origin.port,'8080')
  assert.ok(['127.0.0.1','192.168.100.13','192.168.22.19'].includes(origin.hostname))
  const username=process.env.VALIDATION_USERNAME,password=process.env.VALIDATION_PASSWORD
  assert.ok(username&&password)
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  const context=await browser.newContext({viewport:{width:1440,height:1000},colorScheme:'light'})
  context.setDefaultTimeout(20_000)
  const page=await context.newPage()
  assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  phase='normal-visible-authentik-login'
  await page.goto(base+'/login?returnUrl=/containers')
  await page.getByRole('button',{name:'使用用户名和密码登录',exact:true}).click()
  let identified=false,entered=false,consented=false
  for(let stage=0;stage<100;stage++){
    if(new URL(page.url()).origin===origin.origin&&new URL(page.url()).pathname==='/containers') break
    const identification=page.locator('ak-stage-identification'),secret=page.locator('ak-stage-password'),consent=page.locator('ak-stage-consent')
    const button=/^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if(!identified&&await identification.locator('input[name="uidField"]').isVisible()){
      await identification.locator('input[name="uidField"]').fill(username)
      await identification.getByRole('button',{name:button}).click(); identified=true
    }else if(!entered&&await secret.locator('input[name="password"]').isVisible()){
      await secret.locator('input[name="password"]').fill(password)
      await secret.getByRole('button',{name:button}).click(); entered=true
    }else if(entered&&!consented&&await consent.isVisible()){
      await consent.getByRole('button',{name:button}).click(); consented=true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified&&entered); await expect(page).toHaveURL(base+'/containers')
  assert.equal((await(await context.request.get(base+'/api/v1/auth/me')).json()).isAdministrator,true)
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/uploads`
  let registrations=0,bodies=0
  const headers=[]
  page.on('request',request=>{
    const path=new URL(request.url()).pathname
    if(request.method()==='POST'&&path===endpoint) registrations++
    if(request.method()==='PUT'&&path.startsWith(endpoint+'/')&&path.endsWith('/content')){
      bodies++
      headers.push(request.allHeaders().then(value=>value['content-type']==='application/octet-stream'&&value.origin===origin.origin&&!!value['idempotency-key']))
    }
  })
  phase='owned-container-and-work-directory'
  await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName)
  const card=page.locator('.container-card').filter({hasText:fixture.containerName})
  await expect(card).toHaveCount(1); await card.getByRole('button',{name:'详情',exact:true}).click()
  await page.getByRole('button',{name:'容器文件',exact:true}).click()
  const files=page.getByRole('region',{name:'容器文件',exact:true})
  await files.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work')
  await files.getByRole('button',{name:'前往',exact:true}).click()
  const edits=[]
  for(let index=0;index<2;index++){
    phase=index===0?'desktop-light-editor':'mobile-dark-editor'
    if(index===1){ await page.setViewportSize({width:320,height:1000}); await page.emulateMedia({colorScheme:'dark'}) }
    const download=page.waitForRequest(request=>request.method()==='GET'&&new URL(request.url()).pathname===`/api/v1/containers/${fixture.containerId}/files/download`)
    await files.getByRole('button',{name:`编辑 ${fixture.leaf}`,exact:true}).click()
    const editor=page.getByRole('dialog',{name:`编辑容器文本 · ${fixture.leaf}`,exact:true})
    await expect(editor).toBeVisible()
    const originalVersion=new URL((await download).url()).searchParams.get('expectedVersion')
    assert.match(originalVersion,/^[A-F0-9]{64}$/)
    const text=editor.getByRole('textbox',{name:'容器文件文本内容',exact:true})
    await expect(text).toHaveValue(index===0?fixture.initialText:fixture.texts[index-1])
    await expect(editor.getByRole('checkbox',{name:'容器文本 UTF-8 BOM',exact:true})).toBeChecked()
    await text.fill(fixture.texts[index])
    assert.ok(await editor.evaluate(element=>element.scrollWidth<=element.clientWidth+1))
    await editor.screenshot({path:join(fixture.evidence,index===0?'desktop-light-editor.png':'mobile-dark-editor.png')})
    const planned=page.waitForRequest(request=>request.method()==='POST'&&new URL(request.url()).pathname===endpoint+'/plan')
    await editor.getByRole('button',{name:'校验并保存',exact:true}).click()
    assert.equal((await planned).postDataJSON().expectedVersion,originalVersion)
    const confirmation=page.getByRole('dialog',{name:'确认覆盖容器文件',exact:true})
    await expect(confirmation).toBeVisible()
    await confirmation.getByRole('button',{name:'取消',exact:true}).click()
    assert.equal(registrations,index); assert.equal(bodies,index)
    await files.getByRole('button',{name:'返回编辑草稿',exact:true}).click()
    await expect(text).toHaveValue(fixture.texts[index])
    await editor.getByRole('button',{name:'校验并保存',exact:true}).click()
    await expect(confirmation).toBeVisible()
    const registered=page.waitForResponse(response=>response.request().method()==='POST'&&new URL(response.url()).pathname===endpoint&&response.status()===202)
    const received=page.waitForResponse(response=>response.request().method()==='PUT'&&new URL(response.url()).pathname.startsWith(endpoint+'/')&&response.status()===202)
    await confirmation.getByRole('button',{name:'确认并上传',exact:true}).click()
    const intent=await(await registered).json(); assert.match(intent.id,/^[a-f0-9-]{36}$/)
    assert.equal((await(await received).json()).state,'Ready')
    await expect(files.getByText('已发布',{exact:true})).toBeVisible({timeout:90_000})
    const completed=await(await context.request.get(base+endpoint+'/'+intent.id)).json()
    assert.equal(completed.state,'Succeeded'); assert.equal(completed.previousRetained,true)
    edits.push({id:completed.id,originalVersion,publishedVersion:completed.publishedVersion})
    assert.equal(registrations,index+1); assert.equal(bodies,index+1); assert.ok((await Promise.all(headers)).every(Boolean))
    assert.ok(await files.evaluate(element=>element.scrollWidth<=element.clientWidth+1))
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1))
    await writeFile(join(fixture.evidence,'results.json'),JSON.stringify({revision,edits,bodyRequests:bodies,fullSsoLogout:false}),{mode:0o600})
  }
  phase='normal-two-page-logout-clears-an-unsaved-editor'
  await files.getByRole('button',{name:`编辑 ${fixture.leaf}`,exact:true}).click()
  const editor=page.getByRole('dialog',{name:`编辑容器文本 · ${fixture.leaf}`,exact:true})
  await editor.getByRole('textbox',{name:'容器文件文本内容',exact:true}).fill('退出后不得保留的私有草稿')
  await editor.getByRole('button',{name:'取消',exact:true}).click()
  await expect(page.getByRole('dialog',{name:'放弃容器文本编辑？',exact:true})).toBeVisible()
  const other=await context.newPage(); await other.goto(base+'/')
  await other.getByRole('button',{name:'打开用户菜单',exact:true}).click()
  await other.getByRole('menuitem',{name:'退出登录',exact:true}).click()
  await expect(other).toHaveURL(base+'/login',{timeout:30_000})
  await expect(page).toHaveURL(base+'/login?returnUrl=/containers',{timeout:30_000})
  await expect(editor).toHaveCount(0)
  await expect(page.getByRole('dialog',{name:'放弃容器文本编辑？',exact:true})).toHaveCount(0)
  assert.equal(registrations,2); assert.equal(bodies,2)
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  assert.ok([401,403].includes((await context.request.get(origin.origin.replace(':8080',':8081')+'/api/v3/core/users/me/')).status()))
  await writeFile(join(fixture.evidence,'results.json'),JSON.stringify({revision,edits,bodyRequests:bodies,fullSsoLogout:true}),{mode:0o600})
  await context.close()
  console.log(JSON.stringify({installedContainerTextBrowser:'passed',revision,edits:2,singleBody:true,fullSsoLogout:true}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_text_browser\.mjs:([0-9]+):([0-9]+)/g)].map(match=>({line:Number(match[1]),column:Number(match[2])}))
  console.error(JSON.stringify({installedContainerTextBrowser:'failed',phase,failureType:error?.constructor?.name??'Error',locations})); process.exitCode=1
}finally{ if(browser) await browser.close() }
