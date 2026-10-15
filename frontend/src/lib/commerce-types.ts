import type { Dto } from './types';

export type Customer = Dto<'CustomerDto'>;
export type CustomerAccessCode = Dto<'CustomerAccessCodeDto'>;
export type OrderSnapshot = Dto<'OrderSnapshotDto'>;
export type OrderHistory = Dto<'OrderHistoryDto'>;
export type Order = Dto<'OrderDto'>;
export type OrderDelivery = Dto<'OrderDeliveryDto'>;
export type OrderCatalog = Dto<'OrderCatalogDto'>;
export type ChannelPrice = Dto<'ChannelPriceDto'>;
export type Channel = Dto<'ChannelDto'>;
export type ChannelQuota = Dto<'ChannelQuotaDto'>;
export type PortalSession = Dto<'PortalSessionDto'>;
export type PortalRequest = Dto<'PortalRequestDto'>;
export type ApiCredential = Dto<'ApiCredentialDto'>;
export type ApiCredentialSecret = Dto<'ApiCredentialSecretDto'>;
