"""Separate retirement request/journal policy over proven one-shot task control.

The trusted fixed loader supplies all types. No archive request/signature gains
retirement semantics. A new task is required for any explicitly authorized
recovery; an interrupted task never starts its execution again.
"""
import hashlib
from pathlib import Path
import re

BASE = Path('/var/lib/whaledeck-development-retirement-operations')
SHA = re.compile(r'[a-f0-9]{64}\Z')


class RetirementPolicy:
    def __init__(self, driver, planner, executor, execution):
        super().__init__(driver, planner, execution)
        self.planner, self.executor = planner, executor

    def base(self):
        return BASE

    def refuse(self, code):
        self.d.reject('RETIREMENT_TASK_' + code)

    def persist(self, directory, name, value):
        self.executor.write(directory, name, value)

    def io_failure(self, code):
        return 'WORKSPACE_STORAGE_RETIREMENT_' + code

    def validate(self, value):
        if (not isinstance(value, dict) or set(value) != {'schemaVersion', 'taskId', 'phase',
                'retirementId', 'prerequisite', 'retirementPlanSha256'}
                or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1
                or value['phase'] not in ('plan', 'execute') or len(self.d.canonical(value)) > 4096):
            self.refuse('REQUEST_INVALID')
        self.planner.validate(value['prerequisite'])
        a = self.planner.archives
        a.operation(value['taskId']); a.operation(value['retirementId'])
        p = value['prerequisite']
        if (len({value['taskId'], value['retirementId'], p['archiveId'], p['storage']['workspaceId']}) != 4
                or not isinstance(value['retirementPlanSha256'], str)
                or (not SHA.fullmatch(value['retirementPlanSha256']) if value['phase'] == 'execute'
                    else value['retirementPlanSha256'] != '')):
            self.refuse('REQUEST_INVALID')

    def engine_request(self, value):
        return {k: value[k] for k in ('schemaVersion', 'retirementId', 'prerequisite', 'retirementPlanSha256')}

    def perform(self, value):
        try:
            result = (self.planner.plan(value['prerequisite']) if value['phase'] == 'plan'
                else self.executor.run(self.engine_request(value), self.planner))
        except self.d.quota.QuotaError as error:
            code = str(error)
            if not re.fullmatch('QUOTA_[A-Z_]{1,60}', code):
                code = 'QUOTA_UNCONFIRMED'
            self.d.reject('RETIREMENT_' + code)
        self.result(value, result)
        return result

    def result(self, value, result):
        p = value['prerequisite']
        if not isinstance(result, dict):
            self.refuse('RESULT_INVALID')
        if value['phase'] == 'plan':
            fields = {'schemaVersion', 'purpose', 'storage', 'expectedEpoch', 'storageProofSha256',
                'sourceVersion', 'sourceManifestSha256', 'entryCount', 'totalBytes', 'archiveId',
                'archivePlanSha256', 'captureReceiptSha256', 'archiveFilesystemUuid', 'hddProofSha256',
                'archiveSha256', 'archiveIndexSha256', 'archiveBytes', 'archiveInode',
                'retainsSource', 'reservesBudget', 'deletionAuthorized', 'retirementPlanSha256'}
            bound = dict(schemaVersion=1, purpose='whaledeck-development-source-retirement-plan-v1',
                storage=p['storage'], expectedEpoch=p['expectedEpoch'], archiveId=p['archiveId'],
                archivePlanSha256=p['archivePlanSha256'], captureReceiptSha256=p['captureReceiptSha256'],
                retainsSource=True, reservesBudget=True, deletionAuthorized=False)
            digest = dict(result); expected = digest.pop('retirementPlanSha256', None)
            if expected != hashlib.sha256(self.d.canonical(digest)).hexdigest():
                self.refuse('RESULT_INVALID')
            hashes = ('storageProofSha256', 'sourceManifestSha256', 'hddProofSha256',
                'archiveSha256', 'archiveIndexSha256', 'retirementPlanSha256')
            numbers = ('entryCount', 'totalBytes', 'archiveBytes', 'archiveInode')
            if (not isinstance(result.get('sourceVersion'), str) or not re.fullmatch('[A-F0-9]{64}', result['sourceVersion'])
                    or not isinstance(result.get('archiveFilesystemUuid'), str)):
                self.refuse('RESULT_INVALID')
            self.planner.archives.operation(result['archiveFilesystemUuid'])
        else:
            fields = {'schemaVersion', 'purpose', 'retirementId', 'workspaceId', 'requestSha256',
                'retirementPlanSha256', 'storageProofSha256', 'sourceManifestSha256',
                'captureReceiptSha256', 'projectId', 'sourceRemoved', 'quotaReleased', 'claimReleased',
                'retainsHdd', 'releasesApplicationBudget'}
            bound = dict(schemaVersion=1, purpose='whaledeck-development-source-retirement-receipt-v1',
                retirementId=value['retirementId'], workspaceId=p['storage']['workspaceId'],
                requestSha256=hashlib.sha256(self.d.canonical(self.engine_request(value))).hexdigest(),
                retirementPlanSha256=value['retirementPlanSha256'], captureReceiptSha256=p['captureReceiptSha256'],
                sourceRemoved=True, quotaReleased=True, claimReleased=True, retainsHdd=True, releasesApplicationBudget=False)
            hashes = ('requestSha256', 'retirementPlanSha256', 'storageProofSha256',
                'sourceManifestSha256', 'captureReceiptSha256')
            numbers = ('projectId',)
        if (set(result) != fields or any(self.d.canonical(result.get(k)) != self.d.canonical(v) for k, v in bound.items())
                or any(not isinstance(result.get(k), str) or not SHA.fullmatch(result[k]) for k in hashes)
                or any(type(result.get(k)) is not int or result[k] < 0 for k in numbers)
                or value['phase'] == 'execute' and not 1 <= result['projectId'] <= 0x7fffffff):
            self.refuse('RESULT_INVALID')

    def completed(self, directory, value):
        completed = super().completed(directory, value)
        if completed is not None and completed['status'] == 'Succeeded':
            self.result(value, completed['result'])
        return completed


def create(shared, driver, planner, executor, execution):
    # Share only locking, immutable admission, pinned result commit and exact
    # cgroup stop mechanics. Both schemas and journals remain disjoint.
    class RetirementOperations(RetirementPolicy, shared.ArchiveOperations):
        pass
    return RetirementOperations(driver, planner, executor, execution)
