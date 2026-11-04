import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerRecursiveAclFixture } from './fixtures/container-recursive-acls.mjs'
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174', launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, ignoreDefaultArgs: ['--hide-scrollbars'] } })
const modal = (page: Page) => page.getByRole('dialog', { name: '递归修改容器目录ACL', exact: true })
type Control = Awaited<ReturnType<typeof installContainerRecursiveAclFixture>>
async function files(page: Page) {
  await page.goto('/containers')
  await page.locator('.container-card').filter({ hasText: 'demo-web' }).getByRole('button', { name: '详情', exact: true }).click()
  await page.getByRole('button', { name: '容器文件', exact: true }).click()
  const region = page.getByRole('region', { name: '容器文件', exact: true })
  await expect(region.getByRole('listitem')).toHaveCount(4)
  return region
}
async function open(page: Page) {
  const region=await files(page)
  await region.getByRole('button', { name: '递归ACL 资料', exact: true }).click()
  await expect(modal(page)).toBeVisible()
}
async function plan(page: Page) {
  await modal(page).getByRole('button', { name: '生成递归ACL修改预检', exact: true }).click()
  await expect(modal(page).getByText('等待预检', { exact: true })).toBeVisible()
  await query(page)
  await expect(modal(page).getByText('预检已完成，尚未修改ACL', { exact: true })).toBeVisible()
}
async function query(page: Page) { await modal(page).getByRole('button', { name: '查询原递归ACL修改任务', exact: true }).click() }
function writes(control: Control, suffix: string) { return control.state.writes.filter((v: {path:string}) => suffix ? v.path.endsWith(suffix) : v.path === '') }
async function fit(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  for (const name of ['确认递归ACL修改', '请求取消递归ACL修改']) {
    const button=modal(page).getByRole('button',{name,exact:true})
    await expect(button).toBeInViewport({ratio:1})
  }
}
for (const width of [320, 1045, 1440]) for (const colorScheme of ['light','dark'] as const) test(`${width} ${colorScheme}: two target sets and one original execution`,async({page})=>{
  await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme})
  const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page)
  expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
  const body=writes(control,'')[0].body
  expect(body.aclType).toBe('access');expect(body.fileEntries[0].permissions).toBe(6);expect(body.directoryEntries[0].permissions).toBe(7)
  expect(body).not.toHaveProperty('uid');await fit(page)
  await page.screenshot({path:`../deploy/artifacts/ui-candidates/container-recursive-acl/${width}-${colorScheme}.png`})
  await modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true}).click();await query(page)
  await expect(modal(page).getByText('递归ACL修改已完成',{exact:true})).toBeVisible()
  expect(writes(control,'/confirm')).toHaveLength(1);expect(writes(control,'/confirm')[0].key).toBe(control.id)
  expect(await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1'))).not.toContain('isolated-recursive-acl-confirmation')
})
for(const clear of [false,true])test(`default ${clear?'clear':'set'} keeps files explicitly null`,async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page)
  await modal(page).getByRole('combobox',{name:'递归ACL类型',exact:true}).press('ArrowDown');await page.getByRole('listbox',{name:'递归ACL类型',exact:true}).getByRole('option',{name:'default · 仅目录继承ACL',exact:true}).click({timeout:5000})
  if(clear){await modal(page).getByText('明确清除所有目录的default ACL',{exact:true}).click({timeout:5000});await expect(modal(page).getByRole('checkbox',{name:'明确清除所有目录的default ACL',exact:true})).toBeChecked()}
  await plan(page);const body=writes(control,'')[0].body;expect(body.fileEntries).toBeNull();expect(body.directoryEntries).toHaveLength(clear?0:3)
  await expect(modal(page)).toContainText('仅目录继承，文件不变')
})
test('named zero requires a mask and remains separate from inode owners',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page)
  const editor=modal(page).getByRole('region',{name:'文件目标ACL',exact:true})
  await editor.getByRole('button',{name:'添加文件目标ACL条目',exact:true}).click()
  await editor.getByLabel('文件目标ACL user ID 4',{exact:true}).fill('0')
  await modal(page).getByRole('button',{name:'生成递归ACL修改预检',exact:true}).click()
  await expect(modal(page).getByRole('alert').filter({hasText:'请核对ACL完整条目'})).toBeVisible();expect(writes(control,'')).toHaveLength(0)
  await editor.getByRole('combobox',{name:'文件目标ACL 新增条目类型',exact:true}).press('ArrowDown');await page.getByRole('listbox',{name:'文件目标ACL 新增条目类型',exact:true}).getByRole('option',{name:'mask',exact:true}).click({timeout:5000})
  await editor.getByRole('button',{name:'添加文件目标ACL条目',exact:true}).click();await plan(page)
  const named=writes(control,'')[0].body.fileEntries.find((e:{tag:string})=>e.tag==='user');expect(named).toEqual({tag:'user',id:0,permissions:0})
  await expect(modal(page)).toContainText('原根目录UID/GID 1000:1001')
})
for(const cause of ['loseQueue','loseConfirm'] as const)test(`${cause}: query original without duplicate writes`,async({page})=>{
  const control=await installContainerRecursiveAclFixture(page,{[cause]:true});await open(page)
  if(cause==='loseQueue'){
    await modal(page).getByRole('button',{name:'生成递归ACL修改预检',exact:true}).click();await expect(modal(page).getByRole('alert').last()).toBeVisible();await query(page)
  }else{
    await plan(page);await modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true}).click();await expect(modal(page).getByRole('alert').last()).toBeVisible();await query(page)
  }
  expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(cause==='loseConfirm'?1:0)
})
for(const state of ['Partial','Unknown'])test(`${state}: facts and protection stay unresolved`,async({page})=>{
  const control=await installContainerRecursiveAclFixture(page,{unresolved:state});await open(page);await plan(page)
  await modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true}).click();await query(page)
  await expect(modal(page)).toContainText(state==='Partial'?'部分修改，保护已保留':'结果未知，保护已保留')
  await expect(modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true})).toHaveCount(0)
  await expect(modal(page).getByRole('link',{name:'前往递归ACL修改异常处置',exact:true})).toBeVisible();expect(writes(control,'/confirm')).toHaveLength(1)
})
for(const state of ['Partial','Unknown'])test(`${state} recovery: one acknowledgement, preserve original outcome`,async({page})=>{
  const control=await installContainerRecursiveAclFixture(page,{filePage:true,unresolved:state,loseAck:true});await page.goto('/files')
  await page.getByRole('button',{name:'容器递归ACL修改异常',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'容器递归ACL修改异常处置',exact:true})
  await dialog.getByRole('button',{name:'处置历史结果',exact:true}).click();await dialog.getByRole('button',{name:'确认处置并释放保护',exact:true}).click()
  await expect(dialog.getByText('处置响应尚未核实，请查询原处置，不会自动重发。')).toBeVisible()
  await dialog.getByRole('button',{name:'查询原处置',exact:true}).click();await expect(dialog).toContainText('已核实原处置回执')
  expect(control.state.writes).toHaveLength(1);expect(control.row.receipt.state).toBe(state)
})
for(const colorScheme of ['light','dark']as const)test(`320x600 ${colorScheme}: cancellation remains visible`,async({page})=>{
  await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme});const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page);await fit(page)
  await modal(page).getByRole('button',{name:'请求取消递归ACL修改',exact:true}).click()
  await page.getByRole('button',{name:'确认请求取消',exact:true}).click();expect(writes(control,'/cancel')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
})

