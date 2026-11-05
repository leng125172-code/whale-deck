// Real directory controls only; the parent proves the permanent fence and inode.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {createHash} from 'node:crypto'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {openPermissionFiles,closePermissionDetails,writeSignal} from './installed_ui_permission_session.mjs'

export async function registerWaitingDirectory(page,base,f,profile){
  const b=f.waitingDirectory;assert.match(b.containerId,/^[a-f0-9]{64}$/);assert.notEqual(b.containerId,f.containerId)
  assert.equal(b.path,'work/文件回收保护后的目录');assert.match(b.containerName,/^wdv-container-publish-[a-f0-9]{32}$/)
  const endpoint=`/api/v1/containers/${b.containerId}/files/directories`
  let plans=0,registrations=0,cancellations=0,uploads=0;const headers=[]
  page.on('request',r=>{const path=new URL(r.url()).pathname
    if(r.method()==='POST'&&path===endpoint+'/plan')plans++
    if(r.method()==='POST'&&path===endpoint){registrations++;headers.push(r.allHeaders().then(h=>h.origin===base&&!!h['idempotency-key']))}
    if(r.method()==='POST'&&path.startsWith(endpoint+'/')&&path.endsWith('/cancel'))cancellations++
    if(r.method()==='PUT'&&path.includes('/files/'))uploads++
  })
  const files=await openPermissionFiles(page,b),panel=files.getByRole('region',{name:'容器新建目录',exact:true})
  await panel.getByRole('button',{name:'新建目录',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'新建容器目录',exact:true})
  await dialog.getByLabel('目录名称',{exact:true}).fill(b.path.slice(5))
  await dialog.locator('.el-select__wrapper').click();await page.getByRole('option',{name:/^0750/}).click()
  const planned=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===endpoint+'/plan')
  await dialog.getByRole('button',{name:'生成目录计划',exact:true}).click();assert.equal((await planned).status(),200)
  await expect(dialog.getByText('/'+b.path,{exact:true})).toBeVisible()
  const accepted=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===endpoint)
  await dialog.getByRole('button',{name:'确认创建目录',exact:true}).click();const response=await accepted;assert.equal(response.status(),202)
  const intent=await response.json();assert.equal(intent.id,intent.jobId);assert.equal(intent.state,'Pending')
  assert.deepEqual(intent.command,{location:{containerId:b.containerId,instanceId:b.instanceId,path:b.path},mode:'0750'})
  await expect(dialog.getByRole('status')).toHaveText('等待执行');await dialog.getByRole('status').scrollIntoViewIfNeeded()
  await expect(dialog.getByRole('status')).toBeInViewport({ratio:1});await page.screenshot({path:join(f.evidence,'waiting-pending.png')})
  assert.equal(plans,1);assert.equal(registrations,1);assert.equal(cancellations,0);assert.equal(uploads,0);assert.ok((await Promise.all(headers)).every(Boolean))
  const post=response.request().postDataJSON(),registration={planTokenSha256:createHash('sha256').update(post.planHash,'utf8').digest('hex').toUpperCase(),confirmed:post.confirmed,idempotencyKey:post.idempotencyKey}
  await writeSignal(f.evidence,'waiting-registered.json',{id:intent.id,subject:profile.subject,intent,registration,plans,registrations,cancellations,uploads,originAndIdempotencyObserved:true})
  await closePermissionDetails(page,dialog);await page.goto(base+'/files')
  return async function showWaiting(context,settled){
    assert.equal(settled.id,intent.id);assert.equal(settled.state,'Succeeded')
    await page.goto(base+'/containers');const actualFiles=await openPermissionFiles(page,b),actual=actualFiles.getByRole('region',{name:'容器新建目录',exact:true})
    await expect(actual.getByRole('status')).toContainText('目录已创建',{timeout:30_000})
    const get=await context.request.get(base+endpoint+'/'+intent.id);assert.equal(get.status(),200);assert.ok(get.headers()['cache-control']?.includes('no-store'))
    const result=await get.json();assert.equal(result.id,intent.id);assert.equal(result.state,'Succeeded');assert.equal(result.publishedVersion,settled.publishedVersion);assert.deepEqual(result.command,intent.command)
    const leaf=actualFiles.getByText(b.path.slice(5),{exact:true});await expect(leaf).toBeVisible();await leaf.scrollIntoViewIfNeeded();await expect(leaf).toBeInViewport({ratio:1})
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await page.screenshot({path:join(f.evidence,'waiting-succeeded.png')})
    assert.equal(plans,1);assert.equal(registrations,1);assert.equal(cancellations,0);assert.equal(uploads,0)
    await page.getByRole('dialog',{name:/容器详情/}).locator('.el-drawer__close-btn').click()
    return {id:intent.id,state:'Succeeded',plans,registrations,cancellations,uploads,originAndIdempotencyObserved:true,normalUiRegistration:true,originalTaskVisibleAfterRelease:true,visibleDirectory:true,publishedVersion:result.publishedVersion,privateEndpoint:endpoint+'/'+intent.id}
  }
}
