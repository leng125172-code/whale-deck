// Actual visible auth/denial/logout only. No successful metadata mutation is allowed.
import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
let phase='startup',browser
try{
 const packet=JSON.parse(await readFile('/dev/stdin','utf8')),base='http://127.0.0.1:8080',f=packet.fixture,result=[]
 assert.match(f.containerId,/^[a-f0-9]{64}$/);assert.match(f.id,/^[a-f0-9-]{36}$/)
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const endpoint=`/api/v1/containers/${f.containerId}/files/recursive-acls`,recovery='/api/v1/files/container-recursive-acls/recovery'
 const paths=[endpoint+'/'+f.id,endpoint+'/by-key/'+crypto.randomUUID(),recovery,recovery+'/'+f.id]
 for(const administrator of [true,false]){
  phase=administrator?'admin-login':'ordinary-login'
  const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'}),page=await context.newPage();context.setDefaultTimeout(20_000)
  const profile=await loginVisible(page,context,base,'/',administrator,packet.credentials[administrator?'admin':'ordinary'])
  const role={administrator,subject:profile.subject}
  if(administrator){
   const r=await context.request.get(base+recovery);assert.equal(r.status(),200);assert.ok(r.headers()['cache-control']?.includes('no-store'));assert.ok(Array.isArray(await r.json()))
   assert.equal((await context.request.get(base+endpoint+'/'+f.id)).status(),404)
  }else{
   phase='ordinary-denials';let privateUiRequests=0;page.on('request',r=>{if(new URL(r.url()).pathname.includes('recursive-acls'))privateUiRequests++})
   await page.goto(base+'/files');await expect(page).toHaveURL(base+'/forbidden');assert.equal(privateUiRequests,0)
   await page.screenshot({path:join(f.evidence,'ordinary-forbidden.png')})
   for(const path of paths)assert.equal((await context.request.get(base+path)).status(),403)
   const csrf=await(await context.request.get(base+'/api/v1/auth/csrf')).json(),headers={Origin:base,[csrf.headerName]:csrf.token,'Idempotency-Key':f.id}
   for(const path of [endpoint,endpoint+'/'+f.id+'/confirm',endpoint+'/'+f.id+'/cancel',recovery+'/'+f.id+'/acknowledge'])assert.equal((await context.request.post(base+path,{headers,data:path===endpoint?f.command:{version:f.version,confirmed:true,planHash:'not-authority'}})).status(),403)
   Object.assign(role,{privateUiRequests,deniedGet:4,deniedPost:4})
   await page.getByRole('button',{name:'首页',exact:true}).click();await expect(page).toHaveURL(base+'/')
  }
  phase=administrator?'admin-logout':'ordinary-logout';role.logout=await logoutFromUserMenu(page,context,base)
  for(const path of paths)assert.equal((await context.request.get(base+path)).status(),401)
  role.anonymousGet401=4;result.push(role)
  await writeFile(join(f.evidence,'auth-only-checkpoints.json'),JSON.stringify(result),{mode:0o600});await context.close()
 }
 await browser.close();browser=undefined
 await writeFile(join(f.evidence,'auth-only.json'),JSON.stringify({actualVisibleAuthentication:true,roles:result,fileMutations:0,newIntents:0,originalCancelContext:false,originalFailurePreserved:true}),{mode:0o600})
 console.log(JSON.stringify({recursiveAclAuthOnly:'passed',fileMutations:0,roles:2}))
}catch(error){console.error(JSON.stringify({recursiveAclAuthOnly:'failed',phase,failureType:error?.name,frame:error?.stack?.split('\n').find(v=>v.includes('validate_recursive_acl_auth_only.mjs:'))?.trim()}));if(browser)await browser.close().catch(()=>{});process.exitCode=1}
