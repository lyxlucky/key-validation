# 全局卡种目录与应用引用 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 将卡种拆成独立模块，让多个应用引用同一份完整配置，并使发卡、订单、定价、查询和统计一致使用它。

**Architecture:** 在现有单服务中保留全局 Plans，新增 ApplicationPlans 引用表；统一查询投影、事务内可用性检查和管理服务。采用“先增加引用表、再替换消费者、最后移除单应用归属”的两次迁移；卡密与订单继续使用既有快照。前端新增独立卡种页面和应用引用面板，集中查询及缓存刷新。

**Tech Stack:** C# / ASP.NET Core 10、EF Core 10、PostgreSQL 17；React 19、TypeScript 5.9、Ant Design 6、TanStack Query 5、Playwright。

**Spec:** [docs/superpowers/specs/2026-10-09-shared-plan-catalog-design.md](../specs/2026-10-09-shared-plan-catalog-design.md)。执行前读取设计和本计划。

## Global Constraints

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

---

## 执行约定

本计划的九项任务已实施并完成验收。代码块保留为设计与实施参考，实际代码及复验结果见[验证记录](../../verification.md#全局卡种目录与应用引用)。当前目录无 Git，提交检查点仅作完成记录，未创建提交。

当前目录无 .git。各任务的提交命令只适用于执行时已存在的 Git 受控副本；若仍无 Git，记录该检查点并继续，不自动初始化仓库，不因无法提交中断开发。执行前读取实际 AGENTS.md 和当前变更，保护已有工作。

命令默认从仓库根目录运行，前端命令明确指定工作目录。集成测试的 ApiFixture 位于 `backend/tests/KeyValidation.IntegrationTests/ApiTests.cs`：它优先读取 KEYVALIDATION_TEST_DATABASE，否则读取本工作区忽略的 .local/dev-secrets.json，创建自己的随机测试数据库。不要打印连接字符串或把密钥复制到文档。测试所需 .NET / Node / PostgreSQL 安装步骤见 README。

迁移使用两个固定 ID，方便任务交接和升级测试引用：
- `20261009100000_AddApplicationPlanReferences`
- `20261009101000_GlobalizePlans`

通过 EF CLI 生成迁移与 Designer 后，按任务内命令改成上述固定文件名及 MigrationAttribute；保留 EF 生成的完整模型快照。不得只手写 Up 而遗漏 Designer 和 AppDbContextModelSnapshot。旧历史迁移文件不改。

任务 1–4 有依赖，顺序执行；任务 5 验证完整后端；任务 6–8 顺序完成前端；任务 9 做最终贯通。开发中间检查点不单独上线。

## 文件职责地图

| 文件 | 操作与职责 |
| --- | --- |
| backend/src/KeyValidation.Domain/ApplicationPlan.cs | 新建；引用实体 |
| backend/src/KeyValidation.Domain/LicensePlan.cs | 新建；从 Records.cs 移入卡种实体并去掉 ApplicationId |
| backend/src/KeyValidation.Domain/Records.cs | 仅移出 LicensePlan，其他记录不重组 |
| backend/src/KeyValidation.Application/PlanCatalogQueries.cs | 新建；按应用投影、历史与可发行状态 |
| backend/src/KeyValidation.Application/PlanAvailability.cs | 新建；调用者事务内的行锁及可用性验证 |
| backend/src/KeyValidation.Application/PlanContracts.cs | 新建；全局与引用接口 DTO |
| backend/src/KeyValidation.Application/PlanCatalogService.cs | 新建；管理读写、引用、审计 |
| backend/src/KeyValidation.Application/ILicensingData.cs | 暴露 ApplicationPlans；ICommerceData 自动继承 |
| backend/src/KeyValidation.Application/LicensingService.cs | 发卡改用统一验证；移出卡种管理方法 |
| backend/src/KeyValidation.Application/LicensingQueries.cs | 应用卡种查询委托目录；删除单应用映射器 |
| backend/src/KeyValidation.Application/LicensingReports.cs | 目录按引用展开；统计事实不增加引用表连接 |
| backend/src/KeyValidation.Application/OperationsContracts.cs | ReportPlanDto 增加 IsEnabled |
| backend/src/KeyValidation.Application/ApplicationAccessScope.cs | 全局管理范围门禁 |
| backend/src/KeyValidation.Application/OrderService.cs | 新订单按引用取规则与精确售价 |
| backend/src/KeyValidation.Infrastructure/PlanCatalogModel.cs | 新建；引用表、索引及联合外键 |
| backend/src/KeyValidation.Infrastructure/AppDbContext.cs | DbSet、模型注册；移除卡种单应用外键 |
| backend/src/KeyValidation.Infrastructure/AccessModel.cs | 引用范围、全局写保护与审计归属 |
| backend/src/KeyValidation.Infrastructure/AuditDisplay.cs | 全局卡种与应用引用的可读审计 |
| backend/src/KeyValidation.Infrastructure/CommerceModel.cs | 渠道售价三字段主键 |
| backend/src/KeyValidation.Infrastructure/ChannelService.cs | 价格查询与写入精确使用应用＋卡种 |
| backend/src/KeyValidation.Api/PlanEndpoints.cs | 新建；全局及应用引用路由、旧路由兼容 |
| backend/src/KeyValidation.Api/Endpoints.cs | 移走原三条卡种路由，保留其他路由 |
| backend/src/KeyValidation.Api/Program.cs | 注册目录服务和路由 |
| frontend/src/features/plans/types.ts | 新建；导出 OpenAPI 生成类型别名 |
| frontend/src/features/plans/queries.ts | 新建；全部卡种查询、写入与缓存刷新 |
| frontend/src/features/plans/options.ts | 新建；按应用和用途生成选项 |
| frontend/src/features/plans/PlansPage.tsx | 新建；独立分页管理页 |
| frontend/src/features/plans/PlanEditor.tsx | 新建；全局规则编辑器 |
| frontend/src/features/plans/PlanApplicationsDrawer.tsx | 新建；查看关联应用 |
| frontend/src/features/plans/ApplicationPlanReferences.tsx | 新建；应用内选择引用及发行开关 |
| frontend/src/features/applications/ApplicationSettingsDrawer.tsx | 新建；引用面板＋现有试用策略 |
| frontend/src/features/applications/PlansDrawer.tsx | 被以上组件替代后删除 |
| frontend/src/App.tsx、features/workspace/Workspace.tsx | 新菜单、页面范围门禁 |
| frontend/src/features/operations/useOperationsCatalog.ts | 委托卡种模块查询 |
| frontend/src/features/licenses/IssueModal.tsx | 发卡选择改用统一查询 |
| frontend/src/features/batches/BatchesPage.tsx | 历史筛选改用统一查询 |
| frontend/src/features/commerce/OrderEditor.tsx、PortalRequestReview.tsx | 使用统一的可发行选项 |
| frontend/src/features/commerce/ChannelDetail.tsx | 两级定价选择和组合查价 |
| frontend/src/features/operations/ReportsPage.tsx | 使用共享目录缓存、保留历史选项 |
| frontend/src/lib/schema.d.ts、lib/api.ts、components/common.tsx | 生成契约、中文错误及审计动作名称 |

新增测试文件逐任务列出；已有测试只更新因正式行为变化而失效的入口或断言，不放宽其他断言。

## Task 1: 建立可回填的应用引用关系

**Files:**
- Create: `backend/src/KeyValidation.Domain/ApplicationPlan.cs`
- Create: `backend/src/KeyValidation.Infrastructure/PlanCatalogModel.cs`
- Create: `backend/src/KeyValidation.Infrastructure/Migrations/20261009100000_AddApplicationPlanReferences.cs`
- Create: `backend/src/KeyValidation.Infrastructure/Migrations/20261009100000_AddApplicationPlanReferences.Designer.cs`
- Modify: `backend/src/KeyValidation.Application/ILicensingData.cs`、`LicensingService.cs` 的 CreatePlan
- Modify: `backend/src/KeyValidation.Infrastructure/AppDbContext.cs`、`CommerceModel.cs`、`Migrations/AppDbContextModelSnapshot.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanMigrationTests.cs`
- Create: `backend/tests/KeyValidation.IntegrationTests/SharedPlanTestData.cs`

**Interfaces:**
- Consumes: 现有 LicensePlan、LicenseBatch、CommerceOrder、ChannelPrice、ApiFixture.Catalog / Issue。
- Produces: `ApplicationPlan`；`ILicensingData.ApplicationPlans: DbSet<ApplicationPlan>`。
- Produces test helper: `SharedPlanTestData.Reference(ApiFixture fixture, Guid applicationId, Guid planId, bool enabled = true): Task`。

- [x] **Step 1: 写出引用与升级回填测试**

在 SharedPlanTestData.cs 放入后续测试共用的数据构造器；这是测试范围内的直接 DB 准备，不是生产接口：

~~~csharp
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KeyValidation.IntegrationTests;

internal static class SharedPlanTestData
{
    internal static async Task Reference(ApiFixture fixture, Guid applicationId,
        Guid planId, bool enabled = true)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ApplicationPlans.SingleOrDefaultAsync(
            x => x.ApplicationId == applicationId && x.PlanId == planId);
        if (row is null)
        {
            row = new ApplicationPlan
            {
                ApplicationId = applicationId, PlanId = planId,
                CreatedAt = fixture.Clock.GetUtcNow()
            };
            db.ApplicationPlans.Add(row);
        }
        row.IsEnabled = enabled;
        await db.SaveChangesAsync();
    }
}
~~~

SharedPlanMigrationTests.cs 使用真实旧版本数据库回填；以下是完整测试类：

~~~csharp
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OldPlanIdsAndLicensesSurviveReferenceBackfill()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009041239_ClientExtensions");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var reference = await db.ApplicationPlans.SingleAsync(
            x => x.ApplicationId == app.Id && x.PlanId == plan.Id);
        Assert.True(reference.IsEnabled);
        Assert.Equal(plan.CreatedAt, reference.CreatedAt);
        Assert.Equal(plan.Id, (await db.Batches.SingleAsync(
            x => x.Id == issued.Batch.Id)).PlanId);
        var card = await db.Licenses.SingleAsync(x => x.Id == issued.Licenses[0].Id);
        Assert.Equal(plan.DurationSeconds, card.DurationSeconds);
        Assert.Equal(app.Id, card.ApplicationId);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
~~~

- [x] **Step 2: 运行红灯**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~SharedPlanMigrationTests`  
Expected: 编译失败明确指出 ApplicationPlan / ApplicationPlans 不存在；不要把连接失败当红灯证据。

- [x] **Step 3: 增加实体、数据接口和模型关系**

此任务暂时保留 LicensePlan.ApplicationId，让现有创建与业务继续工作。

~~~csharp
// Domain/ApplicationPlan.cs
namespace KeyValidation.Domain;

public sealed class ApplicationPlan
{
    public Guid ApplicationId { get; set; }
    public Guid PlanId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
~~~

在 ILicensingData 增加 `DbSet<ApplicationPlan> ApplicationPlans { get; }`。AppDbContext 的 OnModelCreating 调用 `ConfigurePlanCatalog(b)`，并声明 `partial void ConfigurePlanCatalog(ModelBuilder b);`。新模型文件：

~~~csharp
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed partial class AppDbContext
{
    public DbSet<ApplicationPlan> ApplicationPlans => Set<ApplicationPlan>();

    partial void ConfigurePlanCatalog(ModelBuilder b)
    {
        b.Entity<ApplicationPlan>(e =>
        {
            e.ToTable("ApplicationPlans");
            e.HasKey(x => new { x.ApplicationId, x.PlanId });
            e.HasIndex(x => x.PlanId);
            e.HasOne<SoftwareApplication>().WithMany()
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicensePlan>().WithMany()
                .HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LicenseBatch>().HasOne<ApplicationPlan>().WithMany()
            .HasForeignKey(x => new { x.ApplicationId, x.PlanId })
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<CommerceOrder>().HasOne<ApplicationPlan>().WithMany()
            .HasForeignKey(x => new { x.ApplicationId, x.PlanId })
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChannelPrice>().HasOne<ApplicationPlan>().WithMany()
            .HasForeignKey(x => new { x.ApplicationId, x.PlanId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
~~~

CommerceModel 的 ChannelPrice 主键换为：

~~~csharp
e.HasKey(x => new { x.ChannelId, x.ApplicationId, x.PlanId });
~~~

CreatePlan 中与 `db.Plans.Add(plan)` 同一事务加入：

~~~csharp
db.ApplicationPlans.Add(new ApplicationPlan
{
    ApplicationId = appId, PlanId = plan.Id, IsEnabled = true,
    CreatedAt = plan.CreatedAt
});
~~~

- [x] **Step 4: 生成扩展迁移并将回填放在联合外键之前**

~~~powershell
dotnet ef migrations add AddApplicationPlanReferences --project backend/src/KeyValidation.Infrastructure --startup-project backend/src/KeyValidation.Api
~~~

生成文件改为约定 ID；只对刚生成且唯一匹配的文件执行：

~~~powershell
$migrationDirectory = Join-Path (Get-Location) 'backend/src/KeyValidation.Infrastructure/Migrations'
$generated = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*_AddApplicationPlanReferences.cs')
if ($generated.Count -ne 1) { throw 'Expected one generated migration.' }
$generatedId = [IO.Path]::GetFileNameWithoutExtension($generated[0].Name)
$plannedId = '20261009100000_AddApplicationPlanReferences'
if ($generatedId -ne $plannedId) {
    Rename-Item -LiteralPath $generated[0].FullName -NewName ($plannedId + '.cs')
    $designerSource = Join-Path $migrationDirectory ($generatedId + '.Designer.cs')
    $designerTarget = Join-Path $migrationDirectory ($plannedId + '.Designer.cs')
    Rename-Item -LiteralPath $designerSource -NewName ($plannedId + '.Designer.cs')
    $designerContent = Get-Content -LiteralPath $designerTarget -Raw
    Set-Content -LiteralPath $designerTarget -Value $designerContent.Replace($generatedId, $plannedId) -Encoding utf8
}
~~~

在 Up 的 CreateTable(ApplicationPlans) 之后、为已有业务表添加联合外键之前插入：

~~~csharp
migrationBuilder.Sql("""
    INSERT INTO "ApplicationPlans" ("ApplicationId", "PlanId", "IsEnabled", "CreatedAt")
    SELECT "ApplicationId", "Id", TRUE, "CreatedAt" FROM "Plans";
    """);
~~~

确认生成迁移先删除旧 ChannelPrices 主键再创建三字段主键。Down 逆序撤销联合外键、恢复旧主键、删除引用表；最终跨应用数据的降级门禁由任务 4 的较新迁移先执行。

- [x] **Step 5: 运行绿灯及原迁移覆盖**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter "FullyQualifiedName~SharedPlanMigrationTests|FullyQualifiedName~CommerceMigrationTests|FullyQualifiedName~AuthorizationPolicyMigrationTests"`  
Expected: 全部通过，已有 ID 和卡密规则不变，EF 无待迁移模型变化。

- [x] **Step 6: 提交检查点**

