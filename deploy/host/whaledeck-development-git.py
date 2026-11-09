#!/usr/bin/env python3
"""Rootless acquisition of an exact Git snapshot for workspace source import."""

import base64
import contextlib
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, HTTPServer
import ipaddress
import json
import os
from pathlib import Path
import re
import resource
import secrets
import signal
import socket
import ssl
import stat
import subprocess
import sys
import tarfile
import tempfile
import threading
import unicodedata
from urllib.parse import urlsplit

GIT = '/usr/bin/git'
PRLIMIT = '/usr/bin/prlimit'
REGISTRY = Path('/etc/whaledeck/development-git.json')
SCRATCH = Path('/tmp')
MAX_ARCHIVE = 128 * 1024**2
MAX_SCRATCH = 512 * 1024**2
MAX_ENTRIES = 10000
MAX_REGISTRY = 65536
RELAY_URL = 'http://workspace-git.invalid/source.git'
ADVERTISEMENT = 'application/x-git-upload-pack-advertisement'
PACK_RESULT = 'application/x-git-upload-pack-result'
ID = re.compile(r'[a-z0-9]+(?:-[a-z0-9]+)*\Z', re.ASCII)
HOST = re.compile(r'[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+\Z', re.ASCII)
URL_PATH = re.compile(r'/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\Z', re.ASCII)
REF = re.compile(r'refs/[A-Za-z0-9_./-]+\Z', re.ASCII)
OID = re.compile(rb'(?:[a-f0-9]{40}|[a-f0-9]{64})\Z')
LOCAL_NETWORKS = tuple(map(ipaddress.ip_network, ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', 'fc00::/7')))
TRANSITION = tuple(map(ipaddress.ip_network, ('64:ff9b::/96', '64:ff9b:1::/48', '2001::/32', '2002::/16')))


class GitSourceError(Exception):
    pass


def refuse(reason):
    raise GitSourceError('WORKSPACE_GIT_' + reason)


def identifier(value):
    return isinstance(value, str) and len(value) <= 48 and ID.fullmatch(value)


def unique_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            refuse('METADATA_INVALID')
        value[key] = item
    return value


def metadata(value):
    try:
        return json.loads(value.decode('utf-8'), object_pairs_hook=unique_object)
    except (ValueError, RecursionError):
        refuse('METADATA_INVALID')


def read_registry(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as stream:
        info = os.fstat(stream.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o7022
                or not 0 < info.st_size <= MAX_REGISTRY):
            refuse('REGISTRY_UNSAFE')
        data = stream.read(MAX_REGISTRY + 1)
        if len(data) != info.st_size or os.fstat(stream.fileno()).st_ctime_ns != info.st_ctime_ns:
            refuse('REGISTRY_UNSAFE')
    return metadata(data)


def private_network(value):
    try:
        net = ipaddress.ip_network(value, strict=True)
    except ValueError:
        refuse('REGISTRY_INVALID')
    if not any(net.version == local.version and net.subnet_of(local) for local in LOCAL_NETWORKS):
        refuse('REGISTRY_INVALID')
    return net


def repository(registry, repo_id):
    if (not isinstance(registry, dict) or set(registry) != {'schemaVersion', 'repositories'}
            or type(registry['schemaVersion']) is not int or registry['schemaVersion'] != 1
            or not isinstance(registry['repositories'], dict) or len(registry['repositories']) > 128):
        refuse('REGISTRY_INVALID')
    value = registry['repositories'].get(repo_id)
    if value is None:
        refuse('REPOSITORY_NOT_APPROVED')
    if (not isinstance(value, dict) or set(value) != {'url', 'allowedCidrs', 'credentialRef'}
            or not isinstance(value['url'], str) or not isinstance(value['allowedCidrs'], list)
            or len(value['allowedCidrs']) > 16 or any(not isinstance(cidr, str) for cidr in value['allowedCidrs'])
            or (value['credentialRef'] is not None and not identifier(value['credentialRef']))):
        refuse('REGISTRY_INVALID')
    url = value['url']
    try:
        parsed = urlsplit(url)
        host, port = parsed.hostname, parsed.port or 443
    except ValueError:
        refuse('URL_INVALID')
    authority = f'{host}:{port}' if ':' in parsed.netloc else host
    if (len(url) > 512 or not host or len(host) > 253 or not HOST.fullmatch(host) or port < 1
            or parsed.scheme != 'https' or not URL_PATH.fullmatch(parsed.path)
            or any(part in ('.', '..') for part in parsed.path.split('/'))
            or url != f'https://{authority}{parsed.path}'):
        refuse('URL_INVALID')
    try:
        ipaddress.ip_address(host)
    except ValueError:
        pass
    else:
        refuse('URL_INVALID')
    return parsed.path, host, port, tuple(map(private_network, value['allowedCidrs'])), value['credentialRef']


def address_allowed(address, networks):
    if (address.is_loopback or address.is_link_local or address.is_multicast or address.is_unspecified
            or address.is_reserved or (address.version == 6 and
            (address.ipv4_mapped is not None or any(address in prefix for prefix in TRANSITION)))):
        return False
    return address.is_global or any(address.version == net.version and address in net for net in networks)


def pinned_address(host, port, networks):
    try:
        addresses = {ipaddress.ip_address(item[4][0])
                     for item in socket.getaddrinfo(host, port, type=socket.SOCK_STREAM, proto=socket.IPPROTO_TCP)}
    except (OSError, ValueError):
        refuse('DNS_UNAVAILABLE')
    if not 0 < len(addresses) <= 16 or any(not address_allowed(addr, networks) for addr in addresses):
        refuse('DESTINATION_NOT_APPROVED')
    selected = min(addresses, key=lambda addr: (addr.version, int(addr)))
    return str(selected)


def authorization(expected, supplied):
    if expected is None:
        if supplied is not None:
            refuse('CREDENTIAL_SCOPE')
        return None
    if not isinstance(supplied, dict) or supplied.get('ref') != expected:
        refuse('CREDENTIAL_SCOPE')
    if set(supplied) != {'ref', 'username', 'token'}:
        refuse('CREDENTIAL_INVALID')
    username, token = supplied['username'], supplied['token']
    if (not isinstance(username, str) or not re.fullmatch(r'[A-Za-z0-9_.@+-]{1,128}', username)
            or not isinstance(token, str) or not re.fullmatch(r'[\x21-\x7e]{1,1024}', token, re.ASCII)):
        refuse('CREDENTIAL_INVALID')
    encoded = base64.b64encode(f'{username}:{token}'.encode('ascii')).decode('ascii')
    return 'Basic ' + encoded


class PinnedHttps(http.client.HTTPSConnection):
    def __init__(self, host, port, address, context):
        super().__init__(host, port, timeout=15, context=context)
        self.address = address
        self.trust = context

    def connect(self):
        with contextlib.ExitStack() as cleanup:
            raw = cleanup.enter_context(socket.create_connection((self.address, self.port), self.timeout))
            self.sock = self.trust.wrap_socket(raw, server_hostname=self.host)
            cleanup.pop_all()


@contextlib.contextmanager
def source_relay(path, host, port, pin, auth):
    trust = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    trust.load_verify_locations('/etc/ssl/certs/ca-certificates.crt')
    admission = secrets.token_hex(32)
    admission_header = 'X-WhaleDeck-Git-Relay'
    remaining = MAX_ARCHIVE + 8 * 1024**2

    class Handler(BaseHTTPRequestHandler):
        def setup(self):
            self.request.settimeout(10)
            super().setup()

        def log_message(self, *_):
            pass

        def do_GET(self):
            self.forward('/info/refs?service=git-upload-pack', ADVERTISEMENT)

        def do_POST(self):
            self.forward('/git-upload-pack', PACK_RESULT)

        def forward(self, suffix, expected_type):
            nonlocal remaining
            token = self.headers.get(admission_header, '')
            if (self.path != RELAY_URL + suffix or self.headers.get('Transfer-Encoding')
                    or not token.isascii() or not secrets.compare_digest(token, admission)):
                self.send_error(403)
                return
            length = self.headers.get('Content-Length', '0')
            if not re.fullmatch(r'[0-9]{1,7}', length) or int(length) > 1024 * 1024:
                self.send_error(413)
                return
            headers = {'Accept': expected_type}
            if self.command == 'POST':
                if self.headers.get('Content-Type') != 'application/x-git-upload-pack-request':
                    self.send_error(415)
                    return
                headers['Content-Type'] = 'application/x-git-upload-pack-request'
                if self.headers.get('Content-Encoding') == 'gzip':
                    headers['Content-Encoding'] = 'gzip'
            protocol = self.headers.get('Git-Protocol')
            if protocol in ('version=1', 'version=2'):
                headers['Git-Protocol'] = protocol
            if auth:
                headers['Authorization'] = auth
            connection = PinnedHttps(host, port, pin, trust)
            try:
                body = self.rfile.read(int(length)) if self.command == 'POST' else None
                if body is not None and len(body) != int(length):
                    self.send_error(400)
                    return
                connection.request(self.command, path + suffix, body=body, headers=headers)
                with connection.getresponse() as response:
                    limit = min(remaining, 2 * 1024**2 if self.command == 'GET' else MAX_ARCHIVE)
                    if (response.status != 200 or response.headers.get_content_type() != expected_type
                            or response.headers.get('Content-Encoding', 'identity') != 'identity'
                            or limit <= 0 or response.length is not None and response.length > limit):
                        self.send_error(502)
                        return
                    self.send_response(200)
                    self.send_header('Content-Type', expected_type)
                    if response.length is not None:
                        self.send_header('Content-Length', str(response.length))
                    self.end_headers()
                    while block := response.read(min(65536, limit + 1)):
                        limit -= len(block)
                        remaining -= len(block)
                        if limit < 0:
                            break
                        self.wfile.write(block)
            except (OSError, http.client.HTTPException):
                self.close_connection = True
            finally:
                connection.close()

    # Git itself gets no credentials. It uses the same proxy for any
    # additional HTTP(S) requests a malicious protocol response can induce.
    with HTTPServer(('127.0.0.1', 0), Handler) as relay:
        thread = threading.Thread(target=relay.serve_forever, kwargs={'poll_interval': .05}, daemon=True)
        thread.start()
        try:
            yield (
                ('http.proxy', f'http://127.0.0.1:{relay.server_port}'),
                ('http.extraHeader', f'{admission_header}: {admission}'),
            )
        finally:
            relay.shutdown()
            thread.join()


def git_limits():
    limits = []
    for key, kind, limit in (('core', resource.RLIMIT_CORE, 0), ('cpu', resource.RLIMIT_CPU, 75),
                             ('fsize', resource.RLIMIT_FSIZE, MAX_ARCHIVE), ('as', resource.RLIMIT_AS, 384 * 1024**2)):
        soft, hard = resource.getrlimit(kind)
        limit = min([limit, *(value for value in (soft, hard) if value != resource.RLIM_INFINITY)])
        limits.append(f'--{key}={limit}:{limit}')
    return limits


def git_environment(home, settings=()):
    env = {
        'PATH': '/usr/bin:/bin', 'HOME': str(home), 'XDG_CONFIG_HOME': str(home),
        'LANG': 'C.UTF-8', 'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': '/dev/null',
        'GIT_TERMINAL_PROMPT': '0', 'GIT_ASKPASS': '/bin/false', 'GIT_NO_REPLACE_OBJECTS': '1',
        'GIT_ALLOW_PROTOCOL': 'http',
    }
    values = (
        ('core.hooksPath', '/dev/null'), ('init.templateDir', ''), ('credential.helper', ''),
        ('protocol.allow', 'never'), ('protocol.http.allow', 'always'), ('submodule.recurse', 'false'),
        ('fetch.recurseSubmodules', 'false'), ('fetch.fsckObjects', 'true'), ('fetch.unpackLimit', '1'),
        ('gc.auto', '0'), ('maintenance.auto', 'false'), ('pack.threads', '1'),
        ('http.followRedirects', 'false'), ('http.sslVerify', 'true'), ('http.postBuffer', '1048576'),
        ('http.lowSpeedLimit', '1024'), ('http.lowSpeedTime', '15'), *settings,
    )
    env['GIT_CONFIG_COUNT'] = str(len(values))
    for index, (name, value) in enumerate(values):
        env[f'GIT_CONFIG_KEY_{index}'] = name
        env[f'GIT_CONFIG_VALUE_{index}'] = value
    return env


@contextlib.contextmanager
def git_process(home, args, settings=(), *, pipe_input=False):
    with subprocess.Popen([PRLIMIT, *git_limits(), '--', GIT, *args], cwd=home, env=git_environment(home, settings),
                          stdin=subprocess.PIPE if pipe_input else subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                          start_new_session=True) as process:
        try:
            yield process
        finally:
            if process.poll() is None:
                with contextlib.suppress(ProcessLookupError):
                    os.killpg(process.pid, signal.SIGKILL)
                process.wait()


def git_small(home, args, reason, settings=(), *, limit=8192):
    with git_process(home, args, settings) as process:
        output = process.stdout.read(limit + 1)
        if len(output) > limit or process.wait() != 0:
            refuse(reason)
        return output


def requested_oid(home, url, ref, settings):
    if not isinstance(ref, str) or not 0 < len(ref) <= 255 or not ref.isascii():
        refuse('REF_INVALID')
    if OID.fullmatch(ref.encode('ascii')) and any(c != '0' for c in ref):
        return ref.encode('ascii')
    if ref != 'HEAD':
        if not REF.fullmatch(ref):
            refuse('REF_INVALID')
        git_small(home, ['check-ref-format', ref], 'REF_INVALID')
    reply = git_small(home, ['ls-remote', '--exit-code', '--', url, ref], 'REF_UNAVAILABLE', settings)
    name = ref.encode('ascii')
    hits = [oid for line in reply.splitlines() for oid, separator, path in [line.partition(b'\t')]
            if separator and path == name and OID.fullmatch(oid)]
    if len(hits) != 1 or not hits[0].strip(b'0'):
        refuse('REF_UNAVAILABLE')
    return hits[0]


def tree_records(stream):
    pending = b''
    while block := stream.read1(65536):
        *records, pending = (pending + block).split(b'\0')
        yield from records
        if len(pending) > 4300:
            refuse('PATH_INVALID')
    if pending:
        refuse('TREE_INVALID')


def file_path(raw):
    if not raw or len(raw) > 4096:
        refuse('PATH_INVALID')
    try:
        path = raw.decode('utf-8', 'strict')
    except UnicodeError:
        refuse('PATH_INVALID')
    parts = raw.split(b'/')
    if (len(parts) > 32 or any(part in (b'', b'.', b'..') or part.lower() == b'.git' or len(part) > 255 for part in parts)
            or '\\' in path or ':' in path
            or any(unicodedata.category(c) in ('Cc', 'Cf', 'Cs', 'Zl', 'Zp') for c in path)):
        refuse('PATH_INVALID')
    return path


class LimitedArchive:
    def __init__(self, stream):
        self.stream = stream
        self.length = 0
        self.digest = hashlib.sha256()

    def write(self, data):
        self.length += len(data)
        if self.length > MAX_ARCHIVE:
            refuse('ARCHIVE_LIMIT')
        self.digest.update(data)
        return self.stream.write(data)


def export_tree(home, repo, commit, sink):
    files, directories = set(), set()
    total = 0
    with git_process(home, ['--git-dir', str(repo), 'ls-tree', '-r', '-z', '--full-tree', commit]) as tree, \
            git_process(home, ['--git-dir', str(repo), 'cat-file', '--batch'], pipe_input=True) as blobs, \
            tarfile.open(fileobj=sink, mode='w|', format=tarfile.PAX_FORMAT) as archive:
        for record in tree_records(tree.stdout):
            description, separator, raw = record.partition(b'\t')
            fields = description.split(b' ')
            if not separator or len(fields) != 3:
                refuse('TREE_INVALID')
            mode, kind, oid = fields
            if kind != b'blob' or mode not in (b'100644', b'100755') or not OID.fullmatch(oid):
                refuse('SPECIAL_OBJECT')
            path = file_path(raw)
            if path in files or path in directories:
                refuse('TREE_INVALID')
            files.add(path)
            parts = path.split('/')
            for depth in range(1, len(parts)):
                parent = '/'.join(parts[:depth])
                if parent in files:
                    refuse('TREE_INVALID')
                directories.add(parent)
            if len(files) + len(directories) >= MAX_ENTRIES:
                refuse('ENTRY_LIMIT')
            blobs.stdin.write(oid + b'\n')
            blobs.stdin.flush()
            header = blobs.stdout.readline(160)
            match = re.fullmatch(oid + rb' blob ([0-9]{1,10})\n', header)
            if not match:
                refuse('BLOB_INVALID')
            size = int(match[1])
            total += size
            if total > MAX_ARCHIVE:
                refuse('ARCHIVE_LIMIT')
            info = tarfile.TarInfo(path)
            info.mode = 0o700 if mode == b'100755' else 0o600
            info.size = size
            archive.addfile(info, blobs.stdout)
            if blobs.stdout.read(1) != b'\n':
                refuse('BLOB_INVALID')
        blobs.stdin.close()
        if tree.wait() != 0 or blobs.wait() != 0 or not files:
            refuse('TREE_INVALID')
    return len(files), total


def prepare(home, registry, request):
    if not isinstance(request, dict) or set(request) != {'repositoryId', 'ref', 'credential'} or not identifier(request['repositoryId']):
        refuse('REQUEST_INVALID')
    repo_id, ref = request['repositoryId'], request['ref']
    path, host, port, networks, credential = repository(registry, repo_id)
    auth = authorization(credential, request['credential'])
    pin = pinned_address(host, port, networks)
    with source_relay(path, host, port, pin, auth) as settings:
        expected = requested_oid(home, RELAY_URL, ref, settings)
        repo = home / 'objects.git'
        object_format = 'sha256' if len(expected) == 64 else 'sha1'
        git_small(home, ['init', '--bare', '--quiet', f'--object-format={object_format}', str(repo)], 'GIT_INIT_FAILED')
        prefix = ['--git-dir', str(repo)]
        git_small(home, prefix + ['fetch', '--quiet', '--depth=1', '--no-tags', '--no-recurse-submodules',
                                  '--no-auto-maintenance', '--', RELAY_URL, ref], 'FETCH_FAILED', settings)
    actual = git_small(home, prefix + ['rev-parse', '--verify', 'FETCH_HEAD'], 'REF_UNAVAILABLE').strip()
    if actual != expected:
        refuse('REF_CHANGED')
    commit = git_small(home, prefix + ['rev-parse', '--verify', 'FETCH_HEAD^{commit}'], 'COMMIT_INVALID').strip()
    if not OID.fullmatch(commit) or len(commit) != len(expected):
        refuse('COMMIT_INVALID')
    if git_small(home, prefix + ['rev-list', '--max-count=2', '--count', commit.decode('ascii')], 'COMMIT_INVALID') != b'1\n':
        refuse('HISTORY_NOT_SHALLOW')
    output = home / 'source.tar'
    with output.open('xb') as stream:
        sink = LimitedArchive(stream)
        count, total = export_tree(home, repo, commit.decode('ascii'), sink)
        stream.flush()
        os.fsync(stream.fileno())
    return output, {
        'repositoryId': repo_id, 'requestedRef': ref, 'resolvedObject': expected.decode('ascii'),
        'commit': commit.decode('ascii'), 'archiveBytes': sink.length, 'archiveSha256': sink.digest.hexdigest(),
        'archiveFormat': 'tar', 'fileCount': count, 'totalBytes': total,
    }


def main():
    if os.geteuid() == 0 or sys.argv[1:]:
        return 2
    os.umask(0o077)
    signal.signal(signal.SIGALRM, lambda *_: refuse('DEADLINE'))
    signal.setitimer(signal.ITIMER_REAL, 95)
    try:
        capacity = os.statvfs(SCRATCH)
        if not 0 < capacity.f_blocks * capacity.f_frsize <= MAX_SCRATCH:
            refuse('SCRATCH_UNSAFE')
        raw = sys.stdin.buffer.read(4097)
        if not 0 < len(raw) <= 4096:
            refuse('REQUEST_INVALID')
        with tempfile.TemporaryDirectory(dir=SCRATCH, prefix='wd-source-git-') as folder:
            output, proof = prepare(Path(folder), read_registry(REGISTRY), metadata(raw))
            sys.stdout.buffer.write(json.dumps(proof, separators=(',', ':')).encode('ascii') + b'\n')
            with output.open('rb') as archive:
                while block := archive.read(65536):
                    sys.stdout.buffer.write(block)
            sys.stdout.buffer.flush()
        return 0
    except GitSourceError as error:
        print(error, file=sys.stderr)
    except (OSError, ValueError, TypeError, RuntimeError, tarfile.TarError, subprocess.SubprocessError):
        print('WORKSPACE_GIT_UNAVAILABLE', file=sys.stderr)
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
    return 2


if __name__ == '__main__':
    sys.exit(main())
