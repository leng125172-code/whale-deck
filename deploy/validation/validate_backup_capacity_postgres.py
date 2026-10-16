#!/usr/bin/env python3
"""Real PostgreSQL alert persistence with synthetic capacity in one isolated database.

No production disk filling or Agent mutation. Connection stays in a temporary
0600 env file; SDK gets no Docker/Agent socket or production data mount.
"""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import tarfile
import uuid
import xml.etree.ElementTree as ET
from run_with_ephemeral_admin import container_environment
from native_validation_status import NativeValidationStatus


def command(arguments, timeout=60, input=None):
    return subprocess.run(arguments, input=input, check=True, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, timeout=timeout).stdout


def main(suite='capacity'):
    repository = Path(__file__).resolve().parents[2]
    source_sha = command(['git', '-C', str(repository), 'rev-parse', 'HEAD']).strip()
    report = NativeValidationStatus(repository / 'deploy/artifacts/native-status', suite, source_sha)
    print(json.dumps({'nativeStatusFile': str(report.path.relative_to(repository)),
        'sourceSha': source_sha}), flush=True)
    try:
        run_native(suite, report)
        report.update(state='Passed', finishedAtUtc=report.now())
    except BaseException as error:
        report.update(state='Failed', failureType=type(error).__name__, finishedAtUtc=report.now())
        raise


