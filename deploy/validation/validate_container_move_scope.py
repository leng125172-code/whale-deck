#!/usr/bin/env python3
"""Frozen internal move candidate on a real, isolated two-filesystem container.

Uses one owned HDD bind and one explicitly named Docker volume on NVMe. No
installed helper/RPC/BFF/UI changes. Unproved data and permanent receipts stay.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load


def run(*args): return subprocess.check_output(args, text=True, timeout=30).strip()


def inventory(root, expected, reader, content):
    assert root.resolve(strict=True) == root
    result = {}
    for relative, kind in expected.items():
        path = root / relative if relative else root; info = path.lstat()
        assert not path.is_symlink() and (stat.S_ISDIR(info.st_mode) if kind == 'Directory' else stat.S_ISREG(info.st_mode))
        if kind == 'File': assert info.st_nlink == 1 and path.read_bytes() == content
        else:
            children = {name.rsplit('/', 1)[-1] for name in expected if name and name.rpartition('/')[0] == relative}
            assert {item.name for item in path.iterdir()} == children
        result[relative] = reader.fingerprint(info)
    return result


def remove_proved(root, expected, proofs, reader, content):
    assert inventory(root, expected, reader, content) == proofs
    for relative in sorted(expected, key=lambda x: (x.count('/'), len(x)), reverse=True):
        if not relative: continue
        path = root / relative; info = reader.fingerprint(path.lstat())
        if expected[relative] == 'File':
            assert info == proofs[relative]; path.unlink()
        else:
            assert info[:5] == proofs[relative][:5] and not list(path.iterdir()); path.rmdir()
    assert reader.fingerprint(root.stat())[:5] == proofs[''][:5] and not list(root.iterdir()); root.rmdir()


def main():
    assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 3 and sys.argv[2] == '--intents'); os.umask(0o077)
    intents = len(sys.argv) == 3
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    names = ('whaledeck-container-copy-scope.py', 'whaledeck-container-move-scope.py', 'whaledeck-container-files.py',
        'whaledeck-files.py', 'whaledeck-container-terminal.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py',
        'whaledeck-file-operations.py', 'whaledeck-tree-copies.py', 'whaledeck-tree-moves.py', 'whaledeck-file-tree.py')
    if intents: names += ('whaledeck-container-copies.py', 'whaledeck-container-moves.py')
    frozen = {name: owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name]) for name in names}
    for name, content in frozen.items(): assert (repository / 'deploy/host' / name).read_bytes() == content
    assert Path(__file__).read_bytes() == owned.common.command(['git','-C',str(repository),'show',revision+':deploy/validation/'+Path(__file__).name])
    evidence = repository / 'deploy/artifacts' / ('container-move-scope-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    state = evidence / 'root-state'; state.mkdir(mode=0o700)
    name = 'wdv-container-move-' + uuid.uuid4().hex; volume_name = 'whaledeck-app-' + name
    before = owned.common.core_snapshot(); owned.common.verify_apps_directory(owned.common.APPS, __import__('pwd').getpwnam('whaledeck-agent').pw_uid)
    folder = owned.common.APPS / name; folder.mkdir(mode=0o755); folder.chmod(0o755)
    folder_identity = [folder.stat().st_dev, folder.stat().st_ino]
    identifier = None; volume = None; proved = False; source_proof = None; target_proof = None
    content = hashlib.shake_256(name.encode()).digest(owned.common.SIZE); results = []
    with tempfile.TemporaryDirectory(prefix='wd-native-move-') as temporary:
        bundle = Path(temporary)
        for module, payload in frozen.items(): (bundle/module).write_bytes(payload); (bundle/module).chmod(0o750)
        reader = load(bundle / 'whaledeck-container-files.py', 'native_move_reader')
        files = reader.trusted_module('whaledeck-files.py', 'native_move_files')
        docker = reader.trusted_module('whaledeck-container-terminal.py', 'native_move_docker')
        upload_type = load(bundle/'whaledeck-container-upload.py','native_move_upload').ContainerUploads
        publisher_type = load(bundle/'whaledeck-container-publish.py','native_move_publish').ContainerPublisher
        copy_scope = load(bundle/'whaledeck-container-copy-scope.py','native_move_base').ContainerCopyScope
        scope_type = load(bundle/'whaledeck-container-move-scope.py','native_move_scope').container_move_scope_type(copy_scope)
        file_type = load(bundle/'whaledeck-file-operations.py','native_move_file').FileOperations
        copy_type = load(bundle/'whaledeck-tree-copies.py','native_move_copies').FileTreeCopies
        tree_type = load(bundle/'whaledeck-tree-moves.py','native_move_trees').FileTreeMoves
        manifest = load(bundle/'whaledeck-file-tree.py','native_move_manifest').FileTreeManifest
        unused = bundle/'unused'; unused.mkdir(mode=0o700); upload = upload_type(reader,files,unused)
        publisher = publisher_type(upload,type('UnusedBackups',(),{'u':upload})())
        if intents:
            base_type=load(bundle/'whaledeck-container-copies.py','native_move_receipt_base').ContainerCopies
            intent_type=load(bundle/'whaledeck-container-moves.py','native_move_receipts').container_moves_type(base_type)
            intent_engine=intent_type(upload,publisher,scope_type,file_type,copy_type,manifest,tree_type,state)
        try:
            run('docker','volume','create','--label','io.whaledeck.validation='+name,volume_name)
            volume = json.loads(run('docker','volume','inspect',volume_name))[0]
            assert volume['Name']==volume_name and volume['Driver']=='local' and not volume.get('Options') and volume['Labels']['io.whaledeck.validation']==name
            fast = Path(volume['Mountpoint']); assert fast.resolve(strict=True)==fast and not list(fast.iterdir())
            os.chown(fast,1000,1000);fast.chmod(0o755)
            assert fast.stat().st_dev != folder.stat().st_dev, 'CROSS_FILESYSTEM_NOT_PROVED'
            for relative in ('中文.bin','项目/代码.bin'):
                path=folder/relative
                if not path.parent.exists():path.parent.mkdir(mode=0o750)
                path.write_bytes(content);os.chown(path,1000,1000);path.chmod(0o640)
            (folder/'项目/空目录').mkdir(mode=0o700)
            for path in (folder/'中文.bin',folder/'项目/代码.bin',folder/'项目/空目录',folder/'项目'):
                os.chown(path,1000,1000);os.utime(path,ns=(1700000000123456789,1700000000123456789))
            if intents:
                for index,path in enumerate((folder/'项目',folder/'项目/代码.bin',folder/'项目/空目录')):os.chown(path,2100+index,3100+index)
            identifier=run('docker','run','--detach','--name',name,'--network','none','--read-only','--cap-drop','ALL',
                '--security-opt','no-new-privileges','--user','1000:1000','--pids-limit','16','--memory','32m','--cpus','0.25',
                '--log-driver','local','--log-opt','max-size=1m','--log-opt','max-file=2','--label','io.whaledeck.validation='+name,
                '--mount','type=bind,src='+str(folder)+',dst=/work','--mount','type=volume,src='+volume_name+',dst=/fast',
                '--entrypoint','/bin/sleep',owned.common.IMAGE,'600')
            inspected=json.loads(run('docker','inspect',identifier))[0]
            assert inspected['Image']==owned.common.IMAGE and inspected['Config']['User']=='1000:1000'
            assert inspected['HostConfig']['ReadonlyRootfs'] and inspected['HostConfig']['NetworkMode']=='none' and len(inspected['Mounts'])==2
            location=docker.target(inspected,identifier,docker.registry());location={'containerId':identifier,'instanceId':location['instanceId']}
            (evidence/'baseline.json').write_text(json.dumps({'revision':revision,**location,'source':str(folder),'sourceIdentity':folder_identity,
                'volume':volume_name,'volumeMount':str(fast),'sourceDevice':folder.stat().st_dev,'targetDevice':fast.stat().st_dev,'bytesPerFile':len(content),'sha256':hashlib.sha256(content).hexdigest().upper(),
                'containerDomainIntents':intents,'coreBefore':before}))
            operations=[]
            with reader.ContainerRoot({**location,'path':'work'},files,docker) as session:
                for kind,source,destination in (('File','work/中文.bin','fast/移入.bin'),('Directory','work/项目','fast/目录'),('File','fast/移入.bin','work/返回.bin')):
                    source_root=folder if source.startswith('work/') else fast;target_root=folder if destination.startswith('work/') else fast
                    source_path=source_root/source.split('/',1)[1];target=target_root/destination.split('/',1)[1]
                    original=source_path.stat();original_file=(source_path/'代码.bin').stat() if kind=='Directory' else original
                    operation=uuid.uuid4().hex;operations.append(operation)
                    command={**location,'sourcePath':source,'targetPath':destination,'expectedVersion':reader.version(location,source,original)}
                    if intents:
                        request={'operationId':operation,'owner':'B'*64,'plan':intent_engine.plan(command,session)}
                        result=intent_engine.apply(request,session)
                        assert result['bindingCurrent'] and all(result[key] is True for key in intent_engine.FACTS)
                        assert result['version']==reader.version(location,destination,target.stat())
                        assert result['version']!=files.version(target.stat())
                    else:
                        scope=scope_type(upload,publisher,session,command,state,operation_id=operation)
                        engine=file_type(scope) if kind=='File' else tree_type(scope,manifest,copy_type)
                        plan=engine.plan(scope.file_command() if kind=='File' else scope.tree_command());request={'operationId':operation,'owner':'B'*64,'plan':plan}
                        result=engine.apply(request) if kind=='File' else engine.move(request)
                    assert result['state']=='Completed' and result['sourceRetained'] and not os.path.lexists(source_path)
                    retained=source_root/scope_type.PRIVATE/(operation+('.mutation' if kind=='File' else '.tree-move'))
                    file=retained if kind=='File' else retained/'代码.bin';out=target if kind=='File' else target/'代码.bin'
                    assert file.read_bytes()==out.read_bytes()==content and file.stat().st_ino==original_file.st_ino
                    assert (out.stat().st_uid,out.stat().st_gid,stat.S_IMODE(out.stat().st_mode),out.stat().st_mtime_ns)==(original_file.st_uid,original_file.st_gid,0o640,original_file.st_mtime_ns)
                    if kind=='Directory':assert (target/'空目录').is_dir() and stat.S_IMODE((target/'空目录').stat().st_mode)==0o700
                    if intents:
                        receipt=state/(operation+'.json');raw=receipt.read_bytes();again=intent_engine.apply(request,session)
                        assert again['replayed'] and {k:v for k,v in again.items() if k!='replayed'}=={k:v for k,v in result.items() if k!='replayed'}
                        assert receipt.read_bytes()==raw
                    else:
                        again=engine.apply(request) if kind=='File' else engine.move(request);assert again==result
                    result.update(kind=kind,operation=operation,sourceDevice=source_root.stat().st_dev,targetDevice=target_root.stat().st_dev,
                        targetContainerVersion=reader.version(location,destination,target.stat()))
                    results.append(result);(evidence/'results.json').write_text(json.dumps(results))
            private=scope_type.PRIVATE;a,b,c=operations
            source_expected={'':'Directory',private:'Directory',private+'/'+a+'.mutation':'File',private+'/'+b+'.tree-move':'Directory',
                private+'/'+b+'.tree-move/代码.bin':'File',private+'/'+b+'.tree-move/空目录':'Directory','返回.bin':'File'}
            target_expected={'':'Directory',private:'Directory',private+'/'+c+'.mutation':'File','目录':'Directory','目录/代码.bin':'File','目录/空目录':'Directory'}
            source_proof=inventory(folder,source_expected,reader,content);target_proof=inventory(fast,target_expected,reader,content)
            assert [folder.stat().st_dev,folder.stat().st_ino]==folder_identity and owned.common.core_snapshot()==before
            (evidence/'filesystem-proofs.json').write_text(json.dumps({'source':source_proof,'target':target_proof}));proved=True
        finally:
            if identifier is not None:
                current=json.loads(run('docker','inspect',identifier))[0]
                assert current['Id']==identifier and current['Name']=='/'+name and current['Image']==owned.common.IMAGE
                assert current['Config']['Labels']['io.whaledeck.validation']==name
                assert {(m['Type'],m['Destination'],m['Source']) for m in current['Mounts']}=={('bind','/work',str(folder)),('volume','/fast',volume['Mountpoint'])}
                run('docker','rm','--force',identifier)
            if proved:
                assert owned.common.core_snapshot()==before
                remove_proved(folder,source_expected,source_proof,reader,content)
                assert inventory(fast,target_expected,reader,content)==target_proof
                assert not run('docker','ps','-aq','--filter','volume='+volume_name)
                current=json.loads(run('docker','volume','inspect',volume_name))[0];assert current==volume
                run('docker','volume','rm',volume_name)
            else:(evidence/'preserved.json').write_text(json.dumps({'source':str(folder),'volume':volume_name,'unprovedDataNotDeleted':True}))
    assert proved and owned.common.core_snapshot()==before
    assert not folder.exists() and subprocess.run(['docker','volume','inspect',volume_name],capture_output=True).returncode!=0
    (evidence/'verified.json').write_text(json.dumps({'revision':revision,'crossFilesystemMoves':3,'allBytesAndRetainedOriginalInodesVerified':True,
        'exactContainerAndVolumeAndSourceCleaned':True,'coreContainersUnchanged':True,'installedEntryPoint':False,'containerDomainIntents':intents,'results':results}))
    print(json.dumps({'containerMoveScope':'passed','evidence':str(evidence),'crossFilesystemMoves':3}),flush=True)


if __name__=='__main__':main()
