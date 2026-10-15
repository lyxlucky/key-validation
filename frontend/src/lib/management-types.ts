import type { components } from './schema';
import type { Dto, License, Page } from './types';

export type AccountRole = 'Admin' | 'Operator' | 'ReadOnly';
export type Account = Dto<'AccountDto'>;
export type BatchOverview = Dto<'BatchOverviewDto'>;
export type ExpiringLicenses = Omit<Dto<'ExpiringLicensesDto'>, keyof Page<License>> & Page<License>;
export type LicenseAction = 'disable' | 'enable' | 'extend' | 'metadata';
export type OperationRequest = Omit<components['schemas']['LicenseOperationRequest'], 'action' | 'additionalSeconds' | 'note' | 'tags'> & {
  action: LicenseAction;
  additionalSeconds?: number;
  note?: string;
  tags?: string[];
};
export type OperationResult = Omit<Dto<'OperationResult'>, 'action' | 'succeeded' | 'failed' | 'items'> & {
  action: LicenseAction;
  succeeded: number;
  failed: number;
  items: (Omit<Dto<'LicenseOperationItem'>, 'license'> & { license: License | null })[];
};
export const roleNames: Record<string, string> = { Admin: '管理员', Operator: '操作员', ReadOnly: '只读' };
export const roleOptions = Object.entries(roleNames).map(([value, label]) => ({ value, label }));
