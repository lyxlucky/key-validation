# 后端代码阅读指南

共享卡种从 [PlanEndpoints.cs](../backend/src/KeyValidation.Api/PlanEndpoints.cs) 进入。[PlanCatalogService.cs](../backend/src/KeyValidation.Application/PlanCatalogService.cs) 管理全局规则与应用引用；[PlanCatalogQueries.cs](../backend/src/KeyValidation.Application/PlanCatalogQueries.cs) 统一目录投影；[PlanAvailability.cs](../backend/src/KeyValidation.Application/PlanAvailability.cs) 在业务事务内按卡种、引用顺序锁定并验证组合。ApplicationPlans 表保存使用关系和本应用发行开关，卡密与订单的参数快照继续独立保存。

目前后端是一个 ASP.NET Core 服务，连接一个 PostgreSQL 数据库。四个项目是同一程序的代码分层，最终启动入口只有 KeyValidation.Api；后台通知、检测和备份任务也注册在这个服务中。

## 先认识四个项目

| 项目 | 主要职责 | 从哪里开始看 |
| --- | --- | --- |
| KeyValidation.Api | 启动服务，定义 HTTP 地址、认证、权限和错误响应 | [Program.cs](../backend/src/KeyValidation.Api/Program.cs)、[Endpoints.cs](../backend/src/KeyValidation.Api/Endpoints.cs) |
| KeyValidation.Application | 编排业务流程：验证卡密、发卡、订单履约、签发离线许可，以及事务顺序 | [LicensingService.cs](../backend/src/KeyValidation.Application/LicensingService.cs)、[OrderFulfillment.cs](../backend/src/KeyValidation.Application/OrderFulfillment.cs) |
| KeyValidation.Domain | 业务对象及自身规则：卡密、设备、会话、订单等 | [License.cs](../backend/src/KeyValidation.Domain/License.cs)、[Records.cs](../backend/src/KeyValidation.Domain/Records.cs) |
| KeyValidation.Infrastructure | 具体实现：EF Core 数据库、Identity、密钥、Webhook 网络、备份工具与后台任务 | [AppDbContext.cs](../backend/src/KeyValidation.Infrastructure/AppDbContext.cs)、[SecretService.cs](../backend/src/KeyValidation.Infrastructure/SecretService.cs) |

编译时的项目引用是 Api → Infrastructure → Application → Domain。运行时 Application 通过 ILicensingData、ICommerceData 等接口使用数据，Program.cs 把这些接口绑定到 AppDbContext。

这里采用务实分层：Application 直接使用 EF Core 的 DbSet 和事务接口，不是完全隔离 ORM 的架构。账号安全服务还在 Api，一些渠道、风险和通知服务还在 Infrastructure；定位业务时应使用下面的功能索引。

## 顺着“激活一张卡密”读

1. 软件发送 POST /api/client/licenses/activate，包含应用代码、卡密、设备标识和可选客户端版本。
2. [Endpoints.cs](../backend/src/KeyValidation.Api/Endpoints.cs) 中的 MapPost 接收请求，把它交给 LicensingService.Activate。
3. [Contracts.cs](../backend/src/KeyValidation.Application/Contracts.cs) 定义 ActivateLicense 请求和 ActivationDto 响应的字段。
4. [LicensingService.cs](../backend/src/KeyValidation.Application/LicensingService.cs) 的 Activate / ActivateTransaction / StartActivation 完成流程：查应用和卡密、检查版本和状态、检查设备与在线上限、建立绑定和会话。
5. [License.cs](../backend/src/KeyValidation.Domain/License.cs) 中的 EnsureUsable、EnsureDeviceCapacity、EnsureSessionCapacity、Activate 负责卡密自身规则。
6. 服务通过 [ILicensingData.cs](../backend/src/KeyValidation.Application/ILicensingData.cs) 读写数据；实际实现来自 [AppDbContext.cs](../backend/src/KeyValidation.Infrastructure/AppDbContext.cs)。
7. 数据库事务提交后返回会话令牌、授权功能和租约。之后软件按约定发送心跳。

~~~mermaid
flowchart LR
    Client[软件客户端] --> Endpoint[Api / Endpoints.cs]
    Endpoint --> Service[Application / LicensingService.Activate]
    Service --> Rules[Domain / License 规则]
    Service --> Data[ILicensingData]
    Data --> Db[Infrastructure / AppDbContext]
    Db --> PG[(PostgreSQL)]
