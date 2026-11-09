"""Owned temporary handoff tests; natural clock/deletion acceptance is separate."""
import ast
import copy
from datetime import datetime,timezone
import json
import os
from pathlib import Path
import time
import unittest
import uuid
from unittest.mock import patch
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import snapshot,read_at
from container_recycle_agent_io import file_snapshot
from validate_container_recycle_archive_plan import payload_snapshot
from verify_container_recycle_archive_aging import aging_handoff_proof,aging_container_proof,source_manifest_proof,aging_live_records,aging_live_evidence_proof
import test_container_recycle_archive_preexpiry as preexpiry
import test_container_recycle_archive_retirements as native
import validate_container_file_publishing as owned

source=Path(__file__).with_name('validate_container_recycle_archive_copies.py')
node=next(n for n in ast.parse(source.read_bytes()).body if isinstance(n,ast.FunctionDef) and n.name=='reserve_aging_handoff')
scope=dict(uuid=uuid,time=time,snapshot=snapshot,read_at=read_at,typed_equal=typed_equal)
exec(compile(ast.Module(body=[node],type_ignores=[]),str(source),'exec'),scope)
handoff=scope['reserve_aging_handoff']


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class AgingHandoffTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRetirementTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=self.f.engine;self.session=self.f.session;n=self.f.native
        plan=self.engine.preflight.plan(dict(archive=n.q,retainDays=1,mode='ArchiveData'),self.session)
        self.a=dict(operationId=self.f.q['operationId'],owner='9'*64,plan=plan);self.b={**self.a,'operationId':uuid.uuid4().hex}
        self.engine.reserve(self.a,self.session)
        self.early=preexpiry.capture(self.engine,self.a,self.session,self.protected)
        self.engine.discard(self.a,self.session);self.engine.reserve(self.b,self.session);self.engine.discard(self.b,self.session)
        protected=self.protected()
        self.reservations=dict(requests=[self.a,self.b],steps=[dict(roots=self.early['retirementRootsBefore'])],before=protected,after=copy.deepcopy(protected),roots=snapshot(self.engine.state_path))
        self.copied=dict(request=n.q,root=n.record(),actual=file_snapshot(n.base.leaf),canceledRequest=dict(operationId=uuid.uuid4().hex),journals=protected['archive'],claims=protected['lifecycles']['restore'],protectedAfter={k:protected['lifecycles'][k] for k in ('source','purge','hdd')})
        self.value=handoff(self.engine,plan,self.session,self.protected)
        q=self.value['request'];source_id=self.f.ref['operationId']
        self.current=dict(roots=snapshot(self.engine.state_path),journal=read_at(None,str(self.engine.state_path/(q['operationId']+'.json')))[1],marker=read_at(None,str(self.engine.claims_path/(source_id+'.json')))[1],oldJournals={r['operationId']:read_at(None,str(self.engine.state_path/(r['operationId']+'.json')))[1] for r in (self.a,self.b)},protected=self.protected())

    def protected(self):
        n=self.f.native
        return dict(archive=snapshot(n.engine.state_path),lifecycles=dict(source=snapshot(n.source.state_path),restore=snapshot(n.engine.restore.state_path),purge=snapshot(n.engine.preflight.purges.state_path),hdd=payload_snapshot(n.base.target)),namespace=preexpiry.namespace_snapshot(n.base.root))

    def prove(self,value=None,current=None):return aging_handoff_proof(self.value if value is None else value,self.copied,self.reservations,self.early,self.current if current is None else current)

    def test_reserved_handoff_is_valid_but_observer_never_mutates_or_deletes(self):
        before=self.protected();roots=snapshot(self.engine.state_path)
        with patch.object(os,'unlink',side_effect=AssertionError('no unlink')),patch.object(os,'write',side_effect=AssertionError('no write')):
            self.assertEqual(self.prove()['request'],self.value['request'])
        self.assertEqual(before,self.protected());self.assertEqual(roots,snapshot(self.engine.state_path));self.assertTrue(self.f.data.exists())

    def test_no_changed_policy_or_manufactured_due_timestamp(self):
        for path,bad in (('preparedAtUnixMs',True),('preparedAtUnixMs',self.a['plan']['notBeforeUnixMs']),('lifetimeSeconds',600),('lifetimeSeconds',True)):
            value=copy.deepcopy(self.value);value[path]=bad
            with self.subTest(path=path,bad=bad),self.assertRaises(AssertionError):self.prove(value)
        value=copy.deepcopy(self.value);value['request']['plan']['command']['retainDays']=0
        with self.assertRaises(AssertionError):self.prove(value)

    def test_coherent_protected_truncation_still_needs_independent_current_tree(self):
        for name in ('source','purge','hdd','restore'):
            value=copy.deepcopy(self.value)
            for suffix in ('Before','After'):value['protected'+suffix]['lifecycles'][name]={}
            with self.subTest(name=name),self.assertRaises(AssertionError):self.prove(value)

    def test_foreign_current_payload_or_namespace_rejects_without_repair(self):
        for path in ('hdd','namespace'):
            current=copy.deepcopy(self.current)
            if path=='hdd':current['protected']['lifecycles']['hdd'].pop(self.f.data.name)
            else:current['protected']['namespace']={}
            with self.subTest(path=path),self.assertRaises(AssertionError):self.prove(current=current)

    def test_changed_root_and_marker_are_not_reconciled(self):
        for which in ('journal','marker'):
            current=copy.deepcopy(self.current);current[which]['state']='Unknown'
            with self.subTest(which=which),self.assertRaises(AssertionError):self.prove(current=current)

    def test_extra_foreign_retirement_files_and_missing_owned_files_reject(self):
        for extra in (False,True):
            current=copy.deepcopy(self.current)
            if extra:current['roots']['foreign.json']=copy.deepcopy(next(iter(current['roots'].values())))
            else:current['roots'].pop(self.value['request']['operationId']+'.lock')
            with self.subTest(extra=extra),self.assertRaises(AssertionError):self.prove(current=current)

    def live_records(self):
        observed=self.early['clockAfterUnixMs'];hdd=dict(data=file_snapshot(self.f.data),history=read_at(None,str(self.f.data.with_suffix('.json')))[1])
        live=dict(actualLiveNewInodeReread=True,containerInstanceCurrent=True,actual=self.copied['actual'],hdd=hdd,stageIdentity=self.copied['root']['privateIdentity'],namespace=self.current['protected']['namespace'],liveObservedAtUnixMs=observed,rootCompleted=True,oldBusinessAndProtectionPreserved=True)
        assessment=dict(plan=self.a['plan'],clockBeforeUnixMs=observed,clockAfterUnixMs=observed,before=self.current['protected'],after=self.current['protected'],installed=False,automaticCleanup=False)
        return live,assessment,hdd

    def test_original_live_frames_and_assessment_are_required_and_joined(self):
        live,assessment,hdd=self.live_records();aging_live_evidence_proof(live,assessment,self.value,self.copied,self.current,hdd)
        for key in ('actual','hdd','namespace','stageIdentity'):
            changed=copy.deepcopy(live);changed[key]={}
            with self.subTest(key=key),self.assertRaises(AssertionError):aging_live_evidence_proof(changed,assessment,self.value,self.copied,self.current,hdd)
        changed=copy.deepcopy(assessment);changed['before']={};changed['after']={}
        with self.assertRaises(AssertionError):aging_live_evidence_proof(live,changed,self.value,self.copied,self.current,hdd)

    def test_lost_original_live_or_assessment_file_is_refused(self):
        e=self.engine.state_path.parent;live,assessment,_=self.live_records();pairs=[('live-target-reread.json',live),('retention-assessment-live.json',assessment)]
        for name,value in pairs:(e/name).write_text(json.dumps(value));(e/name).chmod(0o600)
        self.assertEqual(aging_live_records(e),(live,assessment))
        for name,value in pairs:
            (e/name).unlink()
            with self.subTest(missing=name),self.assertRaises(FileNotFoundError):aging_live_records(e)
            (e/name).write_text(json.dumps(value));(e/name).chmod(0o600)

    def test_live_observation_must_precede_c_handoff_without_boolean_clock(self):
        live,assessment,hdd=self.live_records()
        for bad in (True,self.value['preparedAtUnixMs']+1):
            changed={**live,'liveObservedAtUnixMs':bad}
            with self.subTest(clock=bad),self.assertRaises(AssertionError):aging_live_evidence_proof(changed,assessment,self.value,self.copied,self.current,hdd)

    def test_reservation_helper_refuses_other_policy_before_reserve(self):
        with patch.object(self.engine,'reserve',side_effect=AssertionError('must not reserve')) as reserve,self.assertRaises(AssertionError):handoff(self.engine,{**self.a['plan'],'command':{**self.a['plan']['command'],'retainDays':30}},self.session,self.protected)
        reserve.assert_not_called()


