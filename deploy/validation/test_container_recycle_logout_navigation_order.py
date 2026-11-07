"""Controlled observer regression: no shared-cookie API reads before callback.

The page/expect doubles model a delayed callback, not a live identity acceptance.
"""
import json,subprocess,unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parent
NODE=r'''
import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import vm from 'node:vm'
const helper=process.argv[1],mode=process.argv[2],base='http://127.0.0.1:8080',events=[],reads=[],frame={}
let current='http://127.0.0.1:8081/if/flow/default-invalidation-flow/',callbackObserved=false
const responses=[],requests=[]
const wait=(list,predicate)=>new Promise((resolve,reject)=>list.push({predicate,resolve,reject}))
const fire=(list,value)=>{for(const w of list)if(w.predicate(value))w.resolve(value)}
const request=(url,method='GET',main=true)=>({url:()=>url,method:()=>method,isNavigationRequest:()=>true,frame:()=>main?frame:{}})
const response=(url,method='GET',main=true)=>({url:()=>url,status:()=>302,request:()=>request(url,method,main)})
const context={request:{get:async url=>{reads.push({url,beforeCallback:!callbackObserved});assert.ok(callbackObserved,'SHARED_COOKIE_READ_BEFORE_RP_CALLBACK');return{status:()=>url.startsWith(base+'/')?401:403}}}}
const page={
  mainFrame:()=>frame,
  getByRole:(kind,{name})=>({isVisible:async()=>name==='打开用户菜单',click:async()=>{if(name==='退出登录'){
    fire(responses,response(base+'/api/v1/auth/logout','POST'));fire(requests,request('http://127.0.0.1:8081/application/o/provider/end-session/'))
    if(mode!=='delayed')current=base+'/login'
    // An iframe callback is deliberately insufficient.
    fire(responses,response(base+'/signout-callback-oidc/external','GET',false))
    setTimeout(()=>{
      if(mode==='missing'){for(const w of [...responses,...requests])w.reject(new Error('CALLBACK_NOT_OBSERVED'));return}
      callbackObserved=true;current=base+'/login';fire(responses,response(base+'/signout-callback-oidc/external'))
    },10)
  }}}),
  locator:()=>({evaluate:async()=>true}),
  waitForResponse:predicate=>wait(responses,predicate),waitForRequest:predicate=>wait(requests,predicate)
}
const expect=value=>({toBeInViewport:async()=>{},toHaveURL:async predicate=>{events.push('callback-wait');assert.ok(callbackObserved,'LOGIN_ROUTE_IS_NOT_A_CALLBACK');assert.equal(reads.length,0);assert.ok(predicate(new URL(current)))}})
expect.poll=callback=>({toBe:async expected=>assert.equal(await callback(),expected)})
const module=new vm.SourceTextModule(await readFile(helper,'utf8'))
await module.link(async name=>{const exports=name==='node:assert/strict'?{default:assert}:{expect};return new vm.SyntheticModule(Object.keys(exports),function(){for(const[k,v]of Object.entries(exports))this.setExport(k,v)})})
await module.evaluate();let receipt,error
try{receipt=await module.namespace.logoutFromUserMenu(page,context,base,undefined,event=>{events.push(event.phase)})}catch(e){error=e.message}
if(mode==='missing'){assert.equal(error,'CALLBACK_NOT_OBSERVED');assert.equal(reads.length,0);assert.ok(events.includes('failed'));assert.ok(!receipt)}
else{assert.ok(!error,error);assert.equal(receipt.rpCallbackObserved,true);assert.equal(receipt.finalLoginPage,true);assert.equal(receipt.bffStatus,401);assert.equal(receipt.identityStatus,403);assert.equal(reads.length,2);assert.ok(reads.every(x=>!x.beforeCallback));assert.ok(events.indexOf('rp-callback-observed')<events.indexOf('login-route'));assert.ok(events.indexOf('login-route')<events.indexOf('sessions-cleared'))}
console.log(JSON.stringify({mode,passed:true,sharedCookieReads:reads.length}))
'''


class LogoutNavigationOrderTests(unittest.TestCase):
    def run_case(self,mode):
        r=subprocess.run(['node','--experimental-vm-modules','--input-type=module','-e',NODE,str(ROOT/'installed_ui_logout.mjs'),mode],capture_output=True,text=True,timeout=10)
        self.assertEqual(r.returncode,0,r.stderr);value=json.loads(r.stdout);self.assertTrue(value['passed']);return value

    def test_delayed_callback_finishes_before_any_shared_cookie_api_read(self):
        self.assertEqual(self.run_case('delayed')['sharedCookieReads'],2)

    def test_transient_login_route_and_iframe_do_not_replace_real_callback(self):
        self.assertEqual(self.run_case('transient')['sharedCookieReads'],2)

    def test_missing_callback_fails_without_polling_or_manual_redirect(self):
        self.assertEqual(self.run_case('missing')['sharedCookieReads'],0)


if __name__=='__main__':unittest.main()
