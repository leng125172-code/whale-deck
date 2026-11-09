"""Pure acceptance of captured post-retirement evidence; never executes or repairs.

Checksums protect consistency, not authority. Only an independently acquired
physical snapshot and trusted executor receipt can supply these observations.
"""
import re
from acl_history_browser_proof import typed_equal
from archive_retirement_preexpiry_proof import canonical,digest,integer,raw_record_proof
from validate_container_recycle_archive_readiness import readiness_proof


def completion_acceptance_proof(value,independent_before):
    fields={'request','readiness','clockBeforeUnixMs','clockAfterUnixMs','journalBefore','journalAfter','markerBefore','markerAfter','rootsBefore','rootsAfter','hddBefore','hddAfter','protectedBefore','protectedAfter','namespaceBefore','namespaceAfter','receipt','observation'}
    assert type(value) is dict and set(value)==fields
    assert set(independent_before)=={'request','roots','hdd','protected','namespace'}
    for observed,key in (('request','request'),('rootsBefore','roots'),('hddBefore','hdd'),('protectedBefore','protected'),('namespaceBefore','namespace')):typed_equal(value[observed],independent_before[key])
    q=value['request'];assert set(q)=={'operationId','owner','plan'}
    assert type(q['operationId']) is str and re.fullmatch('[a-f0-9]{32}',q['operationId']) and type(q['owner']) is str and re.fullmatch('[A-F0-9]{64}',q['owner'])
    readiness=value['readiness'];readiness_proof(readiness);typed_equal(readiness['request'],q)
    assert readiness['status']=='ReadyForExplicitAcceptance';p=readiness['currentPlan'];archive=q['plan']['command']['archive'];purge=archive['plan']['command']['purge'];ref=purge['plan']['command']['source'];source_id=ref['operationId']
    assert len({q['operationId'],source_id,purge['operationId'],archive['operationId']})==4
    integer(value['clockBeforeUnixMs'],readiness['clockAfterUnixMs'],253402300799999)
    integer(value['clockAfterUnixMs'],value['clockBeforeUnixMs'],253402300799999)
    typed_equal(value['journalBefore'],dict(schemaVersion=1,**q,state='Reserved'))
    marker=lambda state:dict(schemaVersion=1,archive=archive,retirementOperationId=q['operationId'],retirementOwner=q['owner'],retirementFingerprint=q['plan']['fingerprint'],state=state)
    typed_equal(value['markerBefore'],marker('Reserved'));typed_equal(value['markerAfter'],marker('Retired'))
    final=value['journalAfter'];assert type(final) is dict and set(final)=={'schemaVersion','operationId','owner','plan','state','completion'}
    typed_equal({k:v for k,v in final.items() if k!='completion'},dict(schemaVersion=2,**q,state='Completed'))
    proof=final['completion'];assert set(proof)=={'executionPlan','unlinkConfirmed','unlinkedIdentity','completedAtUnixMs','proofDigest'}
    typed_equal(proof['executionPlan'],p);assert proof['unlinkConfirmed'] is True;typed_equal(proof['unlinkedIdentity'],p['archiveIdentity'])
    integer(proof['completedAtUnixMs'],value['clockBeforeUnixMs'],value['clockAfterUnixMs'])
    assert proof['proofDigest']==digest(b'whaledeck-container-recycle-archive-retirement-completion-v1\n',{k:v for k,v in proof.items() if k!='proofDigest'})
    before=value['hddBefore'];after=value['hddAfter'];data=source_id+'.data';meta=source_id+'.json'
    assert data in before and meta in before and data not in after and meta in after
    assert source_id+'.partial' not in before and source_id+'.partial' not in after
    assert set(before)-set(after)=={data} and not set(after)-set(before)
    for name in after:typed_equal(before[name],after[name])
    payload=before[data];typed_equal(payload['identity'],p['archiveIdentity']);assert payload['sha256']==p['archiveSha256'] and payload['sizeBytes']==p['archiveBytes'] and payload['attributes']==[]
    roots_before=value['rootsBefore'];roots_after=value['rootsAfter'];typed_equal(roots_before,readiness['rootsAfter']);assert set(roots_before)==set(roots_after)
    journal=q['operationId']+'.json';claim='claims/'+source_id+'.json';assert journal in roots_before and claim in roots_before
    for name in roots_before:
        if name not in (journal,claim):typed_equal(roots_before[name],roots_after[name])
    for snapshots,name,record in ((roots_before,journal,value['journalBefore']),(roots_after,journal,final),(roots_before,claim,value['markerBefore']),(roots_after,claim,value['markerAfter'])):raw_record_proof(snapshots[name],record)
    typed_equal(value['protectedBefore'],value['protectedAfter']);typed_equal(value['namespaceBefore'],value['namespaceAfter'])
    typed_equal(value['receipt'],dict(operationId=q['operationId'],sourceOperationId=source_id,archiveOperationId=archive['operationId'],fingerprint=q['plan']['fingerprint'],state='Completed',sourceFenced=True,ownedFence=True,retirementApplied=True,notBeforeUnixMs=q['plan']['notBeforeUnixMs']))
    typed_equal(value['observation'],dict(operationId=source_id,historyDigest=archive['plan']['command']['historyDigest'],disposition='Retired',reason='CommittedRetirement',dataAvailable=False,dataPresent=False,retirementOperationId=q['operationId']))
    return dict(postRetirementEvidence='passed',selectedDataAbsent=True,originalHistoryClaimAndNamespacePreserved=True)
