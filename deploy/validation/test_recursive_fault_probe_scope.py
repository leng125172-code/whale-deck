"""Offline rejection tests for the exact real-fault QA boundary, no live access."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

import bound_container_recursive_permission_cut as cut
from validate_container_recursive_permission_faults import PATHS, unchanged_except_modes


class RecursiveFaultProbeScope(unittest.TestCase):
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
