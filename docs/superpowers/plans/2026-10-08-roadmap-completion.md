# 剩余路线图交付 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 实现并验证剩余13项；按用户要求暂缓F18支付。

**Architecture:** 在既有分层和部分类型上扩展，权限范围与持久事件为共享基础。后端任务依序实现，前端按已稳定契约接入；每个模块独立测试后再集成。

**Tech Stack:** ASP.NET Core 10、EF Core/Npgsql、PostgreSQL 17、React19、Ant Design6、Playwright、xUnit。

**Spec:** ../specs/2026-10-08-operations-reliability-design.md、../specs/2026-10-08-commerce-integrations-design.md、../specs/2026-10-08-client-extensions-design.md。

## Global Constraints

- 沿用 ASP.NET Core 10、React/TypeScript、PostgreSQL 17、现有中文设计系统，不增加外部必需服务。
- 用户明确要求“暂时不做支付”，F18 保持待办。
- 旧账号、旧卡、既有接口默认行为保持兼容。
- 当前工作区没有Git仓库；直接在原工作区修改，保留文件快照和报告，不创建提交/分支，不删除工作记录。
- 验收使用真实PostgreSQL、实际pg_dump/pg_restore和真实Chromium；只对外部网络采用受控接收端或HttpMessageHandler。

### Task 1: 应用范围、细分权限、高级查询与报表后端

**Files:** 新增 Application/ApplicationAccessScope.cs、OperationsContracts.cs、LicensingReports.cs；Infrastructure/AccessModel.cs；Api/AccessEndpoints.cs；修改既有查询/端点/服务、模型与安全上下文；新增迁移和 IntegrationTests/OperationsAccessTests.cs。

**Interfaces:** 生产 `ApplicationAccessScope`、`GET auth/access`、`GET/PUT accounts/{id}/access`、`POST licenses/query`、`GET reports/operations`，字段按运营规格；所有后续模块消费同一scope。维护原服务调用签名可选参数兼容。

- [x] 写失败HTTP测试：受限账号对别的应用列表total为0、详情404、批次/嵌套资源/审计/回执不泄露；无issue权限发卡403；旧Cookie在改授权后401；日期/device组合只返回手选卡；统计首次激活去重、在线租约边界。
- [x] 运行 `dotnet test backend/KeyValidation.slnx -c Release --no-restore --filter FullyQualifiedName~OperationsAccess --verbosity quiet`，记录缺功能失败。
- [x] 实现每请求范围、完整资源过滤、操作权限检查及提交前重查，新增DTO/查询/统计与迁移。测试的代表断言为 `Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/api/admin/licenses/{otherId}")).StatusCode);`。
- [x] 运行针对测试与既有后端全套，检查迁移模型一致；写接口清单/验证证据供前端与下一任务。

### Task 2: 异常、通知、备份恢复与监控后端

**Files:** 新增Domain/Operations.cs、Infrastructure/OperationsModel.cs、Api/Operations*.cs、Infrastructure/Backup*.cs（依职责拆分）、IntegrationTests/OperationsReliabilityTests.cs、BackupTests.cs；修改Program、RetentionWorker和必要的授权事件生产点。

**Interfaces:** 消费Task1 scope；生产风险策略/记录/复核、通知收件箱/配置、签名Webhook订阅/投递、备份作业和监控API；持久事件发表接口供Task3复用。完整路由和DTO在实现报告中固定后交前端。

- [x] 写失败测试：同一异常窗口/到期事件并发只生成一次；其他接收人不能读取通知；伪造国家头忽略；Webhook签名和重试去重；备份实际恢复数据且不修改源库；损坏包拒绝；服务故障/恢复只产生一组事故。
- [x] 运行上述针对测试，确认缺失功能失败。
- [x] 实现基于数据库锁/唯一键的后台作业、配置与管理API；以 `HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(timestamp + "\n" + body))` 生成签名，原始body与eventId在重试间保持一致；备份用真实pg工具和认证加密。
- [x] 生成正式迁移，跑针对测试与后端全套；报告实际恢复数据库名称、校验和耗时，不输出秘密。

### Task 2a: 用户追加的标准RBAC、日志与导航调整

**Spec:** ../specs/2026-10-09-administration-usability-design.md。用户明确追加并要求调整现有账号/日志/菜单；先完成共享授权基础，再做商业模块。

- [x] 后端实现标准多角色RBAC、角色权限管理、旧配置无提权迁移与即时撤权，保持数据范围独立。
- [x] 后端区分登录/操作日志，提供受范围限制的可读对象信息，记录安全的变更前后值与登录结果。
- [x] 前端实现角色管理、多角色账号、独立日志页面及中文变更说明；菜单按组折叠且整栏可收缩。
- [x] 真实后端/浏览器回归与独立审查，修复重要问题。

### Task 3: 客户、渠道、人工订单、门户与开放API后端

