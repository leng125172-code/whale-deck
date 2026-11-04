import { expect,test } from '@playwright/test'
import type { Page } from '@playwright/test'
import {installContainerRecursiveAclRestoreFixture} from './fixtures/container-recursive-acl-restores.mjs'
test.use({baseURL:process.env.WHALEDECK_PRODUCTION_PREVIEW_URL||'http://127.0.0.1:5174',launchOptions:{executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH,ignoreDefaultArgs:['--hide-scrollbars']}})
const modal=(page:Page)=>page.getByRole('dialog',{name:'递归恢复容器目录历史ACL',exact:true})
type Control=Awaited<ReturnType<typeof installContainerRecursiveAclRestoreFixture>>
function writes(c:Control,suffix:string){return c.state.writes.filter((v:{path:string})=>suffix?v.path.endsWith(suffix):v.path==='')}
async function open(page:Page){
 await page.goto('/containers');await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click()
 await page.getByRole('button',{name:'容器文件',exact:true}).click()
 await page.getByRole('region',{name:'容器文件',exact:true}).getByRole('button',{name:'递归ACL历史 资料',exact:true}).click()
 const history=page.getByRole('dialog',{name:'递归ACL历史',exact:true});await history.getByRole('button',{name:/^查看逐项前像 /}).click()
 await history.getByRole('button',{name:'预检恢复此递归ACL历史',exact:true}).click();await expect(modal(page)).toBeVisible()
}
async function query(page:Page){await modal(page).getByRole('button',{name:'查询原递归ACL恢复任务',exact:true}).click()}
async function plan(page:Page){await modal(page).getByRole('button',{name:'生成递归ACL恢复预检',exact:true}).click();await query(page);await expect(modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true})).toBeEnabled()}
for(const width of [320,1045,1440])for(const colorScheme of ['light','dark'] as const)test(`${width} ${colorScheme}: source, bounded targets and one original execution`,async({page})=>{
 await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme});const c=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page)
 await expect(modal(page)).toContainText('来源原预计变更 1');await expect(modal(page)).toContainText('本次预计恢复 2')
 for(const name of ['确认递归ACL恢复','请求取消递归ACL恢复'])await expect(modal(page).getByRole('button',{name,exact:true})).toBeInViewport({ratio:1})
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true)
 await page.screenshot({path:`../deploy/artifacts/ui-candidates/recursive-acl-restore/${width}-${colorScheme}.png`})
 const request=writes(c,'')[0].body;expect(Object.keys(request).sort()).toEqual(['captureId','expectedHistoryDigest','expectedVersion','instanceId','path']);expect(request.captureId).toBe(c.source.captureId)
 await modal(page).getByRole('button',{name:'查看逐项恢复目标ACL',exact:true}).click();await expect(modal(page).getByRole('region',{name:'逐项恢复前像'})).toContainText('指定用户 0')
 await modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true}).click();await query(page)
 await expect(modal(page)).toContainText('递归ACL恢复已完成');expect(writes(c,'/confirm')).toHaveLength(1)
 expect(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-restore-hints-v1'))).not.toContain(c.source.historyDigest)
})
for(const cause of ['loseQueue','loseConfirm'] as const)test(`${cause}: reload queries original without another write`,async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{[cause]:true});await open(page)
 if(cause==='loseQueue'){await modal(page).getByRole('button',{name:'生成递归ACL恢复预检',exact:true}).click();await expect(modal(page).getByRole('alert').last()).toBeVisible()}
 else{await plan(page);await modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true}).click();await expect(modal(page).getByRole('alert').last()).toBeVisible()}
 await page.reload();await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click()
 await page.getByRole('button',{name:'查看递归ACL恢复任务',exact:true}).click();await expect(modal(page)).toBeVisible()
 expect(writes(c,'')).toHaveLength(1);expect(writes(c,'/confirm')).toHaveLength(cause==='loseConfirm'?1:0)
})
for(const state of ['Partial','Unknown'])test(`${state}: preserve facts, never offer retry or confirm`,async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{unresolved:state});await open(page);await plan(page);await modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true}).click();await query(page)
 await expect(modal(page)).toContainText(state==='Partial'?'部分修改，保护已保留':'结果未知，保护已保留');await expect(modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true})).toHaveCount(0);expect(writes(c,'/confirm')).toHaveLength(1)
})
test('default heterogeneous source is paged and never restores file ACLs',async({page})=>{
 await installContainerRecursiveAclRestoreFixture(page,{defaults:true,large:true});await open(page);await plan(page);await modal(page).getByRole('button',{name:'查看逐项恢复目标ACL',exact:true}).click()
 const source=modal(page).getByRole('region',{name:'逐项恢复前像'});await expect(source).toContainText('1–25 / 30');await expect(source).toContainText('文件不受default恢复影响')
 await source.getByRole('button',{name:'下一页恢复前像',exact:true}).click();await expect(source).toContainText('26–30 / 30');await expect(source).toContainText('空（无默认ACL）');await expect(source).toContainText('指定用户 0')
 await source.getByRole('button',{name:'上一页恢复前像',exact:true}).click();await expect(source).toContainText('1–25 / 30')
})
test('cancel dismissal writes nothing; explicit cancellation retains original identity',async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);await modal(page).getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click()
 await page.getByRole('button',{name:'继续等待',exact:true}).click();expect(writes(c,'/cancel')).toHaveLength(0)
 await modal(page).getByRole('button',{name:'请求取消递归ACL恢复',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click()
 await expect(modal(page)).toContainText('已取消');expect(writes(c,'/cancel')).toHaveLength(1);expect(writes(c,'/cancel')[0].key).toBe(c.id)
})
for(const state of ['Partial','Unknown'])test(`${state}: current administrator explicitly resolves original facts`,async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{filePage:true,unresolved:state});await page.goto('/files');await page.getByRole('button',{name:'容器递归ACL恢复异常',exact:true}).click()
 const recovery=page.getByRole('dialog',{name:'容器递归ACL恢复异常处置',exact:true});await recovery.getByRole('button',{name:'处置历史结果',exact:true}).click();await recovery.getByRole('button',{name:'确认处置并释放保护',exact:true}).click()
 await expect(recovery).toContainText('已处置部分修改或未知历史结果并释放保护');expect(c.state.writes).toHaveLength(1);expect(c.state.writes[0].key).toBe(c.id)
})
test('changed source summary blocks the ready confirmation',async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{badSource:true});await open(page);await modal(page).getByRole('button',{name:'生成递归ACL恢复预检',exact:true}).click();await query(page)
 await expect(modal(page).getByRole('alert').last()).toBeVisible();await expect(modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true})).toHaveCount(0);expect(writes(c,'/confirm')).toHaveLength(0)
})

