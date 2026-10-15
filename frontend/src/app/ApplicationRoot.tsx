import { lazy, Suspense } from 'react';
import { Spin } from 'antd';
import { useLocation } from 'react-router-dom';
import { SessionQueryBoundary } from '../components/SessionQueryBoundary';
import { AuthProvider } from '../features/auth/AuthProvider';

const App = lazy(() => import('./App').then(module => ({ default: module.App })));
const PortalApp = lazy(() => import('../features/portal/PortalApp').then(module => ({ default: module.PortalApp })));

export function ApplicationRoot() {
  const { pathname } = useLocation();
  const portal = pathname === '/portal' || pathname.startsWith('/portal/');
  return <Suspense fallback={<div className="full-loading"><Spin description="正在加载应用…" /></div>}>{portal
    ? <SessionQueryBoundary key="portal"><PortalApp /></SessionQueryBoundary>
    : <SessionQueryBoundary key="admin"><AuthProvider><App /></AuthProvider></SessionQueryBoundary>}</Suspense>;
}
