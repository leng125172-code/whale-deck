// Actual installed Vue controls; normal login remains in the caller's original context.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {openPermissionFiles,closePermissionDetails,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'

function proof(response){
 const url=new URL(response.url())
 assert.equal(new Set(url.searchParams.keys()).size,[...url.searchParams.keys()].length)
 return {method:response.request().method(),pathname:url.pathname,query:Object.fromEntries(url.searchParams),status:response.status(),noStore:(response.headers()['cache-control']??'').includes('no-store')}
}
export function historyUrls(base,fixture,completed){
 const item=completed.find(v=>v.executionJobId);assert.ok(item)
 const endpoint=`${base}/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`
 const query={instanceId:fixture.instanceId,path:'work/recursive-tree'}
 return [endpoint+'?'+new URLSearchParams({...query,after:'',take:'25',expectedVersion:''}),endpoint+'/'+item.executionJobId.replaceAll('-','')+'?'+new URLSearchParams({...query,expectedHistoryDigest:item.historyDigest,offset:'0',take:'25'})]
}
export async function historyDenials(context,urls){
 const result=[];for(const url of urls)result.push((await context.request.get(url)).status());return result
}
export async function runRecursiveHistoryBrowser(page,base,fixture,subject,completed){
 const items=completed.filter(v=>v.executionJobId);assert.equal(items.length,7)
 const endpoint=`/api/v1/containers/${fixture.containerId}/files/recursive-acls/history`,pages=[],requests=[]
 const record=request=>{const path=new URL(request.url()).pathname;if(path.startsWith(endpoint))requests.push({method:request.method(),pathname:path})}
 page.on('request',record)
 try{
  await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme:'dark'})
  await page.goto(base+'/containers');const files=await openPermissionFiles(page,fixture)
  const dialog=page.getByRole('dialog',{name:'递归ACL历史',exact:true})
  let response=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
  await files.getByRole('button',{name:'递归ACL历史 recursive-tree',exact:true}).click()
  const listing=[]
  for(let count=0;count<100;count++){
   const r=await response;assert.equal(r.status(),200);const value=await r.json()
   pages.push({request:proof(r),page:value});listing.push(...value.entries)
   if(value.complete)break
   assert.ok(count<99);response=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint&&r.request().method()==='GET')
   await dialog.getByRole('button',{name:'继续扫描',exact:true}).click()
  }
  assert.equal(pages.at(-1).page.complete,true)
  assert.deepEqual(listing.map(v=>v.captureId).sort(),items.map(v=>v.executionJobId.replaceAll('-','')).sort())
  assert.equal(listing.length,7)
  for(const [index,item]of items.entries()){
   await page.setViewportSize({width:index%2?1440:320,height:600});await page.emulateMedia({colorScheme:index%2?'light':'dark'})
   const captureId=item.executionJobId.replaceAll('-','')
   const reading=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/'+captureId&&r.request().method()==='GET')
   await dialog.getByRole('button',{name:'查看逐项前像 '+captureId,exact:true}).click()
   const r=await reading;assert.equal(r.status(),200);const value=await r.json()
   assert.equal(value.summary.captureId,captureId);assert.equal(value.summary.historyDigest,item.historyDigest)
   assert.equal(value.offset,0);assert.equal(value.complete,true);assert.equal(value.nextOffset,null);assert.equal(value.entries.length,4)
   const details=dialog.getByRole('region',{name:'递归ACL历史详情',exact:true})
   await expect(details).toContainText(item.historyDigest);await expect(details).toContainText(item.manifestDigest)
   for(const entry of value.entries){
    const article=details.getByRole('article',{name:'原ACL '+(entry.relativePath||'根目录'),exact:true})
    await article.getByRole('heading',{name:entry.relativePath||'根目录',exact:true}).scrollIntoViewIfNeeded()
    await expect(article).toContainText(entry.kind==='File'?'文件':'目录')
    for(const kind of ['access','default']){
     const region=article.getByRole('region',{name:`历史${kind} ACL`,exact:true}),acl=entry[kind+'Acl']
     await expect(region.locator('li')).toHaveCount(acl.length)
     const labels={user_obj:'文件属主',user:'指定用户',group_obj:'文件属组',group:'指定组',mask:'mask',other:'其他用户'}
     const bits=['---','--x','-w-','-wx','r--','r-x','rw-','rwx']
     for(const [n,e]of acl.entries()){
      await expect(region.locator('li').nth(n)).toContainText(labels[e.tag]+(e.id===null?'':` ${e.id}`))
      await expect(region.locator('li').nth(n)).toContainText(`权限 ${e.permissions} · ${bits[e.permissions]}`)
     }
     if(!acl.length)await expect(region).toContainText('空：未设置默认继承ACL。')
    }
   }
   await expect(dialog.getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1})
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
   const screenshot='history-'+item.name+'.png';await page.screenshot({path:join(fixture.evidence,screenshot)})
   await writeSignal(fixture.evidence,'history-'+item.name+'-result.json',{subject,name:item.name,captureId,snapshot:value,request:proof(r),pages:[...pages],screenshot})
   const verified=await waitSignal(fixture.evidence,'history-'+item.name+'-verified.json')
   assert.equal(verified.captureId,captureId);assert.equal(verified.physicalRootHddAndAuditVerified,true)
   await dialog.getByRole('button',{name:'返回历史列表',exact:true}).click()
  }
  assert.ok(requests.every(r=>r.method==='GET'));assert.equal(requests.length,pages.length+7)
  await closePermissionDetails(page,dialog)
  return {reads:7,pages,requests,mutations:0,realUiSnapshotPagination:false}
 }finally{page.off('request',record)}
}
