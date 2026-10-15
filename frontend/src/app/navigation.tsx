import { CreditCardOutlined, ApiOutlined, AppstoreOutlined, AuditOutlined, BarChartOutlined, BellOutlined, ClockCircleOutlined, DashboardOutlined, FileDoneOutlined, IdcardOutlined, KeyOutlined, SafetyCertificateOutlined, ShopOutlined, ShoppingCartOutlined, TeamOutlined, ToolOutlined, UnorderedListOutlined, WarningOutlined } from '@ant-design/icons';
import { OverviewPage } from '../features/dashboard/OverviewPage';
import { PlansPage } from '../features/plans/PlansPage';
import { ApplicationsPage } from '../features/applications/ApplicationsPage';
import { LicensesPage } from '../features/licenses/LicensesPage';
import { LogPage } from '../features/audit/LogPage';
import { AccountsPage } from '../features/accounts/AccountsPage';
import { RolesPage } from '../features/accounts/RolesPage';
import { BatchesPage } from '../features/batches/BatchesPage';
import { ExpiryPage } from '../features/expiry/ExpiryPage';
import { SecurityPage } from '../features/security/SecurityPage';
import { ReportsPage } from '../features/operations/ReportsPage';
import { RiskPage } from '../features/operations/RiskPage';
import { NotificationsPage } from '../features/operations/NotificationsPage';
import { OperationsPage } from '../features/operations/OperationsPage';
import { IntegrationsPage } from '../features/integrations/IntegrationsPage';
import { CustomersPage } from '../features/commerce/CustomersPage';
import { OrdersPage } from '../features/commerce/OrdersPage';
import { ChannelsPage } from '../features/commerce/ChannelsPage';
import { PortalRequestsPage } from '../features/commerce/PortalRequestsPage';
import { ClientPoliciesPage } from '../features/client-policies/ClientPoliciesPage';
import { OfflineLicensesPage } from '../features/client-policies/OfflineLicensesPage';
import type { WorkspacePage } from '../features/workspace/Workspace';

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

