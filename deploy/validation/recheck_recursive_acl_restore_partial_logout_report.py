#!/usr/bin/env python3
"""One already-cleaned run only: preserve bad original flags, derive truthful outcome.

No creation of identities, browser, mutations, replay, cleanup or original-file
writes. A new separately sealed receipt records the reporting correction.
"""
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from verify_recursive_acl_restore_partial_cancel import verify
from acl_source_apply_missing_proof import source_closure

NAME='recursive-acl-restore-partial-logout-265f8993c57441a0ad2ad5f4324c7801'
SEAL='395bf7d5830b58f6f69e52831e7c4b15cacc2ef6348cc52560c49373e4f0a407'

def target(name):
    assert name==NAME
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name

def main(name,revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(name);repo=p.parents[2]
    assert subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()==revision
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    hashes=source_closure(repo,revision,seeds=('recheck_recursive_acl_restore_partial_logout_report.py',))
    for filename,sha in hashes.items():assert hashlib.sha256((repo/'deploy/validation'/filename).read_bytes()).hexdigest()==sha
    raw=(p/'ORIGINAL_EVIDENCE_SHA256.json').read_bytes();assert hashlib.sha256(raw).hexdigest()==SEAL;original=json.loads(raw)
    for filename,sha in original.items():assert hashlib.sha256((p/filename).read_bytes()).hexdigest()==sha
    result=verify(name,False,logout=True,legacy_report=True)
    assert result['partialCancellation'] is False and result['partialLogout'] is result['legacyOutcomeFlagIncorrect'] is True
    for filename,sha in original.items():assert hashlib.sha256((p/filename).read_bytes()).hexdigest()==sha
    q=p.parent/('restore-partial-logout-report-recheck-'+uuid.uuid4().hex);q.mkdir(mode=0o700)
    with (q/'verification.json').open('x') as f:json.dump(result,f)
    with (q/'provenance.json').open('x') as f:json.dump(dict(originalEvidence=name,originalSealSha256=SEAL,originalFiles=len(original),originalFilesUnchanged=True,fixSourceRevision=revision,fixSourceSha256=hashes,originalReportAccepted=False,originalOperationsReplayed=False),f)
    manifest={f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in q.iterdir() if f.is_file()}
    with (q/'EVIDENCE_SHA256.json').open('x') as f:json.dump(manifest,f,sort_keys=True,indent=2)
    print(json.dumps(dict(reportRecheck='passed',evidence=q.name,files=len(manifest),manifestSha256=hashlib.sha256((q/'EVIDENCE_SHA256.json').read_bytes()).hexdigest(),**result)))

if __name__=='__main__':
    try:assert len(sys.argv)==3;main(*sys.argv[1:])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(reportRecheck='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
