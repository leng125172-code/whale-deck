"""Reject fabricated/missing ACL history proof fields without any live RPC."""
import copy
import unittest

from validate_container_acl_history_agent import entry_proof, snapshot_proof


class HistoryProofTests(unittest.TestCase):
    def setUp(self):
        self.location = dict(containerId='a' * 64, instanceId='123:2026-11-03T00:00:00Z', path='work/acl-file.bin')
        self.acl = [dict(tag='user_obj', id=None, permissions=0), dict(tag='user', id=0, permissions=0),
                    dict(tag='group_obj', id=None, permissions=0), dict(tag='mask', id=None, permissions=0),
                    dict(tag='other', id=None, permissions=0)]
        self.row = dict(request=dict(operationId='a' * 32, plan=dict(command=dict(instanceId=self.location['instanceId'], aclType='access'),
                        kind='File', sourceIdentity=[1, 2, 0o100000], accessAcl=self.acl, defaultAcl=[])), result=dict(historyDigest='A' * 64))
        self.entry = dict(captureId='a' * 32, historyDigest='A' * 64, createdAtUnixMs='0', sourceInstanceId=self.location['instanceId'],
                          changedAclType='access', kind='File', originalMode=0, accessEntryCount=5, defaultEntryCount=0)
        self.snapshot = dict(location=self.location, entry=self.entry,
                             accessAcl=[{k: v for k, v in e.items() if v is not None} for e in self.acl], defaultAcl=[])

    def test_explicit_zero_mode_epoch_named_root_and_permissions_are_preserved(self):
        entry_proof(self.entry, self.row)
        snapshot_proof(self.snapshot, self.row, self.location)

    def test_each_summary_presence_bit_is_required_even_for_zero(self):
        for key in self.entry:
            value = self.entry.copy(); del value[key]
            with self.subTest(key=key), self.assertRaises(AssertionError): entry_proof(value, self.row)

    def test_false_cannot_replace_an_explicit_zero_numeric_proof(self):
        for key in ('originalMode', 'accessEntryCount', 'defaultEntryCount', 'createdAtUnixMs'):
            value = self.entry.copy(); value[key] = False
            with self.subTest(key=key), self.assertRaises(AssertionError): entry_proof(value, self.row)

    def test_altered_capture_digest_path_or_acl_cannot_pass(self):
        for field in ('captureId', 'historyDigest', 'sourceInstanceId', 'changedAclType', 'kind'):
            value = self.entry.copy(); value[field] = 'different'
            with self.subTest(field=field), self.assertRaises(AssertionError): entry_proof(value, self.row)
        for name in ('containerId', 'instanceId', 'path'):
            value = copy.deepcopy(self.snapshot); value['location'][name] = 'different'
            with self.subTest(field=name), self.assertRaises(AssertionError): snapshot_proof(value, self.row, self.location)
        value = copy.deepcopy(self.snapshot); value['accessAcl'][1]['permissions'] = 1
        with self.assertRaises(AssertionError): snapshot_proof(value, self.row, self.location)

    def test_private_extra_fields_and_omitted_named_qualifier_are_rejected(self):
        value = self.entry.copy(); value['plan'] = '/private'
        with self.assertRaises(AssertionError): entry_proof(value, self.row)
        value = copy.deepcopy(self.snapshot); del value['accessAcl'][1]['id']
        with self.assertRaises(AssertionError): snapshot_proof(value, self.row, self.location)
        value = copy.deepcopy(self.snapshot); value['owner'] = 'private'
        with self.assertRaises(AssertionError): snapshot_proof(value, self.row, self.location)


if __name__ == '__main__': unittest.main()
