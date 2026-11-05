// Installed UI only: normal Authentik credentials, real HTTP, no mocked response/session injection.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase = 'startup', browser, evidence, activePage
try {
  const revision = process.argv[2], fixture = JSON.parse(await readFile('/dev/stdin', 'utf8'))
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/); assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080', origin = new URL(base)
  assert.equal(origin.protocol, 'http:'); assert.equal(origin.hostname, '127.0.0.1'); assert.equal(origin.port, '8080')
  const username = process.env.VALIDATION_USERNAME, password = process.env.VALIDATION_PASSWORD
  assert.ok(username && password)
  browser = await chromium.launch({ executablePath: '/opt/google/chrome/chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage()
  activePage = page
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-visible-authentik-login'
  await page.goto(base + '/login?returnUrl=/containers')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === '/containers') break
    const identification = page.locator('ak-stage-identification'), secret = page.locator('ak-stage-password'), consent = page.locator('ak-stage-consent')
    const button = /^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if (!identified && await identification.locator('input[name="uidField"]').isVisible()) {
      await identification.locator('input[name="uidField"]').fill(username)
      await identification.getByRole('button', { name: button }).click(); identified = true
    } else if (!entered && await secret.locator('input[name="password"]').isVisible()) {
      await secret.locator('input[name="password"]').fill(password)
      await secret.getByRole('button', { name: button }).click(); entered = true
    } else if (entered && !consented && await consent.isVisible()) {
      await consent.getByRole('button', { name: button }).click(); consented = true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified && entered); await expect(page).toHaveURL(base + '/containers')
  const profile = await (await context.request.get(base + '/api/v1/auth/me')).json()
  assert.equal(profile.isAdministrator, true)
  const endpoint=`/api/v1/containers/${fixture.containerId}/files/recycles`
  const csrf=await (await context.request.get(base+'/api/v1/auth/csrf')).json();const headers={Origin:origin.origin,[csrf.headerName]:csrf.token}
  let registrations=0,confirmations=0,cancellations=0
  page.on('request',r=>{const path=new URL(r.url()).pathname;if(r.method()==='POST'&&path===endpoint)registrations++;if(r.method()==='POST'&&path.startsWith(endpoint)&&path.endsWith('/confirm'))confirmations++;if(r.method()==='POST'&&path.startsWith(endpoint)&&path.endsWith('/cancel'))cancellations++})
  phase='visible-ready'
  await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme:'dark'})
  await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName);const card=page.locator('.container-card').filter({hasText:fixture.containerName});await card.getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();const files=page.getByRole('region',{name:'容器文件',exact:true});await files.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work');await files.getByRole('button',{name:'前往',exact:true}).click();await files.getByRole('button',{name:'回收 recycle-rpc.bin',exact:true}).click();const dialog=page.getByRole('dialog',{name:'回收容器普通文件',exact:true})
  const registration=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST');await dialog.getByRole('button',{name:'生成文件回收预检',exact:true}).click();const registeredResponse=await registration;assert.equal(registeredResponse.status(),202);const registered=await registeredResponse.json();const key=(await registeredResponse.request().allHeaders())['idempotency-key'];await expect(dialog.getByText('预检完成，尚未移除文件',{exact:true})).toBeVisible({timeout:60000});const ready=await (await context.request.get(base+endpoint+'/'+registered.id)).json();assert.equal(ready.state,'Ready');assert.equal(ready.executionJobId,null);assert.ok(ready.confirmation);await page.screenshot({path:join(evidence,'ready-before-logout.png')})
  const originalExpires=Date.parse(ready.readyExpiresAtUtc), began=performance.now();assert.ok(originalExpires>Date.now()&&originalExpires-Date.now()<=300000)
  const safe=v=>({...v,confirmation:null});await writeFile(join(evidence,'ready-original.json'),JSON.stringify({subject:profile.subject,key,intent:safe(ready)}),{mode:0o600})
  phase='original-normal-dual-logout';await dialog.getByRole('button',{name:'关闭',exact:true}).click();await page.getByRole('dialog',{name:new RegExp('容器详情')}).locator('.el-drawer__close-btn').click();await logoutFromUserMenu(page,context,base)
  const anonymous=[]
  for(const path of [endpoint+'/'+ready.id,endpoint+'/by-key/'+key,'/api/v1/files/container-recycles/recovery','/api/v1/files/container-recycles/recovery/'+ready.id]){const r=await context.request.get(base+path);anonymous.push(r.status());assert.equal(r.status(),401)}
  const oldConfirm=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{...headers,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}});assert.equal(oldConfirm.status(),401)
  phase='same-subject-new-normal-login'
  { await page.goto(base + '/login?returnUrl=/containers')
  await page.getByRole('button', { name: '使用用户名和密码登录', exact: true }).click()
  let identified = false, entered = false, consented = false
  for (let stage = 0; stage < 100; stage++) {
    if (new URL(page.url()).origin === origin.origin && new URL(page.url()).pathname === '/containers') break
    const identification = page.locator('ak-stage-identification'), secret = page.locator('ak-stage-password'), consent = page.locator('ak-stage-consent')
    const button = /^(继续|Continue|Log in|登录|Login|Authorize|授权)$/i
    if (!identified && await identification.locator('input[name="uidField"]').isVisible()) {
      await identification.locator('input[name="uidField"]').fill(username)
      await identification.getByRole('button', { name: button }).click(); identified = true
    } else if (!entered && await secret.locator('input[name="password"]').isVisible()) {
      await secret.locator('input[name="password"]').fill(password)
      await secret.getByRole('button', { name: button }).click(); entered = true
    } else if (entered && !consented && await consent.isVisible()) {
      await consent.getByRole('button', { name: button }).click(); consented = true
    }
    await page.waitForTimeout(250)
  }
  assert.ok(identified && entered); await expect(page).toHaveURL(base + '/containers')
  }
  const newProfile=await (await context.request.get(base+'/api/v1/auth/me')).json();assert.equal(newProfile.subject,profile.subject);assert.equal(newProfile.isAdministrator,true)
  const isolated=[]
  for(const path of [endpoint+'/'+ready.id,endpoint+'/by-key/'+key]){const r=await context.request.get(base+path);isolated.push(r.status());assert.equal(r.status(),404)}
  const freshCsrf=await (await context.request.get(base+'/api/v1/auth/csrf')).json();const newConfirm=await context.request.post(base+endpoint+'/'+ready.id+'/confirm',{headers:{Origin:origin.origin,[freshCsrf.headerName]:freshCsrf.token,'Idempotency-Key':ready.id},data:{planHash:ready.confirmation.planHash,confirmed:true,version:ready.version}});assert.equal(newConfirm.status(),404)
  phase='natural-expiry-observation';let final;const observations=[]
  // The current administrator may inspect recovery metadata, without adopting the original login or confirming it.
  while(true){const response=await context.request.get(base+'/api/v1/files/container-recycles/recovery/'+ready.id);assert.equal(response.status(),200);const value=await response.json();assert.equal(value.id,ready.id);assert.equal(value.readyExpiresAtUtc,ready.readyExpiresAtUtc);assert.equal(value.executionJobId,null);observations.push({elapsedMs:Math.round(performance.now()-began),state:value.state});if(value.state==='Expired'){final=safe(value);break}assert.equal(value.state,'Ready');assert.ok(Date.now()<originalExpires+60000);await page.waitForTimeout(1500)}
  assert.ok(Date.now()>=originalExpires);assert.equal(registrations,1);assert.equal(confirmations,0);assert.equal(cancellations,0)
  phase='new-session-normal-dual-logout';await logoutFromUserMenu(page,context,base)
  await writeFile(join(evidence,'browser.json'),JSON.stringify({revision,containerId:fixture.containerId,subject:profile.subject,key,original:safe(ready),final,anonymous,oldConfirmStatus:oldConfirm.status(),newLoginReads:isolated,newConfirmStatus:newConfirm.status(),observations,elapsedMs:Math.round(performance.now()-began),expiryLateMs:Date.now()-originalExpires,normalDualLogouts:2,registrations,confirmations,cancellations,clockChanged:false,actualUi:true}),{mode:0o600});await context.close();await browser.close();browser=undefined;phase='done';console.log(JSON.stringify({recycleReadyLogout:'passed'}))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_ready_logout.mjs:'))?.trim()
  // Only an already authenticated product page may be captured, never login/credentials.
  if (activePage && evidence && !['startup', 'normal-visible-authentik-login','same-subject-new-normal-login'].includes(phase))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ recycleReadyLogout: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