for(const status of [401,403,404,503])test(`${status}: fail closed without replacing the original request`,async({page})=>{
  const control=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);control.state.status=status
  await query(page)
  if(status===401||status===403){await expect(modal(page)).toHaveCount(0);const stored=await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-restore-hints-v1'));expect(stored??'').not.toContain(control.id)}
  else{await expect(modal(page).getByRole('alert').filter({hasText:status===404?'尚未找到原登记':'请求失败'})).toBeVisible();await expect(modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true})).toBeDisabled()}
  expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
})
test('instance change cannot confirm the old tree',async({page})=>{
  const control=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);control.base.state.target.instanceId='888:2026-10-28T01:02:03.123456789Z'
  await page.reload();await page.goto('/containers');await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();await page.getByRole('button',{name:'查看递归ACL恢复任务',exact:true}).click();await expect(modal(page)).toContainText('容器实例已变化');await expect(modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true})).toBeDisabled();expect(writes(control,'/confirm')).toHaveLength(0)
})
test('expired plan does not replan or authorize confirmation',async({page})=>{
  const control=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);control.row.state='Expired';control.row.confirmation=null;await query(page);await expect(modal(page).getByText('预检计划已过期',{exact:true})).toBeVisible();expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
})
test('malformed completed count never becomes a successful UI result',async({page})=>{
  const control=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);await modal(page).getByRole('button',{name:'确认递归ACL恢复',exact:true}).click();control.state.malformed=true;await query(page);await expect(modal(page).getByRole('alert').last()).toBeVisible();await expect(modal(page).getByText('递归ACL恢复已完成',{exact:true})).toHaveCount(0);expect(writes(control,'/confirm')).toHaveLength(1)
})
for(const unresolved of ['Partial','Unknown'])for(const colorScheme of ['light','dark']as const)test(`320x600 ${colorScheme} ${unresolved}: long recovery keeps explicit actions visible`,async({page})=>{
  await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme});const control=await installContainerRecursiveAclRestoreFixture(page,{filePage:true,unresolved});control.row.command.location.path='work/'+Array(5).fill('长目录'.repeat(20)).join('/')
  await page.goto('/files');await page.getByRole('button',{name:'容器递归ACL恢复异常',exact:true}).click();const dialog=page.getByRole('dialog',{name:'容器递归ACL恢复异常处置',exact:true});await dialog.getByRole('button',{name:'处置历史结果',exact:true}).click()
  await expect(dialog.getByRole('button',{name:'确认处置并释放保护',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'暂不处置',exact:true})).toBeInViewport({ratio:1});expect(control.state.writes).toHaveLength(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true)
})
test('lost acknowledgement queries original without resending',async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{filePage:true,unresolved:'Partial',loseAck:true});await page.goto('/files');await page.getByRole('button',{name:'容器递归ACL恢复异常',exact:true}).click()
 const d=page.getByRole('dialog',{name:'容器递归ACL恢复异常处置',exact:true});await d.getByRole('button',{name:'处置历史结果',exact:true}).click();await d.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();await expect(d).toContainText('处置响应尚未核实')
 await d.getByRole('button',{name:'查询原处置',exact:true}).click();await expect(d).toContainText('已核实原处置回执');expect(c.state.writes).toHaveLength(1)
})
test('closing a late source read clears its page without a write or replacement reference',async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page);await open(page);await plan(page);c.state.historyPause=true;await modal(page).getByRole('button',{name:'查看逐项恢复目标ACL',exact:true}).click();await expect.poll(()=>c.state.historyWaiting).toBe(true)
 await modal(page).getByRole('button',{name:'关闭',exact:true}).click();await expect(modal(page)).not.toBeVisible();c.state.historyRelease()
 await page.getByRole('button',{name:'查看递归ACL恢复任务',exact:true}).click();await expect(modal(page).getByRole('region',{name:'逐项恢复前像'})).not.toContainText('前像 1–4');expect(writes(c,'')).toHaveLength(1);expect(writes(c,'/confirm')).toHaveLength(0)
})
test('ordinary login never reads or submits recursive restoration',async({page})=>{
 const c=await installContainerRecursiveAclRestoreFixture(page,{administrator:false});await page.goto('/containers');await expect(page.getByRole('button',{name:/递归ACL历史 /})).toHaveCount(0);expect(c.state.writes).toHaveLength(0);expect(c.state.reads).toHaveLength(0)
})