~~~powershell
git add -- backend/src/KeyValidation.Domain/ApplicationPlan.cs backend/src/KeyValidation.Application/ILicensingData.cs backend/src/KeyValidation.Application/LicensingService.cs backend/src/KeyValidation.Infrastructure/PlanCatalogModel.cs backend/src/KeyValidation.Infrastructure/AppDbContext.cs backend/src/KeyValidation.Infrastructure/CommerceModel.cs backend/src/KeyValidation.Infrastructure/Migrations/20261009100000_AddApplicationPlanReferences.cs backend/src/KeyValidation.Infrastructure/Migrations/20261009100000_AddApplicationPlanReferences.Designer.cs backend/src/KeyValidation.Infrastructure/Migrations/AppDbContextModelSnapshot.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanMigrationTests.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanTestData.cs
git commit -m "feat: add application references for license plans"
~~~

## Task 2: 统一目录、范围过滤和直接发卡

**Files:**
- Create: `backend/src/KeyValidation.Application/PlanCatalogQueries.cs`、`PlanAvailability.cs`
- Modify: `backend/src/KeyValidation.Application/LicensingQueries.cs`、`LicensingReports.cs`、`LicensingService.cs`、`OperationsContracts.cs`
- Modify: `backend/src/KeyValidation.Infrastructure/AccessModel.cs`
- Modify: `backend/src/KeyValidation.Api/Program.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanIssuanceTests.cs`
- Modify test: `backend/tests/KeyValidation.IntegrationTests/OperationsAccessTests.cs` 的目录字段断言

**Interfaces:**
- Consumes: ApplicationPlans、现有 PlanDto、ReportPlanDto、LicensingService 的事务和应用锁。
- Produces: `PlanCatalogQueries.Scoped(): IQueryable<PlanDto>`。
- Produces: `PlanCatalogQueries.Catalog(): IQueryable<ReportPlanDto>`。
- Produces: `PlanAvailability.Require(Guid applicationId, Guid planId, bool requireEnabled = true): Task<LicensePlan>`；必须已有事务，由调用者提交。
- Produces: `ReportPlanDto(Guid Id, Guid ApplicationId, string Name, bool IsEnabled)`；响应字段必定存在。

- [x] **Step 1: 写跨应用发行及历史保留测试**

SharedPlanIssuanceTests.cs：

~~~csharp
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanIssuanceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OnePlanIssuesIntoTwoApplicationsButNotAnUnreferencedApplication()
    {
        var (admin, first, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (secondAdmin, second, _) = await fixture.Catalog();
        using var secondCleanup = secondAdmin;
        var (thirdAdmin, third, _) = await fixture.Catalog();
        using var thirdCleanup = thirdAdmin;
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id);

        var a = await ApiFixture.Issue(admin, first, plan);
        var b = await ApiFixture.Issue(admin, second, plan);
        Assert.Equal(first.Id, a.Batch.ApplicationId);
        Assert.Equal(second.Id, b.Batch.ApplicationId);
        Assert.Equal(plan.Id, b.Batch.PlanId);
        var rejected = await admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(third.Id, plan.Id, 1, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

        var catalog = (await admin.GetFromJsonAsync<ReportsCatalogDto>("/api/admin/catalog"))!;
        var options = catalog.Plans.Where(x => x.Id == plan.Id).ToArray();
        Assert.Equal(2, options.Length);
        Assert.Contains(options, x => x.ApplicationId == first.Id && x.IsEnabled);
        Assert.Contains(options, x => x.ApplicationId == second.Id && x.IsEnabled);
        var report = (await admin.GetFromJsonAsync<OperationsReport>(
            $"/api/admin/reports/operations?planId={plan.Id}"))!;
        Assert.Equal(2, report.Totals.Issued);
        Assert.Equal(2, report.Breakdown.Count);
        Assert.Contains(report.Breakdown, x => x.ApplicationId == first.Id && x.Issued == 1);
        Assert.Contains(report.Breakdown, x => x.ApplicationId == second.Id && x.Issued == 1);
    }

    [Fact]
    public async Task PausingOneReferenceKeepsItsOldCardAndOtherApplicationUsable()
    {
        var (admin, first, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, second, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id);
        var oldCard = await ApiFixture.Issue(admin, second, plan);
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id, false);

        var rejected = await admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(second.Id, plan.Id, 1, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await ApiFixture.Issue(admin, first, plan);
        var history = (await admin.GetFromJsonAsync<PagedResult<PlanDto>>(
            $"/api/admin/applications/{second.Id}/plans"))!;
        Assert.False(Assert.Single(history.Items, x => x.Id == plan.Id).IsEnabled);
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/client/licenses/activate",
            new ActivateLicense(second.Code, oldCard.Licenses[0].Key, "shared-old-card"))).StatusCode);
    }
}
~~~

- [x] **Step 2: 验证红灯**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~SharedPlanIssuanceTests`  
Expected: 新 ReportPlanDto.IsEnabled 尚不存在，随后跨应用发卡仍为 404；分别观察缺失接口与错误行为。

- [x] **Step 3: 实现唯一的应用目录投影和单向过滤链**

~~~csharp
// Application/PlanCatalogQueries.cs
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class PlanCatalogQueries(ILicensingData db)
{
    public IQueryable<PlanDto> Scoped() =>
        from reference in db.ApplicationPlans.AsNoTracking()
        join plan in db.Plans.AsNoTracking() on reference.PlanId equals plan.Id
        join application in db.Applications.AsNoTracking()
            on reference.ApplicationId equals application.Id
        select new PlanDto(plan.Id, application.Id, plan.Name,
            plan.DurationSeconds, plan.MaxDevices,
            application.DisabledAt == null && plan.IsEnabled && reference.IsEnabled,
            plan.CreatedAt, plan.MaxConcurrentSessions,
            plan.ActivationValidityDays, plan.Features);

    public IQueryable<ReportPlanDto> Catalog() =>
        Scoped().Select(x => new ReportPlanDto(x.Id, x.ApplicationId, x.Name, x.IsEnabled));
}
~~~

OperationsContracts.cs 将 ReportPlanDto 改为接口块指定的四字段 record。AccessModel 中原 LicensePlan 的过滤器替换为：

~~~csharp
b.Entity<ApplicationPlan>().HasQueryFilter(
    x => Applications.Any(a => a.Id == x.ApplicationId));
b.Entity<LicensePlan>().HasQueryFilter(
    x => AccessUnrestricted || ApplicationPlans.Any(r => r.PlanId == x.Id));
~~~

不要给引用过滤器加 IsEnabled 条件，也不要让 ApplicationPlan 过滤器再查询 Plans。

- [x] **Step 4: 实现事务内的组合验证**

~~~csharp
// Application/PlanAvailability.cs
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class PlanAvailability(ILicensingData db)
{
    public async Task<LicensePlan> Require(Guid applicationId, Guid planId,
        bool requireEnabled = true)
    {
        if (applicationId == Guid.Empty || planId == Guid.Empty)
            throw new RuleException("VALIDATION_ERROR", 400);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Plan lookup requires the caller's transaction.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Plans" WHERE "Id" = {planId} FOR SHARE""");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "PlanId" FROM "ApplicationPlans" WHERE "ApplicationId" = {applicationId} AND "PlanId" = {planId} FOR SHARE""");

        var reference = await db.ApplicationPlans.AsNoTracking().SingleOrDefaultAsync(
            x => x.ApplicationId == applicationId && x.PlanId == planId);
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId);
        if (reference is null || plan is null) throw new RuleException("NOT_FOUND", 404);
        if (requireEnabled && !plan.IsEnabled) throw new RuleException("PLAN_DISABLED");
        if (requireEnabled && !reference.IsEnabled)
            throw new RuleException("APPLICATION_PLAN_DISABLED", 409);
        return plan;
    }
}
~~~

- [x] **Step 5: 接入直接发卡、列表和报表目录**

给 LicensingService 主构造器在可选参数之前增加 `PlanAvailability planAvailability, PlanCatalogQueries planQueries`；Program 注册：

~~~csharp
builder.Services.AddScoped<PlanAvailability>();
builder.Services.AddScoped<PlanCatalogQueries>();
~~~

Issue 中保持原幂等回执检查、LockApp 和应用停用检查顺序，将原 Plans 行锁、ApplicationId 匹配和 IsEnabled 检查替换为：

~~~csharp
var plan = await planAvailability.Require(app.Id, request.PlanId);
~~~

LicensingQueries.Plans 保留 Demand 和应用可见性检查，分页主体替换为：

~~~csharp
ValidatePage(page, size);
var query = planQueries.Scoped().Where(x => x.ApplicationId == appId);
var count = await query.CountAsync();
var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
    .Skip((page - 1) * size).Take(size).ToArrayAsync();
return new PagedResult<PlanDto>(items, count, page, size);
~~~

LicensingReports.ReadCatalog 保留 RepeatableRead 事务及应用查询，把 plans 查询替换为：

~~~csharp
var plans = await planQueries.Catalog().OrderBy(x => x.Name)
    .ThenBy(x => x.ApplicationId).ThenBy(x => x.Id).ToArrayAsync();
~~~

OperationsReport 的授权事实连接和分组不变；它已有独立 ApplicationId + PlanId，不能增加一对多引用连接。

OperationsAccessTests 当前把目录卡种字段数固定为 3；接口新增明确字段后替换为以下严格断言，保留其余范围和分页断言：

~~~csharp
Assert.Equal(4, catalogPlan.EnumerateObject().Count());
Assert.True(catalogPlan.GetProperty("isEnabled").GetBoolean());
~~~

- [x] **Step 6: 运行绿灯和原范围/快照测试**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter "FullyQualifiedName~SharedPlanIssuanceTests|FullyQualifiedName~OperationsAccessTests|FullyQualifiedName~AuthorizationPolicyTests"`  
Expected: 通过；授权目录不泄漏应用，暂停引用后历史仍可见，旧卡激活不依赖当前发行开关。

- [x] **Step 7: 提交检查点**

~~~powershell
git add -- backend/src/KeyValidation.Application/PlanCatalogQueries.cs backend/src/KeyValidation.Application/PlanAvailability.cs backend/src/KeyValidation.Application/LicensingQueries.cs backend/src/KeyValidation.Application/LicensingReports.cs backend/src/KeyValidation.Application/LicensingService.cs backend/src/KeyValidation.Application/OperationsContracts.cs backend/src/KeyValidation.Infrastructure/AccessModel.cs backend/src/KeyValidation.Api/Program.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanIssuanceTests.cs backend/tests/KeyValidation.IntegrationTests/OperationsAccessTests.cs
git commit -m "feat: resolve license plans through application references"
~~~


## Task 3: 订单和渠道价格按应用与卡种组合工作

**Files:**
- Modify: `backend/src/KeyValidation.Application/OrderService.cs` 的 CreateUnderLock
- Modify: `backend/src/KeyValidation.Infrastructure/ChannelService.cs` 的 SetPrice
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanCommerceTests.cs`

**Interfaces:**
- Consumes: Task 2 的 PlanAvailability.Require；现有 SetChannelPrice、CreateOrder 和订单快照。
- Produces: SetPrice / CreateUnderLock 保持原签名，内部精确匹配 `ChannelId + ApplicationId + PlanId`。
- OrderFulfillment.Execute 的签名与快照语义保持不变。

- [x] **Step 1: 写跨应用售价及已建订单快照测试**

~~~csharp
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanCommerceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SameChannelAndPlanHaveIndependentPricesPerApplication()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var (channel, _, accountId) = await CommerceTestData.Channel(fixture, admin, a.Id);
        var channelId = CommerceTestData.Id(channel);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(
            $"/api/admin/channels/{channelId}",
            new UpdateChannel("两应用渠道", accountId, [a.Id, b.Id], true))).StatusCode);

        await CommerceTestData.Price(admin, channelId, a.Id, plan.Id, 1100);
        await CommerceTestData.Price(admin, channelId, b.Id, plan.Id, 2200);
        await CommerceTestData.Price(admin, channelId, a.Id, plan.Id, 1300);
        var customerA = await CommerceTestData.Customer(admin, a.Id, channelId);
        var customerB = await CommerceTestData.Customer(admin, b.Id, channelId);
        var orderA = await CommerceTestData.Order(admin, a.Id, plan.Id, CommerceTestData.Id(customerA));
        var orderB = await CommerceTestData.Order(admin, b.Id, plan.Id, CommerceTestData.Id(customerB));
        Assert.Equal(1300, orderA.GetProperty("unitPriceCents").GetInt64());
        Assert.Equal(2200, orderB.GetProperty("unitPriceCents").GetInt64());

        var detail = (await admin.GetFromJsonAsync<ChannelDto>(
            $"/api/admin/channels/{channelId}"))!;
        Assert.Equal(2, detail.Prices.Count(x => x.PlanId == plan.Id));
        Assert.Contains(detail.Prices, x => x.ApplicationId == a.Id && x.UnitPriceCents == 1300);
        Assert.Contains(detail.Prices, x => x.ApplicationId == b.Id && x.UnitPriceCents == 2200);
    }

    [Fact]
    public async Task ExistingOrderFulfillsItsSnapshotAfterGlobalAndReferencePause()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, b.Id);
        var order = await CommerceTestData.Order(admin, b.Id, plan.Id, CommerceTestData.Id(customer));
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id, false);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(
            $"/api/admin/plans/{plan.Id}",
            new UpdatePlan("新规则", 3600, 2, false, 2, 5, ["new"]))).StatusCode);

        var fulfilled = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order));
        Assert.Equal("fulfilled", fulfilled.GetProperty("status").GetString());
        Assert.Equal(plan.DurationSeconds,
            fulfilled.GetProperty("snapshot").GetProperty("durationSeconds").GetInt64());
        Assert.Equal(plan.Name, fulfilled.GetProperty("snapshot").GetProperty("planName").GetString());
    }
}
~~~

现有 `OrderSnapshotDto.PlanName` 序列化为 `snapshot.planName`，测试直接使用该契约。

- [x] **Step 2: 验证红灯**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~SharedPlanCommerceTests`  
Expected: 第二应用设置价格或创建订单被现有单应用归属判断拒绝。

- [x] **Step 3: 接入共享验证和三字段查价**

给 OrderService 和 ChannelService 主构造器增加 `PlanAvailability planAvailability`。OrderService.CreateUnderLock 中，原 Plans 查询替换为：

~~~csharp
var plan = await planAvailability.Require(request.ApplicationId, request.PlanId);
var price = request.UnitPriceCents;
if (customer.ChannelId is { } channelId)
    price = await db.ChannelPrices.Where(x =>
        x.ChannelId == channelId &&
        x.ApplicationId == request.ApplicationId &&
        x.PlanId == plan.Id)
        .Select(x => (long?)x.UnitPriceCents).SingleOrDefaultAsync()
        ?? throw new RuleException("CHANNEL_PRICE_NOT_CONFIGURED", 409);
~~~

保留后续续期时长检查、授权锁、冻结检查及全部订单快照赋值。Create / Fulfill 中幂等回执处理顺序不变。

ChannelService.SetPrice 在应用锁和渠道锁之后，用以下片段替换原卡种验证和价格查找：

~~~csharp
await planAvailability.Require(request.ApplicationId, request.PlanId, requireEnabled: false);
await AuthorizationLock();
var channel = await Find(id);
if (!channel.ApplicationIds.Contains(request.ApplicationId))
    throw new RuleException("NOT_FOUND", 404);
var price = await db.ChannelPrices.SingleOrDefaultAsync(x =>
    x.ChannelId == id && x.ApplicationId == request.ApplicationId &&
    x.PlanId == request.PlanId);
var before = price?.UnitPriceCents;
if (price is null)
{
    price = new ChannelPrice
    {
        ChannelId = id, ApplicationId = request.ApplicationId, PlanId = request.PlanId
    };
    db.ChannelPrices.Add(price);
}
price.UnitPriceCents = request.UnitPriceCents;
~~~

