"""Pure strict host-install delta. No installation, secret reads or rollback."""
import re
from acl_history_browser_proof import typed_equal

REWRITTEN={'/etc/sudoers.d/whaledeck-agent','/etc/whaledeck/resources.json','/etc/whaledeck/agent.env'}
KEY='/etc/whaledeck/agent-capability.key'
HOSTS={'whaledeck-agent.service','whaledeck-maintenance.service'}


def preserved_install(before,after,old_revision,new_revision):
    assert re.fullmatch('[a-f0-9]{40}',old_revision) and re.fullmatch('[a-f0-9]{40}',new_revision) and old_revision!=new_revision
    assert before.keys()==after.keys() and set(before['hosts'])==set(after['hosts'])==HOSTS
    for key in before.keys()-{'hosts','protected'}:typed_equal(before[key],after[key])
    for unit in HOSTS:
        a,b=before['hosts'][unit],after['hosts'][unit];assert set(a)==set(b)=={'pid','sourceRevision'}
        assert type(a['pid']) is type(b['pid']) is int and a['pid']>1 and b['pid']>1 and a['pid']!=b['pid'] and a['sourceRevision']==old_revision and b['sourceRevision']==new_revision
    assert before['protected'].keys()==after['protected'].keys() and REWRITTEN|{KEY}<=before['protected'].keys()
    for name,a in before['protected'].items():
        b=after['protected'][name];assert set(a)==set(b)=={'identity','sha256'};typed_equal(a['sha256'],b['sha256']);x,y=a['identity'],b['identity'];assert len(x)==len(y)==9 and all(type(n) is int for n in (*x,*y))
        if name in REWRITTEN:
            typed_equal(x[:7],y[:7]);assert y[7]>=x[7] and y[8]>=x[8]
        elif name==KEY:
            typed_equal(x[:8],y[:8]);assert y[8]>=x[8]
        else:typed_equal(a,b)
    return dict(hostsChangedOnly=True,protectedContentPreserved=True,configurationMetadataBounded=True,sourceAndHddJournalsPreserved=True)
