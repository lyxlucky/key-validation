import type { ReactNode } from 'react';
import { Button, Result } from 'antd';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../features/auth/AuthProvider';

export function RequireAccess({ permission, anyPermissions, children, adminOnly = false, anyPermission = false, globalScopeOnly = false }: { permission?: string; anyPermissions?: string[]; children: ReactNode; adminOnly?: boolean; anyPermission?: boolean; globalScopeOnly?: boolean }) {
  const auth = useAuth();
  const navigate = useNavigate();
  if ((globalScopeOnly && !(auth.access?.allApplications === true && !auth.access.channelId)) || (adminOnly && !auth.isAdmin) || (permission && !auth.hasPermission(permission)) || (anyPermissions && !anyPermissions.some(value => auth.hasPermission(value))) || (anyPermission && !auth.isAdmin && !auth.access?.permissions.length)) return <Result status="403" title="没有访问权限" subTitle="当前账号未获得此页面的权限，请联系管理员。" extra={<Button onClick={() => navigate('/security')}>账号安全</Button>} />;
  return children;
}