保留现有审计、SaveChanges、Commit 和返回 DTO。PortalRequestService 的续期审批已经调用 CreateUnderLock，继续走这一入口；开放 API 继续使用同一个 OrderService。

- [x] **Step 4: 运行绿灯和原商贸回归**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter "FullyQualifiedName~SharedPlanCommerceTests|FullyQualifiedName~CommerceChannelTests|FullyQualifiedName~CommerceBoundaryTests|FullyQualifiedName~CommercePortalApiTests"`  
Expected: 同一卡种两应用价格独立；已创建订单履约不受新发行开关影响；额度和重试不重复扣减。

- [x] **Step 5: 提交检查点**

~~~powershell
git add -- backend/src/KeyValidation.Application/OrderService.cs backend/src/KeyValidation.Infrastructure/ChannelService.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanCommerceTests.cs
git commit -m "feat: scope shared plan pricing and orders by application"
~~~

## Task 4: 独立管理服务、接口和最终全局模型

**Files:**
- Create: `backend/src/KeyValidation.Domain/LicensePlan.cs`
- Create: `backend/src/KeyValidation.Application/PlanContracts.cs`、`PlanCatalogService.cs`
- Create: `backend/src/KeyValidation.Api/PlanEndpoints.cs`
- Create: `backend/src/KeyValidation.Infrastructure/Migrations/20261009101000_GlobalizePlans.cs`、`20261009101000_GlobalizePlans.Designer.cs`
- Modify: `backend/src/KeyValidation.Domain/Records.cs`
- Modify: `backend/src/KeyValidation.Application/ApplicationAccessScope.cs`、`LicensingService.cs`、`LicensingQueries.cs`
- Modify: `backend/src/KeyValidation.Infrastructure/AppDbContext.cs`、`AccessModel.cs`、`AuditDisplay.cs`、`Migrations/AppDbContextModelSnapshot.cs`
- Modify: `backend/src/KeyValidation.Api/Endpoints.cs`、`Program.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanCatalogTests.cs`

**Interfaces:**
- Consumes: CreatePlan / UpdatePlan、PlanCatalogQueries、ApplicationAccessScope、ILicensingData、TimeProvider。
- Produces: 下述 PlanContracts 中全部 record。
- Produces: `ApplicationAccessScope.RequireGlobalPlanManagement(bool write = false): void`。
- Produces: `PlanCatalogService.List(int page, int size, string? search, bool? isEnabled): Task<PagedResult<GlobalPlanDto>>`；`Get(Guid): Task<GlobalPlanDto>`。
- Produces: `Create(CreatePlan, Actor): Task<GlobalPlanDto>`；`Update(Guid, UpdatePlan, Actor): Task<GlobalPlanDto>`。
- Produces: `CreateForApplication(Guid, CreatePlan, Actor): Task<PlanDto>`。
- Produces: `References(Guid applicationId, int page, int size): Task<PagedResult<ApplicationPlanDto>>`。
- Produces: `Applications(Guid planId): Task<IReadOnlyList<PlanReferenceDto>>`。
- Produces: `SetReference(Guid applicationId, Guid planId, SetPlanReference, Actor): Task<ApplicationPlanDto>`。
- 所有管理写方法拥有自己的事务；后续任务不可在外层事务中嵌套调用。

- [x] **Step 1: 写独立创建、跨应用引用和重复 PUT 测试**

~~~csharp
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanCatalogTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GlobalPlanCanExistUnboundAndBeReferencedWithoutCopying()
    {
        var (admin, a, _) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        var response = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("共享月卡", 2592000, 2, 1, 30, ["export"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        Assert.Equal(0, plan.ReferenceCount);
        foreach (var app in new[] { a, b })
        {
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
                    $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
                    new SetPlanReference(true))).StatusCode);
            var page = (await admin.GetFromJsonAsync<PagedResult<PlanDto>>(
                $"/api/admin/applications/{app.Id}/plans"))!;
            var option = Assert.Single(page.Items, x => x.Id == plan.Id);
            Assert.Equal(app.Id, option.ApplicationId);
            await ApiFixture.Issue(admin, app, option);
        }
        var saved = (await admin.GetFromJsonAsync<GlobalPlanDto>(
            $"/api/admin/plans/{plan.Id}"))!;
        Assert.Equal(2, saved.ReferenceCount);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Plans.CountAsync(x => x.Id == plan.Id));
        Assert.Equal(2, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(
            x => x.TargetId == plan.Id && x.Action == "application-plan.bind"));
    }

    [Fact]
    public async Task ReferenceRequiresExplicitStateAndGlobalUpdatesReturnGlobalDto()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}", new { })).StatusCode);
        var response = await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}",
            new UpdatePlan("全局日卡", 86400, 3, true, 2, null, ["basic"]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        Assert.Equal(3, saved.MaxDevices);
        Assert.Equal(2, saved.MaxConcurrentSessions);
        Assert.Equal(1, saved.ReferenceCount);
    }
}
~~~

- [x] **Step 2: 验证红灯**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~SharedPlanCatalogTests`  
Expected: 新 DTO 不存在；补齐 DTO 后新的全局创建及引用路由为 404。

- [x] **Step 3: 定义契约、实体边界与全局写门禁**

PlanContracts.cs：

~~~csharp
using System.Text.Json.Serialization;

namespace KeyValidation.Application;

public record GlobalPlanDto(Guid Id, string Name, long DurationSeconds,
    int MaxDevices, bool IsEnabled, DateTimeOffset CreatedAt,
    int MaxConcurrentSessions, int? ActivationValidityDays,
    string[] Features, int ReferenceCount);

public record ApplicationPlanDto(Guid ApplicationId, Guid PlanId, string Name,
    long DurationSeconds, int MaxDevices, int MaxConcurrentSessions,
    int? ActivationValidityDays, string[] Features, bool PlanEnabled,
    bool IsEnabled, bool CanIssue, DateTimeOffset CreatedAt);

public record PlanReferenceDto(Guid ApplicationId, string ApplicationName,
    bool IsEnabled, DateTimeOffset CreatedAt);

public record SetPlanReference([property: JsonRequired] bool IsEnabled);
~~~

从 Records.cs 原样移出 LicensePlan 的其余字段到 LicensePlan.cs，仅删除 ApplicationId 属性。AppDbContext 的 Plans 配置删除应用外键和 `ApplicationId + CreatedAt` 索引，增加 `CreatedAt + Id` 索引；名称长度、现有 check constraint 及 AuthorizationPolicyModel 配置保留。

ApplicationAccessScope 增加：

同文件的 ReadLicenses 权限说明统一用“卡种”，对应目录项为：

~~~csharp
new(ReadLicenses, "查看授权", "授权管理", "查看应用、卡种、卡密和设备授权信息。"),
~~~

在 ApplicationAccessScope 类体加入范围门禁：

~~~csharp
public void RequireGlobalPlanManagement(bool write = false)
{
    if (write) RequireWrite(ApplicationPermissions.Administrator);
    else Require(ApplicationPermissions.Administrator);
    if (IsAdministrativeRequest &&
        (!AllApplications || ChannelId != null || CustomerId != null))
        throw new RuleException("FORBIDDEN", 403);
}
~~~

在 AccessModel.WritePermissions 取出 entries 后，现有提前返回之前加入：

~~~csharp
if (entries.Any(x => x.Entity is LicensePlan or ApplicationPlan))
{
    access!.RequireGlobalPlanManagement(write: true);
    return [ApplicationPermissions.Administrator];
}
~~~

ValidateWriteApplications 的实体分支删除 `LicensePlan x => x.ApplicationId`，增加 `ApplicationPlan x => x.ApplicationId`；全局实体已经由上述门禁保护。原有其他实体检查保留。

- [x] **Step 4: 实现管理读模型与基础方法**

在 PlanCatalogService.cs 建立下面的类；步骤 5–6 的方法加入同一个类体：

~~~csharp
using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class PlanCatalogService(ILicensingData db, ApplicationAccessScope access,
    TimeProvider clock, PlanCatalogQueries queries)
{
    private DateTimeOffset Now
    {
        get
        {
            var now = clock.GetUtcNow();
            return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        }
    }

    private static void ValidatePage(int page, int size)
    {
        if (page is < 1 or > 1000000 || size is < 1 or > 100)
            throw new RuleException("VALIDATION_ERROR", 400);
    }

    private IQueryable<GlobalPlanDto> Global() => db.Plans.AsNoTracking()
        .Select(p => new GlobalPlanDto(p.Id, p.Name, p.DurationSeconds,
            p.MaxDevices, p.IsEnabled, p.CreatedAt, p.MaxConcurrentSessions,
            p.ActivationValidityDays, p.Features,
            db.ApplicationPlans.Count(r => r.PlanId == p.Id)));

    private IQueryable<ApplicationPlanDto> ReferenceRows() =>
        from r in db.ApplicationPlans.AsNoTracking()
        join p in db.Plans.AsNoTracking() on r.PlanId equals p.Id
        join a in db.Applications.AsNoTracking() on r.ApplicationId equals a.Id
        select new ApplicationPlanDto(a.Id, p.Id, p.Name, p.DurationSeconds,
            p.MaxDevices, p.MaxConcurrentSessions, p.ActivationValidityDays,
            p.Features, p.IsEnabled, r.IsEnabled,
            a.DisabledAt == null && p.IsEnabled && r.IsEnabled, r.CreatedAt);

    public async Task<PagedResult<GlobalPlanDto>> List(int page, int size,
        string? search, bool? isEnabled)
    {
        access.RequireGlobalPlanManagement();
        ValidatePage(page, size);
        search = search?.Trim();
        if (search?.Length > 120) throw new RuleException("VALIDATION_ERROR", 400);
        var q = Global();
        if (!string.IsNullOrEmpty(search)) q = q.Where(x => x.Name.Contains(search));
        if (isEnabled is { } state) q = q.Where(x => x.IsEnabled == state);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToArrayAsync();
        return new(items, total, page, size);
    }

    public async Task<GlobalPlanDto> Get(Guid id)
    {
        access.RequireGlobalPlanManagement();
        return await Global().SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RuleException("NOT_FOUND", 404);
    }

    public async Task<PagedResult<ApplicationPlanDto>> References(
        Guid applicationId, int page, int size)
    {
        access.Require(ApplicationPermissions.ReadLicenses);
        ValidatePage(page, size);
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId))
            throw new RuleException("NOT_FOUND", 404);
        var q = ReferenceRows().Where(x => x.ApplicationId == applicationId);
        var total = await q.CountAsync();
        var items = await q.OrderBy(x => x.Name).ThenBy(x => x.PlanId)
            .Skip((page - 1) * size).Take(size).ToArrayAsync();
        return new(items, total, page, size);
    }

    public async Task<IReadOnlyList<PlanReferenceDto>> Applications(Guid planId)
    {
        await Get(planId);
        return await (from r in db.ApplicationPlans.AsNoTracking()
            join a in db.Applications.AsNoTracking() on r.ApplicationId equals a.Id
            where r.PlanId == planId
            orderby a.Name, a.Id
            select new PlanReferenceDto(a.Id, a.Name, r.IsEnabled, r.CreatedAt)).ToArrayAsync();
    }

    private static (string Name, int Sessions, string[] Features) Normalize(CreatePlan request)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 120 } ||
            request.DurationSeconds is < 1 or > 315360000 ||
            request.MaxDevices is < 1 or > 100)
            throw new RuleException("VALIDATION_ERROR", 400);
        var sessions = request.MaxConcurrentSessions ?? request.MaxDevices;
        AuthorizationPolicies.Validate(sessions, request.ActivationValidityDays);
        return (name, sessions, AuthorizationPolicies.NormalizeFeatures(request.Features));
    }

    private LicensePlan NewPlan(CreatePlan request)
    {
        var values = Normalize(request);
        var plan = new LicensePlan
        {
            Name = values.Name, DurationSeconds = request.DurationSeconds,
            MaxDevices = request.MaxDevices, MaxConcurrentSessions = values.Sessions,
            ActivationValidityDays = request.ActivationValidityDays,
            Features = values.Features, IsEnabled = true, CreatedAt = Now
        };
        db.Plans.Add(plan);
        return plan;
    }

    private static object Facts(LicensePlan plan) => new
    {
        name = plan.Name, durationSeconds = plan.DurationSeconds,
        maxDevices = plan.MaxDevices, maxConcurrentSessions = plan.MaxConcurrentSessions,
        activationValidityDays = plan.ActivationValidityDays,
        features = plan.Features.ToArray(), isEnabled = plan.IsEnabled
    };

    private void Audit(Actor actor, string action, LicensePlan plan,
        Guid? applicationId, object? before, object after)
    {
        db.AuditLogs.Add(new AdminAuditLog
        {
            ActorId = actor.Id, Action = action, TargetId = plan.Id,
            ApplicationId = applicationId, TargetType = "plan", TargetName = plan.Name,
            Changes = JsonSerializer.Serialize(new { before, after }),
            CreatedAt = Now, TraceId = actor.TraceId
        });
    }

    private async Task LockApplication(Guid id)
    {
        if (id == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Applications" WHERE "Id" = {id} FOR SHARE""");
        if (!await db.Applications.AnyAsync(x => x.Id == id))
            throw new RuleException("NOT_FOUND", 404);
    }
}
~~~

- [x] **Step 5: 实现全局创建/更新与旧创建入口**

以下方法加入 PlanCatalogService：

~~~csharp
public async Task<GlobalPlanDto> Create(CreatePlan request, Actor actor)
{
    access.RequireGlobalPlanManagement(write: true);
    await using var tx = await db.Database.BeginTransactionAsync();
    var plan = NewPlan(request);
    Audit(actor, "plan.create", plan, null, null, Facts(plan));
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return await Get(plan.Id);
}

