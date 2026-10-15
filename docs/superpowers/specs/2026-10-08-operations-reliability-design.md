# 运营与可靠性规格

日期：2026-10-08。范围：F03、F09、F10、F11、F13、F14、F15。用户要求分批完成剩余功能；F18 支付单独暂缓。

## 共同约束

- 沿用 ASP.NET Core 10、React/TypeScript、PostgreSQL 17、现有中文设计系统，不增加外部必需服务。
- 旧账号、旧卡、既有接口默认行为保持兼容。Cookie、CSRF、首次改密、MFA、角色和应用范围同时生效。
- 查询先限制资源范围，再计算总数、分页和统计。不可见资源返回 404；缺少操作权限返回 403。
- 后台任务使用独立作用域；同一任务在多个实例上通过数据库锁和唯一键去重。秘密不进入日志、普通查询或审计正文。
- 时间存 UTC，区间均为左闭右开。前端日期按用户设备时区转换；运营日报明确使用 UTC。

## F13 精细权限

本节按用户于2026-10-09追加的[标准RBAC规格](2026-10-09-administration-usability-design.md)更新。采用用户、角色、权限及两种关联；账号可绑定多个角色，有效权限为角色权限并集，默认拒绝。应用范围和渠道归属独立限制可见数据。迁移保留旧账号的实际权限和范围，不扩大权限。

固定权限：`licenses.read`、`licenses.issue`、`licenses.manage`、`reports.read`、`audit.read`、`risk.review`、`customers.manage`、`orders.manage`、`integrations.manage`、`platform.manage`。保留内置Admin、Operator、ReadOnly；Admin具有全部权限，Operator初始不含`integrations.manage`和`platform.manage`，ReadOnly初始只有三个读取权限。除受保护的Admin外，角色权限可以编辑；不以角色名称判断平台能力，重启不恢复已移除的权限。必须保留至少一个启用的内置Admin账号。

`GET /api/admin/auth/access`响应`{permissions, allApplications, applicationIds, channelId}`。账号角色通过`PATCH /api/admin/accounts/{id}/roles`替换，角色与权限通过`/api/admin/roles`管理。拥有`platform.manage`者通过`GET/PUT /api/admin/accounts/{id}/access`管理独立应用范围，新调用方提交`{allApplications, applicationIds}`。旧`permissions`字段只作为转写角色关联的兼容适配，不保留逐账号授权引擎。渠道关系由渠道管理接口设置，渠道应用集合与账号范围取交集，渠道账号不能获得平台能力。

角色权限、账号角色或数据范围改变后，在同一授权锁协议下轮换受影响账号的安全戳，使旧Cookie失效。自身和内置Admin的范围不能被缩小。下文的Admin管理操作均以`platform.manage`权限校验，业务与Webhook权限仍独立授予。

API、服务与数据查询共同执行范围限制，覆盖应用/卡种列表、卡密详情/明文精确搜索、批次/恢复回执、设备/会话、换绑/冻结历史、到期提醒、仪表盘和审计。混合批量操作对不可见卡返回 NOT_FOUND，禁止泄露其字段；历史操作回执在权限缩小后不可泄露原资源。账号授权变更与授权写入的竞争必须在提交前重新检查权限/安全戳。

`ApplicationAccessScope` 为每请求作用域，包含 `AllApplications`、`ApplicationIds`、`ChannelId`、`Permissions`；无 HTTP 管理请求的客户端和后台任务不受管理账号过滤。数据库构造函数允许测试和迁移不提供 scope。新模块必须复用同一范围对象。

## F03 高级搜索

保留 GET 列表。增加 `POST /api/admin/licenses/query`，正文包含 page(默认1)、pageSize(20，最多100)、applicationId、batchId、status、from/to（发行）、activatedFrom/activatedTo、expiresFrom/expiresTo、note、tag、deviceId。所有非空条件 AND 组合；起止颠倒返回 400。设备查询须指定可见 applicationId，使用现有应用范围内 SHA-256 摘要精确匹配，包含历史与当前绑定；不保存、回显或记录原始设备标识。结果仍为 `PagedResult<LicenseDto>`。

筛选应用时清空不适用批次；改变条件回到第一页；分页保留所有条件。保存筛选不属于本版，设备标识不进入 URL/localStorage。

## F09 运营统计

`GET /api/admin/reports/operations?from=&to=&applicationId=&planId=`，默认最近30个 UTC 日，最多366日。响应包含 `from,to,generatedAt,timeZone="UTC",totals,daily,breakdown`。

`totals` 与分组项目字段：issued（区间发行卡数）、activated（首次激活落在区间的去重卡数）、expired（当前 ExpiresAt 落在区间且已经到期、未冻结的卡数）、onlineLicenses（当前有效在线的去重授权数）、onlineSessions（当前有效会话数）。`daily` 每日 `{date,issued,activated,expired}`，补零；`breakdown` 按应用/卡种返回相同计数及名称，试用单独为 null planId。在线必须同时满足应用启用、授权未禁用/冻结/到期、绑定未释放、会话未撤销且租约/绝对期限均有效。

