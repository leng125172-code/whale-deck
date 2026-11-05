"""Explicit rendering/identity doubles only; not live viewport acceptance."""
import copy
from pathlib import Path
import unittest
from recursive_acl_restore_note_proof import verify, ACK_WARNING
from recursive_acl_restore_job_results_proof import UNKNOWN_WARNING
from test_recursive_acl_restore_unknown_business_proof import fixture as unknown_fixture


def fixture():
    original=unknown_fixture();data=original['after'];job=copy.deepcopy(original['ack_job']);cases=[]
    for width in (320,1440):
        for theme in ('dark','light'):
            cases.append(dict(job=copy.deepcopy(job['job']),viewport=dict(width=width,height=600),theme=theme,
                regions=[dict(name=name,text=text,rect=dict(x=30,y=90,width=100,height=250)) for name,text in (('unknown',UNKNOWN_WARNING),('acknowledged',ACK_WARNING))],
                screenshots=[f'note-{width}-{theme}-{name}.png' for name in ('unknown','acknowledged')]))
    packet=dict(revision='a'*40,jobId=data['row']['execution_job_id'],subject='fresh-read-only-admin',job=job,
        currentAcknowledgedOnly=True,domainWrites=0,logout=dict(normalUiLogout=True,providerNavigationObserved=True,finalLoginPage=True,postStatus=302,bffStatus=401,identityStatus=403),anonymous=[401]*3,cases=cases)
    return packet,data


class CurrentUnknownNoteTests(unittest.TestCase):
    def test_four_current_acknowledged_views_do_not_backfill_old_unknown_phase(self):
        packet,data=fixture();proof=verify(packet,data,'a'*40)
        self.assertEqual(proof['cases'],4);self.assertEqual(proof['noteImages'],8)
        self.assertFalse(proof['preAcknowledgementVisibilityAccepted'])

    def test_duplicate_missing_or_unrecognized_viewport_is_not_accepted(self):
        for change in ('duplicate','missing','height','width','theme'):
            p,d=fixture()
            if change=='duplicate':p['cases'][1]=copy.deepcopy(p['cases'][0])
            elif change=='missing':p['cases'].pop()
            elif change in ('height','width'):p['cases'][0]['viewport'][change]=601 if change=='height' else True
            else:p['cases'][0]['theme']='system'
            with self.subTest(change=change),self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_clipped_nonfinite_boolean_or_zero_rect_is_rejected(self):
        for key,bad in (('y',500),('x',-1),('width',1000),('height',0),('x',float('nan')),('height',True)):
            p,d=fixture();p['cases'][0]['regions'][0]['rect'][key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_note_text_labels_and_screenshot_identity_cannot_be_replaced(self):
        for change in ('text','label','screenshot','count'):
            p,d=fixture()
            if change=='text':p['cases'][0]['regions'][0]['text']='零修改'
            elif change=='label':p['cases'][0]['regions'][0]['name']='acknowledged'
            elif change=='screenshot':p['cases'][0]['screenshots'][0]='../old.png'
            else:p['job']['labels']['actual'][1]='实际修改'
            with self.subTest(change=change),self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_new_observer_must_not_impersonate_original_owner_or_resolver(self):
        for subject in ('original','resolver',''):
            p,d=fixture();p['subject']=subject
            with self.subTest(subject=subject),self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_domain_writes_wrong_task_state_or_retroactive_claim_fail(self):
        for field,bad in (('domainWrites',1),('domainWrites',False),('currentAcknowledgedOnly',False),('jobId','other')):
            p,d=fixture();p[field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):verify(p,d,'a'*40)
        p,d=fixture();d['row']['state']='Unknown'
        with self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_every_view_binds_current_job_receipt_and_requires_normal_exit(self):
        p,d=fixture();p['cases'][0]['job']['resultJson']='{}'
        with self.assertRaises(AssertionError):verify(p,d,'a'*40)
        p,d=fixture();p['anonymous']=[401,401,200]
        with self.assertRaises(AssertionError):verify(p,d,'a'*40)
        p,d=fixture();p['logout']['providerNavigationObserved']=False
        with self.assertRaises(AssertionError):verify(p,d,'a'*40)

    def test_browser_observes_intersection_and_does_not_inject_state_or_write_domain(self):
        root=Path(__file__).parent
        note=(root/'installed_ui_restore_note_visibility.mjs').read_text();entry=(root/'validate_recursive_acl_restore_unknown_note.mjs').read_text()
        for forbidden in ('.route(','.addCookies(','localStorage.setItem','sessionStorage.setItem','request.post(','request.put('):
            self.assertNotIn(forbidden,note+entry)
        for required in ('toBeInViewport({ratio:1})','boundingBox','toBeVisible','document.fonts.ready'):self.assertIn(required,note)
        self.assertIn('domainWrites',entry);self.assertIn('currentAcknowledgedOnly:true',entry)


if __name__=='__main__':unittest.main()
