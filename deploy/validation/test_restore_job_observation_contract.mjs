// Pure observation-contract checks; no mocked API or claim of real UI acceptance.
import {test} from 'node:test'
import assert from 'node:assert/strict'
import {jobState,requirePhase,stateText,acknowledgementWarning} from './restore_job_observation_contract.mjs'
test('existing Ready, success and cancellation states retain their Job states',()=>{
 assert.equal(jobState('planning','Ready'),'Succeeded');assert.equal(jobState('execution','Succeeded'),'Succeeded');assert.equal(jobState('execution','Canceled'),'Canceled')
})
test('unresolved jobs remain Running and acknowledged is Failed',()=>{
 assert.equal(jobState('execution','Partial'),'Running');assert.equal(jobState('execution','Unknown'),'Running');assert.equal(jobState('execution','Acknowledged'),'Failed')
})
test('fault execution states cannot appear as planning results',()=>{
 for(const state of ['Partial','Unknown','Acknowledged'])assert.throws(()=>jobState('planning',state))
 assert.throws(()=>jobState('other','Ready'))
})
test('original fault phase must agree with observed state',()=>{
 for(const [state,phase] of Object.entries({Partial:'AwaitingContainerRecursiveAclRestoreResolution',Unknown:'AwaitingContainerRecursiveAclRestoreReceipt',Acknowledged:'Acknowledged'})){
  requirePhase({phase},state);assert.throws(()=>requirePhase({phase:'Completed'},state))
 }
})
test('unknown and acknowledgement labels cannot claim success',()=>{
 assert.equal(stateText.Unknown,'结果未知，保护已保留');assert.match(stateText.Acknowledged,/不代表整树修改成功/);assert.match(acknowledgementWarning,/原计数与不确定性保留/)
})
