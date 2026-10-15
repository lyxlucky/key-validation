# 授权策略规格：F02、F05、F06、F07、F08

日期：2026-10-06。用户已确认本轮仅实施这五项，并采用推荐业务规则。

## 目标与兼容

扩展现有在线授权系统，沿用 ASP.NET Core 10、PostgreSQL 17、React / TypeScript、中文管理界面和现有权限、CSRF、MFA 门禁。管理操作留审计；密钥、设备标识、会话令牌仍只存摘要。现有卡和客户端继续有效，新响应字段为增量字段。旧卡及旧卡种在线上限回填为各自设备上限，激活期限为空、功能集为空、试用关闭。

## F02 冻结与解冻

- 仅未禁用、已激活且未到期的授权允许冻结。未激活或已到期返回 `LICENSE_NOT_FREEZABLE`；禁用返回 `LICENSE_DISABLED`。
- 冻结记录服务端时间，撤销全部现有会话，激活和心跳返回 `LICENSE_FROZEN`。重复冻结不再次计时、不重复新增历史。
- 解冻将到期时间设置为解冻时间加冻结时的精确剩余时长；重复解冻不延期。不恢复旧会话，不清除禁用状态。禁用优先级高于冻结；恢复禁用后若尚未解冻，依然冻结。
- 冻结期间禁止续期，返回 `LICENSE_FROZEN`；禁用但未冻结的续期规则保持原有约定。
- 独立历史保存冻结/解冻时间、操作人、原因与冻结时剩余秒数，不随普通审计保留期删除。
- 后台详情展示冻结信息、历史和操作；列表支持 frozen 状态。冻结卡不计入有效/即将到期授权。

## F05 在线会话上限

- 卡种新增 `maxConcurrentSessions`（1–100，省略时等于 `maxDevices`），发卡时保存快照；绑定上限仍独立生效。
- 在线定义为未撤销、绑定未释放、租约及绝对会话期限都严格大于服务端当前时间。
- 超限拒绝新会话，返回 `SESSION_LIMIT_REACHED`，不踢其他设备、不新增绑定、不开始授权计时。
- 同一设备重新激活替换旧会话，不占两个名额。失效会话不占名额，释放会话不自动释放绑定。
- 应用 → 授权（ID 顺序）→ 绑定 → 会话的事务锁序覆盖计数和写入，并发不能突破上限。

## F06 激活期限

- 卡种新增可空 `activationValidityDays`（1–3650）；空表示不限。
- 发卡保存 `activateBefore = createdAt + activationValidityDays`。未激活卡在 `now >= activateBefore` 时拒绝激活，返回 `ACTIVATION_DEADLINE_PASSED`，状态为 `activation_expired`。
- 及时激活后，仅按首次激活起算的使用时长到期，激活期限不再限制它。编辑卡种和续期均不重置激活期限，超期卡由后台禁用后重新发行。
- 列表和详情显示此期限及状态；过期未激活卡不能冻结。

## F07 试用授权

- 每应用独立试用策略，默认关闭、默认时长 3600 秒、功能集为空。管理员可配置启停、时长（1–2592000 秒）和功能集；仅影响之后首次领取的设备。
- 公共试用激活按应用与设备摘要唯一领取；固定一设备、一个在线会话，无明文卡密。试用与正式授权使用相同的租约、撤销和心跳规则。
- 同设备重试返回原试用的新会话，替换旧会话但不延长授权期限。到期或已升级后不得重新领取；关闭试用拒绝领取/重试，但已签发的会话可继续到期。
- 领取记录独立、持久保留；数据库唯一约束和事务锁防止并发重复领取。试用授权的 `batchId` 为空，不伪造发行批次/管理员。
- 曾经使用正式授权的应用/设备不能再首次领取试用，释放历史绑定也不恢复领取资格。正式激活与试用领取共用设备事务锁，处理二者首次并发的边界。
- 在同应用、同设备使用正式卡调用原激活接口即可升级：成功激活正式卡、记录关联、撤销该设备原试用全部会话，在同一事务完成。正式激活失败不得破坏试用；无需新升级协议，已到期试用也可关联正式卡。
- 试用接口复用全局及应用/IP 激活限流、输入验证和错误响应；不记录原始设备标识或令牌。设备标识可由客户端修改，因此本轮提供设备摘要去重，不增加账号、验证码或硬件证明。
- 后台应用中配置试用，授权详情区分试用和正式卡。
- 未开放、已到期和已升级分别返回 403 `TRIAL_DISABLED`、`TRIAL_EXPIRED`、`TRIAL_UPGRADED`。

## F08 功能分级

- 卡种及试用策略配置最多 50 个功能标识，每个匹配 `[a-z][a-z0-9._-]{0,63}`；输入去首尾空格、转小写、去重，拒绝非法输入。
- 发卡/首次领取时深复制功能集。卡种或试用策略编辑不会改变已签发授权。升级采用正式卡自身快照；不提供绕过审计的旧卡直接修改权限接口。
- 激活与心跳返回 `licenseId`、`isTrial`、`features`；客户端仅在当前租约有效且功能包含于该服务端快照时允许受保护操作。空/缺失功能集不隐式授予任何细分能力。
- C# 示例支持试用模式和功能检查，保留旧在线使用方式；文档说明本轮为在线响应，无离线签名许可证。

## 接口契约

现有 CreatePlan/UpdatePlan 请求追加可选 `maxConcurrentSessions`、`activationValidityDays`、`features`。PlanDto 增加同名字段。LicenseDto 的 `batchId` 可空，增加 `maxConcurrentSessions`、`activateBefore`、`frozenAt`、`frozenRemainingSeconds`（可空 double）、`features`、`isTrial`。

| 方法 | 路径（均以 `/api` 开头） | 请求 / 响应 | 权限 |
| --- | --- | --- | --- |
| POST | `/admin/licenses/{id}/freeze` | `{ reason }` / 204 | Operate |
| POST | `/admin/licenses/{id}/unfreeze` | `{ reason }` / 204 | Operate |
| GET | `/admin/licenses/{id}/freeze-history?page=1&pageSize=20` | PagedResult<LicenseFreezeDto> | Read |
| GET | `/admin/applications/{id}/trial-policy` | TrialPolicyDto | Read |
| PUT | `/admin/applications/{id}/trial-policy` | `{ isEnabled, durationSeconds, features }` / TrialPolicyDto | Admin |
| POST | `/client/trials/activate` | `{ appCode, deviceId, deviceLabel? }` / ActivationDto | 公共、限流 |

冻结原因去空格后 1–500 字。LicenseFreezeDto 包含 id、licenseId、frozenAt、unfrozenAt、remainingSeconds、frozenBy、unfrozenBy、freezeReason、unfreezeReason。TrialPolicyDto 包含 applicationId、isEnabled、durationSeconds、features。冻结状态属于授权实体，历史和领取配额属于独立持久记录。

## 验收

领域测试覆盖精确冻结剩余时间、重复操作、禁用优先级、冻结续期拒绝、激活期限等值边界、功能校验和快照。真实 PostgreSQL HTTP 测试覆盖冻结/激活/心跳竞争、并发会话计数、过期名额回收、同设备替换、试用去重/重试/到期/升级事务、权限与 CSRF、迁移兼容和 OpenAPI。浏览器测试覆盖卡种策略配置、冻结/解冻及历史、试用策略和手机布局。客户端测试验证租约与功能检查、试用及正式激活协议。完整原有测试、生产构建、迁移一致性均需通过后再标记完成。
