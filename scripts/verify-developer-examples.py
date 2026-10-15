"""Exercise downloaded templates against loopback only, using temporary user directories."""
import datetime
import http.server
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import importlib.util

ROOT = Path(__file__).resolve().parents[1]
EXAMPLES = ROOT / 'backend/src/KeyValidation.Api/DeveloperIntegration/Examples'

def check(condition, message):
    if not condition:
        raise AssertionError(message)

class FakeServer(http.server.ThreadingHTTPServer):
    daemon_threads = True

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        self.server.calls.append((self.path, time.monotonic(), b'', self.headers.get('Authorization'), None))
        self.send_response(403); self.end_headers()

    def do_POST(self):
        server = self.server
        if self.headers.get('Transfer-Encoding') == 'chunked':
            chunks = []
            while True:
                size = int(self.rfile.readline().strip(), 16)
                if not size:
                    self.rfile.readline()
                    break
                chunks.append(self.rfile.read(size)); self.rfile.read(2)
            body = b''.join(chunks)
        else:
            body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        server.calls.append((self.path, time.monotonic(), body, self.headers.get('Authorization'), self.headers.get('X-Client-Version')))
        redirect = getattr(server, 'redirect', None)
        if redirect and self.path.endswith(redirect[0]):
            self.send_response(redirect[1]); self.send_header('Location', redirect[2]); self.end_headers(); return
        if self.path.endswith('/logout'):
            self.send_response(204); self.end_headers(); return
        if self.path.endswith('/heartbeat'):
            server.beats += 1
            if server.case in ('expires', 'absolute', 'long-retry'):
                self.send_response(429); self.send_header('Retry-After', '10'); self.end_headers(); return
            if server.case == 'slow':
                time.sleep(2)
            if server.case in ('network', 'lease'):
                self.connection.close(); return
            if server.case == 'retry' and server.beats == 1:
                self.send_response(429); self.send_header('Retry-After', '1'); self.end_headers(); return
            if server.case == 'denial' or server.beats > 1:
                self.send_response(403); self.end_headers(); return
        now = datetime.datetime.now(datetime.timezone.utc)
        lease = 0.65 if server.case in ('lease', 'network', 'slow', 'long-retry') else 3
        def stamp(seconds):
            return (now + datetime.timedelta(seconds=seconds)).isoformat()
        data = dict(sessionToken='secret-token', serverTime=stamp(0), expiresAt=stamp(0.4 if server.case == 'expires' else 10),
                    leaseExpiresAt=stamp(lease), absoluteExpiresAt=stamp(0.4 if server.case == 'absolute' else 8), heartbeatIntervalSeconds=0.1)
        if server.case != 'features':
            data['features'] = ['export']
        payload = json.dumps(data).encode()
        self.send_response(200); self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(payload))); self.end_headers()
        try:
            self.wfile.write(payload)
        except (ConnectionError, OSError):
            pass  # Expected when the client's deadline cancels a delayed response.

def exercise(command, case, home):
    with FakeServer(('127.0.0.1', 0), Handler) as server:
        server.case, server.calls, server.beats = case, [], 0
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        env = dict(os.environ, HOME=str(home), USERPROFILE=str(home),
                   KEYVALIDATION_API_URL=f'http://127.0.0.1:{server.server_port}',
                   KEYVALIDATION_APP_CODE='example', KEYVALIDATION_LICENSE_KEY='secret-license',
                   KEYVALIDATION_REQUIRED_FEATURE='export', KEYVALIDATION_CLIENT_VERSION='1.2.3')
        env.pop('KEYVALIDATION_DEVICE_ID', None)
        started = time.monotonic()
        result = subprocess.run(command + (['--once'] if case == 'normal' else []),
                                env=env, capture_output=True, text=True, timeout=12)
        elapsed = time.monotonic() - started
        server.shutdown(); thread.join()
        check(result.returncode == 0, f'{command[0]} {case}: {result.stderr}')
        check('secret-token' not in result.stdout + result.stderr and 'secret-license' not in result.stdout + result.stderr,
              'Sensitive values leaked')
        check(any(c[0].endswith('/logout') for c in server.calls), f'{case}: logout missing')
        activation = json.loads(server.calls[0][2])
        check(activation['appCode'] == 'example' and activation['licenseKey'] == 'secret-license', 'Wrong activation contract')
        check(activation['deviceId'], 'Missing stable device')
        check((home / '.keyvalidation/device-id').is_file(), 'Device file escaped temporary home')
        check(activation.get('clientVersion') == '1.2.3', 'Client version not sent on activation')
        check(all(c[3] == 'Bearer secret-token' for c in server.calls[1:]), 'Wrong session authentication')
        check(all(c[4] == '1.2.3' for c in server.calls if c[0].endswith('/heartbeat')), 'Heartbeat version missing')
        if case == 'normal':
            check('Protected operation' in result.stdout and server.beats == 1, 'Normal gate/heartbeat missing')
        if case == 'features':
            check('Protected operation' not in result.stdout and server.beats == 0, 'Missing features granted access')
        if case == 'denial':
            check(server.beats == 1 and result.stdout.count('Protected operation') == 1, 'Denial did not close gate')
        if case == 'retry':
            beats = [c[1] for c in server.calls if c[0].endswith('/heartbeat')]
            check(len(beats) >= 2 and beats[1] - beats[0] >= 0.95, 'Retry-After ignored')
        if case in ('network', 'lease'):
            check(elapsed < 4 and result.stdout.count('Protected operation') == 1, 'Deadline did not bound failure')
        if case in ('expires', 'absolute', 'long-retry', 'slow'):
            check(elapsed < 1.8 and result.stdout.count('Protected operation') == 1, 'Earliest deadline did not clip wait/request')
        return activation['deviceId']

