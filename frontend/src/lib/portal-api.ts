import { ApiError } from './api';

export interface StoredPortalSession { token: string; expiresAt: string }
const storageKey = 'keygate.portal.session.v1';

export function loadPortalSession(): StoredPortalSession | null {
  try {
    const raw = sessionStorage.getItem(storageKey);
    if (!raw) return null;
    const value: unknown = JSON.parse(raw);
    if (typeof value === 'object' && value !== null && 'token' in value && 'expiresAt' in value &&
      typeof value.token === 'string' && value.token.length > 0 && typeof value.expiresAt === 'string' && Date.parse(value.expiresAt) > Date.now()) {
      return { token: value.token, expiresAt: value.expiresAt };
    }
    sessionStorage.removeItem(storageKey);
  } catch { /* Invalid or unavailable browser storage cannot restore a session. */ }
  return null;
}

export function savePortalSession(value: StoredPortalSession | null) {
  try {
    if (value) sessionStorage.setItem(storageKey, JSON.stringify(value));
    else sessionStorage.removeItem(storageKey);
  } catch { /* Keep the current in-memory session when browser storage is unavailable. */ }
}

export async function portalApi<T>(path: string, options: { method?: string; body?: unknown; token?: string } = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const headers: Record<string, string> = {};
  if (options.token) headers.Authorization = `Bearer ${options.token}`;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  let response: Response;
  try {
    response = await fetch('/api/portal' + path, {
      method, headers, credentials: 'omit', cache: 'no-store',
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  } catch {
    throw new Error(method === 'GET' ? '无法连接自助服务，请稍后重试。' : '网络连接失败，请先查看原记录的处理状态，再决定是否重试。');
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    if (response.status === 401 && options.token) window.dispatchEvent(new CustomEvent('portal-session-expired', { detail: { token: options.token } }));
    throw new ApiError(response.status, problem.code ?? 'REQUEST_FAILED', undefined, problem.traceId);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
