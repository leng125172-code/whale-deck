"""Frozen minor40 reader for schema fencing compatibility tests only.

Source: c521c39:deploy/host/whaledeck-container-upload.py ContainerUploads.receipt.
Do not use this historical reader in product code.
Method SHA256: fd1552191f15ca2ed0786a8bcc8fcb5b1dc3d220fa4409d1425d3c321e93de44
"""
import os
import re
import stat


def receipt(self, state, operation):
    try: info = os.stat(operation + '.json', dir_fd=state, follow_symlinks=False)
    except FileNotFoundError: return None
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
        self.reject('RECEIPT_INVALID')
    value = self.f.read_receipt(state, operation)
    if value is None: return None
    if type(value) is dict and value.get('schemaVersion') == 2:
        expected = {'schemaVersion', 'resourceType', 'state', 'operationId', 'owner', 'containerId', 'instanceId', 'targetProof', 'createdAtUnixMs'}
        if set(value) != expected or type(value['schemaVersion']) is not int or value['resourceType'] != 'ContainerUploadCancellation' or value['state'] != 'Prevented':
            self.reject('RECEIPT_INVALID')
        self.scope_request({key: value[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')})
        if value['operationId'] != operation or type(value['targetProof']) is not str or not re.fullmatch('[A-F0-9]{64}', value['targetProof']) or \
                type(value['createdAtUnixMs']) is not int or not 0 <= value['createdAtUnixMs'] <= 253402300799999:
            self.reject('RECEIPT_INVALID')
        return value
    if type(value) is not dict or type(value.get('schemaVersion')) is not int or value['schemaVersion'] != 1:
        self.reject('RECEIPT_INVALID')
    request = value.get('request')
    if self.upload_request(request) != value.get('fingerprint') or any(value.get(key) != request[key]
            for key in ('operationId', 'owner', 'containerId', 'instanceId')) or value['operationId'] != operation:
        self.reject('RECEIPT_INVALID')
    if value.get('state') not in ('Receiving', 'Verified', 'Staged', 'Interrupted', 'Aborted', 'Discarded', 'Unknown',
            'PublishPreparing', 'PublishApplying', 'PublishInterrupted', 'Published', 'PublishDiscarding', 'PublishDiscarded'):
        self.reject('RECEIPT_INVALID')
    if type(value.get('targetProof')) is not str or not re.fullmatch('[A-F0-9]{64}', value['targetProof']):
        self.reject('RECEIPT_INVALID')
    return value

