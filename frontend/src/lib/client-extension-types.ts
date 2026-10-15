import type { Dto } from './types';

export type VersionPolicy = Dto<'VersionPolicyDto'>;
export type UpdateVersionPolicy = Dto<'UpdateVersionPolicy'>;
export type OfflineLicense = Dto<'OfflineLicenseDto'>;
export type OfflineConfiguration = Dto<'OfflineConfigurationDto'>;
export type SignedOfflineLicense = Dto<'SignedOfflineLicenseDto'>;
export const offlineStatusNames: Record<string, string> = {
  active: '有效', expired: '已过期', revoked: '已撤销', superseded: '已替代', unavailable: '当前不可用',
};
