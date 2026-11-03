import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerAclFixture } from './fixtures/container-acls.mjs'
test.use({baseURL:process.env.WHALEDECK_PRODUCTION_PREVIEW_URL||'http://127.0.0.1:5174',launchOptions:{executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH,ignoreDefaultArgs:['--hide-scrollbars']}})
const modal=(page:Page)=>page.getByRole('dialog',{name:'从历史恢复容器ACL',exact:true})
async function setup(page:Page,options:Record<string,unknown>={}) {
 const c=await installContainerAclFixture(page,{...options,restore:true})
 async function open(directory=false){
  await page.goto('/containers');await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click()
  await page.getByRole('region',{name:'容器文件',exact:true}).getByRole('button',{name:`ACL历史 ${directory?'资料':c.base.state.longName}`,exact:true}).click()
  const history=page.getByRole('dialog',{name:'ACL历史',exact:true});await history.getByRole('button',{name:`查看原ACL ${'d'.repeat(32)}`,exact:true}).click()
  await history.getByRole('button',{name:'从此历史生成恢复预检',exact:true}).click();await expect(modal(page)).toBeVisible()
 }
 async function prepare(directory=false){await open(directory);await modal(page).getByRole('button',{name:'生成ACL恢复预检',exact:true}).click();await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible()}
 return {...c,open,prepare}
}
for(const colorScheme of ['light','dark'] as const)test(`320x600 ${colorScheme}: selected source/current plan and explicit confirmation`,async({page})=>{
 await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme});const c=await setup(page);await c.prepare()
 await expect(modal(page).getByRole('region',{name:'ACL恢复来源',exact:true})).toContainText('仅恢复access ACL')
 await modal(page).getByText('原access ACL',{exact:true}).scrollIntoViewIfNeeded();await expect(modal(page).getByText('原access ACL',{exact:true})).toBeInViewport()
 const confirm=modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true});await expect(confirm).toBeInViewport({ratio:1});await expect(modal(page).getByRole('button',{name:'请求取消ACL修改',exact:true})).toBeInViewport({ratio:1})
 await page.screenshot({path:`../deploy/artifacts/ui-candidates/acl-restore-web/ready-320-${colorScheme}.png`})
 expect(c.state.writes).toHaveLength(1);expect(c.state.writes[0].path).toBe('/restores');expect(Object.keys(c.state.writes[0].body)).toEqual(['instanceId','path','captureId','expectedHistoryDigest','expectedVersion'])
 await confirm.click();await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByText('ACL修改已完成',{exact:true})).toBeVisible();expect(c.state.writes.filter(v=>v.path.endsWith('/confirm'))).toHaveLength(1)
 expect(c.row.historyDigest).not.toBe('B'.repeat(64));expect(c.row.historyVerified).toBe(true)
})
test('default history explicitly clears only default while current access stays in plan',async({page})=>{
 const c=await setup(page,{defaultRestore:true});await c.prepare(true);await expect(modal(page).getByRole('region',{name:'ACL恢复来源',exact:true})).toContainText('仅恢复default ACL');expect(c.row.command.entries).toEqual([]);expect(c.row.targetMode).toBe(0o640)
 await modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true}).click();await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByText('ACL修改已完成',{exact:true})).toBeVisible()
})
test('lost registration survives reload and queries original restore key only',async({page})=>{
 const c=await setup(page,{loseQueue:true});await c.open();await modal(page).getByRole('button',{name:'生成ACL恢复预检',exact:true}).click();await expect(modal(page).getByRole('alert')).toBeVisible();const key=c.state.key
 await page.reload();await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();await page.getByRole('button',{name:'查看ACL修改任务',exact:true}).click()
 await expect(modal(page).getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible();expect(c.state.writes).toHaveLength(1);expect(c.state.reads).toContain('/restores/by-key/'+key)
 const hint=await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-acl-hints-v1'));expect(hint).toContain('"restore":true');expect(hint).not.toContain('captureId')
})
test('cancel before confirmation fences original task without creating an execution',async({page})=>{
 const c=await setup(page);await c.prepare();await modal(page).getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'继续等待',exact:true}).click();expect(c.state.writes).toHaveLength(1)
 await modal(page).getByRole('button',{name:'请求取消ACL修改',exact:true}).click();await page.getByRole('button',{name:'确认请求取消',exact:true}).click();await expect(modal(page).getByText('已取消',{exact:true})).toBeVisible();expect(c.row.executionJobId).toBeNull();expect(c.state.writes[1].key).toBe(c.id)
})
test('changed source response blocks confirmation and peer writes stay reserved',async({page})=>{
 const c=await setup(page);await c.prepare();c.state.sourceChanged=true;await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true})).toBeDisabled()
 await modal(page).getByRole('button',{name:'关闭',exact:true}).click();for(const name of [`权限 ${c.base.state.longName}`,`所有者 ${c.base.state.longName}`])await expect(page.getByRole('button',{name,exact:true})).toBeDisabled();expect(c.state.writes).toHaveLength(1)
})
for(const status of [401,403,409,503])test(`${status} query does not resubmit restore`,async({page})=>{
 const c=await setup(page);await c.prepare();c.state.status=status;await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click()
 if([401,403].includes(status)){await expect(modal(page)).toHaveCount(0);expect((await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-acl-hints-v1')))??'').not.toContain(c.id)}
 else{await expect(modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true})).toBeDisabled();await expect(modal(page).locator('.container-acl__error')).toBeVisible()}
 expect(c.state.writes).toHaveLength(1)
})
test('session end discards delayed restore source and removes original hint',async({page})=>{
 const c=await setup(page);await c.prepare();c.state.pause=true;await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect.poll(()=>c.state.waiting).toBe(true)
 await page.evaluate(()=>window.dispatchEvent(new StorageEvent('storage',{key:'whaledeck-session-ended',storageArea:localStorage,newValue:JSON.stringify({version:1,kind:'signed-out',id:'a'.repeat(32)})})))
 await expect(modal(page)).toHaveCount(0);c.state.release();await page.waitForTimeout(200);await expect(modal(page)).toHaveCount(0);expect(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-acl-hints-v1'))).toBeNull();expect(c.state.writes).toHaveLength(1)
})

test('lost confirmation is never repeated and original GET reconciles execution',async({page})=>{
 const c=await setup(page,{loseConfirm:true});await c.prepare();await modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true}).click();await expect(modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true})).toBeDisabled()
 await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByText('ACL修改已完成',{exact:true})).toBeVisible();expect(c.state.writes.filter(v=>v.path.endsWith('/confirm'))).toHaveLength(1)
})
test('expired historical plan cannot be confirmed or automatically replaced',async({page})=>{
 const c=await setup(page);await c.prepare();c.row.state='Expired';c.row.confirmation=null;await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).getByText('预检计划已过期',{exact:true})).toBeVisible();await expect(modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true})).toHaveCount(0);expect(c.state.writes).toHaveLength(1)
})
test('Unknown retains original restore source and offers only original observation and recovery',async({page})=>{
 const c=await setup(page,{unknown:true});await c.prepare();await modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true}).click();await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click()
 await expect(modal(page).getByText('结果未知，保护已保留',{exact:true})).toBeVisible();await expect(modal(page).getByRole('region',{name:'ACL恢复来源',exact:true})).toContainText('d'.repeat(32));await expect(modal(page).getByRole('link',{name:'前往ACL修改异常处置',exact:true})).toBeVisible();await expect(modal(page).getByRole('button',{name:'请求取消ACL修改',exact:true})).toHaveCount(0);expect(c.state.writes).toHaveLength(2)
})
test('a new container instance cannot confirm the old historical plan',async({page})=>{
 const c=await setup(page);await c.prepare();await modal(page).getByRole('button',{name:'关闭',exact:true}).click();c.base.state.target.instanceId='778:2026-11-03T01:02:03Z'
 await page.getByRole('region',{name:'容器文件',exact:true}).getByRole('button',{name:'重新读取',exact:true}).click();await page.getByRole('button',{name:'查看ACL修改任务',exact:true}).click();await expect(modal(page).getByRole('button',{name:'确认ACL历史恢复',exact:true})).toBeDisabled();await expect(modal(page)).toContainText('容器实例已变化');expect(c.state.writes).toHaveLength(1)
})
test('503 registration keeps its reference and cannot become a new unprompted restore',async({page})=>{
 const c=await setup(page);await c.open();c.state.queueStatus=503;await modal(page).getByRole('button',{name:'生成ACL恢复预检',exact:true}).click();await expect(modal(page).locator('.container-acl__error')).toBeVisible();const key=c.state.writes[0].key
 await modal(page).getByRole('button',{name:'查询原ACL修改任务',exact:true}).click();await expect(modal(page).locator('.container-acl__error')).toContainText('查询同一引用');expect(c.state.writes).toHaveLength(1)
 c.state.queueStatus=0;await modal(page).getByRole('button',{name:'重试原预检登记',exact:true}).click();expect(c.state.writes[1].key).toBe(key);expect(c.state.writes).toHaveLength(2)
})
