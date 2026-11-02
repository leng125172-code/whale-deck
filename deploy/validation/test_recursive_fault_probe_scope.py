"""Offline rejection tests for the exact real-fault QA boundary, no live access."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

import bound_container_recursive_permission_cut as cut
from validate_container_recursive_permission_faults import PATHS, unchanged_except_modes, same_operation, lease_held, require_unknown_counts, require_waiting_deferred


class RecursiveFaultProbeScope(unittest.TestCase):
    def test_waiter_cannot_silently_publish_or_finish_behind_unknown_protection(self):
        value = {'row':{'id':'original','actor_subject':'current-admin','state':'Pending','root_started_at_utc':None,'publish_barrier':False,'completed_at_utc':None},
            'job':{'id':'original','actor_subject':'current-admin','state':'Running','completed_at_utc':None},'accepted':1}
        require_waiting_deferred(value,'original','current-admin')
        for changes in ({'state':'Applying'},{'root_started_at_utc':'early'},{'publish_barrier':True},{'completed_at_utc':'early'}):
            bad = copy.deepcopy(value); bad['row'].update(changes)
            with self.subTest(changes=changes),self.assertRaises(AssertionError): require_waiting_deferred(bad,'original','current-admin')
        for changes in ({'state':'Succeeded'},{'state':'Failed'},{'completed_at_utc':'early'}):
            bad = copy.deepcopy(value); bad['job'].update(changes)
            with self.subTest(changes=changes),self.assertRaises(AssertionError): require_waiting_deferred(bad,'original','current-admin')

    def test_waiter_id_subject_and_registration_count_must_stay_original(self):
        value = {'row':{'id':'original','actor_subject':'current-admin','state':'Pending','root_started_at_utc':None,'publish_barrier':False,'completed_at_utc':None},
            'job':{'id':'original','actor_subject':'current-admin','state':'Pending','completed_at_utc':None},'accepted':1}
        for side,key in (('row','id'),('row','actor_subject'),('job','id'),('job','actor_subject')):
            bad = copy.deepcopy(value); bad[side][key] = 'replacement'
            with self.subTest(side=side,key=key),self.assertRaises(AssertionError): require_waiting_deferred(bad,'original','current-admin')
        bad = copy.deepcopy(value); bad['accepted'] = 2
        with self.assertRaises(AssertionError): require_waiting_deferred(bad,'original','current-admin')

    def test_unknown_does_not_misreport_aggregate_as_zero_or_completed(self):
        record = {'state':'Unknown','completedCount':0,'changedCount':0,'abortedCount':0,'notStartedCount':0,'unknownCount':18,'outcomeDigest':''}
        require_unknown_counts(record,18)
        for changes in ({'state':'Aborted'},{'unknownCount':0},{'completedCount':1,'unknownCount':17},
            {'changedCount':1},{'notStartedCount':18},{'unknownCount':True},{'outcomeDigest':'invented'}):
            with self.subTest(changes=changes),self.assertRaises(AssertionError): require_unknown_counts({**record,**changes},18)

    def test_unknown_expected_count_must_be_bounded_integer(self):
        record = {'state':'Unknown','completedCount':0,'changedCount':0,'abortedCount':0,'notStartedCount':0,'unknownCount':18,'outcomeDigest':''}
        for count in (True,0,-1,10001,'18'):
            with self.subTest(count=count),self.assertRaises(AssertionError): require_unknown_counts(record,count)

    def test_recovery_must_retain_original_plan_job_instance_and_subject(self):
        fields = ('id','execution_job_id','root_started_at_utc','actor_subject','owner_hash','container_id','instance_id','relative_path',
            'command_json','request_hash','plan_json','plan_hash','execution_hash','confirmed_at_utc','execution_expires_at_utc')
        original = {key:'original-'+key for key in fields}; same_operation(original,copy.deepcopy(original))
        for key in fields:
            changed = {**original,key:'replacement'}
            with self.subTest(key=key),self.assertRaises(AssertionError): same_operation(original,changed)

    def test_before_natural_expiry_new_lease_or_false_terminal_is_rejected(self):
        fields = ('id','execution_job_id','root_started_at_utc','actor_subject','owner_hash','container_id','instance_id','relative_path',
            'command_json','request_hash','plan_json','plan_hash','execution_hash','confirmed_at_utc','execution_expires_at_utc')
        original = {**{key:'original-'+key for key in fields},'state':'Applying','publish_barrier':True,'completed_at_utc':None,
            'lease_token':'original-token','lease_expires_at_utc':'original-expiry','cancel_requested_at_utc':None}
        lease_held(original,copy.deepcopy(original))
        for changes in ({'lease_token':'replacement'},{'lease_expires_at_utc':'extended'},{'state':'Partial'},{'publish_barrier':False},
            {'completed_at_utc':'early'},{'cancel_requested_at_utc':'not-a-crash-only-case'}):
            with self.subTest(changes=changes),self.assertRaises(AssertionError): lease_held(original,{**original,**changes})

    def test_child_identifiers_reject_non_owned_shapes(self):
        for operation,index in [('A'*32,0),('a'*32,-1),('a'*32,10000),('a'*32,True),('../a',0)]:
            with self.subTest(operation=operation,index=index), self.assertRaises(AssertionError): cut.child_id(operation,index)
        self.assertNotEqual(cut.child_id('a'*32,0),cut.child_id('a'*32,1))

    def test_completed_or_unstarted_tree_cannot_authorize_signal(self):
        base = {'plan':{'entryCount':18},'historyVerified':True,'state':'Applying','finishedThrough':0,'startedThrough':0}
        for values in ({'state':'Completed'},{'state':'Partial'},{'state':'Unknown'}, {'finishedThrough':-1}, {'startedThrough':17}, {'historyVerified':False}):
            record = {**base,**values}; self.assertIsNone(cut.partial_proof(record,Path('/unused')))

    def test_only_intended_modes_may_change(self):
        before = {p:[1,i+10,0o100640 if kind=='File' else 0o40750,2100+i,3100+i,1,100,1000,2000] for i,(p,kind) in enumerate(PATHS.items())}
        after = copy.deepcopy(before); first = 'sub/entry-00.bin'; after[first][2] = 0o100600; after[first][8] += 1
        self.assertEqual(unchanged_except_modes(before,after),1)
        for index in (0,1,3,4,5,6,7):
            bad = copy.deepcopy(after); bad[first][index] += 1
            with self.subTest(index=index), self.assertRaises(AssertionError): unchanged_except_modes(before,bad)
        for mode in (0o100666,0o100000):
            bad = copy.deepcopy(after); bad[first][2] = mode
            with self.assertRaises(AssertionError): unchanged_except_modes(before,bad)

    def test_unchanged_mode_cannot_hide_metadata_change(self):
        before = {p:[1,i+10,0o100640 if k=='File' else 0o40750,2100,3100,1,100,1000,2000] for i,(p,k) in enumerate(PATHS.items())}
        after = copy.deepcopy(before); after[''][8] += 1
        with self.assertRaises(AssertionError): unchanged_except_modes(before,after)

    def test_queries_are_read_only_and_browser_cannot_inject(self):
        directory = Path(__file__).parent
        for name in ('validate_container_recursive_permission_faults.py','verify_container_recursive_permission_faults.py'):
            tree = ast.parse((directory/name).read_text())
            for node in ast.walk(tree):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    value = node.args[0]; self.assertIsInstance(value,ast.Constant)
                    self.assertTrue(value.value.lstrip().upper().startswith('SELECT '))
                    self.assertNotRegex(value.value,r'(?i)\b(?:UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')
        source = (directory/'validate_container_recursive_permission_faults.mjs').read_text()
        for token in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem', 'localStorage.setItem'):
            self.assertNotIn(token,source)


if __name__ == '__main__': unittest.main()
