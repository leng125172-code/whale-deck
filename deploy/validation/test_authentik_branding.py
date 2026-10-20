#!/usr/bin/env python3
"""Appearance synchronizer contracts using only in-memory fake HTTP sessions."""
import copy
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.parse import urlsplit


SPEC = importlib.util.spec_from_file_location('authentik_branding_under_test',
    Path(__file__).resolve().parents[1] / 'install' / 'sync-authentik-branding.py')
BRANDING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BRANDING)


class Response:
    def __init__(self, data=None, content=b'', status=200):
        self.data, self.content, self.status_code = data, content, status

    def json(self):
        return copy.deepcopy(self.data)


class FakeHttp:
    def __init__(self):
        self.brand = {
            'brand_uuid': '8d8fec0a-7a5a-4b2b-aadc-876d42badb23', 'default': True,
            'domain': 'authentik-default', 'branding_title': 'Existing identity service',
            'branding_logo': 'old-logo.svg', 'branding_favicon': 'old-mark.png',
            'branding_default_flow_background': 'old-background.png', 'branding_custom_css': '',
            'flow_authentication': 'approved-authentication-flow', 'flow_invalidation': 'approved-invalidation-flow',
            'flow_user_settings': 'approved-user-settings-flow', 'attributes': {'policy': {'unchanged': True}},
            'provider': {'pk': 14}, 'groups': ['administrators', 'users'],
        }
        self.rows = None
        self.next_page = None
        self.media = {}
        self.files_override = None
        self.public_override = {}
        self.download_override = None
        self.detail_hook = None
        self.detail_reads = 0
        self.redirect_path = None
        self.calls = []
        self.sessions = []

    def session(self, anonymous=False):
        result = FakeSession(self, anonymous)
        self.sessions.append(result)
        return result

    @property
    def writes(self):
        return [call for call in self.calls if call[0] != 'GET']

    @property
    def patches(self):
        return [call for call in self.calls if call[0] == 'PATCH']

    def handle(self, session, method, url, **kwargs):
        if kwargs.get('allow_redirects') is not False or kwargs.get('timeout') != 30:
            raise AssertionError('HTTP request must be bounded and refuse redirects')
        parsed = urlsplit(url)
        if parsed.scheme != 'http' or parsed.netloc != '127.0.0.1:8081':
            raise AssertionError('Fake must never receive a foreign origin')
        path = parsed.path
        self.calls.append((method, path, copy.deepcopy(kwargs)))
        if path == self.redirect_path:
            return Response(status=302)
        if session.anonymous:
            if session.headers or session.trust_env:
                raise AssertionError('Anonymous downloads must not carry authentication or proxy settings')
            if method != 'GET' or not path.startswith('/media/'):
                raise AssertionError('Anonymous session may only download public media')
            content = self.media[path.removeprefix('/media/')]
            return Response(content=self.download_override if self.download_override is not None else content)
        if method == 'GET' and path == '/api/v3/core/brands/':
            return Response({'results': self.rows if self.rows is not None else [self.brand],
                'pagination': {'next': self.next_page}})
        if path == '/api/v3/admin/file/':
            if method == 'GET':
                if kwargs['params']['usage'] != 'media':
                    raise AssertionError('Only media usage is allowed')
                name = kwargs['params']['search']
                return Response(self.files_override if self.files_override is not None else
                    [{'name': name}] if name in self.media else [])
            if method == 'POST':
                name, content, _ = kwargs['files']['file']
                if kwargs['data'] != {'usage': 'media', 'name': name} or name in self.media:
                    raise AssertionError('Only new content-addressed media may be uploaded')
                self.media[name] = content
                return Response({'name': name}, status=201)
        if method == 'GET' and path == '/api/v3/core/brands/current/':
            return Response({**{key: '/media/' + self.brand[key]
                for key in ('branding_logo', 'branding_favicon', 'branding_default_flow_background')},
                **self.public_override})
        if path == '/api/v3/core/brands/' + self.brand['brand_uuid'] + '/':
            if method == 'GET':
                self.detail_reads += 1
                if self.detail_hook:
                    self.detail_hook(self.brand, self.detail_reads)
                return Response(self.brand)
            if method == 'PATCH':
                if not kwargs['json'].keys() <= BRANDING.MANAGED:
                    raise AssertionError('Non-presentation field reached PATCH')
                self.brand.update(copy.deepcopy(kwargs['json']))
                return Response(self.brand)
        raise AssertionError('Unexpected request: ' + method + ' ' + path)


