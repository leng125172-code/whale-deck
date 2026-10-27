#!/usr/bin/env python3
"""Container-only HDD inventory and verified restoration-source handles.

Internal engine, no CLI or write surface. A later signed Agent adapter must
supply a live protected ContainerRoot and a human-authorized durable intent.
Presence is not content verification; only verified_source hashes all bytes.
No capture is rewritten, adopted as a host root, or deleted by this engine.
"""
import contextlib
import hashlib
import json
import os
import re
import stat
import time


class ContainerHistory:
    FIELDS = frozenset(('schemaVersion','resourceType','storageId','operationId','owner','containerId','instanceId',
        'path','sourceVersion','sourceParentVersion','targetProof','uploadFingerprint','sourceIdentity','sizeBytes',
        'sha256','state','createdAtUnixMs','differentSourceDevice','captureKind','captureState'))
    MAX_RECORDS = 30000
    SCAN_LIMIT = 500
    LIST_SECONDS = 5
    SOURCE_SECONDS = 120

    def __init__(self, uploads, storage):
        self.u,self.b=uploads,storage
        self.f,self.r=uploads.f,uploads.r
        if storage.f is not self.f or storage.namespace!='container-files-v1':self.reject('ENGINE_MISMATCH')

    def reject(self, code):raise self.r.Rejected('CONTAINER_HISTORY_'+code)

    @staticmethod
    def digest(kind,value):
        raw=json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode('utf8')
        return hashlib.sha256(b'whaledeck-container-history-v1\n'+kind.encode('ascii')+b'\n'+raw).hexdigest().upper()

    def hash(self,value):
        if type(value) is not str or not re.fullmatch('[A-F0-9]{64}',value):self.reject('REQUEST_INVALID')

    def authorize(self, request, session, empty=False):
        self.r.target_binding(request['containerId'],request['instanceId'])
        self.r.canonical_path(request['path'],empty=empty)
        if self.r.protected(request['path'],self.f):self.reject('PROTECTED')
        if any(request[key]!=session.request.get(key) for key in ('containerId','instanceId')):self.reject('SCOPE_MISMATCH')
        session.revalidate()
        return self.u.target_proof(session)

    def current(self,session,proof,deadline):
        if time.monotonic()>deadline:self.reject('TIMEOUT')
        session.current()
        if self.u.target_proof(session)!=proof:self.reject('TARGET_CHANGED')

    def metadata(self,target,capture):
        self.f.identity(capture)
        descriptor=self.f.open_at(target,capture+'.json',os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW)
        try:
            before=self.f.regular(descriptor)
            if before.st_uid!=0 or stat.S_IMODE(before.st_mode)!=0o600 or before.st_size>16384:self.reject('METADATA_INVALID')
            data=bytearray()
            while chunk:=os.read(descriptor,16385-len(data)):
                data.extend(chunk)
                if len(data)>16384:self.reject('METADATA_INVALID')
            if self.r.fingerprint(os.fstat(descriptor))!=self.r.fingerprint(before):self.reject('METADATA_CHANGED')
            def pairs(items):
                result={}
                for key,value in items:
                    if key in result:self.reject('METADATA_INVALID')
                    result[key]=value
                return result
            try:value=json.loads(data,object_pairs_hook=pairs)
            except (ValueError,UnicodeError):self.reject('METADATA_INVALID')
        finally:os.close(descriptor)
        if type(value) is not dict or set(value)!=self.FIELDS or type(value['schemaVersion']) is not int or value['schemaVersion']!=2 or \
                value['resourceType']!='ContainerFile' or value['storageId']!='database-platform.backup.hdd' or value['operationId']!=capture:
            self.reject('METADATA_INVALID')
        for key in ('owner','sourceVersion','sourceParentVersion','targetProof','uploadFingerprint','sha256'):self.hash(value[key])
        self.r.target_binding(value['containerId'],value['instanceId'])
        self.r.canonical_path(value['path'],empty=False)
        identity=value['sourceIdentity']
        if type(identity) is not list or len(identity)!=8 or any(type(item) is not int for item in identity) or \
                any(not 0<=item<=18446744073709551615 for item in identity[:7]) or identity[1]==0 or identity[2]>65535 or \
                not stat.S_ISREG(identity[2]) or identity[3]>=4294967295 or identity[4]>=4294967295 or identity[5]!=1 or \
                not -9223372036854775808<=identity[7]<=9223372036854775807 or \
                type(value['sizeBytes']) is not int or not 0<=value['sizeBytes']<=self.r.MAX_BYTES or identity[6]!=value['sizeBytes'] or \
                type(value['createdAtUnixMs']) is not int or not 0<=value['createdAtUnixMs']<=253402300799999 or \
                type(value['differentSourceDevice']) is not bool or value['state'] not in ('Preparing','Verified') or \
                value['captureKind']!='Overwrite' or value['captureState']!='Available':self.reject('METADATA_INVALID')
        return value

    def data(self,target,capture,value):
        descriptor=self.f.open_at(target,capture+'.data',os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW)
        try:
            info=self.f.regular(descriptor)
            if info.st_uid!=0 or stat.S_IMODE(info.st_mode)!=0o600 or info.st_size!=value['sizeBytes']:self.reject('CONTENT_INVALID')
            return descriptor
        except BaseException:os.close(descriptor);raise

    def public(self,value,availability):
        # Original owner, raw inode/UID/GID, upload binding and host storage
        # paths are intentionally absent from the catalog response.
        return {'captureId':value['operationId'],'containerId':value['containerId'],'originalInstanceId':value['instanceId'],
            'path':value['path'],'sourceVersion':value['sourceVersion'],'sizeBytes':value['sizeBytes'],'sha256':value['sha256'],
            'createdAtUnixMs':value['createdAtUnixMs'],'differentSourceDevice':value['differentSourceDevice'],
            'availability':availability,'captureHash':self.digest('capture',value)}

    def listing(self,request,session):
        fields={'containerId','instanceId','path','after','take','expectedVersion'}
        if type(request) is not dict or set(request)!=fields or type(request['take']) is not int or not 1<=request['take']<=50 or \
                type(request['after']) is not str or type(request['expectedVersion']) is not str:self.reject('REQUEST_INVALID')
        if request['after']:self.f.identity(request['after'])
        if request['expectedVersion']:self.hash(request['expectedVersion'])
        if request['after'] and not request['expectedVersion']:self.reject('VERSION_REQUIRED')
        proof=self.authorize(request,session,empty=True);deadline=time.monotonic()+self.LIST_SECONDS
        try:
            with self.b.target(create_namespace=False,read_only=True) as (configuration,target):
                directory_version=self.f.version(os.fstat(target))
                version=self.digest('inventory',{'containerId':request['containerId'],'instanceId':request['instanceId'],
                    'path':request['path'],'targetProof':proof,'directoryVersion':directory_version})
                if request['expectedVersion'] and request['expectedVersion']!=version:self.reject('VERSION_CONFLICT')
                names=os.listdir(target)
                if len(names)>self.MAX_RECORDS:self.reject('HISTORY_LIMIT')
                names=sorted(name[:-5] for name in names if re.fullmatch(r'[a-f0-9]{32}\.json',name) and name[:-5]>request['after'])
                entries=[];scanned=invalid=encoded=0;cursor=request['after'];complete=True
                for capture in names:
                    if scanned>=self.SCAN_LIMIT or len(entries)>=request['take'] or time.monotonic()>deadline:
                        if scanned==0 and not entries:self.reject('TIMEOUT')
                        complete=False;break
                    previous_cursor=cursor
                    scanned+=1;cursor=capture;self.current(session,proof,deadline)
                    try:
                        value=self.metadata(target,capture)
                        if value['containerId']!=request['containerId'] or request['path'] and value['path']!=request['path']:continue
                        if self.r.protected(value['path'],self.f):continue
                        availability='Incomplete'
                        if value['state']=='Verified':
                            try:descriptor=self.data(target,capture,value)
                            except FileNotFoundError:availability='Missing'
                            except (OSError,self.f.Rejected,self.r.Rejected):availability='Invalid'
                            else:os.close(descriptor);availability='Present'
                        entry=self.public(value,availability)
                        length=len(json.dumps(entry,ensure_ascii=False,separators=(',',':')).encode('utf8'))+1
                        if encoded+length>self.f.LIST_ENTRIES_BYTES:
                            cursor=previous_cursor;scanned-=1;complete=False;break
                        entries.append(entry);encoded+=length
                    except (OSError,self.f.Rejected,self.r.Rejected,ValueError,KeyError,TypeError):invalid+=1
                session.revalidate();self.current(session,proof,deadline)
                self.b.attest(configuration,target,read_only=True)
                if self.f.version(os.fstat(target))!=directory_version:self.reject('VERSION_CONFLICT')
                return {'containerId':request['containerId'],'instanceId':request['instanceId'],'path':request['path'],
                    'targetProof':proof,'entries':entries,'version':version,'nextCursor':'' if complete else cursor,
                    'scanned':scanned,'invalidRecords':invalid,'complete':complete}
        except FileNotFoundError:
            # An absent namespace is not silently created by a read. Other
            # missing attestation paths remain storage failures, not empty data.
            raise

    @contextlib.contextmanager
    def verified_source(self,request,session):
        fields={'containerId','instanceId','path','captureId','expectedCaptureHash'}
        if type(request) is not dict or set(request)!=fields:self.reject('REQUEST_INVALID')
        self.f.identity(request['captureId']);self.hash(request['expectedCaptureHash'])
        proof=self.authorize(request,session);deadline=time.monotonic()+self.SOURCE_SECONDS
        with self.b.target(create_namespace=False,read_only=True) as (configuration,target):
            value=self.metadata(target,request['captureId'])
            if value['containerId']!=request['containerId'] or value['path']!=request['path']:self.reject('SCOPE_MISMATCH')
            if self.r.protected(value['path'],self.f):self.reject('PROTECTED')
            if self.digest('capture',value)!=request['expectedCaptureHash']:self.reject('CAPTURE_CHANGED')
            if value['state']!='Verified':self.reject('INCOMPLETE')
            descriptor=self.data(target,request['captureId'],value)
            try:
                before=self.r.fingerprint(os.fstat(descriptor));digest=hashlib.sha256();count=0
                while chunk:=os.read(descriptor,self.r.CHUNK):
                    self.current(session,proof,deadline);count+=len(chunk)
                    if count>value['sizeBytes']:self.reject('CONTENT_CHANGED')
                    digest.update(chunk)
                if count!=value['sizeBytes'] or digest.hexdigest().upper()!=value['sha256'] or \
                        self.r.fingerprint(os.fstat(descriptor))!=before:self.reject('CONTENT_INVALID')
                session.revalidate();self.current(session,proof,deadline);self.b.attest(configuration,target,read_only=True)
                if self.metadata(target,request['captureId'])!=value:self.reject('CAPTURE_CHANGED')
                os.lseek(descriptor,0,os.SEEK_SET)
                yield value,descriptor
                self.current(session,proof,deadline);self.b.attest(configuration,target,read_only=True)
                if self.r.fingerprint(os.fstat(descriptor))!=before or self.metadata(target,request['captureId'])!=value:
                    self.reject('CONTENT_CHANGED')
            finally:os.close(descriptor)
