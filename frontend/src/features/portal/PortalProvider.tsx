import { createContext, useContext, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ApiError } from '../../lib/api';
import { loadPortalSession, portalApi, savePortalSession } from '../../lib/portal-api';
import type { StoredPortalSession } from '../../lib/portal-api';
import type { Customer, PortalSession } from '../../lib/commerce-types';
import { changeScreen } from '../../lib/motion';

interface PortalAuth {
  customer: Customer | null; session: StoredPortalSession | null; loading: boolean;
  busy: boolean; error: string | null; login: (accessCode: string) => Promise<void>; logout: () => Promise<void>;
  request: <T>(path: string, options?: { method?: string; body?: unknown }) => Promise<T>;
}
const Context = createContext<PortalAuth | null>(null);

export function PortalProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState(loadPortalSession);
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [loading, setLoading] = useState(!!session);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const current = useRef(session); current.current = session;
  const cache = useQueryClient();
  const navigate = useNavigate();
  function clear() { savePortalSession(null); current.current = null; setSession(null); setCustomer(null); cache.clear(); }
  useEffect(() => {
    let active = true;
    if (!session) { setLoading(false); return; }
    if (!customer) {
      setLoading(true);
      portalApi<Customer>('/me', { token: session.token }).then(value => { if (active) setCustomer(value); }).catch(caught => {
        if (!active) return;
        if (caught instanceof ApiError && caught.status === 401) clear();
        setError((caught as Error).message);
      }).finally(() => { if (active) setLoading(false); });
    }
    const timer = window.setTimeout(() => { clear(); setError('本次自助会话已过期，请重新输入访问码。'); }, Math.max(0, Date.parse(session.expiresAt) - Date.now()));
    return () => { active = false; window.clearTimeout(timer); };
  }, [session, customer, cache]);
  useEffect(() => {
    const expired = (event: Event) => {
      if ((event as CustomEvent<{ token: string }>).detail?.token !== current.current?.token) return;
      void changeScreen(() => { clear(); setError('自助会话已失效，请重新输入有效访问码。'); });
    };
    window.addEventListener('portal-session-expired', expired);
    return () => window.removeEventListener('portal-session-expired', expired);
  }, [cache]);
  async function login(accessCode: string) {
    if (busy) return;
    setBusy(true); setError(null);
    try {
      const value = await portalApi<PortalSession>('/sessions', { method: 'POST', body: { accessCode } });
      const next = { token: value.token, expiresAt: value.expiresAt };
      await changeScreen(() => { cache.clear(); savePortalSession(next); setSession(next); setCustomer(value.customer); navigate('/portal/orders', { replace: true }); });
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  async function logout() {
    if (!session || busy) return;
    setBusy(true); setError(null);
    try { await portalApi<void>('/sessions', { method: 'DELETE', token: session.token }); await changeScreen(() => { clear(); navigate('/portal', { replace: true }); }); }
    catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  async function request<T>(path: string, options: { method?: string; body?: unknown } = {}): Promise<T> {
    const token = current.current?.token;
    if (!token) throw new Error('请先登录客户自助服务。');
    return portalApi<T>(path, { ...options, token });
  }
  return <Context.Provider value={{ customer, session, loading, busy, error, login, logout, request }}>{children}</Context.Provider>;
}

export function usePortal() { const value = useContext(Context); if (!value) throw new Error('PortalProvider missing'); return value; }
