#!/usr/bin/env python3
"""Trusted heterogeneous recursive ACL restore kernel, no CLI/RPC/install grant.

Restore the selected capture's changed ACL type by path/kind. The other ACL,
ordinary attributes, owners and content stay current. Historical inode numbers
are never substituted for the current target tree's identity/version.
"""
import contextlib
import json
import os
import re
import stat

NAMESPACE='container-recursive-acl-restore-v1'

def recursive_acl_restore_history_type(recursive):
    class RestoreHistory(recursive.RecursivePermissionHistory):
        NAMESPACE=NAMESPACE
        RESOURCE_TYPE='ContainerRecursiveAclRestoreMetadata'
        ERROR_PREFIX='CONTAINER_RECURSIVE_ACL_RESTORE_'
        DOMAIN=b'whaledeck-container-recursive-acl-restore-history-v1\n'
    return RestoreHistory

def recursive_acl_restore_type(acl_module,permission_module,directory_type,recursive,acl_profile,reader_type):
    class AclEntry(acl_module.container_acl_type(directory_type)):
        validate_entries=staticmethod(acl_module.validate_entries)
    base=recursive.recursive_permissions_type(AclEntry,directory_type,acl_module.acl_history_type(permission_module),acl=True,acl_restore=True)

    class Restore(base):
        def __init__(self,uploads,publisher,state_path,history,**limits):
            super().__init__(uploads,publisher,state_path,history,**limits)
            source_history=acl_profile.recursive_acl_history_type(recursive)(history.storage)
            source=acl_profile.recursive_acl_type(acl_module,permission_module,directory_type,recursive)(uploads,publisher,state_path,source_history)
            self.sources=reader_type(source)

        def restore_command(self,value):
            self.f.identity(value['captureId'])
            if type(value['expectedHistoryDigest']) is not str or not re.fullmatch('[A-F0-9]{64}',value['expectedHistoryDigest']):self.reject('SOURCE_INVALID')

        @contextlib.contextmanager
        def source_context(self,command,session):
            self.tick();proof=self.guard(command,session)
            with self.sources.namespace() as (configuration,parent,fd):
                if fd is None:self.reject('SOURCE_NOT_FOUND')
                name=command['captureId']+'.json'
                try:
                    frame=self.sources.frame(os.stat(name,dir_fd=fd,follow_symlinks=False))
                    value=self.sources.metadata(fd,command['captureId'],frame)
                except FileNotFoundError:self.reject('SOURCE_NOT_FOUND')
                if not self.sources.matches(value,command):self.reject('SOURCE_NOT_FOUND')
                if value['historyDigest']!=command['expectedHistoryDigest']:self.reject('SOURCE_CHANGED')
                identity=self.sources.frame(os.fstat(fd))[:5]
                def current():
                    self.tick();session.current();self.guard(command,session,proof)
                    self.history.storage.attest(configuration,fd,read_only=True)
                    try:
                        named=self.sources.frame(os.stat(self.sources.NAMESPACE,dir_fd=parent,follow_symlinks=False))[:5]
                        now=self.sources.frame(os.stat(name,dir_fd=fd,follow_symlinks=False))
                    except FileNotFoundError:self.reject('SOURCE_CHANGED')
                    if named!=identity or now!=frame:self.reject('SOURCE_CHANGED')
                current();yield value,frame,current;current()

        def restore_source(self,command,session):
            with self.source_context(command,session) as (value,frame,_):
                return dict(metadata=value,frame=frame,entries=value['entries'],byPath={e['relativePath']:e['plan'] for e in value['entries']})

        @staticmethod
        def restore_summary(source):return reader_type.summary(source['metadata'])

        def restore_targets(self,command,kind,access,relative,source):
            if source is None or relative not in source['byPath'] or source['byPath'][relative]['kind']!=kind:self.reject('SOURCE_TREE_MISMATCH')
            acl_type=source['metadata']['plan']['command']['aclType'];old=source['byPath'][relative]
            if kind=='File' and acl_type=='default':return dict(aclType='access',entries=json.loads(recursive.encode(access)))
            return dict(aclType=acl_type,entries=json.loads(recursive.encode(old['accessAcl' if acl_type=='access' else 'defaultAcl'])))

        @staticmethod
        def restore_targets_digest(entries):
            projected=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],aclType=e['plan']['command']['aclType'],entries=e['plan']['command']['entries']) for e in entries]
            return recursive.digest(b'whaledeck-container-recursive-acl-restore-targets-v1\n',projected)

        def validate_restore_plan(self,plan):
            s=plan['restoreSource'];c=plan['command'];keys={'captureId','historyDigest','createdAtUnixMs','sourceInstanceId','changedAclType','manifestDigest','entryCount','fileCount','directoryCount','changeCount'}
            if type(s) is not dict or set(s)!=keys or s['captureId']!=c['captureId'] or s['historyDigest']!=c['expectedHistoryDigest'] or s['changedAclType'] not in ('access','default') or \
                    type(s['createdAtUnixMs']) is not int or not 0<=s['createdAtUnixMs']<=9223372036854775807 or type(s['manifestDigest']) is not str or not re.fullmatch('[A-F0-9]{64}',s['manifestDigest']):self.reject('SOURCE_INVALID')
            self.r.target_binding(c['containerId'],s['sourceInstanceId'])
            if any(type(s[k]) is not int for k in ('entryCount','fileCount','directoryCount','changeCount')) or any(s[k]!=plan[k] for k in ('entryCount','fileCount','directoryCount')) or not 0<=s['changeCount']<=s['entryCount'] or s['changedAclType']=='default' and s['changeCount']>s['directoryCount']:self.reject('SOURCE_INVALID')
            frame=plan['restoreSourceFrame']
            if type(frame) is not list or len(frame)!=9 or any(type(v) is not int for v in frame) or not all(0<=frame[n]<=18446744073709551615 for n in (0,1)) or frame[1]==0 or \
                    frame[2]!=(stat.S_IFREG|0o600) or frame[3:6]!=[0,0,1] or not 0<frame[6]<=recursive.MAX_METADATA or any(not -9223372036854775808<=frame[n]<=9223372036854775807 for n in (7,8)):self.reject('SOURCE_INVALID')
            if type(plan['restoreTargetsDigest']) is not str or not re.fullmatch('[A-F0-9]{64}',plan['restoreTargetsDigest']):self.reject('SOURCE_INVALID')

        def validate_manifest(self,request,saved):
            entries=super().validate_manifest(request,saved)
            self.sources.topology(saved)
            return entries

        @contextlib.contextmanager
        def mutation_source(self,request,session):
            with self.source_context(request['plan']['command'],session) as (value,frame,current):
                if self.restore_summary(dict(metadata=value))!=request['plan']['restoreSource'] or frame!=request['plan']['restoreSourceFrame']:self.reject('SOURCE_CHANGED')
                yield current
    return Restore
