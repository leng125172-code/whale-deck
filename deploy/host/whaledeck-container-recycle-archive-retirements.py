"""Internal explicit HDD data retirement, not installed or exposed by RPC.

The authenticated permanent workflow must authorize/fence the operation before
calling Apply. A reservation is not deletion authority: current expiry, original
chain, live published copy and exact root-private HDD bytes are checked again.
Only the selected .data is unlinked. Metadata/claims/source/purge stay immutable.
Interrupted execution is observation-only Unknown, never resumed or cleaned up.
"""
import contextlib
import hashlib
import json
import os
import stat
import time


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def archive_retirements_type(fence_type):
    class ContainerRecycleArchiveRetirements(fence_type):
        @staticmethod
        def stable(plan):
            return {k:v for k,v in plan.items() if k not in ('fingerprint','retentionElapsed','retirementEligible')}

        def now(self,plan):
            now=time.time_ns()//1000000
            if not plan['notBeforeUnixMs']<=now<=self.preflight.MAX_TIME_MS:self.reject('NOT_DUE')
            return now

        def checksum(self,fd,plan,session):
            checksum=hashlib.sha256();count=0;os.lseek(fd,0,os.SEEK_SET)
            while chunk:=os.read(fd,self.f.CHUNK):
                session.revalidate();count+=len(chunk);checksum.update(chunk)
                if count>plan['archiveBytes']:self.reject('HISTORY_CHANGED')
            if count!=plan['archiveBytes'] or checksum.hexdigest().upper()!=plan['archiveSha256']:self.reject('HISTORY_CHANGED')
            # HDD captures store plain bytes, not original container attributes.
            if self.source.attributes(fd):self.reject('HISTORY_CHANGED')

        def apply(self,request,session):
            with self.locked(request) as (state,record),self.records(self.archive(request)) as records:
                ref=self.ref(request);source=self.source;history=source.history
                marker=self.read_marker(ref['operationId'])
                if record is None:
                    if not self.matches(marker,request):self.reject('NOT_RESERVED')
                    record=self.orphan_record(request,marker);self.save(state,record)
                self.reconcile(state,record,marker)
                if record['state']!='Reserved':return self.public(state,record,self.read_marker(ref['operationId']))
                # The only permitted change since an early reservation is the
                # expiry diagnostic. No caller-supplied clock or rehashed facts.
                fresh=self.preflight.plan_locked(request['plan']['command'],session,*records)
                if encode(self.stable(fresh))!=encode(self.stable(request['plan'])):self.reject('PLAN_CHANGED')
                if not fresh['retirementEligible']:self.reject('NOT_ELIGIBLE')
                copied=records[0];archive=self.archive(request);ap=archive['plan']
                name=ref['operationId']+'.data';partial=ref['operationId']+'.partial'
                try:
                    with history.target(False) as (configuration,directory),self.copies.preflight.existing_lock(directory,'history.lock'),source.parent(ref['plan'],session) as parent:
                        fd=self.f.open_at(directory,name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
                        live=None;unlinked_identity=None
                        try:
                            leaf=ref['plan']['command']['path'].rsplit('/',1)[-1]
                            live=self.f.open_at(parent,leaf,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
                            def guard(removed=False,terminal=False):
                                source.guard(ref['plan']['command'],session,ap['targetProof']);self.now(fresh)
                                history.storage.attest(configuration,directory)
                                if self.copies.preflight.record_digest(b'',configuration)!=fresh['storageHash'] or self.copies.preflight.record_digest(b'',history.storage.configuration())!=fresh['storageHash'] or [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=fresh['storeIdentity']:self.reject('HISTORY_CHANGED')
                                # Re-resolve protected namespaces, not just old FDs.
                                with history.target(False) as (_,current):
                                    if not os.path.samestat(os.fstat(current),os.fstat(directory)):self.reject('HISTORY_CHANGED')
                                metadata=history.read_metadata(directory,ref)
                                if metadata['captureState']!='Purged' or self.copies.preflight.record_digest(b'archive-history-v1\n',metadata)!=fresh['historyMetadataDigest']:self.reject('HISTORY_CHANGED')
                                claim=source.read_restore_claim(ref)
                                if not self.copies.matches(claim,archive) or self.copies.preflight.record_digest(b'archive-retention-claim-v1\n',claim)!=fresh['claimDigest']:self.reject('FENCE_CHANGED')
                                current_marker=self.read_marker(ref['operationId'])
                                expected='Retired' if terminal else 'Retiring' if record['schemaVersion']==2 else 'Reserved'
                                if not self.matches(current_marker,request) or current_marker['state']!=expected or self.active(state,ref['operationId'])!=[request['operationId']]:self.reject('FENCE_CHANGED')
                                self.copies.check_file(live,archive,session,copied['publishedIdentity'])
                                with source.parent(ref['plan'],session) as current:
                                    if not os.path.samestat(os.fstat(current),os.fstat(parent)) or source.metadata(os.stat(leaf,dir_fd=current,follow_symlinks=False))!=copied['publishedIdentity']:self.reject('COPY_CHANGED')
                                if source.present(directory,partial):self.reject('HISTORY_CHANGED')
                                actual=source.metadata(os.fstat(fd));identity=fresh['archiveIdentity']
                                if removed:
                                    if source.present(directory,name) or actual!=unlinked_identity or actual[:5]!=identity[:5] or actual[5]!=0 or actual[6:8]!=identity[6:8] or actual[8]<identity[8]:self.reject('HISTORY_CHANGED')
                                elif actual!=identity or source.metadata(os.stat(name,dir_fd=directory,follow_symlinks=False))!=identity or not stat.S_ISREG(actual[2]) or (stat.S_IMODE(actual[2]),*actual[3:6])!=(0o600,0,0,1):self.reject('HISTORY_CHANGED')
                            guard();self.checksum(fd,fresh,session);guard()
                            record.update(schemaVersion=2,state='Removing',completion=None);self.save(state,record);self.checkpoint('Removing')
                            self.write_marker(request,'Retiring');self.checkpoint('RetiringFenced')
                            guard();self.checksum(fd,fresh,session);guard()
                            # The directory is root-private; all cooperating
                            # writers hold history.lock. This is not a generic
                            # stat-then-unlink primitive for writable parents.
                            os.unlink(name,dir_fd=directory);self.checkpoint('UnlinkedNamespace')
                            os.fsync(directory);unlinked_identity=source.metadata(os.fstat(fd));self.checkpoint('Unlinked')
                            guard(True);self.checksum(fd,fresh,session);guard(True)
                            proof=dict(executionPlan=fresh,unlinkConfirmed=True,unlinkedIdentity=list(fresh['archiveIdentity']),completedAtUnixMs=self.now(fresh))
                            proof['proofDigest']=self.completion_digest(proof)
                            record.update(state='Removed',completion=proof);self.save(state,record);self.checkpoint('Removed')
                            guard(True)
                            self.write_marker(request,'Retired');self.checkpoint('RetiredFenced')
                            guard(True,True)
                            # No success is reconstructed from Removed/Retired
                            # on restart, even when bytes are already absent.
                            record['state']='Completed';self.save(state,record);self.checkpoint('Completed')
                        finally:
                            if live is not None:os.close(live)
                            os.close(fd)
                    return self.public(state,record,self.read_marker(ref['operationId']))
                except BaseException:
                    if record['schemaVersion']==2:
                        # Even an error after the Completed rename (e.g. its
                        # directory fsync) is uncertain, not durable success.
                        # Poison each owned proof independently, best effort.
                        with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):
                            marker=self.read_marker(ref['operationId'])
                            if self.matches(marker,request) and marker['state'] in ('Retiring','Retired'):self.write_marker(request,'Unknown')
                        record['state']='Unknown'
                        with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.save(state,record)
                    else:
                        with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.reconcile(state,record,self.read_marker(ref['operationId']))
                    raise

        def recover(self,request,session=None):return self.inspect(request,session)

    return ContainerRecycleArchiveRetirements
