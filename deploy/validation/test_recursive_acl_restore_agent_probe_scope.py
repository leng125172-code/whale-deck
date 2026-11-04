"""Offline strict proof tests, with optional real temporary Linux ACLs; no installed RPC."""
import copy
import base64
import json
import os
from pathlib import Path
import tempfile
import unittest

from recursive_acl_restore_rpc_proof import proto_plan, source_proof, manifest_proof, ready_proof, child_id, number, digest, FRAME
from validate_container_recursive_acl_history_agent import Probe as BoundedProbe, MAX_WIRE_LINE


def wire(plan):
    value = copy.deepcopy(plan); command = value['command']
    value['command'] = dict(location={k:command[k] for k in ('containerId', 'instanceId', 'path')}, **{k:command[k] for k in ('captureId', 'expectedHistoryDigest', 'expectedVersion')})
    value['ownerUid'] = value.pop('uid'); value['ownerGid'] = value.pop('gid')
    root = value['rootIdentity']; value['rootIdentity'] = {k:v if k in ('mode', 'ownerUid', 'ownerGid') else str(v) for k,v in zip(FRAME[:8], root[:8], strict=True)}
    value['rootChangedAtUnixNs'] = str(root[8])
    value['parentIdentity'] = {k:str(v) for k,v in zip(('device', 'inode'), value['parentIdentity'], strict=True)}
    value['restoreSource']['createdAtUnixMs'] = str(value['restoreSource']['createdAtUnixMs'])
    value['restoreSourceFrame'] = {k:v if k in ('mode', 'ownerUid', 'ownerGid') else str(v) for k,v in zip(FRAME, value['restoreSourceFrame'], strict=True)}
    return value


def synthetic():
    location = dict(containerId='a'*64, instanceId='777:2026-11-05T00:00:00Z', path='work/recursive-tree')
    return dict(schemaVersion=1, command=dict(**location, captureId='b'*32, expectedHistoryDigest='C'*64, expectedVersion='D'*64), targetProof='E'*64, parentIdentity=[2049, 2], parentVersion='F'*64,
        uid=1000, gid=1000, fingerprint='1'*64, rootIdentity=[2049, 3, 0o40750, 2100, 3100, 2, 4096, -1, 0], manifestDigest='2'*64, entryCount=4, fileCount=2, directoryCount=2, changeCount=4,
        restoreSource=dict(captureId='b'*32, historyDigest='C'*64, createdAtUnixMs=0, sourceInstanceId='666:2026-11-04T00:00:00Z', changedAclType='access', manifestDigest='3'*64, entryCount=4, fileCount=2, directoryCount=2, changeCount=1),
        restoreSourceFrame=[2049, 5, 0o100600, 0, 0, 1, 12345, -1, 0], restoreTargetsDigest='4'*64)


