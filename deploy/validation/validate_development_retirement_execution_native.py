"""Opt-in retirement execution in the archive probe's disposable filesystems.

Only newly allocated QA UUIDs are retired. Real kernel project accounting,
open-unlinked inodes and process death are exercised without quota doubles.
The owning probe controls physical-device attestation and exact teardown.
"""
import hashlib
import os
import signal
import uuid
from unittest.mock import patch


def snapshot(root):
    return {str(p.relative_to(root)): dict(identity=[p.lstat().st_dev, p.lstat().st_ino,
        p.lstat().st_mode, p.lstat().st_uid, p.lstat().st_gid, p.lstat().st_size, p.lstat().st_mtime_ns],
        sha256=hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None)
        for p in [root, *sorted(root.rglob('*'))]}


def exercise(probe, driver, engine, request, evidence, payload):
    sources = probe.Path(__file__).resolve().parents[1] / 'host'
    execution = probe.load(sources, 'development-retirement-execution')
    retirement = probe.load(sources, 'development-retirement')
    lifecycle = probe.load(sources, 'development-lifecycle')
    archive_module = probe.load(sources, 'development-archives')
    executor = execution.DevelopmentRetirementExecution(engine, archive_module.PayloadFiles)
    planner = retirement.DevelopmentRetirementPlans(engine)
    root = payload.parent.parent
    hdd = probe.Path(engine.target.declaration()['mountPoint'])
    original = probe.physical(driver, driver.quota, request, payload)
    results = []

    def refused(action, expected):
        try:
            action()
        except (driver.files.Rejected, driver.quota.QuotaError) as error:
            assert str(error) == expected, str(error)
        else:
            raise AssertionError('INVALID_NATIVE_RETIREMENT_EXECUTION_ACCEPTED')

    def killed(action):
        child = os.fork()
        if child == 0:
            try:
                signal.alarm(30)
                action()
            finally:
                os._exit(4)
        _, status = os.waitpid(child, os.WUNTRACED)
        assert os.WIFSTOPPED(status), 'RETIREMENT_CUT_NOT_REACHED'
        os.kill(child, signal.SIGKILL)
        os.waitpid(child, 0)

    cuts = ('none', 'before-prepared', 'after-detach', 'after-unlink',
            'open-unlinked', 'after-quota-clear', 'after-release-checkpoint')
    for cut in cuts:
        other = dict(request, workspaceId=str(uuid.uuid4()))
        driver.allocate(other)
        allocation = root / other['workspaceId']
        source = allocation / 'data' / '源码'; source.mkdir(mode=0o700)
        selected = source / '已归档.bin'
        selected.write_bytes(bytes(range(256)) * 4096)
        selected.chmod(0o640); os.chown(selected, 65534, 65534)
        (source / '.env').write_bytes(b'disposable-private-fixture-only')
        (source / 'empty').mkdir(mode=0o710)
        transition = dict(storage=other, action='stop', expectedEpoch=1, operationId='',
            planSha256='', storageProofSha256='')
        manager = lifecycle.Lifecycle(driver); stopped = manager.run(transition)
        manager.run(dict(transition, operationId=str(uuid.uuid4()), planSha256=stopped['planSha256'],
            storageProofSha256=stopped['storageProofSha256']), apply=True)
        plan = engine.plan(other, 2)
        archive = dict(storage=other, expectedEpoch=2, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'])
        receipt = engine.capture(archive)
        prerequisite = dict(schemaVersion=1, storage=other, expectedEpoch=2, archiveId=archive['operationId'],
            archivePlanSha256=archive['planSha256'], captureReceiptSha256=receipt['receiptSha256'])
        value = dict(schemaVersion=1, retirementId=str(uuid.uuid4()), prerequisite=prerequisite,
            retirementPlanSha256=planner.plan(prerequisite)['retirementPlanSha256'])
        before = probe.physical(driver, driver.quota, other, allocation / 'data')
        project = before['proof']['projectId']
        journal = root / ('.retirement-' + other['workspaceId'])
        claim = root / ('.quota-' + str(project) + '.json')
        original_claim = claim.read_bytes(), claim.stat().st_ino
        hdd_before = snapshot(hdd)
        run = lambda: executor.run(value, planner)
        if cut == 'none':
            refused(lambda: executor.run(dict(value, retirementPlanSha256='f' * 64), planner),
                'WORKSPACE_STORAGE_RETIREMENT_PLAN_CHANGED')
            assert not journal.exists() and selected.exists()
        elif cut in ('before-prepared', 'after-release-checkpoint'):
            write = executor.write
            def interrupted(parent, name, data):
                if cut == 'before-prepared' and name == 'prepared.json':
                    os.kill(os.getpid(), signal.SIGSTOP)
                write(parent, name, data)
                if cut == 'after-release-checkpoint' and name == 'quota-released.json':
                    os.kill(os.getpid(), signal.SIGSTOP)
            with patch.object(executor, 'write', side_effect=interrupted):
                killed(run)
        elif cut == 'after-detach':
            exchange = driver.files.exchange
            def interrupted(parent, name, target, leaf, flags):
                result = exchange(parent, name, target, leaf, flags)
                if name == other['workspaceId'] and leaf == 'source':
                    os.kill(os.getpid(), signal.SIGSTOP)
                return result
            with patch.object(driver.files, 'exchange', side_effect=interrupted):
                killed(run)
        elif cut == 'after-unlink':
            unlink = os.unlink
            def interrupted(name, *args, **kwargs):
                result = unlink(name, *args, **kwargs)
                if name == '已归档.bin':
                    os.kill(os.getpid(), signal.SIGSTOP)
                return result
            with patch.object(os, 'unlink', side_effect=interrupted):
                killed(run)
        elif cut == 'after-quota-clear':
            release = driver.quota.release_empty
            def interrupted(*args):
                release(*args)
                os.kill(os.getpid(), signal.SIGSTOP)
            with patch.object(driver.quota, 'release_empty', side_effect=interrupted):
                killed(run)
        elif cut == 'open-unlinked':
            fd = os.open(selected, os.O_RDONLY | os.O_NOFOLLOW)
            try:
                refused(run, 'QUOTA_STILL_REFERENCED')
                assert not allocation.exists() and not (journal / 'source').exists()
                assert not (journal / 'receipt.json').exists()
                assert not (journal / 'quota-released.json').exists()
                assert original_claim == (claim.read_bytes(), claim.stat().st_ino)
                with driver.locked_root(other['rootId'], exclusive=False) as (parent, _):
                    pinned = driver.quota.read_quota(parent, project)
                    assert pinned[2] > 0 and pinned[5] > 0
                    assert driver.quota.matches_limits(pinned, driver.quota.limits(project, other['diskMiB']))
                assert os.read(fd, 256) == bytes(range(256))
                probe.save(evidence / 'retirement-open-unlinked.json', dict(projectId=project,
                    quotaWhileFdOpen=list(pinned), completionAbsent=True, claimPreserved=True))
            finally:
                os.close(fd)
        if cut not in ('none', 'open-unlinked'):
            assert journal.is_dir() and not (journal / 'receipt.json').exists()
            refused(lambda: driver.allocate(other), 'WORKSPACE_STORAGE_RETIRED')
            # Before prepared.json exists, the caller must supply its original
            # authorized request; after publication it is immutably bound.
            if cut != 'before-prepared':
                refused(lambda: executor.run(dict(value, retirementId=str(uuid.uuid4())), planner),
                    'WORKSPACE_STORAGE_RETIREMENT_REQUEST_CHANGED')
        result = run()
        assert result == run() and result['sourceRemoved'] and result['quotaReleased'] and result['claimReleased']
        assert result['retainsHdd'] and not result['releasesApplicationBudget']
        assert not allocation.exists() and not (journal / 'source').exists() and not claim.exists()
        with driver.locked_root(other['rootId'], exclusive=False) as (parent, _):
            driver.quota.require_unused(parent, project)
            after_quota = list(driver.quota.read_quota(parent, project))
        assert after_quota == [0] * 8
        assert hdd_before == snapshot(hdd)
        assert receipt == engine.verify(archive, receipt['receiptSha256'])
        refused(lambda: driver.allocate(other), 'WORKSPACE_STORAGE_RETIRED')
        refused(lambda: planner.plan(prerequisite), 'WORKSPACE_STORAGE_RETIRED')
        resume = dict(transition, action='resume', expectedEpoch=2)
        refused(lambda: manager.run(resume), 'WORKSPACE_STORAGE_RETIRED')
        assert (journal / 'prepared.json').is_file() and (journal / 'receipt.json').is_file()
        assert (root / ('.lifecycle-' + other['workspaceId'] + '.json')).exists()
        row = dict(cut=cut, request=value, before=before, receipt=result, kernelQuotaAfter=after_quota,
            hddUnchanged=True, archiveIndependentlyVerified=True, permanentFenceVerified=True)
        results.append(row)
        probe.save(evidence / 'retirement-executions.json', results)
        print(probe.json.dumps(dict(phase='isolated-retirement-execution', cut=cut, passed=True)), flush=True)
    assert original == probe.physical(driver, driver.quota, request, payload)
    proof = dict(passed=True, realKernelQuota=True, retiredNewQaAllocations=len(cuts),
        actualProcessKills=5, openUnlinkedPreventsCompletion=True, kernelLimitsAndUsageZero=True,
        sourceRemovedOnlyWithRetainedMatchingHdd=True, permanentFences=True, originalSourceAndQuotaPreserved=True,
        productionWorkspaceMutations=0, releasesApplicationBudget=False,
        scope='Internal candidate on new disposable allocations only; no installed helper/RPC or user retirement')
    probe.save(evidence / 'retirement-execution-native.json', proof)
    return proof
