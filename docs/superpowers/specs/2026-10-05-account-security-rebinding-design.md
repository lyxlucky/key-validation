# 账号安全与设备换绑

日期：2026-10-05。完成：2026-10-06。状态：已实现并验证，133 项后端、17 项浏览器测试及生产构建通过。源自剩余功能 spec 第一批 F01、F04、F12；其余 19 项保持待实施状态。

## 范围与默认规则

本批交付自行改密、首次改密、TOTP 双重验证及恢复码、敏感操作再次验证、设备换绑次数/冷却/人工重置。其余 19 项保留在 roadmap。采用现有 Identity、Data Protection、PostgreSQL 和 React 模块；不引入外部身份服务。外部身份平台会增加部署依赖，统一商业身份体系留待需要时再接入。

用户已确认：“采用推荐规则，先完成这一批”。以下默认规则与本批交付范围已确认。

- 新建账号、管理员重置密码后要求首次改密；迁移时现有账号不强制改密。CLI 引导管理员也保持可用，CLI 密码重置要求改密。
- 新发卡默认每卡滚动 30 天最多换绑 3 次，两次成功释放设备至少间隔 24 小时；已有卡不自动添加限制。单卡可以配置启停、次数、窗口及冷却。
- TOTP 自愿开启，所有后台角色均可保护自己的账号，不批量强制现有管理员。无“永久信任此设备”；登录或再次验证后的敏感操作有效期为 5 分钟。
- 密码、TOTP 密钥、登录 challenge、恢复码仅短暂驻留当前界面内存，不写浏览器存储、不写日志或审计。

## F01 自行修改密码

账号安全页输入旧密码、新密码和确认密码；后端验证旧密码和原有 Identity 强度规则，拒绝新旧相同。修改成功清除 MustChangePassword，更新 security stamp，其他 Cookie 立即失效；当前会话重新签发并刷新 CSRF。

UserDto 增加 `mustChangePassword`、`twoFactorEnabled`。MustChangePassword=true 时 API 只放行 auth me/csrf/logout、security 状态、二次验证及自行改密，其他后台资源返回 403 `PASSWORD_CHANGE_REQUIRED`。二次验证仅更新当前会话的 MFA 验证时间，确保必须改密的账号在五分钟验证过期后仍可先完成验证再改密；不会解除必须改密状态。前端强制进入账号安全页并说明原因，不能靠输入 URL 绕过。现有 cookie 已失效时仍按 401 处理。

新建、管理员重置密码设置 MustChangePassword=true；原有账户管理 API DTO 增加该状态，方便操作人识别。管理员重置密码不取消 MFA，也不解除其保护。

## F12 双重验证

使用标准 TOTP（SHA1、6 位、30 秒；当前和前后一个时间步容差）兼容认证器。验证接受的时间步必须大于该账号最后使用的时间步，避免重放。关键状态在数据库串行锁内检查和更新，跨进程并发使用同一码不能成功两次。

密码正确且 MFA 开启时不签发完整管理 Cookie，仅返回 5 分钟有效的加密 challenge，绑定账号 ID、安全戳和到期时间。提交 TOTP 或一个恢复码完成登录；账号停用、密码/角色改变、challenge 到期均使旧 challenge 无效。错误密码和错误二次验证码受账号锁定和登录 IP 限流约束。

开启：先验证当前密码，生成未生效密钥（5 分钟有效），界面展示二维码及手动输入密钥；验证首个 TOTP 后才启用，并轮换安全戳、签发当前会话。活动密钥与待验证密钥使用服务器 Data Protection 加密持久化，重启后可继续使用；保护密钥目录必须保留。

启用后生成 10 个至少 128 位随机的一次性恢复码，仅返回本次明文；数据库只保存摘要。恢复码验证、消费在同一事务中完成，不能并发复用。再生成会使旧码全部失效。关闭或重生成要求当前密码和 TOTP/恢复码。关闭 MFA 轮换安全戳，其他会话失效。普通管理员不能读取他人 TOTP 密钥或恢复码。

敏感操作指后台所有写操作（精确查卡和登录/退出等认证流程除外）。已开启 MFA 的账号距上次二次验证超过 5 分钟返回 403 `TWO_FACTOR_VERIFICATION_REQUIRED`，拒绝发生在业务写入前。账号安全页可再次验证，随后重试原操作。安全页自身的关闭/恢复码再生成仍要求密码和二次验证码。初始设置和确认不能绕过账户自身检查。不会自动重放结果不明的业务请求。

## F04 换绑

“一次换绑”定义为成功释放一条仍有效的设备绑定，释放立即撤销该绑定所有会话；随后新设备可按原激活接口绑定。绑定上限与有效期保持不变。已释放绑定再次请求是幂等成功，不再计数。

所有标准释放入口都执行同一规则，包括原管理员释放接口与新增客户端当前设备释放接口。客户端只能使用有效的当前设备 session token 释放自身绑定，不能提交他人 binding ID；过期、禁用、已撤销、超租约或应用停用均拒绝。

