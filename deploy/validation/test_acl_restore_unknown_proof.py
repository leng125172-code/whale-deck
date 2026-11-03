"""Restore Unknown provenance/ack rejection; synthetic rows, no real services."""
import copy
import unittest
import test_acl_restore_fault_proof as restoration_fixtures
import test_acl_unknown_proof as unknown_fixtures
from acl_restore_unknown_proof import require_restore_unknown,verify_restore_unknown_audits,restore_unknown_sources,recovery_source_summary,require_recovery_source_ui,RECOVERY_SOURCE_FIELDS,require_source_denials
from acl_restore_fault_proof import prove_binding,verify_restore_audits,restore_fault_version
from verify_container_acl_unknown import require_source_version,require_acknowledgement
from validate_container_acl_unknown import unknown_mode,unknown_arguments

class RestoreUnknownProofTests(unittest.TestCase):
    def setUp(self):
        self.fixture=restoration_fixtures.RestoreFaultProvenanceTests();self.fixture.setUp()
        self.row,self.audits,_=self.fixture.audit_fixture()
        self.row.update(state='Unknown',acknowledged_by=None)
        self.audits[-1]['result']='Unknown'
        self.baseline={**self.fixture.baseline,'aclRestoreFaultVersion':7,'kind':'post-acl-target-removed','postAcl':True,'instanceChanged':False}

    def source_ui_fixture(self):
        baseline={**self.baseline,'id':'intent','originalSubject':'old','recoverySourceUiVersion':1,'restoreSource':self.fixture.source}
        recovery={'id':'intent','subject':'new','recoverySourceUiVersion':1}
        source=recovery_source_summary(baseline)
        text='历史ACL恢复来源 '+source['captureId']+' '+source['changedAclType']+' '+('文件' if source['kind']=='File' else '目录')+' time 原模式 '+format(source['originalMode'],'04o')+' access '+str(source['accessEntryCount'])+' 项 default '+str(source['defaultEntryCount'])+' 项 这是登记时保存的历史来源摘要，不证明当前文件状态或恢复成功。'
        proof={'schemaVersion':1,'id':'intent','subject':'new','reads':[{'status':200,'noStore':True,'source':copy.deepcopy(source)} for _ in range(3)],'cardText':text,'timeText':'time','confirmationText':'intent '+source['captureId']+' '+source['changedAclType'],'cardInViewport':True,'confirmationInViewport':True,'horizontalOverflow':False}
        return baseline,recovery,proof

    def test_source_ui_proof_seven_requires_explicit_marker_and_preserves_old_profiles(self):
        for instance in (False,True):
            baseline={**self.baseline,'aclRestoreFaultVersion':8 if instance else 7,'kind':'post-acl-instance-change' if instance else 'post-acl-target-removed','instanceChanged':instance}
            old={'proofVersion':6 if instance else 5,'aclRestoreFaultVersion':baseline['aclRestoreFaultVersion']}
            require_source_version(old,baseline)
            new={**old,'proofVersion':7,'recoverySourceUiVersion':1};fresh={**baseline,'recoverySourceUiVersion':1}
            require_source_version(new,fresh)
            for source,base in ((new,baseline),(old,fresh),({**new,'recoverySourceUiVersion':True},fresh),(new,{**fresh,'recoverySourceUiVersion':True})):
                with self.subTest(source=source,base=base),self.assertRaises(AssertionError):require_source_version(source,base)
        with self.assertRaises(AssertionError):require_source_version({'proofVersion':7},dict(postAcl=True,instanceChanged=False))

    def test_source_ui_cli_is_opt_in_and_cannot_change_fault_mode(self):
        self.assertEqual(unknown_arguments(['source-ui'],True),(True,False,True))
        self.assertEqual(unknown_arguments(['post-instance','source-ui'],True),(True,True,True))
        self.assertEqual(unknown_arguments([],True),(True,False,False))
        for args,restore in ((['source-ui'],False),(['source-ui','post-instance'],True),(['source-ui','source-ui'],True),(['post-acl','source-ui'],True),(['post-instance','source-ui','extra'],True)):
            with self.subTest(args=args),self.assertRaises(AssertionError):unknown_arguments(args,restore)

    def test_source_ui_proof_requires_actual_card_three_exact_reads_and_current_actor(self):
        baseline,recovery,proof=self.source_ui_fixture();require_recovery_source_ui(baseline,recovery,proof)
        for key in proof:
            changed=copy.deepcopy(proof);del changed[key]
            with self.subTest(missing=key),self.assertRaises((AssertionError,KeyError)):require_recovery_source_ui(baseline,recovery,changed)
        for key,value in [('schemaVersion',True),('id','foreign'),('subject','old'),('cardInViewport',False),('confirmationInViewport',False),('horizontalOverflow',True),('cardText','fake'),('timeText','absent'),('confirmationText','intent only'),('extra','private')]:
            changed={**proof,key:value}
            with self.subTest(key=key),self.assertRaises(AssertionError):require_recovery_source_ui(baseline,recovery,changed)
        for count in (0,1,2,4):
            with self.subTest(count=count),self.assertRaises(AssertionError):require_recovery_source_ui(baseline,recovery,{**proof,'reads':(proof['reads']*2)[:count]})

    def test_source_ui_never_accepts_changed_missing_or_overbroad_summary_at_any_read(self):
        baseline,recovery,proof=self.source_ui_fixture()
        for index in range(3):
            for key,value in [('status',204),('status',True),('status',403),('noStore',False),('source',None)]:
                changed=copy.deepcopy(proof);changed['reads'][index][key]=value
                with self.subTest(index=index,key=key,value=value),self.assertRaises(AssertionError):require_recovery_source_ui(baseline,recovery,changed)
            for field in RECOVERY_SOURCE_FIELDS:
                changed=copy.deepcopy(proof);changed['reads'][index]['source'][field]='replacement'
                with self.subTest(index=index,field=field),self.assertRaises(AssertionError):require_recovery_source_ui(baseline,recovery,changed)
            for extra in ('accessAcl','historyDigest','sourceInstanceId'):
                changed=copy.deepcopy(proof);changed['reads'][index]['source'][extra]='private'
                with self.subTest(index=index,extra=extra),self.assertRaises(AssertionError):require_recovery_source_ui(baseline,recovery,changed)

    def test_source_summary_denials_require_actual_401_403_and_normal_http_logout(self):
        proof={'schemaVersion':1,'anonymousStatus':401,'ordinaryStatus':403,'ordinaryAdministrator':False,'anonymousIsolatedSession':True,'normalHttpDualLogout':True}
        require_source_denials(proof)
        for key in proof:
            changed=copy.deepcopy(proof);del changed[key]
            with self.subTest(key=key),self.assertRaises(AssertionError):require_source_denials(changed)
        for key,value in [('anonymousStatus',200),('ordinaryStatus',404),('ordinaryAdministrator',0),('normalHttpDualLogout',False),('extra','private')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_source_denials({**proof,key:value})

    def test_restore_unknown_has_distinct_provenance_and_selected_source(self):
        require_restore_unknown(self.baseline)
        source=dict(proofVersion=5,aclRestoreFaultVersion=7)
        require_source_version(source,self.baseline)
        self.assertEqual(prove_binding(self.fixture.row,self.baseline,self.fixture.source,self.fixture.proof),self.fixture.source['accessAcl'])
        for change in ({'proofVersion':4},{'proofVersion':True},{'aclRestoreFaultVersion':6},{'aclRestoreFaultVersion':True}):
            with self.subTest(change=change),self.assertRaises(AssertionError):require_source_version({**source,**change},self.baseline)
        for change in ({'postAcl':False},{'instanceChanged':True},{'kind':'post-acl-agent-restart'},{'aclRestoreFaultVersion':True}):
            with self.subTest(change=change),self.assertRaises(AssertionError):require_restore_unknown({**self.baseline,**change})
        with self.assertRaises(AssertionError):require_source_version(source,dict(postAcl=True,instanceChanged=False))

    def test_unknown_result_is_not_success_failure_or_cancellation(self):
        verify_restore_unknown_audits(self.audits,self.row,self.baseline)
        verify_restore_audits(self.audits,self.row,self.baseline)
        for wrong in ('Succeeded','Failed','Canceled'):
            changed=copy.deepcopy(self.audits);changed[-1]['result']=wrong
            with self.subTest(wrong=wrong),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)

    def test_restore_audit_requires_original_source_hash_and_dedicated_prefix(self):
        for key,value in [('action','containers.files.acl.result'),('actor_subject','another-admin'),('job_id',self.row['id'])]:
            changed=copy.deepcopy(self.audits);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)
        for key,value in [('historyRestore',False),('restoreSourceHash','foreign-source')]:
            changed=copy.deepcopy(self.audits);changed[-1]['detail_json'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)

    def test_ack_is_separate_current_admin_event_and_retains_unknown_result(self):
        row={**self.row,'state':'Acknowledged','acknowledged_by':'new-admin'}
        ack={**copy.deepcopy(self.audits[-1]),'action':'containers.files.acl.restore.unknown-acknowledged','actor_subject':'new-admin'}
        verify_restore_unknown_audits(self.audits+[ack],row,self.baseline)
        for key,value in [('actor_subject',row['actor_subject']),('result','Succeeded'),('job_id',row['id']),('action','containers.files.acl.unknown-acknowledged')]:
            changed={**ack,key:value}
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits+[changed],row,self.baseline)
        with self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits,row,self.baseline)
        with self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits+[ack],self.row,self.baseline)

    def test_ack_cannot_rewrite_restore_source_or_any_authority_hash(self):
        fixture=unknown_fixtures.UnknownProofTests();fixture.setUp()
        keys=('restore_source_json','request_hash','plan_hash','execution_hash','owner_hash')
        for key in keys:
            fixture.original['row'][key]=fixture.final['row'][key]='original-'+key
        require_acknowledgement(fixture.original,fixture.final,'old','new')
        for key in keys:
            changed=copy.deepcopy(fixture.final);changed['row'][key]='rewritten'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_acknowledgement(fixture.original,changed,'old','new')

    def test_restore_dependencies_include_real_capture_ui_and_shared_barrier_sources(self):
        sources=restore_unknown_sources()
        self.assertEqual(len(sources),len(set(sources)))
        for name in ('acl_restore_fault_ui.mjs','acl_restore_fault_proof.py','validate_container_acl_restore_unknown.py','verify_container_acl_restore_unknown.py','validate_container_directory_unknown.py','bound_container_directory_cut.py','test_acl_restore_unknown_proof.py'):
            self.assertIn(name,sources)

    def test_same_cid_instance_change_is_exact_profile_eight_with_proof_six(self):
        baseline={**self.baseline,'aclRestoreFaultVersion':8,'kind':'post-acl-instance-change','instanceChanged':True}
        source=dict(proofVersion=6,aclRestoreFaultVersion=8)
        require_restore_unknown(baseline);require_source_version(source,baseline)
        self.assertEqual(restore_fault_version(('post-acl-target-removed',)),7)
        self.assertEqual(restore_fault_version(('post-acl-instance-change',)),8)
        self.assertEqual(prove_binding(self.fixture.row,baseline,self.fixture.source,self.fixture.proof),self.fixture.source['accessAcl'])
        for delta in ({'proofVersion':5},{'aclRestoreFaultVersion':7},{'proofVersion':True},{'aclRestoreFaultVersion':True}):
            with self.subTest(delta=delta),self.assertRaises(AssertionError):require_source_version({**source,**delta},baseline)
        for delta in ({'instanceChanged':False},{'instanceChanged':1},{'postAcl':False},{'kind':'post-acl-target-removed'},{'aclRestoreFaultVersion':7}):
            with self.subTest(delta=delta),self.assertRaises(AssertionError):require_source_version(source,{**baseline,**delta})
        with self.assertRaises(AssertionError):restore_fault_version(('post-acl-target-removed','post-acl-instance-change'))
        with self.assertRaises(AssertionError):require_source_version(source,dict(postAcl=True,instanceChanged=True))
        from verify_container_acl_faults import validate_source_profile
        from validate_container_acl_faults import terminal_state
        with self.assertRaises(AssertionError):validate_source_profile(dict(schemaVersion=14,aclRestoreFaultVersion=8,sha256={}),['post-acl-instance-change'])
        with self.assertRaises(AssertionError):terminal_state('post-acl-instance-change')

    def test_explicit_instance_cli_cannot_fall_through_to_removal_or_prewrite(self):
        self.assertEqual(unknown_mode([],True),(True,False))
        self.assertEqual(unknown_mode(['post-instance'],True),(True,True))
        self.assertEqual(unknown_mode([],False),(False,False))
        self.assertEqual(unknown_mode(['post-acl'],False),(True,False))
        self.assertEqual(unknown_mode(['post-instance'],False),(True,True))
        for args in (['post-acl'],['unknown'],['post-instance','post-acl'],'post-instance',[True]):
            with self.subTest(args=args),self.assertRaises(AssertionError):unknown_mode(args,True)
        with self.assertRaises(AssertionError):unknown_mode([],1)

    def test_instance_profile_preserves_unknown_audit_and_explicit_ack_truth(self):
        baseline={**self.baseline,'aclRestoreFaultVersion':8,'kind':'post-acl-instance-change','instanceChanged':True}
        verify_restore_audits(self.audits,self.row,baseline)
        row={**self.row,'state':'Acknowledged','acknowledged_by':'new-admin'}
        ack={**copy.deepcopy(self.audits[-1]),'action':'containers.files.acl.restore.unknown-acknowledged','actor_subject':'new-admin'}
        verify_restore_audits(self.audits+[ack],row,baseline)
        changed=copy.deepcopy(self.audits);changed[-1]['result']='Succeeded'
        with self.assertRaises(AssertionError):verify_restore_audits(changed,self.row,baseline)

if __name__=='__main__':unittest.main()
