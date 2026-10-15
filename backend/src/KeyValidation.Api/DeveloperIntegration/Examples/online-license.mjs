// Node.js 22+: native fetch, no packages. Run: node online-license.mjs [--once].
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { pathToFileURL } from 'node:url';

export class Lease {
  constructor(data, roundTrip) {
    const server = Date.parse(data.serverTime);
    this.seconds = Math.min(...['expiresAt', 'leaseExpiresAt', 'absoluteExpiresAt']
      .map(name => (Date.parse(data[name]) - server) / 1000)) - roundTrip;
    this.monotonicStart = performance.now();
    this.wallStart = Date.now();
    this.features = Array.isArray(data.features) ? data.features : [];
    this.interval = Math.max(0.01, Number(data.heartbeatIntervalSeconds));
  }
  remaining() {
    // The wall clock catches suspension; rollback never extends the monotonic budget.
    const elapsed = Math.max(performance.now() - this.monotonicStart, Date.now() - this.wallStart, 0) / 1000;
    return Math.max(0, this.seconds - elapsed);
  }
  allows(feature) { return this.remaining() > 0 && this.features.includes(feature); }
}

async function deviceId() {
  if (process.env.KEYVALIDATION_DEVICE_ID) return process.env.KEYVALIDATION_DEVICE_ID;
  const directory = join(homedir(), '.keyvalidation');
  const path = join(directory, 'device-id');
  await mkdir(directory, { recursive: true });
  try { await writeFile(path, randomUUID(), { flag: 'wx', mode: 0o600 }); }
  catch (error) { if (error.code !== 'EEXIST') throw error; }
  const value = (await readFile(path, 'utf8')).trim();
  if (!value) throw new Error('Device identifier file is empty');
  return value;
}

function retryAfter(value) {
  if (value && Number.isFinite(Number(value))) return Math.max(0, Number(value));
  const date = Date.parse(value);
  return Number.isFinite(date) ? Math.max(0, (date - Date.now()) / 1000) : 0.25;
}

async function main() {
  const env = process.env;
  const base = env.KEYVALIDATION_API_URL.replace(/\/$/, '');
  const url = new URL(base);
  const loopback = url.hostname === 'localhost' || url.hostname === '[::1]' || /^127\.\d+\.\d+\.\d+$/.test(url.hostname);
  if (url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback)) throw new Error('Use HTTPS or loopback HTTP');
  if (url.username || url.password || url.pathname !== '/' || url.search || url.hash) throw new Error('API URL must be an origin');
  const feature = env.KEYVALIDATION_REQUIRED_FEATURE;
  if (!feature || !env.KEYVALIDATION_APP_CODE || !env.KEYVALIDATION_LICENSE_KEY) throw new Error('Missing configuration');
  let token, lease, stopped = false;
  const stop = () => { stopped = true; };
  process.on('SIGINT', stop); process.on('SIGTERM', stop);
  const sleep = seconds => new Promise(resolve => setTimeout(resolve, Math.max(0, seconds * 1000)));
  async function request(path, payload, timeout = 10) {
    const signal = AbortSignal.timeout(Math.max(1, Math.floor(timeout * 1000)));
    const response = await fetch(base + '/api/client/' + path, {
      method: 'POST', signal, redirect: 'manual', headers: { 'Content-Type': 'application/json',
        ...(path === 'sessions/heartbeat' && env.KEYVALIDATION_CLIENT_VERSION ? { 'X-Client-Version': env.KEYVALIDATION_CLIENT_VERSION } : {}),
        ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      ...(payload ? { body: JSON.stringify(payload) } : {})
    });
    // Consume the body under the same abort deadline, rather than just timing headers.
    const body = await response.text();
    return { response, body };
  }
  try {
    const started = performance.now();
    const activation = await request('licenses/activate', { appCode: env.KEYVALIDATION_APP_CODE,
      licenseKey: env.KEYVALIDATION_LICENSE_KEY, deviceId: await deviceId(),
      ...(env.KEYVALIDATION_CLIENT_VERSION ? { clientVersion: env.KEYVALIDATION_CLIENT_VERSION } : {}) });
    if (!activation.response.ok) return;
    const data = JSON.parse(activation.body);
    token = data.sessionToken;
    lease = new Lease(data, (performance.now() - started) / 1000);
    let retry = 0.25;
    while (!stopped && lease.allows(feature)) {
      // Place business work here and repeat this check on every operation and after resume.
      if (!lease.allows(feature)) break;
      console.log('Protected operation');
      await sleep(Math.min(lease.interval, lease.remaining()));
      while (!stopped && lease.allows(feature)) {
        let delay = retry;
        const started = performance.now();
        try {
          const { response, body } = await request('sessions/heartbeat', undefined, Math.min(10, lease.remaining()));
          if (response.ok) {
            if (!lease.allows(feature)) return;
            lease = new Lease(JSON.parse(body), (performance.now() - started) / 1000);
            retry = 0.25;
            break;
          }
          if (response.status !== 429 && response.status < 500) return;
          if (response.status === 429) delay = retryAfter(response.headers.get('Retry-After'));
        } catch (error) {
          if (!(error instanceof TypeError) && !['TimeoutError', 'AbortError'].includes(error.name)) return;
        }
        await sleep(Math.min(delay, lease.remaining()));
        retry = Math.min(2, retry * 2);
      }
      if (process.argv.includes('--once')) return;
    }
  } catch {
    console.error('Authorization unavailable; protected operations stopped.');
  } finally {
    if (token) { try { await request('sessions/logout', undefined, 2); } catch {} }
    process.off('SIGINT', stop); process.off('SIGTERM', stop);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(() => console.error('Missing configuration; protected operations stopped.'));
}