换绑计数使用独立历史事件，不借助审计保留期。每张卡的策略包含 enabled、maxChanges（1–100）、windowDays（1–365）、cooldownHours（0–8760）。窗口按服务端连续时间计算，边界为 `(now - window, now]`；冷却恰好结束即可释放。禁用策略时不限制，但依然记录成功释放，以便之后启用有完整记录。

卡密详情展示本窗口已用/剩余次数、最后释放时间、下次可释放时间、服务端时间。管理员/操作员可以填写原因后更新单卡策略、重置次数与冷却。重置递增计数代次，历史事件保留，避免相同时刻释放被误漏计；不改变绑定或有效期，不复活会话。策略修改不自动重置计数。

沿用 Application → License → Binding → Session 锁序，释放、重置、配置和激活相互串行，跨账号并发不能突破次数或冷却。

## HTTP 契约

统一 `/api` 前缀、UTC、camelCase、现有 CSRF/角色约定。

### 身份与安全

- UserDto：原字段 + `mustChangePassword:boolean,twoFactorEnabled:boolean`。
- POST `/admin/auth/login` 原请求；普通成功返回 UserDto；需二次验证返回 200 `{requiresTwoFactor:true,challengeToken:string}`，不包含密码且不签发完整 Cookie。
- POST `/admin/auth/two-factor` `{challengeToken,code,useRecoveryCode:false}` → UserDto。
- GET `/admin/security` → `{mustChangePassword,twoFactorEnabled,recoveryCodesRemaining,verificationExpiresAt:string|null}`。
- POST `/admin/security/password` `{currentPassword,newPassword}` → UserDto，重新签发当前 Cookie。
- POST `/admin/security/two-factor/setup` `{password}` → `{sharedKey,authenticatorUri}`。
- POST `/admin/security/two-factor/confirm` `{code}` → `{recoveryCodes:string[],user:UserDto}`。
- POST `/admin/security/two-factor/disable` `{password,code,useRecoveryCode:false}` → UserDto。
- POST `/admin/security/two-factor/recovery-codes` `{password,code,useRecoveryCode:false}` → `{recoveryCodes:string[]}`。
- POST `/admin/security/verify` `{code,useRecoveryCode:false}` → 204，刷新当前 Cookie 的二次验证时间。
- 错误：`PASSWORD_CHANGE_REQUIRED`、`TWO_FACTOR_VERIFICATION_REQUIRED` 为 403；错误二次码为 400 `TWO_FACTOR_INVALID`；旧密码错误为 400 `CURRENT_PASSWORD_INVALID`；失效 challenge 为 400 `TWO_FACTOR_CHALLENGE_INVALID`。

### 换绑

- GET `/admin/licenses/{id}/device-policy` → DevicePolicyDto，所有读角色。
- PATCH `/admin/licenses/{id}/device-policy` `{enabled,maxChanges,windowDays,cooldownHours,reason}` → DevicePolicyDto，Admin/Operator。
- POST `/admin/licenses/{id}/device-policy/reset` `{reason}` → DevicePolicyDto，Admin/Operator。
- 原 POST `/admin/device-bindings/{id}/release` 无请求体，保持 204，但执行配额/冷却。
- POST `/client/device-bindings/release` 无请求体，Bearer 当前设备有效 token，成功 204。
- DevicePolicyDto：`{licenseId,enabled,maxChanges,windowDays,cooldownHours,usedChanges,remainingChanges:number|null,lastChangedAt:string|null,nextAllowedAt:string|null,serverTime}`。
- 配额耗尽 409 `REBIND_LIMIT_REACHED`，冷却中 409 `REBIND_COOLDOWN`；配置/重置 reason 去空格后 1–500 字。

## 代码边界与迁移

账号安全服务/端点单独成文件，核心 Identity 账号服务保留，安全状态加密和并发集中封装。AppDbContext 与 AdminUser 改为 partial，安全/换绑各有单独模型配置文件，统一生成一份新迁移。新字段对旧数据使用兼容默认；不修改已应用迁移。

前端新增 `/security` 页面及入口，登录页增加二次验证步骤，AuthProvider 能识别强制改密和 MFA challenge。新增换绑策略组件嵌入 LicenseDrawer；沿用浅色工作台、手机弹窗滚动和纯文本错误提示。

## 验收

- HTTP 覆盖强制改密只能访问必要端点、旧密码失败不变更、他人 Cookie 失效、当前会话保留。
- TOTP 标准向量、错误码/锁定、未完成 challenge 无权限、challenge 过期/安全戳失效、并发重放拒绝、恢复码一次性、密钥持久化、敏感操作过期阻止写入。
- 换绑覆盖默认/旧卡、窗口及冷却边界、并发不超限、重复释放不计数、人工重置保留历史、客户端不能释放他人绑定、会话即时撤销。
- 浏览器完成强制改密、开启 MFA、退出后 TOTP 登录、恢复码登录、关闭、单卡策略与重置；手机/桌面可用。
- 原 77 项后端和 15 项浏览器业务基线随新规则作必要适配并保留全部断线恢复验收；构建、迁移一致性通过。
