import { Alert, Button, Empty, Tag, Typography } from 'antd';
import type { ReactNode } from 'react';
import { SafetyCertificateOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';

export function Brand({ onNavigate, home = '/' }: { onNavigate?: () => void; home?: string } = {}) {
  return <Link className="brand" to={home} aria-label="KeyGate 首页" onClick={event => { if (!event.defaultPrevented && event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey) onNavigate?.(); }}><span className="brand-icon"><SafetyCertificateOutlined /></span><span className="brand-wordmark">KeyGate<small>LICENSE CONSOLE</small></span></Link>;
}

export function PageTitle({ title, subtitle, extra }: { title: string; subtitle: string; extra?: ReactNode }) {
  const labels: Record<string, string> = { '概览': 'WORKSPACE OVERVIEW', '应用管理': 'APPLICATIONS', '卡密管理': 'LICENSE MANAGEMENT', '操作日志': 'ACTIVITY LOG', '登录日志': 'SIGN-IN HISTORY', '角色管理': 'ROLES & PERMISSIONS', '账号管理': 'TEAM ACCOUNTS', '批次管理': 'ISSUANCE BATCHES', '到期提醒': 'EXPIRING LICENSES' };
  return <div className="page-heading"><div><span className="eyebrow">{labels[title] ?? 'KEYGATE CONSOLE'}</span><Typography.Title level={2}>{title}</Typography.Title><p>{subtitle}</p></div>{extra && <div className="page-heading-actions">{extra}</div>}</div>;
}

export function QueryError({ error, retry }: { error: Error | null; retry: () => unknown }) {
  return error ? <Alert type="error" showIcon title={error.message} action={<Button size="small" onClick={() => retry()}>重试</Button>} className="query-error" /> : null;
}

export const statusNames: Record<string, string> = { unused: '未激活', active: '有效', frozen: '已冻结', activation_expired: '激活超期', expired: '已过期', disabled: '已禁用' };
export function LicenseStatus({ status }: { status: string }) {
  return <Tag className="status-tag" color={{ unused: 'default', active: 'success', frozen: 'processing', activation_expired: 'warning', expired: 'warning', disabled: 'error' }[status]}><span className="status-dot" />{statusNames[status] ?? status}</Tag>;
}
export const dateText = (value?: string | null) => value ? new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short', hour12: false }).format(new Date(value)) : '—';
export function durationText(value: number | string) {
  const seconds = Number(value);
  return seconds % 86400 === 0 ? `${seconds / 86400} 天` : seconds % 3600 === 0 ? `${seconds / 3600} 小时` : `${seconds} 秒`;
}
export const emptyTable = <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无记录" />;
export const actions: Record<string, string> = {
  'role.create': '创建角色', 'role.update': '修改角色权限', 'role.delete': '删除角色', 'account.roles': '调整账号角色',
  'security.login-failed': '登录验证失败', 'security.logout': '退出登录',
  'application.create': '创建应用', 'application.disable': '禁用应用', 'application.enable': '恢复应用',
  'plan.create': '创建卡种', 'application-plan.bind': '引用卡种', 'application-plan.update': '修改应用卡种发行状态',
  'plan.update': '修改卡种', 'batch.create': '生成卡密', 'batch.disable': '禁用批次',
  'license.disable': '禁用卡密', 'license.enable': '恢复卡密', 'binding.release': '解绑设备', 'session.revoke': '撤销会话',
  'admin.create': '创建后台账号', 'admin.reset-password': '重置密码',
  'account.create': '创建后台账号', 'account.role': '调整账号角色', 'account.disable': '停用账号', 'account.enable': '恢复账号', 'account.reset-password': '重置密码',
  'license.extend': '续期卡密', 'license.metadata': '修改备注标签',
  'license.freeze': '冻结授权', 'license.unfreeze': '解冻授权', 'application.trial-policy.update': '配置试用规则',
  'application.version-policy.update': '配置客户端版本',
  'offline-license.issue': '签发离线授权', 'offline-license.supersede': '替代旧离线授权', 'offline-license.revoke': '撤销离线授权',
  'security.login': '后台登录', 'security.login-two-factor': '双重验证登录', 'security.password': '自行修改密码',
  'security.two-factor-setup': '设置认证器', 'security.two-factor-enable': '开启双重验证', 'security.two-factor-disable': '关闭双重验证',
  'security.recovery-codes': '重新生成恢复码', 'security.verify': '再次验证身份',
  'license.device-policy.update': '设置换绑规则', 'license.device-policy.reset': '重置换绑次数',
  'account.access': '调整账号权限范围',
  'risk-policy.update': '设置风险策略', 'risk.review': '复核异常', 'notification-policy.update': '设置通知规则',
  'webhook.create': '创建 Webhook 订阅', 'webhook.update': '更新 Webhook 订阅', 'webhook.rotate-secret': '轮换 Webhook 密钥', 'webhook.retry': '重试事件投递',
  'backup.create': '创建加密备份', 'backup.verify': '验证备份恢复',
  'customer.create': '建立客户档案', 'customer.update': '更新客户资料', 'customer.disable': '停用客户', 'customer.enable': '恢复客户',
  'customer.anonymize': '匿名化客户资料', 'customer.access-code.rotate': '轮换客户访问码',
  'order.create': '创建订单', 'order.fulfill': '完成订单履约', 'order.cancel': '取消订单',
  'channel.create': '创建渠道', 'channel.update': '更新渠道配置', 'channel.quota': '调整渠道额度', 'channel.price': '设置渠道售价',
  'portal-request.create': '提交客户申请', 'portal-request.review': '审批客户申请',
  'api-credential.create': '创建 API 凭证', 'api-credential.update': '更新 API 凭证权限', 'api-credential.rotate': '轮换 API 凭证',
};