public async Task<GlobalPlanDto> Update(Guid id, UpdatePlan request, Actor actor)
{
    access.RequireGlobalPlanManagement(write: true);
    if (id == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
    var values = Normalize(new CreatePlan(request.Name, request.DurationSeconds,
        request.MaxDevices, request.MaxConcurrentSessions,
        request.ActivationValidityDays, request.Features));
    await using var tx = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT "Id" FROM "Plans" WHERE "Id" = {id} FOR UPDATE""");
    var plan = await db.Plans.SingleOrDefaultAsync(x => x.Id == id)
        ?? throw new RuleException("NOT_FOUND", 404);
    var before = Facts(plan);
    plan.Name = values.Name;
    plan.DurationSeconds = request.DurationSeconds;
    plan.MaxDevices = request.MaxDevices;
    plan.MaxConcurrentSessions = values.Sessions;
    plan.ActivationValidityDays = request.ActivationValidityDays;
    plan.Features = values.Features;
    plan.IsEnabled = request.IsEnabled;
    Audit(actor, "plan.update", plan, null, before, Facts(plan));
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return await Get(id);
}

public async Task<PlanDto> CreateForApplication(Guid applicationId,
    CreatePlan request, Actor actor)
{
    access.RequireGlobalPlanManagement(write: true);
    await using var tx = await db.Database.BeginTransactionAsync();
    await LockApplication(applicationId);
    var plan = NewPlan(request);
    db.ApplicationPlans.Add(new ApplicationPlan
    {
        ApplicationId = applicationId, PlanId = plan.Id,
        IsEnabled = true, CreatedAt = plan.CreatedAt
    });
    Audit(actor, "plan.create", plan, null, null, Facts(plan));
    Audit(actor, "application-plan.bind", plan, applicationId, null, new { isEnabled = true });
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return await queries.Scoped().SingleAsync(
        x => x.ApplicationId == applicationId && x.Id == plan.Id);
}
~~~

全局修改不获取应用锁，防止卡种 → 应用的反向锁顺序。创建兼容入口原子完成，不调用两个分别提交的公开方法。

- [x] **Step 6: 实现并发安全、幂等的引用 PUT**

以下方法加入 PlanCatalogService：

~~~csharp
public async Task<ApplicationPlanDto> SetReference(Guid applicationId, Guid planId,
    SetPlanReference request, Actor actor)
{
    access.RequireGlobalPlanManagement(write: true);
    if (planId == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
    await using var tx = await db.Database.BeginTransactionAsync();
    await LockApplication(applicationId);
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT "Id" FROM "Plans" WHERE "Id" = {planId} FOR SHARE""");
    var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId)
        ?? throw new RuleException("NOT_FOUND", 404);
    var now = Now;
    var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "ApplicationPlans" ("ApplicationId", "PlanId", "IsEnabled", "CreatedAt")
        VALUES ({applicationId}, {planId}, {request.IsEnabled}, {now})
        ON CONFLICT ("ApplicationId", "PlanId") DO NOTHING
        """);
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT "PlanId" FROM "ApplicationPlans" WHERE "ApplicationId" = {applicationId} AND "PlanId" = {planId} FOR UPDATE""");
    var reference = await db.ApplicationPlans.SingleAsync(
        x => x.ApplicationId == applicationId && x.PlanId == planId);
    var before = reference.IsEnabled;
    reference.IsEnabled = request.IsEnabled;
    if (inserted == 1)
        Audit(actor, "application-plan.bind", plan, applicationId, null,
            new { isEnabled = request.IsEnabled });
    else if (before != request.IsEnabled)
        Audit(actor, "application-plan.update", plan, applicationId,
            new { isEnabled = before }, new { isEnabled = request.IsEnabled });
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return await ReferenceRows().SingleAsync(
        x => x.ApplicationId == applicationId && x.PlanId == planId);
}
~~~

没有变更的重复 PUT 也经过 SaveChanges 的 RequiredWritePermissions 复核；不能在事务前返回而跳过账号失效检查。


- [x] **Step 7: 调整审计归属和名称解析**

AccessModel.AssignAuditApplications 删除对 `plan.` 操作推断单个 ApplicationId 的分支；保留旧已存日志字段，新增全局日志 ApplicationId 为空。引用日志已由服务写入正确应用。

AuditDisplay.AuditActions.TargetType 将 plan 分支改为：

~~~csharp
"plan" or "application-plan" => "plan",
~~~

AuditLabels.Load 的跟踪卡种补全不再访问旧 ApplicationId：

~~~csharp
if (includeTracked)
{
    foreach (var row in db.Plans.Local.Where(x => ids.Contains(x.Id) &&
        (result.GlobalTargets || result.Targets.ContainsKey(("plan", x.Id)))))
        result.Targets[("plan", row.Id)] = row.Name;
}
~~~

先执行现有可见 Plans 查询，再执行这段补全，不能把受限账号未查询到的全局卡种名称带入日志。

- [x] **Step 8: 拆出 HTTP 路由并接入容器**

PlanEndpoints.cs：

~~~csharp
using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

public static class PlanEndpoints
{
    private static Actor ActorOf(HttpContext c) => new(
        c.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new RuleException("UNAUTHENTICATED", 401),
        c.TraceIdentifier);

    public static void MapPlanApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Plan catalog")
            .RequireAuthorization("Read");
        admin.MapGet("/plans", (PlanCatalogService s, int page = 1,
            int pageSize = 20, string? search = null, bool? isEnabled = null) =>
            s.List(page, pageSize, search, isEnabled)).RequireAuthorization("Admin");
        admin.MapGet("/plans/{id:guid}", (Guid id, PlanCatalogService s) =>
            s.Get(id)).RequireAuthorization("Admin");
        admin.MapPost("/plans", async (CreatePlan request, HttpContext c, PlanCatalogService s) =>
        {
            var result = await s.Create(request, ActorOf(c));
            return TypedResults.Created($"/api/admin/plans/{result.Id}", result);
        }).RequireAuthorization("Admin");
        admin.MapPatch("/plans/{id:guid}", (Guid id, UpdatePlan request,
            HttpContext c, PlanCatalogService s) =>
            s.Update(id, request, ActorOf(c))).RequireAuthorization("Admin");
        admin.MapGet("/plans/{id:guid}/applications", (Guid id, PlanCatalogService s) =>
            s.Applications(id)).RequireAuthorization("Admin");
        admin.MapGet("/applications/{id:guid}/plan-references",
            (Guid id, PlanCatalogService s, int page = 1, int pageSize = 20) =>
            s.References(id, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPut("/applications/{id:guid}/plan-references/{planId:guid}",
            (Guid id, Guid planId, SetPlanReference request, HttpContext c, PlanCatalogService s) =>
            s.SetReference(id, planId, request, ActorOf(c))).RequireAuthorization("Admin");

        admin.MapGet("/applications/{id:guid}/plans",
            (Guid id, LicensingService s, int page = 1, int pageSize = 20) =>
            s.Plans(id, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/applications/{id:guid}/plans",
            async (Guid id, CreatePlan request, HttpContext c, PlanCatalogService s) =>
            {
                var result = await s.CreateForApplication(id, request, ActorOf(c));
                return TypedResults.Created($"/api/admin/plans/{result.Id}", result);
            }).RequireAuthorization("Admin");
    }
}
~~~

从 Endpoints.cs 移除这三个原路由，防止重复匹配：GET/POST `/applications/{id:guid}/plans`、PATCH `/plans/{id:guid}`。LicensingService 删除 CreatePlan、UpdatePlan、PlanRules、PlanAuditFacts；LicensingQueries 删除 PlanDtoOf。其余方法不移动。

Program 增加：

~~~csharp
builder.Services.AddScoped<PlanCatalogService>();
// 放在其他 Map*Api 调用旁：
app.MapPlanApi();
~~~

- [x] **Step 9: 生成最终迁移并实现受控降级**

~~~powershell
dotnet ef migrations add GlobalizePlans --project backend/src/KeyValidation.Infrastructure --startup-project backend/src/KeyValidation.Api
$migrationDirectory = Join-Path (Get-Location) 'backend/src/KeyValidation.Infrastructure/Migrations'
$generated = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*_GlobalizePlans.cs')
if ($generated.Count -ne 1) { throw 'Expected one generated migration.' }
$generatedId = [IO.Path]::GetFileNameWithoutExtension($generated[0].Name)
$plannedId = '20261009101000_GlobalizePlans'
if ($generatedId -ne $plannedId) {
    Rename-Item -LiteralPath $generated[0].FullName -NewName ($plannedId + '.cs')
    $designerSource = Join-Path $migrationDirectory ($generatedId + '.Designer.cs')
    $designerTarget = Join-Path $migrationDirectory ($plannedId + '.Designer.cs')
    Rename-Item -LiteralPath $designerSource -NewName ($plannedId + '.Designer.cs')
    $designerContent = Get-Content -LiteralPath $designerTarget -Raw
    Set-Content -LiteralPath $designerTarget -Value $designerContent.Replace($generatedId, $plannedId) -Encoding utf8
}
~~~

Up 应只移除旧 Plans 应用外键、旧索引和 ApplicationId 列，并建立全局排序索引；不能删除或重新生成 Plans 数据。

Down 第一条语句必须先检查可否还原：

~~~csharp
migrationBuilder.Sql("""
    DO $$
    BEGIN
        IF EXISTS (
            SELECT p."Id"
            FROM "Plans" p LEFT JOIN "ApplicationPlans" r ON r."PlanId" = p."Id"
            GROUP BY p."Id" HAVING COUNT(r."PlanId") <> 1
        ) THEN
            RAISE EXCEPTION 'SHARED_PLAN_DOWNGRADE_UNSUPPORTED';
        END IF;
    END $$;
    """);
~~~

将生成 Down 的 AddColumn 改为先可空，填值，再改非空：

~~~csharp
migrationBuilder.AddColumn<Guid>(
    name: "ApplicationId", table: "Plans", type: "uuid", nullable: true);
migrationBuilder.Sql("""
    UPDATE "Plans" p
    SET "ApplicationId" = r."ApplicationId",
        "IsEnabled" = p."IsEnabled" AND r."IsEnabled"
    FROM "ApplicationPlans" r WHERE r."PlanId" = p."Id";
    """);
migrationBuilder.AlterColumn<Guid>(
    name: "ApplicationId", table: "Plans", type: "uuid",
    nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
~~~

其后执行生成的旧索引与应用外键恢复；删除全局索引也必须放在门禁之后。默认迁移事务保证失败完整回滚。

- [x] **Step 10: 验证管理流程、原流程与 EF 模型**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter "FullyQualifiedName~SharedPlanCatalogTests|FullyQualifiedName~SharedPlanMigrationTests|FullyQualifiedName~SharedPlanIssuanceTests|FullyQualifiedName~SharedPlanCommerceTests|FullyQualifiedName~ReadableAuditTests"`  
Expected: 通过；独立卡种可以零引用，重复 PUT 不复制，跨应用发卡正常，旧单引用数据仍能完成旧迁移测试。

Run: `rg -n 'plan\.ApplicationId|p\.ApplicationId|LicensePlan x =>|PlanDtoOf' backend/src -g '*.cs' -g '!**/Migrations/**' -g '!**/obj/**' -g '!**/bin/**'`  
Expected: 没有 LicensePlan 单应用归属引用；同名局部变量若实际代表其他实体，按其类型判断，不批量替换。

- [x] **Step 11: 提交检查点**

~~~powershell
git add -- backend/src/KeyValidation.Domain/LicensePlan.cs backend/src/KeyValidation.Domain/Records.cs backend/src/KeyValidation.Application/PlanContracts.cs backend/src/KeyValidation.Application/PlanCatalogService.cs backend/src/KeyValidation.Application/ApplicationAccessScope.cs backend/src/KeyValidation.Application/LicensingService.cs backend/src/KeyValidation.Application/LicensingQueries.cs backend/src/KeyValidation.Infrastructure/AppDbContext.cs backend/src/KeyValidation.Infrastructure/AccessModel.cs backend/src/KeyValidation.Infrastructure/AuditDisplay.cs backend/src/KeyValidation.Infrastructure/Migrations/20261009101000_GlobalizePlans.cs backend/src/KeyValidation.Infrastructure/Migrations/20261009101000_GlobalizePlans.Designer.cs backend/src/KeyValidation.Infrastructure/Migrations/AppDbContextModelSnapshot.cs backend/src/KeyValidation.Api/PlanEndpoints.cs backend/src/KeyValidation.Api/Endpoints.cs backend/src/KeyValidation.Api/Program.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanCatalogTests.cs
git commit -m "feat: expose global plan management and application references"
~~~

## Task 5: 验证权限、并发暂停和降级边界

**Files:**
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanAccessTests.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanConcurrencyTests.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanDowngradeTests.cs`
- Test: `backend/tests/KeyValidation.IntegrationTests/SharedPlanOpenApiTests.cs`
- Modify only if a new test exposes a defect: `PlanCatalogService.cs`、`PlanAvailability.cs`、`AccessModel.cs`、`PlanEndpoints.cs`、`GlobalizePlans`。
- Existing regression: `OperationsAccessTests.cs`、`ReadableAuditTests.cs`、`CommerceOpenApiTests.cs`。

**Interfaces:**
- Consumes: Task 4 完整 HTTP 契约和 RequireGlobalPlanManagement；现有 UserManager、UpdateAccountAccess、ApiFixture。
- Produces: 全局与应用范围隔离、提交边界、降级门禁及 OpenAPI 的可运行验收证据。没有新增生产接口。

- [x] **Step 1: 写受限管理员和数据库绕过测试**

SharedPlanAccessTests.cs：

~~~csharp
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanAccessTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ScopedPlatformManagerCanUseVisibleReferenceButCannotEditGlobalPlan()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        string id;
        var name = "plan-scope-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = new AdminUser { UserName = name };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded);
            id = user.Id;
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
            $"/api/admin/accounts/{id}/access",
            new UpdateAccountAccess(
                ["platform.manage", "licenses.read", "licenses.issue"], false, [b.Id]))).StatusCode);
        using var actor = await fixture.Login(name);
        var options = (await actor.GetFromJsonAsync<PagedResult<PlanDto>>(
            $"/api/admin/applications/{b.Id}/plans"))!;
        var shared = Assert.Single(options.Items, x => x.Id == plan.Id);
        await ApiFixture.Issue(actor, b, shared);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync(
            $"/api/admin/applications/{a.Id}/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PatchAsJsonAsync(
            $"/api/admin/plans/{plan.Id}", new UpdatePlan("越权", 3600, 1, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PutAsJsonAsync(
            $"/api/admin/applications/{b.Id}/plan-references/{plan.Id}",
            new SetPlanReference(false))).StatusCode);

        using var writeScope = fixture.Services.CreateScope();
        var access = writeScope.ServiceProvider.GetRequiredService<ApplicationAccessScope>();
        access.Initialize(new AccountAccessDto(["platform.manage"], false, [b.Id], null),
            true, (_, _) => Task.CompletedTask);
        var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Plans.SingleAsync(x => x.Id == plan.Id)).Name = "直接绕过";
        await Assert.ThrowsAsync<RuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OrderOnlyApiCredentialReceivesOnlyItsApplicationPlanPair()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var response = await admin.PostAsJsonAsync("/api/admin/api-credentials",
            new CreateApiCredential(a.Id, "shared-order-api", ["orders.manage"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var credential = (await response.Content.ReadFromJsonAsync<ApiCredentialSecretDto>())!;
        using var api = fixture.CreateClient();
        api.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Secret);
        var catalog = (await api.GetFromJsonAsync<ReportsCatalogDto>("/api/v1/catalog"))!;
        Assert.DoesNotContain(catalog.Applications, x => x.Id == b.Id);
        var option = Assert.Single(catalog.Plans, x => x.Id == plan.Id);
        Assert.Equal(a.Id, option.ApplicationId);
        Assert.True(option.IsEnabled);
    }


    [Fact]
    public async Task NewWritesKeepCsrfProtection()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            "/api/admin/plans", new CreatePlan("无 CSRF", 3600))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
            new SetPlanReference(false))).StatusCode);
    }
}
~~~

不要为了通过测试向 scoped 账号添加全部应用范围；服务与 SaveChanges 必须分别拒绝全局修改。

- [x] **Step 2: 写有确定锁等待点的暂停测试及并发引用测试**

SharedPlanConcurrencyTests.cs：

~~~csharp
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanConcurrencyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task IssuanceWaitingOnReferenceLockSeesCommittedPause()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "PlanId" FROM "ApplicationPlans" WHERE "ApplicationId" = {app.Id} AND "PlanId" = {plan.Id} FOR UPDATE""");
        var pending = admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()));
        var waiting = 0;
        for (var attempt = 0; attempt < 100 && waiting == 0; attempt++)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                AND query LIKE '%ApplicationPlans%'
                """).SingleAsync();
            if (waiting == 0) await Task.Delay(20);
        }
        Assert.True(waiting > 0, "Expected issuance to wait on the application reference lock.");
        var reference = await db.ApplicationPlans.SingleAsync(
            x => x.ApplicationId == app.Id && x.PlanId == plan.Id);
        reference.IsEnabled = false;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Equal(0, await db.Batches.CountAsync(x => x.ApplicationId == app.Id));
    }

    [Fact]
    public async Task ConcurrentReferencePutsCreateOneRowAndOneBindAudit()
    {
        var (admin, app, _) = await fixture.Catalog();
        using var cleanup = admin;
        var created = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("并发引用", 3600));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = (await created.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            admin.PutAsJsonAsync(
                $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
                new SetPlanReference(true))));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(
            x => x.TargetId == plan.Id && x.Action == "application-plan.bind"));
    }
}
~~~

