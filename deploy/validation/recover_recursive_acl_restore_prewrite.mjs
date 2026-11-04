// One original failed QA task only; visible current-admin acknowledgement, never Apply.
import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {chromium,expect} from '../../web/node_modules/@playwright/test/index.mjs'
import {loginVisible,writeSignal,waitSignal} from './installed_ui_permission_session.mjs'
import {logoutFromUserMenu} from './installed_ui_logout.mjs'
let browser,phase='startup'
try{
 const f=JSON.parse(await readFile('/dev/stdin','utf8')),base='http://127.0.0.1:8080';assert.match(f.evidence,/\/recursive-acl-restore-prewrite-cancel-bd15b60dd0914c08a8728c1758cf9259$/)
 browser=await chromium.launch({executablePath:'/usr/bin/google-chrome',headless:true});const context=await browser.newContext({viewport:{width:320,height:600},colorScheme:'dark',reducedMotion:'reduce'});context.setDefaultTimeout(30000);const page=await context.newPage()
 phase='normal-current-admin-login';const profile=await loginVisible(page,context,base,'/files',true,f.credentials);assert.notEqual(profile.subject,f.originalSubject)
 assert.equal((await context.request.get(base+`/api/v1/containers/${f.containerId}/files/recursive-acl-restores/${f.id}`)).status(),404)
 const endpoint='/api/v1/files/container-recursive-acl-restores/recovery/'+f.id;let acknowledgements=0;page.on('request',r=>{if(new URL(r.url()).pathname===endpoint+'/acknowledge'&&r.method()==='POST')acknowledgements++})
 await page.getByRole('button',{name:'容器递归ACL恢复异常',exact:true}).click();const dialog=page.getByRole('dialog',{name:'容器递归ACL恢复异常处置',exact:true}),row=dialog.locator('.recursive-acl-restore-recovery__row').filter({hasText:f.id});await expect(row).toHaveCount(1);await expect(row).toContainText('结果未知，保护已保留')
 await row.getByRole('button',{name:'处置历史结果',exact:true}).click();await page.screenshot({path:join(f.evidence,'recovery-unknown.png')});await dialog.getByRole('button',{name:'暂不处置',exact:true}).click();assert.equal(acknowledgements,0)
 await writeSignal(f.evidence,'recovery-dismissed.json',{id:f.id,subject:profile.subject,acknowledgements});assert.equal((await waitSignal(f.evidence,'recovery-allow-verified.json')).originalFactsPreserved,true)
 phase='explicit-current-admin-ack';await row.getByRole('button',{name:'处置历史结果',exact:true}).click();const response=page.waitForResponse(r=>new URL(r.url()).pathname===endpoint+'/acknowledge'&&r.request().method()==='POST');await dialog.getByRole('button',{name:'确认处置并释放保护',exact:true}).click();assert.equal((await response).status(),202)
 await expect(dialog.getByRole('status')).toHaveText('已处置部分修改或未知历史结果并释放保护；不代表整树恢复成功，不会续做或再次恢复ACL，不删除原对象。');await page.screenshot({path:join(f.evidence,'recovery-acknowledged.png')});const r=await context.request.get(base+endpoint);assert.equal(r.status(),200);const result=await r.json();assert.equal(result.state,'Acknowledged');assert.equal(acknowledgements,1)
 await dialog.getByRole('button',{name:'关闭',exact:true}).click();phase='normal-current-admin-logout';const logout=await logoutFromUserMenu(page,context,base);const anonymous=(await context.request.get(base+endpoint)).status();assert.equal(anonymous,401)
 await writeSignal(f.evidence,'recovery-result.json',{id:f.id,subject:profile.subject,result,acknowledgements,logout,anonymous});assert.equal((await waitSignal(f.evidence,'recovery-done-verified.json')).ackAndPhysicalPreserved,true)
 await context.close();await browser.close();browser=undefined;console.log(JSON.stringify({originalUnknownRecovery:'passed',originalApplyNotReplayed:true}))
}catch(error){console.error(JSON.stringify({originalUnknownRecovery:'failed',phase,type:error?.name}));if(browser)await browser.close().catch(()=>{});process.exitCode=1}
