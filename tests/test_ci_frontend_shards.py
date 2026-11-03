"""Offline gate/coverage/process checks. No browsers, installs or external requests."""
import hashlib
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
CI = ROOT/'deploy/ci'

def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, CI/filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

RUNNER = load('ci_shards_test_runner', 'run-frontend-layout-shard.py')
VERIFY = load('ci_shards_test_verifier', 'verify-frontend-layout-shards.py')

class ShardCoverageTests(unittest.TestCase):
    def test_original_four_commands_remain_covered_without_duplicate_files(self):
        raw = (CI/'frontend-layout-baseline.json').read_bytes()
        self.assertEqual('ac12a305920037fa6a18a12a27afce684c2706affc1e5c7280abeb06389db407', hashlib.sha256(raw).hexdigest())
        baseline = json.loads(raw)
        self.assertEqual('28062da454fd579d6860391a88816dab0bfca143', baseline['sourceRevision'])
        original = [name for command in baseline['commands'] for name in re.findall(r'e2e/[a-z0-9.-]+\.spec\.ts', command)]
        self.assertEqual(41, len(original))
        values = RUNNER.suites()
        self.assertEqual(len(values), len(set(values)))
        self.assertTrue(set(original) <= set(values))

    def test_manifest_rejects_paths_duplicates_missing_files_wrong_shapes_and_flags(self):
        valid = json.loads(RUNNER.MANIFEST.read_bytes())
        cases = [[], {**valid, 'schemaVersion':True}, {**valid, 'shards':True}, {**valid, 'shards':5}, {**valid, 'suites':[]},
                 {**valid, 'suites':['../e2e/files.spec.ts']}, {**valid, 'suites':['--list']},
                 {**valid, 'suites':['e2e/missing-unreviewed.spec.ts']}, {**valid, 'suites':[valid['suites'][0]]*2},
                 {**valid, 'extra':'untrusted'}]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'manifest.json'
            for value in cases:
                path.write_text(json.dumps(value))
                with self.subTest(value=value), self.assertRaises(ValueError): RUNNER.suites(path)
            path.write_text('{"schemaVersion":2,"schemaVersion":1,"shards":6,"suites":[]}')
            with self.assertRaises(ValueError): RUNNER.suites(path)

    def test_six_shards_require_disjoint_complete_test_ids(self):
        full = set(range(12)); parts = [set(range(n,12,6)) for n in range(6)]
        VERIFY.prove(full,parts)
        for values in (parts[:5], [*parts[:5],set()], [*parts[:5],parts[0]], [*parts[:5],{5}], [*parts[:5],parts[5]|{99}]):
            with self.subTest(parts=values), self.assertRaises(ValueError): VERIFY.prove(full,values)

    def test_report_collection_includes_nested_parameterized_projects_and_rejects_empty_or_duplicates(self):
        spec = {'id':'case','tests':[{'projectId':'a'},{'projectId':'b'}]}
        report = {'suites':[{'suites':[{'specs':[spec]}]}]}
        self.assertEqual({('case','a'),('case','b')}, VERIFY.test_ids(report))
        for value in ({}, {'specs':[spec,spec]}):
            with self.assertRaises(ValueError): VERIFY.test_ids(value)

class ShardProcessTests(unittest.TestCase):
    def test_each_command_preserves_all_files_and_blocks_only_tests(self):
        for index in range(1,7):
            command = RUNNER.command(str(index))
            self.assertEqual(['node',str(RUNNER.WEB/'node_modules/@playwright/test/cli.js'),'test'], command[:3])
            self.assertEqual([re.escape(v)+'$' for v in RUNNER.suites()], command[3:-3])
            self.assertIn('--shard='+str(index)+'/6',command)
            self.assertIn('--forbid-only',command)
            self.assertIn('--reporter=line',command)
            self.assertNotIn('--list',command)
            self.assertIn('--list',RUNNER.command(str(index),True))
        for index in ('0','7','01','1/6','1 --grep unsafe',1,True):
            with self.subTest(index=index), self.assertRaises(ValueError): RUNNER.command(index)

    def test_invalid_cli_and_live_urls_never_spawn_a_command(self):
        with patch.object(RUNNER.subprocess,'run') as execute, patch.object(Path,'cwd',return_value=RUNNER.WEB):
            for args in ([],['1','--grep'],['1','--list','extra']):
                with self.assertRaises(ValueError): RUNNER.main(args)
            with patch.dict(os.environ, {'WHALEDECK_PRODUCTION_PREVIEW_URL':'http://127.0.0.1:8080'}):
                with self.assertRaises(ValueError): RUNNER.main(['1'])
            execute.assert_not_called()

    def test_a_failed_browser_shard_is_not_retried_or_converted_to_success(self):
        with patch.object(Path,'cwd',return_value=RUNNER.WEB), patch.dict(os.environ, {'WHALEDECK_PRODUCTION_PREVIEW_URL':RUNNER.PREVIEW}):
            for code,expected in ((0,0),(1,1),(17,17),(-15,143)):
                with patch.object(RUNNER.subprocess,'run',return_value=SimpleNamespace(returncode=code)) as execute:
                    self.assertEqual(expected,RUNNER.main(['1']))
                    execute.assert_called_once()
                    self.assertEqual(RUNNER.PREVIEW,execute.call_args.kwargs['env']['WHALEDECK_PRODUCTION_PREVIEW_URL'])

class FrontendAggregateTests(unittest.TestCase):
    def test_only_both_success_results_pass_the_frontend_gate(self):
        for first,second in itertools.product(('success','failure','cancelled','skipped',''), repeat=2):
            result = subprocess.run(['bash',str(CI/'require-frontend-success.sh')],
                env={**os.environ,'FRONTEND_CHECKS_RESULT':first,'FRONTEND_BROWSER_RESULT':second},capture_output=True,timeout=5)
            self.assertEqual(0 if first==second=='success' else 1,result.returncode)

    def test_workflow_has_all_shards_no_fail_fast_and_unconditional_aggregate(self):
        workflow=(ROOT/'.github/workflows/ci.yml').read_text()
        checks=workflow.split('  frontend_checks:\n',1)[1].split('  frontend_browser:\n',1)[0]
        browsers=workflow.split('  frontend_browser:\n',1)[1].split('  frontend:\n',1)[0]
        aggregate=workflow.split('  frontend:\n',1)[1]
        self.assertIn('fail-fast: false',browsers)
        self.assertIn('shard: [1, 2, 3, 4, 5, 6]',browsers)
        self.assertEqual(1,browsers.count('    runs-on:'))
        self.assertIn('needs: frontend_checks',browsers)
        self.assertIn('pnpm build',browsers)
        self.assertIn('playwright install chromium --with-deps',browsers)
        self.assertIn('run-frontend-layout-shard.py ${{ matrix.shard }}',browsers)
        self.assertIn('verify-frontend-layout-shards.py',checks)
        self.assertIn('if: ${{ always() }}',aggregate)
        self.assertIn('needs: [frontend_checks, frontend_browser]',aggregate)
        self.assertIn('require-frontend-success.sh',aggregate)
        self.assertIn('FRONTEND_CHECKS_RESULT: ${{ needs.frontend_checks.result }}',aggregate)
        self.assertIn('FRONTEND_BROWSER_RESULT: ${{ needs.frontend_browser.result }}',aggregate)
        self.assertNotIn('continue-on-error', checks+browsers+aggregate)
        self.assertNotIn('cancel-in-progress: true',workflow)
        self.assertEqual(3,(checks+browsers+aggregate).count('ref: ${{ github.sha }}'))
