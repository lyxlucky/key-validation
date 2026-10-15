# KeyGate 日常管理扩展

日期：2026-10-05。用户已同意实施推荐的六项功能，并确认下述续期规则。

状态：已实现并完成验收。77 项后端测试、15 项浏览器测试及前端生产构建通过；新迁移已应用且模型一致。验证细节见 [验证记录](../../verification.md)，其余 22 项仍为下一轮待实施范围。

## 范围与方案

本次交付账号管理、批次管理、续期延期、批量操作、备注标签、即将到期列表，沿用现有浅色工作台。推荐直接扩展现有 ASP.NET Core / EF Core / React 模块：无需引入任务队列和新服务。另一方案是后台任务队列，适合数万张批量操作，但本次采用每次最多 100 张的同步、有持久化结果的操作，减少运维和恢复复杂度。

后续 22 项单独记录在 `2026-10-05-keygate-future-roadmap-spec.md`，本次不实施。

## 账号管理

- 新增仅 Admin 可访问的账号页：分页列表、用户名搜索、创建、调整单一角色 Admin/Operator/ReadOnly、停用/恢复、重置密码。
- 创建和重置密码遵守已有 Identity 密码规则。密码不回显到列表，不持久化前端，不写审计；关闭表单清除输入。
- 停用或变更角色、重置密码立即使旧 Cookie 失效。停用账号无法登录；恢复账号不会恢复旧会话。
- 不允许停用自己或把自己降权；串行化管理员变更，禁止失去最后一个启用的管理员。不提供删除账号，保留审计关联。
- 新增 AdminUser.DisabledAt、CreatedAt。现有账号 CreatedAt 迁移填充迁移时刻，不伪造历史注册时间。

## 批次管理

- 新增批次页，按应用、卡种、签发人 ID 和备注筛选，展示批次 ID、名称、数量、签发人、时间、备注、已激活数量和已禁用数量。
- 展示“查看卡密”与整批禁用操作；详情抽屉显示批次摘要。查看卡密跳到卡密页并应用 batchId 筛选。
- 批次统计从当前卡密聚合；已激活指 ActivatedAt 非空，已禁用与已激活可能重叠，不绘制互斥状态图。
- 原有发卡 requestId 查询、明文只显示一次和恢复规则保持不变。

## 续期与批量操作

用户已确认：未激活卡密增加待激活 DurationSeconds；未过期卡密从原 ExpiresAt 累加；已过期卡密从服务端当前时间起算新增时长。ActivatedAt 保留历史值；已激活卡的 DurationSeconds 仍表示发行时快照，续期结果通过 ExpiresAt 和审计体现。

- 每次新增整数小时 1–87600，未激活卡总时长仍不超过 315360000 秒；已激活卡到期时间不得超过当前时间后 10 年（315360000 秒）。禁用卡可续期，但不会自动恢复。应用停用状态同样不改变。
- 续期保留设备绑定；已撤销或失效的会话不复活，客户端需要重新激活。仍有效会话可在之后心跳获取更新后的许可到期时间，既有会话绝对上限不延长。
- 单张续期和批量操作共用 operation API，action 为 disable / enable / extend / metadata。
- 表格勾选当前页最多 100 张；翻页或修改筛选清除选择。先显示所选卡密及影响说明，填写原因后执行。结果逐项显示成功或错误原因。
- 顶层参数无效整批拒绝；记录不存在或单张超时长上限为逐项失败，其他合法项目可成功；不可预期数据库异常整笔事务回滚。
- 每次操作包含 requestId。服务端按 actor + requestId 持久化结果并验证规范化参数指纹；同参数重试返回原结果，不重复续期。不同参数返回 IDEMPOTENCY_CONFLICT。
- 浏览器网络不明时锁定原参数，仅允许原请求重试；待确认请求 metadata 按账号写 sessionStorage，重开页面可恢复。此数据不包含卡密明文或密码。
- 使用现有 Application → License → Binding → Session 锁序，跨应用按 ID 排序；所有操作写审计。

## 备注与标签

- 每张卡 note 最长 500 字，tags 最多 10 个、每个 1–32 字；去除首尾空格、重复标签，作为纯文本展示。
- 单张详情可修改备注/标签；批量 metadata 是整体替换，并在确认中明确提示。空 note/tags 表示清空。
- 卡密列表支持备注关键字匹配和单个标签精确匹配。使用参数化查询。

