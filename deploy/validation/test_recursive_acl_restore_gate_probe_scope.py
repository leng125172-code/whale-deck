"""Offline complete transcript proof against real temporary native ACL records.

The wire frames are explicit fixtures, never signed/installed RPC evidence.
Paths are chosen BEFORE plans are made; old plans/histories are never rebound.
"""
import base64
import copy
import json
import os
from types import SimpleNamespace
import unittest

import test_container_file_recursive_acl_restore as kernel
from test_recursive_acl_restore_agent_probe_scope import wire
from recursive_acl_restore_agent_io import read_bundle
from recursive_acl_restore_rpc_proof import FRAME, digest
from recursive_acl_restore_record_proof import records_proof
from verify_container_recursive_acl_restore_agent import transcript_proof, verify


def source_wire(plan):
    result = copy.deepcopy(plan); command = result['command']
    def entries(value): return dict(entries=[{k:v for k,v in e.items() if v is not None} for e in value])
    result['command'] = dict(location={k:command[k] for k in ('containerId', 'instanceId', 'path')}, aclType=command['aclType'],
        expectedVersion=command['expectedVersion'], fileEntries=None if command['fileEntries'] is None else entries(command['fileEntries']), directoryEntries=entries(command['directoryEntries']))
    result['ownerUid'] = result.pop('uid'); result['ownerGid'] = result.pop('gid')
    root = result['rootIdentity']; result['rootIdentity'] = {k:v if k in ('mode', 'ownerUid', 'ownerGid') else str(v) for k,v in zip(FRAME[:8], root[:8], strict=True)}
    result['rootChangedAtUnixNs'] = str(root[8]); result['parentIdentity'] = dict(zip(('device', 'inode'), map(str, result['parentIdentity']), strict=True))
    return result


class GateSelectionTests(unittest.TestCase):
    def test_arbitrary_evidence_paths_fail_before_any_installed_access(self):
        for value in ('../escape', 'recursive-acl-restore-kernel-'+'a'*32, 'container-recursive-acl-restore-agent-'+'A'*32):
            with self.subTest(value=value), self.assertRaises(AssertionError): verify(value)


