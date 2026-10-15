# KeyGate 卡密授权系统

基于 **C# / ASP.NET Core 10 + React / TypeScript + PostgreSQL 17** 的前后端分离软件授权系统。管理后台为中文界面，软件客户端通过 HTTP API 完成激活与心跳验证。

面向实际使用人员的 [系统操作说明](docs/user-guide.md) 提供从创建应用、发卡和激活，到客户订单、售后、渠道与账号管理的逐步教程，并包含可直接提供给客户的自助服务说明、常见问题及新人练习。

全局卡种目录与应用引用已交付：统一维护卡种，应用按需引用并控制发行；完整规则共享，历史授权与订单保留快照。见[设计](docs/superpowers/specs/2026-10-09-shared-plan-catalog-design.md)和[验收记录](docs/verification.md#全局卡种目录与应用引用)。

## 功能

- 多应用、卡种管理；批量生成卡密与一次性 CSV 导出。
- 首次激活开始计时；可配置设备上限；重复激活不延长卡密有效期。
- 默认每 5 秒心跳，180 秒租约，会话绝对有效期最多 8 小时。
- 禁用、恢复、解绑和会话撤销；恢复不会延长有效期或恢复旧会话。
- 标准多角色 RBAC：自定义角色与权限并集，应用范围和渠道归属独立约束；Cookie 身份验证与 CSRF 防护。
- 登录日志、操作日志分开，显示操作者、对象、结果和可读变更；个人资料和秘密不进入变更快照。
- 手风琴分组菜单、可收缩侧边栏；浏览器风格标签页即时切换，保留筛选、分页、表单和滚动位置，关闭后释放；登录与退出使用短过渡，并支持减少动态效果设置。
- 后台账号管理：创建、角色调整、停用/恢复、密码重置，变更即时使旧会话失效。
- 批次筛选与使用统计；卡密备注、标签；单张/批量续期及禁用、恢复、元数据编辑。
- 3 / 7 / 30 天到期提醒；批量操作结果可查询，断线重试不会重复续期。
- 账号安全中心：自行改密、首次改密要求、TOTP 登录验证和一次性恢复码；登录验证在本次会话内持续有效，后续操作无需重复验证码；关闭双重验证及重置恢复码需确认当前密码。
- 设备换绑策略：次数、滚动窗口与冷却时间；支持客户端释放自身设备、售后重置与历史追踪。
- 冻结/解冻授权：暂停剩余可用时间、撤销会话、独立历史；冻结卡不可续期。
- 卡种独立配置设备与在线会话上限，以及从发行起计算的激活期限；并发激活不超额。
- 应用级试用授权：按设备限制领取，重复请求不延长时间，使用正式卡自动升级。
- 授权功能分级：发卡时保存功能快照，激活与心跳返回权限，C# 示例结合有效租约检查能力。
- 高级组合查询与 UTC 运营统计；异常激活、换绑和可信地区变化记录可人工复核。
- 站内通知、到期提醒与签名 Webhook；持久化事件、失败重试及去重。
- 加密数据库备份、隔离恢复验证、服务指标和故障恢复记录。
- 客户、人工订单与交付；渠道额度、独立售价和不可变额度流水；客户自助查询及售后申请。
- 可轮换的开放 API 凭证、独立权限与应用/渠道范围；后台内置接入说明。
- 客户端最低版本与升级提示；启用强制策略后，激活和心跳即时执行版本检查。
- ES256 离线授权签发、下载、撤销和公钥轮换；C# 客户端本地验签、设备/功能检查与时钟回拨防护。
- 中文 Windows 授权客户端：卡密登录后自动验证，后台禁用、冻结、到期或撤销会话时显示原因并结束登录。
- 卡密 HMAC 摘要存储；会话令牌只保存哈希；真实 PostgreSQL 并发测试。

初始六项扩展的规则见 [管理扩展规格](docs/superpowers/specs/2026-10-05-management-expansion-design.md)。后续第一批 F01/F04/F12、第二批 F02/F05/F06/F07/F08 及[剩余计划](docs/superpowers/plans/2026-10-08-roadmap-completion.md)中的 13 项均已交付并通过最终审查；追加的账号、日志、导航、工作区标签和登录过渡一并验收。详细结果见[验证记录](docs/verification.md)。F18 支付按用户要求暂缓，订单采用人工履约。

## 目录

后端从哪里读、各项目的职责及“激活卡密”的调用流程，见 [后端代码阅读指南](docs/backend-navigation.md)。

```text
backend/      API、应用服务、领域规则、数据库迁移与测试
frontend/     React 管理后台与 Playwright 测试
samples/      C# 协议示例、Windows 授权客户端及客户端测试
deploy/       Docker 镜像、Compose 与 Nginx 配置
scripts/      本地开发启动脚本
docs/         设计、接口契约、实施计划与验证记录
```

## 本地启动

需要 .NET SDK 10、Node.js 22.12+ 和 PostgreSQL 17。数据库用户需能创建业务表；运行集成测试时还需创建数据库权限。

当前工作区已准备本机开发环境：前端 `http://127.0.0.1:5173`、API `http://127.0.0.1:5080`，独立 PostgreSQL 位于 `127.0.0.1:55432`。随机生成的本机账号记录在被忽略的 `.local/dev-secrets.json` 中，使用 `adminUser` 和 `adminPassword` 登录；`.env` 已保存对应的开发配置。本机数据库含自动化验证生成的测试记录。这些配置与测试数据不是生产默认账号。

重启本机后，可先运行 `node .local/provision-postgres.mjs` 启动本次工作区的数据库，再运行 `./scripts/start-dev.ps1`；`.local` 辅助文件不属于交付源码，迁移到其他机器时按照下面的标准安装步骤配置 PostgreSQL。

1. 创建一个空数据库 `key_validation`。在 PowerShell 中配置环境变量：

```powershell
$env:ConnectionStrings__Database = 'Host=127.0.0.1;Port=5432;Database=key_validation;Username=keyvalidation;Password=<数据库密码>'
$env:Licensing__CurrentKeyVersion = 'v1'
# 首次生成后保存此密钥。后续启动务必使用同一值。
$bytes = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($bytes)
$generator.Dispose()
$env:Licensing__Keys__v1 = [Convert]::ToBase64String($bytes)
$env:DataProtection__Path = "$PWD/.local/data-protection"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
```

可将配置保存在被 Git 忽略的 `.env` 中，格式为 `变量名=值`。开发启动脚本会读取它；单独执行 `dotnet` 命令时需要先将变量加载到当前进程环境。不要将密钥、数据库密码或生成的卡密提交到版本控制。

2. 安装依赖，迁移数据库并创建首个管理员：

```powershell
dotnet restore backend/KeyValidation.slnx
dotnet run --project backend/src/KeyValidation.Api -- migrate
dotnet run --project backend/src/KeyValidation.Api -- admin create owner Admin
# 上一步会隐藏输入密码；没有默认管理员或默认密码。
npm --prefix frontend ci
```

安装时会自动应用标签页弹窗焦点的兼容修正，详情见 [前端兼容说明](docs/frontend-compatibility.md)。

3. 启动前后端：

```powershell
./scripts/start-dev.ps1
```

打开 **http://127.0.0.1:5173**。API 默认地址 **http://127.0.0.1:5080**，OpenAPI 位于 `/openapi/v1.json`。开发代理将前端 `/api` 请求转发到后端。脚本按 Ctrl+C 只终止本次启动的进程。

也可在两个终端分别启动：

```powershell
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
dotnet run --no-launch-profile --project backend/src/KeyValidation.Api
```

```powershell
npm --prefix frontend run dev
```

## 使用流程

1. 登录后，在“应用管理”创建应用；公开应用标识用于客户端请求。
2. 在“卡种管理”创建卡种，设置时长、设备与在线上限、激活期限和功能；在“应用管理 → 应用设置 → 引用卡种”选择本应用使用的卡种。
3. 在“卡密管理”生成一批卡密，并立即保存 CSV。关闭结果页后无法再次获取明文。
4. 软件客户端调用激活接口，保存返回的临时会话令牌并定时心跳。
5. 在卡密详情查看设备与会话，需要时执行禁用或解绑。

卡种在多个应用间完整共享。修改规则影响所有引用应用后续的直接发卡和新建订单；已发卡密、已创建订单保留原参数。暂停单个应用引用只停止该应用的新业务，全局停发停止全部引用应用的新业务。卡密详情可填写原因冻结/解冻；冻结暂停计时，禁用状态仍单独生效。“应用设置”还可配置独立试用规则，默认关闭、时长 1 小时。

共享卡种升级包含两次迁移：`20261009100000_AddApplicationPlanReferences` 回填原应用引用及渠道价格主键，`20261009101000_GlobalizePlans` 移除单应用归属。部署前备份数据库并暂停旧服务写入，API 和前端一起升级。旧卡种 ID、价格及授权/订单快照保留，同名记录不自动合并。零引用或多引用卡种不能降级为单应用结构，需使用升级前备份或向前修复。

批量发卡响应丢失时，使用同一个请求标识查询已经提交的批次。由于服务端不能恢复明文，应禁用该批次，再用新请求标识生成。客户端不应在网络失败后盲目重复创建新批次。

## Windows 授权客户端

运行 [KeyValidation.Desktop.exe](samples/KeyValidation.Desktop/artifacts/client-v1-standalone/KeyValidation.Desktop.exe)，或在仓库根目录执行：

```powershell
& './samples/KeyValidation.Desktop/artifacts/client-v1-standalone/KeyValidation.Desktop.exe'
```

发布包包含 Windows x64 运行时，保留整个目录即可。程序已配置本机 API 和后台「KeyGate 授权客户端」应用（`keygate-desktop-client`），输入对应卡密登录。登录后显示授权期限、最近验证、验证有效期和设备信息，并根据后台状态自动结束失效登录。卡密和令牌不自动保存。

第一次使用见 [客户端使用说明](docs/desktop-quickstart.md)。交付目录中的 `测试卡密.txt` 提供两张 30 天卡和两张 3 分钟卡。可在后台筛选该应用，操作禁用、冻结、续期和撤销会话，观察客户端响应。配置和构建方法见 [客户端程序说明](samples/KeyValidation.Desktop/README.md)。

旧发布目录和 `.local/desktop-demo/` 保留此前高级授权测试台的验收资料；本次正常使用入口为上述 `client-v1-standalone` 发布。

## C# 客户端示例

```powershell
$env:KEYVALIDATION_API_URL = 'http://127.0.0.1:5080'
$env:KEYVALIDATION_APP_CODE = '<应用标识>'
$env:KEYVALIDATION_LICENSE_KEY = '<卡密>'
$env:KEYVALIDATION_DEVICE_ID = '<在本机生成并持久化的设备标识>'
dotnet run --project samples/KeyValidation.Client
```

远程地址必须使用 HTTPS。示例激活后发送心跳，并在明确拒绝或租约到期时退出；它使用单调时钟，不依赖本地系统时间计算剩余租约。接入真实软件时，每次执行受保护功能前检查当前授权状态，退出进程并不能替代业务代码中的检查。

试用模式设置 `KEYVALIDATION_TRIAL=true`，此时无需卡密。每应用/设备只能领取一次，重试沿用原试用期限；曾使用正式卡的设备不能再领取试用。将试用模式设为 `false` 并使用正式卡、相同设备标识激活，即可关联升级并撤销原试用会话。

可选 `KEYVALIDATION_REQUIRED_FEATURE=export` 会要求授权包含 `export`；卡种需先配置对应功能。软件应在受保护操作前调用 `capabilities.Allows("export")`，它同时检查功能快照和当前租约。旧服务缺失功能字段时不会自动授予细分权限。

可选 `KEYVALIDATION_CLIENT_VERSION=2.1.0` 将客户端版本同时用于激活和心跳。离线模式使用后台下载的许可证和独立配置的受信公钥，不发起网络请求；完整参数、轮换步骤及验证边界见 [C# 客户端说明](samples/KeyValidation.Client/README.md)。

离线许可证默认 24 小时，最多 168 小时且不超过卡密到期时间；客户端完全离线时，无法即时知道后台撤销，最长延迟为许可证剩余有效期。设备标识不是不可伪造的硬件证明，本地校验也不保证抵抗程序修改或完整磁盘快照回滚。支付按用户要求暂缓，订单采用人工履约。

## 测试

```powershell
# 指向用于测试的 PostgreSQL 实例；测试会创建并删除独立的随机名称数据库。
$env:KEYVALIDATION_TEST_DATABASE = '<PostgreSQL 连接字符串，用户须具有 CREATEDB 权限>'
dotnet test backend/KeyValidation.slnx
dotnet test samples/KeyValidation.Client.Tests
dotnet test samples/KeyValidation.Desktop.Tests
npm --prefix frontend run build
```

端到端测试要求 API 已启动、数据库已迁移且管理员已创建：

```powershell
$env:E2E_ADMIN_USER = '<测试管理员用户名>'
# 通过当前进程环境设置 E2E_ADMIN_PASSWORD，避免把真实密码写入脚本或命令历史。
cd frontend
npx playwright install chromium
npm run test:e2e
```

Playwright 可自行启动 Vite。真实 API 测试会创建带唯一后缀的测试应用及卡密；请使用专用开发数据库。未配置数据库或管理员时测试会明确失败，不会跳过后显示通过。

生成前端接口类型：后端运行后执行 `npm --prefix frontend run api:types`。接口字段见 [HTTP 契约](docs/api-contract.md)，最新执行结果见 [验证记录](docs/verification.md)。

## Docker 部署

需要 Docker Compose 和有效 TLS 证书。复制 `.env.example` 为 `.env`，填写数据库密码、至少 32 字节随机值的 Base64 授权密钥、证书及私钥绝对路径。Compose 不对宿主机开放数据库或 API 端口。

```sh
docker compose --env-file .env -f deploy/compose.yaml build
docker compose --env-file .env -f deploy/compose.yaml up -d database
docker compose --env-file .env -f deploy/compose.yaml run --rm api migrate
docker compose --env-file .env -f deploy/compose.yaml run --rm api admin create owner Admin
docker compose --env-file .env -f deploy/compose.yaml up -d
```

管理站点通过 HTTPS 访问，HTTP 重定向至 HTTPS。API 仅信任 Compose 中前端的固定内部地址传来的代理头。若 `172.30.88.0/24` 与既有网络冲突，需同步修改 Compose 子网、前端地址和 `Proxy__KnownProxies__0`。生产环境探针可从内部网络请求 `/health/live`、`/health/ready`。

生产更新先备份，再单独执行迁移命令；不要让多个 API 实例同时迁移。PostgreSQL 数据和 Data Protection 密钥分别保存在持久卷中。HMAC 授权密钥必须保留并安全备份；轮换时添加新版本并切换 `Licensing__CurrentKeyVersion`，旧版本在对应卡密退役前不能删除。

授权策略迁移会回填旧卡的在线上限并保留原有行为。已签发试用后，旧数据库结构无法表示其空批次/空卡密摘要，不能直接执行保留全部数据的向下迁移；回退需使用更新前的备份。

默认审计保留 180 天，授权事件和历史会话保留 30 天。可通过 `Retention__AuditDays`、`Retention__EventDays`、`Retention__SessionDays` 调整。API 限流在单实例内生效，多实例部署需在可信网关增加共享限流。

后台账号运维：

```sh
dotnet run --project backend/src/KeyValidation.Api -- admin create operator Operator
dotnet run --project backend/src/KeyValidation.Api -- admin create observer ReadOnly
dotnet run --project backend/src/KeyValidation.Api -- admin reset-password owner
```

自动化运维可通过 `KEYVALIDATION_ADMIN_PASSWORD` 传入一次性密码；不要把密码放在命令参数中。


## Developer MCP

The integration page exposes an anonymous, read-only documentation MCP at the site origin plus `/mcp` (development: `http://127.0.0.1:5173/mcp`; direct API: `http://127.0.0.1:5080/mcp`). It exposes five documentation tools and nine `keygate://docs/{id}` resources, never live applications, customers, license keys or orders. Fixed C#, Python and Node.js templates cover online activation, heartbeat, feature checks and logout. Offline documentation references the existing C# verifier.

The integration page provides VS Code workspace `.vscode/mcp.json` configuration (`{"servers":{"keygate":{"type":"http","url":"<origin>/mcp"}}}`) and the Claude Code command `claude mcp add --transport http --scope project keygate "<origin>/mcp"`. These client configuration formats were checked against their official documentation. Other clients must support Streamable HTTP; their individual configuration formats have not been verified. No API credential is needed for MCP. Merchant and administrator APIs retain their existing authorization.

Vite proxies MCP without rewriting the path, preserving Host, Origin and protocol headers. The default backend is `http://127.0.0.1:5080`; `API_PROXY_TARGET` can select an isolated backend for both public HTTP docs and MCP; `MCP_PROXY_TARGET` independently overrides MCP when needed. The production Nginx configuration routes `/mcp` and trailing-slash/case variants before the SPA fallback, uses HTTP/1.1, disables buffering/cache, and replaces forwarded client/protocol headers. Only the fixed frontend container address is trusted by the API.

Before deployment set `MCP_ALLOWED_HOST` to the exact public hostname and `MCP_ALLOWED_ORIGIN` to its exact HTTPS origin (including any non-default port) in `.env`. `deploy/compose.yaml` maps these to all default host entries and the origin entry so loopback defaults are replaced. Missing Origin is permitted for native clients; a supplied Origin must match. For local browser requests set `DeveloperMcp__AllowedOrigins__0=http://127.0.0.1:5173` when starting the API (the bundled default is `http://localhost:5173`). Do not use a wildcard origin.

MCP and public developer-doc endpoints default to 60 requests per IP per minute, also subject to the global limit; HTTP 429 carries `Retry-After`. MCP bodies are limited to 64 KiB, including streaming bodies. Search query accepts 1-200 characters, limit 1-10 (default 5), errorCode 1-100, and examples accept only csharp/python/javascript with online-license.

Run `powershell -NoProfile -File scripts/verify-developer-mcp.ps1 -BaseUrl <origin>` and repeat with `-TrailingSlash`. The probe validates JSON/SSE JSON-RPC, legacy initialization (2025-11-25), current discovery (2026-07-28), the exact five tool names, nine public resources and same-origin HTTP/MCP document Markdown/version parity. HTML, HTTP or protocol failures exit nonzero without printing response bodies. Before production release run Nginx configuration validation and the same probe against the actual HTTPS site; local proxy verification does not establish production TLS behavior.
