#!/usr/bin/env python3
"""Frozen, read-only current ACK UI supplement to one already sealed real case."""
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import struct
import subprocess
import sys
import uuid
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import installed, snapshot, hdd_snapshot
from recursive_acl_restore_note_proof import verify
from validate_container_upload_bff import temporary_user
from validate_container_recursive_acl_faults import preservation_snapshot
from validate_container_recursive_acl_agent import host_identity
from validate_container_recursive_acl_restore_agent import core_health
from validate_container_recursive_acl_restore_browser import sql_snapshot
from validate_container_file_publishing import common
from validate_recursive_acl_restore_paging import database, restore_database
from verify_container_recursive_acl import private_json
from validate_container_upload_recovery_agent import trusted

SOURCES = ('validate_recursive_acl_restore_unknown_note.py','validate_recursive_acl_restore_unknown_note.mjs',
    'installed_ui_restore_note_visibility.mjs','recursive_acl_restore_note_proof.py','test_recursive_acl_restore_note_proof.py')


def baseline(revision):
    io = installed(); core = common.core_snapshot()
    return dict(protected=preservation_snapshot(io.storage), sourceRoot=snapshot(io.sourceState),
        restoreRoot=snapshot(io.restoreState), restoreHdd=hdd_snapshot(io,True),
        directoryRoot=snapshot(Path('/var/lib/whaledeck-container-directories')),
        core=core, health=core_health(core), hosts=host_identity(revision),
        sourceDatabase=database(), restoreDatabase=restore_database())


def main(name):
    assert os.geteuid() == 0 and re.fullmatch('recursive-acl-restore-unknown-rename-[a-f0-9]{32}',name)
    os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    repo = Path(__file__).resolve().parents[2]; original = repo/'deploy/artifacts'/name; trusted(original,True)
    def original_seal():
        m=private_json(original/'EVIDENCE_SHA256.json')
        assert set(m)=={str(p.relative_to(original)) for p in original.rglob('*') if p.is_file()}-{'EVIDENCE_SHA256.json'}
        for n,v in m.items():
            relative=Path(n);assert not relative.is_absolute() and '..' not in relative.parts and relative.as_posix()==n
            p=original/relative; assert p.is_relative_to(original) and not p.is_symlink() and p.stat().st_nlink==1
            raw=p.read_bytes(); assert len(raw)==v['sizeBytes'] and hashlib.sha256(raw).hexdigest()==v['sha256']
        return hashlib.sha256((original/'EVIDENCE_SHA256.json').read_bytes()).hexdigest()
    seal=original_seal(); old=private_json(original/'baseline.json'); terminal=private_json(original/'acknowledged.sql.json')
    current=sql_snapshot(terminal['row']['id']); typed_equal(current,terminal)
    accepted=private_json(original/'final-independent.json'); assert accepted['sourceAndContainerAbsent'] is True and accepted['knownActualChanges']==1
    revision=old['revision'];sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    hashes=source_closure(repo,sha,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h for n,h in hashes.items())
    evidence=repo/'deploy/artifacts'/('restore-unknown-note-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    save=lambda n,v:(evidence/n).write_text(json.dumps(v,ensure_ascii=False))
    before=baseline(revision); assert before['restoreDatabase']['active']==0
    save('baseline.json',before);save('source.json',dict(revision=revision,validationSourceRevision=sha,validationSourceSha256=hashes,original=name,originalSeal=seal));save('before.sql.json',current)
    print(json.dumps(dict(currentUnknownNote='started',evidence=evidence.name)),flush=True)
    with temporary_user(True) as credentials:
        with (evidence/'browser.stdout').open('x') as out,(evidence/'browser.stderr').open('x') as err:
            process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_recursive_acl_restore_unknown_note.mjs'),revision],
                stdin=subprocess.PIPE,text=True,stdout=out,stderr=err,start_new_session=True)
            try:
                process.communicate(json.dumps(dict(fixture=dict(revision=revision,jobId=current['row']['execution_job_id'],intentId=current['row']['id'],evidence=str(evidence)),
                    credentials=dict(zip(('username','password'),credentials)))),timeout=240)
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        assert os.getpgid(process.pid)==process.pid;os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=10)
            result=process
        assert result.returncode==0,'CURRENT_NOTE_BROWSER_FAILED'
    packet=private_json(evidence/'browser.json');proof=verify(packet,current,revision)
    typed_equal(sql_snapshot(current['row']['id']),current);after=baseline(revision);typed_equal(after,before);assert original_seal()==seal
    expected={'job-fault-acknowledged-execution-counts.png','job-fault-acknowledged-execution-metadata.png'}
    expected|={f'note-{w}-{t}-{n}.png' for w in (320,1440) for t in ('dark','light') for n in ('unknown','acknowledged')}
    assert {p.name for p in evidence.glob('*.png')}==expected
    for p in evidence.glob('*.png'):
        raw=p.read_bytes();w=1440 if p.name.startswith('note-1440-') else 320
        assert len(raw)>1000 and raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==(w,600)
    save('after.sql.json',current);save('after.json',after);save('verified.json',proof)
    print(json.dumps(dict(currentUnknownNote='passed',evidence=evidence.name,**proof)),flush=True)


if __name__=='__main__':
    try:
        assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        print(json.dumps(dict(currentUnknownNote='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
