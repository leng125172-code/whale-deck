#!/usr/bin/env python3
"""Independent candidate process reopens the live container and actual HDD read-only."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from unittest.mock import patch

import validate_container_file_publishing as owned
from validate_container_recursive_acl import fixture_bytes,inventory
from validate_container_recursive_acl_partial import no_active_work,preflight,host_identity,preservation_snapshot
from validate_container_recursive_acl_history import SOURCES
from recursive_acl_history_probe import engines,source_request,verify_page,root_snapshot
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_permissions import private_json
from validate_container_upload_recovery_agent import trusted


def target(name):
    assert re.fullmatch('container-recursive-acl-history-[a-f0-9]{32}',name)
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';p=target(sys.argv[1]);trusted(p,True)
    repo=p.parents[2];scope=private_json(p/'scope.json');b=private_json(p/'baseline.json');cases=private_json(p/'cases.json');final=private_json(p/'physical-before-read.json')
    assert scope['candidateOnly'] is True and scope['validationSourceSha256']==source_closure(repo,scope['sourceRevision'],seeds=SOURCES)
    for name,sha in scope['installedSourceHashes'].items():assert hashlib.sha256((Path('/usr/local/libexec')/name).read_bytes()).hexdigest()==sha
    assert not Path('/usr/local/libexec/whaledeck-container-recursive-acl-history.py').exists();no_active_work();preflight()
    assert host_identity(scope['runtimeRevision'])==scope['hostIdentity'] and {k:list(v) for k,v in owned.common.core_snapshot().items()}==scope['coreBefore']
    with engines(repo,scope['sourceRevision'],p/'root-state') as e:
        assert e.hashes==scope['sourceHashes'];observed=e.docker.api('GET','/containers/'+b['containerId']+'/json');owned.verify_container(observed,b['containerId'],b['containerName'],Path(b['source']))
        target_data=e.docker.target(observed,b['containerId'],e.docker.registry());assert target_data['instanceId']==b['instanceId']
        content=fixture_bytes(b['containerId']);assert len(content)==b['bytesPerFile'] and hashlib.sha256(content).hexdigest().upper()==b['sha256']
        root=Path(b['source'])/'recursive-tree';assert inventory(root,e.reader,content,e.acl)==final;before=preservation_snapshot(e.storage);assert before==private_json(p/'read-preservation.json') and root_snapshot(p/'root-state')==private_json(p/'root-before-read.json')
        def forbidden(*args,**kwargs):raise AssertionError('INDEPENDENT_READER_MUST_NOT_WRITE')
        checked=0
        with e.reader.ContainerRoot(dict(containerId=b['containerId'],instanceId=b['instanceId']),e.files,e.docker) as session,patch.object(e.acl,'write_acl',side_effect=forbidden),patch.object(e.files,'save_receipt',side_effect=forbidden):
            listing=private_json(p/'listing-pages.json');after='';actual=[]
            while True:
                page=e.catalog.listing(dict(containerId=b['containerId'],instanceId=b['instanceId'],path='work/recursive-tree',after=after,take=1,expectedVersion=listing[0]['version']),session)
                assert page==listing[len(actual)];actual.append(page)
                if page['complete']:break
                assert page['nextAfter']>after;after=page['nextAfter']
            assert len(actual)==len(listing)
            expected_pages=private_json(p/'entry-pages.json')
            for i,case in enumerate(cases):
                saved=e.engine.history.load(case['request']);digest=hashlib.sha256(e.engine.history.DOMAIN+json.dumps({k:v for k,v in saved.items() if k!='historyDigest'},sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper()
                assert digest==saved['historyDigest']==case['receipt']['historyDigest'];offset=0
                for expected in expected_pages[i]:
                    page=e.catalog.read(source_request(b,case,offset=offset,take=1),session);assert page==expected;offset=verify_page(page,b,case,offset);checked+=1
                assert offset==4 and case['receipt']['state']==('Partial' if case['label']=='partial' else 'Completed')
        assert inventory(root,e.reader,content,e.acl)==final and preservation_snapshot(e.storage)==before and root_snapshot(p/'root-state')==private_json(p/'root-before-read.json')
    result=dict(independentLivePinnedRead=True,candidateOnly=True,captures=len(cases),entryPages=checked,fullHddManifestDigestsVerified=True,sourceBytesAndMetadataReadBeforeCleanup=True,readerWrites=False,installedRpcOrBusinessOrUiAccepted=False)
    (p/'independent.json').open('x').write(json.dumps(result));print(json.dumps(result))


if __name__=='__main__':main()
