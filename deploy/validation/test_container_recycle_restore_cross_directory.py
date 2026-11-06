"""Shared-bind aliases must preserve independent identities and strict budgets."""
import copy,unittest
from pathlib import Path
import owned_container_alias as alias
import test_container_recycle_instance as prior


class AliasTests(unittest.TestCase):
    def frames(self):
        f,t=prior.RecycleInstanceTests().fixture();primary=t['before'];other=copy.deepcopy(primary);other['Id']='c'*64;name='wdv-container-publish-'+'d'*32;other['Name']='/'+name;other['Config']['Labels']['io.whaledeck.validation']=name;other['State']['Pid']=300
        return f,primary,other

    def test_two_distinct_ids_really_share_the_same_fixed_host_bind(self):
        f,a,b=self.frames();alias.require_alias(a,b,Path(f['folder']))

    def test_foreign_directory_same_id_or_privileged_alias_is_rejected(self):
        f,a,b=self.frames()
        for change in [lambda v:v.update(Id=a['Id']),lambda v:v.update(Name=a['Name']),lambda v:v['Mounts'][0].update(Source='/etc'),lambda v:v['HostConfig'].update(Privileged=True),lambda v:v['HostConfig'].update(NetworkMode='host'),lambda v:v['Config'].update(User='0')]:
            v=copy.deepcopy(b);change(v)
            with self.assertRaises(AssertionError):alias.require_alias(a,v,Path(f['folder']))




class DeferralTests(unittest.TestCase):
    def samples(self):
        import datetime as dt
        import hashlib
        import test_container_recycle_cross_directory as old
        import restore_cross_directory_proof as proof
        fixture=old.RecycleCrossDirectoryTests();raw=b'owned-restore-unknown-root';start=dt.datetime(2026,11,6,10,tzinfo=dt.timezone.utc);samples=[]
        for i in range(4):
            at=start+dt.timedelta(seconds=i*6);data=fixture.directory(at,2+i*2);data['row']['relative_path']=proof.WAITING_PATH
            samples.append(dict(observedAtUtc=at.isoformat(),sql=data,rootAbsent=True,targetAbsent=True,restoreUnknownProtected=True,originalPhysicalUnchanged=True,unknownRootSha256=hashlib.sha256(raw).hexdigest()))
        b=fixture.baseline();b['path']=proof.WAITING_PATH
        return proof,samples,b,dict(subject='recovery'),samples[0]['sql'],start+dt.timedelta(seconds=30),raw

    def test_four_natural_versions_and_due_times_are_required(self):
        proof,s,b,r,i,t,raw=self.samples();proof.require_deferrals(s,b,r,i,t,raw)
        for key,bad in [('rootAbsent',False),('targetAbsent',False),('restoreUnknownProtected',False),('unknownRootSha256','changed')]:
            values=copy.deepcopy(s);values[1][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_deferrals(values,b,r,i,t,raw)
        values=copy.deepcopy(s);values[1]['sql']['row']['version']=values[0]['sql']['row']['version']
        with self.assertRaises(AssertionError):proof.require_deferrals(values,b,r,i,t,raw)

    def test_started_or_terminal_directory_cannot_be_a_deferral(self):
        proof,s,b,r,i,t,raw=self.samples()
        for key,bad in [('root_started_at_utc','2026-11-06T10:00:00Z'),('state','Succeeded'),('publish_barrier',True)]:
            values=copy.deepcopy(s);values[1]['sql']['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_deferrals(values,b,r,i,t,raw)

    def test_parent_barrier_and_original_execution_cannot_change_during_wait(self):
        import restore_cross_directory_proof as proof
        from test_container_recycle_restore_unknown import case
        v,data,f,source=case();row=data['row']
        # Immutable request fields are explicitly compared by the shared restore contract.
        from container_recycle_restore_restart_proof import IMMUTABLE
        for key in IMMUTABLE:row.setdefault(key,key)
        original=dict(sql=data);proof.protected_original(original,copy.deepcopy(data))
        for key,bad in [('publish_barrier',False),('state','Acknowledged'),('root_state','Completed'),('restored_by_this_operation',True),('execution_job_id','foreign')]:
            current=copy.deepcopy(data);current['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.protected_original(original,current)


if __name__=='__main__':unittest.main()
