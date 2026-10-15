"""Python 3.11+: standard library only. Run: python online-license.py [--once]."""
# urllib bounds socket inactivity, not total streaming duration: a trickling response
# can delay exit/logout. No protected work runs while blocked; expired responses are rejected.
import datetime
import email.utils
import json
import os
from pathlib import Path
import sys
import time
import urllib.error
import urllib.request
import uuid
import ipaddress
from urllib.parse import urlsplit


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Fixed API endpoints never forward credentials to a redirect target.


class Lease:
    def __init__(self, data, round_trip):
        def timestamp(value):
            return datetime.datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()
        server = timestamp(data['serverTime'])
        self.seconds = min(timestamp(data[name]) - server for name in
                           ('expiresAt', 'leaseExpiresAt', 'absoluteExpiresAt')) - round_trip
        self.monotonic_start = time.monotonic()
        self.wall_start = time.time()
        self.features = data.get('features') or []
        self.interval = max(0.01, float(data['heartbeatIntervalSeconds']))

    def remaining(self):
        # Wall time catches platforms where monotonic clocks pause during suspension.
        # A backward wall adjustment can never increase the monotonic budget.
        elapsed = max(time.monotonic() - self.monotonic_start, time.time() - self.wall_start, 0)
        return max(0, self.seconds - elapsed)

    def allows(self, feature):
        return self.remaining() > 0 and feature in self.features


def device_id():
    configured = os.getenv('KEYVALIDATION_DEVICE_ID')
    if configured:
        return configured
    path = Path.home() / '.keyvalidation' / 'device-id'
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with path.open('x', encoding='utf-8') as stream:
            stream.write(str(uuid.uuid4()))
    except FileExistsError:
        pass
    value = path.read_text(encoding='utf-8').strip()
    if not value:
        raise ValueError('Device identifier file is empty')
    return value


def retry_after(value):
    try:
        return max(0, float(value))
    except (TypeError, ValueError):
        try:
            return max(0, email.utils.parsedate_to_datetime(value).timestamp() - time.time())
        except (TypeError, ValueError, OverflowError):
            return 0.25


def main():
    base = os.environ['KEYVALIDATION_API_URL'].rstrip('/')
    url = urlsplit(base)
    try:
        loopback = url.hostname == 'localhost' or ipaddress.ip_address(url.hostname).is_loopback
    except ValueError:
        loopback = False
    if url.scheme != 'https' and not (url.scheme == 'http' and loopback):
        raise ValueError('Use HTTPS, or HTTP on loopback for local development')
    if url.username or url.password or url.path not in ('', '/') or url.query or url.fragment:
        raise ValueError('API URL must be an origin without credentials')
    feature = os.environ['KEYVALIDATION_REQUIRED_FEATURE']
    token, lease = None, None
    opener = urllib.request.build_opener(NoRedirect())

    def request(path, payload=None, timeout=10):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        if path == 'sessions/heartbeat' and os.getenv('KEYVALIDATION_CLIENT_VERSION'):
            headers['X-Client-Version'] = os.environ['KEYVALIDATION_CLIENT_VERSION']
        body = json.dumps(payload).encode() if payload is not None else None
        req = urllib.request.Request(base + '/api/client/' + path, data=body, headers=headers, method='POST')
        return opener.open(req, timeout=max(0.001, timeout))

    try:
        started = time.monotonic()
        activation = dict(appCode=os.environ['KEYVALIDATION_APP_CODE'],
                          licenseKey=os.environ['KEYVALIDATION_LICENSE_KEY'], deviceId=device_id())
        if os.getenv('KEYVALIDATION_CLIENT_VERSION'):
            activation['clientVersion'] = os.environ['KEYVALIDATION_CLIENT_VERSION']
        with request('licenses/activate', activation) as response:
            data = json.load(response)
        token = data['sessionToken']
        lease = Lease(data, time.monotonic() - started)
        retry = 0.25
        while lease.allows(feature):
            # Put business work behind this check; check again for every operation and after resume.
            if not lease.allows(feature):
                break
            print('Protected operation', flush=True)
            time.sleep(min(lease.interval, lease.remaining()))
            while lease.allows(feature):
                started = time.monotonic()
                try:
                    with request('sessions/heartbeat', timeout=min(10, lease.remaining())) as response:
                        data = json.load(response)
                    # Never accept a response received after the previous lease expired.
                    if not lease.allows(feature):
                        return
                    lease = Lease(data, time.monotonic() - started)
                    retry = 0.25
                    break
                except urllib.error.HTTPError as error:
                    if error.code != 429 and error.code < 500:
                        return  # Explicit rejection immediately closes the gate.
                    delay = retry_after(error.headers.get('Retry-After')) if error.code == 429 else retry
                except (urllib.error.URLError, TimeoutError, OSError):
                    delay = retry
                time.sleep(min(delay, lease.remaining()))
                retry = min(2, retry * 2)
            if '--once' in sys.argv:
                return
    except (urllib.error.URLError, TimeoutError, OSError, ValueError, KeyError):
        print('Authorization unavailable; protected operations stopped.', file=sys.stderr)
    except KeyboardInterrupt:
        pass
    finally:
        if token:
            try:
                with request('sessions/logout', timeout=2):
                    pass
            except Exception:
                pass  # Best effort cleanup; token was never persisted.


if __name__ == '__main__':
    main()
