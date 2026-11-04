import { installContainerFilesFixture } from './container-files.mjs'
import { installFileFixture } from './files.mjs'

// Explicit isolated UI protocol fixture, never real Authentik/SQL/Root/HDD evidence.
export async function installContainerRecursiveAclRestoreFixture(page, options = {}) {
  const base = options.filePage ? await installFileFixture(page, options) : await installContainerFilesFixture(page, options)
  const cid = 'a'.repeat(64), id = 'ad12ad12-ad12-4d12-ad12-ad12ad12ad12', execution = 'bd12bd12-bd12-4d12-bd12-bd12bd12bd12'
  const target=base.state.target
  const source={captureId:'a'.repeat(32),historyDigest:'B'.repeat(64),createdAtUnixMs:0,sourceInstanceId:'666:2026-11-03T01:02:03Z',changedAclType:options.defaults?'default':'access',manifestDigest:'D'.repeat(64),entryCount:options.large?30:4,fileCount:options.large?28:2,directoryCount:2,changeCount:1}
  const row = { id, planningJobId: id, executionJobId: null, command: null, state: 'Queued', rootUid: null, rootGid: null, rootMode: null, executorUid: null, executorGid: null, entryCount: null, fileCount: null, directoryCount: null, changeCount: null, manifestDigest: null, restoreSource:null,
    readyExpiresAtUtc: null, resultCode: null, version: 0, historyMayExist: false, historyVerified: false, historyDigest: null, receipt: null, confirmation: null }
  const state = { historyPause:false,historyWaiting:false,historyRelease:()=>{},writes: [], reads: [], registered: false, holdPlan: false, holdRecursive: false, status: 200, canceled: false, lostQueue: false, lostConfirm: false, lostAck: false, key: '', malformed: false, pause: false, waiting: false, release: () => {} }
  function planned() {
    row.state = 'Ready'; row.rootUid = 1000; row.rootGid = 1001; row.rootMode = 16872; row.executorUid = 5100; row.executorGid = 5101
    row.entryCount = source.entryCount; row.fileCount = source.fileCount; row.directoryCount = source.directoryCount; row.changeCount = 2; row.restoreSource=structuredClone(source); row.manifestDigest = 'D'.repeat(64)
    row.readyExpiresAtUtc = new Date(Date.now() + 300000).toISOString()
    row.confirmation = { planHash: 'isolated-recursive-acl-confirmation', expiresAtUtc: row.readyExpiresAtUtc, warning: '预检未修改ACL；先保留HDD元数据，按文件/目录目标ACL逐项修改，不更改inode所有者。Partial/Unknown不自动续做或回滚。' }
    row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_READY'; row.version++
  }
  function receipt(kind) {
    const verified = ['Completed','Partial'].includes(kind)
    row.historyMayExist = kind !== 'Aborted'; row.historyVerified = verified; row.historyDigest = verified ? 'E'.repeat(64) : null
    row.receipt = { id: execution, containerId: cid, instanceId: row.command.location.instanceId, fingerprint: 'A'.repeat(64), targetProof: 'B'.repeat(64), state: kind,
      aclType: source.changedAclType, bindingCurrent: kind === 'Completed' && !options.superseded, replayed: false, completedCount: kind === 'Completed' ? source.entryCount : kind === 'Partial' ? 1 : 0, changedCount: kind === 'Completed' ? 2 : kind === 'Partial' ? 1 : 0,
      abortedCount: 0, notStartedCount: kind === 'Aborted' ? source.entryCount : kind === 'Partial' ? source.entryCount-1 : 0, unknownCount: kind === 'Unknown' ? source.entryCount : 0,
      historyMayExist: row.historyMayExist, historyVerified: verified, historyDigest: row.historyDigest ?? '', historyAvailable: verified, outcomeDigest: verified ? 'F'.repeat(64) : '' }
  }
  const response = () => structuredClone(row)
  const getResponse = () => {
    if (['Queued','Planning'].includes(row.state)) {
      if (state.holdPlan) { if (row.state !== 'Planning') { row.state = 'Planning'; row.version++ } }
      else planned()
    } else if (['Pending','Applying'].includes(row.state)) {
      if (state.canceled) { row.state = 'Canceled'; receipt('Aborted'); row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED'; row.version++ }
      else if (options.unresolved) { row.state = options.unresolved; receipt(options.unresolved); row.resultCode = options.unresolved === 'Partial' ? 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT' : 'CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN'; row.version++ }
      else if (state.holdRecursive) { if (row.state !== 'Applying') { row.state = 'Applying'; row.version++ } }
      else { row.state = options.superseded ? 'Superseded' : 'Succeeded'; receipt('Completed'); row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED'; row.version++ }
    }
    const result = response()
    if (state.malformed && result.receipt) result.receipt.changedCount = 999
    if(options.badSource && result.restoreSource)result.restoreSource.historyDigest="0".repeat(64)
    return result
  }
  await page.route('**/api/v1/containers/*/files/recursive-acl-restores**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname.split('/recursive-acl-restores')[1] || ''
    if (request.method() === 'GET') {
      state.reads.push(path)
      if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
      if (!state.registered || path.startsWith('/by-key/') && path !== '/by-key/' + state.key) return route.fulfill({ status: 404, json: { status: 404 } })
      if (state.pause) { state.waiting = true; await new Promise(resolve => { state.release = resolve }) }
      try { return await route.fulfill({ json: getResponse() }) } catch { return }
    }
    const body = request.postDataJSON(); state.writes.push({ path, body, key: request.headers()['idempotency-key'] })
    if (path === '') {
      state.registered = true; state.key = request.headers()['idempotency-key']
      row.command = { location: { containerId: cid, instanceId: body.instanceId, path: body.path }, captureId:body.captureId, expectedHistoryDigest:body.expectedHistoryDigest, expectedVersion: body.expectedVersion }
      if (options.loseQueue && !state.lostQueue) { state.lostQueue = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/confirm')) {
      row.executionJobId = execution; row.state = 'Pending'; row.confirmation = null; row.version++; row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_ACCEPTED'
      if (options.loseConfirm && !state.lostConfirm) { state.lostConfirm = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: response() })
    }
    if (path.endsWith('/cancel')) {
      state.canceled = true
      if (!row.executionJobId) { row.state = 'Canceled'; row.confirmation = null; row.resultCode = 'CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED' }
      row.version++; return route.fulfill({ status: 202, json: response() })
    }
    return route.fulfill({ status: 404, json: { status: 404 } })
  })
  if (options.filePage) {
    row.command = { location: { containerId: cid, instanceId: '777:2026-10-28T01:02:03.123456789Z', path: 'work/old-tree' }, captureId:source.captureId,expectedHistoryDigest:source.historyDigest, expectedVersion: 'A'.repeat(64) }
    planned(); row.executionJobId = execution; row.confirmation = null; row.state = options.unresolved ?? 'Unknown'; receipt(row.state)
    row.resultCode = row.state === 'Partial' ? 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT' : 'CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN'
    await page.route('**/api/v1/files/container-recursive-acl-restores/recovery**', async route => {
      const request = route.request(), path = new URL(request.url()).pathname.split('/recovery')[1] || ''
      if (request.method() === 'GET') {
        state.reads.push('recovery' + path)
        if (state.status !== 200) return route.fulfill({ status: state.status, json: { status: state.status } })
        return route.fulfill({ json: path ? response() : ['Unknown','Partial'].includes(row.state) ? [response()] : [] })
      }
      state.writes.push({ path: 'recovery' + path, body: request.postDataJSON(), key: request.headers()['idempotency-key'] })
      const partial = row.state === 'Partial'; row.state = 'Acknowledged'; row.version++; row.resultCode = partial ? 'CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_ACKNOWLEDGED' : 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED'
      if (options.loseAck && !state.lostAck) { state.lostAck = true; return route.abort('failed') }
      return route.fulfill({ status: 202, json: { id, state: 'Acknowledged' } })
    })
  }
  const acl=[{tag:'user_obj',id:null,permissions:0},{tag:'user',id:0,permissions:0},{tag:'group_obj',id:null,permissions:0},{tag:'mask',id:null,permissions:0},{tag:'other',id:null,permissions:0}]
  await page.route('**/files/recursive-acls/history**',async route=>{
    const url=new URL(route.request().url()),location={containerId:cid,instanceId:target.instanceId,path:url.searchParams.get('path')}
    if(options.historyFailure)return route.fulfill({status:503,json:{title:'unavailable'}})
    if(url.pathname.endsWith('/history'))return route.fulfill({json:{location,version:'C'.repeat(64),entries:[source],nextAfter:'',scannedCount:1,invalidCount:0,complete:true}})
    if(state.historyPause){state.historyWaiting=true;await new Promise(resolve=>{state.historyRelease=resolve});if(route.request().failure())return}
    const entries=[...Array.from({length:source.fileCount},(_,i)=>({relativePath:'a'+String(i).padStart(2,'0')+'.bin',kind:'File',originalMode:0,accessAcl:acl,defaultAcl:[]})),{relativePath:'folder',kind:'Directory',originalMode:0,accessAcl:acl,defaultAcl:[]},{relativePath:'',kind:'Directory',originalMode:0,accessAcl:acl,defaultAcl:acl}]
    const offset=Number(url.searchParams.get('offset')||0),slice=entries.slice(offset,offset+25),complete=offset+slice.length===source.entryCount
    return route.fulfill({json:{location,summary:source,offset,entries:slice,complete,nextOffset:complete?null:offset+slice.length}})
  })
  return { base, state, row, cid, id, execution,source }
}
