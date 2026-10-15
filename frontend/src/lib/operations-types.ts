import type { Dto } from './types';

export type AccessProfile = Dto<'AccountAccessDto'>;
export type OperationsCounts = Dto<'OperationsCounts'>;
export type OperationsDay = Dto<'OperationsDay'>;
export type OperationsBreakdown = Dto<'OperationsBreakdown'>;
export type OperationsReport = Dto<'OperationsReport'>;
export type OperationsCatalog = Dto<'ReportsCatalogDto'>;

export const permissionNames: Record<string, string> = {
  'licenses.read': '读取授权', 'licenses.issue': '签发卡密', 'licenses.manage': '管理授权',
  'reports.read': '查看报表', 'audit.read': '查看审计', 'risk.review': '复核异常',
  'customers.manage': '管理客户', 'orders.manage': '管理订单', 'integrations.manage': '管理集成',
  'platform.manage': '平台管理',
};
