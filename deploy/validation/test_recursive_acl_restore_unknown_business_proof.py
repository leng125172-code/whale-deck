"""Constructed SQL/API/DOM contracts only; not real DB, identity or disk evidence."""
import copy
import json
import unittest

from recursive_acl_restore_unknown_business_proof import require_unknown, require_acknowledged, PREFIX
from recursive_acl_restore_job_results_proof import projection, display, UNKNOWN_WARNING, COUNTS
from recursive_acl_restore_business_proof import managed_plan
from test_recursive_acl_restore_partial_business_proof import fixture as partial_fixture


def observation(previous, sql, prefix):
    value = copy.deepcopy(previous)
    summary = sql['result_json']
    for key in ('job', 'listJob'):
        value[key].update(state=sql['state'], phase=sql['phase'], resultJson=json.dumps(summary))
    value.update(display=display(summary), unknownWarning=UNKNOWN_WARNING,
        historical='历史事实；来源原修改数与本次回执计数分别记录',
        stateText='结果未知，保护已保留' if summary['state'] == 'Unknown' else '已处置历史结果，不代表整树修改成功',
        screenshots=[prefix + '-counts.png', prefix + '-metadata.png'])
    value['labels']['actual'][1] = '回执已证明修改'
    return value


def fixture():
    value = partial_fixture()
    value['initial']['row']['cancel_requested_at_utc'] = None
    for name, acknowledged in (('data', False), ('after', True)):
        data = value[name]
        row = data['row']
        row.update(state='Acknowledged' if acknowledged else 'Unknown', root_state='Unknown',
            result_code=PREFIX + ('UNKNOWN_ACKNOWLEDGED' if acknowledged else 'RESULT_UNKNOWN'),
            completed_count=0, changed_count=0, aborted_count=0, not_started_count=0,
            unknown_count=26, outcome_digest='', cancel_requested_at_utc=None)
        receipt = json.loads(row['receipt_json'])
        receipt.update(State='Unknown', CompletedCount=0, ChangedCount=0, AbortedCount=0,
            NotStartedCount=0, UnknownCount=26, OutcomeDigest='')
        row['receipt_json'] = json.dumps(receipt)
        if acknowledged:
            row['acknowledgement_json'] = row['receipt_json']
        job = data['executionJob']
        job.update(phase='Acknowledged' if acknowledged else 'AwaitingContainerRecursiveAclRestoreReceipt',
            cancel_requested_at_utc=None, result_json=projection(row, managed_plan(row['plan_json']), execution=True))
        data['audits'] = [a for a in data['audits'] if not a['action'].endswith('cancel-requested')]
        data['events'] = 5 if acknowledged else 4
        for audit in data['audits']:
            if audit['action'].rsplit('.', 1)[-1] == 'result':
                audit['result'] = 'Unknown'
            if audit['action'].endswith('partial-acknowledged'):
                audit.update(action='containers.files.recursive-acl-restore.unknown-acknowledged', result='Unknown')
        api = value['ack_api' if acknowledged else 'api']
        api.update(state=row['state'], resultCode=row['result_code'])
        api['receipt'] = copy.deepcopy(api['receipt'])
        api['receipt'].update(state='Unknown', completedCount=0, changedCount=0, abortedCount=0,
            notStartedCount=0, unknownCount=26, outcomeDigest='')
    value['job'] = observation(value['job'], value['data']['executionJob'], 'job-fault-unknown-execution')
    value['ack_job'] = observation(value['ack_job'], value['after']['executionJob'], 'job-fault-acknowledged-execution')
    return value


def unknown(value):
    return require_unknown(value['data'], value['ready'], value['initial'], value['baseline'],
        value['source'], value['api'], value['job'])


def ack(value, actor='resolver'):
    return require_acknowledged(value['data'], value['after'], actor, value['ack_api'], value['ack_job'])


