# 全局卡种目录与应用引用设计

日期：2026-10-09  
状态：已实现并完成验收；用户已选择“跨应用共用，应用选择引用哪些卡种”。验收证据见[验证记录](../../verification.md#全局卡种目录与应用引用)。  
配套计划：[实施计划](../plans/2026-10-09-shared-plan-catalog.md)

## 1. 目标

将卡种作为现有系统内的独立业务模块。管理员统一维护一份卡种规则，多个应用通过引用使用同一个卡种 ID；发卡、订单、续期选卡、渠道定价、查询、统计与开放 API 使用同一目录。

例如：“月卡”配置为 720 小时、1 台设备、1 个在线会话，应用 A、B 都引用它。修改其规则后，两应用后续直接发卡和新建订单读取新规则；已有卡密和已创建订单继续使用自己的参数快照。

## 2. Global Constraints

- 后端保持 C# / ASP.NET Core 10、EF Core 10 和 PostgreSQL 17，TargetFramework 为 net10.0，TreatWarningsAsErrors=true。
- 前端保持 React 19、TypeScript 5.9、Ant Design 6、TanStack Query 5 和现有工作区标签页机制；Node.js 最低版本为 22.12。
- 本功能不增加运行时依赖，不拆分新的部署服务，不引入通用配置平台。
- 产品界面使用中文，统一称“卡种”；时长仍以整数秒存储，界面按小时输入。
- 名称去首尾空格后为 1–120 字符；授权时长为 1–315360000 秒；设备上限、在线会话上限均为 1–100。
- 激活期限为空或 1–3650 天；授权功能最多 50 个，按现有 AuthorizationPolicies 规则规范化与校验。
- 一份卡种的时长、设备上限、在线上限、激活期限和功能完整共享，应用引用不覆盖这些参数。
- 保留 Cookie、CSRF、敏感操作再次验证、账号变更后的写权限复核及应用/渠道/客户范围约束。
- 卡密在发卡时、订单在创建时保存参数快照；修改卡种或停用引用不改写这些快照。
- 保留已有 PlanId、ApplicationId、订单金额、渠道额度流水、幂等请求标识以及客户端激活/心跳/离线授权契约。
- 不物理删除卡种或应用引用，不按名称自动合并旧卡种，不回写历史卡密和订单。
- 验证使用真实 PostgreSQL 集成测试和现有 Playwright；缺少必需环境时不得把跳过写成通过。

## 3. 现状与边界

`LicensePlan` 已是独立数据库记录，但通过 `ApplicationId` 绑定单一应用。配置入口为 `frontend/src/features/applications/PlansDrawer.tsx`；创建、修改、发行混在 `LicensingService` 中。订单和渠道售价也以该单一归属进行校验。

现有渠道售价主键为 `(ChannelId, PlanId)`。共享后，同一渠道可以为不同应用设置同一卡种的不同售价，因此主键必须增加 `ApplicationId`。运营目录仍返回按应用展开的选项；同一个 `PlanId` 出现在两个应用下属于正常数据。

采用“全局记录＋应用引用关系”。模板复制会产生多份配置；应用覆盖会破坏完整共享规则，两者均不纳入本次交付。试用、应用状态、版本策略和换绑策略仍遵循现有的应用或授权规则。

## 4. 数据模型

### 4.1 全局卡种

`LicensePlan` / `Plans` 最终字段：

| 字段 | 规则 |
| --- | --- |
| Id | 继续使用原 GUID；全局卡种身份 |
| Name | 可重名；选择界面对同名项显示短 ID |
| DurationSeconds | 整数秒 |
| MaxDevices / MaxConcurrentSessions | 分别控制设备和在线会话数量 |
| ActivationValidityDays | 可空，从实际发卡时间计算 |
| Features | 规范化后的功能标识数组 |
| IsEnabled | 全局发行开关 |
| CreatedAt | UTC 创建时间 |

移除 `ApplicationId`。将实体从 `Records.cs` 移到专用文件，现有约束和功能数组配置保持有效。

### 4.2 应用引用

新增 `ApplicationPlan` / `ApplicationPlans`：

| 字段 | 规则 |
| --- | --- |
| ApplicationId + PlanId | 联合主键，分别关联应用和全局卡种 |
| IsEnabled | 本应用的发行开关，创建引用时显式设置 |
| CreatedAt | UTC 首次引用时间，重试或重新启用不重置 |

对 `PlanId` 建反向索引。记录关闭发行后仍保留，以支撑历史批次、订单、售价和统计。新增引用、重新启用、暂停发行均通过同一个幂等 PUT 完成；相同状态的重复 PUT 不重复新增引用或变更日志。

批次、订单、渠道售价增加指向 `(ApplicationId, PlanId)` 的联合外键；删除行为为 Restrict。卡密继续归属自己的应用，通过批次追踪卡种。

渠道售价主键改为 `(ChannelId, ApplicationId, PlanId)`。共享规则不代表共享售价。

### 4.3 查询用途

- 新业务可选条件：应用启用、卡种全局启用、应用引用启用，且当前账号具备对应业务权限和数据范围。
- 历史查询保留停用的卡种和引用；停用后仍可按原卡种筛选历史记录。
- 统计事实继续按批次的 `ApplicationId + PlanId` 分组，不能把多条应用引用直接连接到授权事实上造成重复计数。
- 当前卡种名称展示沿用现有目录语义；订单名称继续用创建时快照。其他历史展示不凭空补造过去的名称。

## 5. 模块和接口

### 5.1 代码职责

| 单元 | 职责 |
| --- | --- |
| Domain/LicensePlan.cs、ApplicationPlan.cs | 全局卡种、应用引用实体 |
| Application/PlanCatalogQueries.cs | 按引用查询卡种及按应用展开目录，统一可发行判定 |
| Application/PlanAvailability.cs | 已有业务事务中锁定并验证应用与卡种组合 |
| Application/PlanCatalogService.cs | 全局卡种创建/更新、引用维护、审计；管理事务边界 |
| Application/PlanContracts.cs | 全局及引用 DTO；复用已有 CreatePlan / UpdatePlan |
| Infrastructure/PlanCatalogModel.cs | 引用表、索引和约束；配合现有 AccessModel |
| Api/PlanEndpoints.cs | 独立管理接口及原应用卡种接口的兼容入口 |
| frontend/features/plans | 页面、编辑器、引用面板、查询封装和缓存失效 |

其他业务通过这些单元获取卡种，不再推断“一个卡种只有一个应用”。模块使用现有 DbContext、请求权限、时钟和审计设施，沿用现有业务层依赖 EF Core 的模式。

### 5.2 HTTP 契约

| 接口 | 行为 |
| --- | --- |
| GET /api/admin/plans | 全局卡种分页列表；page=1、pageSize=20，pageSize 上限 100；可用 search / isEnabled 筛选 |
| POST /api/admin/plans | 创建独立卡种，可暂不引用；201＋GlobalPlanDto |
| GET /api/admin/plans/{id} | 全局卡种详情；GlobalPlanDto |
| PATCH /api/admin/plans/{id} | 更新完整共享规则和全局状态；200＋GlobalPlanDto |
| GET /api/admin/plans/{id}/applications | 引用该卡种的应用及引用状态 |
| GET /api/admin/applications/{id}/plan-references | 应用引用分页列表，分别返回全局开关与引用开关 |
| PUT /api/admin/applications/{id}/plan-references/{planId} | 必填 isEnabled；不存在时创建，存在时更新，200＋ApplicationPlanDto |
| GET /api/admin/applications/{id}/plans | 保留旧分页形状；ApplicationId 表示请求应用，IsEnabled 表示实际可发行状态 |
| POST /api/admin/applications/{id}/plans | 兼容入口：原子创建全局卡种并引用到该应用；201＋原 PlanDto |

`GlobalPlanDto` 没有单一应用字段，包含关联应用数量 `ReferenceCount`，计入已暂停的引用。`ApplicationPlanDto` 明确区分 `PlanEnabled`、`IsEnabled`（引用状态）和 `CanIssue`。旧创建入口不能复制已存在的同名卡种或默认关联全部应用。

`PATCH /api/admin/plans/{id}` 的响应升级为全局 DTO；同步更新本管理前端、OpenAPI 和接口说明。旧按应用列表/创建响应保留，开放 API 和客户端不依赖该管理响应。

`ReportPlanDto` 增加 `IsEnabled`，按应用展开；`/api/admin/catalog`、`/api/admin/reports/catalog` 和 `/api/v1/catalog` 共享目录投影。已有字段保持不变。

### 5.3 权限与审计

全局配置、全局管理列表和引用维护要求 `platform.manage`、`AllApplications=true`，且无渠道或客户限制。应用范围管理员可以按已有业务权限查询和使用本应用的卡种；不能借修改共享卡种影响其他应用。上述限制同时在服务入口和 SaveChanges 写入校验中落实。

应用引用查询受 `licenses.read` 和应用范围约束。业务目录沿用现有调用权限：订单专员不因读取卡种选项被额外要求 `licenses.read`。

`ApplicationPlans` 过滤依赖可见应用；`Plans` 过滤依赖可见引用。过滤链单向，避免 Plans 与 ApplicationPlans 的查询过滤器互相递归。不能按 IsEnabled 隐藏历史记录。

- `plan.create`、`plan.update` 为全局操作，ApplicationId 为空；已有全局日志可见性规则继续生效。
- `application-plan.bind`、`application-plan.update` 明确记录 ApplicationId、PlanId、前后引用状态，目标类型为 plan。
- 全局配置变化记录所有规则的前后值；首次创建、修改名称后仍保留可读目标名称。
- 旧日志保留原 ApplicationId，不批量改写；当前全局卡种日志不能被错误归属到某一个引用应用。

## 6. 业务规则、事务及错误

### 6.1 新业务与快照

直接发卡和新建订单必须使用 `PlanAvailability.Require(applicationId, planId, requireEnabled: true)`。渠道定价使用同一方法的 `requireEnabled: false`，允许预先配置已暂停引用的售价，但不允许未引用的组合。

创建订单时固定名称、规则、功能和售价；履约读取订单快照。卡种改名、改规则、停发或引用暂停后，已创建订单仍按已有应用、渠道、客户及订单状态规则履约；不会重新套用新卡种配置。既有订单幂等重试也不能被新增卡种检查提前拒绝。

续期沿用现有显式 additionalSeconds 和冻结限制；选定卡种提供默认时长，显式单次续期时长不成为卡种覆盖配置。旧卡设备与功能权限不随续期选卡自动升级。

### 6.2 锁顺序

- 直接发卡：原请求幂等锁 → 应用 → 卡种共享锁 → 应用引用共享锁 → 新批次/卡密。
- 订单：保留应用 → 渠道 → 客户的现有顺序，再锁卡种 → 应用引用，随后执行原订单/授权流程。
- 引用 PUT：应用共享锁 → 卡种共享锁 → 引用行排他锁。尚不存在的行通过 ON CONFLICT DO NOTHING 与随后 FOR UPDATE 处理并发创建。
- 全局更新：仅锁卡种行 FOR UPDATE；不会先持有卡种锁再锁应用。
- 所有写事务保留提交前权限复核；辅助查询不得自行提交调用者事务。

并发修改允许请求完整使用修改前或修改后的配置，禁止同一批次混入两版参数。暂停引用一旦提交，之后的新发卡/新订单不能成功。

### 6.3 错误与界面恢复

| 条件 | 结果 |
| --- | --- |
| 无管理权限、应用范围不足以管理全局配置 | FORBIDDEN / 403 |
| 应用、卡种、引用不可见或引用不存在 | NOT_FOUND / 404 |
| 引用存在但已暂停 | APPLICATION_PLAN_DISABLED / 409 |
| 全局卡种已停发 | PLAN_DISABLED，沿用现有状态码 |
| 应用停用 | APPLICATION_DISABLED，沿用现有状态码 |
| 卡种规则非法、卡种/引用写请求空 GUID、缺少 isEnabled、分页越界 | VALIDATION_ERROR / 400 |
| 渠道未配置该应用与卡种售价 | CHANNEL_PRICE_NOT_CONFIGURED / 409 |

表单失败保留输入，成功后再关闭并刷新；不能在列表读取失败时把“未加载”当作“无卡种”。客户端选择框只帮助筛选，服务端每次写入仍验证组合。

## 7. 界面和缓存

在“授权管理”下增加独立“卡种管理”页面，支持创建、编辑、全局停发/恢复、分页筛选和查看关联应用。编辑器复用现有 FeatureField、时长输入、弹窗生命周期和中文错误样式。

应用列表入口改为“应用设置”。抽屉包含“引用卡种”和原“试用策略”：管理员选择已有全局卡种，查看只读规则、启用或暂停本应用发行。引用操作不提供覆盖规则的字段。

查询封装集中在 `features/plans/queries.ts`，选择投影放在 `options.ts`。创建/修改卡种和引用成功后统一刷新卡种列表、应用引用、两个目录缓存，以及显示当前名称的批次、渠道和统计视图。

- 发卡、订单、续期选卡仅展示有效组合。
- 批次筛选、报表筛选、已有渠道售价显示保留历史组合。
- 切换应用清空普通表单的原卡种选择；待恢复的幂等请求保留原请求体，由后端确认结果。
- 渠道定价明确选择应用和卡种；查询、React key 和回填售价全部使用应用与卡种组合。
- 共享卡种的 PlanId 在多个应用下重复是正常情况，不做全局去重。
- 375、768、1024、1440 像素宽度均可操作；标签切换、关闭重开和切换账号不泄漏旧表单或缓存。

## 8. 迁移和发布

采用两步迁移，执行计划中的中间状态仅用于可验证的开发检查点，整体完成后统一发布：

1. `AddApplicationPlanReferences`：保留旧 Plans.ApplicationId，创建引用表，把每条旧卡种映射回原应用；引用默认启用，原全局 IsEnabled 决定实际发行状态。调整渠道售价联合主键及业务联合外键。旧创建逻辑在同一事务写入引用。
2. 将发卡、订单、定价、目录、权限和审计全部改为基于引用。
3. `GlobalizePlans`：移除旧外键、旧应用索引和 Plans.ApplicationId；开放不属于单一应用的全局卡种创建。

不合并同名记录，不修改订单/授权快照，不改 PlanId。旧应用即使停用仍建立引用，保证历史完整。

降级到单应用结构仅在每个卡种恰有一条引用时允许；恢复 ApplicationId，并以“全局启用 AND 引用启用”还原旧发行状态。发现零引用或多引用卡种时，Down 在任何 DDL 前明确拒绝，要求使用升级前备份或向前修复，不能随意选一个应用或删除多余引用。

发布需备份数据库、暂停旧服务写入、按顺序迁移并同时部署匹配的 API 和前端。前后版本不支持混合写入；生产数据库迁移属于执行阶段的发布动作。

## 9. 验收

- 同一卡种可被两个应用引用；两个应用按同一规则发卡；第三个未引用应用被拒绝。
- 修改共享规则后，新卡、新订单使用新值；旧卡、旧订单、重复请求及客户端仍遵循既有快照。
- 暂停 A 的引用不影响 B；全局停发阻止全部新业务；历史查询和待履约订单仍按约定工作。
- 同一渠道、同一卡种在 A/B 保存不同售价；修改 A 的价格不覆盖 B，额度扣减和订单幂等规则不变。
- 受限账号、渠道账号和开放 API 只看到允许的应用与卡种组合；全局修改绕过接口直接 SaveChanges 同样失败。
- 迁移保留旧 ID、引用、价格、快照和统计数量；重复引用并发只产生一行；降级失败不造成局部变更。
- 所有选择框、独立页面、应用设置、试用配置、历史筛选和缓存刷新通过真实浏览器验证。
- 新旧后端测试、客户端测试、前端生产构建、相关端到端测试及 EF 模型一致性通过后才标记功能完成。

## 10. 执行环境说明

当前工作目录没有 .git 元数据，因此本轮只保存设计和计划文件，不创建仓库或伪称已提交。实施计划保留 Git 管理环境下的分步提交命令；执行时若仍无 Git，则记录检查点并继续已授权工作。

