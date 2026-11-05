// Read-only installed-page observation contract. No browser, mutation or replay.
import assert from 'node:assert/strict'
export const stateText={Ready:'预检已完成，尚未恢复ACL',Succeeded:'递归ACL恢复已完成',Canceled:'已取消',Partial:'部分修改，保护已保留',Unknown:'结果未知，保护已保留',Acknowledged:'已处置历史结果，不代表整树修改成功'}
export const acknowledgementWarning='管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。'
export const unknownWarning='当前结果仍含不确定性。已有计数仅表示聚合回执已证明的部分，0不代表未发生修改；没有回执时保持未证明。请核对原任务、逐项记录和HDD历史。'
export function jobState(stage,state){
 assert.ok(stage==='planning'?['Ready','Canceled'].includes(state):stage==='execution'&&['Succeeded','Canceled','Partial','Unknown','Acknowledged'].includes(state))
 return {Ready:'Succeeded',Succeeded:'Succeeded',Canceled:'Canceled',Partial:'Running',Unknown:'Running',Acknowledged:'Failed'}[state]
}
export function requirePhase(job,state){
 const phase={Partial:'AwaitingContainerRecursiveAclRestoreResolution',Unknown:'AwaitingContainerRecursiveAclRestoreReceipt',Acknowledged:'Acknowledged'}[state]
 if(phase)assert.equal(job.phase,phase)
}
