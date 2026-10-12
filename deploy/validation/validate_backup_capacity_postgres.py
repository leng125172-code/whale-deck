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
from run_with_ephemeral_admin import container_environment


def command(arguments, timeout=60, input=None):
    return subprocess.run(arguments, input=input, check=True, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, timeout=timeout).stdout


def main():
    repository = Path(__file__).resolve().parents[2]
    database = 'wdv_capacity_' + uuid.uuid4().hex[:16]
    container = 'whaledeck-native-capacity-' + uuid.uuid4().hex[:12]
    assert re.fullmatch(r'wdv_capacity_[a-f0-9]{16}', database)
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
        with tempfile.TemporaryDirectory(prefix='capacity-native-', dir=artifact_root) as scratch:
            scratch_path = Path(scratch).resolve()
            assert scratch_path.parent == artifact_root.resolve()
            archive = scratch_path / 'source.tar'
            command(['git', '-C', str(repository), 'archive', '--format=tar', '--output=' + str(archive), 'HEAD'])
            public_source = scratch_path / 'public-source'
            public_source.mkdir()
            with tarfile.open(archive) as source:
                source.extractall(public_source, filter='data')
            environment = scratch_path / 'native.env'
            descriptor = os.open(environment, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, 'w') as stream:
                stream.write('WHALEDECK_NATIVE_TEST_CONNECTION=' + connection + '\n')
            identity = command(['docker', 'create', '--name', container, '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--memory', '1536m', '--cpus', '2', '--pids-limit', '256',
                '--user', str(os.getuid()) + ':' + str(os.getgid()), '--tmpfs', '/tmp:rw,nosuid,nodev,size=256m',
                '--log-driver', 'local', '--log-opt', 'max-size=2m', '--log-opt', 'max-file=2',
                '--env-file', str(environment), '--env', 'DOTNET_CLI_HOME=/scratch/home', '--env', 'NUGET_PACKAGES=/scratch/packages',
                '--mount', 'type=bind,source=' + str(public_source) + ',target=/repo,readonly',
                '--mount', 'type=bind,source=' + scratch + ',target=/scratch', '--workdir', '/repo', sdk_identity,
                'dotnet', 'test', 'tests/WhaleDeck.IntegrationTests/WhaleDeck.IntegrationTests.csproj',
                '--configuration', 'Release', '--artifacts-path', '/scratch/build', '--filter', 'FullyQualifiedName~NativeBackupCapacityTests']).strip()
            assert re.fullmatch(r'[a-f0-9]{64}', identity)
            command(['docker', 'network', 'connect', common[0], identity])
            completed = subprocess.run(['docker', 'start', '-a', identity], check=False, text=True,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=600)
            output = completed.stdout + completed.stderr
            status = command(['docker', 'inspect', '--format', '{{.State.ExitCode}}', identity]).strip()
            if status != '0':
                diagnostics = [line for line in output.splitlines() if re.search(r': error (?:CS|CA|IDE|NU)\d+', line)][:8]
                raise RuntimeError('Isolated native capacity test failed: ' + ('\n'.join(diagnostics) if diagnostics else 'native assertion or migration failure; private output withheld'))
            assert 'Failed:' in output and 'Passed:' in output and re.search(r'Skipped:\s+0', output), 'Native test was not observed running without skips'
            print(json.dumps({'nativeBackupCapacityPostgres': 'passed', 'database': 'isolated-and-migrated',
                'capacitySamples': 'synthetic-not-production-disk-saturation', 'dedupEscalationSilenceExpiryUnknownRecoveryDisable': 'passed'}))
            command(['docker', 'rm', identity])
            identity = None
    finally:
        if identity:
            command(['docker', 'rm', '-f', identity])
        if created:
            command(sql_arguments, input=f'DROP DATABASE "{database}" WITH (FORCE);')
        print(json.dumps({'nativeCapacityTemporaryCleanup': 'passed'}))


if __name__ == '__main__': main()
