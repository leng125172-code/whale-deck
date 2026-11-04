// Two independent real Authentik contexts, product controls and stdin credentials.
import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {registerWaitingDirectory} from './recursive_acl_cross_directory.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'

let browser,page,context,directory,id,endpoint,phase='startup'
const role=process.argv[3]
const observeLogout=value=>writeSignal(directory,role+'-logout-'+value.phase+'.json',value)
try{
  const packet=JSON.parse(await readFile('/dev/stdin','utf8')),f=packet.fixture,revision=process.argv[2],base='http://127.0.0.1:8080'
  assert.match(revision,/^[a-f0-9]{40}$/);assert.equal(f.revision,revision);assert.ok(['owner','recovery'].includes(role))
  const kind=f.kind??'cancel';assert.ok(['cancel','logout','group-removal','account-disable','worker-crash','agent-restart','target-removed','instance-changed'].includes(kind));const revoked=['group-removal','account-disable'].includes(kind),unknown=['target-removed','instance-changed'].includes(kind),expectedState=unknown?'Unknown':'Partial'
  directory=f.evidence;assert.match(directory,/\/deploy\/artifacts\/container-recursive-acl-partial-[a-f0-9]{32}$/)
  browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  context=await browser.newContext({viewport:{width:320,height:600},colorScheme:role==='owner'?'dark':'light',reducedMotion:'reduce'});context.setDefaultTimeout(20_000);page=await context.newPage()
  assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  phase='normal-visible-login';const profile=await loginVisible(page,context,base,role==='owner'||f.waitingDirectory?'/containers':'/files',true,packet.credentials)
  endpoint=`/api/v1/containers/${f.containerId}/files/recursive-acls`
  let registrations=0,confirmations=0,cancellations=0,acknowledgements=0,uploads=0
  page.on('request',r=>{const p=new URL(r.url()).pathname;if(r.method()==='POST'){
    if(p===endpoint)registrations++
    if(p.startsWith(endpoint+'/')&&p.endsWith('/confirm'))confirmations++
    if(p.startsWith(endpoint+'/')&&p.endsWith('/cancel'))cancellations++
    if(p.startsWith('/api/v1/files/container-recursive-acls/recovery/')&&p.endsWith('/acknowledge'))acknowledgements++
  }if(r.method()==='PUT'&&p.includes('/files/'))uploads++})
  async function counts(region,values){const facts=region.getByLabel('实际逐项结果',{exact:true});for(const[label,key]of [['已完成核对','completedCount'],['实际修改','changedCount'],['已中止','abortedCount'],['未开始','notStartedCount'],['待证明','unknownCount']])await expect(facts.getByText(label,{exact:true}).locator('..').locator('dd')).toHaveText(String(values[key]));await facts.scrollIntoViewIfNeeded();await expect(facts).toBeInViewport({ratio:1})}
  let waitingDirectory=null,showWaitingDirectory=null,result=null,logout=null,hintsCleared=false,originalIntentStatus=null,currentIdentityStatus=null,currentSubject=null,currentlyAdministrator=null,renderedRevocationPage=null,privatePanelsAbsent=false
  if(role==='owner'){
    const files=await openPermissionFiles(page,f);await files.getByRole('button',{name:'递归ACL recursive-tree',exact:true}).click()
    const dialog=page.getByRole('dialog',{name:'递归修改容器目录ACL',exact:true}),bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx']
    async function target(label,dir){
      const region=dialog.getByRole('region',{name:label,exact:true})
      async function select(name,value){const c=region.getByRole('combobox',{name,exact:true});await c.scrollIntoViewIfNeeded();await c.press('ArrowDown');await page.getByRole('listbox',{name,exact:true}).getByRole('option',{name:value,exact:true}).click()}
      async function perm(i,n){await select(`${label} 权限 ${i}`,`${n} · ${bits[n]}`)}
      for(const[i,n]of [dir?7:6,0,0].entries())await perm(i+1,n)
      await select(`${label} 新增条目类型`,'指定用户');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click();await region.getByLabel(`${label} user ID 4`,{exact:true}).fill('2201');await perm(4,dir?7:6)
      await select(`${label} 新增条目类型`,'mask');await region.getByRole('button',{name:`添加${label}条目`,exact:true}).click();await perm(5,dir?7:4)
    }
    phase='actual-preflight';await target('文件目标ACL',false);await target('目录目标ACL',true)
    const registering=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='POST');await dialog.getByRole('button',{name:'生成递归ACL修改预检',exact:true}).click()
    const registered=await registering;assert.equal(registered.status(),202);id=(await registered.json()).id;await writeSignal(directory,'registered.json',{id,subject:profile.subject})
    await expect(dialog.getByRole('status')).toHaveText('预检已完成，尚未修改ACL',{timeout:45_000});await writeSignal(directory,'ready.json',{id,subject:profile.subject,permissionExpiresAtUtc:profile.permissionExpiresAtUtc})
    await waitSignal(directory,'confirm.json',30_000);phase='actual-confirm'
    const confirming=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+id+'/confirm'&&r.request().method()==='POST');await dialog.getByRole('button',{name:'确认递归ACL修改',exact:true}).click()
    const confirmed=await confirming;assert.equal(confirmed.status(),202);const pending=await confirmed.json();assert.equal(pending.state,'Pending');await writeSignal(directory,'confirmed.json',pending)
    await waitSignal(directory,'action.json',40_000);phase='actual-partial-cut'
    await dialog.getByRole('button',{name:'查询原递归ACL修改任务',exact:true}).click();await expect(dialog.getByRole('status')).toHaveText('正在逐项修改ACL')
    await dialog.getByRole('status').scrollIntoViewIfNeeded();await expect(dialog.getByRole('status')).toBeInViewport({ratio:1});await page.screenshot({path:join(directory,'inflight.png')})
    if(unknown){await writeSignal(directory,'action-done.json',{id,subject:profile.subject});await waitSignal(directory,'logout.json',40_000)}
    if(kind==='cancel'){
      const cancel=dialog.getByRole('button',{name:'请求取消递归ACL修改',exact:true});await cancel.click();await page.getByRole('button',{name:'继续等待',exact:true}).click();assert.equal(cancellations,0)
      await cancel.click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
    }else if(kind==='logout'||unknown){
      phase='inflight-normal-visible-logout';await closePermissionDetails(page,dialog);logout=await logoutFromUserMenu(page,context,base,undefined,observeLogout);await writeSignal(directory,'owner-logout.json',logout)
      await expect(page.getByRole('region',{name:'容器递归ACL修改任务',exact:true})).toHaveCount(0)
      await expect.poll(()=>page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1'))).toBe(null);hintsCleared=true
      await expect(page.getByRole('status',{name:'正在启动 WhaleDeck 协同平台',exact:true})).toHaveCount(0)
      await expect(page.getByRole('heading',{name:'使用工作站账号登录',exact:true})).toBeVisible()
      await page.screenshot({path:join(directory,'owner-logged-out.png')})
    }
    await writeSignal(directory,unknown?'logout-done.json':'action-done.json',{id,subject:profile.subject})
    const final=await waitSignal(directory,'settled.json');assert.equal(final.id,id);assert.equal(final.state,expectedState)
    if(revoked){
      phase='original-revoked-context-denied';originalIntentStatus=(await context.request.get(base+endpoint+'/'+id)).status();assert.ok([401,403].includes(originalIntentStatus))
      const me=await context.request.get(base+'/api/v1/auth/me');currentIdentityStatus=me.status()
      if(kind==='group-removal'){
        assert.equal(me.status(),200);const value=await me.json();assert.equal(value.subject,profile.subject);assert.equal(value.isAdministrator,false);currentSubject=value.subject;currentlyAdministrator=false
      }else assert.ok([401,403].includes(me.status()))
      await page.reload();await expect(page.getByRole('status',{name:'正在启动 WhaleDeck 协同平台',exact:true})).toHaveCount(0)
      if(kind==='group-removal'){
        await expect(page).toHaveURL(base+'/forbidden');await expect(page.getByRole('heading',{name:'没有管理权限',exact:true})).toBeVisible();renderedRevocationPage='forbidden'
      }else{
        await expect(page).toHaveURL(url=>url.origin===base&&url.pathname==='/login');await expect(page.getByRole('heading',{name:'使用工作站账号登录',exact:true})).toBeVisible();renderedRevocationPage='login'
      }
      await expect(page.getByRole('region',{name:'容器递归ACL修改任务',exact:true})).toHaveCount(0);await expect(page.getByRole('dialog',{name:'递归修改容器目录ACL',exact:true})).toHaveCount(0);privatePanelsAbsent=true
      await expect.poll(()=>page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1'))).toBe(null);hintsCleared=true
      assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await page.screenshot({path:join(directory,'authority-denied.png')})
    }
    if(['cancel','worker-crash','agent-restart'].includes(kind)){
    phase='actual-partial-result';await dialog.getByRole('button',{name:'查询原递归ACL修改任务',exact:true}).click();await expect(dialog.getByRole('status')).toHaveText('部分修改，保护已保留')
    const response=await context.request.get(base+endpoint+'/'+id);assert.equal(response.status(),200);assert.ok(response.headers()['cache-control']?.includes('no-store'));result=await response.json()
    assert.equal(result.state,'Partial');assert.equal(result.receipt.state,'Partial');assert.equal(result.confirmation,null);assert.deepEqual(result.receipt,final.receipt)
    await counts(dialog,result.receipt);await page.screenshot({path:join(directory,'partial-counts.png')})
    await expect(dialog.getByRole('button',{name:'确认递归ACL修改',exact:true})).toHaveCount(0)
    await closePermissionDetails(page,dialog);phase='normal-visible-logout';logout=await logoutFromUserMenu(page,context,base,undefined,observeLogout)
    }
    if(!revoked)assert.equal((await context.request.get(base+endpoint+'/'+id)).status(),401)
  }else{
    id=f.id;assert.notEqual(profile.subject,f.originalSubject);phase='different-admin-recovery'
    assert.equal((await context.request.get(base+endpoint+'/'+id)).status(),404)
    if(f.waitingDirectory)showWaitingDirectory=await registerWaitingDirectory(page,base,f,profile)
    await page.getByRole('button',{name:'容器递归ACL修改异常',exact:true}).click();const dialog=page.getByRole('dialog',{name:'容器递归ACL修改异常处置',exact:true})
    const row=dialog.locator('.recursive-acl-recovery__row').filter({hasText:id});await expect(row).toHaveCount(1);await expect(row).toContainText(unknown?'结果未知，保护已保留':'部分修改，保护已保留');await counts(row,f.receipt);await page.screenshot({path:join(directory,'recovery-counts.png')})
    await row.getByRole('button',{name:'处置历史结果',exact:true}).click();await expect(dialog.getByRole('button',{name:'暂不处置',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'确认处置并释放保护',exact:true})).toBeInViewport({ratio:1})
    await page.screenshot({path:join(directory,'recovery-confirm.png')});await dialog.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(acknowledgements,0)
    await writeSignal(directory,'recovery-canceled.json',{id,subject:profile.subject,acknowledgements});await waitSignal(directory,'acknowledge.json',f.waitingDirectory?120_000:40_000)
    await row.getByRole('button',{name:'处置历史结果',exact:true}).click();const ack=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/files/container-recursive-acls/recovery/'+id+'/acknowledge'&&r.request().method()==='POST')
    await dialog.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();assert.equal((await ack).status(),202)
    await expect(dialog.getByRole('status')).toHaveText('已处置部分修改或未知历史结果并释放保护；不代表整树修改成功，不会续做或自动恢复ACL，不删除原对象。')
    await page.screenshot({path:join(directory,'acknowledged.png')});const response=await context.request.get(base+'/api/v1/files/container-recursive-acls/recovery/'+id);assert.equal(response.status(),200);assert.ok(response.headers()['cache-control']?.includes('no-store'));result=await response.json()
    assert.equal(result.state,'Acknowledged');const receiptFacts=r=>Object.fromEntries(Object.entries(r).filter(([k])=>!['replayed','bindingCurrent','historyAvailable'].includes(k)));assert.deepEqual(receiptFacts(result.receipt),receiptFacts(f.receipt))
    await writeSignal(directory,'recovery-before-logout.json',{id,subject:profile.subject,result,actualUi:true,normalDualLogoutProved:false,registrations,confirmations,cancellations,acknowledgements,uploads,kind})
    await dialog.getByRole('button',{name:'关闭',exact:true}).click()
    if(showWaitingDirectory){
      await writeSignal(directory,'waiting-acknowledged.json',{id,subject:profile.subject});const final=await waitSignal(directory,'waiting-finished.json',120_000);waitingDirectory=await showWaitingDirectory(context,final)
    }
    phase='normal-visible-logout';logout=await logoutFromUserMenu(page,context,base,undefined,observeLogout)
    assert.equal((await context.request.get(base+'/api/v1/files/container-recursive-acls/recovery/'+id)).status(),401)
    if(waitingDirectory){waitingDirectory.anonymousStatus=(await context.request.get(base+waitingDirectory.privateEndpoint)).status();assert.equal(waitingDirectory.anonymousStatus,401);delete waitingDirectory.privateEndpoint}
  }
  assert.equal(registrations,role==='owner'?1:0);assert.equal(confirmations,role==='owner'?1:0);assert.equal(cancellations,role==='owner'&&kind==='cancel'?1:0);assert.equal(acknowledgements,role==='recovery'?1:0);assert.equal(uploads,0)
  await writeSignal(directory,role+'-browser.json',{id,subject:profile.subject,result,logout,actualUi:true,normalDualLogout:role==='recovery'||!revoked,registrations,confirmations,cancellations,acknowledgements,uploads,kind,hintsCleared,waitingDirectory,
    authorityRevoked:role==='owner'&&revoked,anonymousGet401:role==='recovery'||!revoked,originalIntentStatus,currentIdentityStatus,currentSubject,currentlyAdministrator,renderedRevocationPage,privatePanelsAbsent})
  console.log(JSON.stringify({recursiveAclPartialBrowser:'passed',role}))
}catch(error){
  const locations=[...(error?.stack??'').matchAll(/validate_container_recursive_acl_partial\.mjs:([0-9]+):([0-9]+)/g)].map(m=>({line:Number(m[1]),column:Number(m[2])}))
  if(page&&directory&&!phase.includes('login')&&!phase.includes('logout'))await page.screenshot({path:join(directory,'failed-'+role+'.png')}).catch(()=>{})
  console.error(JSON.stringify({recursiveAclPartialBrowser:'failed',role,phase,failureType:error?.name,locations}))
  if(directory)await writeSignal(directory,role+'-failed.json',{phase,failureType:error?.name,locations}).catch(()=>{})
  // No implicit ACK on failure; preserve unresolved history and protection.
  process.exitCode=1
}finally{if(browser)await browser.close()}
