#!/usr/bin/env python3
"""Fixed-path, root-only private notification registry; never emits private values.

Input through stdin only. A single fsync/rename publishes bindings and bounded
idempotency receipts together, so a lost reply never creates a second mutation.
No network calls or arbitrary filesystem/command parameters are accepted.
"""
import contextlib
import hashlib
import hmac
import ipaddress
import json
import os
import re
import secrets
import stat
import sys
import time
import unicodedata
import uuid
from urllib.parse import urlsplit, parse_qsl

LIMIT = 65536
PROVIDERS = {'Email', 'Webhook', 'DingTalk', 'WeCom', 'Feishu'}
TARGET_FIELDS = {'provider', 'url', 'smtpHost', 'smtpPort', 'tls', 'from', 'to', 'allowedAddresses', 'allowPrivateNetwork', 'allowHttp', 'minimumIntervalSeconds'}
CREDENTIAL_FIELDS = {'bearerToken', 'signingSecret', 'username', 'password'}


class Rejected(Exception):
    def __init__(self, code):
        super().__init__(code)
        self.code = code


def require(condition, code='CONFIG_INVALID'):
    if not condition:
        raise Rejected(code)


def duplicate_safe(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result)
        result[key] = value
    return result


def decode(raw):
    require(0 < len(raw) <= LIMIT)
    try:
        value = json.loads(raw, object_pairs_hook=duplicate_safe, parse_constant=lambda _: (_ for _ in ()).throw(Rejected('CONFIG_INVALID')))
    except (ValueError, UnicodeError, RecursionError):
        raise Rejected('CONFIG_INVALID') from None
    def depth(item, level=0):
        require(level <= 8)
        if isinstance(item, dict):
            for child in item.values():
                depth(child, level + 1)
        elif isinstance(item, list):
            for child in item:
                depth(child, level + 1)
    depth(value)
    require(isinstance(value, dict))
    return value


def canonical(value):
    return json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def text(value, maximum=4096):
    return isinstance(value, str) and 0 < len(value) <= maximum and all(unicodedata.category(c) != 'Cc' for c in value)


