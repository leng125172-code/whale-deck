#!/usr/bin/env python3
"""Read-only deployed backup capability, schema and debugging-contract checks."""
import json
import os
import shutil
from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment
from validate_metric_partition_runtime import query


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    status = request(api, 'GET', 'agents').json()
    capabilities = status['capabilities']
    assert capabilities['protocolMajor'] == 1 and capabilities['protocolMinor'] >= 15
    assert {'applications.backups', 'applications.backup-retention', 'backups.capacity'} <= set(capabilities['capabilities'])
    assert status['health']['available'] and status['health']['dockerAvailable']
    capacity = request(api, 'GET', 'backups/capacity').json()
    actual = shutil.disk_usage('/data/DockerData')
    assert capacity['available'] and capacity['targetDirectoryId'] == 'database-platform.backup.hdd'
    assert capacity['totalBytes'] == actual.total
    tolerance = max(64 * 1024**2, actual.total // 10000)
    assert abs(capacity['usedBytes'] - actual.used) < tolerance and abs(capacity['freeBytes'] - actual.free) < tolerance
    assert 0 <= capacity['usedPercent'] <= 100
    schema = query("SELECT json_build_object('length',character_maximum_length,'nullable',is_nullable,'default',column_default) FROM information_schema.columns WHERE table_schema='public' AND table_name='backup_policies' AND column_name='consistency';")
    assert schema['length'] == 16 and schema['nullable'] == 'NO' and 'quiesced' in schema['default']
    document = session.get(base + '/openapi/v1.json', timeout=25)
    document.raise_for_status()
    specification = document.json()
    policy_schemas = [item for name, item in specification['components']['schemas'].items() if 'SaveBackupPolicyCommand' in name]
    assert policy_schemas and {'consistency', 'confirmed'} <= set(policy_schemas[0]['properties'])
    assert not any('/restore' in path.lower() for path in specification['paths'])
    assert '/api/v1/backups/capacity' in specification['paths']
    print(json.dumps({'applicationBackupRuntime': 'passed', 'protocol': f"1.{capabilities['protocolMinor']}",
        'databaseMigration': 'applied', 'productionAdminOpenApi': 'passed', 'actualHddCapacity': 'native-match', 'restoreEndpoint': 'absent'}))


if __name__ == '__main__': main()
