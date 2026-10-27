"""Real history bytes/FDs under synthetic namespace policy; no installed RPC claim."""
import base64
import contextlib
import errno
import json
import os
import unittest
from unittest.mock import patch

import test_container_file_history as history_tests
import test_container_file_staging as staging

transport=staging.load('whaledeck-container-history-transport.py','history_transport_candidate')


@unittest.skipUnless(os.name=='posix' and hasattr(os,'geteuid') and os.geteuid()==0,'requires isolated Linux root')
class LinuxHistoryTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture=history_tests.LinuxContainerHistoryTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.identifier=self.fixture.capture();self.r=self.fixture.fixture.r;self.f=self.fixture.fixture.f
        self.engine=self.fixture.engine;self.session=self.fixture.session
        self.request={**self.session.request,'path':self.fixture.fixture.leaf.name,'captureId':self.identifier,
            'expectedCaptureHash':self.engine.digest('capture',self.fixture.metadata(self.identifier))}

    def execute(self,request=None,emit=None):
        rows=[]
        if emit is None:emit=lambda value:rows.append(value)
        with patch.object(self.r,'ContainerRoot',return_value=contextlib.nullcontext(self.session)),patch.object(self.r,'receiver_current'),patch.object(self.f,'emit',side_effect=emit):
            transport.execute('source',request or self.request,self.r,self.f,None,self.engine)
        return rows

    def test_actual_complete_binary_frames_and_end_after_context_success(self):
        rows=self.execute();self.assertEqual(rows[0]['type'],'metadata');self.assertEqual(rows[-1]['type'],'end')
        self.assertNotIn('owner',rows[0]['entry']);self.assertEqual(rows[0]['entry']['captureId'],self.identifier)
        content=bytearray()
        for row in rows[1:-1]:
            self.assertEqual(row['type'],'chunk');self.assertEqual(row['offset'],len(content))
            chunk=base64.b64decode(row['data'],validate=True);self.assertLessEqual(len(chunk),32768);content.extend(chunk)
        self.assertEqual(bytes(content),self.fixture.fixture.original)
        self.assertEqual(rows[-1]['sha256'],self.fixture.metadata(self.identifier)['sha256'])
        self.assertEqual(rows[-1]['targetProof'],rows[0]['targetProof'])
        self.assertEqual(rows[-1]['captureHash'],self.request['expectedCaptureHash'])

    def test_exit_validation_failure_after_last_chunk_cannot_emit_end(self):
        rows=[];changed=False
        def emit(value):
            nonlocal changed
            rows.append(value)
            if value['type']=='chunk' and not changed:
                changed=True;meta=self.fixture.metadata(self.identifier);meta['createdAtUnixMs']+=1;self.fixture.put(self.identifier,meta)
        with self.assertRaisesRegex(self.r.Rejected,'CONTENT_CHANGED'):self.execute(emit=emit)
        self.assertTrue(any(row['type']=='chunk' for row in rows))
        self.assertFalse(any(row['type']=='end' for row in rows))

    def test_disconnected_consumer_never_gets_a_success_end(self):
        rows=[]
        def emit(value):
            rows.append(value)
            if value['type']=='chunk':raise BrokenPipeError()
        with self.assertRaises(BrokenPipeError):self.execute(emit=emit)
        self.assertFalse(any(row['type']=='end' for row in rows))

    def test_bad_shapes_and_unbound_source_are_rejected_before_namespace_access(self):
        for change in ({'rootId':'test.shared'},{'captureId':'../private'},{'expectedCaptureHash':'A'*64+'\n'},
                       {'path':'../private'},{'path':'work/.env'},{'containerId':'A'*64}):
            with self.subTest(change=change),patch.object(self.r,'ContainerRoot',side_effect=AssertionError('namespace must not open')):
                with self.assertRaises((transport.Rejected,self.r.Rejected,self.f.Rejected)):
                    transport.execute('source',{**self.request,**change},self.r,self.f,None,self.engine)

    def test_list_cannot_claim_source_or_accept_unbounded_counts(self):
        request={**self.session.request,'path':'','after':'','take':1,'expectedVersion':''}
        for change in ({'take':True},{'take':51},{'after':self.identifier},{'captureId':self.identifier}):
            with self.subTest(change=change),patch.object(self.r,'ContainerRoot',side_effect=AssertionError('namespace must not open')):
                with self.assertRaises((transport.Rejected,self.r.Rejected,self.f.Rejected)):
                    transport.execute('list',{**request,**change},self.r,self.f,None,self.engine)

    def test_error_codes_never_echo_arbitrary_paths_or_exception_text(self):
        self.assertEqual(transport.error_code(RuntimeError('private path /secret/value')),'CONTAINER_HISTORY_UNAVAILABLE')
        self.assertEqual(transport.error_code(OSError(errno.ENOENT,'private path')),'CONTAINER_HISTORY_NOT_FOUND')
        self.assertEqual(transport.error_code(OSError(errno.EACCES,'private path')),'CONTAINER_HISTORY_PROTECTED')
        self.assertEqual(transport.error_code(self.r.Rejected('CONTAINER_HISTORY_CONTENT_INVALID')),'CONTAINER_HISTORY_CONTENT_INVALID')


if __name__=='__main__':unittest.main()