class FakeSession:
    def __init__(self, server, anonymous):
        self.server, self.anonymous = server, anonymous
        self.headers, self.trust_env = {}, True

    def request(self, method, url, **kwargs):
        return self.server.handle(self, method, url, **kwargs)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False


class AuthentikBrandingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='whaledeck-branding-test-')
        self.addCleanup(self.temporary.cleanup)
        self.assets = Path(self.temporary.name)
        (self.assets / 'whaledeck-mark.png').write_bytes(b'fake-self-contained-png-mark')
        (self.assets / 'whaledeck-ocean-v1.png').write_bytes(b'fake-self-contained-png-background')
        (self.assets / 'wordmark.svg.template').write_text(
            '<svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"/></svg>')
        (self.assets / 'whaledeck.css').write_text(':root { --whaledeck-brand: #409eff; }')
        self.http = FakeHttp()
        self.authenticated = self.http.session()
        self.anonymous_factory = patch.object(BRANDING.requests, 'Session', side_effect=lambda: self.http.session(True))
        self.anonymous_factory.start()
        self.addCleanup(self.anonymous_factory.stop)
        runtime = patch.object(BRANDING, 'token_from_runtime', side_effect=AssertionError('Runtime credentials are forbidden in unit tests'))
        runtime.start()
        self.addCleanup(runtime.stop)

    def apply(self):
        return BRANDING.synchronize(self.authenticated, self.assets, apply=True)

    def test_default_is_read_only_and_does_not_even_read_assets(self):
        result = BRANDING.synchronize(self.authenticated, self.assets / 'nonexistent')
        self.assertEqual('inspected', result['authentikBranding'])
        self.assertFalse(result['identityChanges'])
        self.assertEqual([], self.http.writes)
        self.assertEqual([('GET', '/api/v3/core/brands/')], [(row[0], row[1]) for row in self.http.calls])

    def test_only_five_appearance_fields_are_patched_and_identity_configuration_is_unchanged(self):
        before = copy.deepcopy(self.http.brand)
        result = self.apply()
        self.assertEqual('verified', result['authentikBranding'])
        self.assertEqual(1, len(self.http.patches))
        self.assertEqual(BRANDING.MANAGED, self.http.patches[0][2]['json'].keys())
        self.assertEqual(5, len(self.http.patches[0][2]['json']))
        self.assertEqual(BRANDING.unmanaged(before), BRANDING.unmanaged(self.http.brand))
        self.assertEqual({'POST', 'PATCH'}, {row[0] for row in self.http.writes})
        self.assertTrue(all(path == '/api/v3/admin/file/' or path == '/api/v3/core/brands/' + before['brand_uuid'] + '/'
            for _, path, _ in self.http.writes))
        self.assertFalse(result['identityChanges'])

    def test_second_run_reuses_content_addressed_media_and_has_no_patch(self):
        self.apply()
        original_media = copy.deepcopy(self.http.media)
        self.http.calls.clear()
        result = self.apply()
        self.assertEqual([], self.http.writes)
        self.assertEqual([], result['changedFields'])
        self.assertEqual(original_media, self.http.media)
        for name, content in self.http.media.items():
            self.assertIn(hashlib.sha256(content).hexdigest()[:20], name)

    def test_existing_same_name_media_is_still_verified_without_upload(self):
        self.apply()
        self.http.calls.clear()
        self.http.download_override = b'wrong-public-content'
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_MEDIA_MISMATCH'):
            self.apply()
        self.assertEqual([], self.http.writes)

    def test_no_default_brand_is_rejected_without_writes(self):
        self.http.rows = [{**self.http.brand, 'default': False}]
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_DEFAULT_AMBIGUOUS'):
            self.apply()
        self.assertEqual([], self.http.writes)

    def test_multiple_defaults_are_rejected_without_writes(self):
        self.http.rows = [self.http.brand, {**self.http.brand, 'brand_uuid': '6d3ccede-ebc2-4ee9-9b72-625ee2ffca0b'}]
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_DEFAULT_AMBIGUOUS'):
            self.apply()
        self.assertEqual([], self.http.writes)

    def test_missing_each_appearance_field_is_rejected_without_writes(self):
        for missing in BRANDING.MANAGED:
            with self.subTest(missing=missing):
                self.http.rows = [{key: value for key, value in self.http.brand.items() if key != missing}]
                with self.assertRaisesRegex(RuntimeError, 'BRANDING_SCHEMA_UNSUPPORTED'):
                    self.apply()
                self.assertEqual([], self.http.writes)

    def test_paginated_selection_cannot_silently_skip_another_default(self):
        self.http.next_page = 2
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_PAGINATION_UNSUPPORTED'):
            self.apply()
        self.assertEqual([], self.http.writes)

    def test_invalid_brand_identifier_is_rejected_without_writes(self):
        self.http.brand['brand_uuid'] = '../flows'
        with self.assertRaises(ValueError):
            self.apply()
        self.assertEqual([], self.http.writes)

    def test_ambiguous_content_addressed_name_is_rejected(self):
        payload = b'fixture'
        name = 'whaledeck-mark-' + hashlib.sha256(payload).hexdigest()[:20] + '.png'
        self.http.files_override = [{'name': name}, {'name': name}]
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_FILES_AMBIGUOUS'):
            BRANDING.upload(self.authenticated, 'mark', payload, 'png', 'image/png')
        self.assertEqual([], self.http.writes)

    def test_unexpected_media_listing_shape_is_rejected(self):
        self.http.files_override = {'results': []}
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_FILES_INVALID'):
            BRANDING.upload(self.authenticated, 'mark', b'fixture', 'png', 'image/png')
        self.assertEqual([], self.http.writes)

    def test_origin_checks_reject_external_scheme_authority_credentials_and_port(self):
        for url in ('https://127.0.0.1:8081/media/icon', 'http://evil.invalid/media/icon',
            '//evil.invalid/media/icon', 'http://user@127.0.0.1:8081/media/icon', 'http://127.0.0.1:8082/media/icon'):
            with self.subTest(url=url), self.assertRaisesRegex(ValueError, 'BRANDING_ORIGIN_INVALID'):
                BRANDING.request(self.authenticated, 'GET', url)
        self.assertEqual([], self.http.calls)

    def test_authenticated_requests_reject_redirects_without_following(self):
        self.http.redirect_path = '/api/v3/core/brands/'
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_HTTP_REJECTED'):
            self.apply()
        self.assertEqual(1, len(self.http.calls))
        self.assertEqual([], self.http.writes)

    def test_anonymous_media_download_rejects_redirects_without_following(self):
        self.apply()
        self.http.calls.clear()
        self.http.redirect_path = '/media/' + self.http.brand['branding_logo']
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_HTTP_REJECTED'):
            self.apply()
        self.assertEqual([], self.http.writes)
        self.assertEqual(1, sum(path == self.http.redirect_path for _, path, _ in self.http.calls))

    def test_resolved_media_cannot_send_anonymous_client_to_an_external_origin(self):
        self.http.public_override['branding_logo'] = 'http://untrusted.invalid/logo.svg'
        with self.assertRaisesRegex(ValueError, 'BRANDING_ORIGIN_INVALID'):
            self.apply()
        self.assertFalse(any(session.anonymous for session in self.http.sessions))

    def test_anonymous_content_hash_mismatch_is_rejected(self):
        self.http.download_override = b'not-the-uploaded-media'
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_MEDIA_MISMATCH'):
            self.apply()
        anonymous = [session for session in self.http.sessions if session.anonymous]
        self.assertEqual(1, len(anonymous))
        self.assertFalse(anonymous[0].trust_env)
        self.assertEqual({}, anonymous[0].headers)

    def test_nonappearance_concurrent_mutation_refuses_patch(self):
        for key, changed in (('flow_authentication', 'someone-else-flow'), ('attributes', {'other': True}),
            ('provider', {'pk': 999}), ('groups', ['new-group'])):
            with self.subTest(key=key):
                original = copy.deepcopy(self.http.brand)
                self.http.detail_reads = 0
                self.http.calls.clear()
                self.http.detail_hook = lambda row, count, key=key, changed=changed: row.update({key: changed}) if count == 1 else None
                with self.assertRaisesRegex(RuntimeError, 'BRANDING_CONCURRENT_CHANGE'):
                    self.apply()
                self.assertEqual([], self.http.patches)
                self.http.brand = original

    def test_appearance_concurrent_mutation_refuses_patch(self):
        self.http.detail_hook = lambda row, count: row.update(branding_title='another-admin-change') if count == 1 else None
        with self.assertRaisesRegex(RuntimeError, 'BRANDING_CONCURRENT_CHANGE'):
            self.apply()
        self.assertEqual([], self.http.patches)

    def test_post_patch_identity_or_appearance_mutation_is_not_reported_as_success(self):
        for key, changed in (('flow_authentication', 'changed-after-patch'), ('branding_title', 'changed-after-patch')):
            with self.subTest(key=key):
                self.http = FakeHttp()
                self.authenticated = self.http.session()
                self.http.detail_hook = lambda row, count, key=key, changed=changed: row.update({key: changed}) if count == 2 else None
                with self.assertRaisesRegex(RuntimeError, 'BRANDING_VERIFICATION_FAILED'):
                    self.apply()

    def test_derived_media_urls_may_change_without_becoming_patch_fields(self):
        self.http.brand['branding_logo_url'] = '/temporary/old-signature'
        self.http.detail_hook = lambda row, count: row.update(branding_logo_url='/temporary/new-signature')
        result = self.apply()
        self.assertEqual('verified', result['authentikBranding'])
        self.assertNotIn('branding_logo_url', self.http.patches[0][2]['json'])

    def test_external_import_and_active_css_are_rejected_before_any_write(self):
        for css in ('@import url(https://untrusted.invalid/theme.css);', '@IMPORT "https://untrusted.invalid/a";',
            'a { background: javascript:alert(1); }', 'body { color: {{INJECT}}; }', 'x' * 65537):
            with self.subTest(css=css[:32]):
                (self.assets / 'whaledeck.css').write_text(css)
                with self.assertRaisesRegex(ValueError, 'BRANDING_CSS_INVALID'):
                    self.apply()
                self.assertEqual([], self.http.writes)
                self.assertEqual({}, self.http.media)

    def test_missing_or_duplicate_embedded_mark_placeholder_is_rejected(self):
        for template in ('<svg/>', '<svg>{{MARK_DATA_URI}}{{MARK_DATA_URI}}</svg>'):
            with self.subTest(template=template):
                (self.assets / 'wordmark.svg.template').write_text(template)
                with self.assertRaisesRegex(ValueError, 'BRANDING_LOGO_INVALID'):
                    self.apply()
                self.assertEqual([], self.http.writes)

    def test_active_external_and_non_svg_templates_are_rejected_before_any_write(self):
        templates = (
            '<html><image href="{{MARK_DATA_URI}}"/></html>',
            '<svg><image href="{{MARK_DATA_URI}}"/></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><image href="{{MARK_DATA_URI}}"/></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"><image href="{{MARK_DATA_URI}}"/></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg" style="color: red"><image href="{{MARK_DATA_URI}}"/></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"/><image href="https://untrusted.invalid/logo.png"/></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"/><text fill="url(https://untrusted.invalid/fill)">Title</text></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"/><text fill="javascript:alert(1)">Title</text></svg>',
            '<svg xmlns="http://www.w3.org/2000/svg"><foreignObject>{{MARK_DATA_URI}}</foreignObject></svg>',
            '<!DOCTYPE svg [<!ENTITY external SYSTEM "https://untrusted.invalid/entity">]><svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"/></svg>',
        )
        for template in templates:
            with self.subTest(template=template[:80]):
                (self.assets / 'wordmark.svg.template').write_text(template)
                with self.assertRaisesRegex(ValueError, 'BRANDING_LOGO_INVALID'):
                    self.apply()
                self.assertEqual([], self.http.writes)
                self.assertEqual({}, self.http.media)

    def test_malformed_svg_is_rejected_before_any_write(self):
        (self.assets / 'wordmark.svg.template').write_text(
            '<svg xmlns="http://www.w3.org/2000/svg"><image href="{{MARK_DATA_URI}}"></svg>')
        with self.assertRaises(BRANDING.ElementTree.ParseError):
            self.apply()
        self.assertEqual([], self.http.writes)
        self.assertEqual({}, self.http.media)

    def test_empty_media_asset_is_rejected_before_any_upload(self):
        for name in ('whaledeck-mark.png', 'whaledeck-ocean-v1.png'):
            with self.subTest(name=name):
                asset = self.assets / name
                previous = asset.read_bytes()
                asset.write_bytes(b'')
                try:
                    with self.assertRaisesRegex(ValueError, 'BRANDING_ASSET_SIZE_INVALID'):
                        self.apply()
                    self.assertEqual([], self.http.writes)
                    self.assertEqual({}, self.http.media)
                finally:
                    asset.write_bytes(previous)


if __name__ == '__main__':
    unittest.main()
