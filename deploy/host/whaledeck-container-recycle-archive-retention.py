"""Internal read-only HDD retention assessment after completed archive copy.

retainDays is an explicit minimum age of the original HDD capture, not time
since copy completion. No default policy, scheduler, installation, deletion,
claim release, reconciliation or stage cleanup. Eligibility is diagnostic;
any future mutation requires its own authorization, fencing and crash journal.
"""
import hashlib
import json
import os
import re
import time


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


class ContainerRecycleArchiveRetentionPreflight:
    DAY_MS=86400000
    MAX_TIME_MS=253402300799999
    COMMAND={'archive','retainDays','mode'}
    PLAN={'schemaVersion','command','archiveRecordDigest','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','claimDigest','historyMetadataDigest','historyCreatedAtUnixMs','notBeforeUnixMs','retentionElapsed','bindingCurrent','retirementEligible','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','publishedIdentity','fingerprint'}

    def __init__(self,copies):
        self.copies=copies;self.preflight=copies.preflight;self.source=copies.source

    def reject(self,suffix):raise self.source.r.Rejected('CONTAINER_RECYCLE_ARCHIVE_RETENTION_'+suffix)

    @staticmethod
    def digest(value):
        return hashlib.sha256(b'whaledeck-container-recycle-archive-retention-plan-v1\n'+encode({k:v for k,v in value.items() if k!='fingerprint'})).hexdigest().upper()

    def command(self,value):
        if type(value) is not dict or set(value)!=self.COMMAND or value['mode']!='ArchiveData' or type(value['retainDays']) is not int or not 1<=value['retainDays']<=3650:self.reject('REQUEST_INVALID')
        self.copies.request(value['archive'])
        self.preflight.command(value['archive']['plan']['command'])

    def plan(self,command,session):
        self.command(command)
        if session is None:self.reject('SESSION_REQUIRED')
        q=command['archive'];p=q['plan'];ref=self.copies.ref(q);source=self.source;digest=self.preflight.record_digest
        source.guard(ref['plan']['command'],session,p['targetProof'])
        # Same archive -> purge -> original source -> HDD lock order as Apply.
        # Existing readers never create a missing lock or repair a claim.
        with self.preflight.existing_record(self.copies,q) as copied,self.copies.source_records(q) as (deleted,original):
            if copied['state']!='Completed':self.reject('COPY_UNCONFIRMED')
            purge=p['command']['purge'];marker=source.read_purge_fence(ref)
            if deleted['state']!='Completed' or not deleted['unlinkConfirmed'] or not self.preflight.purges.matches(marker,purge) or marker['state']!='Purged':self.reject('PURGE_UNCONFIRMED')
            if original['state']!='Purged' or original['actualIdentity'] is not None or not original['historyVerified'] or original['historyDigest']!=p['command']['historyDigest']:self.reject('SOURCE_CHANGED')
            facts=dict(sourceRecordDigest=digest(b'archive-source-v1\n',original),purgeRecordDigest=digest(b'archive-purge-v1\n',deleted),purgeMarkerDigest=digest(b'archive-marker-v1\n',marker))
            if any(facts[k]!=p[k] for k in facts):self.reject('SOURCE_CHANGED')
            claim=source.read_restore_claim(ref)
            if not self.copies.matches(claim,q):self.reject('FENCE_CHANGED')
            binding=self.copies.binding(copied,session)
            with source.history.target(False) as (configuration,directory),self.preflight.existing_lock(directory,'history.lock'):
                history=source.history.read(directory,ref)
                if history['captureState']!='Purged' or digest(b'archive-history-v1\n',history)!=p['historyMetadataDigest'] or history['createdAtUnixMs']!=purge['plan']['historyCreatedAtUnixMs']:self.reject('HISTORY_CHANGED')
                def attest():
                    session.revalidate();source.history.storage.attest(configuration,directory,read_only=True)
                    if digest(b'',configuration)!=p['storageHash'] or digest(b'',source.history.storage.configuration())!=p['storageHash'] or [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=p['storeIdentity']:self.reject('HISTORY_CHANGED')
                    if source.metadata(os.stat(ref['operationId']+'.data',dir_fd=directory,follow_symlinks=False))!=p['archiveIdentity']:self.reject('HISTORY_CHANGED')
                attest()
                created=history['createdAtUnixMs'];due=created+command['retainDays']*self.DAY_MS;now=time.time_ns()//1000000
                if not 0<=now<=self.MAX_TIME_MS or due>self.MAX_TIME_MS:self.reject('TIME_RANGE')
                if now<created:self.reject('CLOCK_BEFORE_CAPTURE')
                elapsed=now>=due
                # A historical Completed is retained even when the live copy
                # vanished. Such a source is never eligible for retirement.
                source.guard(ref['plan']['command'],session,p['targetProof'])
                if self.copies.binding(copied,session)!=binding:self.reject('COPY_CHANGED')
                if encode(source.read_restore_claim(ref))!=encode(claim):self.reject('FENCE_CHANGED')
                attest()
                result=dict(schemaVersion=1,command=json.loads(encode(command)),**facts,archiveRecordDigest=digest(b'archive-retention-copy-v1\n',copied),claimDigest=digest(b'archive-retention-claim-v1\n',claim),historyMetadataDigest=p['historyMetadataDigest'],historyCreatedAtUnixMs=created,notBeforeUnixMs=due,retentionElapsed=elapsed,bindingCurrent=binding,retirementEligible=elapsed and binding,storageHash=p['storageHash'],storeIdentity=list(p['storeIdentity']),archiveIdentity=list(p['archiveIdentity']),archiveBytes=p['archiveBytes'],archiveSha256=p['archiveSha256'],publishedIdentity=list(copied['publishedIdentity']))
                result['fingerprint']=self.digest(result);return result

    def validate_plan(self,value):
        if type(value) is not dict or set(value)!=self.PLAN or type(value['schemaVersion']) is not int or value['schemaVersion']!=1 or type(value['fingerprint']) is not str or not re.fullmatch('[A-F0-9]{64}',value['fingerprint']) or value['fingerprint']!=self.digest(value):self.reject('PLAN_INVALID')
        self.command(value['command'])
        for key in ('archiveRecordDigest','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','claimDigest','historyMetadataDigest','storageHash','archiveSha256'):
            if type(value[key]) is not str or not re.fullmatch('[A-F0-9]{64}',value[key]):self.reject('PLAN_INVALID')
        for key in ('retentionElapsed','bindingCurrent','retirementEligible'):
            if type(value[key]) is not bool:self.reject('PLAN_INVALID')
        if value['retirementEligible']!=(value['retentionElapsed'] and value['bindingCurrent']):self.reject('PLAN_INVALID')
        if any(type(value[k]) is not int for k in ('historyCreatedAtUnixMs','notBeforeUnixMs','archiveBytes')) or not 0<=value['historyCreatedAtUnixMs']<=value['notBeforeUnixMs']<=self.MAX_TIME_MS or value['notBeforeUnixMs']!=value['historyCreatedAtUnixMs']+value['command']['retainDays']*self.DAY_MS or not 0<=value['archiveBytes']<2**63:self.reject('PLAN_INVALID')
        for key,length in (('storeIdentity',2),('archiveIdentity',9),('publishedIdentity',9)):
            if type(value[key]) is not list or len(value[key])!=length or any(type(n) is not int for n in value[key]) or not 0<=value[key][0]<2**64 or not 0<value[key][1]<2**64:self.reject('PLAN_INVALID')
        p=value['command']['archive']['plan']
        if any(encode(value[k])!=encode(p[k]) for k in ('historyMetadataDigest','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256')):self.reject('PLAN_INVALID')
        # This unkeyed structural check is never authorization; consumers must
        # use revalidate() to compare the current trusted records and clock.

    def revalidate(self,value,session):
        self.validate_plan(value);current=self.plan(value['command'],session)
        # Canonical bytes preserve int/bool/float distinctions and invalidate
        # a young plan at its due boundary or when live-copy binding changes.
        if encode(current)!=encode(value):self.reject('PLAN_CHANGED')
        return current
