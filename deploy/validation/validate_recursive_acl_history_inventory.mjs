// Actual 26-capture inventory: network continuation and local 25-card paging.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
let phase='startup',browser,page,evidence
function proof(r){const u=new URL(r.url());assert.equal(new Set(u.searchParams.keys()).size,[...u.searchParams.keys()].length);return {method:r.request().method(),pathname:u.pathname,query:Object.fromEntries(u.searchParams),status:r.status(),noStore:(r.headers()['cache-control']??'').includes('no-store')}}
async function denied(context,urls,status){const values=[];for(const u of urls)values.push((await context.request.get(u)).status());assert.deepEqual(values,[status,status]);return values}
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const packet=JSON.parse(await readFile('/dev/stdin','utf8')),fixture=packet.fixture,credentials=packet.credentials
 const base='http://127.0.0.1:8080',endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`
 assert.equal(process.argv[2],fixture.revision);assert.equal(fixture.captures.length,26);assert.deepEqual(fixture.captures,[...new Set(fixture.captures)].sort());evidence=fixture.evidence
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true});const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(30_000);page=await context.newPage()
 phase='admin-visible-login';const admin=await loginVisible(page,context,base,'/containers',true,credentials.admin)
 assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,fixture.revision)
 const files=await openPermissionFiles(page,fixture),dialog=page.getByRole('dialog',{name:'递归ACL历史',exact:true}),requests=[],pages=[],signals=[]
 const record=r=>{if(new URL(r.url()).pathname.startsWith(endpoint))requests.push({method:r.method(),pathname:new URL(r.url()).pathname})};page.on('request',record)
 const list=dialog.getByRole('list',{name:'递归ACL历史记录',exact:true})
 async function visible(captures){
  await expect(list.locator(':scope > li')).toHaveCount(captures.length)
  const buttons=list.getByRole('button',{name:/^查看逐项前像 /});await expect(buttons).toHaveCount(captures.length)
  const values=await buttons.evaluateAll(nodes=>nodes.map(n=>n.getAttribute('aria-label').replace('查看逐项前像 ','')));assert.deepEqual(values,captures)
  for(const [index,capture]of captures.entries()){
   const entry=pages.flatMap(p=>p.page.entries).find(e=>e.captureId===capture);assert.ok(entry)
   const card=list.locator(':scope > li').nth(index);await expect(card).toContainText('访问ACL');await expect(card).toContainText('原计划4项');await expect(card).toContainText('文件2');await expect(card).toContainText('目录2');await expect(card).toContainText('原计划变更'+entry.changeCount)
  }
  if(captures.length)await buttons.last().scrollIntoViewIfNeeded();return values
 }
 async function signal(label,visibleCaptures,extra={}){
  phase='inventory-'+label;await expect(dialog.getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
  const screenshot='inventory-'+label+'.png';await page.screenshot({path:join(evidence,screenshot)});const value={subject:admin.subject,label,pages:[...pages],visibleCaptures,screenshot,...extra};signals.push(value)
  await writeSignal(evidence,'inventory-'+label+'-result.json',value);assert.equal((await waitSignal(evidence,'inventory-'+label+'-verified.json')).originalsAndAuditVerified,true)
 }
 phase='first-network-page';let reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET');await files.getByRole('button',{name:'递归ACL历史 recursive-tree',exact:true}).click()
 let response=await reading;assert.equal(response.status(),200);let value=await response.json();pages.push({request:proof(response),page:value});assert.equal(value.complete,false);assert.ok(value.entries.length<=25)
 await expect(dialog.getByRole('button',{name:'继续扫描',exact:true})).toBeInViewport({ratio:1});await signal('first',await visible(fixture.captures.slice(0,value.entries.length)))
 while(!value.complete){
  assert.ok(pages.length<100);phase='continue-network-page';reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET');await dialog.getByRole('button',{name:'继续扫描',exact:true}).click()
  response=await reading;assert.equal(response.status(),200);value=await response.json();pages.push({request:proof(response),page:value})
 }
 assert.deepEqual(pages.flatMap(p=>p.page.entries.map(v=>v.captureId)),fixture.captures)
 assert.equal(requests.length,pages.length);await expect(dialog.getByRole('button',{name:'继续扫描',exact:true})).toHaveCount(0);await signal('last',await visible(fixture.captures.slice(25)))
 await page.setViewportSize({width:1440,height:600});await page.emulateMedia({colorScheme:'light'})
 await dialog.getByRole('button',{name:'上一页历史',exact:true}).click();await signal('back',await visible(fixture.captures.slice(0,25)));assert.equal(requests.length,pages.length)
 await dialog.getByRole('button',{name:'下一页已读历史',exact:true}).click();await signal('next',await visible(fixture.captures.slice(25)));assert.equal(requests.length,pages.length)
 const capture=fixture.captures.at(-1);phase='selected-original-preimage';reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+capture&&r.request().method()==='GET');await dialog.getByRole('button',{name:'查看逐项前像 '+capture,exact:true}).click()
 response=await reading;assert.equal(response.status(),200);value=await response.json();assert.equal(value.summary.captureId,capture);assert.equal(value.entries.length,4);assert.equal(value.complete,true)
 const details=dialog.getByRole('region',{name:'递归ACL历史详情',exact:true});await expect(details).toContainText(value.summary.historyDigest);await expect(details.getByRole('article')).toHaveCount(4)
 for(const entry of value.entries){
  const article=details.getByRole('article',{name:'原ACL '+(entry.relativePath||'根目录'),exact:true});await article.getByRole('heading',{name:entry.relativePath||'根目录',exact:true}).scrollIntoViewIfNeeded();await expect(article).toContainText('原模式 '+entry.originalMode.toString(8).padStart(4,'0'))
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx'],names={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
  for(const kind of ['access','default']){
   const region=article.getByRole('region',{name:`历史${kind} ACL`,exact:true}),acl=entry[kind+'Acl'];await expect(region.locator('li')).toHaveCount(acl.length)
   for(const [n,e]of acl.entries()){
    await expect(region.locator('li').nth(n)).toContainText(names[e.tag]+(e.id===null?'':` ${e.id}`));await expect(region.locator('li').nth(n)).toContainText(`权限 ${e.permissions} · ${bits[e.permissions]}`)
    const mask=acl.find(v=>v.tag==='mask')?.permissions??7,effective=['user','group_obj','group'].includes(e.tag)?e.permissions&mask:e.permissions;await expect(region.locator('li').nth(n)).toContainText(`有效 ${bits[effective]}`)
   }
   if(!acl.length)await expect(region).toContainText('空：未设置默认继承ACL。')
  }
 }
 await signal('selected',fixture.captures.slice(25),{snapshot:value,request:proof(response)});assert.equal(requests.length,pages.length+1);assert.ok(requests.every(r=>r.method==='GET'));page.off('request',record);await closePermissionDetails(page,dialog)
 const query={instanceId:fixture.instanceId,path:'work/recursive-tree'},urls=[base+endpoint+'?'+new URLSearchParams({...query,after:'',take:'25',expectedVersion:''}),base+endpoint+'/'+capture+'?'+new URLSearchParams({...query,expectedHistoryDigest:value.summary.historyDigest,offset:'0',take:'25'})]
 phase='admin-normal-dual-logout';const adminLogout=await logoutFromUserMenu(page,context,base),adminAnonymous=await denied(context,urls,401)
 const ordinaryContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});ordinaryContext.setDefaultTimeout(30_000);page=await ordinaryContext.newPage();phase='ordinary-visible-login';const ordinary=await loginVisible(page,ordinaryContext,base,'/',false,credentials.ordinary)
 phase='ordinary-denied';let uiRequests=0;page.on('request',r=>{if(new URL(r.url()).pathname.startsWith(endpoint))uiRequests++});await page.goto(base+'/containers');await expect(page).toHaveURL(base+'/forbidden');assert.equal(uiRequests,0);const ordinaryDenied=await denied(ordinaryContext,urls,403);await page.screenshot({path:join(evidence,'inventory-ordinary.png')})
 await page.getByRole('button',{name:'首页',exact:true}).click();await expect(page).toHaveURL(base+'/');phase='ordinary-normal-dual-logout';const logout=await logoutFromUserMenu(page,ordinaryContext,base),anonymous=await denied(ordinaryContext,urls,401)
 const result={revision:fixture.revision,subject:admin.subject,pages,signals,requests,mutations:0,adminLogout,adminAnonymous,ordinary:{subject:ordinary.subject,denied:ordinaryDenied,anonymous,uiRequests,logout},inventoryMultipleCaptures:true,realSnapshotPagination:false}
 await writeSignal(evidence,'inventory-auth-result.json',result);assert.equal((await waitSignal(evidence,'inventory-auth-verified.json')).authorityAndOriginalsVerified,true);await writeFile(join(evidence,'browser.json'),JSON.stringify(result),{mode:0o600,flag:'wx'})
 await ordinaryContext.close();await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({recursiveHistoryInventory:'passed',captureCount:26,actualUi:true}))
}catch(error){if(page&&evidence&&!phase.includes('login')&&!phase.includes('logout'))await page.screenshot({path:join(evidence,'inventory-failed.png')}).catch(()=>{});console.error(JSON.stringify({recursiveHistoryInventory:'failed',phase,failureType:error?.name}));if(browser)await browser.close().catch(()=>{});process.exitCode=1}
