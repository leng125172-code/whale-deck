"""Offline documentation consistency checks for the user-approved scope.

These prove task bookkeeping only, never product implementation/acceptance.
When the user changes scope again, revise the explicit decision and this gate
together; do not silently restore removed tasks or count removals as completed.
"""
import json
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
REMOVED = {
    'P2-09', 'P3-06', 'P3-08', 'P3-10', 'P3-11',
    'P4-01', 'P4-02', 'P4-03', 'P4-04', 'P4-09',
    'P5-01', 'P5-02', 'P5-03', 'P5-04', 'P5-06', 'P5-07', 'P5-08',
}
ORIGINAL = {f'P{stage}-{number:02d}' for stage, total in
    ((0, 10), (1, 12), (2, 12), (3, 18), (4, 12), (5, 12))
    for number in range(1, total + 1)}


def task_rows(text):
    rows = []
    for line in text.splitlines():
        if re.match(r'^\| P[0-5]-\d\d \|', line):
            cells = [cell.strip() for cell in line.split('|')[1:-1]]
            if len(cells) != 4: raise ValueError('SCOPE_TASK_ROW_COLUMNS')
            rows.append(cells)
    return rows


class SingleWorkstationScopeTests(unittest.TestCase):
    def setUp(self):
        self.goal = (ROOT / 'docs/GOAL_P0_P5.md').read_text(encoding='utf-8')
        self.decision = (ROOT / 'docs/SCOPE_SINGLE_WORKSTATION.md').read_text(encoding='utf-8')
        self.rows = task_rows(self.goal)
        self.ids = [row[0] for row in self.rows]

    def test_explicit_removed_and_retained_ids_partition_original_76(self):
        self.assertEqual(76, len(ORIGINAL)); self.assertEqual(17, len(REMOVED))
        self.assertEqual(59, len(self.ids)); self.assertEqual(len(self.ids), len(set(self.ids)))
        self.assertEqual(ORIGINAL - REMOVED, set(self.ids))
        self.assertFalse(set(self.ids) & REMOVED)

    def test_all_17_removals_have_recorded_reason_not_completed_status(self):
        removed_rows = [line for line in self.decision.splitlines() if re.match(r'^\| P[0-5]-\d\d \|', line)]
        self.assertEqual(17, len(removed_rows))
        self.assertEqual(REMOVED, {line.split('|')[1].strip() for line in removed_rows})
        for line in removed_rows:
            cells = [cell.strip() for cell in line.split('|')[1:-1]]
            self.assertEqual(3, len(cells)); self.assertGreater(len(cells[2]), 10)
        self.assertIn('删项不是新增完成', self.decision)

    def test_six_development_workspace_tasks_and_core_safety_are_retained(self):
        self.assertTrue({f'P3-{n}' for n in range(13, 19)} <= set(self.ids))
        self.assertTrue({f'P0-{n:02d}' for n in range(1, 11)} <= set(self.ids))
        self.assertTrue({'P1-11', 'P2-03', 'P2-04', 'P2-07', 'P2-08', 'P2-10', 'P2-11'} <= set(self.ids))
        for phrase in ('2小时权限复核', 'CSRF/Origin', '用户/空间隔离', '备份恢复', '不挂Docker Socket'):
            self.assertTrue(phrase in self.decision, 'Missing safety invariant: ' + phrase)

    def test_all_active_tasks_keep_explicit_state_and_acceptance_requirement(self):
        for identifier, title, state, evidence in self.rows:
            with self.subTest(identifier=identifier):
                self.assertTrue(title); self.assertGreater(len(evidence), 15)
                self.assertIn(state, {'部分实现', '部分实现、待验收', '待实现', '待验收', '已验证', '外部启用待条件'})
        self.assertIn('当前59项', self.goal)

    def test_major_design_entry_points_link_current_scope(self):
        for relative in ('README.md', 'docs/README.md', 'docs/FEATURES.md', 'docs/ROADMAP.md',
                'docs/PAGES.md', 'docs/USE_CASES.md', 'docs/AGENT_RPC.md', 'docs/PERMISSIONS.md',
                'docs/DATA_MODEL.md', 'docs/DEVELOPMENT_WORKSPACES.md', 'docs/IMPLEMENTATION_STATUS.md'):
            with self.subTest(document=relative):
                path = ROOT / relative; text = path.read_text(encoding='utf-8')
                links = re.findall(r'\]\(([^)]+SCOPE_SINGLE_WORKSTATION\.md|SCOPE_SINGLE_WORKSTATION\.md)\)', text)
                self.assertTrue(links)
                for link in links: self.assertEqual((ROOT / 'docs/SCOPE_SINGLE_WORKSTATION.md').resolve(), (path.parent / link).resolve())

    def test_metadata_does_not_require_removed_enterprise_platform(self):
        value = json.loads((ROOT / '.project-atlas.json').read_text(encoding='utf-8'))
        self.assertEqual('whale-deck', value['id']); self.assertEqual('active', value['status'])
        self.assertIn('59', value['summary']); self.assertNotIn('全部76项', value['nextStep'])
        self.assertIn('17项移出不算完成', value['nextStep'])

    def test_workspace_model_has_no_node_tenant_subscription_prerequisite(self):
        text = (ROOT / 'docs/DEVELOPMENT_WORKSPACES.md').read_text(encoding='utf-8')
        row = next(line for line in text.splitlines() if line.startswith('| DM-DEV-WORKSPACE |'))
        self.assertIn('ownerSubject', row); self.assertIn('resourceLimits', row)
        self.assertNotIn('可选 project/node', row)
        self.assertIn('无tenant/node/套餐外键', row)
        self.assertNotIn('P5 接入团队', text)

    def test_task_table_parser_rejects_silent_extra_columns(self):
        with self.assertRaisesRegex(ValueError, 'SCOPE_TASK_ROW_COLUMNS'):
            task_rows('| P0-01 | title | state | evidence | extra |')


if __name__ == '__main__': unittest.main()
