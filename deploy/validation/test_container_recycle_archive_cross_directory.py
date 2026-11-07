"""Constructed source-refusal and archive DB-barrier witnesses; no real writes."""
import ast,copy,datetime as dt,hashlib,unittest,uuid
from pathlib import Path
from unittest.mock import patch
import archive_cross_directory_proof as proof
import validate_container_recycle_archive_cross_directory as gate
import test_container_recycle_cross_directory as prior


class ArchiveCrossDirectoryTests(unittest.TestCase):
    def refusal(self):
        row=dict(id='11111111-2222-3333-4444-555555555555',idempotency_key='11111111-2222-3333-4444-555555555556',source_intent_id='source',version=2,state='Ready',execution_job_id=None,root_started_at_utc=None)
        dto=dict(id=row['id'],version=2,sourceIntentId='source');event=dict(id=row['id'],sourceIntentId='source',rejectedKey='11111111-2222-3333-4444-555555555557',status=409,code='OPERATION_CONFLICT',actualApiRequest=True,originAndIdempotencySupplied=True,before=dto,after=copy.deepcopy(dto));return dict(row=row),event

    def guard(self):
        return {**dict.fromkeys(proof.TABLES,0),'container_recycle_archive_intents':1,'file_path_locks':0}

    def samples(self):
        old=prior.RecycleCrossDirectoryTests();v,r,released,raw=old.samples()
        for i,s in enumerate(v):s['archiveUnknownProtected']=s.pop('recycleUnknownProtected');s['barriers']=self.guard();s['directoryLockIdentity']=None if i==0 else [1,2]
        return v,r,released,raw,old.baseline()

    def test_source_rejection_requires_new_key_and_zero_new_intent_or_job(self):
        before,event=self.refusal();proof.require_source_refusal(event,before,copy.deepcopy(before),dict(intents=0,jobs=0))
        for bad in (dict(intents=1,jobs=0),dict(intents=0,jobs=1)):
            with self.subTest(empty=bad),self.assertRaises(AssertionError):proof.require_source_refusal(event,before,before,bad)

    def test_http_and_original_ready_binding_are_not_optional(self):
        before,event=self.refusal()
        for key,bad in (('status',202),('code','other'),('sourceIntentId','foreign'),('actualApiRequest',False),('originAndIdempotencySupplied',False),('rejectedKey',before['row']['idempotency_key'])):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_source_refusal({**event,key:bad},before,before,dict(intents=0,jobs=0))
        changed=copy.deepcopy(before);changed['row']['version']+=1
        with self.assertRaises(AssertionError):proof.require_source_refusal(event,before,changed,dict(intents=0,jobs=0))

    def test_refusal_is_not_inferred_from_an_unknown_or_executing_intent(self):
        before,event=self.refusal()
        for key,bad in (('state','Unknown'),('root_started_at_utc','started'),('execution_job_id','job')):
            altered=copy.deepcopy(before);altered['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_source_refusal(event,altered,altered,dict(intents=0,jobs=0))

    def test_only_archive_publish_barrier_can_explain_the_directory_delay(self):
        proof.require_archive_only_barrier(self.guard())
        for key in self.guard():
            bad=self.guard();bad[key]=0 if key=='container_recycle_archive_intents' else 1
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_archive_only_barrier(bad)
        with self.assertRaises(AssertionError):proof.require_archive_only_barrier({**self.guard(),'foreign':0})

    def test_four_actual_deferrals_require_separate_versions_due_times_and_unchanged_root(self):
        v,r,released,raw,b=self.samples();initial=copy.deepcopy(v[0]['sql']);proof.require_deferrals(v,b,r,initial,released,raw)
        changes=[lambda x:x[1]['sql']['row'].update(version=2),lambda x:x[1].update(targetAbsent=False),lambda x:x[1].update(archiveUnknownProtected=False),lambda x:x[1].update(unknownRootSha256='changed'),lambda x:x[1]['barriers'].update(container_copy_intents=1),lambda x:x[2].update(directoryLockIdentity=[1,3]),lambda x:x[2].update(directoryLockIdentity=None)]
        for change in changes:
            bad=copy.deepcopy(v);change(bad)
            with self.subTest(change=change),self.assertRaises(AssertionError):proof.require_deferrals(bad,b,r,initial,released,raw)

    def test_directory_cannot_start_early_or_change_owner_plan_or_binding(self):
        v,r,released,raw,b=self.samples();initial=copy.deepcopy(v[0]['sql'])
        for key,bad_value in (('root_started_at_utc','started'),('state','Applying'),('container_id','different'),('login_binding','different'),('plan_hash','different')):
            bad=copy.deepcopy(v);bad[2]['sql']['row'][key]=bad_value
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_deferrals(bad,b,r,initial,released,raw)

    def test_only_exact_new_directory_journal_and_lock_are_allowed(self):
        identifier='11111111-2222-3333-4444-555555555555';op=uuid.UUID(identifier).hex;before=dict(directoryRoot={'old.json':{'hash':'old'}},recycleRoot={},recycleHdd={},purgeRoot={},archiveRoot={},recycleRestoreRoot={},core={'core':'same'},protected={'secret':'same'});after=copy.deepcopy(before);after['directoryRoot'].update({op+'.json':{},op+'.lock':{}})
        proof.preserved_cross(before,after,'a'*32,['b'*32,'c'*32],['d'*32,'e'*32],identifier)
        for category,key in (('directoryRoot','unrelated.json'),('core','core'),('protected','secret')):
            bad=copy.deepcopy(after);bad[category][key]='changed'
            with self.subTest(category=category),self.assertRaises(AssertionError):proof.preserved_cross(before,bad,'a'*32,['b'*32,'c'*32],['d'*32,'e'*32],identifier)

    def test_foreign_peer_scope_refuses_before_docker_lookup(self):
        with patch.object(proof,'query') as queried:
            with patch('container_recycle_cross_directory_proof.command') as command,self.assertRaises(AssertionError):proof.waiting_baseline(dict(identifier='same',folder=Path('/tmp/one')),dict(identifier='same',folder=Path('/tmp/two')))
            command.assert_not_called();queried.assert_not_called()

    def test_browser_requires_directory_plan_register_and_unique_ack_only(self):
        receipt=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        b=dict(actualUi=True,subject='owner',recoverySubject='other',originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,acknowledgements=1,managementWrites=3,logoutReceipts=[receipt,receipt],jobProofs=[{},{}],archives=[{},dict(state='Unknown')],final=dict(state='Acknowledged',archiveCopied=None),waitingDirectory=dict(plans=1,registrations=1,cancellations=0,uploads=0,state='Succeeded',anonymousStatus=401),sourceRefusal=dict(status=409,code='OPERATION_CONFLICT'));f=dict(subject='owner',recoverySubject='other');gate.browser_proof(b,f)
        for key,bad in (('managementWrites',4),('acknowledgements',0),('hintsCleared',False)):
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:bad},f)

    def test_ack_is_held_until_deferrals_and_cleanup_until_independent_live(self):
        s=Path(gate.__file__).read_text();self.assertIn("other['retain_unsettled']=True",s);self.assertIn("(e/'waiting-blocked.json').exists()",s);self.assertIn('finish_waiting(waiting,registered,ack,e)',s);self.assertLess(s.index('independent(repo,e,True)'),s.index('clean_waiting(waiting_verified)'))
        js=Path(gate.__file__).with_suffix('.mjs').read_text();self.assertLess(js.index('registerWaitingDirectory(page'),js.index("name:'确认处置并释放保护'"));self.assertIn('assert.equal(managementWrites,2);assert.equal(acknowledgements,0)',js)
        self.assertIn('timeout:15_000,maxRedirects:0,failOnStatusCode:false',js);self.assertIn("'source-refusal-api-began.json'",js);self.assertIn("'source-refusal-api-received.json'",js)
        for bad in ('page.route(','storageState(','tracing.start(','process.env.VALIDATION_PASSWORD='):self.assertNotIn(bad,js)

    def test_independent_verifier_checks_both_scopes_without_mutation(self):
        s=(Path(__file__).parent/'verify_container_recycle_archive_cross_directory.py').read_text();calls={ast.unparse(n.func) for n in ast.walk(ast.parse(s)) if isinstance(n,ast.Call)}
        for required in ('verify_peer','preserved_cross','require_source_refusal','rejected_key','io.read_operation','published_proof'):self.assertIn(required,calls)
        for bad in ('clean_waiting','cleanup_owned_target','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(bad,calls)


if __name__=='__main__':unittest.main()
