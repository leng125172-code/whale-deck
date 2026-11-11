"""Actual loopback HTTP and disconnects; no production ports or publication."""
import contextlib
import http.server
import socket
import threading
import time
import unittest
from unittest.mock import patch

from standard_update_http_observer import sample, summary, INTERVAL_SECONDS, StandardUpdateHttpObserver, known_auth_redirect


@contextlib.contextmanager
def server(status=200, body=b'ok', *, delay=0, trickle=False, content_type='text/plain', location=None):
    requests = []
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_GET(self):
            requests.append(self.path)
            time.sleep(delay)
            try:
                self.send_response(status)
                self.send_header('Content-Type', content_type)
                self.send_header('Retry-After', '17')
                if location is not None:
                    self.send_header('Location', location)
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                if trickle:
                    for byte in body:
                        self.wfile.write(bytes([byte])); self.wfile.flush(); time.sleep(0.04)
                else:
                    self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError):
                pass
    with http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler) as host:
        thread = threading.Thread(target=lambda: host.serve_forever(poll_interval=0.01))
        thread.start()
        try:
            yield host.server_port, requests
        finally:
            host.shutdown(); thread.join(2)


class ObserverTests(unittest.TestCase):
    def test_only_fixed_auth_origin_path_and_root_return_target_are_accepted(self):
        target = '/flows/-/default/authentication/'
        self.assertTrue(known_auth_redirect(8081, '/', 302, target + '?next=/'))
        self.assertTrue(known_auth_redirect(8081, '/', 302, target + '?next=%2F'))
        for location in ('https://example.test'+target, '//example.test'+target,
                         target+'?next=https://example.test', target+'?next=/&token=private', target+'#fragment'):
            self.assertFalse(known_auth_redirect(8081, '/', 302, location))
        self.assertFalse(known_auth_redirect(8080, '/', 302, target))
        self.assertFalse(known_auth_redirect(8081, '/other', 302, target))
        self.assertFalse(known_auth_redirect(8081, '/', 307, target))

    def test_unexpected_redirects_are_not_success_or_followed(self):
        for target in ('https://example.test/secret', '/elsewhere', '/flows/-/default/authentication/'):
            with self.subTest(target=target), server(302, location=target) as (port, requests):
                row = sample(port, '/')
            self.assertFalse(summary([row])['boundedAvailabilityPassed'])
            self.assertEqual(requests, ['/'])
            self.assertNotIn('Location', row)

    def test_observer_thread_failure_cannot_return_partial_green_evidence(self):
        with patch('standard_update_http_observer.sample', side_effect=ValueError('not logged')):
            with self.assertRaisesRegex(RuntimeError, 'HTTP_OBSERVER_FAILED_ValueError'):
                with StandardUpdateHttpObserver() as observer:
                    observer.thread.join(2)

    def test_actual_success_and_no_body_logging(self):
        with server(body=b'private response never logged') as (port, requests):
            row = sample(port, '/')
        self.assertEqual(requests, ['/'])
        self.assertTrue(summary([row])['threeSecondPassed'])
        self.assertNotIn('private', str(row))
        self.assertIn('connectSeconds', row)

    def test_only_recognized_maintenance_html_is_accepted(self):
        for body, expected in [(b'<h1>Whale Deck '+ '维护'.encode()+b'</h1>', True), (b'upstream error', False)]:
            with self.subTest(body=body), server(503, body, content_type='text/html') as (port, _):
                self.assertEqual(summary([sample(port, '/')])['boundedAvailabilityPassed'], expected)

    def test_rate_limit_is_retained_and_never_retried(self):
        with server(429) as (port, requests):
            row = sample(port, '/api/v1/system/status')
        self.assertEqual(requests, ['/api/v1/system/status'])
        self.assertEqual(row['retryAfterSeconds'], 17)
        self.assertFalse(summary([row])['boundedAvailabilityPassed'])
        self.assertLessEqual(60 / INTERVAL_SECONDS, 40)

    def test_header_deadline_does_not_become_a_success(self):
        with server(delay=0.25) as (port, requests):
            row = sample(port, '/', response_seconds=0.05)
        self.assertEqual(len(requests), 1)
        self.assertIn('error', row)
        self.assertEqual(row['phase'], 'response')
        self.assertLess(row['elapsedSeconds'], 0.2)

    def test_trickled_body_is_bounded_by_wall_deadline(self):
        with server(body=b'0123456789', trickle=True) as (port, _):
            row = sample(port, '/', response_seconds=0.1)
        self.assertIn('error', row)
        self.assertLess(row['elapsedSeconds'], 0.3)

    def test_connection_refusal_is_not_response_timeout(self):
        with socket.socket() as bound:
            bound.bind(('127.0.0.1', 0))
            row = sample(bound.getsockname()[1], '/')
        self.assertEqual(row['phase'], 'connect')
        self.assertIn('error', row)

    def test_oversize_response_and_empty_evidence_fail(self):
        with server(body=b'x' * 65537) as (port, _):
            row = sample(port, '/')
        self.assertEqual(row['error'], 'ResponseTooLarge')
        self.assertFalse(summary([row])['boundedAvailabilityPassed'])
        self.assertFalse(summary([])['boundedAvailabilityPassed'])

    def test_three_second_failure_remains_distinct(self):
        with server(delay=3.05) as (port, _):
            row = sample(port, '/', response_seconds=4)
        self.assertTrue(summary([row])['boundedAvailabilityPassed'])
        self.assertFalse(summary([row])['threeSecondPassed'])


if __name__ == '__main__':
    unittest.main()
