#!/usr/bin/env python3
"""Remove one verified acceptance-owned application after interrupted test cleanup."""
import json
import os
import re
import subprocess
import sys
from validate_application_backups import native
from validate_databases import Api, TERMINAL_STATES
from validate_governance import request
from validate_metric_partition_runtime import query
from validate_oidc import authenticate, required_environment


def main():
    if len(sys.argv)!=2 or not re.fullmatch(r'wdv-backup-[a-f0-9]{12}',sys.argv[1]):
        raise SystemExit('Requires one exact acceptance-generated wdv-backup slug')
    slug=sys.argv[1]
    resource='application:'+slug
    name='whaledeck-app-'+slug
    current=json.loads(native(['inspect',name]))[0]
    assert current['Name']=='/'+name and not current['State']['Paused']
    assert current['Config']['Labels']['com.docker.compose.project']==name
    assert any(re.fullmatch(r'SYNTHETIC_VALUE=SYNTHETIC_[a-f0-9]{32}',value)
               for value in current['Config']['Env'])
    for mount in current['Mounts']:
        assert (mount['Type']=='bind' and mount['Source']=='/data/WhaleDeck/apps/'+slug+'/data' or
                mount['Type']=='volume' and mount['Name']==name+'-cache' or mount['Type']=='tmpfs')
    jobs=query("SELECT coalesce(json_agg(json_build_object('id',id,'state',state)), '[]') FROM operation_jobs "
               "WHERE request_json::text LIKE '%"+slug+"%';")
    assert jobs and all(job['state'] in TERMINAL_STATES for job in jobs)
    for job in jobs:
        identity=job['id'].replace('-','')
        assert re.fullmatch(r'[a-f0-9]{32}',identity)
        unit='whaledeck-application-backup-'+identity+'.service'
        value=subprocess.check_output(['systemctl','show',unit,'-p','ActiveState','-p','LoadState'],text=True)
        fields=dict(line.split('=',1) for line in value.splitlines() if '=' in line)
        assert fields.get('LoadState') in {'loaded','not-found'}
        assert fields.get('ActiveState') not in {'active','activating','deactivating'}
    base=os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    session,_=authenticate(base,required_environment('VALIDATION_USERNAME'),required_environment('VALIDATION_PASSWORD'))
    api=Api(base,session)
    api.run('uninstall','',{'slug':slug,'deleteVolumes':'true'},area='applications',confirmed=True,requires_plan=True,timeout=120)
    retained='/data/WhaleDeck/apps/.retained'
    names=native(['run','--rm','--network','none','--read-only','--cap-drop','ALL','--user','997:975',
        '--mount','type=bind,source='+retained+',target=/retained,readonly','alpine:3.22',
        'find','/retained','-mindepth','1','-maxdepth','1','-name',slug+'-*']).splitlines()
    for path in names:
        name=path.rsplit('/',1)[-1]
        assert re.fullmatch(re.escape(slug)+'-[a-f0-9]{32}',name)
        native(['run','--rm','--network','none','--read-only','--cap-drop','ALL','--cap-add','DAC_OVERRIDE',
            '--mount','type=bind,source='+retained+',target=/retained','alpine:3.22','rm','-rf','--','/retained/'+name])
    native(['run','--rm','--network','none','--read-only','--cap-drop','ALL',
        '--mount','type=bind,source=/data/DockerData/whaledeck/application-backups,target=/backups',
        'alpine:3.22','rm','-rf','--','/backups/'+slug])
    subprocess.run(['docker','exec','-i','-u','postgres','database-platform-postgres','psql','-X','-v','ON_ERROR_STOP=1','-d','whaledeck'],
        input="UPDATE backup_records SET status='Expired' WHERE status='Succeeded' AND instance_resource_id IN "
              "(SELECT id FROM managed_resources WHERE external_id='"+resource+"');",
        check=True,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=30)
    print(json.dumps({'interruptedAcceptanceCleanup':'passed','slug':slug,'removed':'test-application-owned-data-and-archives',
                      'history':'retained-with-expired-archives'}))


if __name__=='__main__':
    main()