class RestoreAgentProofScopeTests(unittest.TestCase):
    def test_exact_small_reference_and_separate_historical_counts_roundtrip(self):
        plan = synthetic(); self.assertEqual(plan, proto_plan(wire(plan)))
        self.assertNotEqual(plan['changeCount'], plan['restoreSource']['changeCount'])
        self.assertNotEqual(plan['command']['instanceId'], plan['restoreSource']['sourceInstanceId'])

    def test_all_fields_required_including_zero_and_no_per_inode_input(self):
        value = wire(synthetic())
        for parent in ((), ('command',), ('command', 'location'), ('restoreSource',), ('restoreSourceFrame',), ('rootIdentity',), ('parentIdentity',)):
            node = value
            for part in parent: node = node[part]
            for key in node:
                changed = copy.deepcopy(value); target = changed
                for part in parent: target = target[part]
                del target[key]
                with self.subTest(parent=parent, key=key), self.assertRaises((AssertionError, KeyError)): proto_plan(changed)
        for parent in ((), ('command',), ('restoreSource',), ('restoreSourceFrame',)):
            changed = copy.deepcopy(value); target = changed
            for part in parent: target = target[part]
            target['entries'] = []
            with self.subTest(parent=parent), self.assertRaises(AssertionError): proto_plan(changed)

    def test_source_metadata_frame_cannot_silently_gain_ownership_mode_links_or_unbounded_size(self):
        for key, bad in (('mode', 0o100644), ('mode', '33152'), ('ownerUid', '0'), ('ownerGid', False), ('ownerUid', 1), ('ownerGid', 1), ('linkCount', '2'), ('sizeBytes', '0'), ('sizeBytes', '16777217'), ('inode', '0'), ('inode', '18446744073709551616'), ('modifiedAtUnixNs', '-9223372036854775809')):
            value = wire(synthetic()); value['restoreSourceFrame'][key] = bad
            with self.subTest(key=key, bad=bad), self.assertRaises(AssertionError): proto_plan(value)

    def test_no_boolean_float_noncanonical_or_oversized_integers(self):
        for bad in (True, False, 0.0, '-0', '00', '1e2', ' 0', '9223372036854775808'):
            value = wire(synthetic()); value['restoreSource']['createdAtUnixMs'] = bad
            with self.subTest(bad=bad), self.assertRaises(AssertionError): proto_plan(value)
        for key in ('entryCount', 'fileCount', 'directoryCount', 'changeCount'):
            for bad in (False, 1.0, '1', 10001):
                value = wire(synthetic()); value[key] = bad
                with self.subTest(key=key, bad=bad), self.assertRaises(AssertionError): proto_plan(value)
        self.assertEqual(-9223372036854775808, number('-9223372036854775808', -9223372036854775808, 9223372036854775807))

    def test_summary_mismatch_and_invalid_directory_counts_never_become_a_plan(self):
        for key, value in (('captureId', 'f'*32), ('historyDigest', 'F'*64), ('sourceInstanceId', 'bad'), ('changedAclType', 'other'), ('directoryCount', 1), ('changeCount', 5)):
            changed = wire(synthetic()); changed['restoreSource'][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): proto_plan(changed)
        changed = wire(synthetic()); changed['restoreSource']['changedAclType'] = 'default'
        with self.assertRaises(AssertionError): proto_plan(changed)

    def test_ready_requires_155_exact_revision_and_strict_scalar_presence(self):
        header = dict(revision='a'*40, probeRevision='b'*40)
        ready_proof(dict(kind='ready', **header, protocolMinor=55), header)
        for minor in (54, False, '55', 55.0):
            with self.subTest(minor=minor), self.assertRaises(AssertionError): ready_proof(dict(kind='ready', **header, protocolMinor=minor), header)
        with self.assertRaises(AssertionError): ready_proof(dict(kind='ready', revision='c'*40, probeRevision='b'*40, protocolMinor=55), header)

    def test_child_id_cannot_share_ordinary_acl_domain(self):
        import hashlib
        operation = 'a'*32
        old = hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:' + operation.encode() + b':0').hexdigest()[:32]
        self.assertNotEqual(old, child_id(operation, 0)); self.assertNotEqual(child_id(operation, 0), child_id(operation, 1))
        for index in (-1, True, 10000):
            with self.subTest(index=index), self.assertRaises(AssertionError): child_id(operation, index)

    def test_existing_bounded_reader_never_treats_oversized_evidence_as_a_frame(self):
        with tempfile.TemporaryDirectory() as folder:
            probe = BoundedProbe.__new__(BoundedProbe); probe.transcript = Path(folder)/'wire'; probe.buffer = b' '*(MAX_WIRE_LINE+1)+b'\n'
            with self.assertRaisesRegex(AssertionError, 'OUTPUT_LIMIT'): probe.receive()

    def test_probe_has_fixed_scope_minimum_capability_no_local_fallback_and_clears_credential(self):
        repo = Path(__file__).resolve().parents[2]; text = (repo/'deploy/validation/TerminalProbe/ContainerRecursiveAclRestoreProbe.cs').read_text()
        for marker in ('ContainerRecursiveAclRestoreBinding', 'caps.ProtocolMinor >= 55', 'docker.files.recursive-acl-restore-intents', 'CryptographicOperations.ZeroMemory(key)', '/run/whaledeck/agent.sock', 'Path = "work/recursive-tree"', 'count < 64'):
            self.assertIn(marker, text)
        for marker in ('Process.Start', 'hostPath', 'Console.WriteLine(key', 'File.WriteAllBytes', 'new ContainerRecursiveAclCommand'):
            self.assertNotIn(marker, text)
        self.assertLess(text.index('Phase = "capabilities"'), text.index('File.ReadAllBytesAsync'))
        self.assertIn('container-recursive-acl-restore', (repo/'deploy/validation/TerminalProbe/Program.cs').read_text())


