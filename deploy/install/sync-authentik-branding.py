#!/usr/bin/env python3
"""Only native Brand presentation + content-addressed media. No identity changes.

The API token is read privately in memory, never passed on argv or printed.
Default is read-only inspection; --apply explicitly updates the default brand.
No old media is overwritten/deleted. Never run the OIDC presentation provisioner
as a shortcut for this appearance-only operation.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
from pathlib import Path
import shlex
import subprocess
import uuid
from xml.etree import ElementTree
from urllib.parse import urljoin, urlsplit

import requests

ORIGIN = 'http://127.0.0.1:8081'
API = ORIGIN + '/api/v3/'
MANAGED = frozenset(('branding_title', 'branding_logo', 'branding_favicon',
    'branding_default_flow_background', 'branding_custom_css'))
DERIVED = frozenset(('branding_logo_url', 'branding_favicon_url', 'branding_default_flow_background_url',
    'branding_logo_themed_urls', 'branding_favicon_themed_urls', 'branding_default_flow_background_themed_urls'))


def token_from_runtime(env_file=None):
    if env_file is not None:
        values = {}
        for line in Path(env_file).read_text().splitlines():
            fields = shlex.split(line)
            if len(fields) == 1 and '=' in fields[0]:
                key, value = fields[0].split('=', 1); values[key] = value
        return values['WHALEDECK_AUTHENTIK_API_TOKEN']
    result = subprocess.run(['docker', 'inspect', '--format', '{{json .Config.Env}}', 'whaledeck-api'],
        check=True, capture_output=True, text=True, timeout=20)
    values = dict(line.split('=', 1) for line in json.loads(result.stdout) if '=' in line)
    return values['Authentication__Authentik__ApiToken']


def origin_url(path):
    url = urljoin(ORIGIN + '/', path)
    if urlsplit(url).scheme != 'http' or urlsplit(url).netloc != urlsplit(ORIGIN).netloc:
        raise ValueError('BRANDING_ORIGIN_INVALID')
    return url


def request(session, method, path, **kwargs):
    result = session.request(method, origin_url(path), timeout=30, allow_redirects=False, **kwargs)
    if not 200 <= result.status_code < 300: raise RuntimeError(f'BRANDING_HTTP_REJECTED_{result.status_code}')
    return result


def wordmark(assets):
    mark = (assets / 'whaledeck-mark.png').read_bytes()
    data = 'data:image/png;base64,' + base64.b64encode(mark).decode('ascii')
    template = (assets / 'wordmark.svg.template').read_text()
    if template.count('{{MARK_DATA_URI}}') != 1: raise ValueError('BRANDING_LOGO_INVALID')
    if '<!DOCTYPE' in template.upper() or '<!ENTITY' in template.upper(): raise ValueError('BRANDING_LOGO_INVALID')
    payload = template.replace('{{MARK_DATA_URI}}', data).encode()
    root = ElementTree.fromstring(payload)
    prefix = '{http://www.w3.org/2000/svg}'
    if root.tag != prefix + 'svg': raise ValueError('BRANDING_LOGO_INVALID')
    for element in root.iter():
        if element.tag not in (prefix + 'svg', prefix + 'image', prefix + 'text'):
            raise ValueError('BRANDING_LOGO_INVALID')
        for key, value in element.attrib.items():
            if key.lower().startswith('on') or key == 'style' or 'url(' in value.lower() or 'javascript:' in value.lower():
                raise ValueError('BRANDING_LOGO_INVALID')
            if key.rsplit('}', 1)[-1] == 'href' and value != data: raise ValueError('BRANDING_LOGO_INVALID')
    return payload


def upload(session, label, payload, extension, content_type):
    digest = hashlib.sha256(payload).hexdigest()
    name = f'whaledeck-{label}-{digest[:20]}.{extension}'
    files = request(session, 'GET', API + 'admin/file/', params={'usage': 'media', 'search': name}).json()
    if not isinstance(files, list): raise RuntimeError('BRANDING_FILES_INVALID')
    matches = [row for row in files if row.get('name') == name]
    if len(matches) > 1: raise RuntimeError('BRANDING_FILES_AMBIGUOUS')
    if not matches:
        request(session, 'POST', API + 'admin/file/', data={'usage': 'media', 'name': name},
            files={'file': (name, payload, content_type)})
    return name, digest


def select_default(session):
    result = request(session, 'GET', API + 'core/brands/', params={'page_size': 100}).json()
    if result.get('pagination', {}).get('next'): raise RuntimeError('BRANDING_PAGINATION_UNSUPPORTED')
    rows = [row for row in result['results'] if row.get('default') is True]
    if len(rows) != 1: raise RuntimeError('BRANDING_DEFAULT_AMBIGUOUS')
    row = rows[0]
    uuid.UUID(row['brand_uuid'])
    if not MANAGED <= row.keys(): raise RuntimeError('BRANDING_SCHEMA_UNSUPPORTED')
    return row


def unmanaged(row):
    return {key: value for key, value in row.items() if key not in MANAGED | DERIVED}


def background_asset(path):
    with path.open('rb') as stream:
        payload = stream.read(25 * 1024**2 + 1)
    if len(payload) > 25 * 1024**2: raise ValueError('BRANDING_ASSET_SIZE_INVALID')
    extension = path.suffix.lower().lstrip('.')
    if extension == 'jpeg': extension = 'jpg'
    signatures = {'jpg': payload.startswith(b'\xff\xd8\xff'), 'png': payload.startswith(b'\x89PNG\r\n\x1a\n'),
        'webp': len(payload) >= 12 and payload[:4] == b'RIFF' and payload[8:12] == b'WEBP'}
    if extension not in signatures or not signatures[extension]: raise ValueError('BRANDING_BACKGROUND_INVALID')
    if not 0 < len(payload) <= 25 * 1024**2: raise ValueError('BRANDING_ASSET_SIZE_INVALID')
    return payload, extension, {'jpg': 'image/jpeg', 'png': 'image/png', 'webp': 'image/webp'}[extension]


def synchronize(session, assets, apply=False, background_file=None):
    before = select_default(session)
    if not apply:
        return {'authentikBranding': 'inspected', 'defaultBrands': 1, 'managedFields': sorted(MANAGED),
            'existingCssBytes': len(before.get('branding_custom_css', '').encode()), 'identityChanges': False}
    logo_payload = wordmark(assets)
    icon_payload = (assets / 'whaledeck-mark.png').read_bytes()
    # A user-selected wallpaper is native persistent media. Later installer
    # synchronizations must not silently replace it with the bundled default.
    preserve_background = background_file is None and isinstance(before.get('branding_default_flow_background'), str) and bool(
        re.fullmatch(r'whaledeck-wallpaper-[a-f0-9]{20}\.(?:jpg|png|webp)', before['branding_default_flow_background']))
    if background_file is not None:
        background_payload, background_extension, background_type = background_asset(background_file)
    elif preserve_background:
        background_payload, background_extension, background_type = None, None, None
    else:
        background_payload, background_extension, background_type = (assets / 'whaledeck-ocean-v1.png').read_bytes(), 'png', 'image/png'
    # DRF's native CharField trims outer whitespace. Canonicalize before both
    # comparison and hashing so a final newline never causes perpetual writes.
    css = (assets / 'whaledeck.css').read_text().strip()
    if len(css.encode()) > 65536 or any(value in css.lower() for value in ('@import', 'javascript:', '{{')):
        raise ValueError('BRANDING_CSS_INVALID')
    if any(not 0 < len(payload) <= 25 * 1024**2 for payload in (logo_payload, icon_payload, background_payload) if payload is not None):
        raise ValueError('BRANDING_ASSET_SIZE_INVALID')
    logo, logo_hash = upload(session, 'wordmark', logo_payload, 'svg', 'image/svg+xml')
    icon, icon_hash = upload(session, 'mark', icon_payload, 'png', 'image/png')
    if preserve_background:
        background, background_hash = before['branding_default_flow_background'], None
    else:
        background, background_hash = upload(session, 'wallpaper' if background_file is not None else 'ocean', background_payload, background_extension, background_type)
    desired = {'branding_title': 'Whale Deck · 身份中心', 'branding_logo': logo, 'branding_favicon': icon,
        'branding_default_flow_background': background, 'branding_custom_css': css}
    changes = {key: value for key, value in desired.items() if before.get(key) != value}
    path = API + 'core/brands/' + before['brand_uuid'] + '/'
    current = request(session, 'GET', path).json()
    if unmanaged(current) != unmanaged(before) or any(current.get(key) != before.get(key) for key in MANAGED):
        raise RuntimeError('BRANDING_CONCURRENT_CHANGE')
    if changes:
        if not changes.keys() <= MANAGED: raise ValueError('BRANDING_PATCH_INVALID')
        request(session, 'PATCH', path, json=changes)
    after = request(session, 'GET', path).json()
    if unmanaged(after) != unmanaged(before):
        raise RuntimeError('BRANDING_VERIFICATION_UNMANAGED')
    for key, value in desired.items():
        if after.get(key) != value: raise RuntimeError('BRANDING_VERIFICATION_' + key.upper())
    # Resolve relative media values through Authentik, never freeze its signed
    # temporary media URL in CSS or depend on Whale Deck to serve assets.
    public = request(session, 'GET', API + 'core/brands/current/').json()
    for field, digest in (('branding_logo', logo_hash), ('branding_favicon', icon_hash)):
        url = origin_url(public[field])
        with requests.Session() as anonymous:
            anonymous.trust_env = False
            response = request(anonymous, 'GET', url)
            if hashlib.sha256(response.content).hexdigest() != digest:
                raise RuntimeError('BRANDING_MEDIA_MISMATCH')
    return {'authentikBranding': 'verified', 'changedFields': sorted(changes), 'identityChanges': False,
        'cssSha256': hashlib.sha256(css.encode()).hexdigest(), 'backgroundSha256': background_hash,
        'backgroundPreserved': preserve_background,
        'logoSha256': logo_hash, 'faviconSha256': icon_hash}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--env-file', type=Path)
    parser.add_argument('--assets-directory', type=Path, default=Path(__file__).resolve().parents[1] / 'branding/authentik')
    parser.add_argument('--background-file', type=Path, help='Upload an original JPG/PNG/WebP as the persistent selected background; never passed to Git.')
    args = parser.parse_args()
    with requests.Session() as session:
        session.trust_env = False
        session.headers['Authorization'] = 'Bearer ' + token_from_runtime(args.env_file)
        print(json.dumps(synchronize(session, args.assets_directory, args.apply, args.background_file), ensure_ascii=False))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        code = str(error)
        if not code.startswith('BRANDING_') or len(code) > 96 or not all(char.isupper() or char.isdigit() or char == '_' for char in code):
            code = type(error).__name__
        raise SystemExit('Authentik branding synchronization failed: ' + code + '; no credentials or response body were logged.') from None