~~~

行锁和事务用于防止并发超发设备名额、重复履约，以及授权撤销后旧请求继续写入。第一次阅读先看流程与方法名称，再阅读锁的具体实现。

## 按功能找代码

| 功能 | HTTP 入口（Api 项目） | 主要实现 |
| --- | --- | --- |
| 应用、卡种、卡密、激活、心跳 | Endpoints.cs | Application/LicensingService.cs、LicensingQueries.cs |
| 续期、备注标签、批量操作 | ManagementEndpoints.cs | Application/LicensingOperations.cs |
| 冻结、试用、授权功能 | PolicyEndpoints.cs | Application/LicensingPolicies.cs、LicensingTrials.cs |
| 设备换绑 | RebindingEndpoints.cs | Application/LicensingRebinding.cs、BindingRelease.cs |
| 账号、角色、权限、数据范围 | AdminAccounts.cs、RoleEndpoints.cs、AccessEndpoints.cs | Api/AccountSecurity.cs、Application/ApplicationAccessScope.cs、Infrastructure/RbacModel.cs、AccessModel.cs |
| 登录安全和双重验证 | AccountSecurityEndpoints.cs | Api/AccountSecurity.cs、Totp.cs |
| 客户、渠道、订单 | CommerceEndpoints.cs | Application/CustomerService.cs、OrderService.cs、OrderFulfillment.cs；Infrastructure/ChannelService.cs |
| 客户门户、申请审批 | PortalEndpoints.cs、CommerceEndpoints.cs | Application/PortalRequestService.cs；Infrastructure/CommerceAuthentication.cs |
| 外部业务 API | ExternalApiEndpoints.cs | Infrastructure/ApiCredentialService.cs，与客户、订单服务共享业务流程 |
| 风险、通知、统计 | ReliabilityEndpoints.cs、AccessEndpoints.cs | Infrastructure/RiskService.cs、NotificationService.cs、OperationsWorker.cs；Application/LicensingReports.cs |
| Webhook | WebhookEndpoints.cs | Infrastructure/WebhookService.cs、WebhookDeliveryProcessor.cs、WebhookTransport.cs |
| 备份、恢复、监控 | BackupEndpoints.cs、MonitoringEndpoints.cs | Infrastructure/Backup*.cs、MonitoringService.cs |
| 版本管理、离线授权 | ClientExtensionEndpoints.cs | Application/ClientVersionPolicy.cs、OfflineLicenseService.cs；Infrastructure/OfflineLicenseKeyring.cs |

## 几个容易误解的命名和写法

- **没有 Controllers 文件夹**：HTTP 层使用 ASP.NET Core Minimal API，以 MapGet、MapPost 等方法注册地址，Endpoints 文件承担常见 Controller 的接口入口职责。
- **partial 不是继承**：多个文件里出现 partial class LicensingService，编译后仍是同一个类。LicensingQueries、LicensingTrials 等只是把这个类的方法按功能分文件；AppDbContext 的多个 Model 文件同理。
- **Contracts / Dto**：用于请求和响应的数据形状；Domain 对象则表示内部业务状态，二者用途不同。
- **Model 文件**：这里多数是 EF Core 表结构、索引、关联和范围过滤的配置，不是网页收到的响应对象。
- **Migrations**：数据库结构升级记录；初次理解业务无需从这些生成文件入手。
- **Application 一词有两层含义**：KeyValidation.Application 是业务逻辑项目；SoftwareApplication 是你在后台创建的“待授权软件应用”。
- **RBAC 与数据范围分别生效**：角色决定能做什么，例如 licenses.manage；应用/渠道范围决定能操作哪些数据。拥有某个权限不代表可以访问所有应用。

## 目前影响可读性的地方

不少类的写法过于紧凑，Program.cs 注册内容较长，功能文件集中在项目根目录，而且部分业务服务所在层并不统一。理解困难来自这些组织问题，并非必须先掌握整套架构才能修改功能。

查一个功能时，先从上表的 HTTP 入口进入，再跟到服务方法与相关实体。新增或修复逻辑时，同时定位 backend/tests/KeyValidation.IntegrationTests 中相应功能测试；这些测试通过真实 PostgreSQL 和 HTTP 验证权限、事务及状态变化。backend/tests/KeyValidation.Tests 主要检查独立规则。

实际接口字段和权限以 [API 契约](api-contract.md) 与运行中的 /openapi/v1.json 为准。
