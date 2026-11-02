"""QA-only mutation of one generated Authentik identity; never a product API.

The username/UUID/numeric PK/admin group are all verified before one PATCH.
Only groups=[] or is_active=false can be written; credentials stay in memory.
"""
import datetime as dt
import re
import uuid

import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


class OwnedAuthentikRevocation:
    def __init__(self, username, subject, *, client=None, settings=None):
        assert re.fullmatch('whaledeck-upload-qa-[a-f0-9]{12}', username)
        assert str(uuid.UUID(subject)) == subject
        self.username, self.subject, self.changed = username, subject, False
        values = container_environment() if settings is None else settings
        self.group = require(values, 'Authentication__Authentik__AdministratorGroupId')
        self.client = requests.Session() if client is None else client
        self.client.trust_env = False
        self.client.headers.update({'Authorization': 'Bearer '+require(values, 'Authentication__Authentik__ApiToken')})
        try:
            response = self.client.get(AUTHENTIK_API+'core/users/', params={'username':username,'page_size':2}, timeout=5)
            assert response.status_code == 200
            body = response.json(); assert not body.get('pagination',{}).get('next')
            users = body['results']; assert len(users) == 1
            self.before = self.snapshot(users[0])
            identifier = str(self.before['pk']); assert re.fullmatch('[1-9][0-9]{0,11}', identifier)
            self.url = AUTHENTIK_API+'core/users/'+identifier+'/'
            assert self.before['is_active'] is True and self.group in self.before['groups']
        except BaseException:
            self.client.close(); raise

    def snapshot(self, user):
        assert user['username'] == self.username and user['uuid'] == self.subject
        assert type(user['is_active']) is bool and isinstance(user['groups'],list) and len(user['groups'])<=256
        assert all(type(group) is str for group in user['groups'])
        return {key:user[key] for key in ('pk','uuid','username','is_active','groups')}

    def change(self, kind):
        assert kind in ('group-removal','account-disable') and not self.changed
        current = self.client.get(self.url,timeout=5); assert current.status_code==200
        before = self.snapshot(current.json()); assert before == self.before
        payload = {'groups':[]} if kind=='group-removal' else {'is_active':False}
        self.changed = True  # a lost response never authorizes a second mutation.
        changed = self.client.patch(self.url,json=payload,timeout=5); assert changed.status_code==200
        assert self.snapshot(changed.json())['pk'] == before['pk']
        confirmed = self.client.get(self.url,timeout=5); assert confirmed.status_code==200
        after = self.snapshot(confirmed.json()); assert after['pk']==before['pk']
        if kind=='group-removal': assert after['groups']==[] and after['is_active'] is True
        else: assert after['is_active'] is False and after['groups']==before['groups']
        return {'kind':kind,'subject':self.subject,'before':before,'after':after,'onlyOwnedIdentityChanged':True,
            'confirmedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat()}

    def close(self): self.client.close()
    def __enter__(self): return self
    def __exit__(self,*_args): self.close()
