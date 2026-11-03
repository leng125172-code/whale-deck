import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installContainerFilesFixture } from './fixtures/container-files.mjs'

test.use({baseURL:process.env.WHALEDECK_PRODUCTION_PREVIEW_URL||'http://127.0.0.1:5174',launchOptions:{executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH,ignoreDefaultArgs:['--hide-scrollbars']}})
const modal=(page:Page)=>page.getByRole('dialog',{name:'ACL历史',exact:true})
async function setup(page:Page,options:{administrator?:boolean;empty?:boolean}={}) {
 const base=await installContainerFilesFixture(page,options),target=base.state.target
 const entry={captureId:'a'.repeat(32),historyDigest:'B'.repeat(64),createdAtUnixMs:0,sourceInstanceId:'666:2026-11-02T01:02:03.123456789Z',
  changedAclType:'access',kind:'File',originalMode:416,accessEntryCount:5,defaultEntryCount:0}
 const state={requests:[] as {method:string;query:Record<string,string>;path:string}[],status:200,pause:false,waiting:false,release:()=>{},malformed:false,partial:false}
 await page.route('**/files/acls/history**',async(route)=>{
  const url=new URL(route.request().url());state.requests.push({method:route.request().method(),query:Object.fromEntries(url.searchParams),path:url.pathname})
  if(state.pause){state.waiting=true;await new Promise<void>((resolve)=>{state.release=resolve});if(route.request().failure())return}
  if(state.status!==200)return route.fulfill({status:state.status,json:{title:'private upstream error'}})
  const location={containerId:target.containerId,instanceId:target.instanceId,path:url.searchParams.get('path')!}
  if(url.pathname.endsWith('/history')){
   const more=!!url.searchParams.get('after')
   return route.fulfill({json:{location,version:'C'.repeat(64),entries:options.empty||state.partial&&!more?[]:[entry],nextAfter:state.partial&&!more?'9'.repeat(32):'',
    scannedCount:1,invalidCount:0,complete:!(state.partial&&!more)}})
  }
  return route.fulfill({json:{location,entry:{...entry,...(state.malformed?{historyDigest:'D'.repeat(64)}:{})},
   accessAcl:[{tag:'user_obj',id:null,permissions:6},{tag:'user',id:0,permissions:0},{tag:'group_obj',id:null,permissions:4},{tag:'mask',id:null,permissions:4},{tag:'other',id:null,permissions:0}],defaultAcl:[]}})
 })
 async function open(){await page.goto('/containers');await page.locator('.container-card').filter({hasText:'demo-web'}).getByRole('button',{name:'详情',exact:true}).click();await page.getByRole('button',{name:'容器文件',exact:true}).click();await page.getByRole('region',{name:'容器文件',exact:true}).getByRole('button',{name:`ACL历史 ${base.state.longName}`,exact:true}).click()}
 return {base,state,entry,open}
}
for(const width of [320,1045,1440])for(const colorScheme of ['light','dark'] as const)test(`${width} ${colorScheme}: verified original ACL metadata and footer stay visible`,async({page})=>{
 await page.setViewportSize({width,height:600});await page.emulateMedia({colorScheme});const c=await setup(page);await c.open()
 await expect(modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true})).toBeVisible()
 await modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true}).click()
 await expect(modal(page).getByRole('region',{name:'历史access ACL',exact:true})).toContainText('指定用户 0')
 await expect(modal(page).getByRole('region',{name:'历史access ACL',exact:true})).toContainText('权限 0 · ---；有效 ---')
 await expect(modal(page).getByRole('region',{name:'历史default ACL',exact:true})).toContainText('空')
 await expect(modal(page)).toContainText('0640');await expect(modal(page)).toContainText(c.entry.historyDigest)
 await expect(modal(page).getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1})
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true)
 expect(c.state.requests).toHaveLength(2);expect(c.state.requests.every((r)=>r.method==='GET')).toBe(true)
 expect(c.state.requests[1]!.query.expectedHistoryDigest).toBe(c.entry.historyDigest)
 await page.screenshot({path:`../deploy/artifacts/ui-candidates/acl-history-web/history-${width}-${colorScheme}.png`})
 await modal(page).getByRole('button',{name:'关闭',exact:true}).click();await expect(modal(page)).toHaveCount(0)
})
test('an empty partial page offers another scan using the original cursor/version',async({page})=>{
 const c=await setup(page);c.state.partial=true;await c.open();await expect(modal(page)).toContainText('当前列表尚未完整')
 await expect(modal(page).getByText('此路径暂无可核实的ACL历史。',{exact:true})).toHaveCount(0)
 await modal(page).getByRole('button',{name:'继续扫描',exact:true}).click();await expect(modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true})).toBeVisible()
 expect(c.state.requests[1]!.query.after).toBe('9'.repeat(32));expect(c.state.requests[1]!.query.expectedVersion).toBe('C'.repeat(64))
})
for(const status of [401,403,404,409,429,503])test(`${status}: a failed list never becomes an empty success`,async({page})=>{
 const c=await setup(page);c.state.status=status;await c.open()
 if([401,403].includes(status))await expect(modal(page)).toHaveCount(0)
 else{await expect(modal(page).locator('.acl-history__error')).toBeVisible();await expect(modal(page).getByText('此路径暂无可核实的ACL历史。',{exact:true})).toHaveCount(0);await expect(modal(page)).not.toContainText('private upstream error')}
 expect(c.state.requests).toHaveLength(1)
})
test('a changed snapshot is refused without showing unverified ACLs',async({page})=>{
 const c=await setup(page);await c.open();c.state.malformed=true;await modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true}).click()
 await expect(modal(page).getByRole('alert').filter({hasText:'无法核实'})).toContainText('无法核实');await expect(modal(page).getByRole('region',{name:'ACL历史快照',exact:true})).toHaveCount(0)
})
for(const reason of ['close','session','hidden'])test(`${reason}: delayed private responses cannot reopen the panel`,async({page})=>{
 const c=await setup(page);c.state.pause=true;await c.open();await expect.poll(()=>c.state.waiting).toBe(true)
 if(reason==='close'){await expect(modal(page).getByRole('button',{name:'取消读取并关闭',exact:true})).toBeInViewport({ratio:1});await modal(page).getByRole('button',{name:'取消读取并关闭',exact:true}).click()}
 if(reason==='session')await page.evaluate(()=>window.dispatchEvent(new StorageEvent('storage',{key:'whaledeck-session-ended',storageArea:localStorage,newValue:JSON.stringify({version:1,kind:'signed-out',id:'a'.repeat(32)})})))
 if(reason==='hidden')await page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});document.dispatchEvent(new Event('visibilitychange'))})
 await expect(modal(page)).toHaveCount(0);c.state.release();await page.waitForTimeout(200);await expect(modal(page)).toHaveCount(0);expect(c.state.requests).toHaveLength(1)
})
test('ordinary users never request or display ACL history',async({page})=>{
 const c=await setup(page,{administrator:false});await page.goto('/containers');await expect(page.getByRole('button',{name:/^ACL历史 /})).toHaveCount(0);expect(c.state.requests).toHaveLength(0)
})
test('a complete verified empty page is the only empty success',async({page})=>{
 const c=await setup(page,{empty:true});await c.open();await expect(modal(page).getByText('此路径暂无可核实的ACL历史。',{exact:true})).toBeVisible()
 await expect(modal(page).getByRole('button',{name:'继续扫描',exact:true})).toHaveCount(0);expect(c.state.requests).toHaveLength(1)
})
test('a continuation conflict clears old rows and requires a fresh inventory',async({page})=>{
 const c=await setup(page);c.state.partial=true;await c.open();await expect(modal(page)).toContainText('当前列表尚未完整')
 c.state.status=409;await modal(page).getByRole('button',{name:'继续扫描',exact:true}).click();await expect(modal(page).locator('.acl-history__error')).toContainText('历史版本已变化')
 await expect(modal(page).getByRole('button',{name:'继续扫描',exact:true})).toHaveCount(0);await expect(modal(page).getByRole('list',{name:'ACL历史记录',exact:true})).toHaveCount(0)
 c.state.status=200;c.state.partial=false;await modal(page).getByRole('button',{name:'重新读取',exact:true}).click()
 await expect(modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true})).toBeVisible();expect(c.state.requests[2]!.query.after).toBe('');expect(c.state.requests[2]!.query.expectedVersion).toBe('')
})
for(const colorScheme of ['light','dark'] as const)test(`320x600 ${colorScheme}: scroll reveals full ACLs while close stays visible`,async({page})=>{
 await page.setViewportSize({width:320,height:600});await page.emulateMedia({colorScheme});const c=await setup(page);await c.open()
 await modal(page).getByRole('button',{name:`查看原ACL ${c.entry.captureId}`,exact:true}).click()
 await modal(page).getByRole('region',{name:'历史default ACL',exact:true}).scrollIntoViewIfNeeded()
 await expect(modal(page).getByText('指定用户 0',{exact:true})).toBeInViewport({ratio:1})
 await expect(modal(page).getByText('空：未设置默认继承ACL。',{exact:true})).toBeInViewport({ratio:1})
 await expect(modal(page).getByRole('button',{name:'关闭',exact:true})).toBeInViewport({ratio:1})
 await page.screenshot({path:`../deploy/artifacts/ui-candidates/acl-history-web/history-acls-320-${colorScheme}.png`})
})
