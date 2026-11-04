import copy
import unittest
from reconcile_recursive_acl_restore_missed_cut import NAME,target,settled

class MissedCutScopeTests(unittest.TestCase):
    def test_only_fixed_failed_evidence_may_be_reconciled(self):
        self.assertEqual(NAME,target(NAME).name)
        for name in ('../escape','recursive-acl-restore-prewrite-cancel-'+'a'*32,NAME+'/child'):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)

    def test_cleanup_requires_successful_original_terminal_without_any_unknown_or_barrier(self):
        value=dict(row=dict(state='Succeeded',completed_at_utc='actual',lease_token=None,publish_barrier=False,completed_count=26,changed_count=26,aborted_count=0,not_started_count=0,unknown_count=0),executionJob=dict(state='Succeeded'))
        settled(value)
        for field,bad in (('state','Unknown'),('completed_at_utc',None),('lease_token','active'),('publish_barrier',True),('completed_count',25),('changed_count',0),('unknown_count',1)):
            changed=copy.deepcopy(value);changed['row'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):settled(changed)
        changed=copy.deepcopy(value);changed['executionJob']['state']='Failed'
        with self.assertRaises(AssertionError):settled(changed)

if __name__=='__main__':unittest.main()
