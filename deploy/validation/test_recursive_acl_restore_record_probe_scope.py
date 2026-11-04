"""Actual temporary ACL Root/HDD proofs; explicit disk/container fixtures, no installed RPC."""
import base64
import copy
import json
import os
from types import SimpleNamespace
import unittest

import test_container_file_recursive_acl_restore as kernel
from recursive_acl_restore_agent_io import read_bundle, snapshot, unique
from recursive_acl_restore_record_proof import records_proof, receipt_proof
from recursive_acl_restore_rpc_proof import digest


class StrictPrivateJsonTests(unittest.TestCase):
    def test_duplicate_json_members_cannot_become_private_record_evidence(self):
        with self.assertRaisesRegex(AssertionError, 'DUPLICATE'): json.loads('{"state":"Completed","state":"Aborted"}', object_pairs_hook=unique)


@unittest.skipUnless(os.geteuid() == 0, 'temporary Linux ACL and root-private files required')
class RestoreRecordProofTests(unittest.TestCase):
    def setUp(self):
        self.k = kernel.RecursiveAclRestoreTests(); self.k.setUp(); self.addCleanup(self.k.doCleanups)
        self.io = SimpleNamespace(storage=self.k.b.base.disk.engine, files=self.k.b.base.f, sourceState=self.k.b.state, restoreState=self.k.state)
        self.archive = self.k.state.parent/'proof-archive'; self.archive.mkdir(mode=0o700)
        self.attrs = digest(b'whaledeck-container-acl-attributes-v1\n', [['user.preserve', base64.b64encode(os.getxattr(self.k.b.tree, 'user.preserve')).decode()]])

    def case(self, mode='access'):
        source, result, old = self.k.seed('default' if mode == 'default' else 'access')
        src = dict(request=source, result=result, before=old, after=self.k.b.snapshot())
        bundle, frame = read_bundle(self.io, source, False, self.archive/'source', create=True); src['metadata'] = bundle['metadata']
        self.assertEqual(4, records_proof(src, bundle, restore=False, attribute_digest=self.attrs))
        before = self.k.b.snapshot(); request = self.k.request(source, result)
        result = self.k.engine.discard(request, self.k.b.base.session) if mode == 'discard' else self.k.engine.apply(request, self.k.b.base.session)
        row = dict(request=request, result=result, before=before, after=self.k.b.snapshot())
        bundle, _ = read_bundle(self.io, request, True, self.archive/'restore', create=True)
        return src, frame, row, bundle

    def proof(self, src, frame, row, bundle):
        return records_proof(row, bundle, restore=True, source=src, frame=frame, attribute_digest=self.attrs)

    def test_actual_access_default_and_discard_prove_records_domains_and_outcomes(self):
        for mode in ('access', 'default', 'discard'):
            # Each iteration has independent names/archives to avoid rewriting old evidence.
            with self.subTest(mode=mode):
                self.archive = self.k.state.parent/('archive-'+mode); self.archive.mkdir(mode=0o700)
                src, frame, row, bundle = self.case(mode)
                self.assertEqual(0 if mode == 'discard' else 4, self.proof(src, frame, row, bundle))

    def test_source_child_coherent_rehash_cannot_change_scope_or_parent_topology(self):
        src, _, _, _ = self.case(); bundle, _ = read_bundle(self.io, src['request'], False, self.archive/'source')
        for key in ('targetProof', 'uid', 'parentIdentity', 'parentVersion', 'command-path', 'extra-field'):
            changed = copy.deepcopy(bundle); row = copy.deepcopy(src); child = changed['metadata']['entries'][0]['plan']
            if key == 'uid': child[key] += 1
            elif key == 'parentIdentity': child[key][1] += 1
            elif key == 'command-path': child['command']['path'] += '-outside'
            elif key == 'extra-field': child['extra'] = True
            else: child[key] = 'F'*64
            child['fingerprint'] = digest(b'whaledeck-container-acl-plan-v1\n', {k:v for k,v in child.items() if k != 'fingerprint'})
            plan = changed['metadata']['plan']; plan['manifestDigest'] = digest(b'whaledeck-container-recursive-acl-manifest-v1\n', changed['metadata']['entries'])
            plan['fingerprint'] = digest(b'whaledeck-container-recursive-acl-plan-v1\n', {k:v for k,v in plan.items() if k != 'fingerprint'})
            row['request']['plan'] = changed['root']['plan'] = plan
            metadata = changed['metadata']; metadata['historyDigest'] = digest(b'whaledeck-container-recursive-acl-history-v1\n', {k:v for k,v in metadata.items() if k != 'historyDigest'})
            row['result']['fingerprint'] = plan['fingerprint']; row['result']['historyDigest'] = changed['root']['historyDigest'] = metadata['historyDigest']
            cr = changed['children'][0]['root']; hdd = changed['children'][0]['hdd']; cr['plan'] = hdd['plan'] = child
            for pair in changed['children']:
                pair['hdd']['manifestDigest'] = plan['manifestDigest']
                pair['hdd']['historyDigest'] = digest(b'whaledeck-container-recursive-acl-entry-history-v1\n', {k:v for k,v in pair['hdd'].items() if k != 'historyDigest'})
                pair['root']['historyDigest'] = pair['hdd']['historyDigest']
            with self.subTest(key=key), self.assertRaises(AssertionError): records_proof(row, changed, restore=False, attribute_digest=self.attrs)

    def test_actual_noop_still_requires_all_four_child_histories(self):
        src, frame, row, _ = self.case(); request = self.k.request(src['request'], src['result']); before = self.k.b.snapshot()
        reply = self.k.engine.apply(request, self.k.b.base.session); bundle, _ = read_bundle(self.io, request, True, self.archive/'noop', create=True)
        row = dict(request=request, result=reply, before=before, after=self.k.b.snapshot()); self.assertEqual(before, row['after'])
        self.assertEqual(0, reply['changedCount']); self.assertEqual(4, self.proof(src, frame, row, bundle))
        del bundle['children'][-1]
        with self.assertRaises(AssertionError): self.proof(src, frame, row, bundle)

    def test_coherent_child_history_change_and_outcome_corruption_are_rejected(self):
        src, frame, row, bundle = self.case()
        for key in ('parentOperationId', 'manifestDigest', 'resourceType', 'storageId', 'owner'):
            changed = copy.deepcopy(bundle); hdd = changed['children'][0]['hdd']; hdd[key] = 'F'*64
            hdd['historyDigest'] = digest(b'whaledeck-container-recursive-acl-restore-entry-history-v1\n', {k:v for k,v in hdd.items() if k != 'historyDigest'})
            changed['children'][0]['root']['historyDigest'] = hdd['historyDigest']
            with self.subTest(key=key), self.assertRaises(AssertionError): self.proof(src, frame, row, changed)
        changed = copy.deepcopy(bundle); changed['root']['outcomeDigest'] = 'F'*64
        with self.assertRaises(AssertionError): self.proof(src, frame, row, changed)

    def test_child_actual_identity_confirmation_and_versions_are_required(self):
        src, frame, row, bundle = self.case()
        for key, value in (('actualIdentity', [0]*9), ('version', 'F'*64), ('aclConfirmed', False), ('historyVerified', False), ('state', 'Unknown')):
            changed = copy.deepcopy(bundle); changed['children'][0]['root'][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): self.proof(src, frame, row, changed)

    def test_receipt_must_retain_durable_result_but_can_lose_current_binding(self):
        src, frame, row, bundle = self.case(); root = bundle['root']; late = copy.deepcopy(row['result']); late.update(bindingCurrent=False, replayed=True)
        receipt_proof(late, row['request'], root, restore=True, binding=False, replayed=True)
        for key, value in (('state', 'Unknown'), ('historyAvailable', False), ('completedCount', 3), ('outcomeDigest', 'F'*64), ('aclType', 'default')):
            changed = copy.deepcopy(late); changed[key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): receipt_proof(changed, row['request'], root, restore=True, binding=False, replayed=True)

    def test_raw_archive_is_immutable_and_private_symlink_never_counts_as_evidence(self):
        _, _, row, _ = self.case(); before = snapshot(self.k.state)
        read_bundle(self.io, row['request'], True, self.archive/'restore', create=False)
        self.assertEqual(before, snapshot(self.k.state))
        (self.archive/'restore'/'root.json').write_bytes(b'{}')
        with self.assertRaisesRegex(AssertionError, 'ARCHIVE_CHANGED'): read_bundle(self.io, row['request'], True, self.archive/'restore', create=False)
        (self.k.state/'bad').symlink_to(self.k.b.tree)
        with self.assertRaisesRegex(AssertionError, 'SYMLINK'): snapshot(self.k.state)


if __name__ == '__main__': unittest.main()