def clock_checks(temp):
    # Import classes / use an external harness. Adjust captured clock origins to model
    # elapsed time without changing the machine clock or adding template test switches.
    data = dict(serverTime='2026-01-01T00:00:00Z', expiresAt='2026-01-01T00:00:02Z',
                leaseExpiresAt='2026-01-01T00:00:05Z', absoluteExpiresAt='2026-01-01T00:00:04Z',
                heartbeatIntervalSeconds=0.1, features=['export'])
    spec = importlib.util.spec_from_file_location('online_license', EXAMPLES / 'online-license.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    lease = module.Lease(data, 0.5)
    check(1.3 < lease.remaining() <= 1.5, 'Python earliest expiry / RTT')
    lease.wall_start += 86400; lease.monotonic_start -= 2
    check(not lease.allows('export'), 'Python rollback extended lease')
    lease = module.Lease(data, 0); lease.wall_start -= 10
    check(not lease.allows('export'), 'Python resume granted expired lease')
    for field in ('leaseExpiresAt', 'absoluteExpiresAt'):
        earlier = dict(data, expiresAt='2026-01-01T00:00:20Z'); earlier[field] = '2026-01-01T00:00:01Z'
        check(0.8 < module.Lease(earlier, 0).remaining() <= 1, f'Python {field} ignored')
    javascript = temp / 'clock-check.mjs'
    javascript.write_text('''import { Lease } from "''' + (EXAMPLES / 'online-license.mjs').as_uri() + '''";
const data = ''' + json.dumps(data) + ''';
const check = (value, message) => { if (!value) throw new Error(message); };
let lease = new Lease(data, 0.5);
check(lease.remaining() > 1.3 && lease.remaining() <= 1.5, 'earliest expiry / RTT');
lease.wallStart += 86400000; lease.monotonicStart -= 2000;
check(!lease.allows('export'), 'rollback');
lease = new Lease(data, 0); lease.wallStart -= 10000;
check(!lease.allows('export'), 'resume');
for (const field of ['leaseExpiresAt', 'absoluteExpiresAt']) {
  const earlier = {...data, expiresAt: '2026-01-01T00:00:20Z', [field]: '2026-01-01T00:00:01Z'};
  lease = new Lease(earlier, 0); check(lease.remaining() > 0.8 && lease.remaining() <= 1, field);
}
''', encoding='utf-8')
    subprocess.run(['node', str(javascript)], check=True, capture_output=True)
    project = temp / 'clock-csharp'; project.mkdir()
    (project / 'check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
    source = (EXAMPLES / 'online-license.cs').read_text(encoding='utf-8').replace('await OnlineLicense.Run(args);', '')
    (project / 'Sample.cs').write_text(source, encoding='utf-8')
    (project / 'Program.cs').write_text('''using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
var data = JsonSerializer.Deserialize<JsonElement>("""''' + json.dumps(data) + '''""");
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Set(Lease value, string field, object replacement) => typeof(Lease).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, replacement);
var lease = new Lease(data, 0.5);
Check(lease.Remaining() > 1.3 && lease.Remaining() <= 1.5, "earliest expiry / RTT");
Set(lease, "wallStart", DateTimeOffset.UtcNow.AddDays(1));
Set(lease, "monotonicStart", Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency);
Check(!lease.Allows("export"), "rollback");
lease = new Lease(data, 0); Set(lease, "wallStart", DateTimeOffset.UtcNow.AddSeconds(-10));
Check(!lease.Allows("export"), "resume");
foreach (var field in new[] { "leaseExpiresAt", "absoluteExpiresAt" }) {
    var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(data.GetRawText())!;
    fields["expiresAt"] = "2026-01-01T00:00:20Z"; fields[field] = "2026-01-01T00:00:01Z";
    lease = new Lease(JsonSerializer.SerializeToElement(fields), 0);
    Check(lease.Remaining() > 0.8 && lease.Remaining() <= 1, field);
}
''', encoding='utf-8')
    result = subprocess.run(['dotnet', 'run', '--project', str(project), '--verbosity', 'quiet'], capture_output=True, text=True)
    check(result.returncode == 0, 'C# clock harness: ' + result.stdout + result.stderr)
    print('PASS all languages: earliest expiry, lease, absolute deadline, RTT, rollback, suspend/resume')

def invalid_origin_check(command, home):
    with FakeServer(('127.0.0.1', 0), Handler) as server:
        server.case, server.calls, server.beats = 'normal', [], 0
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        env = dict(os.environ, HOME=str(home), USERPROFILE=str(home),
                   KEYVALIDATION_API_URL=f'http://127.0.0.1:{server.server_port}/unexpected',
                   KEYVALIDATION_APP_CODE='example', KEYVALIDATION_LICENSE_KEY='secret-license',
                   KEYVALIDATION_REQUIRED_FEATURE='export')
        result = subprocess.run(command + ['--once'], env=env, capture_output=True, text=True, timeout=5)
        server.shutdown(); thread.join()
        check(not server.calls, 'Invalid API origin was contacted')
        check('Protected operation' not in result.stdout, 'Invalid configuration granted access')

def redirect_checks(command, home):
    failures = []
    for endpoint, status in (('/activate', 307), ('/activate', 308), ('/heartbeat', 302)):
        with FakeServer(('127.0.0.1', 0), Handler) as target, FakeServer(('127.0.0.1', 0), Handler) as origin:
            for server in (origin, target):
                server.case, server.calls, server.beats = 'normal', [], 0
            origin.redirect = (endpoint, status, f'http://127.0.0.1:{target.server_port}/redirect-target')
            threads = [threading.Thread(target=server.serve_forever, daemon=True) for server in (origin, target)]
            for thread in threads:
                thread.start()
            env = dict(os.environ, HOME=str(home), USERPROFILE=str(home),
                       KEYVALIDATION_API_URL=f'http://127.0.0.1:{origin.server_port}',
                       KEYVALIDATION_APP_CODE='example', KEYVALIDATION_LICENSE_KEY='secret-license',
                       KEYVALIDATION_REQUIRED_FEATURE='export')
            env.pop('KEYVALIDATION_DEVICE_ID', None)
            result = subprocess.run(command + ['--once'], env=env, capture_output=True, text=True, timeout=12)
            for server in (origin, target):
                server.shutdown()
            for thread in threads:
                thread.join()
            if target.calls:
                failures.append(f'{command[0]} {endpoint} {status}: redirect target received a request')
            if sum(c[0].endswith(endpoint) for c in origin.calls) != 1:
                failures.append(f'{command[0]} {endpoint} {status}: redirect retried')
            expected_operations = 1 if endpoint == '/heartbeat' else 0
            if result.stdout.count('Protected operation') != expected_operations:
                failures.append(f'{command[0]} {endpoint} {status}: redirect did not close gate')
            if endpoint == '/heartbeat' and not any(c[0].endswith('/logout') for c in origin.calls):
                failures.append(f'{command[0]} {endpoint} {status}: logout missing')
            if 'secret-license' in result.stdout + result.stderr or 'secret-token' in result.stdout + result.stderr:
                failures.append(f'{command[0]} {endpoint} {status}: output leaked credentials')
    return failures

def main():
    for filename in ('online-license.py', 'online-license.mjs', 'online-license.cs'):
        check((EXAMPLES / filename).is_file(), f'Missing executable example: {filename}')
    with tempfile.TemporaryDirectory(prefix='keyvalidation-examples-') as directory:
        temp = Path(directory)
        if '--redirects-only' not in sys.argv:
            clock_checks(temp)
        project = temp / 'csharp'; project.mkdir()
        (project / 'sample.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
        shutil.copyfile(EXAMPLES / 'online-license.cs', project / 'Program.cs')
        subprocess.run(['dotnet', 'build', str(project), '--nologo', '-v', 'quiet'], check=True, capture_output=True)
        commands = [[sys.executable, str(EXAMPLES / 'online-license.py')],
                    ['node', str(EXAMPLES / 'online-license.mjs')],
                    ['dotnet', str(project / 'bin/Debug/net10.0/sample.dll')]]
        redirect_failures = []
        for command in commands:
            home = temp / command[0].split(os.sep)[-1]; home.mkdir(exist_ok=True)
            redirect_failures.extend(redirect_checks(command, home))
            if '--redirects-only' in sys.argv:
                continue
            invalid_origin_check(command, home)
            device = exercise(command, 'normal', home)
            check(device == exercise(command, 'normal', home), 'Device identity changed across launches')
            for case in ('features', 'denial', 'retry', 'network', 'lease', 'expires', 'absolute', 'long-retry', 'slow'):
                exercise(command, case, home)
            print(f'PASS {command[0]}: normal, stable device, features, denial, Retry-After, network, lease, expires, absolute, clipped wait/request')
        check(not redirect_failures, '\n'.join(redirect_failures))
        print('PASS all languages: cross-origin activation 307/308 and heartbeat 302 rejected without forwarding or retry')

if __name__ == '__main__':
    main()
