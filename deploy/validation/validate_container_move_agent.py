#!/usr/bin/env python3
"""Installed signed move UDS/helper only, on an exact disposable fixture.

No human authentication, BFF, SQL writes or product-container lifecycle. A
successful probe is required before cleaning any newly created source data.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import validate_container_file_publishing as owned
from validate_container_copy_intents import remove_tree
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 3; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; probe = Path(sys.argv[2])
    assert probe.parent.parent == repo / 'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', probe.parent.name) and probe.name == 'TerminalProbe'
    trusted(probe); trusted(probe.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=probe.parent, capture_output=True, check=True, timeout=20)
    names = ('whaledeck-container-move-transport.py', 'whaledeck-container-moves.py', 'whaledeck-container-move-scope.py',
        'whaledeck-container-copies.py', 'whaledeck-container-copy-scope.py', 'whaledeck-container-files.py', 'whaledeck-container-upload.py',
        'whaledeck-container-publish.py', 'whaledeck-file-operations.py', 'whaledeck-tree-copies.py', 'whaledeck-tree-moves.py', 'whaledeck-file-tree.py', 'whaledeck-privileged')
    digests = {}
    for name in names:
        path = Path('/usr/local/libexec') / name; trusted(path)
        frozen = owned.common.command(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + name])
        assert path.read_bytes() == frozen; digests[name] = hashlib.sha256(frozen).hexdigest()
    state = Path('/var/lib/whaledeck-container-moves'); trusted(state, True); assert stat.S_IMODE(state.stat().st_mode) == 0o700
    reader = load('/usr/local/libexec/whaledeck-container-files.py', 'installed_move_probe_reader')
    evidence = repo / 'deploy/artifacts' / ('container-move-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    before = owned.common.core_snapshot()
    with owned.fixture() as fixture:
        folder = fixture['folder']; source = folder / 'move-source.bin'; source.write_bytes(fixture['old']); os.chown(source, 1000, 1000); source.chmod(0o640)
        original = reader.fingerprint(source.stat())
        tree = folder / 'source-tree'; tree.mkdir(mode=0o750); (tree / 'empty').mkdir(mode=0o700)
        (tree / 'code.bin').write_bytes(fixture['old']); (tree / 'code.bin').chmod(0o640)
        tree_meta = {}
        for index, relative in enumerate(('', 'code.bin', 'empty')):
            path = tree / relative if relative else tree
            os.chown(path, 2100 + index, 3100 + index); os.utime(path, ns=(1700000000123456789, 1700000000123456789))
            tree_meta[relative] = reader.fingerprint(path.stat())
        container = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        instance = str(container['State']['Pid']) + ':' + container['State']['StartedAt']; location = {'containerId':fixture['identifier'], 'instanceId':instance}
        parameters = {'revision':revision, **location, 'coreId':before['/whaledeck-api'][0],
            'sourceVersion':reader.version(location, 'work/move-source.bin', source.stat()),
            'treeSourceVersion':reader.version(location, 'work/source-tree', tree.stat())}
        (evidence/'baseline.json').write_text(json.dumps({**parameters, 'source':str(folder), 'original':original, 'treeOriginals':tree_meta, 'modules':digests, 'coreBefore':before}))
        result = subprocess.run([str(probe), 'container-moves'], input=json.dumps(parameters), text=True, capture_output=True, timeout=330)
        assert len(result.stdout.encode()) <= 16384 and len(result.stderr.encode()) <= 16384
        (evidence/'rpc.stdout').write_text(result.stdout); (evidence/'rpc.stderr').write_text(result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    value = json.loads(line)
                    if value.get('terminalProbe') == 'failed':print(json.dumps({k:value.get(k) for k in ('phase','rpcStatus','fileCode')}),flush=True)
                except ValueError:pass
        assert result.returncode == 0, 'INSTALLED_MOVE_PROBE_FAILED_DATA_PRESERVED'
        rpc = json.loads(result.stdout); assert rpc['containerMoveProbe']=='passed' and rpc['revision']==revision and len(rpc['completed'])==2
        assert rpc['containerId']==fixture['identifier'] and rpc['instanceId']==instance
        private = folder/'.whaledeck-container-moves-v1'; trusted(private,True); completed=[]
        for value in rpc['completed']:
            operation=value['operationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
            path=state/(operation+'.json');trusted(path);raw=path.read_bytes();record=json.loads(raw);plan=record['plan'];command=plan['command']
            assert record['state']=='Completed' and record['owner']=='B'*64 and record['operationId']==operation and plan['operationKind']=='Move'
            assert all(record[k] is True for k in ('destinationPublished','sourceRemoved','sourceRetained')) and not record['privateMayExist']
            assert plan['fingerprint']==value['fingerprint'] and record['version']==value['version']
            assert command['containerId']==fixture['identifier'] and command['instanceId']==instance and (plan['uid'],plan['gid'])==(1000,1000)
            output=folder/('移动文件.bin' if value['kind']=='File' else '移动目录');info=output.lstat()
            assert command['targetPath']=='work/'+output.name and list(reader.fingerprint(info))==record['publicationIdentity']
            assert record['version']==reader.version(location,command['targetPath'],info)
            if value['kind']=='File':
                kept=private/(operation+'.mutation');assert not source.exists() and reader.fingerprint(kept.lstat())[:8]==original[:8]
                assert output.read_bytes()==kept.read_bytes()==fixture['old'] and stat.S_IMODE(info.st_mode)==0o640 and (info.st_uid,info.st_gid,info.st_mtime_ns)==(1000,1000,original[7])
                assert list(reader.fingerprint(output.lstat()))==record['publicationIdentity'];output.unlink();kept.unlink()
            else:
                kept=private/(operation+'.tree-move');assert not tree.exists()
                for relative,old in tree_meta.items():
                    current=(output/relative).lstat()
                    assert stat.S_IFMT(current.st_mode)==stat.S_IFMT(old[2])
                    assert (current.st_uid,current.st_gid,stat.S_IMODE(current.st_mode),current.st_mtime_ns)==(old[3],old[4],old[2]&0o777,old[7])
                retained=kept/'code.bin';assert reader.fingerprint(retained.lstat())[:8]==tree_meta['code.bin'][:8] and retained.read_bytes()==fixture['old']
                remove_tree(output,record['publicationIdentity'][:2],{'code.bin':'File','empty':'Directory'},reader,fixture['old'])
                remove_tree(kept,[kept.stat().st_dev,kept.stat().st_ino],{'code.bin':'File','empty':'Directory'},reader,fixture['old'])
            (evidence/(value['kind']+'-root.json')).write_bytes(raw);completed.append(operation)
        operation=rpc['canceledOperation'];assert re.fullmatch('[a-f0-9]{32}',operation)
        path=state/(operation+'.json');trusted(path);canceled=json.loads(path.read_bytes())
        assert canceled['state']=='Aborted' and canceled['plan']['command']['targetPath']=='work/取消移动.bin' and all(canceled[k] is False for k in ('destinationPublished','sourceRemoved','sourceRetained'))
        assert not (folder/'取消移动.bin').exists();(evidence/'canceled-root.json').write_bytes(path.read_bytes())
        assert not list(private.iterdir());private.rmdir()
    assert owned.common.core_snapshot()==before
    active=subprocess.run(['systemctl','list-units','--all','--no-legend','whaledeck-container-moves-*.service'],capture_output=True,text=True,check=True,timeout=20)
    assert not active.stdout.strip()
    (evidence/'verified.json').write_text(json.dumps({'revision':revision,'completedOperations':completed,'canceledOperation':operation,
        'installedSignedUdsAndHelper':True,'rootHistoryRetained':True,'mixedOwnersPreserved':True,'sourceAndExactContainerCleaned':True,'coreUnchanged':True,'humanBffOrUi':False}))
    print(json.dumps({'installedMoveProtocol':'passed','evidence':str(evidence),'humanBffOrUi':False}),flush=True)


if __name__=='__main__':main()
