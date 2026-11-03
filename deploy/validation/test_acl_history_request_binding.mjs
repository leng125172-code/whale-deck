import assert from 'node:assert/strict'
import {test} from 'node:test'
import {isJobPageResponse} from './installed_ui_permission_session.mjs'

const base='http://127.0.0.1:8080'
function response(path,method='GET'){return {url:()=>new URL(path,base).href,request:()=>({method:()=>method})}}
test('the actual 100-row page wins regardless of AppShell 50-row completion order',()=>{
 const shell=response('/api/v1/jobs?take=50'),page=response('/api/v1/jobs?take=100')
 for(const order of [[shell,page],[page,shell]])assert.deepEqual(order.filter(r=>isJobPageResponse(r,base)),[page])
})
test('foreign, ambiguous, default, event and write responses never identify that page',()=>{
 for(const path of ['/api/v1/jobs','/api/v1/jobs?take=100&take=50','/api/v1/jobs?take=100&extra=x','/api/v1/jobs/id/events','http://example.invalid/api/v1/jobs?take=100'])
  assert.equal(isJobPageResponse(response(path),base),false)
 assert.equal(isJobPageResponse(response('/api/v1/jobs?take=100','POST'),base),false)
})
