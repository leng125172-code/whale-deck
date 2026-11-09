"""Verify offline OCI content and publish a fresh, descriptor-bound Runner rootfs."""

import ctypes
import gzip
import hashlib
import json
import os
import platform
import re
import stat
import zlib

INDEX = 'application/vnd.oci.image.index.v1+json'
MANIFEST = 'application/vnd.oci.image.manifest.v1+json'
CONFIG = 'application/vnd.oci.image.config.v1+json'
LAYER = 'application/vnd.oci.image.layer.v1.tar'
GZIP_LAYER = LAYER + '+gzip'
DOCKER_INDEX = 'application/vnd.docker.distribution.manifest.list.v2+json'
DOCKER_MANIFEST = 'application/vnd.docker.distribution.manifest.v2+json'
DOCKER_CONFIG = 'application/vnd.docker.container.image.v1+json'
DOCKER_GZIP_LAYER = 'application/vnd.docker.image.rootfs.diff.tar.gzip'
DIGEST = re.compile(r'sha256:[0-9a-f]{64}\Z')
IMAGE = re.compile(r'[a-z0-9][a-z0-9._/-]{2,199}@(sha256:[0-9a-f]{64})\Z')
JSON_LIMIT = 2 * 1024**2
IMAGE_LIMIT = 8 * 1024**3
INPUT = '.oci-input'
UNPACKED = '.oci-unpacked'
SEAL = '.rootfs-seal.json'
DIR = os.O_RDONLY | os.O_DIRECTORY
READ = os.O_RDONLY | os.O_NONBLOCK
CHUNK = 1024**2


class RootfsError(Exception):
    """An invalid image, bundle, or sealed root filesystem."""


def refuse(reason):
    raise RootfsError('WORKSPACE_ROOTFS_' + reason)


def unique_fields(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            refuse('JSON_INVALID')
        obj[key] = value
    return obj


def decode(data):
    try:
        obj = json.loads(data, object_pairs_hook=unique_fields, parse_constant=lambda _: refuse('JSON_INVALID'))
    except (ValueError, RecursionError):
        refuse('JSON_INVALID')
    if type(obj) is not dict:
        refuse('JSON_INVALID')
    return obj


def private_directory(fd, exact=True):
    info = os.fstat(fd)
    mode = stat.S_IMODE(info.st_mode)
    if info.st_uid != 0 or not stat.S_ISDIR(info.st_mode) or mode & 0o7022 or (exact and mode != 0o700):
        refuse('DIRECTORY_UNSAFE')
    return info


def read_regular(files, parent, name, maximum, *, private=False):
    fd = files.open_at(parent, name, READ)
    info = os.fstat(fd)
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= maximum
            or (private and (info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600))):
        os.close(fd)
        refuse('BLOB_INVALID')
    return os.fdopen(fd, 'rb'), info.st_size


def read_metadata(files, parent, name, *, private=False):
    stream, size = read_regular(files, parent, name, JSON_LIMIT, private=private)
    with stream:
        data = stream.read(size + 1)
    if len(data) != size:
        refuse('BLOB_CHANGED')
    return data


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode('ascii')


def write_metadata(parent, name, value):
    fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(canonical(value))
        stream.flush()
        os.fsync(stream.fileno())
    os.fsync(parent)


def descriptor(value, types, maximum):
    if (type(value) is not dict or type(value.get('mediaType')) is not str or value['mediaType'] not in types
            or type(value.get('digest')) is not str or not DIGEST.fullmatch(value['digest'])
            or type(value.get('size')) is not int or not 0 < value['size'] <= maximum
            or 'urls' in value or 'data' in value):
        refuse('DESCRIPTOR_INVALID')
    return {key: value[key] for key in ('mediaType', 'digest', 'size')}


def schema(document):
    if type(document.get('schemaVersion')) is not int or document['schemaVersion'] != 2:
        refuse('SCHEMA_INVALID')


def platform_name():
    native = {'x86_64': 'amd64', 'aarch64': 'arm64'}.get(platform.machine())
    if native is None:
        refuse('PLATFORM_UNSUPPORTED')
    return native


def matching_platform(value, architecture):
    if type(value) is not dict or value.get('os') != 'linux' or value.get('architecture') != architecture:
        return False
    return value.get('variant', '') in ('', 'v8' if architecture == 'arm64' else '')


def reference_digest(reference):
    match = IMAGE.fullmatch(reference) if isinstance(reference, str) else None
    if match is None:
        refuse('IMAGE_NOT_PINNED')
    return match[1]


