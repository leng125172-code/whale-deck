"""Read-only task facts versus original SQL/native plan and rendered DOM.

No credentials, MAC secrets, mutation, replay or replacement of missing facts.
Native receipt, source/HDD and whole-tree proofs run separately in the parent.
"""
import json
import re
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_business_proof import managed_plan,json_projection,private_json

SOURCES=('validate_recursive_acl_restore_job_results.py','validate_recursive_acl_restore_job_results.mjs','verify_recursive_acl_restore_job_results.py','installed_ui_restore_job_results.mjs','recursive_acl_restore_job_results_proof.py','test_recursive_acl_restore_job_results_proof.py')
FIELDS='schemaVersion intentId jobId stage state code aclType entryCount fileCount directoryCount changeCount completedCount changedCount abortedCount notStartedCount unknownCount receiptState bindingCurrent historyMayExist historyVerified historyAvailable publishBarrier sourceEntryCount sourceFileCount sourceDirectoryCount sourceChangedCount'.split()
COUNTS=('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')
IMAGES={'job-planning-counts.png','job-planning-metadata.png','job-execution-counts.png','job-execution-metadata.png'}

def column(key):return re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()

def projection(row,plan,*,execution):
    assert type(execution) is bool
    source=plan['restoreSource']
    out=dict(schemaVersion=1,intentId=row['id'],jobId=row['execution_job_id'] if execution else row['id'],stage='execution' if execution else 'planning',state=row['state'],code=row['result_code'],aclType=source['changedAclType'])
    out.update({k:plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')})
    out.update({k:row[column(k)] for k in (*COUNTS,'bindingCurrent','historyMayExist','historyVerified','historyAvailable','publishBarrier')})
    out['receiptState']=row['root_state']
    out.update({k:source[v] for k,v in zip(('sourceEntryCount','sourceFileCount','sourceDirectoryCount','sourceChangedCount'),('entryCount','fileCount','directoryCount','changeCount'))})
    assert set(out)==set(FIELDS)
    return out

def display(s):
    count=lambda v:'未证明' if v is None else str(v)
    fact=lambda v:'未证明' if v is None else '是' if v else '否'
    return dict(identity=[s['intentId'],'预检' if s['stage']=='planning' else '执行',s['aclType']],
        plan=[count(s[k]) for k in ('entryCount','fileCount','directoryCount','changeCount')],
        actual=[count(s[k]) for k in COUNTS],
        source=[count(s[k]) for k in ('sourceEntryCount','sourceFileCount','sourceDirectoryCount','sourceChangedCount')],
        metadata=[s['receiptState'] or '未取得',*[fact(s[k]) for k in ('bindingCurrent','historyMayExist','historyVerified','historyAvailable','publishBarrier')],s['code']])

def safe_summary(raw):
    # HTTP ResultJson must remain text. SQL jsonb can be an object.
    if type(raw) is dict:value=json_projection(raw);size=len(json.dumps(raw,ensure_ascii=False).encode())
    else:value=private_json(raw);size=len(raw.encode())
    assert size<=4096 and type(value) is dict and set(value)==set(FIELDS) and len(value)==26
    return value

def api_job(job,sql,expected):
    assert job['id']==sql['id']==expected['jobId']
    assert job['jobType']==sql['job_type']==('containers.files.recursive-acl-restore-plan' if expected['stage']=='planning' else 'containers.files.recursive-acl-restore')
    assert expected['stage'] in ('planning','execution')
    assert expected['state'] in (('Ready','Canceled') if expected['stage']=='planning' else ('Succeeded','Canceled','Partial','Unknown','Acknowledged'))
    assert job['state']==sql['state']==dict(Ready='Succeeded',Succeeded='Succeeded',Canceled='Canceled',Partial='Running',Unknown='Running',Acknowledged='Failed')[expected['state']]
    phase=dict(Partial='AwaitingContainerRecursiveAclRestoreResolution',Unknown='AwaitingContainerRecursiveAclRestoreReceipt',Acknowledged='Acknowledged').get(expected['state'])
    if phase:assert job['phase']==sql['phase']==phase
    assert type(job['resultJson']) is str
    typed_equal(safe_summary(job['resultJson']),expected);typed_equal(safe_summary(sql['result_json']),expected)

def job_proof(observed,sql,expected,*,prefix=None):
    api_job(observed['job'],sql,expected);api_job(observed['listJob'],sql,expected)
    typed_equal(observed['display'],display(expected))
    typed_equal(observed['request'],dict(method='GET',pathname='/api/v1/jobs/'+expected['jobId'],status=200))
    typed_equal(observed['listRequest'],dict(method='GET',pathname='/api/v1/jobs',status=200,query=dict(take='100')))
    typed_equal(observed['viewport'],dict(width=320,height=600));assert observed['theme']=='dark'
    assert observed['stateText']==dict(Ready='预检已完成，尚未恢复ACL',Succeeded='递归ACL恢复已完成',Canceled='已取消',Partial='部分修改，保护已保留',Unknown='结果未知，保护已保留',Acknowledged='已处置历史结果，不代表整树修改成功')[expected['state']]
    if expected['state']=='Acknowledged':assert observed['acknowledgementWarning']=='管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。'
    assert '历史事实' in observed['historical'] and '来源原修改数与本次实际修改数分别记录' in observed['historical']
    if prefix is not None:
        typed_equal(observed['labels'],dict(identity=['原意图','阶段','恢复ACL类型'],plan=['计划对象','文件','目录（含根）','计划修改'],actual=['已完成核对','实际修改','已中止','未开始','待证明'],source=['来源对象','来源文件','来源目录','来源原修改'],metadata=['原回执','当时对象绑定有效','可能已有历史','历史已验证','当时历史可用','当时发布围栏保留','结果码']))
    prefix=prefix or f"job-{expected['stage']}"
    assert observed['screenshots']==[prefix+'-counts.png',prefix+'-metadata.png']

def ready_job_proof(signal,initial):
    row=initial['row'];plan=managed_plan(row['plan_json']);s=projection(row,plan,execution=False)
    assert row['state']=='Ready' and s['receiptState'] is None and all(s[k] is None for k in COUNTS)
    assert [s[k] for k in ('entryCount','fileCount','directoryCount','changeCount','sourceChangedCount')]==[26,25,1,26,26]
    assert signal['ready']['id']==row['id'];job_proof(signal['jobResults']['planning'],initial['planJob'],s)
    return s

def case_proof(ui,initial,terminal):
    ready=ready_job_proof(ui['readySignal'],initial);packet=ui['jobResults']
    typed_equal(packet['planning'],ui['readySignal']['jobResults']['planning'])
    row=terminal['row'];assert row['state']=='Canceled' and row['cancel_requested_at_utc'] is None
    actual=projection(row,managed_plan(row['plan_json']),execution=True)
    assert actual['receiptState']=='Aborted' and actual['changedCount']==actual['unknownCount']==0
    job_proof(packet['execution'],terminal['executionJob'],actual)
    # Planning job is a Ready historical snapshot, never rewritten to Canceled.
    typed_equal(terminal['planJob'],initial['planJob']);api_job(packet['historicalPlanning'],terminal['planJob'],ready)
    for key,status in (('originalAnonymous',401),('adminAnonymous',401),('ordinaryDenied',403),('ordinaryAnonymous',401)):typed_equal(packet[key],[status]*3)
    assert type(packet['ordinaryJobUiRequests']) is int and packet['ordinaryJobUiRequests']==0
    return dict(planningReady=True,executionCanceled=True,planningSnapshotPreserved=True,fields=26,actualCounts=[actual[k] for k in COUNTS],planningUnprovedCounts=True,sourceChangedCount=actual['sourceChangedCount'],ordinaryAndAnonymousDenied=True)
