"""Real temporary inode plan conversion and fail-closed browser cleanup boundaries."""
import copy,json,os
from pathlib import Path
import unittest
from validate_container_recycle_browser import native_plan
from container_recycle_rpc_proof import native_plan_proof
from test_container_recycle_rpc_proof import plan


def managed(p):
    c=p['command'];i=p['sourceIdentity'];return dict(SchemaVersion=p['schemaVersion'],Command=dict(Location={k[0].upper()+k[1:]:c[k] for k in ('containerId','instanceId','path')},ExpectedVersion=c['expectedVersion']),TargetProof=p['targetProof'],ParentIdentity=dict(Device=p['parentIdentity'][0],Inode=p['parentIdentity'][1]),ParentVersion=p['parentVersion'],OwnerUid=p['uid'],OwnerGid=p['gid'],Fingerprint=p['fingerprint'],SourceIdentity=dict(zip(('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs'),i[:8],strict=True)),SourceChangedAtUnixNs=i[8],Sha256=p['sha256'],Attributes=[dict(Name=n,Value=v) for n,v in p['attributes']])


class BusinessPlanProofTests(unittest.TestCase):
    def test_pascal_database_plan_roundtrip_does_not_lose_exact_metadata(self):
        p=plan();self.assertEqual(native_plan(managed(p)),p);native_plan_proof(native_plan(managed(p)))
    def test_corrupt_but_plausible_database_fields_do_not_become_a_trusted_root_plan(self):
        for field,value in [('Fingerprint','F'*64),('SourceChangedAtUnixNs',123),('Sha256','1'*64),('OwnerUid',True)]:
            m=managed(plan());m[field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):native_plan(m)
    def test_altered_attributes_and_foreign_paths_are_not_accepted_as_original(self):
        for kind in ('bytes','path','links'):
            m=managed(plan())
            if kind=='bytes':m['Attributes'][0]['Value']='eA=='
            elif kind=='path':m['Command']['Location']['Path']='work/other.bin'
            else:m['SourceIdentity']['LinkCount']=2
            with self.subTest(kind=kind),self.assertRaises(AssertionError):native_plan(m)
    def test_cleanup_remains_after_independent_live_proof_and_does_not_call_restore(self):
        root=Path(__file__).parent;code=(root/'validate_container_recycle_browser.py').read_text();self.assertLess(code.index("e/'independent-live.log'"),code.index('retained.unlink()'));self.assertLess(code.index('verify_case(canceled'),code.index("save('cancel-proof-accepted.json'"));self.assertIn("f['retain_unsettled']=True",code);self.assertIn("f['retain_unsettled']=False",code)
        browser=(root/'validate_container_recycle_browser.mjs').read_text()
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'RestoreContainerRecycle', 'docker exec'):
            self.assertNotIn(forbidden,browser)
        self.assertIn('logoutFromUserMenu(page,context,base)',browser);self.assertIn("'cancel-proof-accepted.json'",browser)

if __name__=='__main__':unittest.main()
