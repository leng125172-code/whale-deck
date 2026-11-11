"""Bounded read-only HTTP evidence for a separately authorized standard update.

No publication, credentials, proxy settings, retries or response-body logging.
Availability within the explicit response budget and the original three-second
threshold are reported separately. Neither can erase a previous failed run.
"""
import collections
import http.client
import socket
import threading
import time

ENDPOINTS = ((8080, '/health/live'), (8080, '/build-info.json'),
             (8080, '/api/v1/system/status'), (8081, '/'))
INTERVAL_SECONDS = 1.5  # At most 40 status requests/minute, below API's 120.
CONNECT_SECONDS = 2
RESPONSE_SECONDS = 8  # Covers the existing 5s forwarder and 2s status probe.
MAX_BODY = 65536


def known_auth_redirect(port, path, status, location):
    target = '/flows/-/default/authentication/'
    return port == 8081 and path == '/' and status == 302 and location in (
        target, target + '?next=/', target + '?next=%2F', target + '?next=%2f')


def sample(port, path, *, connect_seconds=CONNECT_SECONDS, response_seconds=RESPONSE_SECONDS):
    started = time.monotonic()
    row = dict(port=port, path=path, monotonic=started, phase='connect')
    connection = http.client.HTTPConnection('127.0.0.1', port, timeout=connect_seconds)
    expired = threading.Event()
    timer = None
    try:
        connection.connect()
        row['connectSeconds'] = time.monotonic() - started
        row['phase'] = 'response'
        transport = connection.sock
        transport.settimeout(response_seconds)

        def expire():
            expired.set()
            try:
                transport.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass

        # A wall deadline also bounds a slow/trickled response body.
        timer = threading.Timer(response_seconds, expire)
        timer.daemon = True
        timer.start()
        connection.request('GET', path, headers={'Connection': 'close'})
        response = connection.getresponse()
        body = response.read(MAX_BODY + 1)
        if expired.is_set():
            raise TimeoutError()
        row.update(status=response.status, bodyBytes=len(body))
        content_type = response.getheader('Content-Type', '').split(';')[0].lower()
        row['contentType'] = content_type
        # Observe the actual root response without following or logging a
        # redirect (query values/cookies must never enter evidence).
        location = response.getheader('Location', '')
        row['expectedAuthRedirect'] = known_auth_redirect(port, path, response.status, location)
        retry = response.getheader('Retry-After', '')
        if retry.isascii() and retry.isdecimal() and len(retry) <= 3:
            row['retryAfterSeconds'] = min(int(retry), 999)
        row['maintenanceHtml'] = (response.status == 503 and content_type == 'text/html'
            and b'Whale Deck' in body and '维护'.encode() in body)
        if len(body) > MAX_BODY:
            row['error'] = 'ResponseTooLarge'
    except (OSError, http.client.HTTPException) as error:
        row['error'] = 'ResponseDeadline' if expired.is_set() else type(error).__name__
        if isinstance(error, OSError) and error.errno is not None:
            row['errno'] = error.errno
    finally:
        if timer is not None:
            timer.cancel()
            timer.join()
        if expired.is_set():
            row['error'] = 'ResponseDeadline'
        connection.close()
        row['elapsedSeconds'] = time.monotonic() - started
        row['overThreeSeconds'] = row['elapsedSeconds'] > 3
    return row


def summary(rows):
    def accepted(row):
        return 'error' not in row and (row.get('status') == 200 or row.get('maintenanceHtml') is True
            or row.get('expectedAuthRedirect') is True)
    available = bool(rows) and all(accepted(row) for row in rows)
    slow = sum(row['overThreeSeconds'] for row in rows)
    return dict(samples=len(rows), statuses=dict(collections.Counter(str(row.get('status')) for row in rows)),
        errors=sum('error' in row for row in rows), overThreeSeconds=slow,
        boundedAvailabilityPassed=available, threeSecondPassed=available and slow == 0)


class StandardUpdateHttpObserver:
    def __init__(self):
        self.rows = []
        self.stop = threading.Event()
        self.thread = None
        self.failure = None

    def __enter__(self):
        if self.thread is not None:
            raise RuntimeError('HTTP_OBSERVER_ALREADY_USED')
        def observe():
            try:
                while not self.stop.is_set() and len(self.rows) < 40000:
                    for port, path in ENDPOINTS:
                        self.rows.append(sample(port, path))
                    self.stop.wait(INTERVAL_SECONDS)
            except BaseException as error:
                self.failure = type(error).__name__
        self.thread = threading.Thread(target=observe, name='standard-update-http-observer')
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.stop.set()
        self.thread.join(4 * (CONNECT_SECONDS + RESPONSE_SECONDS) + 5)
        if self.thread.is_alive():
            raise RuntimeError('HTTP_OBSERVER_DID_NOT_STOP')
        if self.failure is not None:
            raise RuntimeError('HTTP_OBSERVER_FAILED_' + self.failure)
