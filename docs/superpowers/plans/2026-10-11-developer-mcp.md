# Developer MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 第三方 AI 编程助手通过远程 MCP 获取准确的公开卡密验证资料、在线示例和联调指引。

**Architecture:** 在现有 API 进程中增加官方 SDK 支持的无状态 `/mcp`。数据库无关的共享资料目录同时服务于前端接入说明、公开 HTTP 文档端点和 MCP。Vite 与 Nginx 提供同源代理。

**Tech Stack:** .NET 10、ASP.NET Core、官方 MCP C# SDK、React / TypeScript / Ant Design、xUnit、Playwright。

**Spec:** `docs/superpowers/specs/2026-10-11-developer-mcp-design.md`

## Global Constraints

- 匿名、只读；不查询真实应用、客户、卡密或订单，不执行业务操作。
- 每 IP 每分钟 60 次默认限额，同时受全局限流约束；429 携带 Retry-After。
- 请求体上限 64 KiB，对无 Content-Length 请求同样有效。
- query：1–200 字符；limit：1–10，默认 5；errorCode：1–100 字符。
- language：csharp/python/javascript；scenario：online-license。
- 会话令牌仅在内存中，设备标识稳定保存；明确拒绝或租约到期即停止受保护功能。
- 官方依赖版本先核实兼容性后锁定；配置示例须注明经官方资料核实的客户端。
- 当前目录无 Git 元数据；原地保留其他工作，不创建 worktree 或执行提交。

## Review Focus

- 页面 origin 与 API 地址不同：复制出的地址须经过有效代理，不能返回 SPA HTML（任务 5）。
- 分块请求无长度头：64 KiB 限制仍生效（任务 3）。
- 系统时钟回拨或长时间挂起：不得延长有效租约（任务 2）。
- 节点不能连接数据库：静态资料服务不主动查询业务数据库（任务 1、3）。
- 带有效后台 Cookie 的调用：不会改变 MCP 公开资料范围或绕过后台限制（任务 3）。

## Task 1: 共享资料目录与公开读取接口

