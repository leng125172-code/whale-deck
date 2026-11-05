import copy
import datetime as dt
import hashlib
from pathlib import Path
import unittest
import uuid
from unittest.mock import patch

import container_recycle_cross_directory_proof as proof
import validate_container_recycle_cross_directory as gate


class RecycleCrossDirectoryTests(unittest.TestCase):
    def baseline(self):
        return dict(containerId='b'*64, instanceId='other-instance', path=proof.WAITING_PATH, source='/unused')

    def directory(self, at, version=2):
        row={k:k for k in proof.STABLE};row.update(id='11111111-2222-3333-4444-555555555555',container_id='b'*64,instance_id='other-instance',relative_path=proof.WAITING_PATH,actor_subject='recovery',state='Pending',root_started_at_utc=None,completed_at_utc=None,publish_barrier=False,lease_token=None,lease_expires_at_utc=None,version=version,next_attempt_at_utc=(at+dt.timedelta(seconds=6)).isoformat())
        return dict(row=row,job=dict(id=row['id'],state='Queued'),accepted=1,terminalAudits=0,pending=1)

    def samples(self):
        start=dt.datetime(2026,11,5,10,tzinfo=dt.timezone.utc);raw=b'original-unknown-root';values=[]
        for i in range(4):
            at=start+dt.timedelta(seconds=i*6);values.append(dict(observedAtUtc=at.isoformat(),sql=self.directory(at,2+i*2),rootAbsent=True,targetAbsent=True,recycleUnknownProtected=True,originalPhysicalUnchanged=True,unknownRootSha256=hashlib.sha256(raw).hexdigest()))
        return values,dict(subject='recovery'),start+dt.timedelta(seconds=25),raw

    def test_blocked_directory_stays_unstarted_and_allows_transient_claim_lease(self):
        at=dt.datetime(2026,11,5,10,tzinfo=dt.timezone.utc);data=self.directory(at);self.assertTrue(proof.require_blocked(data,self.baseline(),'recovery',at))
        data['row'].update(lease_token='claim',lease_expires_at_utc=(at+dt.timedelta(seconds=90)).isoformat());self.assertFalse(proof.require_blocked(data,self.baseline(),'recovery',at))
        for field,bad in [('root_started_at_utc',at.isoformat()),('publish_barrier',True),('state','Applying'),('actor_subject','other')]:
            changed=self.directory(at);changed['row'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.require_blocked(changed,self.baseline(),'recovery',at)

    def test_four_natural_deferrals_require_distinct_versions_due_times_and_absent_target(self):
        values,registered,released,raw=self.samples();initial=copy.deepcopy(values[0]['sql']);proof.require_deferrals(values,self.baseline(),registered,initial,released,raw)
        for mutate in [lambda v:v[1]['sql']['row'].update(version=2),lambda v:v[1].update(targetAbsent=False),lambda v:v[1].update(recycleUnknownProtected=False),lambda v:v[1].update(unknownRootSha256='different'),lambda v:v[1].update(observedAtUtc=v[0]['observedAtUtc'])]:
            altered=copy.deepcopy(values);mutate(altered)
            with self.assertRaises(AssertionError):proof.require_deferrals(altered,self.baseline(),registered,initial,released,raw)

    def test_deferral_cannot_change_the_original_directory_binding(self):
        values,registered,released,raw=self.samples();initial=copy.deepcopy(values[0]['sql'])
        for key in ('id','container_id','instance_id','login_binding','request_hash','plan_hash'):
            altered=copy.deepcopy(values);altered[2]['sql']['row'][key]='changed'
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_deferrals(altered,self.baseline(),registered,initial,released,raw)

    def test_only_the_exact_new_directory_json_and_lock_are_allowed(self):
        identifier='11111111-2222-3333-4444-555555555555';op=uuid.UUID(identifier).hex
        before=dict(directoryRoot={'old.json':dict(sha256='old'),'old.lock':dict(sha256='old-lock')},recycleRoot={},recycleHdd={},core={'core':'same'},protected={'secret':'unchanged'})
        after=copy.deepcopy(before);after['directoryRoot'].update({op+'.json':dict(sha256='new'),op+'.lock':dict(sha256='empty')});proof.preserved_cross(before,after,[],identifier)
        for kind in ('extra','old','core','missing'):
            bad=copy.deepcopy(after)
            if kind=='extra':bad['directoryRoot']['unrelated.json']={}
            elif kind=='old':bad['directoryRoot']['old.lock']['sha256']='changed'
            elif kind=='core':bad['core']['core']='restarted'
            else:del bad['directoryRoot'][op+'.json']
            with self.subTest(kind=kind),self.assertRaises(AssertionError):proof.preserved_cross(before,bad,[],identifier)

    def test_directory_resource_must_be_different_before_inspection(self):
        with patch.object(proof,'command') as command,self.assertRaises(AssertionError):proof.waiting_baseline(dict(identifier='same',folder=Path('/tmp/owned')),dict(identifier='same',folder=Path('/tmp/other')))
        command.assert_not_called()

    def test_browser_proof_requires_exactly_plan_register_ack_and_completed_original_directory(self):
        logout=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        d=dict(plans=1,registrations=1,cancellations=0,uploads=0,state='Succeeded',anonymousStatus=401)
        b=dict(actualUi=True,subject='owner',recoverySubject='different',registrations=1,confirmations=1,cancellations=0,acknowledgements=1,managementWrites=3,originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,logoutReceipts=[logout,logout],waitingDirectory=d)
        gate.browser_proof(b,dict(subject='owner'))
        for key,bad in [('managementWrites',4),('acknowledgements',2),('cancellations',1)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:bad},dict(subject='owner'))
        for key,bad in [('registrations',2),('state','Pending'),('anonymousStatus',200)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,'waitingDirectory':{**d,key:bad}},dict(subject='owner'))

    def test_ack_waits_for_deferrals_and_cleanup_waits_for_independent_live_proof(self):
        code=Path(gate.__file__).read_text();self.assertIn("if blocked and (e/'recovery-canceled.json').exists()",code);self.assertLess(code.index('observe_waiting(unknown,'),code.index("save('acknowledge.json'"));self.assertLess(code.index("e/'independent-live.log'"),code.index('clean_waiting(waiting_verified)'))
        self.assertIn("other['retain_unsettled']=True",code)
        browser=Path(gate.__file__).with_suffix('.mjs').read_text();self.assertLess(browser.index('registerWaitingDirectory(adminPage'),browser.index("'确认处置并释放保护', exact: true }).click()"));self.assertLess(browser.index('await showWaiting(recovery'),browser.index("phase = 'normal-recovery-logout'"))


if __name__=='__main__':unittest.main()
