"""Internal reserved HDD archive copy to a fresh inode, NOREPLACE publication.

No CLI/RPC/installation. Interrupted Apply never resumes copy, rename or
cleanup. Old purge/source/HDD lifecycle is immutable; the independent journal
alone records this new copy's historical result and current binding.
"""
import base64
import contextlib
import hashlib
import os
import re
import stat


def archive_copies_type(fence_type):
    class ContainerRecycleArchiveCopies(fence_type):
        RECORD=fence_type.RECORD|{'privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity','parentAfterStage'}
        STATES=(*fence_type.STATES,'Applying','Copying','Prepared','Publishing','Published','Completed')

        def new_record(self,request,state):
            return dict(**super().new_record(request,state),privateIdentity=None,pendingIdentity=None,preparedIdentity=None,publishedIdentity=None,parentAfterStage='')

        def validate_record(self,value,operation):
            if type(value) is not dict or set(value)!=self.RECORD or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation or value['state'] not in self.STATES:self.reject('RECEIPT_INVALID')
            for key in ('privateIdentity','pendingIdentity'):
                i=value[key]
                if i is not None and (type(i) is not list or len(i)!=2 or any(type(n) is not int or not 0<=n<=2**64-1 for n in i) or i[1]==0):self.reject('RECEIPT_INVALID')
            original=self.ref(value)['plan']['sourceIdentity']
            private,pending=value['privateIdentity'],value['pendingIdentity']
            if private is not None and (private[0]!=value['plan']['parentIdentity'][0] or not value['parentAfterStage']):self.reject('RECEIPT_INVALID')
            if pending is not None and (private is None or pending[0]!=private[0] or pending in (original[:2],value['plan']['archiveIdentity'][:2])):self.reject('RECEIPT_INVALID')
            for key in ('preparedIdentity','publishedIdentity'):
                i=value[key]
                if i is not None and (type(i) is not list or len(i)!=9 or any(type(n) is not int for n in i) or i[:2]!=value['pendingIdentity'] or i[:2]==original[:2] or i[2:5]!=original[2:5] or i[5]!=1 or i[6:8]!=original[6:8] or not -(2**63)<=i[8]<2**63):self.reject('RECEIPT_INVALID')
            prepared,published=value['preparedIdentity'],value['publishedIdentity']
            if published is not None and (prepared is None or published[:8]!=prepared[:8] or published[8]<prepared[8]):self.reject('RECEIPT_INVALID')
            if type(value['parentAfterStage']) is not str or value['parentAfterStage'] and not re.fullmatch('[A-F0-9]{64}',value['parentAfterStage']):self.reject('RECEIPT_INVALID')
            if value['state'] in ('Preparing','Reserved','Aborted') and any(value[k] is not None for k in ('privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity')):self.reject('RECEIPT_INVALID')
            if value['state'] in ('Preparing','Reserved','Aborted') and value['parentAfterStage']:self.reject('RECEIPT_INVALID')
            if value['state'] in ('Copying','Prepared','Publishing','Published','Completed') and (value['privateIdentity'] is None or value['pendingIdentity'] is None or not value['parentAfterStage']):self.reject('RECEIPT_INVALID')
            if value['state'] in ('Prepared','Publishing','Published','Completed') and prepared is None:self.reject('RECEIPT_INVALID')
            if value['state'] in ('Published','Completed') and published is None:self.reject('RECEIPT_INVALID')

        @staticmethod
        def stage_name(record):return '.whaledeck-container-archive-'+record['operationId']

        def entries(self,parent,exclude=None):
            names=os.listdir(parent)
            if len(names)>4096:self.reject('DIRECTORY_LIMIT')
            return {n:self.source.metadata(os.stat(n,dir_fd=parent,follow_symlinks=False)) for n in sorted(names) if n!=exclude}

        def parent_version(self,parent,ref):
            c=ref['plan']['command'];return self.r.version(c,c['path'].rpartition('/')[0],os.fstat(parent))

        def reconcile(self,state,record):
            if record['state'] in ('Preparing','Reserved','Aborted'):return super().reconcile(state,record)
            # Historical Completed remains proved even if its present binding
            # is later lost. Other interrupted write phases stay Unknown.
            if record['state'] not in ('Unknown','Completed'):record['state']='Unknown';self.save(state,record)
            if self.source.read_restore_claim(self.ref(record)) is None:self.write_claim(record)

        @contextlib.contextmanager
        def archive(self,request,session):
            source=self.source;plan=request['plan'];ref=self.ref(request)
            with source.history.target(False) as (configuration,directory),self.preflight.existing_lock(directory,'history.lock'):
                history=source.history.read(directory,ref)
                if self.preflight.record_digest(b'archive-history-v1\n',history)!=plan['historyMetadataDigest']:self.reject('HISTORY_CHANGED')
                fd=self.f.open_at(directory,ref['operationId']+'.data',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
                try:
                    def attest():
                        session.revalidate();source.history.storage.attest(configuration,directory,read_only=True)
                        if self.preflight.record_digest(b'',source.history.storage.configuration())!=plan['storageHash']:self.reject('HISTORY_CHANGED')
                        if self.preflight.record_digest(b'',configuration)!=plan['storageHash'] or [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=plan['storeIdentity']:self.reject('HISTORY_CHANGED')
                        if source.metadata(os.fstat(fd))!=plan['archiveIdentity'] or source.metadata(os.stat(ref['operationId']+'.data',dir_fd=directory,follow_symlinks=False))!=plan['archiveIdentity']:self.reject('HISTORY_CHANGED')
                    attest();yield fd,attest;attest()
                finally:os.close(fd)

        def check_file(self,fd,request,session,identity=None):
            original=self.ref(request)['plan'];actual,attrs,digest=self.source.fingerprint(fd,session)
            if actual[:2]==original['sourceIdentity'][:2] or actual[:2]==request['plan']['archiveIdentity'][:2] or actual[2:5]!=original['sourceIdentity'][2:5] or actual[6:8]!=original['sourceIdentity'][6:8] or attrs!=original['attributes'] or digest!=original['sha256']:self.reject('COPY_CHANGED')
            if identity is not None and actual!=identity:self.reject('COPY_CHANGED')
            return actual

        def check_stage(self,parent,private,output,record,baseline,session):
            ref=self.ref(record);source=self.source;name=self.stage_name(record)
            source.guard(ref['plan']['command'],session,record['plan']['targetProof']);self.preflight.check_claim(ref,self.claim(record))
            with source.parent(ref['plan'],session) as current:
                if not os.path.samestat(os.fstat(current),os.fstat(parent)):self.reject('PARENT_CHANGED')
            info=os.fstat(private)
            if [info.st_dev,info.st_ino]!=record['privateIdentity'] or (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))!=(0,0,0o700) or not os.path.samestat(info,os.stat(name,dir_fd=parent,follow_symlinks=False)):self.reject('PRIVATE_CHANGED')
            if self.parent_version(parent,ref)!=record['parentAfterStage'] or self.entries(parent,name)!=baseline:self.reject('PARENT_CHANGED')
            if source.present(parent,ref['plan']['command']['path'].rsplit('/',1)[-1]):self.reject('TARGET_EXISTS')
            if [os.fstat(output).st_dev,os.fstat(output).st_ino]!=record['pendingIdentity'] or not os.path.samestat(os.fstat(output),os.stat('copy.partial',dir_fd=private,follow_symlinks=False)) or os.listdir(private)!=['copy.partial']:self.reject('COPY_CHANGED')
            info=os.fstat(output)
            if info.st_nlink!=1 or record['state']=='Copying' and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))!=(0,0,0o600):self.reject('COPY_CHANGED')

        def apply(self,request,session):
            with self.locked(request) as (state,record),self.source_records(request) as (deleted,original):
                if record is None:self.reject('NOT_RESERVED')
                self.reconcile(state,record)
                if record['state']!='Reserved':return self.public(record,session)
                self.fresh(request,session,deleted,original,True)
                ref=self.ref(request);source=self.source;plan=ref['plan'];name=self.stage_name(record);leaf=plan['command']['path'].rsplit('/',1)[-1]
                try:
                    with self.archive(request,session) as (archive,attest),source.parent(plan,session) as parent:
                        if self.parent_version(parent,ref)!=request['plan']['parentVersion'] or source.present(parent,leaf):self.reject('PLAN_CHANGED')
                        capacity=os.fstatvfs(parent)
                        if capacity.f_bavail*capacity.f_frsize<request['plan']['archiveBytes']+16*1024**2:self.reject('COPY_CAPACITY')
                        before=self.entries(parent);parent_before=source.metadata(os.fstat(parent))
                        if source.present(parent,name):self.reject('PRIVATE_EXISTS')
                        record['state']='Applying';self.save(state,record);self.checkpoint('Applying')
                        os.mkdir(name,0o700,dir_fd=parent);os.fsync(parent);self.checkpoint('PrivateCreated')
                        private=self.f.open_at(parent,name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW);output=None
                        try:
                            os.fchown(private,0,0);os.fchmod(private,0o700);os.fsync(private)
                            info=os.fstat(private);after=source.metadata(os.fstat(parent))
                            if self.entries(parent,name)!=before or after[:5]!=parent_before[:5] or after[5]!=parent_before[5]+1:self.reject('PARENT_CHANGED')
                            record.update(privateIdentity=[info.st_dev,info.st_ino],parentAfterStage=self.parent_version(parent,ref));self.save(state,record);self.checkpoint('PrivateBound')
                            output=os.open('copy.partial',os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,0o600,dir_fd=private)
                            info=os.fstat(output)
                            if not stat.S_ISREG(info.st_mode) or info.st_nlink!=1 or info.st_uid!=0 or stat.S_IMODE(info.st_mode)!=0o600 or [info.st_dev,info.st_ino] in (plan['sourceIdentity'][:2],request['plan']['archiveIdentity'][:2]):self.reject('COPY_CHANGED')
                            record.update(state='Copying',pendingIdentity=[info.st_dev,info.st_ino]);self.save(state,record);self.checkpoint('Copying')
                            self.check_stage(parent,private,output,record,before,session);checksum=hashlib.sha256();count=0;os.lseek(archive,0,os.SEEK_SET)
                            while chunk:=os.read(archive,self.f.CHUNK):
                                attest();self.check_stage(parent,private,output,record,before,session);count+=len(chunk);checksum.update(chunk)
                                if count>request['plan']['archiveBytes']:self.reject('HISTORY_CHANGED')
                                pending=memoryview(chunk)
                                while pending:
                                    written=os.write(output,pending)
                                    if written<=0:self.reject('COPY_WRITE_FAILED')
                                    pending=pending[written:]
                                self.checkpoint('CopyChunk')
                            if count!=request['plan']['archiveBytes'] or checksum.hexdigest().upper()!=request['plan']['archiveSha256']:self.reject('HISTORY_CHANGED')
                            if self.source.attributes(output):self.reject('COPY_CHANGED')
                            os.fchown(output,*plan['sourceIdentity'][3:5]);os.fchmod(output,stat.S_IMODE(plan['sourceIdentity'][2]))
                            for key,value in plan['attributes']:os.setxattr(output,key,base64.b64decode(value,validate=True))
                            os.utime(output,ns=(plan['sourceIdentity'][7],plan['sourceIdentity'][7]));os.fsync(output);os.fsync(private)
                            record.update(state='Prepared',preparedIdentity=self.check_file(output,request,session));self.save(state,record);self.checkpoint('Prepared')
                            attest();self.check_stage(parent,private,output,record,before,session);self.check_file(output,request,session,record['preparedIdentity'])
                            record['state']='Publishing';self.save(state,record);self.checkpoint('Publishing')
                            attest();self.check_stage(parent,private,output,record,before,session);self.check_file(output,request,session,record['preparedIdentity'])
                            self.f.exchange(private,'copy.partial',parent,leaf,1);self.checkpoint('PublishedNamespace');os.fsync(parent);os.fsync(private)
                            identity=self.check_file(output,request,session)
                            if identity[:8]!=record['preparedIdentity'][:8] or identity[8]<record['preparedIdentity'][8] or source.metadata(os.stat(leaf,dir_fd=parent,follow_symlinks=False))!=identity or os.listdir(private):self.reject('COPY_CHANGED')
                            expected={**before,leaf:identity}
                            if self.entries(parent,name)!=expected:self.reject('PARENT_CHANGED')
                            with source.parent(plan,session) as current:
                                if not os.path.samestat(os.fstat(current),os.fstat(parent)):self.reject('PARENT_CHANGED')
                            attest();self.preflight.check_claim(ref,self.claim(request))
                            record.update(state='Published',publishedIdentity=identity);self.save(state,record);self.checkpoint('Published')
                        finally:
                            if output is not None:os.close(output)
                            os.close(private)
                    if not self.binding(record,session):self.reject('COPY_CHANGED')
                    # Completed proves publication, not authority to retire the
                    # source claim, HDD history or stage. The container can
                    # rename entries in its writable parent despite our locks;
                    # stat-then-rmdir cannot safely target that stage inode.
                    record['state']='Completed';self.save(state,record);self.checkpoint('Completed');return self.public(record,session)
                except BaseException:
                    with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.reconcile(state,record)
                    raise

        def binding(self,record,session):
            if session is None or record['publishedIdentity'] is None or not self.matches(self.source.read_restore_claim(self.ref(record)),record):return False
            ref=self.ref(record)
            try:
                with self.source.parent(ref['plan'],session) as parent:
                    fd=self.f.open_at(parent,ref['plan']['command']['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
                    try:self.check_file(fd,record,session,record['publishedIdentity'])
                    finally:os.close(fd)
                return True
            except (OSError,self.r.Rejected,self.f.Rejected):return False

        def public(self,record,session=None):
            result=super().public(record)
            result.update(archiveCopied=True if record['state']=='Completed' else False if record['state'] in ('Preparing','Reserved','Aborted') else None,bindingCurrent=None if session is None else record['state']=='Completed' and self.binding(record,session),publishedIdentity=record['publishedIdentity'] if record['state']=='Completed' else None)
            return result

        def observe(self,request,session,discard):
            with self.locked(request) as (state,record),self.source_records(request) as (deleted,original):
                if record is None:
                    same=self.matches(self.source.read_restore_claim(self.ref(request)),request)
                    if not discard and not same:self.reject('NOT_FOUND')
                    record=self.new_record(request,'Unknown' if same else 'Aborted');self.save(state,record)
                self.reconcile(state,record)
                if discard and record['state']=='Reserved':
                    self.fresh(request,session,deleted,original,True);record['state']='Aborted';self.save(state,record);self.checkpoint('AbortedSaved');self.clear_claim(request);self.checkpoint('Released')
                return self.public(record,session)

    return ContainerRecycleArchiveCopies
