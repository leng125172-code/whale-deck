// Real installed Jobs page only: no request interception or identity injection.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'

export const fields='schemaVersion intentId jobId stage state code aclType entryCount fileCount directoryCount changeCount completedCount changedCount abortedCount notStartedCount unknownCount receiptState bindingCurrent historyMayExist historyVerified historyAvailable publishBarrier sourceEntryCount sourceFileCount sourceDirectoryCount sourceChangedCount'.split(' ')
const count=v=>v===null?'未证明':String(v)
const fact=v=>v===null?'未证明':v?'是':'否'
export function displayed(s){
 return {identity:[s.intentId,s.stage==='planning'?'预检':'执行',s.aclType??'未证明'],
  plan:[s.entryCount,s.fileCount,s.directoryCount,s.changeCount].map(count),
  actual:[s.completedCount,s.changedCount,s.abortedCount,s.notStartedCount,s.unknownCount].map(count),
  source:[s.sourceEntryCount,s.sourceFileCount,s.sourceDirectoryCount,s.sourceChangedCount].map(count),
  metadata:[s.receiptState??'未取得',...['bindingCurrent','historyMayExist','historyVerified','historyAvailable','publishBarrier'].map(k=>fact(s[k])),s.code]}
}
export async function observeJob(page,context,base,id,stage,state,evidence){
 // AppShell independently polls take=50. Bind the response rendered by JobsView.
 const listing=page.waitForResponse(r=>{const u=new URL(r.url());return u.pathname==='/api/v1/jobs'&&u.searchParams.get('take')==='100'&&r.request().method()==='GET'})
 await page.goto(base+'/jobs');const lr=await listing;assert.equal(lr.status(),200)
 const jobs=await lr.json(),index=jobs.findIndex(v=>v.id===id);assert.ok(index>=0)
 const detail=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs/'+id&&r.request().method()==='GET')
 await page.locator('.el-table__body-wrapper tbody tr').nth(index).getByRole('button',{name:'详情',exact:true}).click()
 const dr=await detail;assert.equal(dr.status(),200);const job=await dr.json()
 assert.equal(job.id,id);assert.equal(job.state,stage==='planning'?'Succeeded':'Canceled');assert.equal(job.resultJson,jobs[index].resultJson)
 assert.equal(typeof job.resultJson,'string');assert.ok(Buffer.byteLength(job.resultJson)<=4096)
 const s=JSON.parse(job.resultJson);assert.deepEqual(Object.keys(s).sort(),[...fields].sort());assert.equal(s.schemaVersion,1);assert.equal(s.jobId,id);assert.equal(s.stage,stage);assert.equal(s.state,state)
 const drawer=page.getByRole('dialog',{name:'任务详情',exact:true}),region=drawer.getByRole('region',{name:'递归ACL恢复安全结果',exact:true})
 await expect(region).toBeVisible();await expect(region).not.toContainText('此任务未记录可验证')
 const dls=region.locator('dl');await expect(dls).toHaveCount(5)
 const values={},keys=['identity','plan','actual','source','metadata'],expected=displayed(s)
 for(const [i,key]of keys.entries()){
  const dl=dls.nth(i);values[key]=await dl.locator('dd').allTextContents();values[key]=values[key].map(v=>v.trim());assert.deepEqual(values[key],expected[key])
  for(const dd of await dl.locator('dd').all()){await dd.scrollIntoViewIfNeeded();await expect(dd).toBeVisible()}
 }
 const stateText=await region.locator('p').first().innerText();assert.equal(stateText,stage==='planning'?'预检已完成，尚未恢复ACL':'已取消')
 await dls.nth(2).scrollIntoViewIfNeeded();await page.screenshot({path:join(evidence,`job-${stage}-counts.png`)})
 await dls.nth(4).scrollIntoViewIfNeeded();await page.screenshot({path:join(evidence,`job-${stage}-metadata.png`)})
 const historical=await region.locator('p').last().innerText();assert.ok(historical.includes('历史事实')&&historical.includes('来源原修改数与本次实际修改数分别记录'))
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
 assert.equal(await drawer.getByRole('button',{name:'取消任务',exact:true}).count(),0);assert.equal(await drawer.getByRole('button',{name:'重新预检并重试',exact:true}).count(),0)
 await drawer.locator('.el-drawer__close-btn').click()
 return {job,listJob:jobs[index],request:{method:'GET',pathname:'/api/v1/jobs/'+id,status:dr.status()},listRequest:{method:'GET',pathname:'/api/v1/jobs',status:lr.status(),query:Object.fromEntries(new URL(lr.url()).searchParams)},display:values,stateText,historical,viewport:{width:320,height:600},theme:'dark',screenshots:[`job-${stage}-counts.png`,`job-${stage}-metadata.png`]}
}