class VerifiedLayout:
    def __init__(self, files, source, output, blobs):
        self.files = files
        self.source = source
        self.output = output
        self.blobs = blobs

    def metadata(self, item):
        name = item['digest'][7:]
        data = read_metadata(self.files, self.source, 'blobs/sha256/' + name)
        if len(data) != item['size'] or hashlib.sha256(data).hexdigest() != name:
            refuse('DIGEST_MISMATCH')
        return data

    def save_small_blob(self, item, data):
        name = item['digest'][7:]
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=self.blobs)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())

    def layer(self, item, diff_id, remaining):
        name = item['digest'][7:]
        source, size = read_regular(self.files, self.source, 'blobs/sha256/' + name, item['size'])
        with source:
            if size != item['size']:
                refuse('DIGEST_MISMATCH')
            fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=self.blobs)
            encoded = hashlib.sha256()
            copied = 0
            with os.fdopen(fd, 'wb') as target:
                while data := source.read(CHUNK):
                    copied += len(data)
                    if copied > size:
                        refuse('BLOB_CHANGED')
                    encoded.update(data)
                    target.write(data)
                if copied != size or encoded.hexdigest() != name:
                    refuse('DIGEST_MISMATCH')
                target.flush()
                os.fsync(target.fileno())
        if item['mediaType'] == LAYER:
            if size > remaining or name != diff_id[7:]:
                refuse('DIFF_ID_INVALID')
            return size
        raw = self.files.open_at(self.blobs, name, READ)
        decoded, total = hashlib.sha256(), 0
        try:
            with os.fdopen(raw, 'rb') as compressed, gzip.GzipFile(fileobj=compressed) as stream:
                while data := stream.read(CHUNK):
                    total += len(data)
                    if total > remaining:
                        refuse('IMAGE_SIZE_LIMIT')
                    decoded.update(data)
        except (OSError, EOFError, zlib.error):
            refuse('LAYER_COMPRESSION_INVALID')
        if decoded.hexdigest() != diff_id[7:]:
            refuse('DIFF_ID_INVALID')
        return total

    def stage(self, image_digest):
        if decode(read_metadata(self.files, self.source, 'oci-layout')).get('imageLayoutVersion') != '1.0.0':
            refuse('LAYOUT_UNSUPPORTED')
        index = decode(read_metadata(self.files, self.source, 'index.json'))
        schema(index)
        roots = index.get('manifests')
        if type(roots) is not list or len(roots) > 128:
            refuse('INDEX_INVALID')
        selected = [item for item in roots if type(item) is dict and item.get('digest') == image_digest]
        if len(selected) != 1:
            refuse('IMAGE_NOT_FOUND')
        manifest = descriptor(selected[0], (INDEX, MANIFEST, DOCKER_INDEX, DOCKER_MANIFEST), JSON_LIMIT)
        architecture = platform_name()
        if manifest['mediaType'] in (INDEX, DOCKER_INDEX):
            image_index = decode(self.metadata(manifest))
            schema(image_index)
            if image_index.get('mediaType') != manifest['mediaType']:
                refuse('INDEX_INVALID')
            platforms = image_index.get('manifests')
            if type(platforms) is not list or len(platforms) > 128:
                refuse('INDEX_INVALID')
            native = [item for item in platforms if type(item) is dict and matching_platform(item.get('platform'), architecture)]
            if len(native) != 1:
                refuse('PLATFORM_UNSUPPORTED')
            manifest = descriptor(native[0], (MANIFEST, DOCKER_MANIFEST), JSON_LIMIT)
        manifest_bytes = self.metadata(manifest)
        document = decode(manifest_bytes)
        schema(document)
        if document.get('mediaType') != manifest['mediaType']:
            refuse('MANIFEST_INVALID')
        config = descriptor(document.get('config'), (CONFIG, DOCKER_CONFIG), JSON_LIMIT)
        config_bytes = self.metadata(config)
        image = decode(config_bytes)
        if not matching_platform(image, architecture):
            refuse('PLATFORM_UNSUPPORTED')
        rootfs = image.get('rootfs')
        layers = document.get('layers')
        if type(rootfs) is not dict or rootfs.get('type') != 'layers' or type(layers) is not list or not 1 <= len(layers) <= 128:
            refuse('LAYERS_INVALID')
        diff_ids = rootfs.get('diff_ids')
        if type(diff_ids) is not list or len(diff_ids) != len(layers) or any(
                type(value) is not str or not DIGEST.fullmatch(value) for value in diff_ids):
            refuse('LAYERS_INVALID')
        layers = [descriptor(layer, (LAYER, GZIP_LAYER, DOCKER_GZIP_LAYER), IMAGE_LIMIT) for layer in layers]
        if sum(layer['size'] for layer in layers) > IMAGE_LIMIT:
            refuse('IMAGE_SIZE_LIMIT')
        self.save_small_blob(config, config_bytes)
        remaining = IMAGE_LIMIT
        verified = {}
        for layer, diff_id in zip(layers, diff_ids):
            digest = layer['digest']
            if digest in verified:
                previous, previous_diff, size = verified[digest]
                if previous != layer or previous_diff != diff_id:
                    refuse('LAYERS_INVALID')
                if size > remaining:
                    refuse('IMAGE_SIZE_LIMIT')
            else:
                size = self.layer(layer, diff_id, remaining)
                verified[digest] = layer, diff_id, size
            remaining -= size
        # Both OCI and Docker schema 2 descriptors describe the same layer
        # bytes. Give the unpacker a minimal OCI manifest with no extra refs.
        staged_manifest = canonical({
            'schemaVersion': 2, 'mediaType': MANIFEST, 'config': dict(config, mediaType=CONFIG),
            'layers': [dict(item, mediaType=GZIP_LAYER) if item['mediaType'] == DOCKER_GZIP_LAYER else item
                       for item in layers],
        })
        staged_digest = 'sha256:' + hashlib.sha256(staged_manifest).hexdigest()
        selected = {'digest': staged_digest, 'mediaType': MANIFEST, 'size': len(staged_manifest)}
        self.save_small_blob(selected, staged_manifest)
        os.fsync(self.blobs)
        write_metadata(self.output, 'oci-layout', {'imageLayoutVersion': '1.0.0'})
        write_metadata(self.output, 'index.json', {'schemaVersion': 2, 'manifests': [
            dict(selected, annotations={'org.opencontainers.image.ref.name': 'runner'})]})
        return {'imageDigest': image_digest, 'manifestDigest': manifest['digest'],
                'stagedManifestDigest': staged_digest, 'architecture': architecture}