@unittest.skipUnless(os.geteuid() == 0, 'temporary real Linux ACL records required')
class CompleteRestoreGateProofTests(unittest.TestCase):
    def setUp(self):
        self.k = kernel.RecursiveAclRestoreTests(); self.k.setUp(); self.addCleanup(self.k.doCleanups)
        parent = self.k.b.base.root/'work'; parent.mkdir(mode=0o750); target = parent/'recursive-tree'; self.k.b.tree.rename(target); self.k.b.tree = target
        source_command = self.k.b.command; restore_command = self.k.command
        def fixed_source(kind='access', directory=None):
            value = source_command(kind, directory); value['path'] = 'work/recursive-tree'
            value['expectedVersion'] = self.k.b.base.r.version(self.k.b.base.session.request, value['path'], target.stat()); return value
        def fixed_restore(source, result):
            value = restore_command(source, result); value['path'] = 'work/recursive-tree'
            value['expectedVersion'] = self.k.b.base.r.version(self.k.b.base.session.request, value['path'], target.stat()); return value
        self.k.b.command = fixed_source; self.k.command = fixed_restore
        self.io = SimpleNamespace(storage=self.k.b.base.disk.engine, files=self.k.b.base.f, sourceState=self.k.b.state, restoreState=self.k.state)
        self.folder = self.k.state.parent/'gate-archive'; self.folder.mkdir(mode=0o700)
        self.attrs = digest(b'whaledeck-container-acl-attributes-v1\n', [['user.preserve', base64.b64encode(os.getxattr(target, 'user.preserve')).decode()]])

    def exercise(self):
        sources = []; rows = []; bundles = {}; header = dict(revision='a'*40, probeRevision='b'*40)
        source_rx = [dict(kind='ready', **header, protocolMinor=55)]; restore_rx = copy.deepcopy(source_rx)
        def capture(label, kind, mask=None):
            before = self.k.b.snapshot(); request = self.k.b.request(kind)
            if mask is not None:
                command = self.k.b.command(kind); command['fileEntries'][3]['permissions'] = mask
                request['plan'] = self.k.b.engine.plan(command, self.k.b.base.session)
            reply = self.k.b.engine.apply(request, self.k.b.base.session)
            row = dict(label=label, request=request, result=reply, before=before, after=self.k.b.snapshot())
            bundle, frame = read_bundle(self.io, request, False, self.folder/request['operationId'], create=True)
            row.update(metadata=bundle['metadata'], sourceFrame=frame); records_proof(row, bundle, restore=False, attribute_digest=self.attrs)
            sources.append(row); source_rx.extend([dict(kind='plan', plan=json.dumps(source_wire(request['plan'])), boundaryRefusals=0), dict(kind='receipt', operationId=request['operationId'], result=json.dumps(reply))]); return row
        def restore(label, source, discard=False):
            before = self.k.b.snapshot(); request = self.k.request(source['request'], source['result']); operation = request['operationId']
            restore_rx.append(dict(kind='plan', plan=json.dumps(wire(request['plan'])), boundaryRefusals=22 if not rows else 0))
            if not rows: restore_rx.append(dict(kind='rejected', operationId=operation, refusals=23))
            reply = self.k.engine.discard(request, self.k.b.base.session) if discard else self.k.engine.apply(request, self.k.b.base.session)
            row = dict(label=label, sourceId=source['request']['operationId'], request=request, result=reply, before=before, after=self.k.b.snapshot())
            bundle, _ = read_bundle(self.io, request, True, self.folder/operation, create=True); bundles[operation] = bundle
            records_proof(row, bundle, restore=True, source=source, frame=source['sourceFrame'], attribute_digest=self.attrs)
            restore_rx.append(dict(kind='receipt', operationId=operation, result=json.dumps(reply)))
            for action in ('inspect', 'apply', 'discard'):
                value = getattr(self.k.engine, action)(request, self.k.b.base.session)
                restore_rx.append(dict(kind='receipt', operationId=operation, result=json.dumps(value)))
            rows.append(row); return row
        self.k.write('one.bin', 'access', kernel.native.single.named_acl(mask=2, uid=0))
        self.k.write('sub/中文.bin', 'access', kernel.native.single.acl.base_acl(0o600))
        a = capture('source-access', 'access'); first = restore('access-complete', a); restore('access-noop', a)
        b = capture('source-default', 'default'); restore('default-complete', b)
        capture('source-newer-access', 'access', mask=6); restore('discard', a, discard=True)
        before = self.k.b.snapshot(); reply = self.k.engine.apply(first['request'], self.k.b.base.session)
        late = dict(before=before, after=self.k.b.snapshot(), response=dict(kind='receipt', operationId=first['request']['operationId'], result=json.dumps(reply)))
        restore_rx.extend([late['response'], dict(kind='done')]); source_rx.append(dict(kind='done'))
        return source_rx, restore_rx, sources, rows, late, header, bundles

    def test_complete_three_source_four_restore_transcript_and_late_old_apply_are_proved(self):
        args = self.exercise(); result = transcript_proof(*args)
        self.assertEqual(result, dict(normalSourceCaptures=3, distinctRestoreOperations=4, restoreReceipts=17, planRefusals=22, operationRefusals=23))
        late = json.loads(args[4]['response']['result']); self.assertFalse(late['bindingCurrent']); self.assertEqual(late['state'], 'Completed')

    def test_missing_reordered_or_substituted_receipts_cannot_pass_a_full_transcript(self):
        args = self.exercise()
        for change in ('missing', 'order', 'plan', 'late-binding', 'refusals', 'source-order', 'old-snapshot'):
            value = copy.deepcopy(args)
            if change == 'missing': del value[1][4]
            elif change == 'order': value[1][3], value[1][4] = value[1][4], value[1][3]
            elif change == 'plan': value[1][1]['plan'] = value[1][7]['plan']
            elif change == 'late-binding':
                reply = json.loads(value[4]['response']['result']); reply['bindingCurrent'] = True
                value[4]['response']['result'] = json.dumps(reply); value[1][-2] = value[4]['response']
            elif change == 'refusals': value[1][2]['refusals'] = 22
            elif change == 'source-order': value[0][1], value[0][3] = value[0][3], value[0][1]
            else: value[4]['before'] = value[3][0]['after']
            with self.subTest(change=change), self.assertRaises((AssertionError, KeyError, IndexError)): transcript_proof(*value)


if __name__ == '__main__': unittest.main()
