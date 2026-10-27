// Actual installed SSO/UI and CDP throttled uploads, no mocked HTTP or tokens.
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile,writeFile,rename } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium,expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { observeUpload } from './browser_upload_probe.mjs'

let phase='startup',browser,probe,activePage,evidence
try{
  const revision=process.argv[2];assert.match(revision,/^[a-f0-9]{40}$/)
  const fixture=JSON.parse(await readFile('/dev/stdin','utf8'));assert.equal(fixture.revision,revision)
  evidence=fixture.evidence
  assert.match(fixture.containerId,/^[a-f0-9]{64}$/);assert.match(fixture.containerName,/^wdv-container-publish-[a-f0-9]{32}$/)
  const base=process.env.WHALEDECK_URL??'http://192.168.100.13:8080',origin=new URL(base)
  assert.equal(origin.protocol,'http:');assert.equal(origin.port,'8080')
  assert.ok(['127.0.0.1','192.168.100.13','192.168.22.19'].includes(origin.hostname))
  const username=process.env.VALIDATION_USERNAME,password=process.env.VALIDATION_PASSWORD;assert.ok(username&&password)
  async function save(name,value){
    assert.match(name,/^[a-z0-9.-]+$/)
    const path=join(fixture.evidence,name);await writeFile(path+'.tmp',JSON.stringify(value),{mode:0o600});await rename(path+'.tmp',path)
  }
  async function flag(name,id){
    await expect.poll(async()=>{
      try{const value=JSON.parse(await readFile(join(fixture.evidence,name),'utf8'));return value.id===id}
      catch(error){if(error.code==='ENOENT')return false;throw error}
    },{timeout:90_000,intervals:[100,200,500]}).toBe(true)
  }
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  const context=await browser.newContext({viewport:{width:1440,height:1000},colorScheme:'light'});context.setDefaultTimeout(20_000)
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
  phase='owned-container-and-work-directory'
  await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName)
  const card=page.locator('.container-card').filter({hasText:fixture.containerName});await expect(card).toHaveCount(1)
  await card.getByRole('button',{name:'详情',exact:true}).click()
  await page.getByRole('button',{name:'容器文件',exact:true}).click()
  const files=page.getByRole('region',{name:'容器文件',exact:true})
  await files.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work')
  await files.getByRole('button',{name:'前往',exact:true}).click()
  await expect(files.getByText('original.bin',{exact:true})).toBeVisible()
  const upload=files.getByRole('region',{name:'容器上传',exact:true})
  const body=createHash('shake256',{outputLength:fixture.sizeBytes}).update('owned-real-browser-partial-upload-v1').digest()
  assert.equal(createHash('sha256').update(body).digest('hex').toUpperCase(),fixture.sha256)
  const cdp=await context.newCDPSession(page);await cdp.send('Network.enable')
  await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:0,downloadThroughput:-1,uploadThroughput:65536})
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/uploads`
  let bodyRequests=0,observedIntent
  cdp.on('Network.requestWillBeSent',event=>{
    const path=new URL(event.request.url).pathname
    if(event.request.method!=='PUT'||!path.startsWith(endpoint+'/')||!path.endsWith('/content'))return
    bodyRequests++
    if(!probe){observedIntent=path.split('/').at(-2);probe=observeUpload(cdp,path,event)}
  })
  const cases=[]
  for(const [index,scenario] of ['cancel','logout'].entries()){
    phase=scenario+'-confirm-one-real-throttled-upload'
    // Playwright buffers are capped at 50 MiB; select the real private file.
    await upload.getByLabel('选择容器上传文件',{exact:true}).setInputFiles(join(fixture.evidence,'browser-'+scenario+'.bin'))
    await upload.getByRole('button',{name:'校验并上传',exact:true}).click()
    const confirm=page.getByRole('dialog',{name:'确认上传容器文件',exact:true});await expect(confirm).toBeVisible()
    const registered=page.waitForResponse(response=>response.request().method()==='POST'&&new URL(response.url()).pathname===endpoint&&response.status()===202)
    await confirm.getByRole('button',{name:'确认并上传',exact:true}).click()
    const intent=await(await registered).json();assert.match(intent.id,/^[a-f0-9-]{36}$/)
    await expect.poll(()=>probe?.snapshot().seen===true).toBe(true)
    assert.equal(observedIntent,intent.id)
    await save('progress.json',{phase:'await-root-partial',scenario,id:intent.id})
    await flag(scenario+'-partial-attested.json',intent.id)
    phase=scenario+'-real-control-after-positive-server-reception'
    if(scenario==='cancel'){
      await upload.getByRole('button',{name:'中止本地操作',exact:true}).click()
    }else{
      const other=await context.newPage();await other.goto(base+'/')
      await other.getByRole('button',{name:'打开用户菜单',exact:true}).click()
      await other.getByRole('menuitem',{name:'退出登录',exact:true}).click()
      await expect(other).toHaveURL(base+'/login',{timeout:30_000})
      await expect(page).toHaveURL(base+'/login?returnUrl=/containers',{timeout:30_000})
      await expect(page.getByRole('region',{name:'容器上传',exact:true})).toHaveCount(0)
      await expect(page.getByLabel('选择容器上传文件',{exact:true})).toHaveCount(0)
    }
    phase=scenario+'-actual-browser-network-terminal-and-independent-worker-proof'
    await expect.poll(()=>probe.snapshot().terminal,{timeout:20_000}).toBe('failed')
    const network=probe.snapshot();assert.ok(network.canceled);assert.equal(network.networkError,'net::ERR_ABORTED')
    assert.equal(bodyRequests,index+1)
    cases.push({scenario,id:intent.id,network})
    await save('results.json',{revision,cases,bodyRequests,fullSsoLogout:false})
    await flag(scenario+'-backend-verified.json',intent.id)
    probe.dispose();probe=null
    if(scenario==='cancel'){
      await upload.getByRole('button',{name:'查询原任务',exact:true}).click()
      await expect(upload.getByText(/^(上传失败|已取消)$/)).toBeVisible()
      await files.screenshot({path:join(fixture.evidence,'after-inflight-local-cancel.png')})
    }
  }
  phase='both-identities-and-old-cookie-reject-after-normal-logout'
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  assert.equal((await context.request.get(base+'/api/v1/auth/me',{headers:{Cookie:cookie}})).status(),401)
  assert.ok([401,403].includes((await context.request.get(origin.origin.replace(':8080',':8081')+'/api/v3/core/users/me/')).status()))
  await save('results.json',{revision,cases,bodyRequests,fullSsoLogout:true})
  await cdp.detach();await context.close()
  console.log(JSON.stringify({browserUploadInterruption:'passed',revision,bodyRequests,fullSsoLogout:true}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_upload_browser_interruption\.mjs:([0-9]+):([0-9]+)/g)].map(value=>({line:Number(value[1]),column:Number(value[2])}))
  console.error(JSON.stringify({browserUploadInterruption:'failed',phase,failureType:error?.constructor?.name??'Error',locations}));process.exitCode=1
  if(activePage&&evidence&&!activePage.isClosed()&&new URL(activePage.url()).pathname==='/containers'){
    const files=activePage.getByRole('region',{name:'容器文件',exact:true})
    if(await files.count()===1)await files.screenshot({path:join(evidence,'failed-file-panel.png')}).catch(()=>{})
  }
}finally{probe?.dispose();if(browser)await browser.close()}
