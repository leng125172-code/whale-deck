"""Internal read-only source retirement prerequisite, without an execution entry.

Only a currently stopped, attested allocation matching the complete, independently
re-read HDD archive can yield a plan. The application must supply the capture
receipt digest from its authorized persistent workflow, never from browser facts.
This plan does not authorize deletion, release a budget, change quota, or persist
a retirement intent. Future execution must recheck under its own durable fence.
"""
import hashlib
import os
import re

SHA = r'[a-f0-9]{64}'


class DevelopmentRetirementPlans:
    def __init__(self, archives):
        self.archives, self.d = archives, archives.d

    def reject(self, code):
        self.d.reject('RETIREMENT_' + code)

    def validate(self, value):
        if (not isinstance(value, dict) or set(value) != {'schemaVersion', 'storage',
                'expectedEpoch', 'archiveId', 'archivePlanSha256', 'captureReceiptSha256'}
                or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1
                or type(value['expectedEpoch']) is not int
                or not 2 <= value['expectedEpoch'] < 0x7ffffffffffffffe or value['expectedEpoch'] % 2):
            self.reject('REQUEST_INVALID')
        for name in ('archivePlanSha256', 'captureReceiptSha256'):
            if not isinstance(value[name], str) or not re.fullmatch(SHA, value[name]):
                self.reject('REQUEST_INVALID')
        self.archives.operation(value['archiveId'])
        self.d.validate_request(value['storage'])

    def plan(self, value):
        self.validate(value)
        a, d = self.archives, self.d
        archive_request = dict(storage=value['storage'], expectedEpoch=value['expectedEpoch'],
            operationId=value['archiveId'], planSha256=value['archivePlanSha256'])
        # Match the capture lock order: NVMe first, then HDD. Exclusive NVMe
        # waits out source/Runner holders; nonblocking refusal is not a plan.
        with a.source(value['storage'], value['expectedEpoch'], exclusive=True) as (adapter, command, manifest, proof, payload):
            with a.target.locked(exclusive=False) as (target, hdd):
                if os.fstat(payload).st_dev == os.fstat(target).st_dev:
                    self.reject('SEPARATE_STORAGE_REQUIRED')
                # Keep this outer HDD lock throughout the independent full
                # re-read and source comparison. verify takes another SH lock;
                # no source lock is acquired inside it and no lock is upgraded.
                verified = a.verify(archive_request, value['captureReceiptSha256'])
                facts = a.facts(value['storage'], value['expectedEpoch'], manifest, proof, hdd)
                if any(verified.get(key) != expected for key, expected in facts.items()):
                    self.reject('SOURCE_CHANGED')
                a.manifest_type(adapter, max_bytes=16 * 1024**3).verify(command, manifest['manifestSha256'])
                a.require_metadata(payload, manifest)
                a.target.recheck(target, hdd)
                plan = dict(schemaVersion=1, purpose='whaledeck-development-source-retirement-plan-v1',
                    storage=value['storage'], expectedEpoch=value['expectedEpoch'],
                    storageProofSha256=facts['storageProofSha256'], sourceVersion=facts['sourceVersion'],
                    sourceManifestSha256=facts['sourceManifestSha256'], entryCount=facts['entryCount'],
                    totalBytes=facts['totalBytes'], archiveId=value['archiveId'],
                    archivePlanSha256=value['archivePlanSha256'], captureReceiptSha256=value['captureReceiptSha256'],
                    archiveFilesystemUuid=verified['archiveFilesystemUuid'], hddProofSha256=verified['hddProofSha256'],
                    archiveSha256=verified['archiveSha256'], archiveIndexSha256=verified['archiveIndexSha256'],
                    archiveBytes=verified['archiveBytes'], archiveInode=verified['archiveInode'],
                    retainsSource=True, reservesBudget=True, deletionAuthorized=False)
                result = dict(plan, retirementPlanSha256=hashlib.sha256(d.canonical(plan)).hexdigest())
        # Exiting source also rechecks the allocation, quota and namespace.
        # A return inside that context would still run those checks, but this
        # placement makes their completion explicit to future callers.
        return result
