// Real installed UI and normal Authentik login. No response/Cookie injection or SQL writes.
import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let phase='startup',browser,evidence,activePage
const diagnosticStart=Date.now(),diagnostics=[]
function diagnostic(value){if(diagnostics.length<256)diagnostics.push({elapsedMs:Date.now()-diagnosticStart,phase,...value})}
async function reference(label){
  let value=null
  try{
    const raw=await activePage.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1'))
    if(raw){value=JSON.parse(raw);assert.ok(Array.isArray(value)&&value.length<=4)
      for(const v of value){assert.deepEqual(Object.keys(v).sort(),['containerId','id','key','subject']);assert.ok(typeof v.subject==='string'&&v.subject.length<=256);assert.match(v.containerId,/^[a-f0-9]{64}$/);assert.match(v.key,/^[a-f0-9-]{36}$/);assert.ok(v.id===null||/^[a-f0-9-]{36}$/.test(v.id))}
    }
  }catch{value={invalidOrUnavailable:true}}
  diagnostic({type:'reference',label,path:new URL(activePage.url()).pathname,value})
  await writeFile(join(evidence,'reference-observations.json'),JSON.stringify(diagnostics),{mode:0o600})
  return value
}
process.once('SIGTERM', () => { void (async () => { if (browser) await browser.close().catch(() => {}); process.exit(143) })() })
try {
  const revision=process.argv[2], packet=JSON.parse(await readFile('/dev/stdin','utf8')), fixture=packet.fixture, credentials=packet.credentials
  assert.match(revision, /^[a-f0-9]{40}$/); assert.equal(fixture.revision, revision)
  assert.match(fixture.containerId, /^[a-f0-9]{64}$/); assert.match(fixture.containerName, /^wdv-container-publish-[a-f0-9]{32}$/)
  evidence = fixture.evidence
  const base = process.env.WHALEDECK_URL ?? 'http://127.0.0.1:8080'; assert.equal(base, 'http://127.0.0.1:8080')
  browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 800 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(20_000)
  const page = await context.newPage(); activePage = page
  page.on('response',r=>{if(new URL(r.url()).pathname==='/api/v1/auth/me')diagnostic({type:'identity-response',status:r.status()})})
  page.on('requestfailed',r=>{if(new URL(r.url()).pathname==='/api/v1/auth/me'){const code=r.failure()?.errorText;diagnostic({type:'identity-request-failed',code:/^[A-Z0-9_:.-]{1,80}$/i.test(code??'')?code:'NETWORK_ERROR'})}})
  assert.equal((await (await context.request.get(base + '/api/v1/system/status')).json()).build.sourceRevision, revision)
  phase = 'normal-visible-authentik-login'; const profile = await loginVisible(page, context, base, '/containers', true, credentials.admin)
  const endpoint = `/api/v1/containers/${fixture.containerId}/files/recursive-acls`
  const csrf = await (await context.request.get(base + '/api/v1/auth/csrf')).json()
  const headers = { Origin: base, [csrf.headerName]: csrf.token }
  const basic=[{tag:'user_obj',id:null,permissions:6},{tag:'group_obj',id:null,permissions:4},{tag:'other',id:null,permissions:0}]
  const forbidden={instanceId:fixture.instanceId,path:'work/recursive-tree',aclType:'access',fileEntries:basic,directoryEntries:basic,expectedVersion:fixture.sourceVersion}
  assert.equal((await context.request.post(base + endpoint, { headers: { Origin: base, 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 400)
  assert.equal((await context.request.post(base + endpoint, { headers: { ...headers, Origin: 'http://example.invalid', 'Idempotency-Key': crypto.randomUUID() }, data: forbidden })).status(), 403)
  const missing = { ...forbidden }; delete missing.fileEntries
  const rejected = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: missing })
  assert.equal(rejected.status(), 400); assert.equal((await rejected.json()).code, 'CONTAINER_RECURSIVE_ACL_INPUT_INVALID')
  let registrations = 0, confirmations = 0, uploads = 0
  const headerChecks = [], completed = []
  page.on('request', request => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && (path === endpoint || path.startsWith(endpoint + '/') && path.endsWith('/confirm'))) {
      if (path === endpoint) registrations++; else confirmations++
      headerChecks.push(request.allHeaders().then(h => h.origin === base && !!h['idempotency-key']))
    }
    if (request.method() === 'PUT' && path.includes('/files/')) uploads++
  })
  async function query(id){const r=await context.request.get(base+endpoint+'/'+id);assert.equal(r.status(),200);assert.ok(r.headers()['cache-control']?.includes('no-store'));return r.json()}
  const allCases=[
    {name:'default',kind:'default',changes:2},{name:'access',changes:4},{name:'noop',changes:0},
    {name:'mask',changes:2,mask:6},{name:'root',changes:4,mask:6,id:0,permission:0},
    {name:'default-noop',kind:'default',changes:0},{name:'clear-default',kind:'default',clear:true,changes:2},
    {name:'cancel',mask:6,changes:4,cancel:true},
  ]
  const cases=allCases.filter(item=>fixture.cases.includes(item.name));assert.deepEqual(cases.map(item=>item.name),fixture.cases)
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx']
  async function select(region,label,value){
    const control=region.getByRole('combobox',{name:label,exact:true});await control.scrollIntoViewIfNeeded();await control.press('ArrowDown')
    await page.getByRole('listbox',{name:label,exact:true}).getByRole('option',{name:value,exact:true}).click({timeout:5000})
  }
  async function target(dialog,label,directory,item){
    const region=dialog.getByRole('region',{name:label,exact:true}),values=[directory?7:6,0,0]
    async function permission(n,value){await select(region,`${label} 权限 ${n}`,`${value} · ${bits[value]}`)}
    for(let n=0;n<3;n++)await permission(n+1,values[n])
    await select(region,`${label} 新增条目类型`,'指定用户');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click()
    await region.getByLabel(`${label} user ID 4`,{exact:true}).fill(String(item.id??2201));await permission(4,item.permission??(directory?7:6))
    await select(region,`${label} 新增条目类型`,'mask');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click();await permission(5,directory?7:item.kind==='default'?6:item.mask??4)
  }
  function targets(item){
    function acl(directory=false){return [{tag:'user_obj',id:null,permissions:directory?7:6},{tag:'user',id:item.id??2201,permissions:item.permission??(directory?7:6)},{tag:'group_obj',id:null,permissions:0},{tag:'mask',id:null,permissions:directory?7:item.kind==='default'?6:item.mask??4},{tag:'other',id:null,permissions:0}]}
    return item.kind==='default'?{fileEntries:null,directoryEntries:item.clear?[]:acl()}:{fileEntries:acl(),directoryEntries:acl(true)}
  }
  let index=0
  for (const item of cases) {
    phase=item.name;await page.setViewportSize({width:item.cancel?320:index%2?320:1440,height:600});await page.emulateMedia({colorScheme:item.cancel?'dark':index%2?'dark':'light'});index++
    const files = await openPermissionFiles(page, fixture)
    await files.getByRole('button', { name: '递归ACL recursive-tree', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '递归修改容器目录ACL', exact: true })
    if(item.kind==='default'){
      await select(dialog,'递归ACL类型','default · 仅目录继承ACL')
      if(item.clear){await dialog.getByText('明确清除所有目录的default ACL',{exact:true}).click();await expect(dialog.getByRole('checkbox',{name:'明确清除所有目录的default ACL',exact:true})).toBeChecked()}
      else await target(dialog,'目录目标ACL',false,item)
    }else{await target(dialog,'文件目标ACL',false,item);await target(dialog,'目录目标ACL',true,item)}
    const registration = page.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: '生成递归ACL修改预检', exact: true }).click()
    const response = await registration; assert.equal(response.status(), 202)
    const registered = await response.json(), key = (await response.request().allHeaders())['idempotency-key'], body = response.request().postDataJSON()
    assert.equal(registered.state, 'Queued'); assert.equal(registered.executionJobId, null)
    await expect(dialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 45_000 })
    const ready = await query(registered.id); assert.equal(ready.state, 'Ready'); assert.ok(ready.confirmation)
    assert.deepEqual({fileEntries:ready.command.fileEntries,directoryEntries:ready.command.directoryEntries},targets(item));assert.equal(ready.command.aclType,item.kind??'access')
    assert.deepEqual([ready.entryCount, ready.fileCount, ready.directoryCount, ready.changeCount], [4, 2, 2, item.changes])
    assert.deepEqual([ready.rootUid, ready.rootGid, ready.executorUid, ready.executorGid], [...fixture.original[''].identity.slice(3,5), 1000, 1000])
    const duplicate = await context.request.post(base + endpoint, { headers: { ...headers, 'Idempotency-Key': key }, data: body })
    assert.equal(duplicate.status(), 202); assert.equal((await duplicate.json()).id, registered.id)
    const expectedReference={subject:profile.subject,containerId:fixture.containerId,key,id:ready.id}
    const stored=await reference('before-reload');assert.ok(Array.isArray(stored));assert.deepEqual(stored.find(v=>v.containerId===fixture.containerId),expectedReference)
    await page.reload();await reference('after-reload');await expect(page).toHaveURL(base+'/containers');await openPermissionFiles(page,fixture)
    const restored=await reference('after-files-open');assert.ok(Array.isArray(restored));assert.deepEqual(restored.find(v=>v.containerId===fixture.containerId),expectedReference)
    await page.getByRole('button', { name: '查看递归ACL修改任务', exact: true }).click()
    await expect(dialog.getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible({ timeout: 30_000 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth), true)
    await expect(dialog.getByRole('button', { name: '请求取消递归ACL修改', exact: true })).toBeInViewport({ ratio: 1 })
    await expect(dialog.getByRole('button', { name: '确认递归ACL修改', exact: true })).toBeInViewport({ ratio: 1 })
    await page.screenshot({path:join(evidence,item.name+'-ready.png')})
    const facts=dialog.getByRole('region',{name:'递归ACL计划和实际结果',exact:true})
    async function field(label,value){await expect(facts.getByText(label,{exact:true}).locator('xpath=following-sibling::dd[1]')).toHaveText(String(value))}
    for(const [label,value]of [['预检对象',4],['预计变更',item.changes],['文件',2],['目录（含根）',2]])await field(label,value)
    for(const [label,key]of [['文件目标ACL','fileEntries'],['目录目标ACL','directoryEntries']]){
      const entries=targets(item)[key];if(entries===null)continue
      const region=facts.getByRole('region',{name:label,exact:true}),labels={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
      await expect(region.locator('dt')).toHaveCount(entries.length)
      for(const [n,entry]of entries.entries()){
        await expect(region.locator('dt').nth(n)).toHaveText(labels[entry.tag]+(entry.id===null?'':` ${entry.id}`))
        await expect(region.locator('dd code').nth(n)).toHaveText(bits[entry.permissions])
      }
      if(!entries.length)await expect(region).toContainText('空（无默认ACL）')
      await region.scrollIntoViewIfNeeded();await expect(region).toBeInViewport({ratio:1});await page.screenshot({path:join(evidence,item.name+'-'+key+'-targets.png')})
    }
    let result
    if (item.cancel) {
      await dialog.getByRole('button', { name: '请求取消递归ACL修改', exact: true }).click(); await page.getByRole('button', { name: '继续等待', exact: true }).click()
      assert.equal((await query(ready.id)).state, 'Ready')
      await dialog.getByRole('button', { name: '请求取消递归ACL修改', exact: true }).click(); await page.getByRole('button', { name: '确认请求取消', exact: true }).click()
      await expect(dialog.getByText('已取消', { exact: true })).toBeVisible({ timeout: 30_000 })
      result = await query(ready.id); assert.equal(result.state, 'Canceled'); assert.equal(result.executionJobId, null); assert.equal(result.receipt, null)
    } else {
      await dialog.getByRole('button', { name: '确认递归ACL修改', exact: true }).click()
      await expect(dialog.getByText('递归ACL修改已完成', { exact: true })).toBeVisible({ timeout: 45_000 })
      result = await query(ready.id); assert.equal(result.state, 'Succeeded'); assert.equal(result.receipt.state, 'Completed')
      assert.deepEqual([result.receipt.completedCount, result.receipt.changedCount, result.receipt.unknownCount], [4, item.changes, 0])
      const retry = await context.request.post(base + endpoint + '/' + ready.id + '/confirm', { headers: { ...headers, 'Idempotency-Key': ready.id }, data: { planHash: ready.confirmation.planHash, confirmed: true, version: ready.version } })
      assert.equal(retry.status(), 202); assert.equal((await retry.json()).executionJobId, result.executionJobId)
    }
    if(!item.cancel){
      for(const [label,value]of [['已完成核对',4],['实际修改',item.changes],['已中止',0],['未开始',0],['待证明',0]])await field(label,value)
      const counts=facts.locator('dl[aria-label="实际逐项结果"]');await counts.scrollIntoViewIfNeeded();await expect(counts).toBeInViewport({ratio:1})
      await expect(facts).toContainText('已验证留存');await expect(facts).toContainText(/HDD元数据\s*可用/)
    }
    await page.screenshot({path:join(evidence,item.name+'-result.png')})
    await reference('after-result')
    assert.equal(result.confirmation, null); completed.push({ name: item.name, ...result })
    // The next mode change is forbidden until a separate parent reads physical bytes/metadata and SQL/Root/HDD.
    await writeSignal(evidence, item.name + '-result.json', { subject: profile.subject, result: completed.at(-1) })
    const proof = await waitSignal(evidence, item.name + '-verified.json'); assert.equal(proof.id, result.id); assert.equal(proof.physicalAndHistoryVerified, true)

    await closePermissionDetails(page, dialog)
  }
  assert.equal(registrations,cases.length);assert.equal(confirmations,cases.length-1); assert.equal(uploads, 0); assert.ok((await Promise.all(headerChecks)).every(Boolean))
  phase='visible-dual-logout';const adminLogout=await logoutFromUserMenu(page,context,base)
  await writeFile(join(evidence,'admin-logout.json'),JSON.stringify(adminLogout),{mode:0o600})
  const paths=[endpoint+'/'+completed[0].id,endpoint+'/by-key/'+crypto.randomUUID(),'/api/v1/files/container-recursive-acls/recovery','/api/v1/files/container-recursive-acls/recovery/'+completed[0].id]
  for(const path of paths)assert.equal((await context.request.get(base+path)).status(),401)
  phase='ordinary-visible-login';const ordinaryContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'}),ordinaryPage=await ordinaryContext.newPage();activePage=ordinaryPage;ordinaryContext.setDefaultTimeout(20_000)
  const ordinaryProfile=await loginVisible(ordinaryPage,ordinaryContext,base,'/',false,credentials.ordinary)
  let pagePrivateRequests=0;ordinaryPage.on('request',r=>{if(new URL(r.url()).pathname.includes('recursive-acls'))pagePrivateRequests++})
  await ordinaryPage.goto(base+'/files');await expect(ordinaryPage).toHaveURL(base+'/forbidden');assert.equal(pagePrivateRequests,0)
  await ordinaryPage.screenshot({path:join(evidence,'ordinary-denied.png')})
  for(const path of paths)assert.equal((await ordinaryContext.request.get(base+path)).status(),403)
  const ordinaryCsrf=await(await ordinaryContext.request.get(base+'/api/v1/auth/csrf')).json(),ordinaryHeaders={Origin:base,[ordinaryCsrf.headerName]:ordinaryCsrf.token,'Idempotency-Key':completed[0].id}
  for(const path of [endpoint,endpoint+'/'+completed[0].id+'/confirm',endpoint+'/'+completed[0].id+'/cancel','/api/v1/files/container-recursive-acls/recovery/'+completed[0].id+'/acknowledge'])assert.equal((await ordinaryContext.request.post(base+path,{headers:ordinaryHeaders,data:path===endpoint?forbidden:{version:completed[0].version,confirmed:true,planHash:'not-authority'}})).status(),403)
  await writeFile(join(evidence,'ordinary-denials.json'),JSON.stringify({subject:ordinaryProfile.subject,deniedGet:4,deniedPost:4,pagePrivateRequests}),{mode:0o600})
  await ordinaryPage.getByRole('button',{name:'首页',exact:true}).click();await expect(ordinaryPage).toHaveURL(base+'/')
  phase='ordinary-visible-dual-logout';const ordinaryLogout=await logoutFromUserMenu(ordinaryPage,ordinaryContext,base)
  for(const path of paths)assert.equal((await ordinaryContext.request.get(base+path)).status(),401)
  const ordinary={subject:ordinaryProfile.subject,deniedGet:4,deniedPost:4,pagePrivateRequests,logout:ordinaryLogout};await ordinaryContext.close()
  await writeFile(join(evidence,'browser.json'),JSON.stringify({revision,containerId:fixture.containerId,subject:profile.subject,completed,registrations,confirmations,registrationRetries:cases.length,confirmationRetries:cases.length-1,contentUploads:uploads,actualUi:true,normalDualLogout:true,logout:adminLogout,ordinary,anonymousDenied:4}),{mode:0o600})
  await context.close(); await browser.close(); browser = undefined
  console.log(JSON.stringify({ recursiveAclBrowser: 'passed', actualUi:true,cases:cases.length }))
} catch (error) {
  const frame = error?.stack?.split('\n').find(line => line.includes('validate_container_recursive_acl_browser.mjs:'))?.trim()
  if(activePage&&evidence&&!phase.includes('login')&&!phase.includes('logout')){
    await reference('failure').catch(()=>{})

  }
  if (activePage && evidence && phase!=='startup'&&!phase.includes('login')&&!phase.includes('logout')) await activePage.screenshot({ path: join(evidence, 'failed-product-page.png') }).catch(() => {})
  console.error(JSON.stringify({ recursiveAclBrowser: 'failed', phase, failureType: error?.name, frame }))
  if (browser) await browser.close().catch(() => {})
  process.exitCode = 1
}
