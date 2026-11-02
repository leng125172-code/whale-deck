"""Offline rejection checks for installed ACL gate proof parsing; no live calls."""
import copy
import unittest
from validate_container_acl_agent import number,managed_entries

class AclProbeScopeTests(unittest.TestCase):
    def test_protobuf_uint64_decimal_strings_are_allowed_but_boolean_float_aliases_are_not(self):
        for value in (0,2**63-1,'9223372036854775807','-1'):self.assertEqual(number(value),int(value))
        for value in (True,1.0,None,'01','1.0',' 1','1e2'):
            with self.subTest(value=value),self.assertRaises(AssertionError):number(value)
    def test_explicit_zero_qualifier_and_permissions_do_not_become_missing(self):
        entry={'tag':'user','id':0,'permissions':0};self.assertEqual(managed_entries([entry]),[entry])
        self.assertEqual(managed_entries([{'tag':'user_obj','permissions':0}]),[{'tag':'user_obj','id':None,'permissions':0}])
    def test_acl_proof_does_not_silently_coerce_missing_extra_or_bool_fields(self):
        valid={'tag':'user','id':2201,'permissions':4}
        invalid=[{'tag':'user','id':2201}, {**valid,'unexpected':'/etc'}, {**valid,'id':True}, {**valid,'id':'2201'}, {**valid,'id':4294967295}, {**valid,'permissions':True}, {**valid,'permissions':8}]
        for entry in invalid:
            with self.subTest(entry=entry),self.assertRaises(AssertionError):managed_entries([entry])
        with self.assertRaises(AssertionError):managed_entries([valid]*65)