class UnknownBusinessProofTests(unittest.TestCase):
    def test_known_unknown_receipt_and_ack_keep_all_26_counts_unproved(self):
        value = fixture()
        request, receipt = unknown(value)
        self.assertEqual([receipt[k] for k in COUNTS], [0, 0, 0, 0, 26])
        self.assertEqual(request['plan']['entryCount'], 26)
        self.assertEqual(ack(value)[1]['state'], 'Unknown')

    def test_partial_and_missing_receipt_are_separate_unaccepted_cases(self):
        with self.assertRaises(AssertionError):
            unknown(partial_fixture())
        for bad in (None, '{}', 'null'):
            value = fixture()
            value['data']['row']['receipt_json'] = bad
            with self.subTest(receipt=bad), self.assertRaises(AssertionError):
                unknown(value)

    def test_other_unknown_causes_are_not_signed_as_fixed_rename_case(self):
        for code in ('OUTPUT_INVALID', 'BINDING_CHANGED', 'PARTIAL_REQUIRES_ACKNOWLEDGEMENT'):
            value = fixture()
            value['data']['row']['result_code'] = PREFIX + code
            with self.subTest(code=code), self.assertRaises(AssertionError):
                unknown(value)

    def test_unknown_must_remain_unfinished_and_protected_without_lease(self):
        for key, bad in (('completed_at_utc', 'now'), ('publish_barrier', False),
                ('lease_token', 'lease'), ('lease_expires_at_utc', 'future'), ('state', 'Succeeded')):
            value = fixture()
            value['data']['row'][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                unknown(value)
        value = fixture()
        value['data']['pending'] = 0
        with self.assertRaises(AssertionError):
            unknown(value)

    def test_no_cancellation_or_completed_outcome_can_be_invented(self):
        for key, bad in (('cancel_requested_at_utc', 'now'), ('outcome_digest', '2' * 64),
                ('binding_current', True), ('root_state', 'Partial')):
            value = fixture()
            value['data']['row'][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                unknown(value)

    def test_unknown_counters_cannot_be_backfilled_from_known_child(self):
        value = fixture()
        row = value['data']['row']
        receipt = json.loads(row['receipt_json'])
        row.update(completed_count=1, changed_count=1, unknown_count=25)
        receipt.update(CompletedCount=1, ChangedCount=1, UnknownCount=25)
        row['receipt_json'] = json.dumps(receipt)
        with self.assertRaises(AssertionError):
            unknown(value)

    def test_receipt_unknown_counters_require_strict_integer_and_exact_shape(self):
        for key, bad in (('UnknownCount', 0), ('ChangedCount', True), ('State', 'Completed'), ('Extra', 'private')):
            value = fixture()
            row = value['data']['row']
            receipt = json.loads(row['receipt_json'])
            receipt[key] = bad
            row['receipt_json'] = json.dumps(receipt)
            with self.subTest(key=key), self.assertRaises(AssertionError):
                unknown(value)

    def test_history_verification_and_source_digest_are_not_optional(self):
        for key, bad in (('history_verified', False), ('history_digest', ''), ('history_may_exist', False)):
            value = fixture()
            value['data']['row'][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                unknown(value)

    def test_ready_request_and_original_planning_snapshot_cannot_change(self):
        for key in ('login_binding', 'owner_hash', 'plan_hash', 'command_json'):
            value = fixture()
            value['initial']['row'][key] = 'other'
            with self.subTest(key=key), self.assertRaises(AssertionError):
                unknown(value)
        value = fixture()
        value['initial']['planJob']['result_json'] = {}
        with self.assertRaises(AssertionError):
            unknown(value)

    def test_audits_must_show_one_unknown_result_and_no_cancel(self):
        for change in ('missing', 'duplicate', 'actor', 'result', 'cancel'):
            value = fixture()
            audits = value['data']['audits']
            if change == 'missing':
                audits.pop()
            elif change == 'duplicate':
                audits.append(copy.deepcopy(audits[-1]))
            elif change == 'actor':
                audits[-1]['actor_subject'] = 'other'
            elif change == 'result':
                audits[-1]['result'] = 'Partial'
            else:
                audits[-1].update(action='containers.files.recursive-acl-restore.cancel-requested', result='Accepted')
            with self.subTest(change=change), self.assertRaises(AssertionError):
                unknown(value)

    def test_api_and_job_cannot_rename_unknown_to_success_or_another_execution(self):
        for target, key, bad in (('api', 'state', 'Succeeded'), ('api', 'version', 9),
                ('api', 'executionJobId', '12345678-1234-1234-1234-123456789abc'),
                ('job', 'stateText', '递归ACL恢复已完成')):
            value = fixture()
            value[target][key] = bad
            with self.subTest(target=target, key=key), self.assertRaises(AssertionError):
                unknown(value)

    def test_job_must_render_unknown_warning_and_receipt_proven_label(self):
        for target in ('job', 'ack_job'):
            for change in ('warning', 'label', 'historical'):
                value = fixture()
                job = value[target]
                if change == 'warning':
                    job['unknownWarning'] = ''
                elif change == 'label':
                    job['labels']['actual'][1] = '实际修改'
                else:
                    job['historical'] = '历史事实；来源原修改数与本次实际修改数分别记录'
                with self.subTest(target=target, change=change), self.assertRaises(AssertionError):
                    (unknown if target == 'job' else ack)(value)

    def test_ack_requires_other_actor_and_unknown_specific_result_code(self):
        value = fixture()
        with self.assertRaises(AssertionError):
            ack(value, 'original')
        value['after']['row']['result_code'] = PREFIX + 'PARTIAL_ACKNOWLEDGED'
        with self.assertRaises(AssertionError):
            ack(value)

    def test_ack_only_releases_protection_not_whole_tree_success(self):
        for key, bad in (('publish_barrier', True), ('completed_at_utc', None), ('root_state', 'Completed')):
            value = fixture()
            value['after']['row'][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                ack(value)
        value = fixture()
        value['after']['pending'] = 1
        with self.assertRaises(AssertionError):
            ack(value)

    def test_ack_preserves_original_execution_counts_plan_and_history(self):
        for key, bad in (('changed_count', 1), ('unknown_count', 25), ('history_digest', '3' * 64),
                ('execution_hash', '4' * 64), ('plan_json', '{}'), ('outcome_digest', '5' * 64)):
            value = fixture()
            value['after']['row'][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                ack(value)

    def test_ack_audit_and_job_must_preserve_unknown_and_failed_state(self):
        value = fixture()
        value['after']['audits'][-1]['action'] = 'containers.files.recursive-acl-restore.partial-acknowledged'
        with self.assertRaises(AssertionError):
            ack(value)
        value = fixture()
        value['after']['executionJob']['state'] = 'Succeeded'
        with self.assertRaises(AssertionError):
            ack(value)

    def test_acknowledgement_json_must_equal_current_validated_receipt(self):
        value = fixture()
        value['after']['row']['acknowledgement_json'] = value['data']['row']['plan_json']
        with self.assertRaises(AssertionError):
            ack(value)

    def test_ack_current_history_availability_can_change_without_erasing_original_facts(self):
        value = fixture()
        row = value['after']['row']
        receipt = json.loads(row['receipt_json'])
        receipt.update(HistoryAvailable=False, Replayed=True)
        row.update(receipt_json=json.dumps(receipt), acknowledgement_json=json.dumps(receipt),
            history_available=False, receipt_hash='5' * 64)
        value['ack_api']['receipt'].update(historyAvailable=False, replayed=True)
        row_summary = projection(row, managed_plan(row['plan_json']), execution=True)
        value['after']['executionJob']['result_json'] = row_summary
        value['ack_job'] = observation(value['ack_job'], value['after']['executionJob'], 'job-fault-acknowledged-execution')
        receipt = ack(value)[1]
        self.assertFalse(receipt['historyAvailable'])
        self.assertTrue(receipt['historyVerified'])
        self.assertEqual(receipt['unknownCount'], 26)


if __name__ == '__main__':
    unittest.main()