class AgingContainerTests(unittest.TestCase):
    def setUp(self):
        self.started=1794186000000;self.prepared=self.started+10000;self.due=self.started+1000+86400000
        self.stamp=datetime.fromtimestamp(self.started/1000,tz=timezone.utc).isoformat().replace('+00:00','Z')
        command=dict(containerId='a'*64,instanceId='123:'+self.stamp)
        ref=dict(plan=dict(command=command));purge=dict(plan=dict(command=dict(source=ref)))
        archive=dict(plan=dict(command=dict(purge=purge)))
        self.q=dict(plan=dict(historyCreatedAtUnixMs=self.started+1000,notBeforeUnixMs=self.due,command=dict(archive=archive)))
        self.container=dict(Id='a'*64,Path='/bin/sleep',Args=['172800'],Config=dict(Entrypoint=['/bin/sleep'],Cmd=['172800'],User='1000:1000',Tty=False,OpenStdin=False),HostConfig=dict(NetworkMode='none',ReadonlyRootfs=True,Privileged=False,AutoRemove=False,CapDrop=['ALL'],CapAdd=None,SecurityOpt=['no-new-privileges'],RestartPolicy=dict(Name='no',MaximumRetryCount=0),Memory=33554432,NanoCpus=250000000,PidsLimit=16),State=dict(Running=True,Paused=False,Restarting=False,OOMKilled=False,Pid=123,StartedAt=self.stamp))

    def prove(self,container=None,now=None):return aging_container_proof(self.container if container is None else container,self.q,self.prepared,self.prepared if now is None else now)

    def test_natural_due_boundary_only_changes_diagnostic_never_executes(self):
        self.assertEqual(self.prove(now=self.due-1),dict(captureAgeElapsed=False,retirementExecuted=False,automaticExecution=False))
        self.assertEqual(self.prove(now=self.due),dict(captureAgeElapsed=True,retirementExecuted=False,automaticExecution=False))

    def test_wrong_instance_restart_and_unbounded_lifetime_are_rejected(self):
        for changes in (dict(Pid=124),dict(StartedAt=self.stamp.replace('00:','01:',1)),dict(Running=False)):
            c=copy.deepcopy(self.container);c['State'].update(changes)
            with self.subTest(changes=changes),self.assertRaises((AssertionError,ValueError)):self.prove(c)
        c=copy.deepcopy(self.container);c['HostConfig']['RestartPolicy']['Name']='always'
        with self.assertRaises(AssertionError):self.prove(c)
        for duration in ('600','infinity','172801'):
            c=copy.deepcopy(self.container);c['Config']['Cmd']=[duration]
            with self.subTest(duration=duration),self.assertRaises(AssertionError):self.prove(c)

    def test_privileges_network_or_resource_budget_cannot_expand(self):
        for field,bad in (('Privileged',True),('NetworkMode','host'),('ReadonlyRootfs',False),('Memory',0),('NanoCpus',0),('PidsLimit',0),('CapAdd',['SYS_ADMIN']),('SecurityOpt',[])):
            c=copy.deepcopy(self.container);c['HostConfig'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):self.prove(c)

    def test_lifetime_exhaustion_and_invalid_clock_fail_closed(self):
        for now in (True,self.prepared-1,self.started+172800000):
            with self.subTest(now=now),self.assertRaises(AssertionError):self.prove(now=now)

    def test_truncated_or_empty_source_manifests_cannot_skip_freeze_checks(self):
        hosts={'required-host.py':'a'*64};validation={'required-validator.py':'b'*64,'required-dependency.py':'c'*64}
        source=dict(sourceSha256=hosts,validationSourceSha256=validation);source_manifest_proof(source,hosts,validation)
        for field in ('sourceSha256','validationSourceSha256'):
            for empty in (False,True):
                changed=copy.deepcopy(source)
                if empty:changed[field]={}
                else:changed[field].pop(next(iter(changed[field])))
                with self.subTest(field=field,empty=empty),self.assertRaises(AssertionError):source_manifest_proof(changed,hosts,validation)

    def test_old_fixture_mode_cannot_silently_become_long_lived(self):
        with patch.object(owned.common,'verify_apps_directory',side_effect=AssertionError('must reject options first')) as check:
            for kwargs in (dict(natural_expiry=1),dict(natural_expiry=True,removable=True),dict(natural_expiry=True,restartable_core=('whaledeck-worker',))):
                with self.subTest(kwargs=kwargs),self.assertRaises(AssertionError):
                    with owned.fixture(**kwargs):pass
            check.assert_not_called()

    def test_observer_has_no_native_execution_or_cleanup_entrypoint(self):
        tree=ast.parse(Path(__file__).with_name('verify_container_recycle_archive_aging.py').read_bytes())
        forbidden={'apply','reserve','discard','recover','unlink','rmdir','mkdir','write_bytes','write_text','Popen'}
        self.assertFalse([n for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute) and n.func.attr in forbidden])
        self.assertNotIn('whaledeck-container-recycle-archive-retirements.py',ast.unparse(tree))


if __name__=='__main__':unittest.main()
