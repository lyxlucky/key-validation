import { Suspense, useEffect, useRef, useState } from 'react';
import { App as AntApp, Avatar, Button, Drawer, Grid, Layout, Spin, Tag, Tooltip } from 'antd';
import { LogoutOutlined, MenuFoldOutlined, MenuOutlined, MenuUnfoldOutlined, RightOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import type { Location } from 'react-router-dom';
import { Brand } from '../components/common';
import { useAuth } from '../features/auth/AuthProvider';
import { LicenseOperationRecoveryProvider, LicenseOperationsProvider, PendingOperationNotice } from '../features/operations/LicenseOperations';
import { useWorkspace, WorkspacePanels, WorkspaceTabBar } from '../features/workspace/Workspace';
import type { WorkspacePage } from '../features/workspace/Workspace';
import { WorkspaceNavigation } from '../features/workspace/WorkspaceNavigation';
import { isCurrentAdminSession } from '../lib/api';
import { navigation } from './navigation';
import { RequireAccess } from './RequireAccess';

export function AdminLayout({ pages, home }: { pages: WorkspacePage[]; home: string }) {
  const auth = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const screens = Grid.useBreakpoint();
  const [navigationOpen, setNavigationOpen] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);
  const [collapsePreference, setCollapsePreference] = useState<boolean | null>(() => {
    try { const saved = localStorage.getItem('keygate.sidebar.collapsed'); return saved === null ? null : saved === 'true'; } catch { return null; }
  });
  const collapsed = collapsePreference ?? !(screens.lg ?? window.matchMedia('(min-width: 1024px)').matches);
  const workspace = useWorkspace(pages, home);
  const menuButton = useRef<HTMLButtonElement>(null);
  const mainContent = useRef<HTMLElement>(null);
  const focusDestinationAfterClose = useRef(false);
  const { message } = AntApp.useApp();
  function focusDestination() {
    mainContent.current?.focus({ preventScroll: true });
  }
  function closeForNavigation() {
    focusDestinationAfterClose.current = true;
    setNavigationOpen(false);
    focusDestination();
  }
  useEffect(() => {
    setNavigationOpen(false);
    if (!document.activeElement?.closest('[role="tablist"]')) focusDestination();
  }, [location.pathname]);
  useEffect(() => { if (screens.md) setNavigationOpen(false); }, [screens.md]);
  useEffect(() => {
    const requireSecurity = (event: Event) => { if (isCurrentAdminSession((event as CustomEvent<number>).detail)) navigate('/security'); };
    window.addEventListener('security-required', requireSecurity);
    return () => window.removeEventListener('security-required', requireSecurity);
  }, [navigate]);
  const currentPage = navigation.find(item => item.key === location.pathname)?.label ?? '概览';
  const role = auth.isAdmin ? '管理员' : auth.access?.channelId ? '渠道账号' : auth.user!.roles.includes('Operator') ? '操作员' : auth.user!.roles.includes('ReadOnly') ? '只读' : '工作区成员';
  const sidebarContent = (isCollapsed = false) => <>
    <Brand onNavigate={closeForNavigation} home={home} />
    <div className="workspace-card"><span className="workspace-monogram">K</span><div><strong>默认工作空间</strong><span>软件授权管理</span></div></div>
    <div className="sidebar-label">管理控制台</div>
    <WorkspaceNavigation pages={pages} active={location.pathname} collapsed={isCollapsed} visit={key => { closeForNavigation(); workspace.visit(key); }} />
    <div className="sidebar-footer"><span className="sidebar-footer-icon"><SafetyCertificateOutlined /></span><div><strong>授权管理，尽在掌握</strong><p>从卡密发行到设备验证</p></div></div>
  </>;
  const renderPage = (snapshot: Location, tabId: number) => <LicenseOperationsProvider tabId={tabId}><Routes location={snapshot}>{navigation.map(item => <Route key={item.key} path={item.key} element={<RequireAccess permission={item.permission} anyPermissions={item.anyPermissions} globalScopeOnly={item.globalScopeOnly} adminOnly={item.adminOnly} anyPermission={item.anyPermission}><Suspense fallback={<Spin description="正在加载页面…" />}>{item.element}</Suspense></RequireAccess>} />)}</Routes></LicenseOperationsProvider>;
  return <LicenseOperationRecoveryProvider activeTabId={workspace.tabs.find(tab => tab.location.pathname === workspace.active)?.id}><Layout className="app-shell screen-enter">
    <a href="#main-content" className="skip-link">跳到主内容</a>
    {screens.md && <Layout.Sider width={232} collapsed={collapsed} collapsedWidth={76} trigger={null} className="sidebar" theme="dark">{sidebarContent(collapsed)}</Layout.Sider>}
    <Drawer title="工作空间导航" placement="left" size={280} open={navigationOpen} focusable={{ focusTriggerAfterClose: false }} onClose={() => { focusDestinationAfterClose.current = false; setNavigationOpen(false); }} afterOpenChange={open => { if (!open) { if (focusDestinationAfterClose.current) focusDestination(); else menuButton.current?.focus({ preventScroll: true }); } }} className="navigation-drawer" styles={{ body: { padding: 0 } }}>{sidebarContent()}</Drawer>
    <Layout className="content-layout">
      <Layout.Header className="topbar">
        <div className="topbar-location">{screens.md ? <Tooltip title={collapsed ? '展开侧边栏' : '收起侧边栏'}><Button type="text" icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />} aria-label={collapsed ? '展开侧边栏' : '收起侧边栏'} aria-expanded={!collapsed} onClick={() => { setCollapsePreference(!collapsed); try { localStorage.setItem('keygate.sidebar.collapsed', String(!collapsed)); } catch { /* Optional preference storage. */ } }} /></Tooltip> : <Button ref={menuButton} className="mobile-menu-button" type="text" icon={<MenuOutlined />} aria-label="打开导航" aria-expanded={navigationOpen} onClick={() => { focusDestinationAfterClose.current = false; setNavigationOpen(true); }} />}<span className="workspace-name">工作空间</span><RightOutlined className="breadcrumb-divider" /><span className="current-page">{currentPage}</span></div>
        <div className="account"><Avatar size={34} className="account-avatar">{auth.user!.userName?.slice(0, 1).toUpperCase()}</Avatar><div className="account-name"><strong>{auth.user!.userName}</strong><span>{role}</span></div><Tag className="role-tag" bordered={false}>{role}</Tag><span className="account-divider" /><Tooltip title="退出登录"><Button type="text" icon={<LogoutOutlined />} aria-label="退出登录" loading={loggingOut} onClick={async () => { setLoggingOut(true); try { await auth.logout(); } catch (error) { message.error((error as Error).message); } finally { setLoggingOut(false); } }} /></Tooltip></div>
      </Layout.Header>
      <WorkspaceTabBar workspace={workspace} />
      <main ref={mainContent} id="main-content" tabIndex={-1} className="main-content">{auth.hasPermission('licenses.read') && <div className="workspace-notice"><PendingOperationNotice /></div>}<WorkspacePanels workspace={workspace} renderPage={renderPage} /></main>
    </Layout>
  </Layout></LicenseOperationRecoveryProvider>;
}

