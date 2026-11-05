"""Current acknowledged Unknown note visibility; never retroactive pre-ACK evidence."""
import math
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_business_proof import managed_plan
from recursive_acl_restore_job_results_proof import projection, job_proof, api_job, UNKNOWN_WARNING
from recursive_acl_restore_unknown_workflow import normal_logout

ACK_WARNING = '管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。'


def verify(packet, data, revision):
    row = data['row']
    assert row['state'] == 'Acknowledged' and row['root_state'] == 'Unknown'
    assert row['result_code'] == 'CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED'
    assert packet['revision'] == revision and packet['jobId'] == row['execution_job_id']
    assert packet['currentAcknowledgedOnly'] is True
    assert type(packet['subject']) is str and packet['subject'] and packet['subject'] not in (row['actor_subject'], row['acknowledged_by'])
    assert type(packet['domainWrites']) is int and packet['domainWrites'] == 0
    normal_logout(packet['logout']); typed_equal(packet['anonymous'], [401, 401, 401])
    summary = projection(row, managed_plan(row['plan_json']), execution=True)
    assert [summary[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')] == [0,0,0,0,26]
    job_proof(packet['job'], data['executionJob'], summary, prefix='job-fault-acknowledged-execution')
    assert type(packet['cases']) is list and len(packet['cases']) == 4
    seen = set()
    for case in packet['cases']:
        viewport = case['viewport']; width = viewport['width']; theme = case['theme']
        assert type(width) is int and width in (320, 1440) and type(viewport['height']) is int and viewport['height'] == 600
        assert theme in ('dark', 'light') and (width, theme) not in seen
        seen.add((width, theme)); api_job(case['job'], data['executionJob'], summary)
        assert len(case['regions']) == 2
        for region, name, text in zip(case['regions'], ('unknown','acknowledged'), (UNKNOWN_WARNING, ACK_WARNING)):
            assert region['name'] == name and region['text'] == text
            rect = region['rect']; assert set(rect) == {'x','y','width','height'}
            assert all(type(v) in (int, float) and math.isfinite(v) for v in rect.values())
            assert rect['x'] >= 0 and rect['y'] >= 0 and rect['width'] > 0 and rect['height'] > 0
            assert rect['x'] + rect['width'] <= width + 1 and rect['y'] + rect['height'] <= 601
        typed_equal(case['screenshots'], [f'note-{width}-{theme}-{name}.png' for name in ('unknown','acknowledged')])
    assert seen == {(width, theme) for width in (320,1440) for theme in ('dark','light')}
    return dict(currentAcknowledgedOnly=True, preAcknowledgementVisibilityAccepted=False,
        readOnlyDomainRequests=True, cases=4, noteImages=8, originalJobCountsPreserved=True)
