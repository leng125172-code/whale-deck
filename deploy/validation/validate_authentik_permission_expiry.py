#!/usr/bin/env python3
"""Observe the real two-hour administrator snapshot limit without changing time/cache.

Only this invocation's temporary Authentik user's memberships are removed.
Keep the original BFF login alive, await natural expiry and require actual
admin-route denial plus a downgraded/expired identity. No production user,
group, role mapping, cache key, SQL row or system clock is modified.
"""
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import re
import sys
import time
import uuid

import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_upload_bff import login, logout, temporary_user

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    evidence = Path(__file__).resolve().parents[1]/'artifacts'/('permission-expiry-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    def save(name,value):
        assert name in ('baseline.json','progress.json','verified.json')
        path=evidence/name;temporary=evidence/(name+'.tmp');temporary.write_text(json.dumps(value));temporary.chmod(0o600);temporary.replace(path)
    native=requests.Session();native.trust_env=False
    native.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    with temporary_user(True) as (username,password):
        session,initial=login(base,username,password)
        assert initial['isAdministrator']
        runtime=session.get(base+'/api/v1/system/status',timeout=20);assert runtime.status_code==200 and runtime.json()['build']['sourceRevision']==revision
        protected=base+'/api/v1/files/roots'
        assert session.get(protected,timeout=20).status_code==200
        expires=datetime.fromisoformat(initial['permissionExpiresAtUtc']).astimezone(timezone.utc)
        remaining=(expires-datetime.now(timezone.utc)).total_seconds();assert 7100<remaining<=7201
        PHASE='remove-only-owned-user-memberships'
        response=native.get(AUTHENTIK_API+'core/users/',params={'username':username,'page_size':2},timeout=20)
        assert response.status_code==200
        users=[item for item in response.json()['results'] if item['username']==username];assert len(users)==1
        identifier=str(users[0]['pk']);assert users[0]['groups']
        changed=native.patch(AUTHENTIK_API+f'core/users/{identifier}/',json={'groups':[]},timeout=20);assert changed.status_code==200
        confirmed=native.get(AUTHENTIK_API+f'core/users/{identifier}/',timeout=20);assert confirmed.status_code==200 and confirmed.json()['groups']==[]
        removed=datetime.now(timezone.utc);started=time.monotonic();deadline=started+max(0,(expires-removed).total_seconds())+30
        save('baseline.json',{'revisionAtStart':revision,'removedAtUtc':removed.isoformat(),'originalPermissionExpiresAtUtc':expires.isoformat(),
            'maximumConfiguredSeconds':7200,'systemClockSqlAndCacheUnchanged':True})
        print(json.dumps({'permissionExpiry':'observing','maximumSeconds':7200,'expiresAtUtc':expires.isoformat(),
            'onlyOwnedMembershipsRemoved':True,'evidence':str(evidence)}),flush=True)
        PHASE='natural-permission-expiry-on-original-live-login';probes=0;allowed=0;first_denial=None
        while time.monotonic()<=deadline:
            requested_at=datetime.now(timezone.utc)
            response=session.get(protected,allow_redirects=False,timeout=15);probes+=1
            if response.status_code in (401,403):first_denial=response.status_code;break
            assert response.status_code==200
            # Sampling tolerance permits observing denial on the next probe;
            # it never permits an admin grant requested after natural expiry.
            assert requested_at<expires
            allowed+=1
            now=datetime.now(timezone.utc)
            save('progress.json',{'permissionExpiry':'observing','probeCount':probes,'allowedBeforeExpiry':allowed,
                'lastAllowedRequestedAtUtc':requested_at.isoformat(),
                'remainingSeconds':max(0,round((expires-now).total_seconds())),'originalCookieOnly':True})
            print(json.dumps({'permissionExpiry':'observing','probeCount':probes,'remainingSeconds':max(0,round((expires-now).total_seconds()))}),flush=True)
            # Coarse while far from expiry, five seconds near the actual bound.
            time.sleep(min(30,5 if (expires-now).total_seconds()<45 else 30))
        assert first_denial in (401,403)
        PHASE='verify-real-downgrade-and-exact-owned-cleanup'
        denied=datetime.now(timezone.utc)
        assert denied<=expires+timedelta(seconds=30)
        me=session.get(base+'/api/v1/auth/me',allow_redirects=False,timeout=20)
        assert me.status_code==401 or me.status_code==200 and me.json()['isAdministrator'] is False
        assert session.get(protected,allow_redirects=False,timeout=20).status_code in (401,403)
        save('verified.json',{'originalPermissionExpiresAtUtc':expires.isoformat(),'firstDeniedAtUtc':denied.isoformat(),
            'denialStatus':first_denial,'probes':probes,'elapsedSeconds':round(time.monotonic()-started,3),
            'actualGroupRemovalObserved':True,'originalLoginFailedClosed':True,'clockSqlAndCacheUnchanged':True})
        if me.status_code==200:logout(base,session)
        session.close()
    native.close()
    print(json.dumps({'permissionExpiry':'passed','probes':probes,'maximumConfiguredSeconds':7200,
        'actualGroupRemovalAndNaturalExpiry':True,'originalLiveLoginDenied':True,'ownedIdentityCleaned':True}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'permissionExpiry':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}),file=sys.stderr)
        raise SystemExit(1) from None
