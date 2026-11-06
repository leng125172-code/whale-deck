"""Gate boundaries with explicit SQL/namespace fixtures; never product mutations."""
import copy
import hashlib
import json
import os
from pathlib import Path
from contextlib import contextmanager
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch
import validate_container_recycle_purge_browser as gate
from test_container_recycle_purge_browser_proof import hdd
from container_recycle_purge_rpc_proof import digest


class GateScopeTests(unittest.TestCase):
    def test_browser_waits_for_final_parent_verification_without_native_mutation_shortcuts(self):
        root=Path(__file__).parent
        script=(root/'validate_container_recycle_purge_browser.mjs').read_text()
        self.assertLess(script.index("coordinate('ready-to-purge'"),script.index("name: '暂不清理'"))
        self.assertLess(script.index("coordinate('final-confirm'"),script.index("name: '确认删除留存原件'"))
        self.assertNotIn('page.route(',script);self.assertNotIn('storageState',script)
        verifier=(root/'verify_container_recycle_purge_browser.py').read_text()
        self.assertIn("hdd_observation(source['request']['operationId'])",verifier)
        self.assertIn("e/'hdd-after-cleanup.json'",verifier)
        for forbidden in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.terminate(','.kill('):self.assertNotIn(forbidden,verifier)

    def ready(self,root):
        value=dict(id='291a9ea0-e736-4f2c-a090-1cce1138fbb3',version=4)
        r=dict(state='Ready',version=4,execution_job_id=None,root_started_at_utc=None,receipt_json=None,apply_started_at_utc=None,reservation_json=None,publish_barrier=False)
        data=dict(row=r,executionJob=None,confirmed=0,canceled=0)
        records=dict(root={'saved':'actual-before-fixture'},history={'saved':'history-before-fixture'})
        observation=hdd();source=dict(request=dict(operationId='a'*32),receipt={'fixture':'receipt'},physical={'retained':'fixture'},records=records,hdd=observation)
        p=dict(sourceRecordDigest=digest(b'whaledeck-container-recycle-purge-source-v1\n',records['root']),historyMetadataDigest=digest(b'whaledeck-container-recycle-purge-history-v1\n',records['history']),storeIdentity=observation['storeIdentity'],storageHash=observation['storageHash'])
        return value,dict(folder=str(root)),source,data,p

    def test_irreversible_handoff_requires_ready_and_no_execution_or_native_record(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);value,f,source,data,p=self.ready(root);op=gate.execution(value['id'])
            with patch.object(gate,'plan_join',return_value=(data,p,op)),patch.object(gate.purge_io,'STATE',root),patch.object(gate.source_io,'physical',return_value=source['physical']),patch.object(gate.source_io,'read_operation',return_value=source['records']),patch.object(gate,'hdd_observation',return_value=source['hdd']):
                self.assertTrue(gate.prove_ready(value,f,source,root)['originalStillRetained'])
                for field,bad in [('state','Pending'),('execution_job_id',op),('root_started_at_utc','started'),('reservation_json',{}),('apply_started_at_utc','started'),('publish_barrier',True)]:
                    saved=data['row'][field];data['row'][field]=bad
                    with self.subTest(field=field),self.assertRaises(AssertionError):gate.prove_ready(value,f,source,root)
                    data['row'][field]=saved
                for suffix in ('.json','.lock'):
                    target=root/(gate.uuid.UUID(op).hex+suffix);target.write_text('fixture')
                    with self.subTest(suffix=suffix),self.assertRaises(AssertionError):gate.prove_ready(value,f,source,root)
                    target.unlink()

    def test_handoff_refuses_changed_physical_file_history_digest_or_store(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);value,f,source,data,p=self.ready(root);op=gate.execution(value['id'])
            with patch.object(gate,'plan_join',return_value=(data,p,op)),patch.object(gate.purge_io,'STATE',root),patch.object(gate.source_io,'physical',return_value=source['physical']) as physical,patch.object(gate.source_io,'read_operation',return_value=source['records']),patch.object(gate,'hdd_observation',return_value=source['hdd']):
                physical.return_value={'retained':'foreign'}
                with self.assertRaises(AssertionError):gate.prove_ready(value,f,source,root)
                physical.return_value=source['physical']
                for field in ('sourceRecordDigest','historyMetadataDigest','storageHash','storeIdentity'):
                    old=p[field];p[field]=[1,2] if field=='storeIdentity' else '0'*64
                    with self.subTest(field=field),self.assertRaises(AssertionError):gate.prove_ready(value,f,source,root)
                    p[field]=old

    def test_preservation_includes_every_old_purge_row_and_scope_is_exact(self):
        def query(sql,**parameters):
            self.assertEqual(parameters['cid'],'a'*64)
            if 'container_recycle_purge_intents' in sql:
                self.assertIn('to_jsonb(c)::text',sql);self.assertIn("container_id<>:'cid'",sql)
                return [{'id':'opaque','sha256':'A'*64}]
            return {'sources':[],'restores':[]}
        with patch.object(gate,'query',side_effect=query):
            result=gate.old_rows('a'*64);self.assertEqual(len(result['purges']),1)
            for bad in ('a'*63,'A'*64,'a'*64+"' OR TRUE"):
                with self.subTest(bad=bad),self.assertRaises(AssertionError):gate.old_rows(bad)

    @unittest.skipUnless(os.geteuid()==0,'temporary root-owned HDD namespace fixture requires Linux root')
    def test_hdd_reacquisition_reopens_read_only_attested_fd_and_reads_actual_bytes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);namespace=root/gate.source_io.NAMESPACE;namespace.mkdir(mode=0o700);op='a'*32
            (namespace/(op+'.json')).write_text(json.dumps({'fixture':'metadata'}));(namespace/(op+'.json')).chmod(0o600);data=namespace/(op+'.data');data.write_bytes(b'archive bytes');data.chmod(0o600)
            calls=[]
            @contextmanager
            def target(**kwargs):
                self.assertEqual(kwargs,dict(create_namespace=False,read_only=True));fd=os.open(root,os.O_RDONLY|os.O_DIRECTORY)
                try:yield {'fixture':'storage'},fd
                finally:os.close(fd)
            def attest(config,fd,**kwargs):
                self.assertEqual(kwargs,dict(read_only=True));self.assertEqual(os.fstat(fd).st_ino,namespace.stat().st_ino);calls.append(fd)
            def open_at(base,name,flags):return os.open(name,flags,dir_fd=base)
            io=SimpleNamespace(storage=SimpleNamespace(target=target,attest=attest),files=SimpleNamespace(open_at=open_at))
            with patch.object(gate.source_io,'installed',return_value=io):
                v=gate.hdd_observation(op)
            self.assertEqual(v['data']['sha256'],hashlib.sha256(b'archive bytes').hexdigest().upper());self.assertEqual(v['data']['identity'][1],data.stat().st_ino);self.assertEqual(len(calls),2)


if __name__=='__main__':unittest.main()