@unittest.skipUnless(os.geteuid() == 0, 'temporary Linux ACL inode tests require root')
class RestoreActualTemporaryManifestProofTests(unittest.TestCase):
    def setUp(self):
        import test_container_file_recursive_acl_restore as kernel
        self.k = kernel.RecursiveAclRestoreTests(); self.k.setUp(); self.addCleanup(self.k.doCleanups)

    def capture(self, kind):
        request, result, before = self.k.seed(kind)
        return dict(request=request, result=result, before=before, metadata=json.loads(self.k.source_file(request).read_bytes()))

    def test_source_path_mismatch_is_rejected_before_writable_parent_checks(self):
        source = self.capture('access')
        command = self.k.command(source['request'], source['result'])
        command['path'] = 'tmp/recursive-readonly'
        before = self.k.b.snapshot()
        with self.assertRaisesRegex(self.k.b.base.r.Rejected, 'CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND'):
            self.k.engine.plan(command, self.k.b.base.session)
        self.assertEqual(before, self.k.b.snapshot())

    def test_wrong_expected_current_version_has_plan_invalid_without_state_or_hdd_write(self):
        source = self.capture('access'); before = self.k.b.snapshot()
        command = self.k.command(source['request'], source['result']); command['expectedVersion'] = '0'*64
        with self.assertRaisesRegex(self.k.b.base.r.Rejected, 'CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID'):
            self.k.engine.plan(command, self.k.b.base.session)
        self.assertEqual(before, self.k.b.snapshot()); self.assertFalse(list(self.k.state.iterdir()))

    def test_coherent_rehash_cannot_substitute_child_authority_parent_topology_or_operation(self):
        source = self.capture('access'); before = self.k.b.snapshot(); request = self.k.request(source['request'], source['result'])
        self.k.engine.apply(request, self.k.b.base.session)
        metadata = json.loads((self.k.b.base.disk.target/'container-recursive-acl-restore-v1'/(request['operationId']+'.json')).read_bytes())
        attrs = digest(b'whaledeck-container-acl-attributes-v1\n', [['user.preserve', base64.b64encode(os.getxattr(self.k.b.tree, 'user.preserve')).decode()]])
        for index in (0, 3):
            for key in ('uid', 'gid', 'targetProof', 'parentIdentity', 'parentVersion'):
                changed = copy.deepcopy(metadata); child = changed['entries'][index]['plan']
                if key in ('uid', 'gid'): child[key] += 1
                elif key == 'parentIdentity': child[key][1] += 1
                else: child[key] = 'F'*64
                child['fingerprint'] = digest(b'whaledeck-container-acl-plan-v1\n', {k:v for k,v in child.items() if k != 'fingerprint'})
                plan = changed['plan']; plan['manifestDigest'] = digest(b'whaledeck-container-recursive-acl-restore-manifest-v1\n', changed['entries'])
                plan['fingerprint'] = digest(b'whaledeck-container-recursive-acl-restore-plan-v1\n', {k:v for k,v in plan.items() if k != 'fingerprint'})
                changed['historyDigest'] = digest(b'whaledeck-container-recursive-acl-restore-history-v1\n', {k:v for k,v in changed.items() if k != 'historyDigest'})
                with self.subTest(index=index, key=key), self.assertRaises(AssertionError):
                    manifest_proof(plan, changed, source, before, attrs, operation_id=request['operationId'], owner=request['owner'])
        for key in ('operationId', 'owner'):
            changed = copy.deepcopy(metadata); changed[key] = 'f'*32 if key == 'operationId' else 'F'*64
            changed['historyDigest'] = digest(b'whaledeck-container-recursive-acl-restore-history-v1\n', {k:v for k,v in changed.items() if k != 'historyDigest'})
            with self.subTest(key=key), self.assertRaises(AssertionError):
                manifest_proof(changed['plan'], changed, source, before, attrs, operation_id=request['operationId'], owner=request['owner'])

    def test_actual_native_plan_manifest_source_and_heterogeneous_target_projections(self):
        for kind in ('access', 'default'):
            source = self.capture(kind); before = self.k.b.snapshot(); request = self.k.request(source['request'], source['result']); plan = request['plan']
            # The standard fixture uses tree; this independent wire gate has a
            # fixed installed work/recursive-tree scope. Verify native hashes
            # directly rather than rewrite signed paths or hashes to fit it.
            frame = self.k.engine.sources.frame(self.k.source_file(source['request']).stat())
            source_proof(plan, source, frame, before)
            self.k.engine.apply(request, self.k.b.base.session)
            metadata_path = self.k.b.base.disk.target/'container-recursive-acl-restore-v1'/(request['operationId']+'.json')
            metadata = json.loads(metadata_path.read_bytes())
            attributes = digest(b'whaledeck-container-acl-attributes-v1\n', [['user.preserve', base64.b64encode(os.getxattr(self.k.b.tree, 'user.preserve')).decode()]])
            entries = manifest_proof(plan, metadata, source, before, attributes, operation_id=request['operationId'], owner=request['owner'])
            self.assertEqual(4, len(entries))
            for field in ('restoreTargetsDigest', 'manifestDigest', 'fingerprint'):
                changed = copy.deepcopy(plan); changed[field] = 'F'*64
                with self.subTest(kind=kind, field=field), self.assertRaises(AssertionError):
                    source_proof(changed, source, frame, before)
            changed = copy.deepcopy(metadata); changed['entries'][0]['plan']['command']['entries'][0]['permissions'] ^= 1
            with self.assertRaises(AssertionError): manifest_proof(plan, changed, source, before, attributes, operation_id=request['operationId'], owner=request['owner'])
            for index in range(9):
                changed = list(frame); changed[index] += 1
                with self.subTest(frame=index), self.assertRaises(AssertionError): source_proof(plan, source, changed, before)


if __name__ == '__main__':
    unittest.main()
