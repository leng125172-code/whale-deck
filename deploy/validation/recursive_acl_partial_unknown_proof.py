"""Owned target disappearance: preserve a known prior write, prove Unknown separately."""
import json
import select
from pathlib import Path

from bound_container_recursive_acl_cut import STATE,child_id
from bound_container_recursive_acl_partial_cut import partial_proof


def require_unknown_identity(baseline,fixture):
    source=Path(baseline['source'])
    assert baseline['kind']=='target-removed' and fixture['identifier']==baseline['containerId'] and fixture['folder']==source
    assert source.parent==Path('/data/WhaleDeck/apps') and source.name==baseline['containerName']


def capture_completed_child(row,directory):
    operation=row['execution_job_id'].replace('-','');path=STATE/(operation+'.entries')/(child_id(operation,0)+'.json')
    raw=path.read_bytes();value=json.loads(raw);assert value['state']=='Completed' and value['operationId']==child_id(operation,0) and value['owner']==row['owner_hash']
    (directory/'cut-completed-child.root.json').write_bytes(raw)


def remove_unknown_target(baseline,fixture,held,current,cut,directory):
    require_unknown_identity(baseline,fixture)
    assert current()==cut and not select.select([held.descriptor],[],[],0)[0]
    fixture['remove_owned']()
    assert current()==cut and not select.select([held.descriptor],[],[],0)[0]
    proof=dict(containerId=baseline['containerId'],onlyExactOwnedContainerRemoved=True,helperAliveBeforeNormalLogout=True,partialSourceInventoryUnchanged=True)
    (directory/'removed-owned.json').write_text(json.dumps(proof))
    return proof


def verify_unknown_target(baseline,row,directory,read):
    assert baseline['kind']=='target-removed' and row['root_state']=='Unknown'
    removed=read(directory/'removed-owned.json');assert removed==dict(containerId=baseline['containerId'],onlyExactOwnedContainerRemoved=True,helperAliveBeforeNormalLogout=True,partialSourceInventoryUnchanged=True)
    physical=read(directory/'cut-physical.json');assert physical==read(directory/'physical-partial.json')==read(directory/'physical-final.json')
    cut=read(directory/'cut-root.json');assert cut['operationId']==row['execution_job_id'].replace('-','') and cut['owner']==row['owner_hash']
    proof=partial_proof(cut,baseline['original'],physical);bound=read(directory/'bound-helper.json')
    assert proof is not None and bound['proof']['twoKernelFdProofs'] is True and all(bound['proof'][k]==v for k,v in proof.items())
    first=STATE/(cut['operationId']+'.entries')/(child_id(cut['operationId'],0)+'.json')
    assert first.read_bytes()==(directory/'cut-completed-child.root.json').read_bytes()
    return dict(realChangeBeforeTargetLoss=True,originalCompletedChildRetained=True,aggregateUnknownIsNotZeroActualChanges=True)
