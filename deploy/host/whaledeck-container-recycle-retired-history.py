"""Read-only container HDD history disposition; no retirement executor.

Legacy byte verification remains mandatory for Available. Retired requires a
versioned terminal Root certificate, exact Retired marker and original chain,
plus independently observed absence. Missing/corrupt bytes alone never retire
history. No lock creation, reconciliation, repair, payload write or unlink.
"""
import contextlib
import json
import os
import re
import stat
import time


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


class ContainerRecycleRetiredHistory:
    def __init__(self,retirements):
        self.retirements=retirements;self.source=retirements.source;self.history=self.source.history
        self.copies=retirements.copies;self.preflight=self.copies.preflight

    def reject(self,suffix):raise self.source.r.Rejected('CONTAINER_RECYCLE_RETIRED_HISTORY_'+suffix)

    @contextlib.contextmanager
    def directory(self,engine):
        path=engine.state_path
        if not path.is_absolute() or path!=path.resolve(strict=True):self.reject('STATE_UNSAFE')
        fd=engine.f.trusted_directory(path)
        try:
            i=os.fstat(fd)
            if (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))!=(0,0,0o700):self.reject('STATE_UNSAFE')
            yield fd
        finally:os.close(fd)

    def bound_record(self,engine,request):
        # No journal locks from the HDD level: that would invert the writer's
        # journal -> source -> HDD order. Atomic receipts are re-read below.
        engine.request(request)
        with self.directory(engine) as fd:
            lock=os.open(request['operationId']+'.lock',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=fd)
            try:
                i=os.fstat(lock)
                if not stat.S_ISREG(i.st_mode) or (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink,i.st_size)!=(0,0,0o600,1,0):self.reject('STATE_UNSAFE')
            finally:os.close(lock)
            record=engine.read(fd,request['operationId'])
            if record is None or record['owner']!=request['owner'] or encode(record['plan'])!=encode(request['plan']):self.reject('SOURCE_CHANGED')
            return record

    def reservation_snapshot(self,ref):
        records=[]
        with self.directory(self.retirements) as fd:
            names=os.listdir(fd)
            if len(names)>20000:self.reject('RECEIPT_LIMIT')
            for name in sorted(names):
                if not name.endswith('.json'):continue
                if not re.fullmatch(r'[a-f0-9]{32}\.json',name):self.reject('STATE_UNSAFE')
                record=self.retirements.read(fd,name[:-5])
                if record is None:self.reject('OBSERVATION_CHANGED')
                if self.retirements.ref(record)['operationId']==ref['operationId']:
                    if encode(self.retirements.ref(record))!=encode(ref):self.reject('SOURCE_CHANGED')
                    request={k:record[k] for k in self.retirements.REQUEST}
                    if encode(self.bound_record(self.retirements,request))!=encode(record):self.reject('OBSERVATION_CHANGED')
                    records.append(record)
        marker=self.retirements.read_marker(ref['operationId'])
        if marker is not None and encode(self.copies.ref(marker['archive']))!=encode(ref):self.reject('SOURCE_CHANGED')
        return dict(records=records,marker=marker)

    def original_chain(self,record):
        archive=self.retirements.archive(record);ref=self.copies.ref(archive);purge=archive['plan']['command']['purge']
        return dict(copy=self.bound_record(self.copies,archive),purge=self.bound_record(self.preflight.purges,purge),source=self.bound_record(self.source,ref),purgeMarker=self.source.read_purge_fence(ref),archiveClaim=self.source.read_restore_claim(ref))

    def validate_chain(self,record,chain,metadata,configuration,directory):
        archive=self.retirements.archive(record);p=record['plan'];ap=archive['plan'];purge=ap['command']['purge'];digest=self.preflight.record_digest
        if chain['copy']['state']!='Completed' or chain['purge']['state']!='Completed' or not chain['purge']['unlinkConfirmed'] or chain['source']['state']!='Purged' or chain['source']['actualIdentity'] is not None or metadata['captureState']!='Purged':self.reject('SOURCE_CHANGED')
        if not self.preflight.purges.matches(chain['purgeMarker'],purge) or chain['purgeMarker']['state']!='Purged' or not self.copies.matches(chain['archiveClaim'],archive):self.reject('FENCE_CHANGED')
        for key,domain,value in (('archiveRecordDigest',b'archive-retention-copy-v1\n',chain['copy']),('sourceRecordDigest',b'archive-source-v1\n',chain['source']),('purgeRecordDigest',b'archive-purge-v1\n',chain['purge']),('purgeMarkerDigest',b'archive-marker-v1\n',chain['purgeMarker']),('claimDigest',b'archive-retention-claim-v1\n',chain['archiveClaim']),('historyMetadataDigest',b'archive-history-v1\n',metadata)):
            if p[key]!=digest(domain,value):self.reject('SOURCE_CHANGED')
        if chain['source']['historyDigest']!=ap['command']['historyDigest'] or metadata['historyDigest']!=ap['command']['historyDigest'] or metadata['createdAtUnixMs']!=p['historyCreatedAtUnixMs'] or metadata['createdAtUnixMs']!=purge['plan']['historyCreatedAtUnixMs'] or metadata['sha256']!=p['archiveSha256'] or metadata['sizeBytes']!=p['archiveBytes'] or chain['copy']['publishedIdentity']!=p['publishedIdentity']:self.reject('SOURCE_CHANGED')
        if [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=p['storeIdentity'] or digest(b'',configuration)!=p['storageHash'] or digest(b'',self.history.storage.configuration())!=p['storageHash']:self.reject('HISTORY_CHANGED')

    @staticmethod
    def data_frame(directory,name):
        try:i=os.stat(name,dir_fd=directory,follow_symlinks=False)
        except FileNotFoundError:return None
        return [i.st_dev,i.st_ino,i.st_mode,i.st_uid,i.st_gid,i.st_nlink,i.st_size,i.st_mtime_ns,i.st_ctime_ns]

    def observe(self,ref,history_digest):
        self.source.request(ref)
        if type(history_digest) is not str or not re.fullmatch('[A-F0-9]{64}',history_digest):self.reject('REQUEST_INVALID')
        with self.history.target(False) as (configuration,directory),self.preflight.existing_lock(directory,'history.lock'):
            metadata=self.history.read_metadata(directory,ref)
            if metadata['historyDigest']!=history_digest:self.reject('HISTORY_CHANGED')
            snapshot=self.reservation_snapshot(ref);marker=snapshot['marker'];records=snapshot['records'];source_id=ref['operationId'];name=source_id+'.data'
            data=self.data_frame(directory,name);partial=self.data_frame(directory,source_id+'.partial');chain=None;terminal=None
            versioned=[r for r in records if r['schemaVersion']==2]
            active=[r for r in records if r['state'] in ('Preparing','Reserved','Unknown')]
            disposition='Unavailable';reason='RetirementUncertain';retirement_id=None if marker is None else marker['retirementOperationId']
            if versioned or marker is not None and marker['state']=='Retired':
                eligible=[r for r in versioned if r['state']=='Completed' and r['completion'] is not None]
                if len(versioned)!=1 or len(eligible)!=1 or marker is None or marker['state']!='Retired' or not self.retirements.matches(marker,eligible[0]) or active:
                    reason='RetirementProofIncomplete'
                else:
                    terminal=eligible[0];chain=self.original_chain(terminal);self.validate_chain(terminal,chain,metadata,configuration,directory)
                    now=time.time_ns()//1000000
                    if not 0<=now<=self.retirements.preflight.MAX_TIME_MS or now<terminal['completion']['completedAtUnixMs']:reason='CompletionInFuture'
                    elif data is not None or partial is not None:reason='RetirementDataConflict'
                    else:disposition='Retired';reason='CommittedRetirement'
            else:
                matching=[r for r in active if marker is not None and self.retirements.matches(marker,r)]
                uncertain=any(r['state']!='Reserved' for r in active) or len(active)>1
                if marker is None:uncertain=uncertain or bool(active)
                elif marker['state']=='Reserved':uncertain=uncertain or len(matching)!=1 or matching[0]['state']!='Reserved'
                elif marker['state']=='Unknown':uncertain=True
                elif marker['state']=='Released':uncertain=uncertain or bool(active)
                if uncertain:reason='RetirementUncertain'
                elif partial is not None:reason='PartialDataConflict'
                elif data is None:reason='MissingData'
                else:
                    try:self.history.storage.verify_data(directory,source_id,self.history.expected(ref))
                    except (OSError,self.source.r.Rejected,self.source.f.Rejected):reason='InvalidData'
                    else:disposition='Available';reason='VerifiedData'
            if self.data_frame(directory,name)!=data or self.data_frame(directory,source_id+'.partial')!=partial or encode(self.reservation_snapshot(ref))!=encode(snapshot) or encode(self.history.read_metadata(directory,ref))!=encode(metadata):self.reject('OBSERVATION_CHANGED')
            if chain is not None and encode(self.original_chain(terminal))!=encode(chain):self.reject('OBSERVATION_CHANGED')
            if encode(self.history.storage.configuration())!=encode(configuration):self.reject('HISTORY_CHANGED')
            self.history.storage.attest(configuration,directory,read_only=True)
            return dict(operationId=source_id,historyDigest=history_digest,disposition=disposition,reason=reason,dataAvailable=disposition=='Available',dataPresent=data is not None,retirementOperationId=retirement_id)
