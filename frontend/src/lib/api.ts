import type { Application, Page } from './types';

const messages: Record<string, string> = {
  PASSWORD_CHANGE_REQUIRED: '请先修改初始密码，再继续使用工作台', CURRENT_PASSWORD_INVALID: '当前密码不正确',
  TWO_FACTOR_INVALID: '验证码无效或已使用，请使用新的验证码或恢复码',
  TWO_FACTOR_CHALLENGE_INVALID: '本次登录验证已失效，请重新输入用户名和密码',
  TWO_FACTOR_ALREADY_ENABLED: '该账号已开启双重验证',
  TWO_FACTOR_NOT_ENABLED: '该账号尚未开启双重验证，请刷新安全设置',
  REBIND_LIMIT_REACHED: '当前窗口的换绑次数已用完', REBIND_COOLDOWN: '换绑仍在冷却中，请在允许时间后重试',
  LICENSE_INVALID: '卡密无效，请核对后重试', LICENSE_EXPIRED: '卡密已过期',
  LICENSE_DISABLED: '卡密已禁用', APPLICATION_DISABLED: '该应用已禁用',
  LICENSE_FROZEN: '授权已冻结，请解冻后重试', LICENSE_NOT_FREEZABLE: '仅已激活且未到期的授权可以冻结',
  SESSION_LIMIT_REACHED: '已达到同时在线会话上限，请先退出其他设备',
  ACTIVATION_DEADLINE_PASSED: '已超过最晚激活时间，请联系管理员重新发行',
  TRIAL_DISABLED: '此应用尚未开放试用', TRIAL_EXPIRED: '此设备的试用已到期，请使用正式卡',
  TRIAL_UPGRADED: '此设备已使用正式授权，不能重新领取试用',
  DEVICE_LIMIT_REACHED: '已达到设备数量上限', BATCH_ALREADY_CREATED: '该批次已创建，无法再次获取明文，请禁用后重新生成',
  APPLICATION_PLAN_DISABLED: '该应用已暂停此卡种发行，请选择其他卡种',
  PLAN_DISABLED: '卡种已停止发行，请选择其他卡种',
  IDEMPOTENCY_CONFLICT: '此请求标识已用于其他参数，请检查原请求结果',
  RATE_LIMITED: '操作过于频繁，请稍后重试', LOGIN_FAILED: '用户名或密码错误，或账户已被锁定',
  INVALID_CREDENTIALS: '用户名或密码错误，或账户已被锁定', VALIDATION_FAILED: '请检查填写的内容',
  CSRF_INVALID: '安全令牌已失效，请刷新页面后重试', FORBIDDEN: '当前账号没有此操作权限',
  NOT_FOUND: '记录不存在或已不可用', APPLICATION_CODE_EXISTS: '应用标识已存在',
  DURATION_LIMIT_EXCEEDED: '续期后超过十年授权上限，请减少延长时长',
  ACCOUNT_DISABLED: '账号已停用，请联系管理员', ACCOUNT_NOT_FOUND: '账号不存在',
  VALIDATION_ERROR: '请检查填写的内容是否符合要求', RESOURCE_CONFLICT: '用户名已存在或记录已变更，请刷新后重试',
  ACCOUNT_SELF_CHANGE_FORBIDDEN: '不能停用自己或降低自己的权限', LAST_ADMIN_REQUIRED: '必须保留至少一位启用的管理员',
  ROLE_PROTECTED: '内置管理员角色受保护，不能修改或删除', BUILT_IN_ROLE_PROTECTED: '内置管理员角色受保护，不能修改或删除', ROLE_IN_USE: '该角色仍有成员，请先调整这些账号的角色',
  CHANNEL_PLATFORM_GRANT_FORBIDDEN: '渠道账号不能获得平台管理权限，请调整角色或渠道关联',
  INVALID_NOTIFICATION_RECIPIENT: '接收账号已停用或没有此应用的访问权限，请重新选择',
  WEBHOOK_TARGET_BLOCKED: '接收地址不符合出站配置，请检查 HTTPS 地址与允许的主机清单',
  DELIVERY_IN_PROGRESS: '该事件正在投递，请稍后查看结果',
  CUSTOMER_DISABLED: '客户已停用或匿名化，不能进行此操作', CUSTOMER_ANONYMIZED: '客户已匿名化，不能恢复或修改其资料',
  CHANNEL_DISABLED: '渠道已停用，请联系管理员处理', CHANNEL_QUOTA_EXHAUSTED: '渠道剩余额度不足，请核对后由管理员调整',
  CHANNEL_ORDER_REQUIRED: '渠道卡密需通过续期订单处理，请在订单管理中创建并履约续期订单',
  CHANNEL_PRICE_NOT_CONFIGURED: '尚未设置此卡种的渠道售价，请先由管理员配置', ACCOUNT_CHANNEL_CONFLICT: '该账号已关联其他渠道，请选择未关联的账号',
  ORDER_NOT_PENDING: '订单已处理，不能再次修改，请刷新查看结果', ORDER_CANCELLED: '订单已取消，不能履约',
  DELIVERY_UNAVAILABLE: '交付内容尚不可用或已超过保留期限，卡密明文无法取回',
  CLIENT_VERSION_INVALID: '客户端版本格式不正确，请使用 0–9999 的三段版本号，例如 2.1.0',
  CLIENT_VERSION_REQUIRED: '此应用要求客户端提供版本号，请更新客户端后重试',
  CLIENT_UPGRADE_REQUIRED: '当前客户端版本低于最低要求，请升级后重试',
  OFFLINE_DISABLED: '离线授权已关闭，请由管理员检查服务配置',
  OFFLINE_UNCONFIGURED: '离线签名密钥尚未配置，请由管理员配置后签发',
  OFFLINE_KEY_CONFIGURATION_INVALID: '离线签名密钥配置无效，请由管理员检查',
  OFFLINE_LICENSE_INVALID: '离线许可证格式或签名无效',
  OFFLINE_KEY_UNKNOWN: '此签名密钥不在受信任的验签密钥中',
  OFFLINE_LICENSE_EXPIRED: '离线许可证已到期，请重新申请签发',
  OFFLINE_LICENSE_NOT_YET_VALID: '离线许可证尚未生效，请核对时间',
  OFFLINE_LICENSE_REVOKED: '离线签发记录已撤销',
  OFFLINE_LICENSE_SUPERSEDED: '此离线许可证已被新的签发记录替代',
  OFFLINE_DEVICE_MISMATCH: '设备标识与离线授权的绑定不一致，请核对激活时的原始标识',
  OFFLINE_APPLICATION_MISMATCH: '离线许可证不属于此应用',
  OFFLINE_FORMAL_LICENSE_REQUIRED: '仅正式授权可以签发离线许可证，试用授权不支持',
  OFFLINE_ACTIVATION_REQUIRED: '请先激活卡密并绑定设备，再签发离线许可证',
  BINDING_RELEASED: '此设备绑定已释放，请使用当前有效的绑定',
};

