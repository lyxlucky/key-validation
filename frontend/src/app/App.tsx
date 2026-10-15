import { Spin } from 'antd';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useAuth } from '../features/auth/AuthProvider';
import { LoginPage } from '../features/auth/LoginPage';
import { AdminLayout } from './AdminLayout';
import { navigation } from './navigation';

function AuthenticatedWorkspace() {
  const auth = useAuth();
  const location = useLocation();
  if (!auth.user) return <Navigate to="/login" replace />;
  if (auth.user.mustChangePassword && location.pathname !== '/security') return <Navigate to="/security" replace />;
  const pages = navigation.filter(item => auth.user?.mustChangePassword ? item.key === '/security' : (!item.globalScopeOnly || (auth.access?.allApplications === true && !auth.access.channelId)) && (!item.adminOnly || auth.isAdmin) && (!item.permission || auth.hasPermission(item.permission)) && (!item.anyPermissions || item.anyPermissions.some(value => auth.hasPermission(value))) && (!item.anyPermission || auth.isAdmin || !!auth.access?.permissions.length));
  const home = pages[0]?.key ?? '/security';
  if (location.pathname === '/' || !navigation.some(item => item.key === location.pathname)) return <Navigate to={home} replace />;
  const sessionKey = `${auth.user.id}:${JSON.stringify(auth.access)}:${auth.user.mustChangePassword}`;
  return <AdminLayout key={sessionKey} pages={pages} home={home} />;
}

export function App() {
  const auth = useAuth();
  if (auth.loading) return <div className="full-loading screen-enter"><Spin size="large" description="正在连接管理服务…" /></div>;
  return <Routes><Route path="/login" element={<LoginPage />} /><Route path="/*" element={<AuthenticatedWorkspace />} /></Routes>;
}

