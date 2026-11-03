"""Acceptance cannot silently lose a role, scene, cancellation or auth boundary."""
import copy
import importlib.util
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('platform_ui_proof',ROOT/'deploy/validation/platform_maintenance_ui_proof.py')
M=importlib.util.module_from_spec(spec);spec.loader.exec_module(M)
REVISION='1'*40

def report():
    logout=dict(normalUiLogout=True,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True,postStatus=302)
    common=dict(executionRequests=0,otherWrites=0,anonymous=[401]*3,logout=logout)
    admin=dict(**copy.deepcopy(common),role='administrator',planRequests=4,forbidden=[],
        scenes=[dict(width=w,colorScheme=c,actualBuildRevision=REVISION,noHorizontalOverflow=True) for w in (320,1440) for c in ('dark','light')],
        cancelled=[dict(action=a,width=w,colorScheme=c,cancelVisible=True,planStatus=200) for a in ('apply-update','rollback') for w,c in ((320,'dark'),(1440,'light'))])
    ordinary=dict(**copy.deepcopy(common),role='ordinary',planRequests=0,cancelled=[],forbidden=[dict(status=403)]*5,
        scenes=[dict(width=w,colorScheme=c,forbiddenPage=True,buildCardAbsent=True) for w,c in ((320,'dark'),(1440,'light'))])
    return dict(revision=REVISION,actualUi=True,results=[admin,ordinary])

class PlatformUiProofTests(unittest.TestCase):
    def test_complete_evidence(self):
        M.prove_report(report(),REVISION)
    def rejected(self,edit):
        value=report();edit(value)
        with self.assertRaises((AssertionError,KeyError,TypeError)):M.prove_report(value,REVISION)
    def test_missing_role(self):
        self.rejected(lambda v:v['results'].pop())
    def test_duplicate_scene_cannot_fill_missing_theme(self):
        self.rejected(lambda v:v['results'][0]['scenes'].__setitem__(0,v['results'][0]['scenes'][1]))
    def test_duplicate_cancel_cannot_fill_missing_action(self):
        self.rejected(lambda v:v['results'][0]['cancelled'].__setitem__(0,v['results'][0]['cancelled'][1]))
    def test_stale_actual_build_rejected(self):
        self.rejected(lambda v:v['results'][0]['scenes'][0].update(actualBuildRevision='2'*40))
    def test_execution_request_never_readonly(self):
        self.rejected(lambda v:v['results'][0].update(executionRequests=1))
    def test_provider_session_must_also_end(self):
        self.rejected(lambda v:v['results'][1]['logout'].update(identityStatus=200))
    def test_ordinary_success_is_not_permission_denial(self):
        self.rejected(lambda v:v['results'][1]['forbidden'].__setitem__(0,dict(status=200)))
    def test_anonymous_success_is_not_logout(self):
        self.rejected(lambda v:v['results'][0]['anonymous'].__setitem__(0,200))
    def test_missing_card_proof_cannot_be_inferred_from_forbidden_title(self):
        self.rejected(lambda v:v['results'][1]['scenes'][0].update(buildCardAbsent=False))

if __name__=='__main__':unittest.main()
