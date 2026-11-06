"""Pure bounded comparison for a standard archive release; never performs I/O."""
import re
from acl_history_browser_proof import typed_equal

LEDGER = '/var/lib/whaledeck-agent/platform-maintenance/deployment-revisions.json'
KEY = '/etc/whaledeck/agent-capability.key'
CONFIG = {KEY, '/etc/sudoers.d/whaledeck-agent', '/etc/whaledeck/resources.json', '/etc/whaledeck/agent.env'}
APPS = {'/whaledeck-api', '/whaledeck-worker', '/whaledeck-gateway'}


def verify(before, after, target):
    assert re.fullmatch('[a-f0-9]{40}', target)
    assert type(before) is dict and type(after) is dict and set(before) == set(after)
    for name in before.keys() - {'core', 'health', 'hosts', 'protected'}:
        typed_equal(after[name], before[name])
    assert len(before['core']) == len(after['core']) == 11 and set(before['core']) == set(after['core'])
    assert set(after['health']) == set(after['core']) and all(v == 'healthy' for v in after['health'].values())
    for name, old in before['core'].items():
        new = after['core'][name]
        if name.startswith('/database-platform-'):
            typed_equal(new, old)
        else:
            assert name in APPS and new[0] != old[0] and new[2] == 'running', name
    assert set(before['hosts']) == set(after['hosts']) and len(after['hosts']) == 2
    for name, old in before['hosts'].items():
        new = after['hosts'][name]
        assert new['sourceRevision'] == target and type(new['pid']) is int and new['pid'] > 0 and new['pid'] != old['pid'], name
    assert set(before['protected']) == set(after['protected']) and KEY in after['protected'] and LEDGER in after['protected']
    changed = []
    for name, old in before['protected'].items():
        new = after['protected'][name]
        assert set(new) == set(old) == {'identity', 'sha256'}
        assert len(old['identity']) == len(new['identity']) == 9
        assert all(type(v) is int for v in old['identity'] + new['identity'])
        if name != LEDGER:
            assert new['sha256'] == old['sha256'], name
        if new['identity'] != old['identity']:
            assert name in CONFIG | {LEDGER}, name
            changed.append(name)
        if name in CONFIG:
            assert new['identity'][2:7] == old['identity'][2:7], name
        if name == LEDGER:
            assert new['identity'][2:6] == old['identity'][2:6], name
    # Existing key inode, content and mtime remain authoritative; chmod may move ctime.
    assert after['protected'][KEY]['identity'][:8] == before['protected'][KEY]['identity'][:8]
    return {'protectedFiles': len(after['protected']), 'allowedIdentityChanges': sorted(changed),
            'coreDatabaseContainersPreserved': 8, 'applicationContainersReplaced': 3}