- [x] **Step 3: 写降级拒绝及契约测试**

SharedPlanDowngradeTests.cs：

~~~csharp
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanDowngradeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UnboundPlanRejectsDowngradeBeforeSchemaChanges()
    {
        using var admin = await fixture.Login();
        var created = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("无应用卡种", 3600));
        created.EnsureSuccessStatusCode();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261009100000_AddApplicationPlanReferences"));
        Assert.Contains("SHARED_PLAN_DOWNGRADE_UNSUPPORTED", error.MessageText);
        Assert.Contains("20261009101000_GlobalizePlans",
            await db.Database.GetAppliedMigrationsAsync());
        var columns = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Plans'
                AND column_name = 'ApplicationId'
            """).SingleAsync();
        Assert.Equal(0, columns);
    }
}

public sealed class SharedPlanMultiReferenceDowngradeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SharedPlanRejectsDowngradeWithoutDroppingEitherReference()
    {
        var (admin, a, _) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        var response = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("两应用共享", 3600));
        response.EnsureSuccessStatusCode();
        var plan = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        await SharedPlanTestData.Reference(fixture, a.Id, plan.Id);
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261009100000_AddApplicationPlanReferences"));
        Assert.Contains("SHARED_PLAN_DOWNGRADE_UNSUPPORTED", error.MessageText);
        Assert.Equal(2, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
        Assert.Contains("20261009101000_GlobalizePlans",
            await db.Database.GetAppliedMigrationsAsync());
    }
}

~~~

SharedPlanOpenApiTests.cs：

~~~csharp
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanOpenApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ContractSeparatesGlobalPlansFromApplicationOptions()
    {
        using var client = fixture.CreateClient();
        var api = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = api.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/admin/plans").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/admin/applications/{id}/plan-references/{planId}")
            .TryGetProperty("put", out _));
        var schemas = api.GetProperty("components").GetProperty("schemas");
        var global = schemas.GetProperty("GlobalPlanDto").GetProperty("properties");
        Assert.False(global.TryGetProperty("applicationId", out _));
        Assert.True(global.TryGetProperty("referenceCount", out _));
        Assert.True(schemas.GetProperty("ReportPlanDto").GetProperty("properties")
            .TryGetProperty("isEnabled", out _));
    }
}
~~~

- [x] **Step 4: 运行边界验证，并仅修复实际失败**

Run: `dotnet test backend/tests/KeyValidation.IntegrationTests --filter "FullyQualifiedName~SharedPlan|FullyQualifiedName~OperationsAccess|FullyQualifiedName~ReadableAudit|FullyQualifiedName~CommerceOpenApi"`  
Expected: 全部通过。若测试先因环境失败，修复环境后重跑；不能把超时或缺失数据库当实现红灯。实际代码修复遵循任务 2–4 的锁序、范围门禁和事务接口。

原审计测试继续要求批次/订单名称快照、操作者和业务归属准确；只有新全局 plan.create/update 日志的应用归属应为空，application-plan.* 日志应有应用。不得统一清空其他审计 ApplicationId。

- [x] **Step 5: 提交边界覆盖**

~~~powershell
git add -- backend/tests/KeyValidation.IntegrationTests/SharedPlanAccessTests.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanConcurrencyTests.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanDowngradeTests.cs backend/tests/KeyValidation.IntegrationTests/SharedPlanOpenApiTests.cs
git commit -m "test: cover shared plan access concurrency and migration boundaries"
~~~


## Task 6: 独立卡种页面和前端公共查询模块

**Files:**
- Create: `frontend/src/features/plans/types.ts`、`queries.ts`、`options.ts`
- Create: `frontend/src/features/plans/PlansPage.tsx`、`PlanEditor.tsx`、`PlanApplicationsDrawer.tsx`
- Modify: `frontend/src/App.tsx`、`frontend/src/features/workspace/Workspace.tsx`
- Modify: `frontend/src/lib/schema.d.ts`、`frontend/src/lib/api.ts`、`frontend/src/components/common.tsx`
- Test: `frontend/e2e/shared-plans.spec.ts`

**Interfaces:**
- Consumes: Task 4 的全局与引用接口，现有 api / queryString、useFormSnapshot、LiveModal、FeatureField。
- Produces types: GlobalPlan、ApplicationPlanReference、PlanReference、PlanUpdate（全部从生成 schema 导出）。
- Produces queries: useGlobalPlans、useAllGlobalPlans、useApplicationPlans、useApplicationPlanReferences、usePlanApplications、usePlanCatalog。
- Produces mutations: savePlan、setPlanReference、invalidatePlanQueries；精确签名见下面代码。
- Produces UI: `PlansPage(): ReactElement`；`PlanEditor({ open, plan, close })`；`PlanApplicationsDrawer({ plan, close })`。

- [x] **Step 1: 写真实浏览器创建/编辑测试**

shared-plans.spec.ts：

~~~typescript
import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test.use({ trace: 'off', screenshot: 'off' });

async function loginSharedPlans(page: Page) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const csrf = await (await page.request.get('/api/admin/auth/csrf')).json();
  return { 'X-CSRF-TOKEN': csrf.token as string };
}

test('global plan page creates and edits complete shared rules', async ({ page }) => {
  await loginSharedPlans(page);
  await page.goto('/plans');
  await expect(page.getByRole('heading', { name: '卡种管理', exact: true })).toBeVisible();
  const name = '共享月卡-' + randomUUID().slice(0, 8);
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
  await editor.getByLabel('卡种名称', { exact: true }).fill(name);
  await editor.getByLabel('授权时长（小时）', { exact: true }).fill('720');
  await editor.getByLabel('设备上限', { exact: true }).fill('3');
  await editor.getByLabel('在线会话上限', { exact: true }).fill('2');
  await editor.getByLabel('激活期限（天）', { exact: true }).fill('30');
  await editor.getByLabel('授权功能', { exact: true }).fill('export');
  await editor.getByLabel('授权功能', { exact: true }).press('Enter');
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(editor.getByRole('button', { name: '保存卡种', exact: true })).toBeInViewport();
  }
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor).toBeHidden();
  const row = page.getByRole('row').filter({ hasText: name });
  await expect(row).toContainText('0 个应用');
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const editing = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await editing.getByLabel('在线会话上限', { exact: true }).fill('1');
  await editing.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editing).toBeHidden();
  const saved = await (await page.request.get(
    '/api/admin/plans?search=' + encodeURIComponent(name))).json();
  expect(saved.items).toHaveLength(1);
  expect(saved.items[0].maxDevices).toBe(3);
  expect(saved.items[0].maxConcurrentSessions).toBe(1);
  expect(saved.items[0].features).toEqual(['export']);
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const statusEditor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await statusEditor.getByLabel('允许继续发行', { exact: true }).click();
  await statusEditor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(statusEditor).toBeHidden();
  await page.getByLabel('卡种发行状态', { exact: true }).click();
  await page.getByTitle('已停发', { exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible();
  await page.getByLabel('卡种发行状态', { exact: true }).click();
  await page.getByTitle('可发行', { exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0);
});
~~~

- [x] **Step 2: 验证红灯**

在已配置测试管理员和本机 API 的环境，工作目录 frontend：

Run: `npm run test:e2e -- shared-plans.spec.ts`  
Expected: /plans 还没有“卡种管理”页面。若登录失败，先修复测试环境，不能作为功能红灯。

- [x] **Step 3: 更新运行中的开发 API 并生成契约**

按 README 从已加载开发环境配置的终端迁移开发数据库，并通过原启动会话重启本次编译的 API；不用旧进程的 schema。命令：

~~~powershell
dotnet run --project backend/src/KeyValidation.Api -- migrate
npm --prefix frontend run api:types
~~~

这一步是执行阶段的本机开发迁移；生产发布流程在任务 9。生成后确认 GlobalPlanDto、ApplicationPlanDto、PlanReferenceDto、SetPlanReference、ReportPlanDto.IsEnabled 均存在。

types.ts：

~~~typescript
import type { Dto } from '../../lib/types';

export type GlobalPlan = Dto<'GlobalPlanDto'>;
export type ApplicationPlanReference = Dto<'ApplicationPlanDto'>;
export type PlanReference = Dto<'PlanReferenceDto'>;
export type PlanUpdate = Dto<'UpdatePlan'>;
~~~

- [x] **Step 4: 实现统一查询、写入和缓存失效**

queries.ts：

~~~typescript
import { useQuery } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { api, queryString } from '../../lib/api';
import type { Page, Plan } from '../../lib/types';
import type { OperationsCatalog } from '../../lib/operations-types';
import type { ApplicationPlanReference, GlobalPlan, PlanReference, PlanUpdate } from './types';

async function allPlanPages<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<T>>(path + queryString({ page, pageSize: 100 }));
    items.push(...result.items);
    if (!result.items.length || items.length >= Number(result.total)) return items;
  }
}

export function useGlobalPlans(page: number, search = '', isEnabled?: boolean) {
  return useQuery({
    queryKey: ['plans', 'global', page, search, isEnabled],
    queryFn: () => api<Page<GlobalPlan>>('/admin/plans' +
      queryString({ page, pageSize: 20, search: search || undefined,
        isEnabled: isEnabled === undefined ? undefined : String(isEnabled) })),
  });
}

export function useAllGlobalPlans(enabled: boolean) {
  return useQuery({
    queryKey: ['plans', 'global-options'],
    queryFn: () => allPlanPages<GlobalPlan>('/admin/plans'), enabled,
  });
}

export function useApplicationPlans(applicationId?: string, enabled = true) {
  return useQuery({
    queryKey: ['plans', 'application-options', applicationId],
    queryFn: () => allPlanPages<Plan>('/admin/applications/' + applicationId + '/plans'),
    enabled: enabled && !!applicationId,
  });
}

export function useApplicationPlanReferences(applicationId: string, page: number) {
  return useQuery({
    queryKey: ['plans', 'references', applicationId, page],
    queryFn: () => api<Page<ApplicationPlanReference>>(
      '/admin/applications/' + applicationId + '/plan-references' +
      queryString({ page, pageSize: 20 })),
  });
}

export function usePlanApplications(planId?: string) {
  return useQuery({
    queryKey: ['plans', 'applications', planId],
    queryFn: () => api<PlanReference[]>('/admin/plans/' + planId + '/applications'),
    enabled: !!planId,
  });
}

export function usePlanCatalog(kind: 'operations' | 'reports' = 'operations', enabled = true) {
  return useQuery({
    queryKey: [kind === 'reports' ? 'operations-catalog' : 'operations-access-catalog'],
    queryFn: () => api<OperationsCatalog>(
      kind === 'reports' ? '/admin/reports/catalog' : '/admin/catalog'),
    enabled,
  });
}

export async function invalidatePlanQueries(cache: QueryClient) {
  const roots = ['plans', 'operations-access-catalog', 'operations-catalog',
    'batches', 'batch', 'operations-report', 'channel', 'channels', 'audit'];
  await Promise.all(roots.map(root => cache.invalidateQueries({ queryKey: [root] })));
}

export async function savePlan(cache: QueryClient, id: string | undefined, body: PlanUpdate) {
  const result = await api<GlobalPlan>(id ? '/admin/plans/' + id : '/admin/plans', {
    method: id ? 'PATCH' : 'POST', body,
  });
  await invalidatePlanQueries(cache);
  return result;
}

export async function setPlanReference(cache: QueryClient, applicationId: string,
  planId: string, isEnabled: boolean) {
  const result = await api<ApplicationPlanReference>(
    '/admin/applications/' + applicationId + '/plan-references/' + planId,
    { method: 'PUT', body: { isEnabled } });
  await invalidatePlanQueries(cache);
  return result;
}
~~~

options.ts 使用有类型的完整响应，不使用类型断言规避 schema 错误：

~~~typescript
interface PlanOptionSource {
  id: string;
  applicationId: string;
  name: string;
  isEnabled: boolean;
}

export function planOptions(plans: readonly PlanOptionSource[] | undefined,
  applicationId: string | undefined, purpose: 'issue' | 'history') {
  const visible = plans?.filter(plan => plan.applicationId === applicationId &&
    (purpose === 'history' || plan.isEnabled)) ?? [];
  const counts = new Map<string, number>();
  for (const plan of visible) counts.set(plan.name, (counts.get(plan.name) ?? 0) + 1);
  return visible.map(plan => ({
    value: plan.id,
    label: (counts.get(plan.name) ?? 0) > 1
      ? plan.name + ' · ' + plan.id.slice(0, 8) : plan.name,
  }));
}
~~~

现有 queryString 接受字符串和数值；本模块用 String(isEnabled) 将布尔筛选明确序列化为 true / false。

- [x] **Step 5: 实现独立规则编辑器**

PlanEditor.tsx；复用现有 LiveModal 和 useFormSnapshot 保持弹窗关闭及标签页恢复行为：

~~~tsx
import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Form, Input, InputNumber, Switch } from 'antd';
import { LiveModal } from '../../components/LiveModal';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { FeatureField } from '../policies/FeatureField';
import { savePlan } from './queries';
import type { GlobalPlan } from './types';

interface Values {
  name: string; durationHours: number; maxDevices: number;
  maxConcurrentSessions: number; activationValidityDays: number | null;
  features: string[]; isEnabled: boolean;
}

export function PlanEditor({ open, plan, close }: {
  open: boolean; plan: GlobalPlan | null; close: () => void;
}) {
  const [form] = Form.useForm<Values>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cache = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => { setError(null); }, [open, plan?.id]);
  useFormSnapshot(form, plan ?? 'new-plan', open, () => ({
    name: plan?.name ?? '', durationHours: plan ? Number(plan.durationSeconds) / 3600 : 720,
    maxDevices: Number(plan?.maxDevices ?? 1),
    maxConcurrentSessions: Number(plan?.maxConcurrentSessions ?? 1),
    activationValidityDays: plan?.activationValidityDays == null
      ? null : Number(plan.activationValidityDays),
    features: plan?.features ?? [], isEnabled: plan?.isEnabled ?? true,
  }));
  async function save(values: Values) {
    if (saving) return;
    setSaving(true); setError(null);
    try {
      await savePlan(cache, plan?.id, {
        name: values.name.trim(), durationSeconds: Math.round(values.durationHours * 3600),
        maxDevices: values.maxDevices, maxConcurrentSessions: values.maxConcurrentSessions,
        activationValidityDays: values.activationValidityDays ?? null,
        features: values.features ?? [], isEnabled: values.isEnabled,
      });
      message.success('卡种已保存'); close();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : '保存失败，请重试');
    } finally { setSaving(false); }
  }
  return <LiveModal className="policy-editor-modal" open={open}
    title={plan ? '编辑卡种' : '创建卡种'} onCancel={() => { if (!saving) close(); }}
    closable={{ 'aria-label': '关闭', disabled: saving }} confirmLoading={saving}
    okText="保存卡种" cancelText="取消" onOk={() => form.submit()}>
    {error && <Alert type="error" showIcon title={error} />}
    <Form form={form} name="global-plan-editor" layout="vertical"
      requiredMark={false} onFinish={save} disabled={saving}>
      <Form.Item label="卡种名称" name="name"
        rules={[{ required: true, whitespace: true, message: '请输入卡种名称' }]}>
        <Input maxLength={120} placeholder="例如：月卡、年卡" />
      </Form.Item>
      <div className="form-grid">
        <Form.Item label="授权时长（小时）" name="durationHours" rules={[{ required: true }]}>
          <InputNumber min={1 / 3600} max={87600} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label="设备上限" name="maxDevices" rules={[{ required: true }]}>
          <InputNumber min={1} max={100} precision={0} style={{ width: '100%' }} />
        </Form.Item>
      </div>
      <div className="form-grid">
        <Form.Item label="在线会话上限" name="maxConcurrentSessions"
          rules={[{ required: true, message: '请输入在线会话上限' }]}>
          <InputNumber min={1} max={100} precision={0} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label="激活期限（天）" name="activationValidityDays"
          extra="从发行起计算，留空不限。">
          <InputNumber min={1} max={3650} precision={0} style={{ width: '100%' }} />
        </Form.Item>
      </div>
      <FeatureField label="授权功能" />
      {plan && <Form.Item label="允许继续发行" name="isEnabled" valuePropName="checked">
        <Switch />
      </Form.Item>}
      <p className="form-note">所有引用应用统一使用这些规则；已发卡密和已创建订单保留原参数。</p>
    </Form>
  </LiveModal>;
}
~~~

- [x] **Step 6: 实现列表与关联应用视图**

PlanApplicationsDrawer.tsx：

~~~tsx
import { Drawer, Table, Tag } from 'antd';
import { QueryError, dateText, emptyTable } from '../../components/common';
import { usePlanApplications } from './queries';
import type { GlobalPlan, PlanReference } from './types';

export function PlanApplicationsDrawer({ plan, close }: {
  plan: GlobalPlan | null; close: () => void;
}) {
  const references = usePlanApplications(plan?.id);
  return <Drawer open={!!plan} title={(plan?.name ?? '') + ' · 关联应用'}
    size={640} onClose={close} closable={{ 'aria-label': '关闭' }}>
    <QueryError error={references.error} retry={references.refetch} />
    <Table<PlanReference> rowKey="applicationId" dataSource={references.data}
      loading={references.isLoading} locale={{ emptyText: emptyTable }}
      pagination={{ pageSize: 20 }} columns={[
        { title: '应用', dataIndex: 'applicationName' },
        { title: '本应用发行', dataIndex: 'isEnabled', render: value =>
          <Tag color={value ? 'success' : 'default'}>{value ? '已启用' : '已暂停'}</Tag> },
        { title: '引用时间', dataIndex: 'createdAt', render: dateText },
      ]} />
  </Drawer>;
}
~~~

PlansPage.tsx：

~~~tsx
import { useState } from 'react';
import { Button, Card, Input, Select, Space, Table, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { PageTitle, QueryError, durationText, emptyTable } from '../../components/common';
import { useGlobalPlans } from './queries';
import { PlanEditor } from './PlanEditor';
import { PlanApplicationsDrawer } from './PlanApplicationsDrawer';
import type { GlobalPlan } from './types';

export function PlansPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<'enabled' | 'disabled'>();
  const [editing, setEditing] = useState<GlobalPlan | 'new' | null>(null);
  const [viewing, setViewing] = useState<GlobalPlan | null>(null);
  const plans = useGlobalPlans(page, search,
    status === undefined ? undefined : status === 'enabled');
  return <>
    <PageTitle title="卡种管理" subtitle="统一配置授权规则，供多个应用引用。"
      extra={<Button type="primary" icon={<PlusOutlined />}
        onClick={() => setEditing('new')}>创建卡种</Button>} />
    <Card>
      <Space wrap>
        <Input.Search aria-label="搜索卡种" placeholder="搜索卡种名称" allowClear
          onSearch={value => { setSearch(value.trim()); setPage(1); }} style={{ width: 240 }} />
        <Select aria-label="卡种发行状态" allowClear placeholder="全部发行状态"
          value={status} style={{ width: 160 }}
          options={[{ value: 'enabled', label: '可发行' }, { value: 'disabled', label: '已停发' }]}
          onChange={value => { setStatus(value); setPage(1); }} />
        <Button onClick={() => plans.refetch()}>刷新</Button>
      </Space>
      <QueryError error={plans.error} retry={plans.refetch} />
      <Table<GlobalPlan> rowKey="id" dataSource={plans.data?.items}
        loading={plans.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 960 }}
        pagination={{ current: page, pageSize: 20, total: plans.data?.total,
          showSizeChanger: false, onChange: setPage }} columns={[
          { title: '卡种名称', dataIndex: 'name', render: (name, row) =>
            <><strong>{name}</strong><div className="muted">{row.id.slice(0, 8)}</div></> },
          { title: '授权时长', dataIndex: 'durationSeconds', render: durationText },
          { title: '设备上限', dataIndex: 'maxDevices' },
          { title: '在线上限', dataIndex: 'maxConcurrentSessions' },
          { title: '激活期限', dataIndex: 'activationValidityDays',
            render: value => value == null ? '不限' : String(value) + ' 天' },
          { title: '发行状态', dataIndex: 'isEnabled', render: value =>
            <Tag color={value ? 'success' : 'default'}>{value ? '可发行' : '已停发'}</Tag> },
          { title: '关联应用', render: (_, row) =>
            <Button type="link" onClick={() => setViewing(row)}>
              {String(row.referenceCount) + ' 个应用'}
            </Button> },
          { title: '操作', fixed: 'right', width: 80, render: (_, row) =>
            <Button type="link" disabled={!!plans.error}
              onClick={() => setEditing(row)}>编辑</Button> },
        ]} />
    </Card>
    <PlanEditor open={editing !== null} plan={editing === 'new' ? null : editing}
      close={() => setEditing(null)} />
    <PlanApplicationsDrawer plan={viewing} close={() => setViewing(null)} />
  </>;
}
~~~

- [x] **Step 7: 接入菜单、权限和中文文案**

WorkspacePage 增加 `globalScopeOnly?: boolean`。App.tsx 引入 PlansPage 和 CreditCardOutlined，导航在应用管理后增加：

~~~tsx
{ key: '/plans', icon: <CreditCardOutlined />, label: '卡种管理',
  group: 'licensing', adminOnly: true, globalScopeOnly: true, element: <PlansPage /> },
~~~

RequireAccess 参数和类型增加 `globalScopeOnly = false` / `globalScopeOnly?: boolean`；其拒绝条件增加：

~~~tsx
const globalScope = auth.access?.allApplications === true && !auth.access.channelId;
// 与原权限拒绝条件使用 ||
(globalScopeOnly && !globalScope)
~~~

AuthenticatedWorkspace 的 pages 过滤增加：

~~~tsx
(!item.globalScopeOnly ||
  (auth.access?.allApplications === true && !auth.access.channelId))
~~~

renderPage 给 RequireAccess 传入 `globalScopeOnly={item.globalScopeOnly}`，使直接访问 /plans 和菜单隐藏采用同一规则。

lib/api.ts 的现有错误映射增加：

~~~typescript
APPLICATION_PLAN_DISABLED: '该应用已暂停此卡种发行，请选择其他卡种',
~~~

components/common.tsx 的 actions 增加：

~~~typescript
'application-plan.bind': '引用卡种',
'application-plan.update': '调整应用卡种发行',
~~~

PageTitle 的 labels 增加 `'卡种管理': 'PLAN CATALOG'`。

- [x] **Step 8: 验证构建、浏览器流程与状态筛选**

Run: `npm --prefix frontend run build`  
Run（工作目录 frontend）: `npm run test:e2e -- shared-plans.spec.ts`  
Expected: 无类型错误，完整共享字段正确保存，新增卡种为零引用，四种宽度可操作。

- [x] **Step 9: 提交检查点**

~~~powershell
git add -- frontend/src/features/plans/types.ts frontend/src/features/plans/queries.ts frontend/src/features/plans/options.ts frontend/src/features/plans/PlansPage.tsx frontend/src/features/plans/PlanEditor.tsx frontend/src/features/plans/PlanApplicationsDrawer.tsx frontend/src/App.tsx frontend/src/features/workspace/Workspace.tsx frontend/src/lib/schema.d.ts frontend/src/lib/api.ts frontend/src/components/common.tsx frontend/e2e/shared-plans.spec.ts
git commit -m "feat: add standalone shared plan management"
~~~


## Task 7: 应用设置只维护引用，并保留试用配置

**Files:**
- Create: `frontend/src/features/plans/ApplicationPlanReferences.tsx`
- Create: `frontend/src/features/applications/ApplicationSettingsDrawer.tsx`
- Modify: `frontend/src/features/applications/ApplicationsPage.tsx`
- Delete after replacement: `frontend/src/features/applications/PlansDrawer.tsx`
- Test: `frontend/e2e/shared-plans.spec.ts`、`frontend/e2e/policies.spec.ts`、`frontend/e2e/ui.spec.ts`

**Interfaces:**
- Consumes: Task 6 的 useApplicationPlanReferences、useAllGlobalPlans、setPlanReference 和生成类型。
- Produces: `ApplicationPlanReferences({ application: Application })`。
- Produces: `ApplicationSettingsDrawer({ application: Application | null, close: () => void })`。
- TrialPolicyPanel 的 applicationId 接口与业务行为保持原样。

- [x] **Step 1: 写两应用通过界面引用同一卡种的测试**

将本测试追加到任务 6 的 shared-plans.spec.ts，复用同文件已定义的 loginSharedPlans：

~~~typescript
test('applications reference one global plan and retain trial settings', async ({ page }) => {
  const headers = await loginSharedPlans(page);
  const suffix = randomUUID().slice(0, 8);
  const created = await page.request.post('/api/admin/plans', {
    headers, data: { name: '共用年卡-' + suffix, durationSeconds: 31536000, maxDevices: 2 },
  });
  expect(created.status()).toBe(201);
  const plan = await created.json();
  const applications: { id: string; name: string }[] = [];
  for (const letter of ['a', 'b']) {
    const response = await page.request.post('/api/admin/applications', {
      headers, data: { code: 'shared-' + letter + '-' + suffix,
        name: '引用应用-' + letter + '-' + suffix },
    });
    expect(response.status()).toBe(201);
    applications.push(await response.json());
  }
  await page.goto('/applications');
  for (const app of applications) {
    await page.getByRole('row').filter({ hasText: app.name })
      .getByRole('button', { name: '应用设置', exact: true }).click();
    const settings = page.getByRole('dialog', { name: app.name + ' · 应用设置', exact: true });
    await expect(settings.getByRole('heading', { name: '试用授权', exact: true })).toBeVisible();
    await settings.getByLabel('选择全局卡种', { exact: true }).click();
    await page.getByTitle(plan.name + ' · ' + plan.id.slice(0, 8), { exact: true }).click();
    await settings.getByRole('button', { name: '引用卡种', exact: true }).click();
    await expect(settings.getByRole('row').filter({ hasText: plan.name })).toBeVisible();
    await settings.getByRole('button', { name: '关闭', exact: true }).click();
  }
  const global = await (await page.request.get('/api/admin/plans/' + plan.id)).json();
  expect(global.referenceCount).toBe(2);
  for (const app of applications) {
    const scoped = await (await page.request.get('/api/admin/applications/' + app.id + '/plans')).json();
    expect(scoped.items.find((item: { id: string }) => item.id === plan.id).applicationId).toBe(app.id);
  }
});
~~~

- [x] **Step 2: 验证红灯**

Run（frontend）: `npm run test:e2e -- shared-plans.spec.ts -g "applications reference"`  
Expected: 应用设置入口或引用界面不存在。

- [x] **Step 3: 实现引用面板**

ApplicationPlanReferences.tsx：

~~~tsx
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Select, Space, Switch, Table, Tag } from 'antd';
import { useAuth } from '../auth/AuthProvider';
import { QueryError, durationText, emptyTable } from '../../components/common';
import type { Application } from '../../lib/types';
import type { ApplicationPlanReference } from './types';
import { useAllGlobalPlans, useApplicationPlanReferences, setPlanReference } from './queries';

export function ApplicationPlanReferences({ application }: { application: Application }) {
  const auth = useAuth();
  const canEdit = auth.isAdmin && auth.access?.allApplications === true && !auth.access.channelId;
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string>();
  const [saving, setSaving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cache = useQueryClient();
  const { message } = App.useApp();
  const references = useApplicationPlanReferences(application.id, page);
  const global = useAllGlobalPlans(canEdit);

  async function setState(planId: string, enabled: boolean) {
    if (saving) return;
    setSaving(planId); setError(null);
    try {
      await setPlanReference(cache, application.id, planId, enabled);
      setSelected(undefined);
      message.success(enabled ? '本应用已启用此卡种' : '本应用已暂停此卡种发行');
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : '保存失败，请重试');
    } finally { setSaving(null); }
  }

  return <section>
    <h2 className="panel-title">引用卡种</h2>
    <p className="panel-description">授权规则在卡种管理中统一设置。已有引用再次提交会恢复本应用发行。</p>
    <QueryError error={references.error} retry={references.refetch} />
    {canEdit && <>
      <QueryError error={global.error} retry={global.refetch} />
      <Space wrap>
        <Select aria-label="选择全局卡种" showSearch={{ optionFilterProp: 'label' }}
          value={selected} onChange={setSelected} loading={global.isLoading}
          disabled={!!saving || !!global.error} style={{ width: 280 }}
          placeholder="选择全局卡种" options={global.data?.map(plan => ({
            value: plan.id, label: plan.name + ' · ' + plan.id.slice(0, 8),
          }))} />
        <Button type="primary" disabled={!selected || !!saving || !!global.error}
          loading={saving === selected} onClick={() => { if (selected) void setState(selected, true); }}>
          引用卡种
        </Button>
      </Space>
    </>}
    {error && <Alert type="error" showIcon title={error} />}
    <Table<ApplicationPlanReference> rowKey="planId" dataSource={references.data?.items}
      loading={references.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 760 }}
      pagination={{ current: page, pageSize: 20, total: references.data?.total,
        showSizeChanger: false, onChange: setPage }} columns={[
        { title: '卡种', dataIndex: 'name', width: 170 },
        { title: '共享规则', render: (_, row) => <>
          <div>{durationText(row.durationSeconds)} · {row.maxDevices} 台设备</div>
          <div>{row.maxConcurrentSessions} 个在线会话 · {row.activationValidityDays == null
            ? '激活期限不限' : '发行后 ' + row.activationValidityDays + ' 天内激活'}</div>
          <div className="muted">{row.features.length ? row.features.join('、') : '无细分功能'}</div>
        </> },
        { title: '全局发行', dataIndex: 'planEnabled', render: value =>
          <Tag color={value ? 'success' : 'default'}>{value ? '可发行' : '已停发'}</Tag> },
        { title: '本应用发行', render: (_, row) => canEdit
          ? <Switch aria-label={'允许本应用发行 ' + row.name} checked={row.isEnabled}
              loading={saving === row.planId} disabled={!!saving || !!references.error}
              onChange={value => { void setState(row.planId, value); }} />
          : row.isEnabled ? '已启用' : '已暂停' },
        { title: '实际状态', dataIndex: 'canIssue', render: value => value ? '可发行' : '不可发行' },
      ]} />
  </section>;
}
~~~

全局停发的卡种可以先引用，其实际状态仍不可发行；用户需要在卡种管理中恢复全局开关。

- [x] **Step 4: 用应用设置抽屉承载引用和原试用配置**

ApplicationSettingsDrawer.tsx：

~~~tsx
import { Drawer } from 'antd';
import type { Application } from '../../lib/types';
import { ApplicationPlanReferences } from '../plans/ApplicationPlanReferences';
import { TrialPolicyPanel } from './TrialPolicyPanel';

export function ApplicationSettingsDrawer({ application, close }: {
  application: Application | null; close: () => void;
}) {
  return <Drawer title={(application?.name ?? '') + ' · 应用设置'} open={!!application}
    size={820} onClose={close} closable={{ 'aria-label': '关闭' }}>
    {application && <>
      <div className="detail-summary application-summary">
        <div><h3>{application.name}</h3><code>{application.code}</code></div>
      </div>
      <ApplicationPlanReferences key={application.id} application={application} />
      <TrialPolicyPanel key={'trial-' + application.id} applicationId={application.id} />
    </>}
  </Drawer>;
}
~~~

ApplicationsPage 导入替换为 ApplicationSettingsDrawer，原“管理卡种”按钮改为“应用设置”，末尾替换组件：

~~~tsx
<ApplicationSettingsDrawer key={selected?.id ?? 'closed'} application={selected}
  close={() => setSelected(null)} />
~~~

页面说明改为“管理软件应用，选择共用卡种并配置应用策略。”；创建应用提示改为“创建后可引用已有卡种，为软件签发授权。”

给本页导入 invalidatePlanQueries，在应用创建、禁用和恢复的成功分支追加：

~~~typescript
await invalidatePlanQueries(cache);
~~~

这些应用变化会影响目录内容或 CanIssue；保留既有 applications / licenses 等失效操作。确认没有导入旧组件后删除 PlansDrawer.tsx。

- [x] **Step 5: 更新已有 UI 测试的真实入口**

policies.spec.ts 将原 openPlans 拆成应用设置与全局编辑两个明确入口：

~~~typescript
async function openApplicationSettings(page: Page, name: string) {
  await page.goto('/applications');
  await page.getByRole('row').filter({ hasText: name })
    .getByRole('button', { name: '应用设置', exact: true }).click();
}

async function openGlobalPlan(page: Page, id: string) {
  await page.goto('/plans');
  await page.getByRole('row').filter({ hasText: id.slice(0, 8) })
    .getByRole('button', { name: '编辑', exact: true }).click();
}
~~~

卡种规则测试改为 `await openGlobalPlan(page, plan.id)`，删除随后重复点击编辑的旧语句；试用测试改为 `await openApplicationSettings(page, app.name)`，保存后的 API 断言仍使用按应用接口。

ui.spec.ts 的页面宽度矩阵加入 `['plans', '卡种管理']`。移动端抽屉测试用以下片段替换旧“抽屉内新建卡种”操作：

~~~typescript
await page.getByRole('button', { name: '应用设置', exact: true }).first().click();
const settings = page.getByRole('dialog', { name: /· 应用设置$/ });
await expect(settings.getByRole('heading', { name: '引用卡种', exact: true })).toBeVisible();
await expect(settings.getByRole('heading', { name: '试用授权', exact: true })).toBeVisible();
await settings.getByRole('button', { name: '关闭', exact: true }).click();
await page.goto('/plans');
await page.getByRole('button', { name: '创建卡种', exact: true }).click();
await expect(page.getByLabel('授权时长（小时）', { exact: true })).toBeVisible();
await expect(page.getByRole('button', { name: '保存卡种', exact: true })).toBeInViewport();
await page.getByRole('button', { name: '取消', exact: true }).click();
~~~

其他移动端卡密、日期、工作区及权限测试不改动预期。

- [x] **Step 6: 运行绿灯**

Run: `npm --prefix frontend run build`  
Run（frontend）: `npm run test:e2e -- shared-plans.spec.ts policies.spec.ts ui.spec.ts`  
Expected: 两应用使用同一卡种 ID；独立编辑页、引用抽屉和试用配置均可操作，屏幕宽度检查通过。

- [x] **Step 7: 提交检查点**

~~~powershell
git add -- frontend/src/features/plans/ApplicationPlanReferences.tsx frontend/src/features/applications/ApplicationSettingsDrawer.tsx frontend/src/features/applications/ApplicationsPage.tsx frontend/src/features/applications/PlansDrawer.tsx frontend/e2e/shared-plans.spec.ts frontend/e2e/policies.spec.ts frontend/e2e/ui.spec.ts
git commit -m "feat: manage shared plan references in application settings"
~~~


## Task 8: 所有消费入口统一选卡，修正跨应用定价与恢复流程

**Files:**
- Modify: `frontend/src/features/operations/useOperationsCatalog.ts`、`ReportsPage.tsx`
- Modify: `frontend/src/features/licenses/IssueModal.tsx`
- Modify: `frontend/src/features/batches/BatchesPage.tsx`
- Modify: `frontend/src/features/commerce/OrderEditor.tsx`、`PortalRequestReview.tsx`、`ChannelDetail.tsx`
- Test: `frontend/e2e/shared-plans.spec.ts`

**Interfaces:**
- Consumes: Task 6 的 useApplicationPlans、usePlanCatalog、planOptions、invalidatePlanQueries。
- Produces: 各既有页面对外组件接口保持不变；渠道价格表单新增 applicationId 必填字段。
- Query key 保持 operations-access-catalog / operations-catalog；原包装 hook 委托公共模块，避免另建一份目录状态。

- [x] **Step 1: 建立浏览器共用的两应用样本并写定价测试**

在 shared-plans.spec.ts 增加以下 helper 和测试：

~~~typescript
async function createSharedBrowserCatalog(page: Page, headers: Record<string, string>) {
  const suffix = randomUUID().slice(0, 8);
  const created = await page.request.post('/api/admin/plans', {
    headers, data: { name: '公共卡-' + suffix, durationSeconds: 86400, maxDevices: 1 },
  });
  expect(created.status()).toBe(201);
  const plan = await created.json();
  const applications: { id: string; name: string; code: string }[] = [];
  for (const letter of ['a', 'b']) {
    const response = await page.request.post('/api/admin/applications', {
      headers, data: { code: 'catalog-' + letter + '-' + suffix,
        name: '共用应用-' + letter + '-' + suffix },
    });
    expect(response.status()).toBe(201);
    const app = await response.json();
    applications.push(app);
    const bound = await page.request.put(
      '/api/admin/applications/' + app.id + '/plan-references/' + plan.id,
      { headers, data: { isEnabled: true } });
    expect(bound.status()).toBe(200);
  }
  return { plan, applications, suffix };
}

test('channel price editor distinguishes applications sharing a plan', async ({ page }) => {
  const headers = await loginSharedPlans(page);
  const { plan, applications, suffix } = await createSharedBrowserCatalog(page, headers);
  const [a, b] = applications;
  const accountResponse = await page.request.post('/api/admin/accounts', {
    headers, data: { userName: 'plan-channel-' + suffix,
      password: 'Aa1!' + randomUUID().replaceAll('-', ''), role: 'Operator' },
  });
  expect(accountResponse.status()).toBe(201);
  const account = await accountResponse.json();
  const channelName = '共用定价渠道-' + suffix;
  const channelResponse = await page.request.post('/api/admin/channels', {
    headers, data: { name: channelName, accountId: account.id,
      applicationIds: [a.id, b.id], initialQuota: 10 },
  });
  expect(channelResponse.status()).toBe(201);
  const channel = await channelResponse.json();
  for (const [applicationId, unitPriceCents] of [[a.id, 1100], [b.id, 2200]]) {
    const saved = await page.request.put('/api/admin/channels/' + channel.id + '/prices',
      { headers, data: { applicationId, planId: plan.id, unitPriceCents } });
    expect(saved.status()).toBe(200);
  }
  await page.goto('/channels');
  await page.getByLabel('搜索渠道', { exact: true }).fill(channelName);
  await page.getByRole('row').filter({ hasText: channelName })
    .getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '设置售价', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '设置渠道售价', exact: true });
  await editor.getByLabel('定价应用', { exact: true }).click();
  await page.getByTitle(b.name, { exact: true }).click();
  await editor.getByLabel('定价卡种', { exact: true }).click();
  await page.getByTitle(plan.name, { exact: true }).click();
  await expect(editor.getByLabel('售价（分）', { exact: true })).toHaveValue('2200');
  await editor.getByLabel('售价（分）', { exact: true }).fill('3300');
  await editor.getByRole('button', { name: '保存售价', exact: true }).click();
  await expect(editor).toBeHidden();
  const saved = await (await page.request.get('/api/admin/channels/' + channel.id)).json();
  expect(saved.prices.find((x: { applicationId: string }) => x.applicationId === a.id).unitPriceCents).toBe(1100);
  expect(saved.prices.find((x: { applicationId: string }) => x.applicationId === b.id).unitPriceCents).toBe(3300);
});
~~~

- [x] **Step 2: 写两种发卡失败的恢复区别**

以下测试复用同文件 helper；故障注入只替代本次发卡响应，不改数据库：

~~~typescript
for (const recovery of [false, true]) {
  test('reference rejection ' + (recovery ? 'preserves uncertain request' : 'allows a new selection'),
    async ({ page }) => {
      const headers = await loginSharedPlans(page);
      const { plan, applications } = await createSharedBrowserCatalog(page, headers);
      const app = applications[0];
      await page.goto('/licenses');
      await page.getByRole('button', { name: /生成卡密$/ }).click();
      const modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
      await modal.getByLabel('所属应用', { exact: true }).click();
      await page.getByTitle(app.name, { exact: true }).click();
      await modal.getByLabel('卡种', { exact: true }).click();
      await page.getByTitle(plan.name, { exact: true }).click();
      let attempt = 0;
      const requests: unknown[] = [];
      await page.route('**/api/admin/license-batches', async route => {
        if (route.request().method() !== 'POST') return route.continue();
        requests.push(route.request().postDataJSON());
        attempt++;
        const uncertain = recovery && attempt === 1;
        await route.fulfill({
          status: uncertain ? 503 : 409, contentType: 'application/json',
          body: JSON.stringify({ code: uncertain ? 'INTERNAL_ERROR' : 'APPLICATION_PLAN_DISABLED' }),
        });
      });
      await modal.getByRole('button', { name: '确认生成', exact: true }).click();
      if (recovery) {
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toBeVisible();
        await modal.getByRole('button', { name: '使用原请求重试', exact: true }).click();
        await expect(modal.getByText('该应用已暂停此卡种发行，请选择其他卡种', { exact: true })).toBeVisible();
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toBeVisible();
        expect(requests).toHaveLength(2);
        expect(requests[1]).toEqual(requests[0]);
      } else {
        await expect(modal.getByText('该应用已暂停此卡种发行，请选择其他卡种', { exact: true })).toBeVisible();
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toHaveCount(0);
        await expect(modal.getByRole('button', { name: '确认生成', exact: true })).toBeVisible();
      }
    });
}
~~~

- [x] **Step 3: 验证红灯**

Run（frontend）: `npm run test:e2e -- shared-plans.spec.ts -g "channel price|reference rejection"`  
Expected: 旧价格编辑器没有应用选择；新 409 还被当成结果未知，首次失败无法恢复选卡。

- [x] **Step 4: 让目录和选项统一进入卡种模块**

useOperationsCatalog.ts 完整替换为：

~~~typescript
import { usePlanCatalog } from '../plans/queries';

export function useOperationsCatalog(enabled = true) {
  return usePlanCatalog('operations', enabled);
}
~~~

ReportsPage 导入 usePlanCatalog / planOptions，目录查询与 options 替换为：

~~~tsx
const catalog = usePlanCatalog('reports');
// “统计卡种”的 Select：
options={planOptions(catalog.data?.plans, applicationId, 'history')}
~~~

IssueModal 导入 useApplicationPlans / invalidatePlanQueries 和 planOptions，替换局部多页查询：

~~~tsx
const plans = useApplicationPlans(appId, open);
// “卡种”的 Select：
options={planOptions(plans.data, appId, pending ? 'history' : 'issue')}
disabled={!appId || plans.isLoading || !!plans.error}
~~~

空状态提示仅在 `plans.isSuccess` 时显示，文案为“该应用暂无可发行卡种，请先在应用设置中引用卡种并启用发行。”。保留已有查询错误提示和重试按钮。submit 的早期校验加入：

~~~typescript
if (!pending && (plans.isPending || plans.isError)) return;
~~~

BatchesPage 导入同一 hook 与 options，原重复翻页代码替换为：

~~~tsx
const plans = useApplicationPlans(applicationId);
// 批次卡种筛选：
options={planOptions(plans.data, applicationId, 'history')}
~~~

OrderEditor 和 PortalRequestReview 导入 planOptions，分别替换原局部数组映射：

~~~tsx
// OrderEditor 的订单卡种：
options={planOptions(applications?.plans, applicationId, pending ? 'history' : 'issue')}
// PortalRequestReview 的续期卡种：
options={planOptions(catalog?.plans, request?.applicationId, 'issue')}
~~~

保留订单应用变化时清空 customerId / planId / licenseId 的逻辑；pending 请求仍使用既有固定请求体。

- [x] **Step 5: 正确处理新发卡拒绝，并保留旧请求恢复**

IssueModal.submit 的 catch 中，用以下判断替换原明确失败分支；保留原 setError：

~~~typescript
if (!hadPendingRequest && e instanceof ApiError &&
    ([400, 401, 403, 404, 429].includes(e.status) ||
      e.code === 'APPLICATION_PLAN_DISABLED')) {
  sessionStorage.removeItem(pendingKey);
  setPending(null); setRecovered(null);
  if (e.code === 'PLAN_DISABLED' || e.code === 'APPLICATION_PLAN_DISABLED') {
    form.setFieldValue('planId', undefined);
    await invalidatePlanQueries(cache);
  }
}
~~~

不能把全部 409 加入可清除集合：BATCH_ALREADY_CREATED 等冲突可能代表已经提交。已有 pending 的请求无论收到何种新拒绝，继续保持原恢复路径。

- [x] **Step 6: 渠道价格表单明确选择应用和卡种**

ChannelDetail 将 form 类型改为：

~~~typescript
interface PriceValues { applicationId: string; planId: string; unitPriceCents: number }
const [form] = Form.useForm<PriceValues>();
const priceApplicationId = Form.useWatch('applicationId', form);
~~~

将 useFormSnapshot 移到 channel 已由 detail.data 得到之后，初始值为：

~~~typescript
useFormSnapshot(form, id, priceOpen, () => ({
  applicationId: channel?.applicationIds.length === 1 ? channel.applicationIds[0] : undefined,
  planId: undefined, unitPriceCents: 0,
}));
~~~

savePrice 签名使用 PriceValues，查找和请求体必须同时使用两个 ID：

~~~typescript
const plan = catalog.data?.plans.find(item =>
  item.applicationId === values.applicationId && item.id === values.planId);
if (!id || busy || !plan || !channel?.applicationIds.includes(values.applicationId)) return;
// 原 try 中的 PUT：
await api('/admin/channels/' + id + '/prices', {
  method: 'PUT',
  body: { applicationId: values.applicationId, planId: values.planId,
    unitPriceCents: values.unitPriceCents },
});
~~~

保留 busy / error / refresh / 关闭弹窗逻辑；删除原只按渠道应用集合筛出的局部 plans 变量和 plan.applicationId 推断，价格来源完全由表单应用与卡种组合确定。用下列两个 Form.Item 替换原单个卡种选择器：

~~~tsx
<Form.Item label="定价应用" name="applicationId"
  rules={[{ required: true, message: '请选择定价应用' }]}>
  <Select showSearch={{ optionFilterProp: 'label' }}
    options={catalog.data?.applications.filter(app => channel?.applicationIds.includes(app.id))
      .map(app => ({ value: app.id, label: app.name }))}
    onChange={() => form.setFieldsValue({ planId: undefined, unitPriceCents: 0 })} />
</Form.Item>
<Form.Item label="定价卡种" name="planId"
  rules={[{ required: true, message: '请选择卡种' }]}>
  <Select showSearch={{ optionFilterProp: 'label' }} disabled={!priceApplicationId}
    options={planOptions(catalog.data?.plans, priceApplicationId, 'history')}
    onChange={planId => form.setFieldValue('unitPriceCents', Number(channel?.prices.find(
      item => item.applicationId === priceApplicationId && item.planId === planId
    )?.unitPriceCents ?? 0))} />
</Form.Item>
~~~

价格表保留现有组合 rowKey，并把卡种名称解析限定到所在应用：

~~~tsx
{ title: '卡种', dataIndex: 'planId', render: (value, row) =>
  catalog.data?.plans.find(item => item.applicationId === row.applicationId &&
    item.id === value)?.name || '卡种未记录' },
~~~

- [x] **Step 7: 运行绿灯与消费入口回归**

Run: `npm --prefix frontend run build`  
Run（frontend）: `npm run test:e2e -- shared-plans.spec.ts commerce.spec.ts policies.spec.ts workspace-tabs.spec.ts`  
Expected: 同一卡种两应用价格独立；历史选项仍存在；首次明确拒绝可重选，旧未知请求保留原请求 ID。

- [x] **Step 8: 提交检查点**

~~~powershell
git add -- frontend/src/features/operations/useOperationsCatalog.ts frontend/src/features/operations/ReportsPage.tsx frontend/src/features/licenses/IssueModal.tsx frontend/src/features/batches/BatchesPage.tsx frontend/src/features/commerce/OrderEditor.tsx frontend/src/features/commerce/PortalRequestReview.tsx frontend/src/features/commerce/ChannelDetail.tsx frontend/e2e/shared-plans.spec.ts
git commit -m "feat: share plan selection and preserve application-specific pricing"
~~~


## Task 9: 贯通缓存与历史流程，更新使用说明并记录验证

**Files:**
- Test: `frontend/e2e/shared-plans.spec.ts`
- Modify: `README.md`、`docs/api-contract.md`、`docs/backend-navigation.md`、`docs/desktop-quickstart.md`、`docs/verification.md`
- Modify: `frontend/src/features/integrations/IntegrationGuide.tsx`
- Read: `docs/frontend-compatibility.md`、`docs/backup-restore.md`
- Generated verification artifacts: `.local/verification/shared-plans/`，不提交其中的测试日志或敏感数据。

**Interfaces:**
- Consumes: 完整 Task 1–8，实现与任务 6–8 在 shared-plans.spec.ts 定义的 helper。
- Produces: 新卡种操作路径、全局/引用 API 契约、迁移与回退说明、实际验证记录。没有新增业务接口。

- [x] **Step 1: 写保留页面中的配置刷新与历史筛选测试**

将测试追加到 shared-plans.spec.ts：

~~~typescript
test('shared edits refresh existing pages while paused references remain in history', async ({ page }) => {
  test.setTimeout(90000);
  const headers = await loginSharedPlans(page);
  const { plan, applications } = await createSharedBrowserCatalog(page, headers);
  const [a, b] = applications;
  const originalResponse = await page.request.post('/api/admin/license-batches', {
    headers, data: { applicationId: a.id, planId: plan.id, quantity: 1, requestId: randomUUID() },
  });
  expect(originalResponse.status()).toBe(201);
  const original = await originalResponse.json();

  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  let modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(a.name, { exact: true }).click();
  await modal.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(plan.name, { exact: true }).click();
  await modal.getByRole('button', { name: '取消', exact: true }).click();

  await page.getByRole('menuitem', { name: '卡种管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: plan.id.slice(0, 8) })
    .getByRole('button', { name: '编辑', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  const changedName = plan.name + '-新版';
  await editor.getByLabel('卡种名称', { exact: true }).fill(changedName);
  await editor.getByLabel('授权时长（小时）', { exact: true }).fill('2');
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor).toBeHidden();

  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(b.name, { exact: true }).click();
  await modal.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(changedName, { exact: true }).click();
  await modal.getByRole('button', { name: '取消', exact: true }).click();
  const newResponse = await page.request.post('/api/admin/license-batches', {
    headers, data: { applicationId: b.id, planId: plan.id, quantity: 1, requestId: randomUUID() },
  });
  expect(newResponse.status()).toBe(201);
  const issued = await newResponse.json();
  const oldDetail = await (await page.request.get(
    '/api/admin/licenses/' + original.licenses[0].id)).json();
  const newDetail = await (await page.request.get(
    '/api/admin/licenses/' + issued.licenses[0].id)).json();
  expect(oldDetail.license.durationSeconds).toBe(86400);
  expect(newDetail.license.durationSeconds).toBe(7200);

  await page.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: a.name })
    .getByRole('button', { name: '应用设置', exact: true }).click();
  const settings = page.getByRole('dialog', { name: a.name + ' · 应用设置', exact: true });
  const toggle = settings.getByRole('switch', { name: '允许本应用发行 ' + changedName, exact: true });
  await toggle.click();
  await expect(toggle).not.toBeChecked();
  await settings.getByRole('button', { name: '关闭', exact: true }).click();

  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(a.name, { exact: true }).click();
  await expect(modal.getByText(
    '该应用暂无可发行卡种，请先在应用设置中引用卡种并启用发行。', { exact: true })).toBeVisible();
  await modal.getByRole('button', { name: '取消', exact: true }).click();
  await page.getByRole('menuitem', { name: '批次管理', exact: true }).click();
  await page.locator('#batch-application-filter').click();
  await page.getByTitle(a.name, { exact: true }).click();
  await page.locator('#batch-plan-filter').click();
  await expect(page.getByTitle(changedName, { exact: true })).toBeVisible();
});
~~~

- [x] **Step 2: 更新面向使用者的文档与接入提示**

README 使用流程、desktop-quickstart 的后台准备步骤、IntegrationGuide 的准备说明统一为：

~~~text
1. 在“卡种管理”创建卡种，设置时长、设备与在线上限、激活期限和功能。
2. 在“应用管理 → 应用设置 → 引用卡种”选择此应用使用的卡种。
3. 在“卡密管理”选择应用与卡种生成授权，或通过订单完成交付。
修改卡种影响所有引用应用后续的直接发卡和新建订单。已发卡密和已创建订单保留原参数。
应用试用规则在“应用设置”中单独配置。
~~~

docs/api-contract.md 增加全局目录、应用引用和原接口兼容说明，逐条写入：

~~~text
GET/POST /api/admin/plans：全局卡种列表与创建。
GET/PATCH /api/admin/plans/{id}：返回 GlobalPlanDto，不再返回单一 applicationId。
GET /api/admin/plans/{id}/applications：关联应用及本应用发行状态。
GET /api/admin/applications/{id}/plan-references：分页引用详情。
PUT /api/admin/applications/{id}/plan-references/{planId}：必填 {"isEnabled": true|false}，重复提交幂等。
GET /api/admin/applications/{id}/plans：保留 PlanDto，applicationId 为请求应用，isEnabled 为有效发行状态。
POST /api/admin/applications/{id}/plans：原子创建全局卡种并引用到请求应用。
ReportPlanDto 新增 isEnabled；同一 planId 可以在不同 applicationId 下重复出现。
全局配置和引用写入要求 platform.manage、全部应用范围且无渠道/客户限制。
APPLICATION_PLAN_DISABLED 为 409；PLAN_DISABLED 保持现有 403；未引用或不可见组合为 404。
渠道价格以 channelId + applicationId + planId 唯一定位。
~~~

记录三个可复制的请求示例：

~~~json
{
  "name": "共享月卡",
  "durationSeconds": 2592000,
  "maxDevices": 1,
  "maxConcurrentSessions": 1,
  "activationValidityDays": 30,
  "features": ["export"]
}
~~~

~~~json
{ "isEnabled": true }
~~~

~~~json
{
  "applicationId": "11111111-1111-4111-8111-111111111111",
  "planId": "22222222-2222-4222-8222-222222222222",
  "quantity": 1,
  "requestId": "33333333-3333-4333-8333-333333333333"
}
~~~

示例 GUID 仅用于展示结构，不是现有资源。backend-navigation 的索引增加 PlanEndpoints → PlanCatalogService / PlanCatalogQueries / PlanAvailability，并说明引用表和订单快照的区别。

README 部署说明加入设计第 8 节的两次迁移顺序、先备份并暂停旧版本写入、API/前端一起升级，以及零引用/多引用数据不能向单应用结构强行降级。记录实际适用限制，不把可回退当作无条件承诺。

- [x] **Step 3: 执行必要的完整验证**

先准备专用结果目录：

~~~powershell
New-Item -ItemType Directory -Force -Path '.local/verification/shared-plans' | Out-Null
~~~

Run:

~~~powershell
dotnet test backend/KeyValidation.slnx --logger trx --results-directory .local/verification/shared-plans/backend
dotnet test samples/KeyValidation.Client.Tests --logger trx --results-directory .local/verification/shared-plans/client
dotnet test samples/KeyValidation.Desktop.Tests --logger trx --results-directory .local/verification/shared-plans/desktop
npm --prefix frontend run build
~~~

逐条检查退出码；前一条失败时先定位修复，不把后面的成功覆盖前面的失败。客户端源码没有新增协议字段要求，这里验证共享配置重构没有改变其现有契约。

本机 API 与开发数据库已迁移、管理员变量已配置后，执行浏览器验收并输出机器可读报告：

~~~powershell
$env:PLAYWRIGHT_JSON_OUTPUT_FILE = Join-Path (Get-Location) '.local/verification/shared-plans/browser.json'
npm --prefix frontend run test:e2e -- shared-plans.spec.ts policies.spec.ts commerce.spec.ts rbac-audit.spec.ts workspace-tabs.spec.ts editor-lifecycle.spec.ts ui.spec.ts --reporter=list,json
~~~

Expected: 后端、客户端、桌面测试、生产构建和指定浏览器测试均成功；没有被跳过的必需场景。出现回归时保留原断言，按实际原因修复后仅重跑受影响测试，最终确认其依赖的必要检查。

- [x] **Step 4: 从本次执行结果生成验证记录**

确认结果目录只包含本次执行产生的文件。下面脚本读取实际计数并拒绝失败或跳过；它不替代对 build 退出码的确认：

~~~powershell
$verificationRoot = Join-Path (Get-Location) '.local/verification/shared-plans'
$trxFiles = @(Get-ChildItem -LiteralPath $verificationRoot -Recurse -Filter '*.trx')
if ($trxFiles.Count -lt 3) { throw 'Missing test result files.' }
$verificationLines = [Collections.Generic.List[string]]::new()
$verificationLines.Add('## 全局卡种与应用引用验收 — ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
$verificationLines.Add('')
foreach ($trx in $trxFiles) {
    [xml]$result = Get-Content -LiteralPath $trx.FullName -Raw
    $counts = $result.TestRun.ResultSummary.Counters
    if ([int]$counts.total -eq 0 -or [int]$counts.failed -ne 0 -or
        [int]$counts.notExecuted -ne 0 -or [int]$counts.passed -ne [int]$counts.total) {
        throw ('Incomplete test result: ' + $trx.Name)
    }
    $verificationLines.Add('- ' + $trx.Directory.Name + ' / ' + $trx.Name +
        ': ' + $counts.passed + '/' + $counts.total + ' passed.')
}
$browser = Get-Content -LiteralPath (Join-Path $verificationRoot 'browser.json') -Raw | ConvertFrom-Json
if ($browser.stats.expected -le 0 -or $browser.stats.unexpected -ne 0 -or
    $browser.stats.skipped -ne 0 -or $browser.stats.flaky -ne 0) {
    throw 'Browser verification is incomplete.'
}
$verificationLines.Add('- Playwright: ' + $browser.stats.expected + ' passed; 0 unexpected, skipped, or flaky.')
$verificationLines.Add('- 覆盖共享配置、应用引用、权限、迁移、并发、渠道定价、历史快照及工作区缓存刷新。')
Add-Content -LiteralPath 'docs/verification.md' -Value ($verificationLines -join [Environment]::NewLine) -Encoding utf8
~~~

在该记录中再写入实际已确认的 build 命令与退出码，以及本次后端/浏览器命令。若有环境限制，明确写未执行的项，不能运行上述脚本生成“全部通过”的替代结论。

- [x] **Step 5: 检查旧入口、文档与范围一致性并提交**

Run:

~~~powershell
rg -n 'PlansDrawer|管理卡种' frontend/src README.md docs/desktop-quickstart.md docs/api-contract.md
rg -n 'plan\.ApplicationId|p\.ApplicationId|LicensePlan x =>|PlanDtoOf' backend/src -g '*.cs' -g '!**/Migrations/**' -g '!**/obj/**' -g '!**/bin/**'
~~~

Expected: 没有被删除组件的引用或过时的“应用内维护卡种规则”指引；兼容 API 的应用字段和其他实体的 ApplicationId 合法保留。不要清改历史设计文档。

~~~powershell
git add -- frontend/e2e/shared-plans.spec.ts frontend/src/features/integrations/IntegrationGuide.tsx README.md docs/api-contract.md docs/backend-navigation.md docs/desktop-quickstart.md docs/verification.md
git commit -m "docs: document and verify shared plan catalog workflows"
~~~

## 规格覆盖与交接

| 设计要求 | 实施与验证 |
| --- | --- |
| 全局记录、引用联合主键、保留旧 ID | Task 1、4；SharedPlanMigrationTests / CatalogTests |
| 唯一可用性判断、历史可见、统计不重复 | Task 2；SharedPlanIssuanceTests / OperationsAccessTests |
| 跨应用价格与订单快照 | Task 3；SharedPlanCommerceTests；原 CommerceChannel / Boundary |
| 全局管理范围、应用/渠道/客户过滤、审计 | Task 4、5；SharedPlanAccessTests；原 OperationsAccess / ReadableAudit |
| 并发引用、暂停提交边界、旧请求幂等 | Task 4、5、8；ConcurrencyTests / 浏览器故障注入 |
| 移除单应用归属、受控降级 | Task 4、5；SharedPlanDowngradeTests / 原迁移回归 |
| 独立页面、完整字段、应用引用和试用 | Task 6、7；shared-plans / policies / ui |
| 所有消费者统一查询、组合选项及缓存刷新 | Task 6、8、9；shared-plans / commerce / workspace |
| OpenAPI、客户端兼容、说明及最终验证 | Task 5、9；OpenApiTests / 客户端套件 / 构建 |

执行交接时可选择：
1. superpowers:subagent-driven-development：按任务分派实施与审查；依赖顺序仍按本计划。
2. superpowers:executing-plans：在当前会话逐任务执行并记录检查点。

本计划到此完整；业务代码与上述测试在本轮规划中尚未实施或运行。







