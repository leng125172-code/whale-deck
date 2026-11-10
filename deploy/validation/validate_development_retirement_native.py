"""Native retirement-plan checks inside the archive probe's disposable images.

Called only by validate_development_archives_native.py after its physical
NVMe/HDD and inode-bound image setup. Never relocate product configuration.
The one extra workspace, its corrupt archive and all fault evidence stay in
these disposable mounts until the owning probe performs its precise cleanup.
"""
import hashlib
import uuid


def command(storage, archive, receipt):
    return dict(schemaVersion=1, storage=storage, expectedEpoch=archive['expectedEpoch'],
        archiveId=archive['operationId'], archivePlanSha256=archive['planSha256'],
        captureReceiptSha256=receipt['receiptSha256'])


def exercise(probe, driver, engine, request, archive, receipt, evidence, payload):
    retirement = probe.load(probe.Path(__file__).resolve().parents[1] / 'host', 'development-retirement')
    planner = retirement.DevelopmentRetirementPlans(engine)
    value = command(request, archive, receipt)
    before = probe.physical(driver, driver.quota, request, payload)
    original = planner.plan(value)
    assert original == planner.plan(value)
    assert not original['deletionAuthorized'] and original['retainsSource'] and original['reservesBudget']
    assert before == probe.physical(driver, driver.quota, request, payload)
    def refused(value, expected):
        try: planner.plan(value)
        except driver.files.Rejected as error:
            assert str(error) == expected, str(error)
        else: raise AssertionError('INVALID_NATIVE_RETIREMENT_PLAN_ACCEPTED')
    refused(dict(value, captureReceiptSha256='f' * 64), 'WORKSPACE_STORAGE_ARCHIVE_RECEIPT_CHANGED')
    refused(dict(value, expectedEpoch=4), 'WORKSPACE_STORAGE_ARCHIVE_NOT_STOPPED')
    with driver.attested_payload(request):
        refused(value, 'WORKSPACE_STORAGE_BUSY')
    with engine.target.locked(exclusive=True):
        refused(value, 'WORKSPACE_STORAGE_ARCHIVE_BUSY')
    assert original == planner.plan(value)
    assert before == probe.physical(driver, driver.quota, request, payload)
    # Use a different O_EXCL-created workspace for irreversible edits. The
    # original proof source and the configured five product spaces stay intact.
    other = dict(request, workspaceId=str(uuid.uuid4()))
    driver.allocate(other)
    root = payload.parent.parent
    extra_payload = root / other['workspaceId'] / 'data'
    source = extra_payload / 'unarchived-edit.bin'; source.write_bytes(b'old disposable source')
    lifecycle = probe.load(probe.Path(__file__).resolve().parents[1] / 'host', 'development-lifecycle')
    transition = dict(storage=other, action='stop', expectedEpoch=1, operationId='', planSha256='', storageProofSha256='')
    manager = lifecycle.Lifecycle(driver); planned = manager.run(transition)
    manager.run(dict(transition, operationId=str(uuid.uuid4()), planSha256=planned['planSha256'],
        storageProofSha256=planned['storageProofSha256']), apply=True)
    plan = engine.plan(other, 2)
    extra_archive = dict(storage=other, expectedEpoch=2, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'])
    extra_receipt = engine.capture(extra_archive)
    extra_value = command(other, extra_archive, extra_receipt)
    assert planner.plan(extra_value)['sourceManifestSha256'] == extra_receipt['sourceManifestSha256']
    source.write_bytes(b'new disposable source MUST remain')
    refused(extra_value, 'WORKSPACE_STORAGE_RETIREMENT_SOURCE_CHANGED')
    assert source.read_bytes() == b'new disposable source MUST remain'
    # New capture of the changed source now passes. Then corrupt real HDD
    # bytes to prove the old successful Verify is not accepted as a cache.
    plan = engine.plan(other, 2)
    fresh = dict(extra_archive, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'])
    fresh_receipt = engine.capture(fresh)
    fresh_value = command(other, fresh, fresh_receipt)
    assert planner.plan(fresh_value)['archiveSha256'] == fresh_receipt['archiveSha256']
    hdd = probe.Path(engine.target.declaration()['mountPoint'])
    archive_path = hdd / 'workspace-archives-v1' / fresh['operationId'] / 'payload.tar.gz'
    intact = archive_path.read_bytes()
    # The disposable corrupted object is intentionally retained until teardown.
    with archive_path.open('r+b') as stream:
        stream.truncate(len(intact) - 10); stream.flush(); probe.os.fsync(stream.fileno())
    try: planner.plan(fresh_value)
    except driver.files.Rejected: pass
    else: raise AssertionError('CORRUPT_NATIVE_HDD_ACCEPTED_FOR_RETIREMENT')
    assert source.read_bytes() == b'new disposable source MUST remain'
    assert before == probe.physical(driver, driver.quota, request, payload)
    proof = dict(passed=True, matchingPlan=original, realSourceReaderAndHddWriterLocksRefused=True,
        wrongSavedDigestAndEpochRefused=True, editedSourceRefused=True,
        newCaptureRequiredAfterEdit=True, corruptedHddReReadAndRefused=True,
        originalSourceAndQuotaPreserved=True, readOnlyPlansPreserveSourceAndQuota=True,
        extraFixtureAllocations=1, extraFixtureCaptures=2,
        extraDisposableWorkspace=other['workspaceId'], retainedEditedSourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        extraArchiveRequests=[extra_archive, fresh], scope='Read-only internal plan; no deletion authorization or budget release')
    probe.save(evidence / 'retirement-plan-native.json', proof)
    return planner, value, original, proof
