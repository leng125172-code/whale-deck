#!/usr/bin/env python3
"""Internal restoration attributes, derived only from a verified HDD capture.

No CLI or raw uid/mode input. The signed adapter must bind restoreSource in
the Stage request. No production adapter advertises this engine yet.
"""
import os
import stat


class ContainerRestorations:
    def __init__(self, uploads, history):
        self.u, self.h = uploads, history
        if history.u is not uploads: uploads.reject('RESTORE_ENGINE_MISMATCH')

    def capture(self, request, session):
        source = request['restoreSource']
        location = {key: request[key] for key in ('containerId', 'instanceId')}
        location.update(path=source['path'], captureId=source['captureId'], expectedCaptureHash=source['captureHash'])
        with self.h.verified_source(location, session) as (metadata, _descriptor):
            if metadata['sizeBytes'] != request['sizeBytes'] or metadata['sha256'] != request['sha256']:
                self.u.reject('RESTORE_CONTENT_MISMATCH')
            identity = metadata['sourceIdentity']
            # Setuid/setgid/sticky and richer ACL/xattr policy are deliberately
            # not silently reduced to ordinary permissions on a "full" restore.
            if stat.S_IMODE(identity[2]) & 0o7000: self.u.reject('RESTORE_MODE_UNSUPPORTED')
            attributes = {'uid': identity[3], 'gid': identity[4],
                'mode': stat.S_IMODE(identity[2]), 'modifiedNs': identity[7]}
            self.permitted(attributes, session)
        return attributes

    def permitted(self, attributes, session):
        self.u.restoration_attributes(attributes)
        session.current()
        pid = self.u.r.target_binding(session.request['containerId'], session.request['instanceId'])
        for key, filename in (('uid', 'uid_map'), ('gid', 'gid_map')):
            descriptor = os.open('/proc/' + str(pid) + '/' + filename, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
            try:
                data = os.read(descriptor, 65537)
                if len(data) > 65536 or os.read(descriptor, 1): self.u.reject('RESTORE_OWNER_UNMAPPED')
            finally: os.close(descriptor)
            if not self.mapped(data, attributes[key]): self.u.reject('RESTORE_OWNER_UNMAPPED')
        session.current()

    def mapped(self, data, identifier):
        rows = data.splitlines()
        if not rows or len(rows) > 340: self.u.reject('RESTORE_OWNER_UNMAPPED')
        ranges = []
        for row in rows:
            fields = row.split()
            if len(fields) != 3 or any(not value.isdigit() for value in fields): self.u.reject('RESTORE_OWNER_UNMAPPED')
            inside, outside, length = map(int, fields)
            if length <= 0 or inside < 0 or outside < 0 or inside + length > 2**32 or outside + length > 2**32:
                self.u.reject('RESTORE_OWNER_UNMAPPED')
            if any(inside < old_inside + old_length and old_inside < inside + length or
                   outside < old_outside + old_length and old_outside < outside + length
                   for old_inside, old_outside, old_length in ranges): self.u.reject('RESTORE_OWNER_UNMAPPED')
            ranges.append((inside, outside, length))
        return any(outside <= identifier < outside + length for _, outside, length in ranges)

    def apply(self, descriptor, attributes, session):
        self.permitted(attributes, session)
        os.fchown(descriptor, attributes['uid'], attributes['gid'])
        os.fchmod(descriptor, attributes['mode'])
        current = os.fstat(descriptor)
        os.utime(descriptor, ns=(current.st_atime_ns, attributes['modifiedNs']))
        if not self.matches(os.fstat(descriptor), attributes): self.u.reject('RESTORE_ATTRIBUTES_CHANGED')

    @staticmethod
    def matches(info, attributes):
        return (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns) == \
            (attributes['uid'], attributes['gid'], attributes['mode'], attributes['modifiedNs'])