export function errorCodeText(code: string | null) { return code ? messages[code] ?? code : '操作未完成'; }

export class ApiError extends Error {
  constructor(public status: number, public code: string, public batchId?: string, public traceId?: string) {
    super(messages[code] ?? (status === 401 ? '登录已失效，请重新登录' : status === 403 ? '没有操作权限' : status === 400 ? '提交内容不符合要求，请检查表单' : '操作未完成，请稍后重试'));
  }
}

let csrfToken: string | undefined;
let adminSessionGeneration = 0;
type AdminPhase = 'active' | 'authentication' | 'logout' | 'closed';
type AdminRequestOwner = { generation: number; phase: Exclude<AdminPhase, 'closed'> };
type ApiOptions = { method?: string; body?: unknown; responseType?: 'json' | 'text' };
let adminPhase: AdminPhase = 'closed';
const adminRequests = new Set<AbortController>();
export function getAdminSessionGeneration() { return adminSessionGeneration; }
export function isCurrentAdminSession(generation: number) { return generation === adminSessionGeneration; }
function cancelAdminRequests() {
  const obsolete = [...adminRequests];
  adminRequests.clear();
  // Cancel before a late response can apply Set-Cookie in the browser. Checking
  // ownership after fetch resolves is too late to prevent that side effect.
  for (const request of obsolete) request.abort();
}
export function beginAdminSession(phase: AdminPhase = 'closed') {
  csrfToken = undefined;
  const generation = ++adminSessionGeneration;
  adminPhase = phase;
  cancelAdminRequests();
  return generation;
}
export function requireCurrentAdminSession(generation: number) {
  if (!isCurrentAdminSession(generation)) throw new DOMException('登录状态已变更，请在当前账号下重试。', 'AbortError');
}
function isCurrentAdminRequest(owner: AdminRequestOwner) {
  return isCurrentAdminSession(owner.generation) && owner.phase === adminPhase;
}
function requireCurrentAdminRequest(owner: AdminRequestOwner) {
  if (!isCurrentAdminRequest(owner)) throw new DOMException('登录状态已变更，请在当前账号下重试。', 'AbortError');
}
function changeAdminPhase(owner: AdminRequestOwner, phase: AdminPhase) {
  requireCurrentAdminRequest(owner);
  adminPhase = phase;
  csrfToken = undefined;
  cancelAdminRequests();
}
function requireCurrentAdminResponse(response: Response, owner: AdminRequestOwner) {
  if (isCurrentAdminRequest(owner)) return;
  // A discarded fetch still owns a response stream. Release it before rejecting
  // the old session, or Chromium can keep the HTTP request active indefinitely.
  void response.body?.cancel().catch(() => {});
  requireCurrentAdminRequest(owner);
}
async function withAdminRequest<T>(owner: AdminRequestOwner | null, run: (signal?: AbortSignal) => Promise<T>): Promise<T> {
  if (owner === null) return run();
  requireCurrentAdminRequest(owner);
  const request = new AbortController();
  adminRequests.add(request);
  try { return await run(request.signal); }
  finally { adminRequests.delete(request); }
}

