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
  async function marker(name,value){await writeFile(join(evidence,name),JSON.stringify(value),{mode:0o600})}
  async function awaitMarker(name,timeout=40000){const deadline=Date.now()+timeout;while(true){try{return JSON.parse(await readFile(join(evidence,name),'utf8'))}catch(error){if(error.code!=='ENOENT')throw error}assert.ok(Date.now()<deadline);await page.waitForTimeout(100)}}
  let registrations=0,confirmations=0,cancellations=0
  page.on('request',r=>{const p=new URL(r.url()).pathname;if(r.method()==='POST'&&p===endpoint)registrations++;if(r.method()==='POST'&&p.startsWith(endpoint)&&p.endsWith('/confirm'))confirmations++;if(r.method()==='POST'&&p.startsWith(endpoint)&&p.endsWith('/cancel'))cancellations++})
  phase='visible-ready';await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme:'dark'});await page.getByRole('textbox',{name:'搜索容器',exact:true}).fill(fixture.containerName);await page.locator('.container-card').filter({hasText:fixture.containerName}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();const files=page.getByRole('region',{name:'容器文件',exact:true});await files.getByRole('textbox',{name:'容器内相对目录',exact:true}).fill('work');await files.getByRole('button',{name:'前往',exact:true}).click();await files.getByRole('button',{name:'回收 recycle-rpc.bin',exact:true}).click();const dialog=page.getByRole('dialog',{name:'回收容器普通文件',exact:true});const accepted=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST');await dialog.getByRole('button',{name:'生成文件回收预检',exact:true}).click();const queued=await accepted;assert.equal(queued.status(),202);const initial=await queued.json();await expect(dialog.getByText('预检完成，尚未移除文件',{exact:true})).toBeVisible({timeout:130000});await marker('armed-intent.json',{id:initial.id,subject:profile.subject});await awaitMarker('observer-armed.json');const confirmed=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+initial.id+'/confirm');await dialog.getByRole('button',{name:'确认文件回收',exact:true}).click();const confirmation=await confirmed;assert.equal(confirmation.status(),202);const pending=await confirmation.json();assert.ok(pending.executionJobId);const cut=await awaitMarker('cut-held.json');assert.equal(cut.id,initial.id);assert.equal(cut.executionJobId,pending.executionJobId);phase='cancel-bound-helper';await page.screenshot({path:join(evidence,'held-before-cancel.png')});await dialog.getByRole('button',{name:'请求取消文件回收',exact:true}).click();await marker('cancel-request-began.json',{id:initial.id});await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(dialog.getByText('已取消',{exact:true})).toBeVisible({timeout:60000});const response=await context.request.get(base+endpoint+'/'+initial.id);assert.equal(response.status(),200);const result=await response.json();assert.equal(result.state,'Canceled');assert.equal(result.receipt.state,'Aborted');assert.equal(result.executionJobId,pending.executionJobId);await page.screenshot({path:join(evidence,'canceled-result.png')});await dialog.getByRole('button',{name:'关闭',exact:true}).click();await page.getByRole('dialog',{name:new RegExp('容器详情')}).locator('.el-drawer__close-btn').click();phase='job-summary';const listing=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs');await page.goto(base+'/jobs');const list=await (await listing).json();const index=list.findIndex(v=>v.id===result.executionJobId);assert.ok(index>=0);await page.locator('.jobs-page .el-table__body tr').nth(index).getByRole('button',{name:'详情',exact:true}).click();const detail=page.getByRole('dialog',{name:'任务详情',exact:true});await expect(detail).toContainText(result.executionJobId);await expect(detail.getByRole('region',{name:'回收安全结果',exact:true})).toContainText('已取消');await page.screenshot({path:join(evidence,'canceled-job.png')});await detail.getByRole('button',{name:'Close this dialog',exact:true}).click();assert.equal(registrations,1);assert.equal(confirmations,1);assert.equal(cancellations,1);phase='visible-dual-logout';await logoutFromUserMenu(page,context,base);await marker('browser.json',{revision,subject:profile.subject,result,registrations,confirmations,cancellations,normalDualLogout:true,actualUi:true});await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({actualRecycleInflightCancel:'passed'}))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recycle_inflight_cancel.mjs:'))?.trim()
  // Only an already authenticated product page may be captured, never login/credentials.
  if (activePage && evidence && !['startup', 'normal-visible-authentik-login'].includes(phase))
    await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ recycleInflightCancel: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
