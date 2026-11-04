// Installed product UI, real Authentik, stdin-only credentials. No response hooks.
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium, expect } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible, openPermissionFiles, closePermissionDetails, writeSignal, waitSignal } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

let browser, page, context, directory, endpoint, id, phase='startup'
try {
  const packet=JSON.parse(await readFile('/dev/stdin','utf8')), f=packet.fixture, revision=process.argv[2]
  assert.match(revision,/^[a-f0-9]{40}$/);assert.equal(f.revision,revision)
  assert.ok(['cancel','logout'].includes(f.kind));directory=f.evidence
  assert.match(directory,/\/deploy\/artifacts\/container-recursive-acl-faults-[a-f0-9]{32}\/(cancel|logout)$/)
  const base='http://127.0.0.1:8080'
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  context=await browser.newContext({viewport:{width:f.kind==='cancel'?320:1440,height:600},colorScheme:f.kind==='cancel'?'dark':'light',reducedMotion:'reduce'})
  context.setDefaultTimeout(20_000);page=await context.newPage()
  assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  phase='normal-visible-login';const profile=await loginVisible(page,context,base,'/containers',true,packet.credentials)
  endpoint=`/api/v1/containers/${f.containerId}/files/recursive-acls`
  let registrations=0,confirmations=0,cancellations=0,uploads=0
  page.on('request',r=>{const p=new URL(r.url()).pathname
    if(r.method()==='POST'&&p===endpoint)registrations++
    if(r.method()==='POST'&&id&&p===endpoint+'/'+id+'/confirm')confirmations++
    if(r.method()==='POST'&&id&&p===endpoint+'/'+id+'/cancel')cancellations++
    if(r.method()==='PUT'&&p.includes('/files/'))uploads++
  })
  const files=await openPermissionFiles(page,f)
  await files.getByRole('button',{name:'递归ACL recursive-tree',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'递归修改容器目录ACL',exact:true})
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx']
  async function target(label,directory){
    const region=dialog.getByRole('region',{name:label,exact:true})
    async function select(name,value){const c=region.getByRole('combobox',{name,exact:true});await c.scrollIntoViewIfNeeded();await c.press('ArrowDown');await page.getByRole('listbox',{name,exact:true}).getByRole('option',{name:value,exact:true}).click()}
    async function permission(i,n){await select(`${label} 权限 ${i}`,`${n} · ${bits[n]}`)}
    for(const [i,n]of [directory?7:6,0,0].entries())await permission(i+1,n)
    await select(`${label} 新增条目类型`,'指定用户');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click()
    await region.getByLabel(`${label} user ID 4`,{exact:true}).fill('2201');await permission(4,directory?7:6)
    await select(`${label} 新增条目类型`,'mask');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click();await permission(5,directory?7:4)
  }
  phase='visible-preflight';await target('文件目标ACL',false);await target('目录目标ACL',true)
  const registering=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST')
  await dialog.getByRole('button',{name:'生成递归ACL修改预检',exact:true}).click()
  const registered=await registering;assert.equal(registered.status(),202);id=(await registered.json()).id
  await writeSignal(directory,'registered.json',{id,subject:profile.subject})
  await expect(dialog.getByRole('status')).toHaveText('预检已完成，尚未修改ACL',{timeout:45_000})
  const ready=await(await context.request.get(base+endpoint+'/'+id)).json()
  assert.equal(ready.state,'Ready');assert.deepEqual([ready.entryCount,ready.fileCount,ready.directoryCount,ready.changeCount],[4,2,2,4])
  await writeSignal(directory,'ready.json',{id,subject:profile.subject,permissionExpiresAtUtc:profile.permissionExpiresAtUtc})
  await waitSignal(directory,'confirm.json',30_000)
  phase='visible-confirmation';const confirming=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+id+'/confirm'&&r.request().method()==='POST')
  await dialog.getByRole('button',{name:'确认递归ACL修改',exact:true}).click()
  const response=await confirming;assert.equal(response.status(),202);const pending=await response.json()
  assert.equal(pending.state,'Pending');assert.ok(pending.executionJobId);await writeSignal(directory,'confirmed.json',pending)
  assert.equal((await waitSignal(directory,'action.json',40_000)).kind,f.kind)
  phase='visible-inflight';await dialog.getByRole('button',{name:'查询原递归ACL修改任务',exact:true}).click()
  await expect(dialog.getByRole('status')).toHaveText('正在逐项修改ACL')
  await dialog.getByRole('status').scrollIntoViewIfNeeded();await expect(dialog.getByRole('status')).toBeInViewport({ratio:1})
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth))
  await page.screenshot({path:join(directory,'inflight.png')})
  let logout
  if(f.kind==='cancel'){
    phase='visible-cancel';const cancel=dialog.getByRole('button',{name:'请求取消递归ACL修改',exact:true});await expect(cancel).toBeInViewport({ratio:1})
    await cancel.click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal(cancellations,0)
    await cancel.click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
  }else{
    phase='normal-visible-logout';await closePermissionDetails(page,dialog);logout=await logoutFromUserMenu(page,context,base)
    await writeSignal(directory,'logout.json',logout)
    assert.equal(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1')),null)
  }
  await writeSignal(directory,'action-done.json',{kind:f.kind,id,subject:profile.subject,registrations,confirmations,cancellations,uploads})
  const final=await waitSignal(directory,'settled.json');assert.equal(final.id,id);assert.equal(final.state,'Canceled')
  let terminal=null
  if(f.kind==='cancel'){
    phase='visible-terminal';await dialog.getByRole('button',{name:'查询原递归ACL修改任务',exact:true}).click()
    await expect(dialog.getByRole('status')).toHaveText('已取消');await dialog.getByRole('status').scrollIntoViewIfNeeded();await expect(dialog.getByRole('status')).toBeInViewport({ratio:1})
    const r=await context.request.get(base+endpoint+'/'+id);assert.equal(r.status(),200);assert.ok(r.headers()['cache-control']?.includes('no-store'));terminal=await r.json()
    assert.equal(terminal.state,'Canceled');assert.equal(terminal.receipt.state,'Aborted');assert.equal(terminal.receipt.changedCount,0);assert.equal(terminal.receipt.unknownCount,0)
    await page.screenshot({path:join(directory,'terminal.png')});await closePermissionDetails(page,dialog)
    phase='normal-visible-logout';logout=await logoutFromUserMenu(page,context,base);await writeSignal(directory,'logout.json',logout)
  }
  assert.equal(registrations,1);assert.equal(confirmations,1);assert.equal(cancellations,f.kind==='cancel'?1:0);assert.equal(uploads,0)
  assert.equal((await context.request.get(base+endpoint+'/'+id)).status(),401)
  await writeSignal(directory,'browser.json',{revision,kind:f.kind,id,executionJobId:pending.executionJobId,subject:profile.subject,actualUi:true,normalDualLogout:true,logout,registrations,confirmations,cancellations,uploads,anonymousGet401:true,terminal})
  console.log(JSON.stringify({recursiveAclFaultBrowser:'passed',kind:f.kind}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_recursive_acl_faults\.mjs:([0-9]+):([0-9]+)/g)].map(m=>({line:Number(m[1]),column:Number(m[2])}))
  if(page&&directory&&!phase.includes('login')&&!phase.includes('logout'))await page.screenshot({path:join(directory,'failed-product-page.png')}).catch(()=>{})
  console.error(JSON.stringify({recursiveAclFaultBrowser:'failed',phase,failureType:error?.name,locations}))
  if(directory)await writeSignal(directory,'browser-failed.json',{phase,failureType:error?.name,locations}).catch(()=>{})
  // Failure cleanup uses only the exact original registration and login, never a replay.
  if(context&&id&&endpoint)try{
    const r=await context.request.get('http://127.0.0.1:8080/api/v1/auth/csrf')
    if(r.status()===200){const c=await r.json();const canceled=await context.request.post('http://127.0.0.1:8080'+endpoint+'/'+id+'/cancel',{headers:{Origin:'http://127.0.0.1:8080',[c.headerName]:c.token,'Idempotency-Key':id}});await writeSignal(directory,'failure-cancellation.json',{id,status:canceled.status(),gatePassed:false})}
  }catch{}
  process.exitCode=1
}finally{if(browser)await browser.close()}
