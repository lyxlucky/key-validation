import type { components } from './schema';

export type Dto<K extends keyof components['schemas']> = Required<components['schemas'][K]>;
export type Application = Dto<'ApplicationDto'>;
export type Plan = Dto<'PlanDto'>;
export type License = Dto<'LicenseDto'>;
export type LicenseDetail = Dto<'LicenseDetail'>;
export type Batch = Dto<'BatchDto'>;
export type Issuance = Dto<'IssuanceDto'>;
export type User = Dto<'UserDto'> & { mustChangePassword: boolean; twoFactorEnabled: boolean };
export type Audit = Dto<'AuditDto'>;
export type Dashboard = Dto<'DashboardDto'>;
export interface Page<T> { items: T[]; total: number; page: number; pageSize: number }
export interface BatchRequest { applicationId: string; planId: string; quantity: number; note?: string; requestId: string }