export async function refreshCsrf(generation = getAdminSessionGeneration()) {
  return refreshOwnedCsrf({ generation, phase: 'active' });
}
async function refreshOwnedCsrf(owner: AdminRequestOwner) {
  return withAdminRequest(owner, async signal => {
    const response = await fetch('/api/admin/auth/csrf', { credentials: 'include', cache: 'no-store', signal });
    requireCurrentAdminResponse(response, owner);
    if (!response.ok) throw new Error('无法连接服务，请检查后端是否启动');
    const value = await response.json() as { token: string };
    requireCurrentAdminRequest(owner);
    csrfToken = value.token;
  });
}
export function clearCsrf() { csrfToken = undefined; }

export async function api<T>(path: string, options: ApiOptions = {}): Promise<T> {
  const owner: AdminRequestOwner | null = path.startsWith('/admin/') ? { generation: getAdminSessionGeneration(), phase: 'active' } : null;
  return requestApi<T>(path, options, owner);
}
async function requestApi<T>(path: string, options: ApiOptions, owner: AdminRequestOwner | null): Promise<T> {
  return withAdminRequest(owner, async signal => {
    const checkSession = () => { if (owner !== null) requireCurrentAdminRequest(owner); };
    const method = options.method ?? 'GET';
    const headers: Record<string, string> = {};
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    if (method !== 'GET' && path.startsWith('/admin/')) {
      if (!csrfToken) await refreshOwnedCsrf(owner!);
      checkSession();
      headers['X-CSRF-TOKEN'] = csrfToken!;
    }
    let response: Response;
    try {
      response = await fetch('/api' + path, { method, headers, credentials: 'include', cache: 'no-store', signal, body: options.body === undefined ? undefined : JSON.stringify(options.body) });
    } catch { checkSession(); throw new Error(path === '/admin/license-batches' && method !== 'GET' ? '网络连接失败。若正在生成卡密，请先检查批次是否已创建。' : method === 'GET' ? '网络连接失败，请稍后重试。' : '网络连接失败，操作结果尚未确认。请先检查原记录，再决定是否重试。'); }
    if (owner !== null) requireCurrentAdminResponse(response, owner);
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      checkSession();
      if (owner !== null && response.status === 403 && problem.code === 'PASSWORD_CHANGE_REQUIRED') window.dispatchEvent(new CustomEvent('security-required', { detail: owner.generation }));
      if (owner !== null && response.status === 401 && !['/admin/auth/login', '/admin/auth/two-factor'].includes(path)) {
        clearCsrf();
        window.dispatchEvent(new CustomEvent('session-expired', { detail: owner.generation }));
      }
      throw new ApiError(response.status, problem.code ?? (path === '/admin/auth/login' ? 'LOGIN_FAILED' : 'REQUEST_FAILED'), problem.batchId, problem.traceId);
    }
    if (response.status === 204) return undefined as T;
    const result = options.responseType === 'text' ? await response.text() : await response.json();
    // Include body consumption: a response can arrive before logout but finish
    // parsing after the next login. Its result must not reach state or caches.
    checkSession();
    return result as T;
  });
}

// These capabilities belong to one explicit auth operation. Ordinary callers
// cannot borrow their CSRF/auth exceptions merely by using the same URL.
export function beginAdminAuthentication() {
  const owner: AdminRequestOwner = { generation: beginAdminSession('authentication'), phase: 'authentication' };
  return {
    generation: owner.generation,
    request: <T>(path: '/admin/auth/me' | '/admin/auth/access' | '/admin/auth/login' | '/admin/auth/two-factor', options: ApiOptions = {}) => requestApi<T>(path, options, owner),
    refreshCsrf: () => refreshOwnedCsrf(owner),
    accept: () => changeAdminPhase(owner, 'active'),
  };
}
export type AdminAuthentication = ReturnType<typeof beginAdminAuthentication>;

export function beginAdminLogout() {
  const owner: AdminRequestOwner = { generation: beginAdminSession('logout'), phase: 'logout' };
  return {
    generation: owner.generation,
    finish: async () => {
      try { await requestApi('/admin/auth/logout', { method: 'POST' }, owner); }
      catch (error) {
        // A failed logout keeps the current login usable, but must never reopen
        // or clear a replacement session created while this request was pending.
        if (isCurrentAdminRequest(owner)) changeAdminPhase(owner, 'active');
        throw error;
      }
      // Seal HTTP/security state before any deferred rendering. This does not
      // create an admissible replacement generation for the retiring workspace.
      changeAdminPhase(owner, 'closed');
    },
  };
}

export function queryString(values: Record<string, string | number | undefined | null>) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  return '?' + params.toString();
}

export async function allApplications() {
  const items: Application[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<Application>>(`/admin/applications?page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.items.length || items.length >= result.total) return items;
  }
}
