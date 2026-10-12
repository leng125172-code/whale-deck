#!/usr/bin/env python3
"""Read-only deployed backup capability, schema and debugging-contract checks."""
import json
import os
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
    assert capabilities['protocolMajor'] == 1 and capabilities['protocolMinor'] >= 14
    assert {'applications.backups', 'applications.backup-retention'} <= set(capabilities['capabilities'])
    assert status['health']['available'] and status['health']['dockerAvailable']
    schema = query("SELECT json_build_object('length',character_maximum_length,'nullable',is_nullable,'default',column_default) FROM information_schema.columns WHERE table_schema='public' AND table_name='backup_policies' AND column_name='consistency';")
    assert schema['length'] == 16 and schema['nullable'] == 'NO' and 'quiesced' in schema['default']
    document = session.get(base + '/openapi/v1.json', timeout=25)
    document.raise_for_status()
    specification = document.json()
    policy_schemas = [item for name, item in specification['components']['schemas'].items() if 'SaveBackupPolicyCommand' in name]
    assert policy_schemas and {'consistency', 'confirmed'} <= set(policy_schemas[0]['properties'])
    assert not any('/restore' in path.lower() for path in specification['paths'])
    print(json.dumps({'applicationBackupRuntime': 'passed', 'protocol': f"1.{capabilities['protocolMinor']}",
        'databaseMigration': 'applied', 'productionAdminOpenApi': 'passed', 'restoreEndpoint': 'absent'}))


if __name__ == '__main__': main()