统计基于授权/绑定/会话事实，审计清理不改变签发和首次激活数字。到期口径是当前排期，不声称保存续期前的历史到期事件。页面每60秒刷新并展示生成时刻和口径。

## F10 异常使用检测

按应用配置，默认开启记录但不自动限制：同卡10分钟成功激活达到10次；同卡1小时释放达到3次；24小时出现不同的可信国家代码。阈值可配置并校验合理范围。只记录证据、规则、窗口、数量和复核状态；无自动封卡。

地区默认不可用，不猜测 IP 归属。只有在配置可信直接代理地址后才接收该代理的 `X-KeyGate-Country` 两位国家代码，转发头处理前核对直接来源；普通客户端同名头忽略。不保存完整 IP 或原始设备 ID。

后台每60秒扫描；同规则/卡/窗口唯一去重。复核状态 open/confirmed/dismissed，复核须理由，记录复核人/时间。列表、详情和复核受应用范围与 `risk.review` 限制。提供按需扫描 API 供运营和验收使用。

## F11 通知

管理员为每应用配置事件类型与接收账号。事件：授权3/7/30天到期（按当前到期时刻去重，续期后可重新提醒）、异常记录、批量操作完成，以及服务异常/恢复。默认站内通知，支持配置签名 HTTPS Webhook；无默认邮件或企业聊天供应商。

站内通知每接收人独立未读状态，普通账号只能查看自己的通知并仍需拥有该应用权限。跨应用批量结果只发送有权读取的计数/引用，不包含卡密或完整回执。

事件和投递记录持久化，事件 ID 稳定；同事件/目的地只有一条投递。Webhook 为至少一次投递，不保证顺序；接收方按事件 ID 去重。签名 `hex(HMAC-SHA256(secret, timestamp + "\n" + rawBody))`，头为 `X-KeyGate-Event-Id`、`X-KeyGate-Timestamp`、`X-KeyGate-Signature`。秘密仅创建/轮换时返回，Data Protection 加密保存。

最多5次自动尝试，首次立即，失败后1/5/30/120分钟，24小时过期；2xx成功，其他结果及网络失败可重试。最后错误保存分类，不保存响应正文；管理员可查看/重新排队。独立任务锁防止并发重复领取，失联任务可重新领取。持久事件发表接口供商业集成复用。

出站连接要求 HTTPS、配置的主机白名单、禁止重定向并验证实际连接 IP，阻止回环/私网/链路本地。仅 Development/Testing 且明确允许本机测试时可使用 loopback HTTP。请求有超时和响应大小上限。

## F14 备份与恢复

备份默认不启用；配置目录、32字节 Base64 AES 密钥和 PostgreSQL 工具路径后可手动执行或每日调度，默认保留7份成功备份。目标 RPO 24小时；RTO 记录本机恢复实测耗时，不作未验证承诺。

使用真实 pg_dump 自定义格式；备份包包括数据库、Data Protection 密钥以及授权/HMAC/离线签名所需配置，整体认证加密。缺密钥、工具失败、超出大小限额、磁盘错误均留下失败状态；任何临时明文在 finally 清理。配置与 API 不接受任意 shell 命令，命令参数通过 ArgumentList/环境变量传递，密码不进入命令行。

管理 API 列出作业状态、大小、时间、校验与错误分类；创建/恢复验证只限 Admin 且遵守敏感操作验证。恢复验证必须新建 `keyvalidation_restore_<随机值>` 数据库，pg_restore 实际恢复，检查迁移、关键表与密钥包可读，再删除该隔离数据库；绝不覆盖当前业务数据库。提供 CLI 恢复到新隔离数据库并导出加密包中的密钥材料至新目录的操作步骤，人工切换连接完成灾难恢复。

路径必须解析到配置的备份根目录，备份 ID 不能成为任意文件路径。仅清理本系统生成且验证归属的过期成功备份；不触碰其他文件。备份密钥自身由运营方另行保管。

## F15 监控

保留 `/health/live` 与 `/health/ready`。每60秒采样数据库可用性和验证接口实际请求计数、5xx错误率、p95延迟，无流量时显示无样本。路由标签来自固定模板，禁止高基数 ID/查询串。管理界面展示当前状态与历史样本；Admin 可读取 Prometheus 文本指标。

默认：数据库失败立即告警；至少20个样本时，5xx>=5%或p95>=500ms连续3次采样告警。每次事故一条持久记录，恢复记录时间并通知；数据库不可用期间保留内存状态，恢复后写入。保留30天采样与180天事故/通知，清理不影响未完成投递。

## 验收

真实 PostgreSQL HTTP 测试覆盖跨范围列表/总数/详情/嵌套资源/批量/回执/统计、权限即时失效及竞争；日期边界、设备精确查找；统计去重、租约边界；异常去重、可信地区；接收人隔离、Webhook签名/重试/并发；真实备份和隔离恢复、损坏拒绝；监控错误率及恢复。浏览器覆盖筛选分页、统计、权限设置、复核、通知与运维状态，375/768/1024/1440px 检查主要新增页面。

