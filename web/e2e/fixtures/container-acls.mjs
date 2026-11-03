import {installContainerFilesFixture} from './container-files.mjs'
import {installFileFixture} from './files.mjs'
// UI-only protocol fixture. No real Authentik/Agent/PostgreSQL/filesystem mutations.
export async function installContainerAclFixture(page,options={}){
 const base=options.filePage?await installFileFixture(page,options):await installContainerFilesFixture(page,options)
 const cid='a'.repeat(64),id='ac12ac12-ac12-4c12-ac12-ac12ac12ac12',execution='bc12bc12-bc12-4c12-bc12-bc12bc12bc12'
 const basic=()=>[{tag:'user_obj',id:null,permissions:6},{tag:'group_obj',id:null,permissions:4},{tag:'other',id:null,permissions:0}]
 const named=()=>[{tag:'user_obj',id:null,permissions:6},{tag:'user',id:0,permissions:0},{tag:'group_obj',id:null,permissions:4},{tag:'mask',id:null,permissions:6},{tag:'other',id:null,permissions:0}]
 const mode=e=>e.find(v=>v.tag==='user_obj').permissions*64+(e.find(v=>v.tag==='mask')??e.find(v=>v.tag==='group_obj')).permissions*8+e.find(v=>v.tag==='other').permissions
 const row={id,planningJobId:id,executionJobId:null,command:null,state:'Queued',kind:null,originalUid:null,originalGid:null,executorUid:null,executorGid:null,originalAccessAcl:null,originalDefaultAcl:null,targetMode:null,noChange:null,readyExpiresAtUtc:null,resultCode:null,version:0,publishedVersion:null,aclConfirmed:null,historyMayExist:false,historyVerified:false,historyDigest:null,receipt:null,confirmation:null}
 const state={writes:[],reads:[],registered:false,holdPlan:false,holdAcl:false,status:200,canceled:false,lostQueue:false,lostConfirm:false,lostAck:false,key:'',malformed:false,pause:false,waiting:false,release:()=>{}}
 function planned(){
  row.state='Ready';row.kind=row.command.location.path.endsWith('资料')?'Directory':'File';row.originalUid=1000;row.originalGid=1001;row.executorUid=5100;row.executorGid=5101
  row.originalAccessAcl=basic();row.originalDefaultAcl=row.command.aclType==='default'?named():[];row.targetMode=row.command.aclType==='access'?mode(row.command.entries):mode(row.originalAccessAcl)
  row.noChange=JSON.stringify(row.command.entries)===JSON.stringify(row.command.aclType==='access'?row.originalAccessAcl:row.originalDefaultAcl)
  row.readyExpiresAtUtc=new Date(Date.now()+300000).toISOString();row.confirmation={planHash:'isolated-acl-confirmation',expiresAtUtc:row.readyExpiresAtUtc,warning:'完整替换所选一类ACL，显式mask影响组权限位；保留另一类ACL和UID/GID，不递归。'};row.resultCode='CONTAINER_ACL_READY';row.version++
 }
 let selectedSource=null
 function historical(location){return {location,entry:{captureId:'d'.repeat(32),historyDigest:'B'.repeat(64),createdAtUnixMs:0,sourceInstanceId:'666:2026-11-02T01:02:03Z',changedAclType:options.defaultRestore?'default':'access',kind:location.path.endsWith('资料')?'Directory':'File',originalMode:432,accessEntryCount:5,defaultEntryCount:0},accessAcl:named(),defaultAcl:[]}}
 function wrapped(intent){const source=structuredClone(selectedSource);if(state.sourceChanged)source.entry.captureId='e'.repeat(32);return {intent,source}}
 const response=()=>structuredClone(row)
 function receipt(rootState){return {id:execution,containerId:cid,instanceId:row.command.location.instanceId,fingerprint:'C'.repeat(64),targetProof:'D'.repeat(64),state:rootState,version:rootState==='Completed'?row.publishedVersion:'',bindingCurrent:rootState==='Completed'&&row.state!=='Superseded',replayed:false,kind:row.kind,aclConfirmed:rootState==='Unknown'?null:rootState==='Completed',noChange:row.noChange,historyMayExist:row.historyMayExist,historyVerified:row.historyVerified,historyDigest:row.historyDigest??'',aclType:row.command.aclType,targetMode:row.targetMode}}
 function getResponse(){
  if(['Queued','Planning'].includes(row.state)){if(state.holdPlan){if(row.state!=='Planning'){row.state='Planning';row.version++}}else planned()}
  else if(['Pending','Applying'].includes(row.state)){
   if(state.canceled){row.state='Canceled';row.aclConfirmed=false;row.resultCode='CONTAINER_ACL_CANCELED';row.receipt=receipt('Aborted');row.version++}
   else if(options.unknown){row.state='Unknown';row.historyMayExist=true;row.resultCode='CONTAINER_ACL_RESULT_UNKNOWN';row.receipt=receipt('Unknown');row.version++}
   else if(state.holdAcl){if(row.state!=='Applying'){row.state='Applying';row.version++}}
   else{row.state=options.superseded?'Superseded':'Succeeded';row.publishedVersion='F'.repeat(64);row.aclConfirmed=true;row.historyMayExist=row.historyVerified=true;row.historyDigest='E'.repeat(64);row.resultCode='CONTAINER_ACL_COMPLETED';row.receipt=receipt('Completed');row.version++}
  }
  const result=response();if(state.malformed&&result.receipt)result.receipt.aclConfirmed=null;return result
 }
 await page.route('**/api/v1/containers/*/files/acls**',async route=>{
  const request=route.request(),url=new URL(request.url()),path=url.pathname.split('/acls')[1]||''
  if(options.restore&&path.startsWith('/history')){
   const source=historical({containerId:cid,instanceId:url.searchParams.get('instanceId'),path:url.searchParams.get('path')})
   return route.fulfill({json:path==='/history'?{location:source.location,version:'C'.repeat(64),entries:[source.entry],nextAfter:'',scannedCount:1,invalidCount:0,complete:true}:source})
  }
  if(request.method()==='GET'){
   state.reads.push(path);if(state.status!==200)return route.fulfill({status:state.status,json:{status:state.status}})
   if(!state.registered||(path.includes('/by-key/')&&!path.endsWith('/by-key/'+state.key)))return route.fulfill({status:404,json:{status:404}})
   if(state.pause){state.waiting=true;await new Promise(resolve=>{state.release=resolve})}
   try{return await route.fulfill({json:path.startsWith('/restores/')?wrapped(getResponse()):getResponse()})}catch{return}
  }
  const body=request.postDataJSON();state.writes.push({path,body,key:request.headers()['idempotency-key']})
  if(options.restore&&path==='/restores'){
   if(state.queueStatus)return route.fulfill({status:state.queueStatus,json:{status:state.queueStatus,title:'private upstream error'}})
   state.registered=true;state.key=request.headers()['idempotency-key'];selectedSource=historical({containerId:cid,instanceId:body.instanceId,path:body.path})
   row.command={location:selectedSource.location,aclType:selectedSource.entry.changedAclType,entries:selectedSource.entry.changedAclType==='access'?selectedSource.accessAcl:selectedSource.defaultAcl,expectedVersion:body.expectedVersion}
   if(options.loseQueue&&!state.lostQueue){state.lostQueue=true;return route.abort('failed')}
   return route.fulfill({status:202,json:wrapped(response())})
  }
  if(path===''){state.registered=true;state.key=request.headers()['idempotency-key'];row.command={location:{containerId:cid,instanceId:body.instanceId,path:body.path},aclType:body.aclType,entries:body.entries,expectedVersion:body.expectedVersion};if(options.queueExecution){planned();row.executionJobId=execution;row.confirmation=null;row.state='Pending';return route.fulfill({status:202,json:getResponse()})}if(options.loseQueue&&!state.lostQueue){state.lostQueue=true;return route.abort('failed')}return route.fulfill({status:202,json:response()})}
  if(path.endsWith('/confirm')){row.executionJobId=execution;row.state='Pending';row.confirmation=null;row.version++;row.resultCode='CONTAINER_ACL_ACCEPTED';if(options.loseConfirm&&!state.lostConfirm){state.lostConfirm=true;return route.abort('failed')}return route.fulfill({status:202,json:response()})}
  if(path.endsWith('/cancel')){state.canceled=true;if(!row.executionJobId){row.state='Canceled';row.confirmation=null;row.resultCode='CONTAINER_ACL_CANCELED'}row.version++;return route.fulfill({status:202,json:response()})}
  return route.fulfill({status:404,json:{status:404}})
 })
 if(options.filePage){
  row.command={location:{containerId:cid,instanceId:'777:2026-10-28T01:02:03.123456789Z',path:'work/old.txt'},aclType:'access',entries:named(),expectedVersion:'A'.repeat(64)};planned();row.executionJobId=execution;row.confirmation=null;row.state='Unknown';row.historyMayExist=true;row.resultCode='CONTAINER_ACL_RESULT_UNKNOWN';row.receipt=receipt('Unknown')
  await page.route('**/api/v1/files/container-acls/recovery**',async route=>{
   const request=route.request(),path=new URL(request.url()).pathname.split('/recovery')[1]||''
   if(request.method()==='GET'){state.reads.push('recovery'+path);if(state.status!==200)return route.fulfill({status:state.status,json:{status:state.status}});return route.fulfill({json:path?response():row.state==='Unknown'?[response()]:[]})}
   state.writes.push({path:'recovery'+path,body:request.postDataJSON(),key:request.headers()['idempotency-key']});row.state='Acknowledged';row.version++;row.resultCode='CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED'
   if(options.loseAck&&!state.lostAck){state.lostAck=true;return route.abort('failed')}return route.fulfill({status:202,json:{id,state:'Acknowledged'}})
  })
 }
 return {base,state,row,cid,id,execution}
}
