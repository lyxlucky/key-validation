# 后端代码阅读指南

后端采用 ASP.NET Core MVC Web API，按 API、Application、Domain、Infrastructure 分层，每层内部再按业务模块组织。所有业务 HTTP 接口均由 Controller 承载；健康检查、OpenAPI 和 MCP 协议入口在启动组合层注册。

## 项目职责

| 项目 | 职责 | 入口 |
| --- | --- | --- |
| KeyValidation.Api | Controller、HTTP 参数绑定、认证授权、限流、错误响应与启动配置 | [Program.cs](../backend/src/KeyValidation.Api/Program.cs)、[Composition](../backend/src/KeyValidation.Api/Composition) |
| KeyValidation.Application | 用例编排、事务顺序、业务查询、请求与响应契约、基础设施抽象 | [Licensing](../backend/src/KeyValidation.Application/Licensing)、[Abstractions](../backend/src/KeyValidation.Application/Abstractions) |
| KeyValidation.Domain | 领域实体、状态转换与业务规则 | [Licensing](../backend/src/KeyValidation.Domain/Licensing)、[Commerce](../backend/src/KeyValidation.Domain/Commerce) |
| KeyValidation.Infrastructure | EF Core、Identity、密钥、网络发送、备份与后台任务 | [Persistence](../backend/src/KeyValidation.Infrastructure/Persistence)、[Identity](../backend/src/KeyValidation.Infrastructure/Identity)、[Reliability](../backend/src/KeyValidation.Infrastructure/Reliability) |

项目引用方向为 Api → Infrastructure → Application → Domain。Application 通过 `ILicensingData`、`ICommerceData` 等接口访问数据，Infrastructure 提供实现；每层的 `DependencyInjection.cs` 负责注册本层服务。Application 目前保留 EF Core 的 DbSet 和事务抽象，未改成完全隔离 ORM 的仓储架构。

```text
KeyValidation.Api/
  Composition/               服务注册、请求管道与协议入口
  Middleware/                HTTP 错误、CSRF、访问范围、Bearer 与可信地区
  Features/
    Administration/          Controllers、账号服务、HTTP 契约与安全组件
    Licensing/Controllers/   卡密、试用、冻结、设备策略与客户端会话
    Plans/Controllers/       全局卡种与应用引用
    Commerce/Controllers/    客户、订单、渠道、门户与开放接口
    ClientExtensions/        客户端版本与离线授权
    Reliability/             风险、通知、Webhook、备份与监控
  DeveloperIntegration/      开发文档、嵌入式示例与 MCP
  Common/                    HTTP 错误、指标与操作者提取
  OpenApi/                   契约与安全说明
  Cli/                       管理账号、迁移与备份命令
  Compatibility/             原 Map*Api 方法的弃用兼容入口
```

API 面向独立前端，不需要 Razor Views。MVC 的 Model 使用请求/响应 DTO 和领域对象，各有自己的职责。账号安全服务仍在 API 层，因为它们直接依赖 Cookie、HttpContext 与 Identity 登录流程；渠道、通知等现有服务的职责和行为在本轮重构中保持不变。

## Controller 约定

- 使用 `[ApiController]` 和 `[Route]` 声明接口边界，HTTP 动作用 `[HttpGet]`、`[HttpPost]`、`[HttpPut]`、`[HttpPatch]`、`[HttpDelete]` 明确声明。
- 服务由构造函数注入；路由、查询串和请求体分别使用 `[FromRoute]`、`[FromQuery]`、`[FromBody]`。需要强制提供的查询参数使用 `[BindRequired]`。
- `[Authorize(Policy = ...)]` 保留原角色、权限策略及其叠加关系。业务服务中的应用/渠道数据范围检查继续生效。
- `[EnableRateLimiting]` 声明命名限流策略；激活接口使用 `[ServiceFilter]` 接入按应用和 IP 分桶的 MVC Action Filter。全局限流仍先于请求体解析。
- DTO 响应使用具体返回类型；创建或空响应使用 `ActionResult<T>` / `IActionResult`，并通过 `[ProducesResponseType]` 声明响应状态。
- 请求绑定失败使用统一错误响应，保留 `code`、`traceId` 和原有 HTTP 状态。业务校验继续由领域和应用服务执行；未为增加特性而改变既有输入约束。

## 从激活卡密开始阅读