**Files:** 新增Domain/Commerce*.cs、Infrastructure/CommerceModel.cs、Api/Commerce*.cs、Portal*.cs、Integration*.cs、对应IntegrationTests；修改共享scope、批次/授权ChannelId及必要的授权事务接口。

**Interfaces:** 消费Task1权限/范围与Task2事件投递；生产客户/渠道/订单/交付/申请/凭证API和独立门户Bearer会话。无支付接口。

- [x] 写失败HTTP测试：两次并发履约只有一批、一次额度扣除；中途故障全部回滚；价格/功能快照不随卡种变化；跨客户门户和跨渠道API返回404；轮换访问码/凭证旧值即时失效；旧批量发卡不能绕过渠道额度。
- [x] 运行新测试，确认失败；代表并发断言 `Assert.Single(await db.Batches.Where(x => x.RequestId == orderId).ToListAsync());` 并独立断言额度差值。
- [x] 实现事务/幂等、密文交付保留、门户审批、API范围/限流及事件；迁移回填旧数据ChannelId=null并保持原行为。
- [x] 跑针对测试与后端全套，报告所有DTO/端点、角色与生命周期。

### Task 4: 客户端版本与离线授权后端和C#示例

**Files:** 新增Domain/ClientPolicies.cs、Application/ClientContracts.cs、Infrastructure/ClientPolicyModel.cs、Api/ClientPolicy*.cs；修改激活/心跳协议与samples，新增实际签名/回拨/版本测试和迁移。

**Interfaces:** 消费Task1 scope；激活新增可选clientVersion，心跳支持X-Client-Version；管理版本/离线签发撤销与公开版本信息；C#从受信公钥验证JWS。

- [x] 写失败测试：旧客户端默认能用；设minimum=2.1.0后2.0.9激活/心跳426；2.10.0高于2.9.0；签名真实生成后篡改一个payload字节失败；错设备/应用/时间/回拨/持久化失败均拒绝。
- [x] 运行新后端与客户端测试，记录失败。
- [x] 实现严格版本策略、ES256签发/撤销记录、公钥轮换及独立离线示例；`ECDsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)` 是签名验证原语，不能绕过payload条件检查。
- [x] 生成迁移，跑后端及客户端全套；报告兼容性与离线撤销边界。独立审查Spec/Quality均通过，见task-4-review.md。

### Task 5: 中文管理界面与客户门户

**Files:** 修改App.tsx、AuthProvider、相关既有页面，新增features/operations（报表/风险/通知/运维）、commerce（客户/订单/渠道）、integrations、portal和client-policies；共享组件与CSS沿用design-system/MASTER.md；新增e2e覆盖。

**Interfaces:** 只消费Task1–4实际OpenAPI类型，不自行猜测字段；新增请求辅助函数区分管理员CSRF、门户Bearer和外部凭证。

- [x] 写浏览器场景，先在现有UI确认缺少对应入口/流程而失败。
- [x] 实现高级查询/分页、UTC报表、权限设置、风险复核、通知配置与收件箱、备份/监控、客户/渠道/订单/交付、门户申请审批、API/Webhook、版本配置/离线下载。所有权限影响导航/动作，服务端仍是最终边界。
- [x] 从运行中API生成schema，执行 `npm --prefix frontend run build` 与聚焦e2e；375/768/1024/1440px检查新页面；秘密界面关闭trace/截图，其他页面保留可审阅截图。审查前完整61项浏览器复测通过；最终审查的I1–I3修复及新回归在Task6收尾。

### Task 6: 综合验收、独立审查与交付记录

**Files:** README.md、docs/api-contract.md、docs/verification.md、roadmap、三个规格与本计划进度。

- [x] 对每个功能检查规格到实现/测试证据映射；实际部署本机迁移与API，生成新schema和前端构建。
- [x] 执行后端、客户端、完整Playwright；EF `migrations has-pending-model-changes`；实际隔离恢复验证；不将历史运行计入本轮数字。审查前445项通过，清单记录源码/证据哈希及运行DLL一致性；前端修复后更新完整浏览器结果与清单。
- [x] 独立审查权限/事务/秘密处理及前端完整流程，修复重要问题后重跑覆盖测试。最终退出边界复审Spec/Quality通过，51项定向及109项完整浏览器回归通过；未改动的后端/客户端证据经哈希核对沿用。
- [x] 更新路线图：13项实证完成后标完成，F18明确按用户要求暂缓；记录环境限制和启动步骤。无Git提交或外部部署。验证清单共493项，不叠加重复运行；桌面demo单独验收。

### Task 7: 全部spec完成后的C#手动测试demo

- [x] 开放集成页提供基于最终API的接入说明，覆盖客户端验证、业务API和Webhook。
- [ ] 在所有spec实现、审查和回归完成后，按用户界面偏好制作独立完整C#手动测试程序。
- [ ] 验证demo实际调用、授权功能门禁和异常/离线流程，提供运行说明与手动测试步骤。