## 即将到期

- 新增到期列表，3 / 7 / 30 天切换，按应用过滤，按到期时间升序。
- 时间窗口为服务端 Now < ExpiresAt <= Now + N 天（每一天 24 小时）。只包含已激活且未禁用、所属应用启用的卡。
- 显示到期时间、卡密标识、应用、备注标签，并可打开详情续期。续期后刷新列表。
- 展示查询返回 serverTime 和 withinDays，避免客户端时钟造成边界歧义。

## API 契约

继续使用 `/api`、camelCase、UTC、CSRF、现有角色和分页约定。

### 账号

- GET `/admin/accounts?page=1&pageSize=20&search=` → Page<AccountDto>
- POST `/admin/accounts` `{userName,password,role}` → 201 AccountDto
- PATCH `/admin/accounts/{id}/role` `{role}` → AccountDto
- POST `/admin/accounts/{id}/disable`、`/enable` → 204
- POST `/admin/accounts/{id}/reset-password` `{password}` → 204
- AccountDto：`{id,userName,roles:string[],disabledAt:string|null,createdAt:string}`。

### 授权运营

- GET `/admin/batch-overview?page&pageSize&applicationId&planId&createdBy&search` → Page<BatchOverviewDto>
- GET `/admin/batch-overview/{id}` → BatchOverviewDto
- BatchOverviewDto：现有 BatchDto 字段，加 `applicationName,planName,createdByName,activatedCount,disabledCount`。
- GET `/admin/licenses` 在旧参数上新增 `note,tag`；LicenseDto 增加 `note:string|null,tags:string[]`。
- GET `/admin/expiring-licenses?withinDays=7&applicationId&page&pageSize` → `{items:LicenseDto[],total,page,pageSize,serverTime,withinDays}`。
- POST `/admin/license-operations`：`{requestId,licenseIds:guid[],action,additionalSeconds?:number,note?:string,tags?:string[],reason:string}` → OperationResult。
- GET `/admin/license-operations/{requestId}` → 当前账号的 OperationResult，未提交返回 404。
- OperationResult：`{requestId,action,completedAt,succeeded,failed,items:[{licenseId,success,errorCode:string|null,license:LicenseDto|null}]}`。
- 操作请求 action=extend 时 additionalSeconds 必填且为 3600 的正整数倍；metadata 时 note/tags 整体替换。reason 必填且最多 500 字。
- 读取账号仅 Admin；其他读取延续 Read；授权 mutation 仅 Admin/Operator。

## 数据结构与边界

- Domain License 增加 Note、Tags 与受约束的续期方法。
- 增加 LicenseOperationReceipt：Id、ActorId、RequestId、Fingerprint、ResultJson、CreatedAt；(ActorId,RequestId) 唯一。
- AppDbContext 配置标签 PostgreSQL text[]、备注长度、到期索引和操作结果映射；生成新迁移，不改初始迁移。
- Accounts API/服务、运营 API/服务单独成文件，原核心发卡服务仅必要扩展，避免一个端点文件继续膨胀。
- 所有现有页面、文案层级、移动导航、弹窗焦点行为遵循现有 design-system/MASTER.md。

## 验收

1. 后端领域测试覆盖未激活、未过期、已过期和禁用续期及上限。
2. PostgreSQL 集成覆盖重复/并发重试只延期一次、混合结果、无明文泄漏、禁用即时撤销、只读拒绝写入。
3. 账号覆盖停用登录拒绝、旧会话失效、角色修改、密码重置、自保护及最后管理员保护。
4. 批次统计、备注/标签检索、到期边界及排序通过真实数据库验证。
5. 浏览器完成新增账号、批次查看、备注标签编辑、单张/批量续期、批量启停和到期列表；保留原发卡恢复回归。
6. 前后端构建通过，OpenAPI 生成前端类型；新页面 375/768/1024/1440px 无页面横向溢出。

## 自检

六项功能共享原账号/授权模型，适合单次交付；网络恢复与锁序明确，无队列或商业支付依赖。功能范围和续期语义已获用户确认，按此规格连续实施。