**Files:**
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperDocumentationCatalog.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperDocumentationModels.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperDocumentationEndpoints.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/Content/*.md`（quick-start、activation、trial、heartbeat、logout、device-release、versions、offline、errors）
- Modify: `backend/src/KeyValidation.Api/Program.cs`、`backend/src/KeyValidation.Api/KeyValidation.Api.csproj`
- Test: `backend/tests/KeyValidation.IntegrationTests/DeveloperDocumentationTests.cs`

**Interfaces:**
- Produces: `DeveloperDocument(string Id, string Title, string Topic, string Markdown, string Version)`；`DeveloperDocumentSummary(string Id, string Title, string Topic, string Version)`。
- Produces: `DeveloperApiReference(string OperationId, string Method, string Path, string Authentication, string Markdown, string Version)`。
- Produces: `DeveloperDocumentationCatalog.List(): IReadOnlyList<DeveloperDocumentSummary>`、`Get(string id): DeveloperDocument?`、`Search(string query, int limit): IReadOnlyList<DeveloperDocumentSummary>`、`GetApiReference(string operationId): DeveloperApiReference?`。
- Produces: `MapDeveloperDocumentationApi(this IEndpointRouteBuilder app)`；GET `/api/integrations/developer-docs` 返回索引数组，GET `/{id}` 返回完整记录或 404。

- [x] 编写测试 `AnonymousReadsPublicCatalog`（200、版本非空、包含九个主题）、`UnknownDocumentReturns404`、`CatalogRoutesExist`（方法及路径存在于 EndpointDataSource）、`CatalogHasNoBusinessDependencies`（独立构造目录并读取全部内容）。
- [x] 运行 `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~DeveloperDocumentationTests`，确认新增行为失败。
- [x] 实现目录和端点。Markdown 嵌入 API 程序集；显式目录校验重复 ID，使用 `2026-10-11` 资料版本；操作 ID 固定为 activate、trial-activate、heartbeat、logout、device-release、version、offline-verify。资料逐条对照实际端点和 `docs/api-contract.md`。
- [x] 重跑同一测试命令，要求全部通过。

## Task 2: 三种语言的在线示例与排错资料

**Files:**
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperExampleCatalog.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/Examples/online-license.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/Examples/online-license.py`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/Examples/online-license.mjs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperTroubleshootingCatalog.cs`
- Create: `scripts/verify-developer-examples.py`
- Modify: `backend/src/KeyValidation.Api/KeyValidation.Api.csproj`（源码示例作为嵌入内容，排除模板 .cs 的编译）
- Test: `backend/tests/KeyValidation.IntegrationTests/DeveloperExampleTests.cs`

**Interfaces:**
- Produces: `DeveloperCodeExample(string Language, string Scenario, string Code, string Instructions, string Version)`；`DeveloperExampleCatalog.Get(string language, string scenario): DeveloperCodeExample`。
- Produces: `DeveloperTroubleshootingGuide(string ErrorCode, bool Known, string Markdown, string Version)`；`DeveloperTroubleshootingCatalog.Get(string errorCode, string? operationId): DeveloperTroubleshootingGuide`。

- [x] 编写测试 `AllSupportedExamplesLoad`（三种语言内容与启动说明非空）、`UnsupportedExampleRejected`、`UnknownErrorIsExplicitlyUnknown`；用假 HTTP 服务编写行为用例：正常激活/退出、features 缺失、明确拒绝、429 的 Retry-After、网络故障、租约到期、时钟回拨、挂起后恢复。
- [x] 运行上述测试及 `python scripts/verify-developer-examples.py`，确认未实现示例失败。
- [x] 实现模板和目录。使用现有契约字段，环境变量采用 KEYVALIDATION_API_URL、APP_CODE、LICENSE_KEY、DEVICE_ID、REQUIRED_FEATURE 对应的 KEYVALIDATION_ 前缀；未显式配置 DEVICE_ID 时在本机用户目录保存随机稳定标识。Python 使用标准库，JavaScript 使用 Node 原生 fetch，C# 使用 .NET 10；模板启动说明精确对应实际运行方式。
- [x] 用 monotonic/performance.now/Stopwatch 计算剩余期限；有界退避等待必须截断于最早截止期限，每次受保护操作和挂起恢复均重新检查。敏感值不写日志，退出以 finally/best-effort logout 清理。离线专题沿用现有 C# 示例指引。
- [x] 运行三种模板行为验证及 `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~DeveloperExampleTests`，要求通过；验证脚本只操作临时设备文件与假服务。

## Task 3: MCP 协议端点与请求控制

**Files:**
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperMcpTools.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperMcpResources.cs`
- Create: `backend/src/KeyValidation.Api/DeveloperIntegration/DeveloperMcpConfiguration.cs`
- Modify: `backend/src/KeyValidation.Api/Program.cs`、`backend/src/KeyValidation.Api/KeyValidation.Api.csproj`、`backend/src/KeyValidation.Api/appsettings.json`
- Test: `backend/tests/KeyValidation.IntegrationTests/DeveloperMcpTests.cs`

**Interfaces:**
- Consumes: 任务 1、2 的目录与返回类型。
- Produces: 五个 SDK 注册工具，名称和输入严格使用 spec；resources 的 URI 为 `keygate://docs/{id}`。
- Produces: `AddDeveloperMcp(this IServiceCollection services, IConfiguration configuration)`、`MapDeveloperMcp(this IEndpointRouteBuilder app)`。
- Configuration: `RateLimiting:DeveloperDocsPerMinute=60`；`DeveloperMcp:AllowedOrigins`、`DeveloperMcp:AllowedHosts` 显式部署白名单。

- [x] 查官方 C# SDK 当前稳定版本、ASP.NET Core 无状态接入及真实 MCP 客户端 API，记录版本并锁定包引用；测试使用官方客户端而非仅手写 JSON 冒充协议验证。
- [x] 编写 `AnonymousProtocolRoundTrip`，断言 initialize、工具发现、五个工具调用、资源发现/读取成功且版本一致；`InvalidToolInputsReturnErrors`；`SearchNoMatchReturnsEmpty`；`UnknownCodeReturnsGenericGuide`。
- [x] 编写 `RateLimitHasRetryAfter`（专用限额隔离全局限额）、`ChunkedOversizedBodyRejected`（未知长度自定义 HttpContent）、`OriginAndHostAreValidated`（无 Origin 允许、白名单允许、未知 Origin/Host 拒绝）、`CookieDoesNotExpandPublicContent`；验证目录可脱离数据库使用，协议处理不解析业务凭证。
- [x] 运行 `dotnet test backend/tests/KeyValidation.IntegrationTests --filter FullyQualifiedName~DeveloperMcpTests`，确认行为测试失败。
- [x] 实现 SDK 注册与工具参数校验，设置只读注解；校验资源 ID。不访问数据库或外部 URL。接入现有全局限流并增加共享开发资料 IP 限流；通过服务器请求体限制/计数读取兼顾未知长度，不预先消费 SDK 请求流。补齐 `/mcp` 的生产 HTTPS 检查，保留正确的 SDK 协议错误响应。
- [x] 重跑上述测试并检查原有鉴权回归测试。任何中间件若影响 SDK 响应，仅为 `/mcp` 明确处理，不扩大后台匿名范围。

## Task 4: 系统集成页面及共享资料展示

**Files:**
- Create: `frontend/src/features/integrations/McpIntegrationPanel.tsx`
- Create: `frontend/src/features/integrations/developer-docs.ts`
- Modify: `frontend/src/features/integrations/IntegrationsPage.tsx`、`IntegrationGuide.tsx`、`integrations.css`
- Test: `frontend/e2e/developer-mcp.spec.ts`

**Interfaces:**
- Consumes: 任务 1 的匿名 HTTP DTO；前端 `api()` 已有 `/api` 前缀行为。
- Produces: `McpIntegrationPanel()`；“MCP 接入”页签；`developer-docs.ts` 中与后端一致的 TypeScript DTO。

- [x] 编写页面测试：页签可见、`${window.location.origin}/mcp` 展示正确、复制配置可解析且 URL 正确、示例提问可复制、共享资料加载失败可重试、刷新后版本一致。内容使用真实资料接口或有代表性的固定响应，剪贴板测试配置所需浏览器权限。
- [x] 运行 `npm --prefix frontend run test:e2e -- developer-mcp.spec.ts`，确认新增页签与资料行为失败。
- [x] 核实首版选定客户端的官方远程 MCP 配置格式，注明客户端及配置位置；将配置生成函数与复制按钮放在独立面板。页面展示免凭证、只读能力与提问示例。
- [x] 接入说明读取共享目录，使用分节纯文本/Markdown安全展示，保留代码块复制；不把 Markdown 当未过滤 HTML 渲染。沿用 React Query 加载、QueryError 重试与现有样式；资料页加载仅调用公开目录端点。
- [x] 运行 `npm --prefix frontend run build` 和同一 Playwright 命令，要求通过。

## Task 5: 本地与生产代理、部署文档及最终验收

**Files:**
- Modify: `frontend/vite.config.ts`、`deploy/nginx.conf`、`README.md`、`docs/api-contract.md`、`docs/verification.md`
- Create: `scripts/verify-developer-mcp.ps1`

**Interfaces:**
- Consumes: 完整 `/mcp` 端点与前端页签；Vite 路由无重写代理至 `http://127.0.0.1:5080`。
- Produces: `verify-developer-mcp.ps1 -BaseUrl <origin>` 发起初始化/发现请求，验证协议响应、内容类型和五个工具；失败退出码非零。

- [x] 先通过站点 origin 运行探测脚本，验证代理尚未添加时失败（HTML、404 或不匹配协议）。
- [x] 增加 Vite `/mcp` 代理与 Nginx 精确 `/mcp` location，保留协议头、设置 HTTP/1.1、可信转发头、`proxy_buffering off` 和无缓存，禁止落入 SPA fallback；生产设置明确 AllowedHosts/AllowedOrigins。
- [x] 运行本地 origin 探测，并在可用的 Docker/Nginx 环境运行 `nginx -t` 及相同 HTTPS 探测；环境不可用时记录尚未实测的生产代理限制，不称已通过。
- [x] 更新 README 和 HTTP 契约，记录连接地址、匿名范围、部署白名单、限流及受支持客户端。verification.md 记录实际命令、结果和环境限制。
- [x] 最终运行 `dotnet test backend/KeyValidation.slnx`、任务 2 的示例验证、`npm --prefix frontend run build` 和新增 Playwright 测试。相关测试依赖真实 PostgreSQL，按现有 fixture 加载本机配置，输出不得包含凭证。
- [x] 核对全部 spec 验收项，检查目录公开内容不包含秘密或内部管理资料，汇报实际完成项及未验证条件。

## 执行交接

建议由主代理在当前会话逐项执行：五项任务共享同一资料目录和协议接口，顺序执行便于控制集成变化。若用户选择子代理执行，再按对应技能调度。实现前由用户审阅本计划并选择执行方式。