def run_native(suite, report):
    if suite not in ('capacity', 'metrics', 'alerts', 'notifications', 'dispatch', 'snapshots'):
        raise ValueError('Unknown isolated native suite')
    repository = Path(__file__).resolve().parents[2]
    database = 'wdv_' + suite + '_' + uuid.uuid4().hex[:16]
    container = 'whaledeck-native-' + suite + '-' + uuid.uuid4().hex[:12]
    assert re.fullmatch(r'wdv_' + suite + r'_[a-f0-9]{16}', database)
    values = container_environment()
    connection = values['ConnectionStrings__WhaleDeck']
    connection, changed = re.subn(r'(?i)\bDatabase=[^;]*', 'Database=' + database, connection)
    assert changed == 1 and '\n' not in connection and '\r' not in connection
    sql_arguments = ['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres',
        'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'postgres']
    identity = None
    created = False
    try:
        command(sql_arguments, input=f'CREATE DATABASE "{database}" OWNER whaledeck;')
        created = True
        api_networks = json.loads(command(['docker', 'inspect', '--format', '{{json .NetworkSettings.Networks}}', 'whaledeck-api']))
        db_networks = json.loads(command(['docker', 'inspect', '--format', '{{json .NetworkSettings.Networks}}', 'database-platform-postgres']))
        common = sorted(set(api_networks) & set(db_networks))
        assert common, 'API and PostgreSQL have no shared internal network'
        version = json.loads((repository / 'global.json').read_text())['sdk']['version']
        assert re.fullmatch(r'10\.0\.[0-9]{3}', version)
        sdk = 'mcr.microsoft.com/dotnet/sdk:' + version
        try: command(['docker', 'image', 'inspect', sdk])
        except subprocess.CalledProcessError: command(['docker', 'pull', sdk], timeout=240)
        sdk_identity = command(['docker', 'image', 'inspect', '--format', '{{.Id}}', sdk]).strip()
        assert re.fullmatch(r'sha256:[a-f0-9]{64}', sdk_identity)
        artifact_root = repository / 'deploy/artifacts'
        artifact_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=suite + '-native-', dir=artifact_root) as scratch:
            scratch_path = Path(scratch).resolve()
            assert scratch_path.parent == artifact_root.resolve()
            runtime_directory = scratch_path / 'runtime'
            runtime_directory.mkdir(mode=0o700)
            archive = scratch_path / 'source.tar'
            command(['git', '-C', str(repository), 'archive', '--format=tar', '--output=' + str(archive), report.data['sourceSha']])
            public_source = scratch_path / 'public-source'
            public_source.mkdir()
            with tarfile.open(archive) as source:
                source.extractall(public_source, filter='data')
            environment = scratch_path / 'native.env'
            descriptor = os.open(environment, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, 'w') as stream:
                stream.write('WHALEDECK_NATIVE_TEST_CONNECTION=' + connection + '\n')
                if suite == 'metrics':
                    stream.write('WHALEDECK_METRIC_LOAD_VALIDATION=1\n')
            identity = command(['docker', 'create', '--name', container, '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--memory', '1536m', '--cpus', '2', '--pids-limit', '256',
                '--user', str(os.getuid()) + ':' + str(os.getgid()), '--tmpfs', '/tmp:rw,nosuid,nodev,size=256m',
                '--log-driver', 'local', '--log-opt', 'max-size=2m', '--log-opt', 'max-file=2',
                '--env-file', str(environment), '--env', 'DOTNET_CLI_HOME=/scratch/home', '--env', 'NUGET_PACKAGES=/scratch/packages',
                '--env', 'XDG_RUNTIME_DIR=/scratch/runtime',
                '--mount', 'type=bind,source=' + str(public_source) + ',target=/repo,readonly',
                '--mount', 'type=bind,source=' + scratch + ',target=/scratch', '--workdir', '/repo', sdk_identity,
                'dotnet', 'test', 'tests/WhaleDeck.IntegrationTests/WhaleDeck.IntegrationTests.csproj',
                '--configuration', 'Release', '--artifacts-path', '/scratch/build', '--logger', 'console;verbosity=normal',
                '--logger', 'trx;LogFileName=native.trx', '--results-directory', '/scratch/results', '--filter',
                'FullyQualifiedName~' + {'capacity': 'NativeBackupCapacityTests', 'metrics': 'NativeMetricAggregationTests', 'alerts': 'NativeMonitoringAlertsTests', 'notifications': 'NativeNotificationInboxTests', 'dispatch': 'NativeNotificationDispatchTests', 'snapshots': 'NativeNotificationSnapshotTests'}[suite]]).strip()
            assert re.fullmatch(r'[a-f0-9]{64}', identity)
            command(['docker', 'network', 'connect', common[0], identity])
            report.update(state='Running', stage='test', sdkImageId=sdk_identity, containerName=container)
            try:
                completed = subprocess.run(['docker', 'start', '-a', identity], check=False, text=True,
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=600)
            except subprocess.TimeoutExpired as error:
                partial = error.stdout or ''
                if isinstance(partial, bytes): partial = partial.decode('utf-8', errors='replace')
                phases = re.findall(r'INBOX_PHASE=([a-z-]+)', partial)
                # Public progress only: never print the captured exception/SQL/connection payload.
                progress = {'restoredProjects': partial.count('Restored '),
                    'testExecutionStarted': 'Starting test execution' in partial,
                    'inboxPhases': phases[-8:]}
                raise RuntimeError('Isolated native ' + suite + ' deadline expired; public progress=' + json.dumps(progress)) from None
            output = completed.stdout + completed.stderr
            status = command(['docker', 'inspect', '--format', '{{.State.ExitCode}}', identity]).strip()
            if status != '0':
                diagnostics = [line for line in output.splitlines() if re.search(r': error (?:CS|CA|IDE|NU|NETSDK|MSB)\d+', line)][:8]
                startup_failed = 'NuGet-Migrations' in output and 'System.Threading.Mutex' in output
                # Only stable exception/code tokens; never connection strings or native SQL values.
                error_types = sorted(set(re.findall(r'\b(?:[A-Za-z]+\.)*(?:PostgresException|InvalidOperationException|EqualException|TrueException|SingleException|NotNullException|NullException|InRangeException)\b', output)))
                sql_states = sorted(set(re.findall(r'\b([0-9A-Z]{5}):\s', output)))
                source_lines = re.findall(r'NativeNotification(?:Inbox|Dispatch|Snapshot)Tests(?:\.[A-Za-z0-9_-]{1,80})?\.cs:line ([0-9]+)', output)
                raise RuntimeError('Isolated native ' + suite + ' test failed: ' + ('\n'.join(diagnostics) if diagnostics else
                    'native failure types=' + ','.join(error_types) + ' sdkMutexStartupFailed=' + str(startup_failed) + ' SQLSTATE=' + ','.join(sql_states) +
                    ' inboxSourceLines=' + ','.join(source_lines[:8]) + '; private output withheld'))
            results = ET.parse(scratch_path / 'results/native.trx').getroot()
            namespace = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
            counters = results.find('t:ResultSummary/t:Counters', namespace)
            runs = results.findall('t:Results/t:UnitTestResult', namespace)
            assert counters is not None and all(counters.get(key) == '1' for key in ('total', 'executed', 'passed')), 'Native test execution/pass count differs'
            assert all(counters.get(key, '0') == '0' for key in ('failed', 'error', 'timeout', 'aborted', 'notExecuted', 'notRunnable', 'pending')), 'Native test was skipped/failed/incomplete'
            assert len(runs) == 1 and runs[0].get('outcome') == 'Passed', 'Native result is not one complete passed execution'
            report.update(executedTests=1, passedTests=1, stage='cleanup')
            if suite == 'capacity':
                print(json.dumps({'nativeBackupCapacityPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'capacitySamples': 'synthetic-not-production-disk-saturation', 'dedupEscalationSilenceExpiryUnknownRecoveryDisable': 'passed'}))
            elif suite == 'alerts':
                print(json.dumps({'nativeMonitoringAlertsPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'selectorQualityCoverageHysteresisEscalationSilenceRecoveryConcurrencyUniqueDisable': 'passed'}))
            elif suite == 'snapshots':
                print(json.dumps({'nativeNotificationSnapshotsPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'lateCommitExactSetScopeDowngradeExpirationReplacementMemberProgressReplayRetention': 'passed'}))
            elif suite == 'dispatch':
                print(json.dumps({'nativeNotificationDispatchPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'lateCommitAudienceFanoutDedupeConcurrentLeaseRestartUnknownSafeRetryVersionRetention': 'passed',
                    'externalDeliveries': 0}))
            elif suite == 'notifications':
                print(json.dumps({'nativeNotificationInboxPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'atomicSourcePrivacyAudienceDowngradeReadIdempotenceConcurrencyPaginationBatchSnapshotRetention': 'passed'}))
            else:
                benchmark = re.findall(r'METRIC_QUERY_BENCH_MS=([0-9]{1,6})', '\n'.join(results.itertext()))
                assert len(benchmark) == 1, 'Native metric load query was not observed'
                print(json.dumps({'nativeMetricAggregationPostgres': 'passed', 'database': 'isolated-and-migrated',
                    'upgradeBackfillRestartFirstLastWeightedQualityLateDirtyHistoryDownUp': 'passed',
                    'syntheticColdHistoryRows': 2764800, 'coldSevenDayQueryMilliseconds': int(benchmark[0])}))
            command(['docker', 'rm', identity])
            identity = None
    finally:
        try:
            if identity:
                command(['docker', 'rm', '-f', identity])
        finally:
            if created:
                command(sql_arguments, input=f'DROP DATABASE "{database}" WITH (FORCE);')
        report.update(cleanupConfirmed=True)
        print(json.dumps({'nativeTemporaryCleanup': 'passed', 'suite': suite}))


if __name__ == '__main__': main()
