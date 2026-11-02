"""Offline rejection tests for the exact real-fault QA boundary, no live access."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch, Mock

import bound_container_recursive_ownership_cut as cut
from validate_container_recursive_ownership_faults import PATHS, unchanged_except_owners, same_operation, lease_held, require_unknown_counts, require_waiting_deferred, require_agent_restart, require_stopped_tree, maintenance_response, restore_fault_services, require_authority_change, require_logout_authority


class RecursiveOwnershipFaultProbeScope(unittest.TestCase):
    def test_logout_cannot_be_proved_by_identity_removal_or_disabling(self):
        subject = '11111111-1111-4111-8111-111111111111'
        identity = {'pk':1,'uuid':subject,'username':'whaledeck-upload-qa-'+'a'*12,'groups':['admin'],'is_active':True}
        value = {'kind':'logout','subject':subject,'before':identity,'after':copy.deepcopy(identity),'noIdentityMutation':True}
        require_logout_authority(value,subject)
        for changes in ({'groups':[]},{'is_active':False},{'pk':2},{'uuid':'22222222-2222-4222-8222-222222222222'}):
            bad = copy.deepcopy(value); bad['after'].update(changes)
            with self.subTest(changes=changes),self.assertRaises(AssertionError): require_logout_authority(bad,subject)
            if 'pk' not in changes:
                bad['before'].update(changes)
                with self.subTest(both=changes),self.assertRaises(AssertionError): require_logout_authority(bad,subject)
        for changes in ({'kind':'account-disable'},{'noIdentityMutation':False},{'subject':'22222222-2222-4222-8222-222222222222'}):
            with self.subTest(changes=changes),self.assertRaises(AssertionError): require_logout_authority({**value,**changes},subject)

    def test_revocation_proof_cannot_change_identity_or_other_fields(self):
        subject = '11111111-1111-4111-8111-111111111111'
        ready = {'subject':subject,'permissionExpiresAtUtc':'2026-11-03T10:00:00+00:00'}
        before = {'pk':1,'uuid':subject,'username':'whaledeck-upload-qa-'+'a'*12,'groups':['admin'],'is_active':True}
        for kind,changes in (('group-removal',{'groups':[]}),('account-disable',{'is_active':False})):
            value = {'kind':kind,'subject':subject,'before':before,'after':{**before,**changes},'onlyOwnedIdentityChanged':True,'confirmedAtUtc':'2026-11-02T10:00:00+00:00'}
            require_authority_change(value,ready,kind)
            for field,replacement in (('pk',2),('uuid','22222222-2222-4222-8222-222222222222'),('username','real-admin'),('is_active',True if kind == 'account-disable' else False),('groups',['another-admin'])):
                bad = copy.deepcopy(value); bad['after'][field] = replacement
                with self.subTest(kind=kind,field=field),self.assertRaises(AssertionError): require_authority_change(bad,ready,kind)
            for bad_changes in ({'subject':'22222222-2222-4222-8222-222222222222'},{'kind':'cancel'},{'onlyOwnedIdentityChanged':False}):
                with self.subTest(kind=kind,bad=bad_changes),self.assertRaises(AssertionError): require_authority_change({**value,**bad_changes},ready,kind)
            with self.assertRaises(AssertionError): require_authority_change(value,{**ready,'permissionExpiresAtUtc':'2026-11-02T10:59:59+00:00'},kind)

    def test_agent_cleanup_failure_never_skips_worker_restore(self):
        host = {'whaledeck-agent.service':{'pid':10}}; revision = 'a'*40
        prefix = 'validate_container_recursive_ownership_faults.'
        for outputs,identity_failure in (([RuntimeError('state lookup failed')],False),(['inactive',RuntimeError('start failed')],False),(['active'],True)):
            worker = Mock()
            with patch(prefix+'command',side_effect=outputs),patch(prefix+'agent_identity',side_effect=RuntimeError('identity failed') if identity_failure else None):
                with self.assertRaises(RuntimeError): restore_fault_services(worker,True,host,revision)
            worker.start.assert_called_once_with()
        worker = Mock()
        with patch(prefix+'command') as command_mock,patch(prefix+'agent_identity') as identity_mock:
            restore_fault_services(worker,False,host,revision)
            command_mock.assert_not_called(); identity_mock.assert_not_called()
        worker.start.assert_called_once_with()

    def test_restart_must_change_only_agent_process(self):
        revision = 'a'*40
        before = {'whaledeck-agent.service':{'pid':10,'sourceRevision':revision},'whaledeck-maintenance.service':{'pid':20,'sourceRevision':revision}}
        after = copy.deepcopy(before); after['whaledeck-agent.service']['pid'] = 30
        require_agent_restart(before,after,revision)
        for bad in (before,{**after,'unexpected.service':{}}, {'whaledeck-agent.service':{'pid':30,'sourceRevision':revision},'whaledeck-maintenance.service':{'pid':21,'sourceRevision':revision}}):
            with self.assertRaises(AssertionError): require_agent_restart(before,bad,revision)
        bad = copy.deepcopy(after); bad['whaledeck-agent.service']['sourceRevision'] = 'b'*40
        with self.assertRaises(AssertionError): require_agent_restart(before,bad,revision)

    def test_stop_does_not_authorize_redo_finished_or_unstarted_entries(self):
        cut = {p:[1,i+10,0o100640 if kind=='File' else 0o40750,2100+i,3100+i,1,100,1000,2000] for i,(p,kind) in enumerate(PATHS.items())}
        cut['sub/entry-00.bin'][3:5] = [2200,3200]
        stopped = copy.deepcopy(cut); stopped['sub/entry-01.bin'][3:5] = [2200,3200]; stopped['sub/entry-01.bin'][8] += 1
        require_stopped_tree(cut,stopped,0,1)
        for path in ('sub/entry-00.bin','sub/entry-02.bin','sub',''):
            bad = copy.deepcopy(stopped); bad[path][8] += 1
            with self.subTest(path=path),self.assertRaises(AssertionError): require_stopped_tree(cut,bad,0,1)
        bad = copy.deepcopy(stopped); bad['sub/entry-01.bin'][3] += 1
        with self.assertRaises(AssertionError): require_stopped_tree(cut,bad,0,1)

    def test_maintenance_must_be_bounded_uncached_healthy_response(self):
        raw = b'{"state":"healthy","phase":"Ready"}'
        self.assertEqual(maintenance_response(200,raw,'no-store'),('healthy','Ready'))
        for status,body,cache in ((503,raw,'no-store'),(200,raw,''),(200,b'x'*2049,'no-store'),(200,b'{"state":"Unavailable","phase":"GatewayOffline"}','no-store'),(200,b'{"state":"healthy","phase":"Ready","private":"not-public"}','no-store')):
            with self.assertRaises(AssertionError): maintenance_response(status,body,cache)

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
            'job':{'id':'original','actor_subject':'current-admin','state':'Queued','completed_at_utc':None},'accepted':1}
        require_waiting_deferred(value,'original','current-admin')
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

    def test_only_explicit_owners_and_necessary_ctime_may_change(self):
        before={p:[1,i+10,0o100640 if kind=='File' else 0o40750,2100+i,3100+i,1,100,1000,2000] for i,(p,kind) in enumerate(PATHS.items())}
        after=copy.deepcopy(before);first='sub/entry-00.bin';after[first][3:5]=[2200,3200];after[first][8]+=1
        self.assertEqual(unchanged_except_owners(before,after),1)
        for index in (0,1,2,5,6,7):
            bad=copy.deepcopy(after);bad[first][index]+=1
            with self.subTest(index=index),self.assertRaises(AssertionError):unchanged_except_owners(before,bad)
        for owners in ([0,0],[2200,3300],[2300,3200]):
            bad=copy.deepcopy(after);bad[first][3:5]=owners
            with self.subTest(owners=owners),self.assertRaises(AssertionError):unchanged_except_owners(before,bad)

    def test_unchanged_owner_cannot_hide_any_metadata_change(self):
        before={p:[1,i+10,0o100640 if kind=='File' else 0o40750,2100,3100,1,100,1000,2000] for i,(p,kind) in enumerate(PATHS.items())}
        for index in range(9):
            after=copy.deepcopy(before);after[''][index]+=1
            with self.subTest(index=index),self.assertRaises(AssertionError):unchanged_except_owners(before,after)

    def test_queries_are_read_only_and_browser_cannot_inject(self):
        directory = Path(__file__).parent
        for name in ('validate_container_recursive_ownership_faults.py','verify_container_recursive_ownership_faults.py'):
            tree = ast.parse((directory/name).read_text())
            for node in ast.walk(tree):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    value = node.args[0]; self.assertIsInstance(value,ast.Constant)
                    self.assertTrue(value.value.lstrip().upper().startswith('SELECT '))
                    self.assertNotRegex(value.value,r'(?i)\b(?:UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')
        source = (directory/'validate_container_recursive_ownership_faults.mjs').read_text()
        for token in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem', 'localStorage.setItem'):
            self.assertNotIn(token,source)


if __name__ == '__main__': unittest.main()
