"""Scope substitutions must fail before any execution control is reachable."""
import copy
import json
import unittest
from unittest.mock import Mock

from interrupt_qa_retirement_execution import check_scope, interrupt_locked

WORKSPACE = '11111111-1111-1111-1111-111111111111'
SUBJECT = '22222222-2222-2222-2222-222222222222'
RETIREMENT = '33333333-3333-3333-3333-333333333333'
TASK = '44444444-4444-4444-4444-444444444444'
ARCHIVE = '55555555-5555-5555-5555-555555555555'


class ScopeTests(unittest.TestCase):
    def fixture(self):
        slug = 'qa-native-'+'a'*24
        plan = dict(retirementPlanSha256='b'*64, archiveId=ARCHIVE)
        payload = dict(workspaceId=WORKSPACE, ownerSubject=SUBJECT, retirementId=RETIREMENT)
        state = dict(slug=slug, workspace=dict(id=WORKSPACE), ownerSubject=SUBJECT,
            retirementBusiness=dict(retirement=dict(retirementId=RETIREMENT, planJson=json.dumps(plan))))
        declaration = dict(slug=slug, originalWorkspaceIds=[])
        row = dict(slug=slug, workspaceId=WORKSPACE, ownerSubject=SUBJECT, retirementId=RETIREMENT,
            lifecycle='Retiring', finished=None, attention=False, attempt=0, recoveries=0, completions=0,
            planSha256=plan['retirementPlanSha256'], phase='plan', confirmed=None, executeStepId=None, settled=None)
        return payload, state, declaration, row

    def test_existing_workspace_wrong_owner_or_not_new_qa_rejected(self):
        for target, key, value in [('declaration', 'originalWorkspaceIds', [WORKSPACE]),
                ('row', 'ownerSubject', ARCHIVE), ('row', 'workspaceId', ARCHIVE),
                ('row', 'retirementId', ARCHIVE), ('declaration', 'slug', 'existing-workspace'),
                ('row', 'finished', 'done'), ('row', 'attention', True), ('row', 'recoveries', 1),
                ('row', 'completions', 1), ('row', 'attempt', 1), ('row', 'planSha256', 'c'*64)]:
            with self.subTest(target=target, key=key):
                args = self.fixture(); args[2 if target == 'declaration' else 3][key] = value
                with self.assertRaises(AssertionError): check_scope(*args)

    def test_confirmed_task_must_match_every_current_binding(self):
        args = self.fixture(); row = args[3]
        request = dict(taskId=TASK, retirementId=RETIREMENT, phase='execute', retirementPlanSha256='b'*64,
            prerequisite=dict(storage=dict(workspaceId=WORKSPACE), archiveId=ARCHIVE))
        row.update(phase='execute', confirmed='now', executeStepId=TASK, request=json.dumps(request))
        check_scope(*args, task=TASK)
        for key, value in [('taskId', ARCHIVE), ('retirementId', ARCHIVE), ('phase', 'plan'),
                ('retirementPlanSha256', 'c'*64)]:
            altered = copy.deepcopy(args); changed = dict(request, **{key: value})
            altered[3]['request'] = json.dumps(changed)
            with self.subTest(key=key), self.assertRaises(AssertionError): check_scope(*altered, task=TASK)
        for key, value in [('executeStepId', ARCHIVE), ('settled', 'already-done')]:
            altered = copy.deepcopy(args); altered[3][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): check_scope(*altered, task=TASK)

    def test_evidence_failure_never_reaches_kill(self):
        from contextlib import nullcontext
        tasks = Mock(); tasks.entry.return_value = nullcontext(1)
        tasks.completed.return_value = None; tasks.cancelled.return_value = False
        proof = dict(bootId=SUBJECT, path='/system.slice/whaledeck-development-retirement-run-'+TASK.replace('-', '')+'.service')
        tasks.proof.return_value = proof; tasks.execution.boot.return_value = SUBJECT
        tasks.execution.settled.return_value = False; tasks.d.canonical.return_value = b'{}'
        with self.assertRaises(OSError):
            interrupt_locked(tasks, dict(taskId=TASK), Mock(side_effect=OSError('Evidence unavailable')))
        self.assertEqual(tasks.execution.settled.call_args_list[0].kwargs, {})
        self.assertEqual(tasks.execution.settled.call_count, 1)

    def test_wrong_task_cgroup_boot_or_finished_task_never_killed(self):
        from contextlib import nullcontext
        for fault in ('path', 'boot', 'result', 'cancel', 'gone'):
            tasks = Mock(); tasks.entry.return_value = nullcontext(1)
            tasks.completed.return_value = {} if fault == 'result' else None
            tasks.cancelled.return_value = fault == 'cancel'
            tasks.execution.boot.return_value = SUBJECT
            tasks.execution.settled.return_value = fault == 'gone'
            tasks.proof.return_value = dict(bootId=ARCHIVE if fault == 'boot' else SUBJECT,
                path='/system.slice/'+('other.service' if fault == 'path' else
                    'whaledeck-development-retirement-run-'+TASK.replace('-', '')+'.service'))
            with self.subTest(fault=fault), self.assertRaises(AssertionError):
                interrupt_locked(tasks, dict(taskId=TASK), Mock())
            self.assertFalse(any(call.kwargs.get('stop') for call in tasks.execution.settled.call_args_list))
