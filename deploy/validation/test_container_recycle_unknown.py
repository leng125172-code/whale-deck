import copy
import json
import os
from pathlib import Path
from types import SimpleNamespace
import subprocess
import sys
import tempfile
import unittest
import uuid
from unittest.mock import patch

import container_recycle_unknown_proof as proof
import validate_container_recycle_unknown as gate
from container_recycle_restart_proof import IMMUTABLE


class RecycleUnknownTests(unittest.TestCase):
    def test_actual_owned_displacement_preserves_inode_bytes_and_user_attributes(self):
        with tempfile.TemporaryDirectory() as folder, subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']) as child:
            apps=Path(folder); root=apps/('wdv-container-publish-'+uuid.uuid4().hex); root.mkdir(); root.chmod(0o755)
            src=root/'recycle-rpc.bin';src.write_bytes(b'original bytes');src.chmod(0o640);os.setxattr(src,'user.unknown',b'original')
            before=proof.file_snapshot(src);i=root.stat();fd=os.pidfd_open(child.pid)
            held=SimpleNamespace(descriptor=fd,proof=dict(twoKernelFdProofs=True,sourceNotRemoved=True),record=dict(plan=dict(parentIdentity=[i.st_dev,i.st_ino])))
            fixture=dict(containerId='a'*64,folder=str(root),original=before);f=dict(identifier='a'*64,folder=root)
            try:
                with patch.object(proof,'APPS',apps):result=proof.displace(f,fixture,held)
                self.assertFalse(src.exists());proof.displaced_proof(before,result['displaced']);self.assertEqual((root/proof.DISPLACED).read_bytes(),b'original bytes')
                self.assertTrue(result['originalHelperStillAlive'])
            finally:os.close(fd);child.terminate();child.wait(timeout=3)

    def test_native_noreplace_never_overwrites_an_existing_leaf(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder);(p/'source').write_bytes(b'source');(p/'target').write_bytes(b'other');fd=os.open(p,os.O_RDONLY|os.O_DIRECTORY)
            try:
                with self.assertRaises(OSError):proof.rename_noreplace(fd,'source','target')
                self.assertEqual((p/'source').read_bytes(),b'source');self.assertEqual((p/'target').read_bytes(),b'other')
            finally:os.close(fd)

    def test_displacement_does_not_accept_changed_inode_content_or_attributes(self):
        a=dict(identity=[1,2,33184,1000,1000,1,5,10,11],sha256='original',attributes=[['user.x','eA==']],sizeBytes=5)
        proof.displaced_proof(a,{**a,'identity':a['identity'][:8]+[12]})
        for field,bad in [('sha256','changed'),('attributes',[]),('sizeBytes',6),('identity',[1,3,*a['identity'][2:]])]:
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.displaced_proof(a,{**a,field:bad})

    def ack(self):
        row={k:k for k in IMMUTABLE};row.update(state='Unknown',publish_barrier=True,version=4,actor_subject='owner',root_state='Unknown',source_retained=None,source_restored=None,history_digest='digest',history_may_exist=True,history_verified=True,cancel_requested_at_utc=None)
        receipt=dict(state='Unknown',sourceRetained=None,sourceRestored=None,replayed=False,bindingCurrent=False,historyAvailable=True)
        before=dict(sql=dict(row=row),receipt=receipt,records=dict(root='original',history='original'),physical=dict(source=None,retained=None),displaced='kept')
        after=copy.deepcopy(before);new=after['sql']['row'];new.update(state='Acknowledged',publish_barrier=False,version=5,acknowledged_by='different',receipt_json=json.dumps(receipt),acknowledgement_json=json.dumps(receipt))
        return before,after

    def test_ack_keeps_null_source_facts_and_original_records(self):
        before,after=self.ack();proof.require_ack(before,after,'different')
        for field,bad in [('source_retained',False),('root_state','Trashed'),('history_digest','changed'),('plan_json','changed'),('acknowledged_by','owner'),('version',4)]:
            altered=copy.deepcopy(after);altered['sql']['row'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.require_ack(before,altered,'different')
        after['records']['history']='deleted'
        with self.assertRaises(AssertionError):proof.require_ack(before,after,'different')

    def test_unknown_false_fact_is_rejected_before_reading_root(self):
        r=dict(state='Unknown',actor_subject='s',container_id='c',instance_id='i',relative_path='work/recycle-rpc.bin',root_started_at_utc='time',execution_job_id='execution',cancel_requested_at_utc=None,lease_token=None,lease_expires_at_utc=None,root_state='Unknown',source_retained=False,source_restored=None,history_verified=True,history_may_exist=True)
        data=dict(row=r,pending=0,accepted=1,confirmed=1,planJob=dict(state='Succeeded'))
        with patch.object(proof,'sql',return_value=data),patch.object(proof,'read_operation') as read,self.assertRaises(AssertionError):proof.prove(dict(id='id',state='Unknown',executionJobId='execution'),dict(subject='s',containerId='c',instanceId='i'),Path('/unused'))
        read.assert_not_called()

    def test_ui_proof_requires_one_ack_and_distinct_admin_with_no_cancel(self):
        logout=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        b=dict(actualUi=True,subject='owner',recoverySubject='different',registrations=1,confirmations=1,cancellations=0,acknowledgements=1,managementWrites=1,originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,logoutReceipts=[logout.copy(),logout.copy()])
        gate.browser_proof(b,dict(subject='owner'))
        for field,bad in [('recoverySubject','owner'),('acknowledgements',2),('managementWrites',2),('cancellations',1),('cancelledDecisionPreserved',False),('hintsCleared',False),('differentAdminOldIdStatus',200)]:
            with self.subTest(field=field),self.assertRaises(AssertionError):gate.browser_proof({**b,field:bad},dict(subject='owner'))

    def test_scope_is_rejected_before_touching_unrelated_source(self):
        with patch.object(proof,'file_snapshot') as read,self.assertRaises(AssertionError):proof.displace(dict(identifier='a'*64,folder=Path('/tmp/unrelated')),dict(containerId='a'*64,folder='/tmp/unrelated'),SimpleNamespace())
        read.assert_not_called()

    def test_gate_preserves_unsettled_data_and_independently_checks_before_cleanup(self):
        code=Path(gate.__file__).read_text();self.assertLess(code.index("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'"),code.index('moved=displace('))
        self.assertLess(code.index("e/'independent-live.log'"),code.index('.unlink()'));self.assertIn("f['retain_unsettled']=True",code)
        self.assertLess(code.index('typed_equal(canceled,unknown)'),code.index("save('acknowledge.json'"))
        browser=Path(gate.__file__).with_suffix('.mjs').read_text()
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'clock.install('):self.assertNotIn(forbidden,browser)


if __name__=='__main__':unittest.main()