def allowed_ip(value, private):
    try:
        address = ipaddress.ip_address(value)
        if isinstance(address, ipaddress.IPv6Address) and address.ipv4_mapped:
            address = address.ipv4_mapped
    except ValueError:
        return False
    if address.is_loopback or address.is_unspecified or address.is_link_local or address.is_multicast:
        return False
    if address.version == 4:
        number = int(address)
        if number >> 24 == 0 or number >= int(ipaddress.ip_address('224.0.0.0')):
            return False
        internal = any(address in ipaddress.ip_network(n) for n in ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', '100.64.0.0/10'))
    else:
        if address in ipaddress.ip_network('fec0::/10') or any(address in ipaddress.ip_network(n) for n in ('::/96', '64::/16', '2002::/16', '2001::/32')):
            return False
        internal = address in ipaddress.ip_network('fc00::/7')
        if not internal and address not in ipaddress.ip_network('2000::/3'):
            return False
    return private or not internal


def validate(target, credential):
    require(isinstance(target, dict) and not target.keys() - TARGET_FIELDS)
    provider = target.get('provider')
    require(provider in PROVIDERS)
    for key in ('allowPrivateNetwork', 'allowHttp'):
        require(type(target.get(key, False)) is bool)
    interval = target.get('minimumIntervalSeconds', 5)
    require(type(interval) is int and 5 <= interval <= 300)
    addresses = target.get('allowedAddresses')
    require(isinstance(addresses, list) and len(addresses) <= 8)
    private = target.get('allowPrivateNetwork', False)
    require(not private or len(addresses) > 0)
    require(all(isinstance(value, str) and allowed_ip(value, private) for value in addresses))
    require(credential is None or isinstance(credential, dict) and not credential.keys() - CREDENTIAL_FIELDS)
    credential = credential or {}
    for value in list(target.get(key) for key in ('url', 'smtpHost', 'from')) + list(credential.values()):
        require(value is None or text(value))
    port = target.get('smtpPort', 587)
    require(type(port) is int and 1 <= port <= 65535 and target.get('tls', 'StartTls') in ('StartTls', 'SslOnConnect'))
    if provider == 'Email':
        require(text(target.get('smtpHost'), 253) and re.fullmatch(r'[A-Za-z0-9.:-]+', target['smtpHost']) is not None)
        host = target['smtpHost'].lower().rstrip('.')
        require(host not in ('localhost', 'metadata.google.internal') and not host.endswith('.localhost'), 'NETWORK_DENIED')
        recipients = target.get('to')
        require(isinstance(recipients, list) and 1 <= len(recipients) <= 10)
        require(all(text(mailbox, 254) and re.fullmatch(r'[^\s<>@]+@[^\s<>@]+', mailbox) for mailbox in [target.get('from'), *recipients]))
        require(target.get('url') is None and not target.get('allowHttp', False) and not credential.keys() - {'username', 'password'})
        require((credential.get('username') is None) == (credential.get('password') is None))
    else:
        require(text(target.get('url')) and target.get('smtpHost') is None and target.get('from') is None and target.get('to', []) == [])
        require(not credential.keys() - {'bearerToken', 'signingSecret'})
        try:
            uri = urlsplit(target['url'])
            require(uri.scheme in ('https', 'http') and uri.hostname is not None and uri.username is None and uri.password is None and not uri.fragment)
            require(uri.port is None or 1 <= uri.port <= 65535)
            host = uri.hostname.lower().rstrip('.')
            require(host not in ('localhost', 'metadata.google.internal') and not host.endswith('.localhost'), 'NETWORK_DENIED')
            require(uri.scheme != 'http' or target.get('allowHttp', False) and private)
            if provider != 'Webhook':
                require(uri.scheme == 'https' and uri.port in (None, 443) and credential.get('bearerToken') is None and not private and not target.get('allowHttp', False))
            if provider == 'DingTalk':
                require(host == 'oapi.dingtalk.com' and uri.path == '/robot/send' and len(parse_qsl(uri.query)) == 1 and parse_qsl(uri.query)[0][0] == 'access_token' and text(credential.get('signingSecret')))
            if provider == 'WeCom':
                require(host == 'qyapi.weixin.qq.com' and uri.path == '/cgi-bin/webhook/send' and len(parse_qsl(uri.query)) == 1 and parse_qsl(uri.query)[0][0] == 'key' and not credential)
            if provider == 'Feishu':
                require(host == 'open.feishu.cn' and uri.path.startswith('/open-apis/bot/v2/hook/') and not uri.query and text(credential.get('signingSecret')))
        except ValueError:
            raise Rejected('CONFIG_INVALID') from None
    # Numeric endpoints cannot bypass the same deny-list with a hostname field.
    try:
        ipaddress.ip_address(host)
    except ValueError:
        return
    require(allowed_ip(host, private), 'NETWORK_DENIED')
    if private:
        require(any(ipaddress.ip_address(host) == ipaddress.ip_address(value) for value in addresses), 'NETWORK_DENIED')


class Store:
    def __init__(self, directory, recipient_uid, revision_key=None):
        self.directory = directory
        self.uid = recipient_uid
        self.revision_key = revision_key or secrets.token_bytes(32)

    def revision(self, registry):
        # Do not expose a guessable password digest. Receipt results are excluded
        # to avoid a self-referential digest; all private bindings and nonce remain
        # bound so even manual edits invalidate a previously inspected version.
        core = {key: value for key, value in registry.items() if key != 'receipts'}
        return hmac.new(self.revision_key, canonical(core), hashlib.sha256).hexdigest().upper()

    def binding_revision(self, registry, alias):
        binding = {'target': registry['targets'].get(alias), 'credential': registry['credentials'].get(alias)}
        return hmac.new(self.revision_key, canonical(binding), hashlib.sha256).hexdigest()

    @contextlib.contextmanager
    def locked(self):
        import fcntl
        fd = os.open(self.directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            metadata = os.fstat(fd)
            require(metadata.st_uid == os.geteuid() and not metadata.st_mode & 0o022, 'FILE_UNSAFE')
            lock = os.open('.registry.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=fd)
            try:
                require(os.fstat(lock).st_uid == os.geteuid() and stat.S_IMODE(os.fstat(lock).st_mode) == 0o600 and os.fstat(lock).st_nlink == 1, 'FILE_UNSAFE')
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                yield fd
            except BlockingIOError:
                raise Rejected('CONFIG_BUSY') from None
            finally:
                os.close(lock)
        finally:
            os.close(fd)

    def read(self, fd):
        try:
            source = os.open('registry.json', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
        except FileNotFoundError:
            return {'version': 1, 'targets': {}, 'credentials': {}, 'receipts': {}}, 'EMPTY'
        try:
            metadata = os.fstat(source)
            require(stat.S_ISREG(metadata.st_mode) and metadata.st_nlink == 1 and metadata.st_uid == self.uid and stat.S_IMODE(metadata.st_mode) == 0o600 and 0 < metadata.st_size <= LIMIT, 'FILE_UNSAFE')
            raw = os.read(source, LIMIT + 1)
        finally:
            os.close(source)
        registry = decode(raw)
        require(type(registry.get('version')) is int and registry['version'] == 1 and not registry.keys() - {'version', 'targets', 'credentials', 'receipts', 'revisionNonce'})
        for key in ('targets', 'credentials', 'receipts'):
            registry.setdefault(key, {})
            require(isinstance(registry[key], dict))
        require(len(registry['targets']) <= 32 and len(registry['credentials']) <= 32 and len(registry['receipts']) <= 64)
        for key in [*registry['targets'], *registry['credentials']]:
            require(re.fullmatch(r'[a-z][a-z0-9-]{0,63}', key) is not None)
        nonce = registry.get('revisionNonce')
        require(nonce is None or isinstance(nonce, str) and re.fullmatch(r'[A-F0-9]{64}', nonce) is not None)
        return registry, self.revision(registry)

    def execute(self, action, request):
        require(action in ('inspect', 'apply', 'remove'))
        allowed = {'channelId', 'operationId'} if action == 'inspect' else {'channelId', 'operationId', 'expectedRevision', 'target', 'credential', 'credentialMode'}
        require(isinstance(request, dict) and not request.keys() - allowed)
        try:
            channel = uuid.UUID(request['channelId'])
            require(channel.int != 0 and str(channel) == request['channelId'])
            operation = None if action == 'inspect' and request.get('operationId') is None else uuid.UUID(request['operationId'])
            require(operation is None or operation.int != 0 and str(operation) == request['operationId'])
        except (KeyError, ValueError, TypeError, AttributeError):
            raise Rejected('CONFIG_INVALID') from None
        alias = 'wd-' + channel.hex
        with self.locked() as fd:
            registry, revision = self.read(fd)
            if action == 'inspect' and operation is not None:
                receipt = registry['receipts'].get(str(operation))
                if receipt is not None:
                    require(receipt.get('channelId') == str(channel) and isinstance(receipt.get('result'), dict), 'IDEMPOTENCY_CONFLICT')
                    return {**receipt['result'], 'replayed': True, 'bindingCurrent': receipt.get('bindingRevision') == self.binding_revision(registry, alias)}
            if action != 'inspect':
                input_hash = hashlib.sha256(canonical({'action': action, 'request': request})).hexdigest()
                receipt = registry['receipts'].get(str(operation))
                if receipt is not None:
                    require(receipt.get('inputHash') == input_hash and receipt.get('channelId') == str(channel), 'IDEMPOTENCY_CONFLICT')
                    require(isinstance(receipt.get('result'), dict), 'CONFIG_INVALID')
                    return {**receipt['result'], 'replayed': True, 'bindingCurrent': receipt.get('bindingRevision') == self.binding_revision(registry, alias)}
                require(request.get('expectedRevision') == revision, 'VERSION_CONFLICT')
                now = int(time.time())
                registry['receipts'] = {key: value for key, value in registry['receipts'].items() if isinstance(value, dict) and type(value.get('createdAt')) is int and value['createdAt'] > now - 86400}
                require(len(registry['receipts']) < 64, 'RECEIPT_BUDGET')
                if action == 'apply':
                    mode = request.get('credentialMode')
                    require(mode in ('keep', 'replace', 'remove'))
                    require(mode == 'replace' or request.get('credential') is None)
                    credential = registry['credentials'].get(alias) if mode == 'keep' else request.get('credential') if mode == 'replace' else None
                    require(mode != 'replace' or isinstance(credential, dict))
                    validate(request.get('target'), credential)
                    registry['targets'][alias] = request['target']
                    if credential is None:
                        registry['credentials'].pop(alias, None)
                    else:
                        registry['credentials'][alias] = credential
                else:
                    require(not request.keys() & {'target', 'credential', 'credentialMode'})
                    registry['targets'].pop(alias, None)
                    registry['credentials'].pop(alias, None)
                require(len(registry['targets']) <= 32 and len(registry['credentials']) <= 32, 'BINDING_BUDGET')
                registry['revisionNonce'] = secrets.token_hex(32).upper()
                revision = self.revision(registry)
                result = {'operationId': str(operation), 'applied': True, 'replayed': False, 'revision': revision,
                          'destinationReference': alias, 'credentialReference': alias if alias in registry['credentials'] else None,
                          'hasTarget': alias in registry['targets'], 'hasCredential': alias in registry['credentials'], 'bindingCurrent': True}
                registry['receipts'][str(operation)] = {'inputHash': input_hash, 'channelId': str(channel), 'createdAt': now, 'result': result, 'bindingRevision': self.binding_revision(registry, alias)}
                raw = canonical(registry)
                require(len(raw) <= LIMIT, 'CONFIG_BUDGET')
                temporary = '.registry-' + secrets.token_hex(16)
                out = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=fd)
                try:
                    os.fchown(out, self.uid, os.getgid())
                    with os.fdopen(out, 'wb', closefd=False) as stream:
                        stream.write(raw)
                        stream.flush()
                        os.fsync(out)
                    os.replace(temporary, 'registry.json', src_dir_fd=fd, dst_dir_fd=fd)
                    os.fsync(fd)
                finally:
                    os.close(out)
                    try:
                        os.unlink(temporary, dir_fd=fd)
                    except FileNotFoundError:
                        pass
            return {'operationId': str(operation) if operation else None, 'applied': action != 'inspect', 'replayed': False, 'revision': revision,
                    'destinationReference': alias, 'credentialReference': alias if alias in registry['credentials'] else None,
                    'hasTarget': alias in registry['targets'], 'hasCredential': alias in registry['credentials'], 'bindingCurrent': True}


def main():
    try:
        require(os.geteuid() == 0 and len(sys.argv) == 2, 'ROOT_REQUIRED')
        action = sys.argv[1]
        require(action in ('inspect', 'apply', 'remove'))
        # Runtime UID is deliberately fixed to the pinned Whale Deck Worker image.
        with open('/etc/whaledeck/agent-capability.key', 'rb') as source:
            revision_key = source.read(65)
        require(len(revision_key) == 32, 'FILE_UNSAFE')
        result = Store('/etc/whaledeck/notifications', 1654, revision_key).execute(action, decode(sys.stdin.buffer.read(LIMIT + 1)))
        print(json.dumps(result, separators=(',', ':')))
    except Rejected as error:
        print(json.dumps({'errorCode': error.code}), file=sys.stderr)
        return 2
    except Exception:
        print('{"errorCode":"CONFIG_IO_FAILED"}', file=sys.stderr)
        return 3
    return 0


if __name__ == '__main__':
    sys.exit(main())
