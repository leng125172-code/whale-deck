"""Offline process tests; no dependency downloads or registry access."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'deploy/ci/install-frontend-dependencies.sh'


@unittest.skipUnless(os.name == 'posix', 'Linux CI shell process test')
class FrontendDependencyInstallTests(unittest.TestCase):
    def execute(self, mode):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            corepack = folder / 'corepack'
            corepack.write_text('''#!/bin/sh
test "$*" = "pnpm install --frozen-lockfile" || exit 99
count=0
if test -f "$QA_STATE"; then count=$(cat "$QA_STATE"); fi
count=$((count+1))
printf '%s' "$count" > "$QA_STATE"
case "$QA_MODE" in
  success) exit 0 ;;
  timeout-then-success) if test "$count" -ge 2; then exit 0; fi ;;
  permanent) printf '%s\\n' 'ERR_PNPM_OUTDATED_LOCKFILE'; exit 17 ;;
  audit) printf '%s\\n' 'Security audit failed'; exit 18 ;;
esac
printf '%s\\n' 'Could not download the pnpm 12.10.1 binary: Could not reach https://registry.npmjs.org/@pnpm/exe.linux-x64/12.10.1: timed out after 30s'
exit 23
''', encoding='utf-8'); corepack.chmod(0o700)
            sleeper = folder / 'sleep'
            sleeper.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$QA_SLEEPS"\n', encoding='utf-8'); sleeper.chmod(0o700)
            result = subprocess.run(['bash', str(SCRIPT)], cwd=folder,
                env={**os.environ, 'PATH': str(folder)+os.pathsep+os.environ['PATH'],
                    'QA_MODE': mode, 'QA_STATE': str(folder/'count'), 'QA_SLEEPS': str(folder/'sleeps')},
                capture_output=True, text=True, timeout=10)
            count = int((folder/'count').read_text())
            sleeps = (folder/'sleeps').read_text().splitlines() if (folder/'sleeps').exists() else []
            return result, count, sleeps

    def test_success_runs_once_without_delay(self):
        result, count, sleeps = self.execute('success')
        self.assertEqual((0, 1, []), (result.returncode, count, sleeps))

    def test_observed_download_timeout_retries_but_success_stops(self):
        result, count, sleeps = self.execute('timeout-then-success')
        self.assertEqual((0, 2, ['5']), (result.returncode, count, sleeps))

    def test_download_timeout_has_three_attempts_and_preserves_failure_status(self):
        result, count, sleeps = self.execute('timeout')
        self.assertEqual((23, 3, ['5', '10']), (result.returncode, count, sleeps))

    def test_lockfile_and_audit_failures_are_not_retried_or_weakened(self):
        for mode, status in (('permanent', 17), ('audit', 18)):
            with self.subTest(mode=mode):
                result, count, sleeps = self.execute(mode)
                self.assertEqual((status, 1, []), (result.returncode, count, sleeps))


if __name__ == '__main__': unittest.main()
