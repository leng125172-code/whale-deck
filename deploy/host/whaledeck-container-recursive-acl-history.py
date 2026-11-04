#!/usr/bin/env python3
"""Verified recursive ACL preimages; no CLI, write, restore or RPC grant.

Read the attested aggregate manifest, not the subset of children that happened
to finish. Historical ACLs are heterogeneous and cannot be restored by a single
uniform file/directory ACL. Source selection stays bound to a complete digest.
"""
import hashlib
import json
import os
import time

MAX_METADATA = 16 * 1024**2


def recursive_acl_history_reader_type(common):
    class RecursiveAclHistoryReader(common.ContainerAclHistoryReader):
        NAMESPACE = 'container-recursive-acl-v1'
        HISTORY_DOMAIN = b'whaledeck-container-recursive-acl-history-v1\n'
        INVENTORY_DOMAIN = b'whaledeck-container-recursive-acl-history-inventory-v1\n'
        ERROR_PREFIX = 'CONTAINER_RECURSIVE_ACL_HISTORY_'
        # Each capture may have one aggregate JSON and a child-history directory.
        DIRECTORY_MULTIPLIER = 2

        def __init__(self, acl):
            super().__init__(acl)
            if acl.COMMAND != {'containerId','instanceId','path','expectedVersion','aclType','fileEntries','directoryEntries'}:
                raise ValueError(self.ERROR_PREFIX + 'ENGINE_MISMATCH')

        def topology(self, value):
            deadline = time.monotonic() + common.SECONDS
            entries = {entry['relativePath']:entry['plan'] for entry in value['entries']}
            command = value['plan']['command']
            for relative, child in entries.items():
                if time.monotonic() > deadline: self.reject('TIMEOUT')
                if not relative: continue
                if relative.count('/') >= 32: self.reject('INVALID')
                parent = relative.rpartition('/')[0]
                if parent not in entries or entries[parent]['kind'] != 'Directory': self.reject('INVALID')
                identity = entries[parent]['sourceIdentity']
                path = command['path'] + ('/' + parent if parent else '')
                version = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps(
                    [command['containerId'],command['instanceId'],path,*identity], ensure_ascii=False, separators=(',',':')).encode()).hexdigest().upper()
                if child['parentIdentity'] != identity[:2] or child['parentVersion'] != version: self.reject('INVALID')

        def metadata(self, descriptor, capture, expected_frame=None):
            self.f.identity(capture)
            name = capture + '.json'
            before = self.frame(os.stat(name, dir_fd=descriptor, follow_symlinks=False))
            if expected_frame is not None and before != expected_frame: self.reject('VERSION_CONFLICT')
            value = self.h.storage.read_json(descriptor, name, MAX_METADATA)
            fields = {'schemaVersion','resourceType','storageId','operationId','owner','plan','entries','createdAtUnixMs','historyDigest'}
            if type(value) is not dict or set(value) != fields or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1 or \
                    value['resourceType'] != 'ContainerRecursiveAclMetadata' or value['storageId'] != 'database-platform.backup.hdd' or value['operationId'] != capture or \
                    type(value['createdAtUnixMs']) is not int or not 0 <= value['createdAtUnixMs'] <= 9223372036854775807 or \
                    type(value['historyDigest']) is not str or not common.HASH.fullmatch(value['historyDigest']): self.reject('INVALID')
            self.a.request({key:value[key] for key in ('operationId','owner','plan')})
            digest = hashlib.sha256(self.HISTORY_DOMAIN + common.encode({key:item for key,item in value.items() if key != 'historyDigest'})).hexdigest().upper()
            if digest != value['historyDigest']: self.reject('INVALID')
            self.a.validate_manifest(value, value)
            self.topology(value)
            if self.frame(os.stat(name, dir_fd=descriptor, follow_symlinks=False)) != before: self.reject('VERSION_CONFLICT')
            return value

        @staticmethod
        def summary(value):
            plan = value['plan']
            return dict(captureId=value['operationId'],historyDigest=value['historyDigest'],createdAtUnixMs=value['createdAtUnixMs'],
                sourceInstanceId=plan['command']['instanceId'],changedAclType=plan['command']['aclType'],manifestDigest=plan['manifestDigest'],
                entryCount=plan['entryCount'],fileCount=plan['fileCount'],directoryCount=plan['directoryCount'],changeCount=plan['changeCount'])

        @staticmethod
        def entry(value):
            plan = value['plan']
            return dict(relativePath=value['relativePath'],kind=plan['kind'],originalMode=plan['sourceIdentity'][2]&0o777,
                accessAcl=json.loads(common.encode(plan['accessAcl'])),defaultAcl=json.loads(common.encode(plan['defaultAcl'])))

        def read(self, request, session):
            proof = self.scope(request,session,{'containerId','instanceId','path','captureId','expectedHistoryDigest','offset','take'})
            if type(request['captureId']) is not str or not common.CAPTURE.fullmatch(request['captureId']) or \
                    type(request['expectedHistoryDigest']) is not str or not common.HASH.fullmatch(request['expectedHistoryDigest']) or \
                    type(request['offset']) is not int or not 0 <= request['offset'] < common.MAX_RECORDS or \
                    type(request['take']) is not int or not 1 <= request['take'] <= common.PAGE_LIMIT: self.reject('REQUEST_INVALID')
            deadline = time.monotonic() + common.SECONDS
            self.current(request,session,proof,deadline)
            with self.namespace() as (configuration,_,descriptor):
                if descriptor is None: self.reject('NOT_FOUND')
                name = request['captureId'] + '.json'
                try:
                    frame = self.frame(os.stat(name,dir_fd=descriptor,follow_symlinks=False))
                    value = self.metadata(descriptor,request['captureId'],frame)
                except FileNotFoundError: self.reject('NOT_FOUND')
                if not self.matches(value,request): self.reject('NOT_FOUND')
                if value['historyDigest'] != request['expectedHistoryDigest']: self.reject('VERSION_CONFLICT')
                if request['offset'] >= len(value['entries']): self.reject('REQUEST_INVALID')
                header = {**self.summary(value),'containerId':request['containerId'],'instanceId':request['instanceId'],'path':request['path'],'offset':request['offset']}
                entries = []; offset = request['offset']; size = len(common.encode({**header,'entries':[],'complete':False,'nextOffset':common.MAX_RECORDS}))+16
                for item in value['entries'][offset:offset+request['take']]:
                    self.current(request,session,proof,deadline)
                    entry = self.entry(item); length = len(common.encode(entry))+1
                    if length > common.RESPONSE_BYTES: self.reject('RESPONSE_LIMIT')
                    if size+length > common.RESPONSE_BYTES: break
                    entries.append(entry);size+=length;offset+=1
                if not entries: self.reject('RESPONSE_LIMIT')
                self.h.storage.attest(configuration,descriptor,read_only=True)
                self.current(request,session,proof,deadline)
                if self.frame(os.stat(name,dir_fd=descriptor,follow_symlinks=False)) != frame: self.reject('VERSION_CONFLICT')
                complete = offset == len(value['entries'])
                result = {**header,'entries':entries,'complete':complete,'nextOffset':None if complete else offset}
                if len(common.encode(result)) > common.RESPONSE_BYTES: self.reject('RESPONSE_LIMIT')
                return result
    return RecursiveAclHistoryReader
