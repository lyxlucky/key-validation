import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, ApiError, beginAdminAuthentication, beginAdminLogout, beginAdminSession, clearCsrf, getAdminSessionGeneration, isCurrentAdminSession, refreshCsrf, requireCurrentAdminSession } from '../../lib/api';
import type { AdminAuthentication } from '../../lib/api';
import type { User } from '../../lib/types';
import type { AccessProfile } from '../../lib/operations-types';
import { useNavigate } from 'react-router-dom';
import { changeScreen } from '../../lib/motion';

interface Challenge { requiresTwoFactor: true; challengeToken: string }
interface Auth { user: User | null; access: AccessProfile | null; loading: boolean; error: string | null; challenge: Challenge | null; login: (userName: string, password: string) => Promise<void>; completeLogin: (code: string, useRecoveryCode: boolean) => Promise<void>; cancelChallenge: () => void; refreshUser: () => Promise<void>; logout: () => Promise<void>; canManage: boolean; canIssue: boolean; isAdmin: boolean; hasPermission: (permission: string) => boolean }
const Context = createContext<Auth | null>(null);
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [access, setAccess] = useState<AccessProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [challenge, setChallenge] = useState<Challenge | null>(null);
  const cache = useQueryClient();
  const navigate = useNavigate();
  useEffect(() => {
    let active = true;
    const authentication = beginAdminAuthentication();
    const generation = authentication.generation;
    authentication.request<User>('/admin/auth/me').then(async value => {
      if (!active || !isCurrentAdminSession(generation)) return;
      const profile = value.mustChangePassword ? null : await authentication.request<AccessProfile>('/admin/auth/access');
      if (active && isCurrentAdminSession(generation)) { authentication.accept(); setUser(value); setAccess(profile); }
    }).catch(e => {
      if (active && isCurrentAdminSession(generation) && !(e instanceof ApiError && e.status === 401)) setError(e.message);
    }).finally(() => { if (active && isCurrentAdminSession(generation)) setLoading(false); });
    const expired = (event: Event) => {
      if (!isCurrentAdminSession((event as CustomEvent<number>).detail)) return;
      beginAdminSession('closed'); setUser(null); setAccess(null); setChallenge(null); setError(null); setLoading(false); cache.clear();
    };
    window.addEventListener('session-expired', expired);
    return () => { active = false; beginAdminSession('closed'); window.removeEventListener('session-expired', expired); };
  }, [cache]);
  const acceptUser = async (next: User, generation: number, authentication?: AdminAuthentication) => {
    requireCurrentAdminSession(generation);
    const profile = next.mustChangePassword ? null : await (authentication ? authentication.request<AccessProfile>('/admin/auth/access') : api<AccessProfile>('/admin/auth/access'));
    requireCurrentAdminSession(generation);
    const update = () => {
      // Native view transitions may defer this callback beyond a logout,
      // expiry, or a newer login even after the access request has completed.
      if (!isCurrentAdminSession(generation)) return;
      if (user?.id !== next.id || JSON.stringify(access) !== JSON.stringify(profile)) cache.clear();
      authentication?.accept();
      setAccess(profile); setUser(next); if (authentication) navigate(next.mustChangePassword ? '/security' : '/', { replace: true });
    };
    if (authentication) await changeScreen(update); else update();
    requireCurrentAdminSession(generation);
  };
  const login = async (userName: string, password: string) => {
    const authentication = beginAdminAuthentication();
    const generation = authentication.generation;
    try {
      cache.clear();
      const next = await authentication.request<User | Challenge>('/admin/auth/login', { method: 'POST', body: { userName, password } });
      requireCurrentAdminSession(generation);
      clearCsrf();
      await authentication.refreshCsrf();
      requireCurrentAdminSession(generation);
      cache.clear(); setError(null);
      if ('requiresTwoFactor' in next) { setUser(null); setAccess(null); setChallenge(next); }
      else { setChallenge(null); await acceptUser(next, generation, authentication); }
    } catch (caught) {
      // An obsolete attempt must not put its cancellation/error into the
      // replacement login form. Current failures still reach that form.
      if (isCurrentAdminSession(generation)) throw caught;
    }
  };
  const completeLogin = async (code: string, useRecoveryCode: boolean) => {
    if (!challenge) return;
    const authentication = beginAdminAuthentication();
    const generation = authentication.generation;
    try {
      cache.clear();
      const next = await authentication.request<User>('/admin/auth/two-factor', { method: 'POST', body: { challengeToken: challenge.challengeToken, code, useRecoveryCode } });
      requireCurrentAdminSession(generation);
      clearCsrf(); await authentication.refreshCsrf(); requireCurrentAdminSession(generation);
      cache.clear(); setChallenge(null); await acceptUser(next, generation, authentication); requireCurrentAdminSession(generation); setError(null);
    } catch (caught) { if (isCurrentAdminSession(generation)) throw caught; }
  };
  const refreshUser = async () => {
    const generation = getAdminSessionGeneration();
    clearCsrf(); await refreshCsrf(generation); requireCurrentAdminSession(generation);
    const next = await api<User>('/admin/auth/me');
    await acceptUser(next, generation); requireCurrentAdminSession(generation);
    await cache.invalidateQueries();
    requireCurrentAdminSession(generation);
  };
  const logout = async () => {
    const session = beginAdminLogout();
    const generation = session.generation;
    try { await cache.cancelQueries(); await session.finish(); }
    catch (caught) { if (isCurrentAdminSession(generation)) throw caught; return; }
    await changeScreen(() => {
      if (!isCurrentAdminSession(generation)) return;
      setUser(null); setAccess(null); setChallenge(null); setError(null); cache.clear(); navigate('/login', { replace: true });
    });
  };
  const hasPermission = (permission: string) => !!user && !user.mustChangePassword && !!access?.permissions.includes(permission);
  const isAdmin = hasPermission('platform.manage');
  return <Context.Provider value={{ user, access, loading, error, challenge, login, completeLogin, cancelChallenge: () => { beginAdminSession('closed'); setChallenge(null); }, refreshUser, logout, canManage: hasPermission('licenses.manage'), canIssue: hasPermission('licenses.issue') && !access?.channelId, isAdmin, hasPermission }}>{children}</Context.Provider>;
}
export function useAuth() { const context = useContext(Context); if (!context) throw new Error('AuthProvider missing'); return context; }
