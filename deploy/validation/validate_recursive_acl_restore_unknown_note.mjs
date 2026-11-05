import assert from 'node:assert/strict'
import {readFile,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
import {observeNotes} from './installed_ui_restore_note_visibility.mjs'
import {observeJob} from './installed_ui_restore_job_results.mjs'

let browser,phase='startup'
process.once('SIGTERM',()=>{void(async()=>{if(browser)await browser.close().catch(()=>{});process.exit(143)})()})
try{
 const {fixture,credentials}=JSON.parse(await readFile('/dev/stdin','utf8')),base='http://127.0.0.1:8080'
 assert.match(fixture.jobId,/^[a-f0-9-]{36}$/);assert.equal(process.argv[2],fixture.revision)
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(30000)
 const page=await context.newPage();phase='visible-login';const admin=await loginVisible(page,context,base,'/jobs',true,credentials)
 assert.equal((await(await context.request.get(base+'/api/v1/system/status')).json()).build.sourceRevision,fixture.revision)
 let domainWrites=0;context.on('request',r=>{const p=new URL(r.url()).pathname;if(p.startsWith('/api/v1/')&&!p.startsWith('/api/v1/auth/')&& !['GET','HEAD'].includes(r.method()))domainWrites++})
 phase='current-acknowledged-job';const job=await observeJob(page,context,base,fixture.jobId,'execution','Acknowledged',fixture.evidence,'job-fault-acknowledged-execution')
 const cases=[]
 for(const width of [320,1440])for(const theme of ['dark','light']){phase=`note-${width}-${theme}`;cases.push(await observeNotes(page,base,fixture.jobId,fixture.evidence,width,theme))}
 phase='normal-logout';const logout=await logoutFromUserMenu(page,context,base),anonymous=[]
 for(const path of ['/api/v1/jobs?take=100','/api/v1/jobs/'+fixture.jobId,'/api/v1/files/container-recursive-acl-restores/recovery/'+fixture.intentId])anonymous.push((await context.request.get(base+path)).status())
 assert.deepEqual(anonymous,[401,401,401]);assert.equal(domainWrites,0)
 await writeFile(join(fixture.evidence,'browser.json'),JSON.stringify({revision:fixture.revision,jobId:fixture.jobId,subject:admin.subject,job,cases,domainWrites,logout,anonymous,currentAcknowledgedOnly:true}))
 await browser.close();browser=undefined;console.log(JSON.stringify({currentUnknownNoteVisibility:'passed',cases:4,domainWrites:0}))
}catch(error){console.error(JSON.stringify({currentUnknownNoteVisibility:'failed',phase,type:error?.name}));process.exitCode=1}
finally{if(browser)await browser.close().catch(()=>{})}
