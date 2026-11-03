"""QA-only replacement of a NEW verified capture; no installed grant or CLI.

Original capture stays untouched in OwnedSourceQuarantine. Only timestamp +1
and its canonical digest differ in a fully written separate temporary inode.
Publish/withdraw use NOREPLACE. Recovery never removes an unproved file; only
the exact QA replacement is archived, then unlinked, after original restoration.
"""
import contextlib
import hashlib
import json
import os
import re

from owned_acl_source_quarantine import read_file,unique_json,private_directory,catalogue,move_without_replace

JOURNAL='source-replacement.json'
ARCHIVE='replacement-metadata.json'
LEAF='candidate.json'
DOMAIN=b'whaledeck-container-acl-history-v1\n'


def encode(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def changed_metadata(raw):
    value=unique_json(raw)
    assert set(value)=={'schemaVersion','resourceType','storageId','operationId','owner','plan','createdAtUnixMs','historyDigest'}
    assert type(value['createdAtUnixMs']) is int and 0<=value['createdAtUnixMs']<9223372036854775807
    assert value['schemaVersion']==1 and value['resourceType']=='ContainerAclMetadata' and value['storageId']=='database-platform.backup.hdd'
    digest=lambda obj:hashlib.sha256(DOMAIN+encode({k:v for k,v in obj.items() if k!='historyDigest'})).hexdigest().upper()
    assert digest(value)==value['historyDigest']
    original=value['historyDigest'];value['createdAtUnixMs']+=1;value['historyDigest']=digest(value)
    assert value['historyDigest']!=original
    return encode(value)


def save(fd,name,raw):
    f=os.open(name,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600,dir_fd=fd)
    try:
        with os.fdopen(f,'wb',closefd=False) as output:output.write(raw);output.flush();os.fsync(f)
    finally:os.close(f)
    os.fsync(fd)


class OwnedSourceReplacement:
    def __init__(self,quarantine):self.q=quarantine

    def prepare(self):
        q=self.q;s=q.state();q.old_unchanged(s,present=True)
        info,raw=read_file(q.fd,s['name']);assert info==s['original']
        replacement=changed_metadata(raw)
        stage='.wd-qa-acl-replacement-'+s['captureId'];q.check()
        os.mkdir(stage,0o700,dir_fd=q.fd)
        fd=os.open(stage,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=q.fd)
        try:
            identity=private_directory(fd);save(fd,LEAF,replacement);after,_=read_file(fd,LEAF)
            assert after['frame'][:2]!=info['frame'][:2]
            state=dict(schemaVersion=1,captureId=s['captureId'],stage=stage,stageIdentity=identity,replacement=after,
                armedSha256=read_file(q.journal,'source-quarantine.json',4*1024**2)[0]['sha256'],
                originalDigest=unique_json(raw)['historyDigest'],replacementDigest=unique_json(replacement)['historyDigest'])
            save(q.journal,JOURNAL,encode(state));os.fsync(q.fd)
            return state
        finally:os.close(fd)

    def state(self):
        q=self.q;s=q.state();r=unique_json(read_file(q.journal,JOURNAL)[1])
        assert set(r)=={'schemaVersion','captureId','stage','stageIdentity','replacement','armedSha256','originalDigest','replacementDigest'}
        assert type(r['schemaVersion']) is int and r['schemaVersion']==1 and r['captureId']==s['captureId']
        assert r['stage']=='.wd-qa-acl-replacement-'+s['captureId']
        assert r['armedSha256']==read_file(q.journal,'source-quarantine.json',4*1024**2)[0]['sha256']
        assert len(r['stageIdentity'])==5 and all(type(x) is int for x in r['stageIdentity'])
        assert set(r['replacement'])=={'frame','sha256'} and len(r['replacement']['frame'])==9
        assert all(type(x) is int for x in r['replacement']['frame'])
        assert re.fullmatch('[a-f0-9]{64}',r['replacement']['sha256'])
        assert all(type(r[k]) is str and re.fullmatch('[A-F0-9]{64}',r[k]) for k in ('originalDigest','replacementDigest'))
        assert r['originalDigest']!=r['replacementDigest']
        assert r['replacement']['frame'][:2]!=s['original']['frame'][:2]
        return s,r

    def old_unchanged(self,s):
        current=catalogue(self.q.fd)
        assert {k:v for k,v in current.items() if k!=s['name']}==s['old'],'QA_REPLACEMENT_UNRELATED_CAPTURE_CHANGED'

    def stage_fd(self,r):
        fd=os.open(r['stage'],os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=self.q.fd)
        try:assert private_directory(fd)==r['stageIdentity'],'QA_REPLACEMENT_STAGE_CHANGED'
        except BaseException:os.close(fd);raise
        return fd

    def original_held(self,s):
        q=self.q;proof=unique_json(read_file(q.journal,'source-quarantine-directory.json')[1])
        assert proof['captureId']==s['captureId'] and proof['armedSha256']==read_file(q.journal,'source-quarantine.json',4*1024**2)[0]['sha256']
        fd=os.open(s['quarantine'],os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=q.fd)
        try:
            assert private_directory(fd)==proof['identity'] and os.listdir(fd)==[s['name']]
            q.same_original(read_file(fd,s['name'])[0],s['original'])
        finally:os.close(fd)

    def publish(self):
        q=self.q;s,r=self.state();self.old_unchanged(s);self.original_held(s)
        fd=self.stage_fd(r)
        try:
            assert os.listdir(fd)==[LEAF]
            q.same_original(read_file(fd,LEAF)[0],r['replacement']);q.check()
            move_without_replace(fd,LEAF,q.fd,s['name']);os.fsync(q.fd);os.fsync(fd)
            q.same_original(read_file(q.fd,s['name'])[0],r['replacement']);self.old_unchanged(s)
        finally:os.close(fd)
        return dict(captureId=s['captureId'],sameCaptureId=True,canonicalSourceExists=True,
            originalDigest=r['originalDigest'],replacementDigest=r['replacementDigest'],originalPreserved=True,
            oldCapturesUnchanged=len(s['old']))

    def verify_archive(self,r):
        info,raw=read_file(self.q.journal,ARCHIVE)
        assert info['sha256']==r['replacement']['sha256'] and unique_json(raw)['historyDigest']==r['replacementDigest']

    def restore(self):
        q=self.q
        try:s,r=self.state()
        except FileNotFoundError:
            # Preparation may have failed before a durable replacement journal.
            # Restore only the original; do not delete an unproved staging dir.
            return q.restore()
        self.old_unchanged(s)
        try:fd=self.stage_fd(r)
        except FileNotFoundError:
            q.old_unchanged(s,present=True);self.verify_archive(r)
            return dict(captureId=s['captureId'],originalRestored=True,replacementArchived=True,alreadyPresent=True)
        try:
            names=os.listdir(fd);assert set(names)<= {LEAF},'QA_REPLACEMENT_STAGE_FOREIGN_ENTRY'
            try:canonical=read_file(q.fd,s['name'])[0]
            except FileNotFoundError:canonical=None
            if canonical and canonical['sha256']==r['replacement']['sha256']:
                assert not names,'QA_REPLACEMENT_DUPLICATE_OR_OCCUPIED_STAGE'
                self.original_held(s)
                q.same_original(canonical,r['replacement']);q.check()
                move_without_replace(q.fd,s['name'],fd,LEAF);os.fsync(fd);os.fsync(q.fd)
                names=[LEAF]
            elif canonical:
                q.same_original(canonical,s['original'])
            # Exact original recovery has priority once the canonical name is free.
            restored=q.restore();q.old_unchanged(s,present=True)
            if names:
                info,raw=read_file(fd,LEAF);q.same_original(info,r['replacement'])
                assert unique_json(raw)['historyDigest']==r['replacementDigest']
                try:save(q.journal,ARCHIVE,raw)
                except FileExistsError:self.verify_archive(r)
                self.verify_archive(r)
                # Delete ONLY this newly created, independently archived QA inode.
                q.same_original(read_file(fd,LEAF)[0],r['replacement']);q.check()
                assert private_directory(fd)==r['stageIdentity']
                os.unlink(LEAF,dir_fd=fd);os.fsync(fd)
            else:self.verify_archive(r)
            assert not os.listdir(fd)
            assert private_directory(fd)==r['stageIdentity']
            named=os.stat(r['stage'],dir_fd=q.fd,follow_symlinks=False)
            assert [named.st_dev,named.st_ino,named.st_mode,named.st_uid,named.st_gid]==r['stageIdentity']
            q.check();os.rmdir(r['stage'],dir_fd=q.fd);os.fsync(q.fd)
        finally:os.close(fd)
        return {**restored,'replacementArchived':True}

    @contextlib.contextmanager
    def replaced(self):
        try:
            self.prepare();self.q.hold();yield self.publish()
        finally:self.restore()