for(const status of [401,403,404,503])test(`${status}: fail closed without replacing the original request`,async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page);control.state.status=status
  await query(page)
  if(status===401||status===403){await expect(modal(page)).toHaveCount(0);const stored=await page.evaluate(()=>sessionStorage.getItem('whaledeck-container-recursive-acl-hints-v1'));expect(stored??'').not.toContain(control.id)}
  else{await expect(modal(page).getByRole('alert').filter({hasText:status===404?'尚未找到原登记':'请求失败'})).toBeVisible();await expect(modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true})).toBeDisabled()}
  expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
})
test('lost registration survives reload with its original key only',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page,{loseQueue:true});await open(page);await modal(page).getByRole('button',{name:'生成递归ACL修改预检',exact:true}).click();await expect.poll(()=>control.state.key).not.toBe('');const key=control.state.key
  await page.reload();await files(page);await page.getByRole('button',{name:'查看递归ACL修改任务',exact:true}).click();await expect(modal(page).getByText('预检已完成，尚未修改ACL',{exact:true})).toBeVisible();expect(control.state.reads).toContain('/by-key/'+key);expect(writes(control,'')).toHaveLength(1)
})
test('instance change cannot confirm the old tree',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page);control.base.state.target.instanceId='888:2026-10-28T01:02:03.123456789Z'
  await page.reload();await files(page);await page.getByRole('button',{name:'查看递归ACL修改任务',exact:true}).click();await expect(modal(page)).toContainText('容器实例已变化');await expect(modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true})).toBeDisabled();expect(writes(control,'/confirm')).toHaveLength(0)
})
test('expired plan does not replan or authorize confirmation',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page);control.row.state='Expired';control.row.confirmation=null;await query(page);await expect(modal(page).getByText('预检计划已过期',{exact:true})).toBeVisible();expect(writes(control,'')).toHaveLength(1);expect(writes(control,'/confirm')).toHaveLength(0)
})
test('malformed completed count never becomes a successful UI result',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page);await open(page);await plan(page);await modal(page).getByRole('button',{name:'确认递归ACL修改',exact:true}).click();control.state.malformed=true;await query(page);await expect(modal(page).getByRole('alert').last()).toBeVisible();await expect(modal(page).getByText('递归ACL修改已完成',{exact:true})).toHaveCount(0);expect(writes(control,'/confirm')).toHaveLength(1)
})
test('ordinary user has no recursive ACL or recovery calls',async({page})=>{
  const control=await installContainerRecursiveAclFixture(page,{administrator:false});await page.goto('/containers');await expect(page.getByRole('button',{name:/递归ACL /})).toHaveCount(0);expect(control.state.reads).toHaveLength(0);expect(control.state.writes).toHaveLength(0)
})
for(const unresolved of ['Partial','Unknown'])for(const colorScheme of ['light','dark']as const)test(`320x600 ${colorScheme} ${unresolved}: long recovery keeps explicit actions visible`,async({page})=>{
  await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme});const control=await installContainerRecursiveAclFixture(page,{filePage:true,unresolved});control.row.command.location.path='work/'+Array(5).fill('长目录'.repeat(20)).join('/')
  await page.goto('/files');await page.getByRole('button',{name:'容器递归ACL修改异常',exact:true}).click();const dialog=page.getByRole('dialog',{name:'容器递归ACL修改异常处置',exact:true});await dialog.getByRole('button',{name:'处置历史结果',exact:true}).click()
  await expect(dialog.getByRole('button',{name:'确认处置并释放保护',exact:true})).toBeInViewport({ratio:1});await expect(dialog.getByRole('button',{name:'暂不处置',exact:true})).toBeInViewport({ratio:1});expect(control.state.writes).toHaveLength(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true)
})
