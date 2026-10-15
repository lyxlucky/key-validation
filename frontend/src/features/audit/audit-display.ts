import type { Audit } from '../../lib/types';
import { actions, dateText, durationText } from '../../components/common';
import { errorCodeText } from '../../lib/api';
import { permissionNames } from '../../lib/operations-types';
import { roleNames } from '../../lib/management-types';

const fieldNames: Record<string, string> = {
  name: '名称', username: '用户名', code: '应用标识', description: '说明', role: '角色', roles: '角色', permissions: '操作权限',
  allapplications: '全部应用', applicationids: '应用范围', channelid: '渠道限制',
  durationseconds: '授权时长', additionalseconds: '延长时长', maxdevices: '设备上限', maxconcurrentsessions: '在线上限',
  activationvaliditydays: '激活期限（天）', isenabled: '启用状态', enabled: '启用状态', features: '功能权限', quantity: '数量',
  note: '备注', tags: '标签', keyhint: '卡密标识', status: '状态', reason: '原因', expiresat: '到期时间', activatebefore: '最晚激活时间',
  disabledat: '停用时间', frozenat: '冻结时间', releasedat: '解绑时间', mustchangepassword: '下次登录修改密码',
  allowselfrelease: '允许自主解绑', maxreleases: '换绑次数上限', windowhours: '换绑窗口（小时）', cooldownminutes: '冷却时间（分钟）',
  activationthreshold: '激活次数阈值', activationwindowminutes: '激活窗口（分钟）', rebindingthreshold: '换绑次数阈值',
  rebindingwindowminutes: '换绑窗口（分钟）', regionwindowhours: '地区比较窗口（小时）', expirydays: '到期提醒天数',
  recipientids: '接收账号', eventtypes: '订阅事件', minimumversion: '最低版本', latestversion: '最新版本', enforceminimum: '强制最低版本',
  updateurl: '更新地址', releasenotes: '发布说明', sha256: '安装包摘要', kid: '签名密钥',
  errorcode: '失败原因', success: '执行成功', reviewedby: '复核人', retryat: '下次重试时间',
  type: '业务类型', fields: '提交的资料字段', delta: '额度变动', balance: '变动后额度', remainingquota: '剩余额度',
  unitpricecents: '单价', totalcents: '总额', disabled: '已停用', anonymized: '已匿名化',
};
const dates = new Set(['expiresat', 'activatebefore', 'disabledat', 'frozenat', 'releasedat', 'retryat']);
const states: Record<string, string> = { active: '有效', unused: '未激活', disabled: '已停用', enabled: '已启用', frozen: '已冻结', expired: '已到期', open: '待复核', confirmed: '已确认', dismissed: '已排除', pending: '待处理', succeeded: '成功', failed: '失败', completed: '已完成', fulfilled: '已履约', cancelled: '已取消', approved: '已批准', rejected: '已拒绝', revoked: '已撤销', superseded: '已替代', unavailable: '当前不可用' };
export const targetTypes: Record<string, string> = { application: '应用', plan: '卡种', batch: '发行批次', license: '卡密', binding: '设备绑定', session: '客户端会话', account: '账号', role: '角色', risk: '异常记录', webhook: 'Webhook', backup: '备份', customer: '客户', order: '订单', channel: '渠道', credential: 'API 凭证', 'api-credential': 'API 凭证', 'portal-request': '客户申请', offline: '离线授权', 'offline-license': '离线授权' };
export const loginActions = ['security.login', 'security.login-two-factor', 'security.login-failed', 'security.logout'];
const loginReasons: Record<string, string> = { credentials: '账号或密码验证未通过', locked: '账号暂时锁定', disabled: '账号已停用', 'invalid-input': '登录信息格式不正确', challenge: '登录验证已失效', 'challenge-expired': '登录验证已过期', 'two-factor': '双重验证未通过', 'invalid-code': '验证码未通过验证' };
const methodNames: Record<string, string> = { password: '密码', totp: '认证器验证码', 'recovery-code': '恢复码', session: '登录会话', cookie: '登录会话' };

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? Object.fromEntries(Object.entries(value).map(([key, entry]) => [key.toLowerCase(), entry])) : {};
}
export function parsedChanges(log: Audit): Record<string, unknown> {
  try { return record(JSON.parse(log.changes)); } catch { return {}; }
}
function displayValue(key: string, value: unknown): string {
  if (value === undefined) return '未记录';
  if (value === null || value === '') return '未设置';
  if (typeof value === 'boolean') return value ? '是' : '否';
  if (key === 'channelid') return '限制到所属渠道';
  if (Array.isArray(value)) {
    if (key === 'fields') return value.map(item => ({ name: '姓名', email: '邮箱', phone: '电话', note: '备注' } as Record<string, string>)[String(item)] ?? String(item)).join('、');
    if (key === 'applicationids') return value.length ? `${value.length} 个应用` : '未选择应用';
    if (key === 'recipientids') return `${value.length} 个账号`;
    return value.map(item => key === 'permissions' ? permissionNames[String(item)] ?? String(item) : key === 'roles' ? roleNames[String(item)] ?? String(item) : String(item)).join('、') || '无';
  }
  if (key === 'durationseconds' || key === 'additionalseconds') return durationText(String(value));
  if (dates.has(key) && !Number.isNaN(new Date(String(value)).getTime())) return dateText(String(value));
  if (key === 'status') return states[String(value)] ?? String(value);
  if (key === 'type') return ({ issue: '发卡', renewal: '续期', rebinding: '换绑' } as Record<string, string>)[String(value)] ?? String(value);
  if (key === 'unitpricecents' || key === 'totalcents') return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(Number(value) / 100);
  if (key === 'role') return roleNames[String(value)] ?? String(value);
  if (key === 'errorcode') return errorCodeText(String(value));
  if (typeof value === 'object') return '详见已记录数据';
  return String(value);
}
export interface AuditChange { key: string; label: string; before: string; after: string }
export function auditChanges(log: Audit): { comparison: boolean; rows: AuditChange[] } {
  const changes = parsedChanges(log);
  if (log.action === 'channel.price' && Object.hasOwn(changes, 'after')) return { comparison: true, rows: [{ key: 'unitpricecents', label: '售价', before: displayValue('unitpricecents', changes.before), after: displayValue('unitpricecents', changes.after) }] };
  const comparison = Object.hasOwn(changes, 'before') && Object.hasOwn(changes, 'after');
  const before = record(changes.before);
  const after = Object.hasOwn(changes, 'after') ? record(changes.after) : changes;
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(key => fieldNames[key] && (!comparison || JSON.stringify(before[key]) !== JSON.stringify(after[key])));
  return { comparison, rows: keys.map(key => ({ key, label: fieldNames[key], before: displayValue(key, comparison && changes.before === null ? null : before[key]), after: displayValue(key, comparison && changes.after === null ? null : after[key]) })) };
}
export const actorText = (log: Audit) => log.actorName || (log.actorId === 'cli' ? '命令行管理员' : log.actorId === 'system' ? '系统任务' : log.category === 'login' && log.result === 'failed' ? '未确认账号' : '操作人未记录');
export const targetText = (log: Audit) => log.targetName || '对象名称未记录';
export const methodText = (log: Audit) => log.action === 'security.logout' ? '退出登录' : methodNames[log.method ?? ''] ?? '未记录';
export function auditSummary(log: Audit) {
  if (log.category === 'login') {
    if (log.result === 'failed') return log.errorCategory ? loginReasons[log.errorCategory] ?? '登录验证未通过，详见记录' : '登录验证未通过';
    return log.action === 'security.logout' ? '已结束本次登录会话' : '登录验证通过';
  }
  const changes = parsedChanges(log);
  if (log.result === 'failed') return typeof changes.errorcode === 'string' ? errorCodeText(changes.errorcode) : '操作未完成，查看详情了解原因';
  if (log.result === 'submitted') return '任务已提交，最终结果请在对应功能页查看';
  const { comparison, rows } = auditChanges(log);
  return rows.slice(0, 2).map(row => comparison ? `${row.label}：${row.before} → ${row.after}` : `${row.label}：${row.after}`).join('；') || `${actions[log.action] ?? '操作'}已记录`;
}
