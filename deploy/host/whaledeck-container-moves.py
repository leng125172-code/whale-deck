#!/usr/bin/env python3
"""Container-domain move plans and permanent observations, no transport.

Reuse only the validated receipt-storage primitives of ContainerCopies. Plans,
operation kind, inner engine and physical facts belong to the move domain.
An authenticated fixed helper/RPC and business publication barrier are required
before exposure. Recovery never repeats copy, rename or source removal.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import stat


def container_moves_type(copy_type):
    class ContainerMoves(copy_type):
        PLAN = copy_type.PLAN | {'operationKind'}
        RECORD = copy_type.RECORD | {'destinationPublished', 'sourceRemoved', 'sourceRetained'}
        FACTS = ('destinationPublished', 'sourceRemoved', 'sourceRetained')

        def __init__(self, uploads, publisher, scope_type, file_type, tree_type, manifest_type, move_type, state_path):
            super().__init__(uploads, publisher, scope_type, file_type, tree_type, manifest_type, state_path)
            self.move_type = move_type

        def reject(self, suffix): raise self.f.Rejected('CONTAINER_MOVE_' + suffix)

        @classmethod
        def digest(cls, value):
            return hashlib.sha256(b'whaledeck-container-move-plan-v1\n' + cls.encode(
                {k: v for k, v in value.items() if k != 'fingerprint'})).hexdigest().upper()

        def inner_command(self, plan):
            command = super().inner_command(plan)
            if plan['kind'] == 'File': command['kind'] = 'move'
            return command

        def engine(self, scope, kind):
            return self.file_type(scope) if kind == 'File' else self.move_type(scope, self.manifest_type, self.tree_type)

        def validate_plan(self, plan):
            if type(plan) is not dict or set(plan) != self.PLAN or type(plan['schemaVersion']) is not int or plan['schemaVersion'] != 1 or plan['operationKind'] != 'Move':
                self.reject('PLAN_INVALID')
            self.command(plan['command'])
            if type(plan['kind']) is not str or plan['kind'] not in ('File', 'Directory'): self.reject('PLAN_INVALID')
            self.metadata(plan['sourceIdentity'], plan['kind'])
            if any(not self.hexadecimal(plan[k]) for k in ('targetProof', 'targetParentVersion', 'fingerprint')) or \
                    any(type(plan[k]) is not int or not 0 <= plan[k] < 4294967295 for k in ('uid', 'gid')) or \
                    any(not self.inode_value(plan[k]) for k in ('sourceParent', 'targetParent')):
                self.reject('PLAN_INVALID')
            if self.container_version(plan['command'], plan['command']['sourcePath'], plan['sourceIdentity']) != plan['command']['expectedVersion']:
                self.reject('PLAN_INVALID')
            inner = plan['innerPlan']
            if plan['kind'] == 'File':
                if type(inner) is not dict or set(inner) != self.file_type.PLAN_FIELDS or inner['command'] != self.inner_command(plan) or \
                        inner['sourceIdentity'] != plan['sourceIdentity'][:8] or inner['sourceParent'] != plan['sourceParent'] or inner['targetParent'] != plan['targetParent'] or \
                        type(inner['sizeBytes']) is not int or inner['sizeBytes'] != plan['sourceIdentity'][6] or not self.hexadecimal(inner['sha256']):
                    self.reject('PLAN_INVALID')
                self.metadata(inner['sourceIdentity'], 'File', 8)
                unsigned = {k: v for k, v in inner.items() if k != 'fingerprint'}
                expected = hashlib.sha256(b'whaledeck-file-operation-v1:' + json.dumps(unsigned, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
                if inner['fingerprint'] != expected: self.reject('PLAN_INVALID')
            else:
                engine = self.engine(self.f, 'Directory'); engine.validate(inner)
                copied = inner['copyPlan']
                if inner['sourceParent'] != plan['sourceParent'] or copied['command'] != self.inner_command(plan) or copied['targetParent'] != plan['targetParent']:
                    self.reject('PLAN_INVALID')
                common = {'path': '/container', 'containerId': plan['command']['containerId'], 'instanceId': plan['command']['instanceId'],
                          'targetProof': plan['targetProof'], 'ownerUid': plan['uid'], 'ownerGid': plan['gid']}
                for field, actions in (('sourcePolicyHash', ['files-list', 'files-download', 'files-mutate', 'files-tree-move']),
                        ('targetPolicyHash', ['files-mutate', 'files-upload', 'files-tree-move'])):
                    if copied[field] != engine.copy.digest({**common, 'allowedActions': actions}): self.reject('PLAN_INVALID')
            if len(self.encode(plan)) > self.LIMIT or self.digest(plan) != plan['fingerprint']: self.reject('PLAN_INVALID')

        def plan(self, command, session):
            self.command(command); scope = self.scope_type(self.u, self.p, session, command, self.state_path)
            with scope.directory({'rootId': scope.SOURCE, 'path': command['sourcePath']}, 'stat') as (_, parent, leaf):
                source = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                try:
                    info = os.fstat(source); kind = 'Directory' if stat.S_ISDIR(info.st_mode) else 'File'
                    identity = list(self.r.fingerprint(info)); self.metadata(identity, kind); source_parent = list(scope.inode(parent))
                finally: os.close(source)
            inner = self.engine(scope, kind).plan(scope.file_command() if kind == 'File' else scope.tree_command())
            with scope.directory({'rootId': scope.TARGET, 'path': command['targetPath']}, 'mutate') as (_, parent, _):
                parent_version = self.r.version(command, command['targetPath'].rpartition('/')[0], os.fstat(parent))
            result = {'schemaVersion': 1, 'operationKind': 'Move', 'command': dict(command), 'kind': kind, 'targetProof': scope.proof,
                'sourceIdentity': identity, 'sourceParent': source_parent, 'targetParent': list(scope.target_parent),
                'targetParentVersion': parent_version, 'uid': scope.owner[0], 'gid': scope.owner[1], 'innerPlan': inner}
            result['fingerprint'] = self.digest(result); self.validate_plan(result)
            return json.loads(self.encode(result))

        def adapter(self, plan, session, operation):
            base = self.f.trusted_directory(self.state_path)
            try:
                try: os.mkdir('engines', 0o700, dir_fd=base); os.fsync(base)
                except FileExistsError: pass
            finally: os.close(base)
            return self.scope_type(self.u, self.p, session, plan['command'], self.state_path / 'engines',
                target_parent=plan['targetParent'], source_parent=plan['sourceParent'], operation_id=operation,
                expected_binding={key: plan[key] for key in ('targetProof', 'uid', 'gid')})

        def new_record(self, request, phase):
            return {**super().new_record(request, phase), **{key: False if phase == 'Aborted' else None for key in self.FACTS}}

        def validate_record(self, record, operation):
            if type(record) is not dict or set(record) != self.RECORD or type(record['schemaVersion']) is not int or record['schemaVersion'] != 1:
                self.reject('RECEIPT_INVALID')
            self.request({k: record[k] for k in self.REQUEST})
            if record['operationId'] != operation or type(record['state']) is not str or record['state'] not in (*self.TERMINAL, 'Preparing', 'Running') or \
                    type(record['innerState']) is not str or record['innerState'] not in ('', *self.TERMINAL) or type(record['privateMayExist']) is not bool or \
                    record['innerReceiptHash'] != '' and not self.hexadecimal(record['innerReceiptHash']) or \
                    any(record[key] is not None and type(record[key]) is not bool for key in self.FACTS): self.reject('RECEIPT_INVALID')
            if record['state'] == 'Completed':
                self.metadata(record['publicationIdentity'], record['plan']['kind'])
                identity = record['publicationIdentity']; plan = record['plan']
                if any(record[key] is not True for key in self.FACTS) or identity[3:5] != plan['sourceIdentity'][3:5] or \
                        identity[2] & 0o777 != plan['sourceIdentity'][2] & 0o777 or identity[7] != plan['sourceIdentity'][7] or \
                        plan['kind'] == 'File' and identity[6] != plan['sourceIdentity'][6] or not self.hexadecimal(record['version']) or \
                        record['version'] != self.container_version(plan['command'], plan['command']['targetPath'], identity) or \
                        record['innerState'] != 'Completed' or not self.hexadecimal(record['innerReceiptHash']) or record['privateMayExist']:
                    self.reject('RECEIPT_INVALID')
            elif record['publicationIdentity'] is not None or record['version'] != '': self.reject('RECEIPT_INVALID')
            if record['state'] == 'Aborted' and any(record[key] is not False for key in self.FACTS): self.reject('RECEIPT_INVALID')

        def apply(self, request, session):
            with self.locked(request) as (directory, record):
                if record is not None:
                    self.recover(directory, record, session); return self.public(record, session, True)
                if session is None: self.reject('TARGET_UNAVAILABLE')
                if self.plan(request['plan']['command'], session) != request['plan']: self.reject('VERSION_CONFLICT')
                record = self.new_record(request, 'Preparing'); self.save(directory, record)
                try:
                    self.checkpoint('Preparing'); scope = self.adapter(record['plan'], session, record['operationId'])
                    engine = self.engine(scope, record['plan']['kind']); record['state'] = 'Running'; self.save(directory, record); self.checkpoint('Running')
                    inner = {**request, 'plan': record['plan']['innerPlan']}
                    if record['plan']['kind'] == 'File': engine.apply(inner)
                    else: engine.move(inner)
                    self.checkpoint('InnerFinished'); self.recover(directory, record, session)
                except BaseException:
                    with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected): self.recover(directory, record, session)
                    raise
                return self.public(record, session, False)

        def validate_inner(self, record, plan, operation, owner):
            if type(record) is not dict or record.get('operationId') != operation or record.get('owner') != owner or record.get('plan') != plan['innerPlan']:
                self.reject('RECEIPT_INVALID')
            phases = ('Preparing', 'DestinationApplying', 'SourceApplying') if plan['kind'] == 'File' else self.move_type.PHASES
            if type(record.get('state')) is not str or record['state'] not in (*self.TERMINAL, *phases): self.reject('RECEIPT_INVALID')
            if plan['kind'] == 'Directory':
                for key in ('destinationPublished', 'sourceRemoved', 'sourceRetained', 'sourceMutationStarted', 'destinationStagingRetained'):
                    if key in record and type(record[key]) is not bool: self.reject('RECEIPT_INVALID')
                if 'retainedIdentity' in record and not self.inode_value(record['retainedIdentity']): self.reject('RECEIPT_INVALID')
            else:
                if 'newIdentity' in record and not self.inode_value(record['newIdentity']): self.reject('RECEIPT_INVALID')
                if 'newMetadata' in record:
                    value = record['newMetadata']; self.metadata(value, 'File', 8)
                    if value[:2] != record.get('newIdentity') or value[2:5] != plan['sourceIdentity'][2:5] or value[6:8] != plan['sourceIdentity'][6:8]: self.reject('RECEIPT_INVALID')
            if record['state'] == 'Completed' and (not self.hexadecimal(record.get('version')) or record.get('sourceRetained') is not True or \
                    plan['kind'] == 'Directory' and (record.get('sourceRemoved') is not True or record.get('destinationPublished') is not True)):
                self.reject('RECEIPT_INVALID')

        def observed_target(self, record, scope, inner):
            plan = record['plan']; engine = self.engine(scope, plan['kind']); copied = None
            if plan['kind'] == 'Directory':
                with engine.copy.state(record['operationId'], record['owner']) as (_, copied):
                    if copied is None or copied['state'] != 'Completed' or copied['plan'] != plan['innerPlan']['copyPlan']:
                        self.reject('TARGET_UNCONFIRMED')
                metadata = copied
            else: metadata = inner
            with scope.directory({'rootId': scope.TARGET, 'path': plan['command']['targetPath']}, 'stat') as (_, parent, leaf):
                descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW | (os.O_DIRECTORY if copied is not None else 0))
                try:
                    info = os.fstat(descriptor)
                    if list(scope.inode(descriptor)) != metadata.get('newIdentity'): self.reject('TARGET_CHANGED')
                    if copied is None:
                        if scope.publication_identity(info) != metadata.get('newMetadata') or engine.hash_descriptor(descriptor, scope.regular(descriptor)) != plan['innerPlan']['sha256']:
                            self.reject('TARGET_CHANGED')
                    elif engine.copy.snapshot(descriptor, plan['innerPlan']['copyPlan']['command'], scope.roots()[scope.TARGET], lambda: False) != copied['treeSha256']:
                        self.reject('TARGET_CHANGED')
                    if inner['state'] == 'Completed' and scope.version(info) != inner['version']: self.reject('TARGET_CHANGED')
                    if self.r.fingerprint(os.fstat(descriptor)) != self.r.fingerprint(info) or self.r.fingerprint(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != self.r.fingerprint(info):
                        self.reject('TARGET_CHANGED')
                    return list(self.r.fingerprint(info))
                finally: os.close(descriptor)

        def observed_source(self, record, scope, inner):
            plan = record['plan']; engine = self.engine(scope, plan['kind'])
            with scope.directory({'rootId': scope.SOURCE, 'path': plan['command']['sourcePath']}, 'stat') as (_, parent, leaf):
                try: current = os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                except FileNotFoundError: removed = True
                else:
                    removed = False if list(scope.inode(parent)) == plan['sourceParent'] and [current.st_dev, current.st_ino] == plan['sourceIdentity'][:2] else None
                private = scope.private(parent)
                try:
                    name = record['operationId'] + ('.mutation' if plan['kind'] == 'File' else '.tree-move')
                    descriptor = self.f.open_at(private, name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | (os.O_DIRECTORY if plan['kind'] == 'Directory' else 0))
                    try:
                        if plan['kind'] == 'File':
                            if scope.publication_identity(scope.regular(descriptor)) != plan['sourceIdentity'][:8] or engine.hash_descriptor(descriptor, scope.regular(descriptor)) != plan['innerPlan']['sha256']:
                                self.reject('SOURCE_CHANGED')
                        else:
                            with engine.state(record['operationId'], record['owner']) as (folder, _):
                                manifest = engine.read_json(folder, record['operationId'] + '.manifest.json', engine.MAX_MANIFEST)
                            if engine.digest(manifest) != inner.get('manifestDigest'): self.reject('RECEIPT_INVALID')
                            engine.verify_retained(inner, manifest, descriptor, lambda: False)
                        scope_private = scope.private(parent)
                        try:
                            if scope.inode(private) != scope.inode(scope_private): self.reject('PRIVATE_UNSAFE')
                        finally: os.close(scope_private)
                        named = os.stat(name, dir_fd=private, follow_symlinks=False)
                        if (named.st_dev, named.st_ino) != scope.inode(descriptor): self.reject('SOURCE_CHANGED')
                    finally: os.close(descriptor)
                finally: os.close(private)
                if removed is True:
                    try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                    except FileNotFoundError: pass
                    else: self.reject('SOURCE_CHANGED')
                return removed, True

        def recover(self, directory, record, session):
            if record['state'] in self.TERMINAL: return
            unstarted = record['state'] == 'Preparing'; record['state'] = 'Unknown'; record['privateMayExist'] = True
            try:
                try: scope = self.adapter(record['plan'], session, record['operationId'])
                except (OSError, self.r.Rejected, self.f.Rejected): scope = self.adapter(record['plan'], None, record['operationId'])
                inner = self.observe_inner(record, scope, unstarted)
                record['innerState'] = inner['state']; record['innerReceiptHash'] = hashlib.sha256(self.encode(inner)).hexdigest().upper()
                if inner['state'] == 'Aborted':
                    record.update(state='Aborted', privateMayExist=bool(inner.get('mayHaveStaged')))
                    for key in self.FACTS: record[key] = False
                else:
                    identity = None
                    with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected):
                        identity = self.observed_target(record, scope, inner); record['destinationPublished'] = True
                    with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected):
                        record['sourceRemoved'], record['sourceRetained'] = self.observed_source(record, scope, inner)
                    if inner['state'] == 'Completed' and identity is not None and all(record[key] is True for key in self.FACTS):
                        record.update(state='Completed', publicationIdentity=identity, privateMayExist=False,
                            version=self.container_version(record['plan']['command'], record['plan']['command']['targetPath'], identity))
            except (OSError, self.r.Rejected, self.f.Rejected): pass
            self.save(directory, record)

        def public(self, record, session, replayed):
            current = False
            if record['state'] == 'Completed' and session is not None:
                try:
                    scope = self.adapter(record['plan'], session, record['operationId']); inner = self.observe_inner(record, scope)
                    current = inner['state'] == 'Completed' and hashlib.sha256(self.encode(inner)).hexdigest().upper() == record['innerReceiptHash'] and \
                        self.observed_target(record, scope, inner) == record['publicationIdentity'] and self.observed_source(record, scope, inner) == (True, True)
                except (OSError, self.r.Rejected, self.f.Rejected): pass
            plan = record['plan']; command = plan['command']
            return {'operationId': record['operationId'], 'operationKind': 'Move', 'containerId': command['containerId'], 'instanceId': command['instanceId'],
                'kind': plan['kind'], 'fingerprint': plan['fingerprint'], 'targetProof': plan['targetProof'], 'state': record['state'],
                'version': record['version'], 'bindingCurrent': current, 'privateMayExist': record['privateMayExist'], 'replayed': replayed,
                **{key: record[key] for key in self.FACTS}}

    return ContainerMoves