1. [ClientLicensingController.cs](../backend/src/KeyValidation.Api/Features/Licensing/Controllers/ClientLicensingController.cs) 接收 `POST /api/client/licenses/activate`，绑定 `ActivateLicense`，调用应用服务并记录激活指标。
2. [ActivationAdmissionActionFilter.cs](../backend/src/KeyValidation.Api/Features/Licensing/ActivationAdmissionActionFilter.cs) 在绑定后执行原有应用/IP 限流。
3. [LicensingService.cs](../backend/src/KeyValidation.Application/Licensing/LicensingService.cs) 编排事务：查应用和卡密，检查版本、状态、设备与在线上限，建立绑定和会话。
4. [License.cs](../backend/src/KeyValidation.Domain/Licensing/License.cs) 及同目录的 partial 文件维护授权状态与设备策略。
5. [ILicensingData.cs](../backend/src/KeyValidation.Application/Abstractions/ILicensingData.cs) 定义数据访问边界，[AppDbContext.cs](../backend/src/KeyValidation.Infrastructure/Persistence/AppDbContext.cs) 实现它；模型配置位于 `Persistence/Models`。

## 功能索引

| 功能 | HTTP 入口 | 服务位置 |
| --- | --- | --- |
| 应用、卡密、发卡、激活、心跳 | Licensing/Controllers | Application/Licensing |
| 卡种目录、应用引用 | Plans/Controllers/PlansController | Application/Plans |
| 续期、备注、批量操作 | Licensing/Controllers/LicenseManagementController | Application/Licensing |
| 冻结、试用、设备换绑 | Licensing/Controllers | Application/Licensing；Domain/Licensing |
| 账号、角色、权限、登录与双重验证 | Administration/Controllers | Api/Features/Administration/Services、Security；Infrastructure/Identity |
| 客户、订单、门户 | Commerce/Controllers | Application/Commerce；Infrastructure/Commerce/Authentication |
| 渠道、开放 API 凭证 | Commerce/Controllers | Infrastructure/Commerce/Channels、Integrations |
| 版本策略、离线授权 | ClientExtensions/Controllers | Application/ClientVersions、OfflineLicensing；Infrastructure/OfflineLicensing |
| 风险、通知、Webhook、备份、监控 | Reliability/Controllers | Infrastructure/Reliability；Infrastructure/BackgroundJobs |
| 开发接入文档与 MCP | DeveloperIntegration | 原嵌入资源路径保持不变 |

共享卡种由 [PlanCatalogService.cs](../backend/src/KeyValidation.Application/Plans/PlanCatalogService.cs) 维护全局规则与应用引用；[PlanCatalogQueries.cs](../backend/src/KeyValidation.Application/Plans/PlanCatalogQueries.cs) 统一目录投影；[PlanAvailability.cs](../backend/src/KeyValidation.Application/Plans/PlanAvailability.cs) 在业务事务内按原顺序锁定和验证。卡密与订单继续保留独立的发行参数快照。

`partial` 文件仍属于同一个类，用于分隔现有用例或模型配置，不代表继承。数据库迁移保留在 Infrastructure/Migrations；本轮结构调整不修改迁移历史。

## 验证与配置

数据库表统一采用 `t_` 加小写下划线命名，例如 `t_applications`、`t_order_licenses`、`t_asp_net_users`，由 Infrastructure 的 `TableNamingConvention` 集中应用，覆盖业务表与 Identity 表。列名保持现有映射；PostgreSQL 中包含大写字母的列名仍需双引号。

`PrefixTableNames` 增量迁移重命名现有表及关联索引、约束，保留数据和引用，不重建业务表。历史迁移保持原样；框架迁移历史表 `__EFMigrationsHistory` 保持默认名称。备份恢复验证同时识别迁移前后的表名。

`backend/tests/KeyValidation.Tests` 验证独立规则；`backend/tests/KeyValidation.IntegrationTests` 使用真实 PostgreSQL 和 HTTP 验证授权、事务、并发、错误响应及 OpenAPI。测试连接使用 `KEYVALIDATION_TEST_DATABASE`，应与应用的 `ConnectionStrings__Database` 分开配置。

ASP.NET Core 使用 Npgsql 连接串，例如 `Host=...;Port=5432;Database=key_validation;Username=...;Password=...`，不能直接使用 JDBC URL。数据库密码和密钥保存于被忽略的 `.env` 或部署环境变量。

接口字段与权限参见 [API 契约](api-contract.md) 和运行中的 `/openapi/v1.json`。历史设计与实施记录中的旧源码位置保留为历史说明；当前导航以本页为准。
