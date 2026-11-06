"""Read-only Unknown/ACK joins after namespace publication, without copy success."""
from pathlib import Path
from acl_history_browser_proof import typed_equal
from archive_prepared_unknown_proof import row_proof,audits,require_ack
from recycle_archive_browser_proof import managed_plan,stored_object,receipt_join
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import hdd_observation
from bound_container_recycle_archive_prepared_cut import parent_version
import container_recycle_archive_agent_io as io


def published_proof(value,q,root,cut):
    assert root['state']=='Unknown' and cut['state']=='Publishing'
    for key in cut:
        if key!='state':typed_equal(root[key],cut[key])
    p=q['plan'];original=p['command']['purge']['plan']['command']['source']['plan']
    assert root['publishedIdentity'] is None and value['retained'] is value['pending'] is value['staged'] is None
    typed_equal(value['parentIdentity'],p['parentIdentity']);typed_equal(value['oldPrivateIdentity'],p['privateIdentity']);typed_equal(value['stageIdentity'],root['privateIdentity'])
    file=value['source'];assert file is not None
    typed_equal(file['identity'][:8],root['preparedIdentity'][:8]);assert file['identity'][8]>=root['preparedIdentity'][8]
    typed_equal(file['identity'][:2],root['pendingIdentity']);typed_equal(file['identity'][2:8],original['sourceIdentity'][2:8]);typed_equal(file['attributes'],original['attributes'])
    assert file['identity'][5]==1 and file['identity'][:2]!=p['archiveIdentity'][:2] and file['identity'][:2]!=original['sourceIdentity'][:2] and file['sha256']==p['archiveSha256']==original['sha256'] and file['sizeBytes']==p['archiveBytes']
    return dict(physicalPublishedFullCopy=True,stageEmpty=True,publishedCopyNotProven=True,directoryFsyncProven=False)


def prove(dto,fixture,source,purge,cut,e,*,create=False,acknowledged=False):
    data=archive_sql(dto['id']);row_proof(data,fixture,purge['sql']['row'],acknowledged);r=data['row'];assert dto['state']==r['state'] and dto['executionJobId']==r['execution_job_id'] and dto['archiveCopied'] is None
    p=managed_plan(stored_object(r['plan_json']));typed_equal(p['command']['purge'],purge['request']);q=dict(operationId=r['execution_job_id'].replace('-',''),owner=r['owner_hash'],plan=p)
    receipt=receipt_join(r,dto['receipt'],q);assert receipt['state']=='Unknown' and receipt['archiveCopied'] is None and receipt['ownedFence'] is receipt['sourceFenced'] is True
    records=io.read_operation(q,receipt,purge['records'],e/'archive-originals',create=create);folder=Path(fixture['folder']);physical=io.physical(folder,q);published_proof(physical,q,records['root'],cut)
    import json
    cut_proof=json.loads((e/'cut-proof.json').read_text());typed_equal(physical,cut_proof['physical']);assert parent_version(cut,folder)==cut_proof['parentVersionAfterPublication']
    from validate_container_recycle_browser import sql as source_sql
    from validate_container_recycle_purge_browser import sql as purge_sql
    typed_equal(source_sql(source['sql']['row']['id']),source['sql']);typed_equal(purge_sql(purge['sql']['row']['id']),purge['sql']);hdd=hdd_observation(source['request']['operationId']);typed_equal(hdd,purge['hdd'])
    audit=audits(r);assert audit['cancel']==1 and audit['ack']==int(acknowledged) and audit['ackActors']==([r['acknowledged_by']] if acknowledged else [])
    if acknowledged:assert r['acknowledged_by']!=r['actor_subject'] and stored_object(r['acknowledgement_json'])==stored_object(r['receipt_json'])
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd,audits=audit)
