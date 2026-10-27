import { EventEmitter } from 'node:events'
import assert from 'node:assert/strict'
import test from 'node:test'
import { observeUpload } from './browser_upload_probe.mjs'
const path='/api/v1/containers/'+'a'.repeat(64)+'/files/uploads/11111111-2222-3333-4444-555555555555/content'
function start(session,requestId='own',method='PUT',url='http://localhost'+path){session.emit('Network.requestWillBeSent',{requestId,request:{method,url}})}
test('only the selected PUT can provide a cancellation terminal event',()=>{
  const session=new EventEmitter(),probe=observeUpload(session,path)
  start(session,'read','GET');start(session,'foreign','PUT','http://localhost'+path.replace('11111111','99999999'))
  session.emit('Network.loadingFailed',{requestId:'read',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.equal(probe.snapshot().seen,false)
  start(session);session.emit('Network.loadingFailed',{requestId:'foreign',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.equal(probe.snapshot().terminal,null)
  session.emit('Network.loadingFailed',{requestId:'own',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.deepEqual(probe.snapshot(),{seen:true,terminal:'failed',networkError:'net::ERR_ABORTED',canceled:true,responseStatus:null})
  probe.dispose();assert.equal(session.eventNames().length,0)
})
test('an observation or disposal never fabricates network completion',()=>{
  const session=new EventEmitter(),probe=observeUpload(session,path);start(session)
  assert.equal(probe.snapshot().terminal,null);probe.dispose()
  session.emit('Network.loadingFinished',{requestId:'own'})
  assert.equal(probe.snapshot().terminal,null)
})
test('a completed transfer cannot later be relabeled canceled',()=>{
  const session=new EventEmitter(),probe=observeUpload(session,path);start(session)
  session.emit('Network.responseReceived',{requestId:'own',response:{status:202}})
  session.emit('Network.loadingFinished',{requestId:'own'})
  session.emit('Network.loadingFailed',{requestId:'own',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.equal(probe.snapshot().terminal,'finished');assert.equal(probe.snapshot().canceled,false)
  assert.equal(probe.snapshot().responseStatus,202);probe.dispose()
})
test('unknown error text is discarded instead of leaking arbitrary content',()=>{
  const session=new EventEmitter(),probe=observeUpload(session,path);start(session)
  session.emit('Network.loadingFailed',{requestId:'own',canceled:false,errorText:'private unrelated text'})
  assert.equal(probe.snapshot().networkError,'unclassified-network-error');probe.dispose()
})
test('a second request to the same path cannot replace the original transfer',()=>{
  const session=new EventEmitter(),probe=observeUpload(session,path);start(session);start(session,'retry')
  session.emit('Network.loadingFailed',{requestId:'retry',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.equal(probe.snapshot().terminal,null);probe.dispose()
})
test('an original CDP start event is scoped without fabricating a terminal',()=>{
  const session=new EventEmitter()
  const event={requestId:'original',request:{method:'PUT',url:'http://localhost'+path}}
  const probe=observeUpload(session,path,event)
  assert.equal(probe.snapshot().seen,true);assert.equal(probe.snapshot().terminal,null)
  session.emit('Network.loadingFailed',{requestId:'original',canceled:true,errorText:'net::ERR_ABORTED'})
  assert.equal(probe.snapshot().terminal,'failed');probe.dispose()
})
test('rejects unrelated, incomplete and newline-suffixed observer paths',()=>{
  for(const value of [path+'\n',path.replace('content','download'),'/api/v1/files/download',path.replace('a'.repeat(64),'a')])
    assert.throws(()=>observeUpload(new EventEmitter(),value),/invalid-upload-observer-path/)
})
