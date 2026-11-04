// Installed UI only. The 26 preimages were captured by actual signed RPC.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'

let phase='startup',browser,page,evidence
function proof(response){
 const url=new URL(response.url());assert.equal(new Set(url.searchParams.keys()).size,[...url.searchParams.keys()].length)
 return {method:response.request().method(),pathname:url.pathname,query:Object.fromEntries(url.searchParams),status:response.status(),noStore:(response.headers()['cache-control']??'').includes('no-store')}
}
async function denied(context,urls,status){const values=[];for(const url of urls)values.push((await context.request.get(url)).status());assert.deepEqual(values,[status,status]);return values}
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const packet=JSON.parse(await readFile('/dev/stdin','utf8')),fixture=packet.fixture,credentials=packet.credentials
 const base='http://127.0.0.1:8080',endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`
 assert.equal(process.argv[2],fixture.revision);assert.match(fixture.captureId,/^[a-f0-9]{32}$/);assert.match(fixture.historyDigest,/^[A-F0-9]{64}$/)
 evidence=fixture.evidence;browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(30_000);page=await context.newPage()
 phase='admin-visible-login';const admin=await loginVisible(page,context,base,'/containers',true,credentials.admin)
 assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,fixture.revision)
 phase='actual-history-controls';const files=await openPermissionFiles(page,fixture),dialog=page.getByRole('dialog',{name:'递归ACL历史',exact:true})
 const requests=[];const record=r=>{if(new URL(r.url()).pathname.startsWith(endpoint))requests.push({method:r.method(),pathname:new URL(r.url()).pathname})};page.on('request',record)
 let listingResponse=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
 await files.getByRole('button',{name:'递归ACL历史 recursive-tree',exact:true}).click()
 const pages=[],entries=[]
 for(let n=0;n<100;n++){
  const r=await listingResponse;assert.equal(r.status(),200);const value=await r.json();pages.push({request:proof(r),page:value});entries.push(...value.entries)
  if(value.complete)break
  assert.ok(n<99);listingResponse=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET');await dialog.getByRole('button',{name:'继续扫描',exact:true}).click()
 }
 assert.equal(pages.at(-1).page.complete,true);assert.deepEqual(entries.map(e=>e.captureId),[fixture.captureId])
 const labels=['first','last','back','last-again'],signals=[]
 for(let index=0;index<4;index++){
  phase='snapshot-'+labels[index]
  if(index===2){await page.setViewportSize({width:1440,height:600});await page.emulateMedia({colorScheme:'light'})}
  const reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+fixture.captureId&&r.request().method()==='GET')
  await dialog.getByRole('button',{name:index===0?'查看逐项前像 '+fixture.captureId:index===2?'上一页前像':'下一页前像',exact:true}).click()
  const r=await reading;assert.equal(r.status(),200);const value=await r.json();assert.equal(value.offset,index%2?25:0);assert.equal(value.entries.length,index%2?1:25)
  assert.equal(value.complete,index%2===1);assert.equal(value.nextOffset,index%2?null:25)
  const details=dialog.getByRole('region',{name:'递归ACL历史详情',exact:true});await expect(details).toContainText(fixture.historyDigest)
  await expect(details.getByRole('article')).toHaveCount(value.entries.length)
  const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx'],names={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
  for(const entry of value.entries){
   const article=details.getByRole('article',{name:'原ACL '+(entry.relativePath||'根目录'),exact:true})
   await article.getByRole('heading',{name:entry.relativePath||'根目录',exact:true}).scrollIntoViewIfNeeded()
   await expect(article).toContainText('原模式 '+entry.originalMode.toString(8).padStart(4,'0'))
   for(const kind of ['access','default']){
    const region=article.getByRole('region',{name:`历史${kind} ACL`,exact:true}),acl=entry[kind+'Acl']
    await expect(region.locator('li')).toHaveCount(acl.length)
    for(const [n,e]of acl.entries()){
     await expect(region.locator('li').nth(n)).toContainText(names[e.tag]+(e.id===null?'':` ${e.id}`))
     await expect(region.locator('li').nth(n)).toContainText(`权限 ${e.permissions} · ${bits[e.permissions]}`)
     const mask=acl.find(v=>v.tag==='mask')?.permissions??7,effective=['user','group_obj','group'].includes(e.tag)?e.permissions&mask:e.permissions
     await expect(region.locator('li').nth(n)).toContainText(`有效 ${bits[effective]}`)
    }
    if(!acl.length)await expect(region).toContainText('空：未设置默认继承ACL。')
   }
  }
  await expect(dialog.getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1})
  await expect(dialog.getByRole('button',{name:index%2?'上一页前像':'下一页前像',exact:true})).toBeInViewport({ratio:1})
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
  const screenshot=`paging-${index}.png`;await page.screenshot({path:join(evidence,screenshot)})
  const signal={subject:admin.subject,index,snapshot:value,request:proof(r),pages:[...pages],screenshot};signals.push(signal)
  await writeSignal(evidence,'paging-'+labels[index]+'-result.json',signal)
  assert.equal((await waitSignal(evidence,'paging-'+labels[index]+'-verified.json')).physicalRootHddAuditUnchanged,true)
 }
 assert.ok(requests.every(r=>r.method==='GET'));assert.equal(requests.length,pages.length+4);page.off('request',record)
 await closePermissionDetails(page,dialog)
 const query={instanceId:fixture.instanceId,path:'work/recursive-tree'}
 const urls=[base+endpoint+'?'+new URLSearchParams({...query,after:'',take:'25',expectedVersion:''}),base+endpoint+'/'+fixture.captureId+'?'+new URLSearchParams({...query,expectedHistoryDigest:fixture.historyDigest,offset:'25',take:'25'})]
 phase='admin-normal-dual-logout';const adminLogout=await logoutFromUserMenu(page,context,base);const adminAnonymous=await denied(context,urls,401)
 const ordinaryContext=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});ordinaryContext.setDefaultTimeout(30_000);const ordinaryPage=await ordinaryContext.newPage();page=ordinaryPage
 phase='ordinary-visible-login';const ordinary=await loginVisible(ordinaryPage,ordinaryContext,base,'/',false,credentials.ordinary)
 let uiRequests=0;ordinaryPage.on('request',r=>{if(new URL(r.url()).pathname.startsWith(endpoint))uiRequests++})
 phase='ordinary-denied';await ordinaryPage.goto(base+'/containers');await expect(ordinaryPage).toHaveURL(base+'/forbidden');assert.equal(uiRequests,0)
 const ordinaryDenied=await denied(ordinaryContext,urls,403)
 await ordinaryPage.screenshot({path:join(evidence,'paging-ordinary.png')})
 await ordinaryPage.getByRole('button',{name:'首页',exact:true}).click();await expect(ordinaryPage).toHaveURL(base+'/')
 phase='ordinary-normal-dual-logout';const ordinaryLogout=await logoutFromUserMenu(ordinaryPage,ordinaryContext,base);const anonymous=await denied(ordinaryContext,urls,401)
 const result={revision:fixture.revision,subject:admin.subject,pages,signals,requests,mutations:0,adminLogout,adminAnonymous,ordinary:{subject:ordinary.subject,denied:ordinaryDenied,anonymous,uiRequests,logout:ordinaryLogout},realSnapshotPagination:true,inventoryMultipleCaptures:false}
 await writeSignal(evidence,'paging-auth-result.json',result);assert.equal((await waitSignal(evidence,'paging-auth-verified.json')).authorityAndSourceVerified,true)
 await writeFile(join(evidence,'browser.json'),JSON.stringify(result),{mode:0o600,flag:'wx'})
 await ordinaryContext.close();await context.close();await browser.close();browser=undefined
 console.log(JSON.stringify({recursiveAclHistoryPaging:'passed',snapshotOffsets:[0,25,0,25],actualUi:true}))
}catch(error){
 if(page&&evidence&&!phase.includes('login')&&!phase.includes('logout'))await page.screenshot({path:join(evidence,'paging-failed.png')}).catch(()=>{})
 console.error(JSON.stringify({recursiveAclHistoryPaging:'failed',phase,failureType:error?.name}))
 if(browser)await browser.close().catch(()=>{});process.exitCode=1
}
