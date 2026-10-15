import type { Dto } from './types';

type ApiNumber = number | string;
export type RiskPolicy = Dto<'RiskPolicyDto'>;
export type RiskEvent = Dto<'RiskEventDto'>;
export type OperationsScan = Dto<'OperationsScanDto'>;
export type NotificationPolicy = Dto<'NotificationPolicyDto'>;
export type Notification = Dto<'NotificationDto'>;
export type WebhookSubscription = Dto<'WebhookSubscriptionDto'>;
export type WebhookSecret = Dto<'WebhookSecretDto'>;
export type WebhookDelivery = Dto<'WebhookDeliveryDto'>;
export type BackupStatus = Dto<'BackupStatusDto'>;
export type BackupJob = Dto<'BackupJobDto'>;
export type BackupVerification = Dto<'BackupVerificationDto'>;
export type MonitoringRoute = Dto<'MonitoringRouteDto'>;
export type MonitoringSample = Dto<'MonitoringSampleDto'>;
export type MonitoringIncident = Dto<'MonitoringIncidentDto'>;
export type WorkerError = Dto<'WorkerErrorDto'>;
export type Monitoring = Dto<'MonitoringDto'>;

export const eventNames: Record<string, string> = {
  'license.expiring': '授权到期提醒', 'risk.detected': '检测到异常', 'batch.completed': '批量操作完成',
  'service.degraded': '服务异常', 'service.recovered': '服务恢复', 'order.fulfilled': '订单履约完成',
  'license.renewed': '授权续期', 'binding.released': '设备已释放',
};
export const eventOptions = Object.entries(eventNames).map(([value, label]) => ({ value, label }));
export const riskStatusNames: Record<string, string> = { open: '待复核', confirmed: '已确认', dismissed: '已排除' };
export const riskRuleNames: Record<string, string> = { activation_burst: '频繁激活', rebinding_burst: '频繁换绑', country_change: '跨地区使用' };
export const deliveryStatusNames: Record<string, string> = { pending: '等待投递', delivering: '正在投递', succeeded: '投递成功', failed: '投递失败', expired: '已过期' };
export function bytesText(value: ApiNumber | null) { return value == null ? '—' : Number(value) >= 1048576 ? `${(Number(value) / 1048576).toFixed(1)} MB` : `${(Number(value) / 1024).toFixed(1)} KB`; }
