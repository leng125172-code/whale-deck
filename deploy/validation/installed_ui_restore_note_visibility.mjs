// Read-only current task rendering; no interception, state injection or domain writes.
import assert from 'node:assert/strict'
import {join} from 'node:path'
import {expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {unknownWarning,acknowledgementWarning} from './restore_job_observation_contract.mjs'

export async function observeNotes(page,base,id,evidence,width,theme){
 assert.ok([320,1440].includes(width));assert.ok(['light','dark'].includes(theme))
 await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme:theme})
 const listing=page.waitForResponse(r=>{const u=new URL(r.url());return u.pathname==='/api/v1/jobs'&&u.searchParams.get('take')==='100'&&r.request().method()==='GET'})
 await page.goto(base+'/jobs');const lr=await listing;assert.equal(lr.status(),200);const jobs=await lr.json(),index=jobs.findIndex(j=>j.id===id);assert.ok(index>=0)
 const reading=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/jobs/'+id&&r.request().method()==='GET')
 await page.locator('.el-table__body-wrapper tbody tr').nth(index).getByRole('button',{name:'详情',exact:true}).click()
 const response=await reading;assert.equal(response.status(),200);const job=await response.json(),s=JSON.parse(job.resultJson)
 assert.equal(job.state,'Failed');assert.equal(job.phase,'Acknowledged');assert.equal(s.state,'Acknowledged');assert.equal(s.receiptState,'Unknown')
 assert.deepEqual(['completedCount','changedCount','abortedCount','notStartedCount','unknownCount'].map(k=>s[k]),[0,0,0,0,26])
 const drawer=page.getByRole('dialog',{name:'任务详情',exact:true}),region=drawer.getByRole('region',{name:'递归ACL恢复安全结果',exact:true})
 await expect.poll(()=>page.evaluate(()=>document.documentElement.classList.contains('dark'))).toBe(theme==='dark')
 await page.evaluate(async()=>{await document.fonts.ready})
 const screenshots=[],regions=[]
 for(const [name,element,text]of [['unknown',region.getByRole('note',{name:'未知回执计数限制',exact:true}),unknownWarning],['acknowledged',region.getByText(acknowledgementWarning,{exact:true}),acknowledgementWarning]]){
  await expect(element).toHaveText(text);await element.scrollIntoViewIfNeeded();await expect(element).toBeVisible();await expect(element).toBeInViewport({ratio:1})
  const rect=await element.boundingBox();assert.ok(rect&&rect.x>=0&&rect.y>=0&&rect.width>0&&rect.height>0&&rect.x+rect.width<=width+1&&rect.y+rect.height<=601)
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth))
  const file=`note-${width}-${theme}-${name}.png`;await page.screenshot({path:join(evidence,file)});screenshots.push(file);regions.push({name,text,rect})
 }
 assert.equal(await drawer.getByRole('button',{name:'取消任务',exact:true}).count(),0);assert.equal(await drawer.getByRole('button',{name:'重新预检并重试',exact:true}).count(),0)
 await drawer.locator('.el-drawer__close-btn').click()
 return {job,viewport:{width,height:600},theme,regions,screenshots}
}
