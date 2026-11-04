// Read-only new anonymous context; never retroactively replaces the old screenshot.
import assert from 'node:assert/strict'
import {readFile,mkdir,writeFile} from 'node:fs/promises'
import {fileURLToPath} from 'node:url'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'

let browser
try{
  const revision=process.argv[2],name=process.argv[3];assert.match(revision,/^[a-f0-9]{40}$/);assert.match(name,/^container-recursive-acl-partial-[a-f0-9]{32}$/)
  const directory=fileURLToPath(new URL('../artifacts/'+name+'/',import.meta.url)),baseline=JSON.parse(await readFile(join(directory,'baseline.json'),'utf8'))
  assert.equal(baseline.kind,'logout');assert.equal(baseline.revision,revision)
  const original=JSON.parse(await readFile(join(directory,'owner-browser.json'),'utf8'));assert.equal(original.kind,'logout');assert.equal(original.normalDualLogout,true);assert.equal(original.result,null)
  const proof=JSON.parse(await readFile(join(directory,'independent.json'),'utf8'));assert.equal(proof.independentReadOnlyVerifier,true)
  const extra=join(directory,'anonymous-ui-check');await mkdir(extra,{mode:0o700})
  const base='http://127.0.0.1:8080';browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
  const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'}),page=await context.newPage();context.setDefaultTimeout(20_000)
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,revision)
  let privateRequests=0;page.on('request',r=>{if(new URL(r.url()).pathname.includes('recursive-acls'))privateRequests++})
  await page.goto(base+'/login');await expect(page).toHaveURL(base+'/login')
  await expect(page.getByRole('status',{name:'正在启动 WhaleDeck 协同平台',exact:true})).toHaveCount(0)
  await expect(page.getByRole('heading',{name:'使用工作站账号登录',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'使用用户名和密码登录',exact:true})).toBeVisible()
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.equal(privateRequests,0)
  await page.screenshot({path:join(extra,'login.png')});assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  await writeFile(join(extra,'verified.json'),JSON.stringify({revision,newAnonymousContext:true,originalLogoutContext:false,loginFormVisible:true,authenticationPerformed:false,fileWrites:0,newIntents:0,privateRequests,bffStatus:401}),{mode:0o600,flag:'wx'})
  console.log(JSON.stringify({partialLogoutAnonymousPage:'passed',originalLogoutContext:false,authenticationPerformed:false,newIntents:0}))
}catch(error){console.error(JSON.stringify({partialLogoutAnonymousPage:'failed',failureType:error?.name}));process.exitCode=1}
finally{if(browser)await browser.close()}
