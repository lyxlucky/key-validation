import { useLocation } from 'react-router-dom';
import { App } from './App';
import { SessionQueryBoundary } from '../components/SessionQueryBoundary';
import { AuthProvider } from '../features/auth/AuthProvider';
import { PortalApp } from '../features/portal/PortalApp';

export function ApplicationRoot() {
  const { pathname } = useLocation();
  const portal = pathname === '/portal' || pathname.startsWith('/portal/');
  return portal
    ? <SessionQueryBoundary key="portal"><PortalApp /></SessionQueryBoundary>
    : <SessionQueryBoundary key="admin"><AuthProvider><App /></AuthProvider></SessionQueryBoundary>;
}
