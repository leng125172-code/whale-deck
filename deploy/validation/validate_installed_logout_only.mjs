// Auth-only visible gate. No file task, session injection or response replacement.
import assert from 'node:assert/strict'
import { chromium } from '../../web/node_modules/@playwright/test/index.mjs'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
let browser
try {
 const base=process.env.WHALEDECK_URL;assert.equal(base,'http://127.0.0.1:8080')
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true})
 const context=await browser.newContext({viewport:{width:320,height:600},reducedMotion:'reduce'})
 const page=await context.newPage();const user=await loginVisible(page,context,base)
 const proof=await logoutFromUserMenu(page,context,base)
 console.log(JSON.stringify({...proof,subject:user.subject,authOnly:true,fileMutations:0}))
 await context.close();await browser.close();browser=undefined
} catch(error) {
 console.error(JSON.stringify({authOnlyLogout:'failed',failureType:error?.name,frames:error?.stack?.split('\n').filter(line=>line.includes('installed_ui_logout.mjs:'))}))
 if(browser)await browser.close().catch(()=>{})
 process.exitCode=1
}
