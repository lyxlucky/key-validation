import { lazy } from 'react';
import { CreditCardOutlined, ApiOutlined, AppstoreOutlined, AuditOutlined, BarChartOutlined, BellOutlined, ClockCircleOutlined, DashboardOutlined, FileDoneOutlined, IdcardOutlined, KeyOutlined, SafetyCertificateOutlined, ShopOutlined, ShoppingCartOutlined, TeamOutlined, ToolOutlined, UnorderedListOutlined, WarningOutlined } from '@ant-design/icons';
import type { WorkspacePage } from '../features/workspace/Workspace';

const OverviewPage = lazy(() => import('../features/dashboard/OverviewPage').then(module => ({ default: module.OverviewPage })));
const PlansPage = lazy(() => import('../features/plans/PlansPage').then(module => ({ default: module.PlansPage })));
const ApplicationsPage = lazy(() => import('../features/applications/ApplicationsPage').then(module => ({ default: module.ApplicationsPage })));
const LicensesPage = lazy(() => import('../features/licenses/LicensesPage').then(module => ({ default: module.LicensesPage })));
const LogPage = lazy(() => import('../features/audit/LogPage').then(module => ({ default: module.LogPage })));
const AccountsPage = lazy(() => import('../features/accounts/AccountsPage').then(module => ({ default: module.AccountsPage })));
const RolesPage = lazy(() => import('../features/accounts/RolesPage').then(module => ({ default: module.RolesPage })));
const BatchesPage = lazy(() => import('../features/batches/BatchesPage').then(module => ({ default: module.BatchesPage })));
const ExpiryPage = lazy(() => import('../features/expiry/ExpiryPage').then(module => ({ default: module.ExpiryPage })));
const SecurityPage = lazy(() => import('../features/security/SecurityPage').then(module => ({ default: module.SecurityPage })));
const ReportsPage = lazy(() => import('../features/operations/ReportsPage').then(module => ({ default: module.ReportsPage })));
const RiskPage = lazy(() => import('../features/operations/RiskPage').then(module => ({ default: module.RiskPage })));
const NotificationsPage = lazy(() => import('../features/operations/NotificationsPage').then(module => ({ default: module.NotificationsPage })));
const OperationsPage = lazy(() => import('../features/operations/OperationsPage').then(module => ({ default: module.OperationsPage })));
const IntegrationsPage = lazy(() => import('../features/integrations/IntegrationsPage').then(module => ({ default: module.IntegrationsPage })));
const CustomersPage = lazy(() => import('../features/commerce/CustomersPage').then(module => ({ default: module.CustomersPage })));
const OrdersPage = lazy(() => import('../features/commerce/OrdersPage').then(module => ({ default: module.OrdersPage })));
const ChannelsPage = lazy(() => import('../features/commerce/ChannelsPage').then(module => ({ default: module.ChannelsPage })));
const PortalRequestsPage = lazy(() => import('../features/commerce/PortalRequestsPage').then(module => ({ default: module.PortalRequestsPage })));
const ClientPoliciesPage = lazy(() => import('../features/client-policies/ClientPoliciesPage').then(module => ({ default: module.ClientPoliciesPage })));
const OfflineLicensesPage = lazy(() => import('../features/client-policies/OfflineLicensesPage').then(module => ({ default: module.OfflineLicensesPage })));

export const navigation: WorkspacePage[] = [
  { key: '/overview', icon: <DashboardOutlined />, label: '概览', permission: 'reports.read', element: <OverviewPage /> },
  { key: '/applications', icon: <AppstoreOutlined />, label: '应用管理', group: 'licensing', permission: 'licenses.read', element: <ApplicationsPage /> },
{ key: '/plans', icon: <CreditCardOutlined />, label: '卡种管理',
  group: 'licensing', adminOnly: true, globalScopeOnly: true, element: <PlansPage /> },
  { key: '/licenses', icon: <KeyOutlined />, label: '卡密管理', group: 'licensing', permission: 'licenses.read', element: <LicensesPage /> },
  { key: '/batches', icon: <UnorderedListOutlined />, label: '批次管理', group: 'licensing', permission: 'licenses.read', element: <BatchesPage /> },
  { key: '/expiring', icon: <ClockCircleOutlined />, label: '到期提醒', group: 'licensing', permission: 'licenses.read', element: <ExpiryPage /> },
  { key: '/client-policies', icon: <ToolOutlined />, label: '客户端版本', group: 'licensing', permission: 'platform.manage', element: <ClientPoliciesPage /> },
  { key: '/offline-licenses', icon: <SafetyCertificateOutlined />, label: '离线授权', group: 'licensing', permission: 'licenses.read', element: <OfflineLicensesPage /> },
  { key: '/customers', icon: <IdcardOutlined />, label: '客户管理', group: 'commerce', permission: 'customers.manage', element: <CustomersPage /> },
  { key: '/orders', icon: <ShoppingCartOutlined />, label: '订单管理', group: 'commerce', permission: 'orders.manage', element: <OrdersPage /> },
  { key: '/portal-requests', icon: <FileDoneOutlined />, label: '客户申请', group: 'commerce', permission: 'orders.manage', element: <PortalRequestsPage /> },
  { key: '/channels', icon: <ShopOutlined />, label: '渠道管理', group: 'commerce', adminOnly: true, element: <ChannelsPage /> },
  { key: '/reports', icon: <BarChartOutlined />, label: '运营统计', group: 'operations', permission: 'reports.read', element: <ReportsPage /> },
  { key: '/risk', icon: <WarningOutlined />, label: '异常检测', group: 'operations', permission: 'risk.review', element: <RiskPage /> },
  { key: '/notifications', icon: <BellOutlined />, label: '通知中心', group: 'operations', anyPermission: true, element: <NotificationsPage /> },
  { key: '/integrations', icon: <ApiOutlined />, label: '开放集成', group: 'operations', anyPermissions: ['integrations.manage', 'platform.manage'], element: <IntegrationsPage /> },
  { key: '/operations', icon: <ToolOutlined />, label: '运维中心', group: 'system', adminOnly: true, element: <OperationsPage /> },
  { key: '/audit', icon: <AuditOutlined />, label: '操作日志', group: 'system', permission: 'audit.read', element: <LogPage /> },
  { key: '/login-logs', icon: <SafetyCertificateOutlined />, label: '登录日志', group: 'system', permission: 'audit.read', element: <LogPage category="login" /> },
  { key: '/accounts', icon: <TeamOutlined />, label: '账号管理', group: 'system', adminOnly: true, element: <AccountsPage /> },
  { key: '/roles', icon: <SafetyCertificateOutlined />, label: '角色管理', group: 'system', adminOnly: true, element: <RolesPage /> },
  { key: '/security', icon: <SafetyCertificateOutlined />, label: '账号安全', group: 'system', element: <SecurityPage /> },
];

