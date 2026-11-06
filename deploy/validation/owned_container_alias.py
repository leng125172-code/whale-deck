"""QA-only second fixed-budget container sharing one exact owned host bind.

The alias never owns/deletes the directory. Unsettled aliases and the primary
fixture are retained; only a positively verified alias may be removed.
"""
import contextlib,json,os,re,stat,subprocess,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from validate_container_file_publishing import common,verify_container
from recursive_acl_instance_proof import instance_frame,require_frame


def baseline(cid,name,folder):return dict(containerId=cid,containerName=name,source=str(folder))


def require_alias(primary,alias,folder):
    assert primary['Id']!=alias['Id'] and primary['Name']!=alias['Name']
    for frame in (primary,alias):
        require_frame(baseline(frame['Id'],frame['Name'][1:],folder),frame)
        assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',frame['Name'][1:])
    assert primary['Image']==alias['Image'] and primary['Mounts']==alias['Mounts'] and primary['HostConfig']==alias['HostConfig']
    assert primary['Mounts'][0]['Source']==alias['Mounts'][0]['Source']==str(folder)


@contextlib.contextmanager
def fixture(primary):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',primary['identifier'])
    folder=primary['folder'];assert folder.parent==common.APPS and folder.resolve(strict=True)==folder and not folder.is_symlink() and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    info=folder.lstat();assert info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o755
    original=instance_frame(json.loads(common.command(['docker','inspect',primary['identifier']]))[0]);require_frame(baseline(primary['identifier'],folder.name,folder),original)
    core=common.core_snapshot();name='wdv-container-publish-'+uuid.uuid4().hex;identifier=None;alias=None
    primary['retain_unsettled']=True
    try:
        identifier=common.command(['docker','run','--detach','--name',name,'--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--user','1000:1000','--pids-limit','16','--memory','32m','--cpus','0.25','--log-driver','local','--log-opt','max-size=1m','--log-opt','max-file=2','--label','io.whaledeck.validation='+name,'--mount','type=bind,src='+str(folder)+',dst=/work','--entrypoint','/bin/sleep',common.IMAGE,'600']).decode().strip()
        frame=instance_frame(json.loads(common.command(['docker','inspect',identifier]))[0]);require_alias(original,frame,folder)
        alias=dict(identifier=identifier,containerName=name,folder=folder,retain_unsettled=True,primaryFrame=original,aliasFrame=frame,folderIdentity=[info.st_dev,info.st_ino])
        yield alias
    finally:
        if alias is not None and alias['retain_unsettled'] is False:
            current=instance_frame(json.loads(common.command(['docker','inspect',identifier]))[0]);typed_equal(current,alias['aliasFrame']);require_alias(original,current,folder)
            assert [folder.stat().st_dev,folder.stat().st_ino]==alias['folderIdentity'];typed_equal(common.core_snapshot(),core)
            common.command(['docker','rm','--force',identifier]);assert subprocess.run(['docker','inspect',identifier],capture_output=True).returncode!=0
            print(json.dumps(dict(ownedAliasRemoved=True,sharedDirectoryDeleted=False)),flush=True)
        else:print(json.dumps(dict(ownedAliasRemoved=False,unsettledSharedFixtureRetained=True)),flush=True)