def materialize(files, source, bundle, reference, unpack):
    """Copy verified blobs, unpack in a trusted sandbox, then atomically publish."""
    digest = reference_digest(reference)
    source_stat, bundle_stat = private_directory(source), private_directory(bundle)
    if os.path.samestat(source_stat, bundle_stat) or os.listdir(bundle):
        refuse('BUNDLE_NOT_EMPTY')
    os.mkdir(INPUT, 0o700, dir_fd=bundle)
    stage = files.open_at(bundle, INPUT, DIR)
    try:
        os.mkdir('blobs', 0o700, dir_fd=stage)
        os.mkdir('blobs/sha256', 0o700, dir_fd=stage)
        blobs = files.open_at(stage, 'blobs/sha256', DIR)
        try:
            provenance = VerifiedLayout(files, source, stage, blobs).stage(digest)
        finally:
            os.close(blobs)
        unpack(bundle, INPUT, UNPACKED)
    finally:
        os.close(stage)

    output = files.open_at(bundle, UNPACKED, DIR)
    try:
        private_directory(output, exact=False)
        rootfs = files.open_at(output, 'rootfs', DIR)
        try:
            identity = private_directory(rootfs, exact=False)
            # The unpacker can have created thousands of nested files. A
            # directory fsync alone cannot seal their contents across a crash.
            libc = ctypes.CDLL(None, use_errno=True)
            if libc.syncfs(ctypes.c_int(rootfs)) != 0:
                refuse('SYNC_FAILED')
            seal = dict(provenance, schemaVersion=1, rootfsDevice=identity.st_dev, rootfsInode=identity.st_ino)
            write_metadata(bundle, SEAL, seal)
            files.exchange(output, 'rootfs', bundle, 'rootfs', 1)
            os.fsync(output)
            os.fsync(bundle)
        finally:
            os.close(rootfs)
    finally:
        os.close(output)
    return require_materialized(files, bundle, reference)


def require_materialized(files, bundle, reference):
    private_directory(bundle)
    seal = decode(read_metadata(files, bundle, SEAL, private=True))
    if (set(seal) != {'schemaVersion', 'imageDigest', 'manifestDigest', 'stagedManifestDigest', 'architecture', 'rootfsDevice', 'rootfsInode'}
            or type(seal['schemaVersion']) is not int or seal['schemaVersion'] != 1
            or seal['imageDigest'] != reference_digest(reference) or seal['architecture'] != platform_name()
            or type(seal['manifestDigest']) is not str or not DIGEST.fullmatch(seal['manifestDigest'])
            or type(seal['stagedManifestDigest']) is not str or not DIGEST.fullmatch(seal['stagedManifestDigest'])
            or type(seal['rootfsDevice']) is not int or type(seal['rootfsInode']) is not int):
        refuse('SEAL_INVALID')
    fd = files.open_at(bundle, 'rootfs', DIR)
    try:
        identity = private_directory(fd, exact=False)
        if identity.st_dev != seal['rootfsDevice'] or identity.st_ino != seal['rootfsInode']:
            refuse('ROOTFS_CHANGED')
    finally:
        os.close(fd)
    return seal
